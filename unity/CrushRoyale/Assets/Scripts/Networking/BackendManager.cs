using System;
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

        public GameClient Client { get; }

        public bool IsConfigured => Client != null;

        /// <summary>True after a successful server login.</summary>
        public bool IsOnline { get; private set; }

        public CrushApiException LastError { get; private set; }

        public ProfileDto Profile => IsOnline ? Client.Profile : null;

        public string PlayerId => Client?.Auth.UserId ?? "offline-player";

        public bool IsGuest => Client == null || Client.Auth.IsAnonymous;

        public GameBalance Balance => IsOnline ? Client.Balance : _offlineBalance;

        public StageCatalog Catalog => IsOnline ? Client.Catalog : _offlineCatalog;

        /// <summary>Signs in (guest by default) and logs into the game server. Returns false when offline.</summary>
        public async Task<bool> StartAsync(string language)
        {
            if (Client == null)
            {
                IsOnline = false;
                return false;
            }
            try
            {
                await Client.StartAsync(_save.DeviceHash(_config.DeviceSalt), RegionCode(), language);
                IsOnline = true;
                LastError = null;
                return true;
            }
            catch (CrushApiException ex)
            {
                Debug.LogWarning("Login failed: " + ex.Code + " " + ex.Message);
                LastError = ex;
                IsOnline = false;
                return false;
            }
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
