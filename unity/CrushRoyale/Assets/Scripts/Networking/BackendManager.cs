using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using CrushRoyale.Client;
using CrushRoyale.Contracts;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Story;
using UnityEngine;

namespace CrushRoyale.Game.Networking
{
    /// <summary>
    /// Task 17 (Supabase replaces Firebase, see docs/DESIGN_DECISIONS.md): sign-in, profile sync, offline queue and
    /// realtime chat, exposed to the UI on the main thread. Without a configured backend the game runs in offline
    /// practice mode (local rules, no rewards) so gameplay can be tested immediately.
    /// </summary>
    public sealed class BackendManager : IDisposable
    {
        private readonly ClientConfig _config;
        private readonly LocalSave _save;
        private readonly GameBalance _offlineBalance = GameBalance.CreateDefault();
        private readonly StageCatalog _offlineCatalog;
        private Task<bool> _login;
        private bool _gaveUp;

        public BackendManager(ClientConfig config, LocalSave save)
        {
            _config = config;
            _save = save;
            _offlineCatalog = new StageCatalog(_offlineBalance);

            if (!config.IsConfigured)
            {
                Debug.LogWarning("Crush Royale backend not configured (Resources/Config/client.json): offline practice mode.");
                return;
            }

            try
            {
                Client = new GameClient(new ClientOptions
                {
                    ApiBaseUrl = config.ApiBaseUrl,
                    SupabaseUrl = config.SupabaseUrl,
                    SupabasePublishableKey = config.SupabasePublishableKey
                }, save.Sessions, save.Queue, save);

                Client.ProfileChanged += profile => MainThread.Post(() => ProfileChanged?.Invoke(profile));
                Client.Chat.MessageReceived += message => MainThread.Post(() => ChatMessageReceived?.Invoke(message));
                Client.Queue.Delivered += (request, json) => MainThread.Post(() => QueuedRequestDelivered?.Invoke(request, json));
            }
            catch (ArgumentException ex)
            {
                Debug.LogError("Invalid client configuration: " + ex.Message);
                Client = null;
            }
        }

        public event Action<ProfileDto> ProfileChanged;

        public event Action<ChatMessageDto> ChatMessageReceived;

        public event Action<QueuedRequest, string> QueuedRequestDelivered;

        /// <summary>A login that outlived the splash finally succeeded: the game can switch from practice to online.</summary>
        public event Action CameOnline;

        public GameClient Client { get; }

        public bool IsConfigured => Client != null;

        /// <summary>True after a successful server login.</summary>
        public bool IsOnline { get; private set; }

        public CrushApiException LastError { get; private set; }

        /// <summary>
        /// Profile served while offline, set only by the editor screenshot tool and the UI tests: without a server the
        /// screens would all render their empty state, so nothing would ever exercise the populated layouts.
        /// Always null in a player build.
        /// </summary>
        internal ProfileDto OfflineProfile { get; set; }

        /// <summary>
        /// A shop catalogue to show when there is no server, set by the screenshot harness.
        ///
        /// The shop asks the server for its items, so every capture ever taken showed the word "Loading" and nothing
        /// else. Two reported defects in the shop grid were invisible to me for that reason alone. The game itself
        /// never sets this, so a real player offline still sees the offline notice.
        /// </summary>
        internal ShopResponse OfflineShop { get; set; }

        public ProfileDto Profile => IsOnline ? Client.Profile : OfflineProfile;

        public string PlayerId => Client?.Auth.UserId ?? "offline-player";

        public bool IsGuest => Client == null || Client.Auth.IsAnonymous;

        public GameBalance Balance => IsOnline ? Client.Balance : _offlineBalance;

        public StageCatalog Catalog => IsOnline ? Client.Catalog : _offlineCatalog;

        /// <summary>True while a login is still running (possibly in the background after the splash gave up waiting).</summary>
        public bool IsConnecting => _login != null && !_login.IsCompleted;

        /// <summary>
        /// Signs in (guest by default) and logs into the game server. Returns false when offline, or when the login takes
        /// longer than <paramref name="budgetMs"/> or <paramref name="giveUp"/> completes first: a free Render instance
        /// can need 30-50 s to wake up. The login then keeps running and raises <see cref="CameOnline"/> if it succeeds.
        /// </summary>
        public async Task<bool> StartAsync(string language, int budgetMs = 8000, Task giveUp = null)
        {
            if (Client == null)
            {
                IsOnline = false;
                return false;
            }
            if (_login == null || _login.IsCompleted)
            {
                _gaveUp = false;
                _login = LoginAsync(language);
            }

            var waits = new List<Task> { _login, Task.Delay(Math.Max(0, budgetMs)) };
            if (giveUp != null)
            {
                waits.Add(giveUp);
            }
            await Task.WhenAny(waits);
            if (_login.IsCompleted)
            {
                return _login.Result;
            }
            _gaveUp = true;
            return false;
        }

