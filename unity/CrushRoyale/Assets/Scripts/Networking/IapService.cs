using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CrushRoyale.Client;
using CrushRoyale.Contracts;
using UnityEngine;
#if CRUSH_IAP
using Newtonsoft.Json.Linq;
using UnityEngine.Purchasing;
#endif

namespace CrushRoyale.Game.Networking
{
    /// <summary>
    /// Google Play billing through Unity IAP. Flow: server precheck (age gate, minor cap) -> store purchase ->
    /// server receipt validation and grant -> ConfirmPurchase (consumes packs so they can be bought again).
    /// Purchases interrupted by a crash are re-delivered by Unity IAP at the next launch and granted exactly once,
    /// because only a confirmed order is consumed and the server refuses to grant the same token twice.
    /// </summary>
    public sealed class IapService
    {
        private readonly BackendManager _backend;
        private readonly ClientConfig _config;

        public IapService(BackendManager backend, ClientConfig config)
        {
            _backend = backend;
            _config = config;
        }

        public event Action<IapValidateResponse> PurchaseGranted;

        public bool IsReady { get; private set; }

#if CRUSH_IAP
        private StoreController _store;
        private TaskCompletionSource<IapValidateResponse> _pending;

        /// <param name="durableSkus">
        /// The products bought once and kept. Everything else is consumable, so it can be bought again.
        /// </param>
        public void Initialize(IEnumerable<string> skus, IEnumerable<string> durableSkus)
        {
            if (!_config.IapEnabled || _store != null)
            {
                return;
            }
            _ = ConnectAsync(skus, durableSkus);
        }

        private async Task ConnectAsync(IEnumerable<string> skus, IEnumerable<string> durableSkus)
        {
            var durable = new HashSet<string>(durableSkus ?? Array.Empty<string>(), StringComparer.Ordinal);
            List<ProductDefinition> products = skus
                .Select(sku => new ProductDefinition(sku, durable.Contains(sku) ? ProductType.NonConsumable : ProductType.Consumable))
                .ToList();

            try
            {
                _store = UnityIAPServices.StoreController();
                // Subscribed before connecting: a purchase the player made last session, that the server never
                // granted, arrives as soon as the connection opens and would be missed otherwise.
                _store.OnPurchasePending += OnPurchasePending;
                _store.OnPurchaseFailed += OnPurchaseFailed;
                _store.OnPurchaseDeferred += OnPurchaseDeferred;
                _store.OnProductsFetched += OnProductsFetched;
                _store.OnProductsFetchFailed += failure => Debug.LogWarning("IAP products unavailable: " + failure.FailureReason);
                _store.OnStoreDisconnected += reason =>
                {
                    IsReady = false;
                    Debug.LogWarning("IAP store disconnected: " + reason.message);
                };

                await _store.Connect();
                _store.FetchProducts(products);
            }
            catch (Exception ex)
            {
                // No store on this device, or Play Services missing: the game stays playable, the shop does not.
                Debug.LogWarning("IAP init failed: " + ex.Message);
                _store = null;
                IsReady = false;
            }
        }

        private void OnProductsFetched(List<Product> products)
        {
            IsReady = products.Count > 0;
            // Only once the catalogue is in: fetching purchases first would hand back orders for products the
            // controller cannot name yet.
            _store.FetchPurchases();
        }

        public string LocalizedPrice(string sku)
        {
            Product product = _store?.GetProductById(sku);
            return product != null && product.availableToPurchase ? product.metadata.localizedPriceString : null;
        }

        /// <summary>How long the store gets to say anything at all before the player is let go.</summary>
        private const int PurchaseTimeoutSeconds = 90;

