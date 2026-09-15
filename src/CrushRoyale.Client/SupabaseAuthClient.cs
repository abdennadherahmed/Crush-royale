using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CrushRoyale.Client
{
    public sealed class SupabaseUser
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("email")]
        public string Email { get; set; }

        [JsonProperty("is_anonymous")]
        public bool IsAnonymous { get; set; }
    }

    /// <summary>GoTrue session (snake_case on the wire).</summary>
    public sealed class SupabaseSession
    {
        [JsonProperty("access_token")]
        public string AccessToken { get; set; }

        [JsonProperty("refresh_token")]
        public string RefreshToken { get; set; }

        [JsonProperty("token_type")]
        public string TokenType { get; set; }

        [JsonProperty("expires_in")]
        public long ExpiresIn { get; set; }

        /// <summary>Unix seconds.</summary>
        [JsonProperty("expires_at")]
        public long ExpiresAt { get; set; }

        [JsonProperty("user")]
        public SupabaseUser User { get; set; }
    }

    /// <summary>Where the session survives app restarts.</summary>
    public interface ISessionStore
    {
        SupabaseSession Load();

        void Save(SupabaseSession session);

        void Clear();
    }

    public sealed class InMemorySessionStore : ISessionStore
    {
        private SupabaseSession _session;

        public SupabaseSession Load() => _session;

        public void Save(SupabaseSession session) => _session = session;

        public void Clear() => _session = null;
    }

    /// <summary>
    /// JSON file store with an optional protector (Unity: wrap with Android Keystore encryption).
    /// The refresh token grants account access: never store it unprotected on shared storage.
    /// </summary>
    public sealed class FileSessionStore : ISessionStore
    {
        private readonly string _path;
        private readonly Func<byte[], byte[]> _protect;
        private readonly Func<byte[], byte[]> _unprotect;

        public FileSessionStore(string path, Func<byte[], byte[]> protect = null, Func<byte[], byte[]> unprotect = null)
        {
            _path = path ?? throw new ArgumentNullException(nameof(path));
            _protect = protect ?? (b => b);
            _unprotect = unprotect ?? (b => b);
        }

        public SupabaseSession Load()
        {
            try
            {
                if (!File.Exists(_path))
                {
                    return null;
                }
                string json = Encoding.UTF8.GetString(_unprotect(File.ReadAllBytes(_path)));
                return JsonConvert.DeserializeObject<SupabaseSession>(json);
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is System.Security.Cryptography.CryptographicException)
            {
                return null;
            }
        }

        public void Save(SupabaseSession session)
        {
            string directory = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            string temp = _path + ".tmp";
            File.WriteAllBytes(temp, _protect(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(session))));
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
            File.Move(temp, _path);
        }

        public void Clear()
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
    }

    /// <summary>
    /// Supabase Auth (GoTrue REST) for the game: anonymous guest accounts, Google sign-in with an ID token
    /// (Android Credential Manager), email/password, identity linking (guest to Google), token refresh.
    /// </summary>
    public sealed class SupabaseAuthClient
    {
        private const int RefreshMarginSeconds = 60;

        private readonly ClientOptions _options;
        private readonly HttpClient _http;
        private readonly ISessionStore _store;
        private readonly Func<DateTime> _utcNow;
        private readonly SemaphoreSlim _refreshGate = new SemaphoreSlim(1, 1);

        public SupabaseAuthClient(ClientOptions options, HttpClient http, ISessionStore store, Func<DateTime> utcNow = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
            Session = store.Load();
        }

        public event Action<SupabaseSession> SessionChanged;

        public SupabaseSession Session { get; private set; }

        public bool IsSignedIn => Session?.RefreshToken != null;

        public string UserId => Session?.User?.Id;

        public bool IsAnonymous => Session?.User?.IsAnonymous ?? false;

        private string AuthUrl => _options.SupabaseUrl.TrimEnd('/') + "/auth/v1";

        public Task<SupabaseSession> SignInAnonymouslyAsync(CancellationToken cancellationToken = default) =>
            SessionRequestAsync(AuthUrl + "/signup", new JObject { ["data"] = new JObject() }, null, cancellationToken);

        /// <summary>Google (or Apple) ID token obtained natively on the device. Pass the raw nonce if one was hashed into the request.</summary>
        public Task<SupabaseSession> SignInWithIdTokenAsync(string provider, string idToken, string nonce = null, CancellationToken cancellationToken = default) =>
            SessionRequestAsync(AuthUrl + "/token?grant_type=id_token", IdTokenBody(provider, idToken, nonce), null, cancellationToken);

        /// <summary>Upgrades the current (guest) account by linking an ID-token identity, keeping all progress.</summary>
        public async Task<SupabaseSession> LinkIdTokenAsync(string provider, string idToken, string nonce = null, CancellationToken cancellationToken = default)
        {
            string token = await GetAccessTokenAsync(cancellationToken).ConfigureAwait(false) ?? throw new CrushApiException("AuthFailed", 401, "Not signed in.");
            JObject body = IdTokenBody(provider, idToken, nonce);
            body["link_identity"] = true;
            return await SessionRequestAsync(AuthUrl + "/token?grant_type=id_token", body, token, cancellationToken).ConfigureAwait(false);
        }

        public Task<SupabaseSession> SignInWithPasswordAsync(string email, string password, CancellationToken cancellationToken = default) =>
            SessionRequestAsync(AuthUrl + "/token?grant_type=password", new JObject { ["email"] = email, ["password"] = password }, null, cancellationToken);

        /// <summary>Email sign-up. Returns null when the project requires email confirmation first.</summary>
        public async Task<SupabaseSession> SignUpAsync(string email, string password, CancellationToken cancellationToken = default)
        {
            JObject response = await PostAsync(AuthUrl + "/signup", new JObject { ["email"] = email, ["password"] = password }, null, cancellationToken).ConfigureAwait(false);
            if (response["access_token"] == null)
            {
                return null;
            }
            return Store(response.ToObject<SupabaseSession>());
        }

        /// <summary>Returns a valid access token, refreshing it shortly before expiry (single flight).</summary>
        public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
        {
            SupabaseSession session = Session;
            if (session == null)
            {
                return null;
            }
            if (!IsExpiring(session))
            {
                return session.AccessToken;
            }
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
            return Session?.AccessToken;
        }

        public async Task<bool> RefreshAsync(CancellationToken cancellationToken = default)
        {
            await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                SupabaseSession current = Session;
                if (current?.RefreshToken == null)
                {
                    return false;
                }
                if (!IsExpiring(current) && current.AccessToken != null && current.ExpiresAt - Unix(_utcNow()) > RefreshMarginSeconds * 5)
                {
                    return true;
                }

                try
                {
                    await SessionRequestAsync(AuthUrl + "/token?grant_type=refresh_token", new JObject { ["refresh_token"] = current.RefreshToken }, null, cancellationToken).ConfigureAwait(false);
                    return true;
                }
                catch (CrushApiException ex) when (!ex.IsNetwork && ex.StatusCode >= 400 && ex.StatusCode < 500)
                {
                    // Refresh token revoked or already used: the player must sign in again.
                    SignOutLocally();
                    return false;
                }
            }
            finally
            {
                _refreshGate.Release();
            }
        }

        public async Task SignOutAsync(CancellationToken cancellationToken = default)
        {
            string token = Session?.AccessToken;
            if (token != null)
            {
                try
                {
                    await PostAsync(AuthUrl + "/logout", new JObject(), token, cancellationToken).ConfigureAwait(false);
                }
                catch (CrushApiException)
                {
                    // Local sign-out still happens.
                }
            }
            SignOutLocally();
        }

        private void SignOutLocally()
        {
            Session = null;
            _store.Clear();
            SessionChanged?.Invoke(null);
        }

        private bool IsExpiring(SupabaseSession session) => session.AccessToken == null || session.ExpiresAt - Unix(_utcNow()) <= RefreshMarginSeconds;

        private static JObject IdTokenBody(string provider, string idToken, string nonce)
        {
            if (string.IsNullOrWhiteSpace(provider) || string.IsNullOrWhiteSpace(idToken))
            {
                throw new ArgumentException("provider and idToken are required.");
            }
            var body = new JObject { ["provider"] = provider, ["id_token"] = idToken };
            if (!string.IsNullOrEmpty(nonce))
            {
                body["nonce"] = nonce;
            }
            return body;
        }

        private async Task<SupabaseSession> SessionRequestAsync(string url, JObject body, string bearer, CancellationToken cancellationToken)
        {
            JObject response = await PostAsync(url, body, bearer, cancellationToken).ConfigureAwait(false);
            var session = response.ToObject<SupabaseSession>();
            if (session?.AccessToken == null || session.RefreshToken == null)
            {
                throw new CrushApiException("AuthFailed", 200, "Auth server returned no session.");
            }
            return Store(session);
        }

        private SupabaseSession Store(SupabaseSession session)
        {
            if (session.ExpiresAt == 0 && session.ExpiresIn > 0)
            {
                session.ExpiresAt = Unix(_utcNow()) + session.ExpiresIn;
            }
            Session = session;
            _store.Save(session);
            SessionChanged?.Invoke(session);
            return session;
        }

        private async Task<JObject> PostAsync(string url, JObject body, string bearer, CancellationToken cancellationToken)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Post, url))
            {
                request.Headers.Add("apikey", _options.SupabasePublishableKey);
                if (bearer != null)
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer);
                }
                request.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");

                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
                    HttpResponseMessage response;
                    try
                    {
                        response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
                    }
                    catch (HttpRequestException ex)
                    {
                        throw new CrushApiException("Network", 0, ex.Message, isNetwork: true);
                    }
                    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                    {
                        throw new CrushApiException("Timeout", 0, "Auth server timeout.", isNetwork: true);
                    }

                    using (response)
                    {
                        string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!response.IsSuccessStatusCode)
                        {
                            string message = text;
                            try
                            {
                                JObject error = JObject.Parse(text);
                                message = (string)(error["msg"] ?? error["error_description"] ?? error["message"] ?? error["error"]) ?? text;
                            }
                            catch (JsonException)
                            {
                                // Non-JSON error body.
                            }
                            throw new CrushApiException("AuthFailed", (int)response.StatusCode, message);
                        }
                        return string.IsNullOrWhiteSpace(text) ? new JObject() : JObject.Parse(text);
                    }
                }
            }
        }

        private static long Unix(DateTime utc) => (long)(utc - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
    }
}
