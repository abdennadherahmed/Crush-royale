using System.Security.Cryptography;
using System.Text;
using CrushRoyale.Contracts;
using CrushRoyale.Core.AntiCheat;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Economy;
using CrushRoyale.Core.Progression;
using CrushRoyale.Server.Infrastructure;
using CrushRoyale.Server.Persistence;

namespace CrushRoyale.Server.Services;

/// <summary>Lives, shop, cosmetics, rewarded ads and real-money purchases (receipt-validated, idempotent).</summary>
public sealed class EconomyService
{
    private readonly PlayerOperations _ops;
    private readonly IReceiptValidator _receipts;
    private readonly ILogger<EconomyService> _logger;

    public EconomyService(PlayerOperations ops, IReceiptValidator receipts, ILogger<EconomyService> logger)
    {
        _ops = ops;
        _receipts = receipts;
        _logger = logger;
    }

    public Task<PurchaseResponse> BuyLivesAsync(Guid userId, BuyLivesRequest request, CancellationToken ct) =>
        _ops.RunAsync(userId, ctx =>
        {
            PlayerWorkspace ws = ctx.Player;
            int count = request?.Count ?? 0;
            ws.Stamina.BuyLives(count, ws.Wallet).ThrowIfFailed();
            return new PurchaseResponse { Granted = new RewardDto { Lives = count }, Wallet = Mappers.Wallet(ws), Lives = Mappers.Lives(ws) };
        }, ct);

    public Task<PurchaseResponse> ClaimVipLifeAsync(Guid userId, CancellationToken ct) =>
        _ops.RunAsync(userId, ctx =>
        {
            PlayerWorkspace ws = ctx.Player;
            if (!ws.Stamina.ClaimVipDailyLife())
            {
                throw new ApiException(ErrorCode.AlreadyClaimed, "No VIP life available today.");
            }
            return new PurchaseResponse { Granted = new RewardDto { Lives = 1 }, Wallet = Mappers.Wallet(ws), Lives = Mappers.Lives(ws) };
        }, ct);

    public Task<VipGiftResponse> ClaimVipGiftAsync(Guid userId, CancellationToken ct) =>
        _ops.RunAsync(userId, ctx =>
        {
            PlayerWorkspace ws = ctx.Player;
            int tier = (int)ws.State.Vip.Tier;
            int today = TimeUtil.DayIndex(ws.Now);
            if (!VipGifts.HasGift(tier))
            {
                throw new ApiException(ErrorCode.PermissionDenied, "VIP gifts start at VIP " + VipGifts.FromTier + ".");
            }
            if (!VipGifts.CanClaim(tier, ws.State.VipGiftDay, today))
            {
                throw new ApiException(ErrorCode.AlreadyClaimed, "Today's VIP gift was already claimed.");
            }
            VipGift gift = VipGifts.For(tier, today);
            ws.State.VipGiftDay = today;
            string key = "vip-gift:" + today;
            ws.GrantReward(gift.Reward, TransactionReason.VipGift, key, key);
            ws.Pets.Get(gift.FragmentsPet).Fragments += gift.PetFragments;
            return new VipGiftResponse
            {
                Reward = Mappers.Reward(gift.Reward),
                PetFragments = gift.PetFragments,
                FragmentsPet = gift.FragmentsPet.ToString(),
                Wallet = Mappers.Wallet(ws),
                Inventory = Mappers.Inventory(ws),
                Pets = Mappers.Pets(ws),
                Lives = Mappers.Lives(ws)
            };
        }, ct);

    public Task<ShopResponse> GetShopAsync(Guid userId, CancellationToken ct) =>
        _ops.RunAsync(userId, ctx => BuildShop(ctx.Player, CreateShop(ctx.Player)), ct);

    public Task<ShopResponse> RefreshShopAsync(Guid userId, CancellationToken ct) =>
        _ops.RunAsync(userId, ctx =>
        {
            ShopSystem shop = CreateShop(ctx.Player);
            shop.RefreshManually(ctx.Player.ShopContext).ThrowIfFailed();
            return BuildShop(ctx.Player, shop);
        }, ct);

