using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CrushRoyale.Client;
using CrushRoyale.Contracts;
using Newtonsoft.Json;
using UnityEngine;

namespace CrushRoyale.Game.Networking
{
    /// <summary>
    /// Home-made analytics and crash reporting: events and exceptions are buffered (and saved while offline), then sent
    /// to the game server every minute and when the app goes to background. No third-party SDK, no personal data.
    /// </summary>
    public sealed class Telemetry
    {
        private const int MaxBuffered = 400;
        private const int MaxErrors = 20;
        private const float FlushIntervalSeconds = 60f;

        private readonly BackendManager _backend;
        private readonly string _path;
        private readonly object _lock = new object();
        private readonly Dictionary<string, ClientErrorDto> _errors = new Dictionary<string, ClientErrorDto>();
        private List<TelemetryEventDto> _events = new List<TelemetryEventDto>();
        private float _nextFlushAt;
        private bool _sending;

        public Telemetry(BackendManager backend)
        {
            _backend = backend;
            _path = Path.Combine(Application.persistentDataPath, "telemetry.json");
            SessionId = Guid.NewGuid().ToString("N");
            Load();
            Application.logMessageReceivedThreaded += OnLog;
            _nextFlushAt = Time.realtimeSinceStartup + 15f;
        }

        public string SessionId { get; }

        /// <summary>Records an event, e.g. Track("stage_end", ("stage", "12"), ("result", "Won")).</summary>
        public void Track(string name, params (string Key, object Value)[] props)
        {
            var e = new TelemetryEventDto { Name = name, AtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
            foreach ((string key, object value) in props)
            {
                e.Props[key] = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
            }
            lock (_lock)
            {
                _events.Add(e);
                if (_events.Count > MaxBuffered)
                {
                    _events.RemoveRange(0, _events.Count - MaxBuffered);
                }
            }
        }

        /// <summary>Call every frame: sends the buffer once a minute when online.</summary>
        public void Update()
        {
            if (Time.realtimeSinceStartup >= _nextFlushAt)
            {
                _nextFlushAt = Time.realtimeSinceStartup + FlushIntervalSeconds;
                _ = FlushAsync();
            }
        }

        /// <summary>App going to background: send now, and keep a copy on disk in case it never comes back.</summary>
        public void OnPause()
        {
            Save();
            _ = FlushAsync();
        }

        public async Task FlushAsync()
        {
            if (_sending || !_backend.IsOnline)
            {
                return;
            }
            TelemetryRequest request;
            lock (_lock)
            {
                if (_events.Count == 0 && _errors.Count == 0)
                {
                    return;
                }
                request = new TelemetryRequest
                {
                    SessionId = SessionId,
                    AppVersion = Application.version,
                    Device = SystemInfo.deviceModel,
                    Os = SystemInfo.operatingSystem,
                    Events = new List<TelemetryEventDto>(_events),
                    Errors = new List<ClientErrorDto>(_errors.Values)
                };
            }

            _sending = true;
            try
            {
                await _backend.Client.Api.SendTelemetryAsync(request);
                lock (_lock)
                {
                    _events.RemoveRange(0, Math.Min(request.Events.Count, _events.Count));
                    _errors.Clear();
                }
                Save();
            }
            catch (CrushApiException)
            {
                // Offline or server busy: retried at the next flush.
            }
            finally
            {
                _sending = false;
            }
        }

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Exception && type != LogType.Error && type != LogType.Assert)
            {
                return;
            }
            // Network noise is already handled by the game (offline mode); only real bugs are reported.
            if (condition != null && (condition.StartsWith("Login failed", StringComparison.Ordinal) || condition.Contains("Telemetry")))
            {
                return;
            }
            string message = Scrub((condition ?? string.Empty).Length > 2000 ? condition.Substring(0, 2000) : condition ?? string.Empty);
            stackTrace = Scrub(stackTrace);
            string key = message.Length > 160 ? message.Substring(0, 160) : message;
            lock (_lock)
            {
                if (_errors.TryGetValue(key, out ClientErrorDto existing))
                {
                    existing.Count++;
                }
                else if (_errors.Count < MaxErrors)
                {
                    _errors[key] = new ClientErrorDto { Message = type + ": " + message, Stack = stackTrace ?? string.Empty, Count = 1 };
                }
            }
        }

        private static readonly System.Text.RegularExpressions.Regex Secrets = new System.Text.RegularExpressions.Regex(
            @"eyJ[\w-]+\.[\w-]+\.[\w-]+|Bearer\s+\S+|sb_(publishable|secret)_\w+|[\w.+-]+@[\w-]+\.[\w.]+|(purchaseToken|refresh_token|access_token|apikey)[=:""\s]+[^\s&""]+",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        /// <summary>Security audit S-07: tokens, keys and e-mails never leave the device in error reports.</summary>
        private static string Scrub(string text) => string.IsNullOrEmpty(text) ? text ?? string.Empty : Secrets.Replace(text, "[redacted]");

        private void Load()
        {
            try
            {
                if (File.Exists(_path))
                {
                    _events = JsonConvert.DeserializeObject<List<TelemetryEventDto>>(File.ReadAllText(_path)) ?? new List<TelemetryEventDto>();
                }
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException)
            {
                _events = new List<TelemetryEventDto>();
            }
        }

        private void Save()
        {
            try
            {
                string json;
                lock (_lock)
                {
                    json = JsonConvert.SerializeObject(_events);
                }
                File.WriteAllText(_path, json);
            }
            catch (IOException)
            {
                // Best effort only.
            }
        }
    }
}