        /// <summary>Background reconnection (resume, periodic retry while offline).</summary>
        public void TryReconnect()
        {
            if (Client != null && !IsOnline && !IsConnecting)
            {
                _ = StartAsync(GameRoot.Instance.Loc.Language, 0);
            }
        }

        private async Task<bool> LoginAsync(string language)
        {
            try
            {
                await Client.StartAsync(_save.DeviceHash(_config.DeviceSalt), RegionCode(), language);
                IsOnline = true;
                LastError = null;
            }
            catch (CrushApiException ex)
            {
                Debug.LogWarning("Login failed: " + ex.Code + " " + ex.Message);
                LastError = ex;
                IsOnline = false;
                return false;
            }
            catch (Exception ex)
            {
                // TLS / DNS / socket errors can surface as non-API exceptions on some Android devices: never hang on them.
                Debug.LogWarning("Login failed: " + ex.GetType().Name + " " + ex.Message);
                LastError = new CrushApiException("Network", 0, ex.Message, isNetwork: true);
                IsOnline = false;
                return false;
            }

            if (_gaveUp)
            {
                _gaveUp = false;
                MainThread.Post(() => CameOnline?.Invoke());
            }
            return true;
        }

        /// <summary>Upgrades a guest account to Google (progress kept) or signs in with Google.</summary>
        public async Task<bool> SignInWithGoogleAsync(GoogleSignIn google)
        {
            if (Client == null)
            {
                return false;
            }
            GoogleSignIn.Result result = await google.RequestIdTokenAsync(_config.GoogleWebClientId);
            if (!result.Success)
            {
                LastError = new CrushApiException("AuthFailed", 0, result.Error);
                return false;
            }
            try
            {
                if (Client.Auth.IsSignedIn && Client.Auth.IsAnonymous)
                {
                    await Client.Auth.LinkIdTokenAsync("google", result.IdToken, result.RawNonce);
                }
                else
                {
                    await Client.Auth.SignInWithIdTokenAsync("google", result.IdToken, result.RawNonce);
                }
                return await StartAsync(GameRoot.Instance.Loc.Language);
            }
            catch (CrushApiException ex)
            {
                LastError = ex;
                return false;
            }
        }

        public async Task SignOutAsync()
        {
            if (Client == null)
            {
                return;
            }
            Client.Chat.Disconnect();
            await Client.Auth.SignOutAsync();
            IsOnline = false;
        }

        /// <summary>Permanently deletes the account on the server (Google Play requirement), then signs out locally.</summary>
        public async Task<bool> DeleteAccountAsync()
        {
            if (Client == null || !IsOnline)
            {
                return false;
            }
            try
            {
                await Client.Api.DeleteMeAsync();
            }
            catch (CrushApiException ex)
            {
                LastError = ex;
                return false;
            }
            await SignOutAsync();
            return true;
        }

        public void ApplyWallet(WalletDto wallet)
        {
            if (Profile == null || wallet == null)
            {
                return;
            }
            Profile.Wallet = wallet;
            ProfileChanged?.Invoke(Profile);
        }

        public void ApplyLives(LivesDto lives)
        {
            if (Profile == null || lives == null)
            {
                return;
            }
            Profile.Lives = lives;
            ProfileChanged?.Invoke(Profile);
        }

        public void ApplyInventory(InventoryDto inventory)
        {
            if (Profile == null || inventory == null)
            {
                return;
            }
            Profile.Inventory = inventory;
            ProfileChanged?.Invoke(Profile);
        }

        public async Task RefreshProfileAsync()
        {
            if (!IsOnline)
            {
                return;
            }
            try
            {
                await Client.RefreshProfileAsync();
            }
            catch (CrushApiException ex)
            {
                LastError = ex;
            }
        }

        public void OnResume()
        {
            if (!IsOnline)
            {
                TryReconnect();
                return;
            }
            _ = FlushAndRefreshAsync();
        }

        public void Dispose()
        {
            Client?.Dispose();
        }

        private async Task FlushAndRefreshAsync()
        {
            try
            {
                await Client.Queue.FlushAsync(Client.Api.Transport);
            }
            catch (CrushApiException)
            {
                // Still offline.
            }
            await RefreshProfileAsync();
        }

        private static string RegionCode()
        {
            try
            {
                return RegionInfo.CurrentRegion.TwoLetterISORegionName;
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
        }
    }
}