    public Task<PurchaseResponse> PurchaseAsync(Guid userId, PurchaseRequest request, CancellationToken ct)
    {
        PaymentMethod method = Mappers.ParseEnum<PaymentMethod>(request?.Method, "method");
        if (method == PaymentMethod.RealMoney)
        {
            throw new ApiException(ErrorCode.InvalidArgument, "Use " + ApiRoutes.IapValidate + " for real-money items.");
        }

        return _ops.RunAsync(userId, ctx =>
        {
            PlayerWorkspace ws = ctx.Player;
            ShopSystem shop = CreateShop(ws);
            PurchaseResult result = shop.BuyItem(request!.ItemId ?? string.Empty, method, ws.ShopContext);
            if (!result.Success)
            {
                throw new ApiException(result.Error, result.Message);
            }
            if (result.Item.Kind == ShopItemKind.Cosmetic)
            {
                ws.Achievements.SetStatMax(StatKey.CosmeticsOwned, ws.State.Inventory.Cosmetics.Count);
            }
            return new PurchaseResponse { Granted = Mappers.Reward(result.Granted), Wallet = Mappers.Wallet(ws), Lives = Mappers.Lives(ws) };
        }, ct);
    }

    public Task<InventoryDto> EquipAsync(Guid userId, EquipRequest request, CancellationToken ct) =>
        _ops.RunAsync(userId, ctx =>
        {
            ctx.Player.Inventory.Equip(request?.CosmeticId ?? string.Empty).ThrowIfFailed();
            return Mappers.Inventory(ctx.Player);
        }, ct);

    public Task<IapPrecheckResponse> PrecheckAsync(Guid userId, IapPrecheckRequest request, CancellationToken ct) =>
        _ops.RunAsync(userId, ctx =>
        {
            PlayerWorkspace ws = ctx.Player;
            ErrorCode error = CreateShop(ws).CanStartRealMoneyPurchase(request?.Sku ?? string.Empty, ws.ShopContext);
            int cents = ws.Balance.Economy.OrbePacks.Find(p => p.Sku == request?.Sku)?.PriceCents
                ?? (request?.Sku == ws.Balance.Economy.RemoveAdsSku ? ws.Balance.Economy.RemoveAdsPriceCents
                : request?.Sku == ws.Balance.Economy.BattlePassSku ? ws.Balance.Economy.BattlePassPriceCents
                : request?.Sku == ws.Balance.Economy.RarePerkSku ? ws.Balance.Economy.RarePerkPriceCents : 0);
            return new IapPrecheckResponse { Allowed = error == ErrorCode.None, Error = error == ErrorCode.None ? null : error.ToString(), PriceCents = cents };
        }, ct);

    /// <summary>
    /// 1) validate the receipt with Google (outside the transaction), 2) grant atomically and idempotently
    /// (unique store order id), 3) consume/acknowledge. A crash between 2 and 3 is healed by the client retrying:
    /// the grant is skipped (already granted) and the acknowledgement is attempted again.
    /// </summary>
    public async Task<IapValidateResponse> ValidatePurchaseAsync(Guid userId, IapValidateRequest request, CancellationToken ct)
    {
        string sku = request?.Sku?.Trim() ?? string.Empty;
        string token = request?.PurchaseToken?.Trim() ?? string.Empty;
        if (sku.Length == 0 || token.Length is 0 or > 4096)
        {
            throw new ApiException(ErrorCode.InvalidArgument, "sku and purchaseToken are required.");
        }

        ReceiptValidationResult receipt = await _receipts.ValidateAsync(sku, token, ct).ConfigureAwait(false);
        if (!receipt.Valid)
        {
            await _ops.RunAsync(userId, ctx =>
            {
                ctx.Player.AntiCheat.FlagSuspiciousActivity(ctx.Player.IdString, FlagReason.ReceiptFraud, CheatSeverity.Suspicious, sku + ": " + receipt.Error);
                return true;
            }, ct, allowBanned: true).ConfigureAwait(false);
            throw new ApiException(ErrorCode.PermissionDenied, "Receipt could not be verified.");
        }

        // The server only ACKNOWLEDGES (prevents Google's automatic refund); consumables are consumed by the client
        // (Unity IAP ConfirmPendingPurchase) once it has received this response, which allows buying the pack again.
        const bool consumable = false;
        IapValidateResponse response = await _ops.RunAsync(userId, async ctx =>
        {
            PlayerWorkspace ws = ctx.Player;
            ShopSystem shop = CreateShop(ws);
            OrbePackDefinition? pack = ws.Balance.Economy.OrbePacks.Find(p => p.Sku == sku);
            int cents = pack?.PriceCents ?? 0;

            bool inserted = await ctx.Tx.TryInsertIapAsync(new IapRow
            {
                StoreTransactionId = receipt.OrderId,
                PlayerId = userId,
                Sku = sku,
                PriceCents = Math.Max(1, cents == 0 ? PriceOfNonPack(ws, sku) : cents),
                PurchaseTokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))),
                Status = IapStatus.Granted,
                CreatedAt = ws.Now
            }).ConfigureAwait(false);