        public async Task<IapValidateResponse> BuyAsync(string sku)
        {
            if (!IsReady || _store == null)
            {
                throw new CrushApiException("StoreUnavailable", 0, "Store not ready.");
            }
            if (_pending != null && !_pending.Task.IsCompleted)
            {
                // A second tap while the Google sheet is open would strand the first waiter.
                throw new CrushApiException("PurchaseFailed", 0, "A purchase is already in progress.");
            }
            IapPrecheckResponse precheck = await _backend.Client.Api.PrecheckPurchaseAsync(sku);
            if (!precheck.Allowed)
            {
                throw new CrushApiException(precheck.Error ?? "PermissionDenied", 403, precheck.Error);
            }

            TaskCompletionSource<IapValidateResponse> waiter = new TaskCompletionSource<IapValidateResponse>();
            _pending = waiter;
            _store.PurchaseProduct(sku);

            // A store that answers nothing is the worst case: the player would sit on a frozen screen for ever.
            Task finished = await Task.WhenAny(waiter.Task, Task.Delay(TimeSpan.FromSeconds(PurchaseTimeoutSeconds)));
            if (finished != waiter.Task)
            {
                Fail(waiter, new CrushApiException("PurchaseFailed", 0, "The store did not answer.", true));
            }
            return await waiter.Task;
        }

        private void Fail(TaskCompletionSource<IapValidateResponse> waiter, CrushApiException error)
        {
            if (ReferenceEquals(waiter, _pending))
            {
                _pending = null;
            }
            waiter?.TrySetException(error);
        }

        /// <summary>
        /// A purchase the store has taken money for. It stays pending, and the player keeps their claim on it,
        /// until the server has checked the receipt and granted the contents.
        /// </summary>
        private void OnPurchasePending(PendingOrder order) => _ = ValidateAsync(order);

        private void OnPurchaseFailed(FailedOrder order)
        {
            bool cancelled = order.FailureReason == PurchaseFailureReason.UserCancelled;
            Fail(_pending, new CrushApiException(cancelled ? "Cancelled" : "PurchaseFailed", 0, order.Details));
        }

        /// <summary>
        /// Waiting on someone else, a parent approving the purchase for instance. The money is not taken yet and
        /// nothing is granted, so the player is told and released rather than left on a frozen screen.
        /// </summary>
        private void OnPurchaseDeferred(DeferredOrder order) =>
            Fail(_pending, new CrushApiException("PurchaseDeferred", 0, "Waiting for approval.", true));

        private async Task ValidateAsync(PendingOrder order)
        {
            string sku = order.CartOrdered.Items().FirstOrDefault()?.Product?.definition.id;
            try
            {
                if (!_backend.IsOnline)
                {
                    // Left pending: Unity IAP re-delivers it after the next successful login.
                    Fail(_pending, new CrushApiException("Network", 0, "Offline: the purchase will be delivered at next launch.", true));
                    return;
                }

                string token = ExtractPurchaseToken(order.Info.Receipt, out string orderId);
                IapValidateResponse response = await _backend.Client.Api.ValidatePurchaseAsync(new IapValidateRequest
                {
                    Sku = sku,
                    PurchaseToken = token,
                    OrderId = orderId
                });

                _store.ConfirmPurchase(order);
                _backend.ApplyWallet(response.Wallet);
                _backend.ApplyInventory(response.Inventory);
                MainThread.Post(() => PurchaseGranted?.Invoke(response));
                TaskCompletionSource<IapValidateResponse> waiter = _pending;
                _pending = null;
                waiter?.TrySetResult(response);
            }
            catch (CrushApiException ex)
            {
                if (!ex.IsRetryable)
                {
                    // Invalid receipt: never grant, release the transaction.
                    _store.ConfirmPurchase(order);
                }
                Fail(_pending, ex);
            }
            catch (Exception ex)
            {
                // A receipt that will not parse, a null the store did not promise: whatever it is, the player
                // must not be left waiting on it. The order stays pending so the purchase is re-delivered.
                Debug.LogError("IAP validation crashed for " + sku + ": " + ex);
                Fail(_pending, new CrushApiException("PurchaseFailed", 0, ex.Message, true));
            }
        }

        /// <summary>Unity IAP Google Play receipt: {"Payload": "{\"json\": \"{... purchaseToken ...}\"}"}.</summary>
        private static string ExtractPurchaseToken(string unifiedReceipt, out string orderId)
        {
            JObject receipt = JObject.Parse(unifiedReceipt);
            JObject payload = JObject.Parse((string)receipt["Payload"]);
            JObject json = JObject.Parse((string)payload["json"]);
            orderId = (string)json["orderId"];
            return (string)json["purchaseToken"];
        }
#else
        public void Initialize(IEnumerable<string> skus, IEnumerable<string> durableSkus)
        {
            Debug.Log("Unity IAP package not installed: purchases disabled.");
        }

