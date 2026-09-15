using System;
using System.Globalization;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CrushRoyale.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CrushRoyale.Client
{
    public enum RealtimeStatus
    {
        Disconnected,
        Connecting,
        Joined,
        Reconnecting
    }

    /// <summary>
    /// Guild chat over Supabase Realtime (Phoenix channel protocol, Postgres Changes on public.guild_messages).
    /// Messages are written through the API (moderation) and read here; RLS limits delivery to guild members.
    /// Reconnects with backoff and pushes refreshed access tokens to the channel.
    /// </summary>
    public sealed class RealtimeChatClient : IDisposable
    {
        private const string Topic = "realtime:guild-chat";
        private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(25);

        /// <summary>Keep timestamps as strings: Json.NET would otherwise convert them to local time.</summary>
        private static readonly JsonSerializerSettings FrameSettings = new JsonSerializerSettings { DateParseHandling = DateParseHandling.None };

        private readonly ClientOptions _options;
        private readonly Func<CancellationToken, Task<string>> _accessToken;
        private readonly SemaphoreSlim _sendGate = new SemaphoreSlim(1, 1);
        private CancellationTokenSource _lifetime;
        private ClientWebSocket _socket;
        private int _ref;
        private string _lastToken;

        public RealtimeChatClient(ClientOptions options, Func<CancellationToken, Task<string>> accessToken)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _accessToken = accessToken ?? throw new ArgumentNullException(nameof(accessToken));
        }

        public event Action<ChatMessageDto> MessageReceived;

        public event Action<RealtimeStatus> StatusChanged;

        public RealtimeStatus Status { get; private set; } = RealtimeStatus.Disconnected;

        public long GuildId { get; private set; }

        /// <summary>Starts (or restarts) the background connection for a guild.</summary>
        public void Connect(long guildId)
        {
            Disconnect();
            GuildId = guildId;
            _lifetime = new CancellationTokenSource();
            _ = RunAsync(_lifetime.Token);
        }

        public void Disconnect()
        {
            _lifetime?.Cancel();
            _lifetime?.Dispose();
            _lifetime = null;
            try
            {
                _socket?.Abort();
            }
            catch (ObjectDisposedException)
            {
            }
            _socket?.Dispose();
            _socket = null;
            SetStatus(RealtimeStatus.Disconnected);
        }

        public void Dispose() => Disconnect();

        /// <summary>Maps a Postgres Changes payload to a chat message (null if not a guild message insert).</summary>
        public static ChatMessageDto ParseChange(JObject frame)
        {
            if ((string)frame["event"] != "postgres_changes")
            {
                return null;
            }
            JToken record = frame["payload"]?["data"]?["record"];
            if (record == null || (string)frame["payload"]?["data"]?["table"] != "guild_messages")
            {
                return null;
            }

            long createdMs = 0;
            JToken createdToken = record["created_at"];
            if (createdToken is JValue createdValue && createdValue.Value is DateTimeOffset offset)
            {
                createdMs = offset.ToUnixTimeMilliseconds();
            }
            else if (createdToken is JValue dateValue && dateValue.Value is DateTime date)
            {
                // Json.NET already converted the ISO string; convert back to UTC without losing milliseconds.
                createdMs = new DateTimeOffset(date.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(date, DateTimeKind.Utc) : date.ToUniversalTime()).ToUnixTimeMilliseconds();
            }
            else if (createdToken != null && DateTimeOffset.TryParse((string)createdToken, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed))
            {
                createdMs = parsed.ToUnixTimeMilliseconds();
            }

            return new ChatMessageDto
            {
                Id = (long?)record["id"] ?? 0,
                PlayerId = (string)record["player_id"],
                DisplayName = (string)record["display_name"],
                Body = (string)record["body"],
                Masked = (bool?)record["masked"] ?? false,
                CreatedAtUnixMs = createdMs
            };
        }

        private async Task RunAsync(CancellationToken ct)
        {
            int failures = 0;
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    SetStatus(failures == 0 ? RealtimeStatus.Connecting : RealtimeStatus.Reconnecting);
                    await SessionAsync(ct).ConfigureAwait(false);
                    failures = 0;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex) when (ex is WebSocketException || ex is IOException || ex is JsonException || ex is InvalidOperationException || ex is CrushApiException)
                {
                    failures++;
                }

                if (ct.IsCancellationRequested)
                {
                    break;
                }
                SetStatus(RealtimeStatus.Reconnecting);
                double seconds = Math.Min(30, Math.Pow(2, Math.Min(failures, 5)));
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(seconds), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
            SetStatus(RealtimeStatus.Disconnected);
        }

        private async Task SessionAsync(CancellationToken ct)
        {
            string wsBase = _options.SupabaseUrl.TrimEnd('/').Replace("https://", "wss://").Replace("http://", "ws://");
            var uri = new Uri(wsBase + "/realtime/v1/websocket?apikey=" + Uri.EscapeDataString(_options.SupabasePublishableKey) + "&vsn=1.0.0");

            using (var socket = new ClientWebSocket())
            {
                _socket = socket;
                await socket.ConnectAsync(uri, ct).ConfigureAwait(false);

                _lastToken = await _accessToken(ct).ConfigureAwait(false) ?? throw new CrushApiException("AuthFailed", 401, "Not signed in.");
                string joinRef = NextRef();
                await SendAsync(new JObject
                {
                    ["topic"] = Topic,
                    ["event"] = "phx_join",
                    ["ref"] = joinRef,
                    ["join_ref"] = joinRef,
                    ["payload"] = new JObject
                    {
                        ["config"] = new JObject
                        {
                            ["broadcast"] = new JObject { ["self"] = false },
                            ["presence"] = new JObject { ["key"] = string.Empty },
                            ["postgres_changes"] = new JArray
                            {
                                new JObject { ["event"] = "INSERT", ["schema"] = "public", ["table"] = "guild_messages", ["filter"] = "guild_id=eq." + GuildId.ToString(CultureInfo.InvariantCulture) }
                            }
                        },
                        ["access_token"] = _lastToken
                    }
                }, ct).ConfigureAwait(false);

                using (var heartbeatStop = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    Task heartbeat = HeartbeatLoopAsync(heartbeatStop.Token);
                    try
                    {
                        await ReceiveLoopAsync(socket, ct).ConfigureAwait(false);
                    }
                    finally
                    {
                        heartbeatStop.Cancel();
                        try
                        {
                            await heartbeat.ConfigureAwait(false);
                        }
                        catch (OperationCanceledException)
                        {
                        }
                        catch (WebSocketException)
                        {
                        }
                    }
                }
            }
            _socket = null;
        }

        private async Task ReceiveLoopAsync(ClientWebSocket socket, CancellationToken ct)
        {
            var buffer = new byte[8192];
            using (var message = new MemoryStream())
            {
                while (socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
                {
                    WebSocketReceiveResult result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        throw new WebSocketException("Server closed the connection.");
                    }
                    message.Write(buffer, 0, result.Count);
                    if (!result.EndOfMessage)
                    {
                        continue;
                    }

                    string text = Encoding.UTF8.GetString(message.ToArray());
                    message.SetLength(0);
                    JObject frame = JsonConvert.DeserializeObject<JObject>(text, FrameSettings);
                    string evt = (string)frame["event"];

                    if (evt == "phx_reply" && (string)frame["topic"] == Topic)
                    {
                        string status = (string)frame["payload"]?["status"];
                        if (status == "ok")
                        {
                            SetStatus(RealtimeStatus.Joined);
                        }
                        else if (status == "error")
                        {
                            throw new InvalidOperationException("Realtime join refused: " + frame["payload"]);
                        }
                    }
                    else if (evt == "phx_error" || evt == "phx_close")
                    {
                        throw new InvalidOperationException("Realtime channel closed.");
                    }
                    else
                    {
                        ChatMessageDto chat = ParseChange(frame);
                        if (chat != null)
                        {
                            MessageReceived?.Invoke(chat);
                        }
                    }
                }
            }
        }

        private async Task HeartbeatLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(HeartbeatInterval, ct).ConfigureAwait(false);
                await SendAsync(new JObject { ["topic"] = "phoenix", ["event"] = "heartbeat", ["payload"] = new JObject(), ["ref"] = NextRef() }, ct).ConfigureAwait(false);

                string token = await _accessToken(ct).ConfigureAwait(false);
                if (token != null && token != _lastToken)
                {
                    _lastToken = token;
                    await SendAsync(new JObject { ["topic"] = Topic, ["event"] = "access_token", ["payload"] = new JObject { ["access_token"] = token }, ["ref"] = NextRef() }, ct).ConfigureAwait(false);
                }
            }
        }

        private async Task SendAsync(JObject frame, CancellationToken ct)
        {
            ClientWebSocket socket = _socket;
            if (socket == null || socket.State != WebSocketState.Open)
            {
                throw new WebSocketException("Socket not open.");
            }
            byte[] bytes = Encoding.UTF8.GetBytes(frame.ToString(Formatting.None));
            await _sendGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct).ConfigureAwait(false);
            }
            finally
            {
                _sendGate.Release();
            }
        }

        private string NextRef() => Interlocked.Increment(ref _ref).ToString(CultureInfo.InvariantCulture);

        private void SetStatus(RealtimeStatus status)
        {
            if (Status == status)
            {
                return;
            }
            Status = status;
            StatusChanged?.Invoke(status);
        }
    }
}
