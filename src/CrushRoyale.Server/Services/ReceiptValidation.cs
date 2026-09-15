using CrushRoyale.Server.Infrastructure;
using Google;
using Google.Apis.AndroidPublisher.v3;
using Google.Apis.AndroidPublisher.v3.Data;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;

namespace CrushRoyale.Server.Services;

public sealed record ReceiptValidationResult(bool Valid, string OrderId, bool TestPurchase, string? Error)
{
    public static ReceiptValidationResult Invalid(string error) => new(false, string.Empty, false, error);
}

/// <summary>Server-side store receipt validation (never trust the client's "purchase succeeded").</summary>
public interface IReceiptValidator
{
    Task<ReceiptValidationResult> ValidateAsync(string sku, string purchaseToken, CancellationToken cancellationToken);

    /// <summary>Consumes (orbe packs) or acknowledges (non-consumables) after the grant is committed. Unacknowledged purchases are refunded by Google after 3 days.</summary>
    Task AcknowledgeAsync(string sku, string purchaseToken, bool consumable, CancellationToken cancellationToken);
}

/// <summary>Google Play Developer API (purchases.products). Requires a service account linked in the Play Console.</summary>
public sealed class GooglePlayReceiptValidator : IReceiptValidator, IDisposable
{
    private readonly AndroidPublisherService _service;
    private readonly string _packageName;
    private readonly ILogger<GooglePlayReceiptValidator> _logger;

    public GooglePlayReceiptValidator(IapOptions options, ILogger<GooglePlayReceiptValidator> logger)
    {
        if (string.IsNullOrWhiteSpace(options.ServiceAccountJsonPath) || !File.Exists(options.ServiceAccountJsonPath))
        {
            throw new InvalidOperationException("Iap:ServiceAccountJsonPath must point to the Play Console service account JSON.");
        }

#pragma warning disable CS0618 // FromFile is flagged obsolete in recent Google.Apis.Auth; the file is our own trusted service account.
        GoogleCredential credential = GoogleCredential.FromFile(options.ServiceAccountJsonPath).CreateScoped(AndroidPublisherService.Scope.Androidpublisher);
#pragma warning restore CS0618

        _service = new AndroidPublisherService(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "CrushRoyaleServer"
        });
        _packageName = options.PackageName;
        _logger = logger;
    }

    public async Task<ReceiptValidationResult> ValidateAsync(string sku, string purchaseToken, CancellationToken cancellationToken)
    {
        try
        {
            ProductPurchase purchase = await _service.Purchases.Products.Get(_packageName, sku, purchaseToken).ExecuteAsync(cancellationToken).ConfigureAwait(false);
            if (purchase.PurchaseState != 0)
            {
                return ReceiptValidationResult.Invalid("Purchase state " + purchase.PurchaseState);
            }
            if (string.IsNullOrEmpty(purchase.OrderId))
            {
                return ReceiptValidationResult.Invalid("Missing order id.");
            }
            return new ReceiptValidationResult(true, purchase.OrderId, purchase.PurchaseType == 0, null);
        }
        catch (GoogleApiException ex)
        {
            _logger.LogWarning(ex, "Receipt validation failed for {Sku}", sku);
            return ReceiptValidationResult.Invalid("Store rejected the receipt.");
        }
    }

    public async Task AcknowledgeAsync(string sku, string purchaseToken, bool consumable, CancellationToken cancellationToken)
    {
        if (consumable)
        {
            await _service.Purchases.Products.Consume(_packageName, sku, purchaseToken).ExecuteAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _service.Purchases.Products.Acknowledge(new ProductPurchasesAcknowledgeRequest(), _packageName, sku, purchaseToken).ExecuteAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public void Dispose() => _service.Dispose();
}

/// <summary>Development/testing: tokens "fake:&lt;orderId&gt;" are valid. Registration is refused in Production.</summary>
public sealed class FakeReceiptValidator : IReceiptValidator
{
    public const string Prefix = "fake:";

    public Task<ReceiptValidationResult> ValidateAsync(string sku, string purchaseToken, CancellationToken cancellationToken)
    {
        if (purchaseToken == null || !purchaseToken.StartsWith(Prefix, StringComparison.Ordinal) || purchaseToken.Length <= Prefix.Length)
        {
            return Task.FromResult(ReceiptValidationResult.Invalid("Not a fake receipt."));
        }
        return Task.FromResult(new ReceiptValidationResult(true, purchaseToken[Prefix.Length..], true, null));
    }

    public Task AcknowledgeAsync(string sku, string purchaseToken, bool consumable, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>No store configured: every receipt is refused.</summary>
public sealed class DisabledReceiptValidator : IReceiptValidator
{
    public Task<ReceiptValidationResult> ValidateAsync(string sku, string purchaseToken, CancellationToken cancellationToken) =>
        Task.FromResult(ReceiptValidationResult.Invalid("In-app purchases are not configured on this server."));

    public Task AcknowledgeAsync(string sku, string purchaseToken, bool consumable, CancellationToken cancellationToken) => Task.CompletedTask;
}
