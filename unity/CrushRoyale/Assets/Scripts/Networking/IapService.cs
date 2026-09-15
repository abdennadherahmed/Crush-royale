using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CrushRoyale.Client;
using CrushRoyale.Contracts;
using UnityEngine;
#if CRUSH_IAP
using Newtonsoft.Json.Linq;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Extension;
#endif

namespace CrushRoyale.Game.Networking
{
    /// <summary>
    /// Google Play billing through Unity IAP. Flow: server precheck (age gate, minor cap) -> store purchase ->
    /// server receipt validation and grant -> ConfirmPendingPurchase (consumes packs so they can be bought again).
    /// Purchases interrupted by a crash are re-delivered by Unity IAP at the next launch and granted exactly once.
    /// </summary>
    public sealed class IapService
#if CRUSH_IAP
        : IDetailedStoreListener
#endif
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
        private IStoreController _controller;
        private TaskCompletionSource<IapValidateResponse> _pending;

        public void Initialize(IEnumerable<string> skus, string consumablePrefix)
        {
            if (!_config.IapEnabled || IsReady)
            {
                return;
            }
            ConfigurationBuilder builder = ConfigurationBuilder.Instance(StandardPurchasingModule.Instance());
            foreach (string sku in skus)
            {
                bool consumable = sku.StartsWith(consumablePrefix, StringComparison.Ordinal) || sku.EndsWith("battlepass", StringComparison.Ordinal);
                builder.AddProduct(sku, consumable ? ProductType.Consumable : ProductType.NonConsumable);
            }
            UnityPurchasing.Initialize(this, builder);
        }

        public string LocalizedPrice(string sku)
        {
            Product product = _controller?.products.WithID(sku);
            return product != null && product.availableToPurchase ? product.metadata.localizedPriceString : null;
        }

        public async Task<IapValidateResponse> BuyAsync(string sku)
        {
            if (!IsReady || _controller == null)
            {
                throw new CrushApiException("StoreUnavailable", 0, "Store not ready.");
            }
            IapPrecheckResponse precheck = await _backend.Client.Api.PrecheckPurchaseAsync(sku);
            if (!precheck.Allowed)
            {
                throw new CrushApiException(precheck.Error ?? "PermissionDenied", 403, precheck.Error);
            }

            _pending = new TaskCompletionSource<IapValidateResponse>();
            _controller.InitiatePurchase(sku);
            return await _pending.Task;
        }

        public void OnInitialized(IStoreController controller, IExtensionProvider extensions)
        {
            _controller = controller;
            IsReady = true;
        }

        public void OnInitializeFailed(InitializationFailureReason error) => Debug.LogWarning("IAP init failed: " + error);

        public void OnInitializeFailed(InitializationFailureReason error, string message) => Debug.LogWarning("IAP init failed: " + error + " " + message);

        public PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs purchaseEvent)
        {
            Product product = purchaseEvent.purchasedProduct;
            _ = ValidateAsync(product);
            return PurchaseProcessingResult.Pending;
        }

        public void OnPurchaseFailed(Product product, PurchaseFailureReason failureReason) =>
            _pending?.TrySetException(new CrushApiException("PurchaseFailed", 0, failureReason.ToString()));

        public void OnPurchaseFailed(Product product, PurchaseFailureDescription failureDescription) =>
            _pending?.TrySetException(new CrushApiException(failureDescription.reason == PurchaseFailureReason.UserCancelled ? "Cancelled" : "PurchaseFailed", 0, failureDescription.message));

        private async Task ValidateAsync(Product product)
        {
            try
            {
                if (!_backend.IsOnline)
                {
                    // Left pending: Unity IAP re-delivers it after the next successful login.
                    _pending?.TrySetException(new CrushApiException("Network", 0, "Offline: the purchase will be delivered at next launch.", true));
                    return;
                }

                string token = ExtractPurchaseToken(product.receipt, out string orderId);
                IapValidateResponse response = await _backend.Client.Api.ValidatePurchaseAsync(new IapValidateRequest
                {
                    Sku = product.definition.id,
                    PurchaseToken = token,
                    OrderId = orderId
                });

                _controller.ConfirmPendingPurchase(product);
                _backend.ApplyWallet(response.Wallet);
                _backend.ApplyInventory(response.Inventory);
                MainThread.Post(() => PurchaseGranted?.Invoke(response));
                _pending?.TrySetResult(response);
            }
            catch (CrushApiException ex)
            {
                if (!ex.IsRetryable)
                {
                    // Invalid receipt: never grant, release the transaction.
                    _controller.ConfirmPendingPurchase(product);
                }
                _pending?.TrySetException(ex);
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
        public void Initialize(IEnumerable<string> skus, string consumablePrefix)
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
        public AdsService(ClientConfig config)
        {
            if (!config.AdsEnabled)
            {
                Provider = new NoAdsProvider();
            }
            else
            {
                Provider = Application.isEditor || Debug.isDebugBuild ? new SimulatedAdsProvider() : (IAdsProvider)new NoAdsProvider();
            }
        }

        /// <summary>Swap in a real SDK adapter at startup.</summary>
        public IAdsProvider Provider { get; set; }

        public Task ShowInterstitialIfRequested(bool serverRequested) =>
            serverRequested && Provider.IsInterstitialReady ? Provider.ShowInterstitialAsync() : Task.CompletedTask;
    }
}
