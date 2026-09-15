using System;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CrushRoyale.Contracts;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Replay;
using CrushRoyale.Core.Story;

namespace CrushRoyale.Client
{
    /// <summary>Cache for the downloaded balance (so the game starts offline with the last known rules).</summary>
    public interface IConfigCache
    {
        string Load();

        void Save(string configJson);
    }

    /// <summary>
    /// Facade used by the Unity layer: sign-in (guest by default), server login, rules download and verification,
    /// match submission with offline fallback, and guild chat.
    /// </summary>
    public sealed class GameClient : IDisposable
    {
        private readonly HttpClient _http;
        private readonly IConfigCache _configCache;

        public GameClient(ClientOptions options, ISessionStore sessions, IQueueStorage queueStorage, IConfigCache configCache = null, HttpMessageHandler handler = null)
        {
            Options = options ?? throw new ArgumentNullException(nameof(options));
            options.Validate();
            _http = handler == null ? new HttpClient() : new HttpClient(handler, disposeHandler: false);
            _http.Timeout = System.Threading.Timeout.InfiniteTimeSpan;
            _configCache = configCache;

            Auth = new SupabaseAuthClient(options, _http, sessions);
            Api = new CrushApi(new ApiClient(options, _http, Auth.GetAccessTokenAsync, Auth.RefreshAsync));
            Queue = new OfflineQueue(queueStorage ?? new InMemoryQueueStorage());
            Chat = new RealtimeChatClient(options, Auth.GetAccessTokenAsync);

            ApplyBalance(GameBalance.CreateDefault());
            TryLoadCachedConfig();
        }

        public ClientOptions Options { get; }

        public SupabaseAuthClient Auth { get; }

        public CrushApi Api { get; }

        public OfflineQueue Queue { get; }

        public RealtimeChatClient Chat { get; }

        public GameBalance Balance { get; private set; }

        public string BalanceHash { get; private set; }

        public StageCatalog Catalog { get; private set; }

        public ProfileDto Profile { get; private set; }

        public event Action<ProfileDto> ProfileChanged;

        /// <summary>Signs in (guest if needed), logs into the game server, syncs rules and flushes queued submissions.</summary>
        public async Task<LoginResponse> StartAsync(string deviceHash, string region, string language, CancellationToken cancellationToken = default)
        {
            if (!Auth.IsSignedIn)
            {
                await Auth.SignInAnonymouslyAsync(cancellationToken).ConfigureAwait(false);
            }

            LoginResponse login = await Api.LoginAsync(new LoginRequest
            {
                DeviceHash = deviceHash,
                Platform = "android",
                ClientVersion = typeof(GameClient).Assembly.GetName().Version?.ToString(),
                RulesVersion = GameBalance.RulesVersion,
                BalanceHash = BalanceHash,
                Region = region,
                Language = language
            }, cancellationToken).ConfigureAwait(false);

            if (login.ConfigOutdated)
            {
                await RefreshConfigAsync(cancellationToken).ConfigureAwait(false);
            }
            SetProfile(login.Profile);

            try
            {
                await Queue.FlushAsync(Api.Transport, cancellationToken).ConfigureAwait(false);
            }
            catch (CrushApiException)
            {
                // Still offline for some routes: retried on the next start or connectivity change.
            }
            return login;
        }

        /// <summary>Downloads the live balance and verifies the client reproduces the server hash exactly.</summary>
        public async Task RefreshConfigAsync(CancellationToken cancellationToken = default)
        {
            ConfigResponse config = await Api.GetConfigAsync(cancellationToken).ConfigureAwait(false);
            if (config.RulesVersion != GameBalance.RulesVersion)
            {
                throw new CrushApiException("VersionMismatch", 426, "A game update is required.");
            }

            GameBalance balance = ParseBalance(config.BalanceJson, config.BalanceHash);
            ApplyBalance(balance);
            _configCache?.Save(JsonSettings.Serialize(config));
        }

        public async Task<ProfileDto> RefreshProfileAsync(CancellationToken cancellationToken = default)
        {
            ProfileDto profile = await Api.GetMeAsync(cancellationToken).ConfigureAwait(false);
            SetProfile(profile);
            return profile;
        }

        /// <summary>Submits a story result; queued when the network is down (returns null in that case).</summary>
        public Task<StageCompleteResponse> SubmitStageAsync(string matchId, ReplayData replay, CancellationToken cancellationToken = default) =>
            SubmitOrQueueAsync("stage", ApiRoutes.Fill(ApiRoutes.StageComplete, "matchId", matchId), MatchConfigFactory.ToSubmission(matchId, replay),
                req => Api.CompleteStageAsync(req, cancellationToken));

        public Task<PvpResultDto> SubmitPvpAsync(string matchId, ReplayData replay, CancellationToken cancellationToken = default) =>
            SubmitOrQueueAsync("pvp", ApiRoutes.PvpRecord, MatchConfigFactory.ToSubmission(matchId, replay),
                req => Api.RecordPvpAsync(req, cancellationToken));

        public Task<GuildBossAttackResponse> SubmitGuildBossAsync(string matchId, ReplayData replay, CancellationToken cancellationToken = default) =>
            SubmitOrQueueAsync("boss", ApiRoutes.GuildBossDamage, MatchConfigFactory.ToSubmission(matchId, replay),
                req => Api.SubmitGuildBossAsync(req, cancellationToken));

        public static GameBalance ParseBalance(string json, string expectedHash)
        {
            GameBalance balance = JsonSettings.Deserialize<GameBalance>(json) ?? throw new CrushApiException("InvalidConfig", 0, "Empty balance.");
            balance.Validate();
            string hash = balance.ComputeHash().ToString("x16", CultureInfo.InvariantCulture);
            if (!string.Equals(hash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new CrushApiException("InvalidConfig", 0, "Balance hash mismatch (client " + hash + ", server " + expectedHash + ").");
            }
            return balance;
        }

        public void Dispose()
        {
            Chat.Dispose();
            _http.Dispose();
        }

        private async Task<T> SubmitOrQueueAsync<T>(string kind, string route, SubmitReplayRequest request, Func<SubmitReplayRequest, Task<T>> send) where T : class
        {
            try
            {
                return await send(request).ConfigureAwait(false);
            }
            catch (CrushApiException ex) when (ex.IsNetwork)
            {
                Queue.Enqueue(kind, HttpMethod.Post, route, request);
                return null;
            }
        }

        private void ApplyBalance(GameBalance balance)
        {
            Balance = balance;
            BalanceHash = balance.ComputeHash().ToString("x16", CultureInfo.InvariantCulture);
            Catalog = new StageCatalog(balance);
        }

        private void TryLoadCachedConfig()
        {
            string cached = _configCache?.Load();
            if (string.IsNullOrEmpty(cached))
            {
                return;
            }
            try
            {
                ConfigResponse config = JsonSettings.Deserialize<ConfigResponse>(cached);
                if (config?.RulesVersion == GameBalance.RulesVersion)
                {
                    ApplyBalance(ParseBalance(config.BalanceJson, config.BalanceHash));
                }
            }
            catch (Exception ex) when (ex is CrushApiException || ex is Newtonsoft.Json.JsonException || ex is InvalidOperationException)
            {
                // Corrupt cache: fall back to the built-in balance until the next download.
            }
        }

        private void SetProfile(ProfileDto profile)
        {
            Profile = profile;
            ProfileChanged?.Invoke(profile);
        }
    }
}