        public string LocalizedPrice(string sku) => null;

        public Task<IapValidateResponse> BuyAsync(string sku) =>
            Task.FromException<IapValidateResponse>(new CrushApiException("StoreUnavailable", 0, "In-app purchases are not available in this build."));
#endif
    }

    /// <summary>Implemented by an ad network SDK adapter (LevelPlay, AdMob...). Rewards are granted server-side.</summary>
    public interface IAdsProvider
    {
        bool IsRewardedReady { get; }

        bool IsInterstitialReady { get; }

        /// <summary>Returns the ad network verification token, or null if the player skipped.</summary>
        Task<string> ShowRewardedAsync(string placement);

        Task ShowInterstitialAsync();
    }

    /// <summary>Builds with no ad SDK: ads never show, rewarded placements are hidden in the UI.</summary>
    public sealed class NoAdsProvider : IAdsProvider
    {
        public bool IsRewardedReady => false;

        public bool IsInterstitialReady => false;

        public Task<string> ShowRewardedAsync(string placement) => Task.FromResult<string>(null);

        public Task ShowInterstitialAsync() => Task.CompletedTask;
    }

    /// <summary>Editor/dev builds: simulates a completed rewarded ad and a skipped interstitial.</summary>
    public sealed class SimulatedAdsProvider : IAdsProvider
    {
        public bool IsRewardedReady => true;

        public bool IsInterstitialReady => true;

        public async Task<string> ShowRewardedAsync(string placement)
        {
            await Task.Delay(500);
            return "simulated";
        }

        public Task ShowInterstitialAsync() => Task.CompletedTask;
    }

    /// <summary>Ad orchestration: interstitials only when the server says so (frequency caps live server-side).</summary>
    public sealed class AdsService
    {
        private readonly ClientConfig _config;
        private bool _started;

        public AdsService(ClientConfig config, bool personalisedConsent = false)
        {
            _config = config;
            Personalised = personalisedConsent;
            // Nothing until Start is called: the ad unit ids come from the server, so there is nothing to
            // initialise at boot and no ad is offered before the config has landed.
            Provider = !config.AdsEnabled ? new NoAdsProvider()
                : Application.isEditor ? new SimulatedAdsProvider()
                : (IAdsProvider)new NoAdsProvider();
        }

        /// <summary>Whether the player allowed ads chosen from what the networks know about them.</summary>
        public bool Personalised { get; private set; }

        /// <summary>
        /// Brings the SDK up once the ad unit ids are known, which is after the first /config download. Calling it
        /// again does nothing: the SDK initialises once per process and a second Init would drop the loaded ads.
        /// The ids are taken from the server when it supplies them and from client.json otherwise, so a build can
        /// still be tested with nothing deployed.
        /// </summary>
        public void Start(string rewardedUnitId, string interstitialUnitId, bool personalised)
        {
            Personalised = personalised;
            if (_started || !_config.AdsEnabled || Application.isEditor)
            {
                return;
            }
            string rewarded = string.IsNullOrWhiteSpace(rewardedUnitId) ? _config.LevelPlayRewardedUnitId : rewardedUnitId;
            string interstitial = string.IsNullOrWhiteSpace(interstitialUnitId) ? _config.LevelPlayInterstitialUnitId : interstitialUnitId;
            if (string.IsNullOrWhiteSpace(_config.LevelPlayAppKey) || string.IsNullOrWhiteSpace(rewarded))
            {
                Debug.LogWarning("Ads are enabled but no LevelPlay app key or rewarded ad unit is configured.");
                return;
            }
            _started = true;
            Provider = new LevelPlayAdsProvider(_config.LevelPlayAppKey, rewarded, interstitial, personalised);
        }

        /// <summary>Swap in a real SDK adapter at startup.</summary>
        public IAdsProvider Provider { get; set; }

        public Task ShowInterstitialIfRequested(bool serverRequested) =>
            serverRequested && Provider.IsInterstitialReady ? Provider.ShowInterstitialAsync() : Task.CompletedTask;
    }
}