            if (!inserted)
            {
                return new IapValidateResponse { AlreadyGranted = true, Wallet = Mappers.Wallet(ws), Vip = Mappers.Vip(ws), Inventory = Mappers.Inventory(ws) };
            }

            PurchaseResult result = shop.GrantRealMoneyPurchase(sku, receipt.OrderId, ws.ShopContext);
            if (!result.Success)
            {
                throw new ApiException(result.Error, result.Message);
            }
            if (receipt.TestPurchase)
            {
                _logger.LogInformation("Test purchase {Sku} granted to {PlayerId}", sku, userId);
            }
            return new IapValidateResponse
            {
                Reward = Mappers.Reward(result.Granted),
                Wallet = Mappers.Wallet(ws),
                Vip = Mappers.Vip(ws),
                Inventory = Mappers.Inventory(ws)
            };
        }, ct, allowBanned: true).ConfigureAwait(false);

        try
        {
            await _receipts.AcknowledgeAsync(sku, token, consumable, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Acknowledge failed for order {OrderId}; the client will retry.", receipt.OrderId);
        }
        return response;
    }

    public Task<RewardedAdResponse> RewardedAdAsync(Guid userId, RewardedAdRequest request, CancellationToken ct) =>
        _ops.RunAsync(userId, ctx =>
        {
            PlayerWorkspace ws = ctx.Player;
            string placement = (request?.Placement ?? string.Empty).Trim().ToLowerInvariant();
            if (placement is not ("life" or "coins"))
            {
                throw new ApiException(ErrorCode.InvalidArgument, "placement must be life or coins.");
            }
            if (!AdPolicy.RegisterRewarded(ws.State.Ads, ws.Now, ws.Balance.LiveOps))
            {
                throw new ApiException(ErrorCode.LimitReached, "Daily rewarded ad limit reached.");
            }

            var reward = new RewardData();
            if (placement == "life")
            {
                ws.Stamina.AddLives(1);
                reward.Lives = 1;
            }
            else
            {
                reward.Coins = ws.CreditEarnedCoins(100, TransactionReason.AdReward, "ad:" + ws.State.Ads.RewardedToday);
            }

            return new RewardedAdResponse
            {
                Reward = Mappers.Reward(reward),
                RemainingToday = ws.Balance.LiveOps.RewardedAdDailyCap - ws.State.Ads.RewardedToday,
                Wallet = Mappers.Wallet(ws),
                Lives = Mappers.Lives(ws)
            };
        }, ct);

    private ShopSystem CreateShop(PlayerWorkspace ws)
    {
        var shop = new ShopSystem(ws.Balance, ws.Clock);
        shop.OnTransaction += record =>
        {
            record.PlayerId = ws.IdString;
            ws.NewPurchases.Add(record);
        };
        return shop;
    }

    private static ShopResponse BuildShop(PlayerWorkspace ws, ShopSystem shop) => new()
    {
        Items = shop.GetShopItems(ws.ShopContext).Select(Mappers.ShopItem).ToList(),
        NextRefreshUnixMs = TimeUtil.ToUnixMs(TimeUtil.NextDailyReset(ws.Now)),
        RefreshCostOrbes = ws.Balance.Economy.ShopRefreshOrbes,
        Wallet = Mappers.Wallet(ws)
    };

    private static int PriceOfNonPack(PlayerWorkspace ws, string sku)
    {
        var e = ws.Balance.Economy;
        if (sku == e.RemoveAdsSku)
        {
            return e.RemoveAdsPriceCents;
        }
        if (sku == e.BattlePassSku)
        {
            return e.BattlePassPriceCents;
        }
        if (sku == e.RarePerkSku)
        {
            return e.RarePerkPriceCents;
        }
        throw new ApiException(ErrorCode.NotFound, "Unknown SKU.");
    }
}
