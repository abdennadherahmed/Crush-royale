using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace CrushRoyale.Client
{
    public sealed class QueuedRequest
    {
        public string Id { get; set; }

        /// <summary>Caller-defined category ("stage", "pvp", "boss") used to route results back to the UI.</summary>
        public string Kind { get; set; }

        public string Method { get; set; }

        public string Route { get; set; }

        public string BodyJson { get; set; }

        public long CreatedAtUnixMs { get; set; }

        public int Attempts { get; set; }
    }

    public interface IQueueStorage
    {
        List<QueuedRequest> Load();

        void Save(List<QueuedRequest> items);
    }

    public sealed class InMemoryQueueStorage : IQueueStorage
    {
        private List<QueuedRequest> _items = new List<QueuedRequest>();

        public List<QueuedRequest> Load() => new List<QueuedRequest>(_items);

        public void Save(List<QueuedRequest> items) => _items = new List<QueuedRequest>(items);
    }

    public sealed class FileQueueStorage : IQueueStorage
    {
        private readonly string _path;

        public FileQueueStorage(string path)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
        }

        public List<QueuedRequest> Load()
        {
            try
            {
                return File.Exists(_path)
                    ? JsonConvert.DeserializeObject<List<QueuedRequest>>(File.ReadAllText(_path)) ?? new List<QueuedRequest>()
                    : new List<QueuedRequest>();
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException)
            {
                return new List<QueuedRequest>();
            }
        }

        public void Save(List<QueuedRequest> items)
        {
            string directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            string temp = _path + ".tmp";
            File.WriteAllText(temp, JsonConvert.SerializeObject(items));
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
            File.Move(temp, _path);
        }
    }

    /// <summary>
    /// Persistent FIFO of requests made while offline (typically replay submissions: a finished match is never lost
    /// when the network drops). Flushed in order when the connection comes back; permanent 4xx failures are dropped
    /// and reported, transient failures stop the flush and keep the item.
    /// Note: the server expires unsubmitted matches after 20 minutes.
    /// </summary>
    public sealed class OfflineQueue
    {
        private readonly IQueueStorage _storage;
        private readonly int _maxItems;
        private readonly SemaphoreSlim _flushGate = new SemaphoreSlim(1, 1);
        private readonly object _lock = new object();
        private readonly List<QueuedRequest> _items;

        public OfflineQueue(IQueueStorage storage, int maxItems = 100)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _maxItems = Math.Max(1, maxItems);
            _items = storage.Load();
        }

        /// <summary>(request, response JSON) after a successful delivery.</summary>
        public event Action<QueuedRequest, string> Delivered;

        /// <summary>(request, error) when the server permanently refused it.</summary>
        public event Action<QueuedRequest, CrushApiException> Dropped;

        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _items.Count;
                }
            }
        }

        public QueuedRequest Enqueue(string kind, HttpMethod method, string route, object body)
        {
            var item = new QueuedRequest
            {
                Id = Guid.NewGuid().ToString("N"),
                Kind = kind,
                Method = method.Method,
                Route = route,
                BodyJson = body == null ? null : JsonSettings.Serialize(body),
                CreatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };
            lock (_lock)
            {
                if (_items.Count >= _maxItems)
                {
                    _items.RemoveAt(0);
                }
                _items.Add(item);
                _storage.Save(_items);
            }
            return item;
        }

        /// <summary>Delivers queued requests in order. Returns how many were delivered.</summary>
        public async Task<int> FlushAsync(ApiClient api, CancellationToken cancellationToken = default)
        {
            if (api == null)
            {
                throw new ArgumentNullException(nameof(api));
            }
            if (!await _flushGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            {
                return 0;
            }

            int delivered = 0;
            try
            {
                while (true)
                {
                    QueuedRequest next;
                    lock (_lock)
                    {
                        if (_items.Count == 0)
                        {
                            break;
                        }
                        next = _items[0];
                    }

                    try
                    {
                        string response = await api.SendRawAsync(new HttpMethod(next.Method), next.Route, next.BodyJson, cancellationToken).ConfigureAwait(false);
                        Remove(next);
                        delivered++;
                        Delivered?.Invoke(next, response);
                    }
                    catch (CrushApiException ex) when (ex.IsRetryable || ex.StatusCode == 401)
                    {
                        lock (_lock)
                        {
                            next.Attempts++;
                            _storage.Save(_items);
                        }
                        break;
                    }
                    catch (CrushApiException ex)
                    {
                        Remove(next);
                        Dropped?.Invoke(next, ex);
                    }
                }
            }
            finally
            {
                _flushGate.Release();
            }
            return delivered;
        }

        private void Remove(QueuedRequest item)
        {
            lock (_lock)
            {
                _items.Remove(item);
                _storage.Save(_items);
            }
        }
    }
}
