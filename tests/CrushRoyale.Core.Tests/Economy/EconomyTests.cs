using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Economy;
using CrushRoyale.Core.Progression;
using CrushRoyale.Core.Tests.Gameplay;

namespace CrushRoyale.Core.Tests.Economy;

internal static class EconomyFixtures
{
    public static ManualClock Clock() => new(new DateTime(2026, 9, 14, 9, 0, 0));

    public static Wallet Wallet(IClock clock, long coins = 10000, long orbes = 2000)
    {
        var wallet = new Wallet(new WalletState(), clock);
        if (coins > 0)
        {
            wallet.Credit(Currency.Coins, coins, TransactionReason.StartingGrant);
        }
        if (orbes > 0)
        {
            wallet.Credit(Currency.Orbes, orbes, TransactionReason.StartingGrant);
        }
        return wallet;
    }

    public static PlayerContext Context(IClock clock, int stage = 16, int? age = 30, League league = League.Bronze, long orbes = 2000) => new()
    {
        PlayerId = "p1",
        Wallet = Wallet(clock, 10000, orbes),
        Inventory = new Inventory(new InventoryState()),
        Shop = new ShopState(),
        Stamina = new StaminaManager(Fixtures.Balance, new StaminaState(), clock),
        Vip = new VipStatus { PlayerId = "p1" },
        HighestLeague = league,
        HighestUnlockedStage = stage,
        DeclaredAge = age,
        BattlePassSeason = 5
    };
}

public class WalletTests
{
    [Fact]
    public void CreditDebit_KeepsLedgerReconciled()
    {
        var wallet = EconomyFixtures.Wallet(EconomyFixtures.Clock(), 500, 50);
        Assert.True(wallet.Debit(Currency.Coins, 200, TransactionReason.ShopPurchase, "x").Success);
        Assert.Equal(ErrorCode.NotEnoughOrbes, wallet.Debit(Currency.Orbes, 51, TransactionReason.ShopPurchase).Error);
        Assert.Equal(ErrorCode.InvalidArgument, wallet.Credit(Currency.Coins, -5, TransactionReason.AdminGrant).Error);
        Assert.Equal(300, wallet.Coins);
        Assert.Equal(50, wallet.Orbes);
        Assert.True(wallet.Reconcile(out string report), report);
        Assert.Equal(3, wallet.State.Ledger.Count);
    }

    [Fact]
    public void IdempotencyKey_PreventsDoubleCredit()
    {
        var wallet = EconomyFixtures.Wallet(EconomyFixtures.Clock(), 0, 0);
        Assert.True(wallet.Credit(Currency.Orbes, 395, TransactionReason.RealMoneyPurchase, "pack2", "gp-123").Success);
        Assert.Equal(ErrorCode.DuplicateRequest, wallet.Credit(Currency.Orbes, 395, TransactionReason.RealMoneyPurchase, "pack2", "gp-123").Error);
        Assert.Equal(395, wallet.Orbes);
    }

    [Fact]
    public void LedgerTrimming_StillReconciles()
    {
        var clock = EconomyFixtures.Clock();
        var wallet = new Wallet(new WalletState(), clock, maxLedgerEntries: 10);
        for (int i = 0; i < 40; i++)
        {
            wallet.Credit(Currency.Coins, 10 + i, TransactionReason.StageReward);
            if (i % 3 == 0)
            {
                wallet.Debit(Currency.Coins, 5, TransactionReason.ShopPurchase);
            }
        }
        Assert.Equal(10, wallet.State.Ledger.Count);
        Assert.True(wallet.Reconcile(out string report), report);
    }
}

public class OrbePricingAndVipTests
{
    private readonly ShopSystem _shop = new(Fixtures.Balance, EconomyFixtures.Clock());

    [Fact]
    public void DefaultBalance_IsValid_AndPackValueIncreases()
    {
        Fixtures.Balance.Validate();
        decimal previous = 0;
        for (int level = 1; level <= 7; level++)
        {
            var quote = _shop.CalculateOrbePricing(level);
            Assert.True(quote.OrbesPerEuro > previous, $"pack {level}");
            previous = quote.OrbesPerEuro;
        }
    }

    [Fact]
    public void BrokenPackOrder_FailsValidation()
    {
        var balance = GameBalance.CreateDefault();
        balance.Economy.OrbePacks[2].BonusOrbes = 35;
        Assert.Throws<InvalidOperationException>(() => balance.Validate());
    }

    [Fact]
    public void Pack2_WithRepeatBonusOnThirdPurchase()
    {
        var first = _shop.CalculateOrbePricing(2);
        Assert.Equal(395, first.TotalOrbes);
        Assert.Equal(4.99m, first.PriceEur);
        Assert.Equal(79.2m, first.OrbesPerEuro);
        Assert.Equal(395 + 77, _shop.CalculateOrbePricing(2, previousPurchasesOfPack: 2).TotalOrbes);
        Assert.Equal(395, _shop.CalculateOrbePricing(2, previousPurchasesOfPack: 3).TotalOrbes);
        Assert.Equal(Tuple.Create(30000, 199.99m), _shop.CalculateOrbePricingTuple(7));
    }

    [Theory]
    [InlineData(0, VipTier.None)]
    [InlineData(149, VipTier.None)]
    [InlineData(150, VipTier.Vip1)]
    [InlineData(4999, VipTier.Vip3)]
    [InlineData(999999, VipTier.Vip9)]
    [InlineData(1000000, VipTier.Vip10)]
    public void VipTiers_FromLifetimeSpend(long cents, VipTier tier)
    {
        Assert.Equal(tier, new VipSystem(Fixtures.Balance).GetTierForSpend(cents));
        Assert.Equal(tier, new VipSystem(Fixtures.Balance, _ => cents).GetPlayerVipTier("any"));
    }

    [Fact]
    public void VipBenefits_AreNotPayToWin()
    {
        var vip = new VipSystem(Fixtures.Balance);
        Assert.Equal(160f, vip.ApplyVipBonuses(100f, VipTier.Vip10));
        Assert.Equal(1.2f, vip.GetOrbeBonusMultiplier(VipTier.Vip10));
        Assert.False(vip.CanStealPowerUp(VipTier.Vip10, GameMode.PvpRanked));
        Assert.True(vip.CanStealPowerUp(VipTier.Vip9, GameMode.FriendlyChallenge));
        Assert.False(vip.CanStealPowerUp(VipTier.Vip8, GameMode.FriendlyChallenge));
        Assert.True(vip.GetBenefit(VipTier.Vip8).FreeContinueDaily);
        Assert.False(vip.GetBenefit(VipTier.Vip7).FreeContinueDaily);
    }

    [Fact]
    public void RecordPurchase_GrantsTierRewards()
    {
        var vip = new VipSystem(Fixtures.Balance);
        var status = new VipStatus();
        var upgrade = vip.RecordPurchase(status, 10000);
        Assert.Equal(VipTier.Vip5, upgrade.After);
        Assert.Equal(5, upgrade.ReachedTiers.Count);
        Assert.Contains("frame.crystal", upgrade.CosmeticsGranted);
        Assert.Contains("frame.royal", upgrade.CosmeticsGranted);
        Assert.False(upgrade.RarePerkGranted);

        Assert.True(vip.RecordPurchase(status, 990000).RarePerkGranted);
        vip.RecordRefund(status, 990000);
        Assert.Equal(VipTier.Vip5, status.Tier);

        var progress = vip.GetProgress(7500);
        Assert.Equal(VipTier.Vip4, progress.Tier);
        Assert.Equal(0.5f, progress.ProgressToNext, 3);
    }

    [Fact]
    public void CoinBonusStack_IsCapped()
    {
        var context = new BonusContext { Vip = VipTier.Vip10, CollectionPagesCompleted = 12, GuildCoinBonusPermille = 100, RarePerk = true };
        Assert.Equal(1400, RewardCalculator.TotalCoinBonusPermille(Fixtures.Balance, context));
        context.CollectionPagesCompleted = 30;
        Assert.Equal(1500, RewardCalculator.TotalCoinBonusPermille(Fixtures.Balance, context));
        Assert.Equal(250, RewardCalculator.ApplyCoinBonus(Fixtures.Balance, 100, context));
        Assert.Equal(12, RewardCalculator.ApplyOrbeBonus(Fixtures.Balance, 10, VipTier.Vip10));
    }
}

public class ShopTests
{
    [Fact]
    public void Shop_LockedBeforeStage15()
    {
        var clock = EconomyFixtures.Clock();
        var shop = new ShopSystem(Fixtures.Balance, clock);
        var ctx = EconomyFixtures.Context(clock, stage: 10);
        Assert.Equal(ErrorCode.FeatureLocked, shop.BuyItem("coins.small", PaymentMethod.Orbes, ctx).Error);
    }

    [Fact]
    public void DailyOffers_AreDeterministic_AndRespectUnlocks()
    {
        var clock = EconomyFixtures.Clock();
        var shop = new ShopSystem(Fixtures.Balance, clock);
        var a = shop.GenerateDailyOffers("p1", 100, 0, League.Bronze, VipTier.None);
        var b = shop.GenerateDailyOffers("p1", 100, 0, League.Bronze, VipTier.None);
        Assert.Equal(a.Select(o => o.Id), b.Select(o => o.Id));
        Assert.Equal(6, a.Count);
        Assert.Single(a, o => o.IsDeal);
        Assert.All(a, o => Assert.True(o.PowerUp <= PowerUpType.BrightSpark));
        Assert.NotEqual(a.Select(o => o.Id), shop.GenerateDailyOffers("p2", 100, 0, League.Bronze, VipTier.None).Select(o => o.Id));
    }

    [Fact]
    public void BuyDailyOffer_OnceThenSoldOut()
    {
        var clock = EconomyFixtures.Clock();
        var shop = new ShopSystem(Fixtures.Balance, clock);
        var ctx = EconomyFixtures.Context(clock);
        var logged = new List<TransactionRecord>();
        shop.OnTransaction += logged.Add;

        var offer = shop.GetShopItems(ctx).First(i => i.Kind is ShopItemKind.PowerUp or ShopItemKind.PowerUpBundle);
        var result = shop.BuyItem(offer.Id, PaymentMethod.Coins, ctx);

        Assert.True(result.Success, result.Message);
        Assert.Equal(offer.Quantity, ctx.Inventory.Count(offer.PowerUp));
        Assert.Equal(10000 - offer.PriceCoins, ctx.Wallet.Coins);
        Assert.Equal(ErrorCode.LimitReached, shop.BuyItem(offer.Id, PaymentMethod.Coins, ctx).Error);
        Assert.Single(logged);
    }

    [Fact]
    public void ManualRefresh_CostsOrbes_AndRerolls()
    {
        var clock = EconomyFixtures.Clock();
        var shop = new ShopSystem(Fixtures.Balance, clock);
        var ctx = EconomyFixtures.Context(clock);
        var before = shop.GetShopItems(ctx).Where(i => i.Id.StartsWith("daily.")).Select(i => i.Id).ToList();
        Assert.True(shop.RefreshManually(ctx).Success);
        Assert.Equal(1950, ctx.Wallet.Orbes);
        Assert.NotEqual(before, ctx.Shop.DailyOffers.Select(i => i.Id));

        clock.Advance(TimeSpan.FromDays(1));
        shop.RefreshShop(ctx);
        Assert.Equal(0, ctx.Shop.ManualRefreshesToday);
    }

    [Fact]
    public void CoinPack_LivesPack_AndCosmetic()
    {
        var clock = EconomyFixtures.Clock();
        var shop = new ShopSystem(Fixtures.Balance, clock);
        var ctx = EconomyFixtures.Context(clock);

        Assert.True(shop.BuyItem("coins.small", PaymentMethod.Orbes, ctx).Success);
        Assert.Equal(11200, ctx.Wallet.Coins);
        Assert.Equal(ErrorCode.InvalidArgument, shop.BuyItem("coins.small", PaymentMethod.Coins, ctx).Error);

        Assert.True(shop.BuyItem("lives.1", PaymentMethod.Coins, ctx).Success);
        Assert.Equal(3, ctx.Stamina.Lives);

        Assert.True(shop.BuyItem("cosmetic.board.ice", PaymentMethod.Orbes, ctx).Success);
        Assert.True(ctx.Inventory.HasCosmetic("board.ice"));
        Assert.DoesNotContain(shop.GetShopItems(ctx), i => i.Id == "cosmetic.board.ice");
        Assert.True(ctx.Inventory.Equip("board.ice").Success);

        Assert.Equal(ErrorCode.InvalidArgument, shop.BuyItem("orbes.pack1", PaymentMethod.RealMoney, ctx).Error);
    }

    [Fact]
    public void ConsumerProtection_AgeGateAndMinorCap()
    {
        var clock = EconomyFixtures.Clock();
        var shop = new ShopSystem(Fixtures.Balance, clock);

        Assert.Equal(ErrorCode.PermissionDenied, shop.CanStartRealMoneyPurchase("crushroyale.orbes.pack1", EconomyFixtures.Context(clock, age: 12)));

        var teen = EconomyFixtures.Context(clock, age: 15);
        Assert.Equal(ErrorCode.None, shop.CanStartRealMoneyPurchase("crushroyale.orbes.pack5", teen));
        Assert.True(shop.GrantRealMoneyPurchase("crushroyale.orbes.pack5", "gpa.1", teen).Success);
        Assert.Equal(ErrorCode.LimitReached, shop.CanStartRealMoneyPurchase("crushroyale.orbes.pack1", teen));

        clock.Advance(TimeSpan.FromDays(31));
        Assert.Equal(ErrorCode.None, shop.CanStartRealMoneyPurchase("crushroyale.orbes.pack1", teen));

        var adult = EconomyFixtures.Context(clock, age: 30);
        Assert.True(shop.GrantRealMoneyPurchase("crushroyale.orbes.pack6", "gpa.2", adult).Success);
        Assert.Equal(ErrorCode.None, shop.CanStartRealMoneyPurchase("crushroyale.orbes.mega", adult));
    }

    [Fact]
    public void RealMoneyGrant_IsIdempotent_AndUpdatesVip()
    {
        var clock = EconomyFixtures.Clock();
        var shop = new ShopSystem(Fixtures.Balance, clock);
        var ctx = EconomyFixtures.Context(clock, orbes: 0);

        var result = shop.GrantRealMoneyPurchase("crushroyale.orbes.pack5", "gpa.77", ctx);
        Assert.True(result.Success);
        Assert.Equal(5500, ctx.Wallet.Orbes);
        Assert.Equal(VipTier.Vip3, ctx.Vip.Tier);
        Assert.Contains("frame.crystal", result.Granted.Cosmetics);
        Assert.Equal(ErrorCode.DuplicateRequest, shop.GrantRealMoneyPurchase("crushroyale.orbes.pack5", "gpa.77", ctx).Error);
        Assert.Equal(5500, ctx.Wallet.Orbes);

        Assert.True(shop.GrantRealMoneyPurchase("crushroyale.removeads", "gpa.78", ctx).Success);
        Assert.True(ctx.Inventory.State.AdsRemoved);
        Assert.Equal(ErrorCode.AlreadyClaimed, shop.CanStartRealMoneyPurchase("crushroyale.removeads", ctx));
    }
}

public class InventoryTests
{
    [Fact]
    public void Loadout_UsesStock_AndOncePerMatchRule()
    {
        var inventory = new Inventory(new InventoryState());
        inventory.Add(PowerUpType.ChronoBomb, 10);
        inventory.Add(PowerUpType.Multiplier2x, 4);

        var loadout = inventory.BuildLoadout(new[] { PowerUpType.ChronoBomb, PowerUpType.Multiplier2x }, Fixtures.Balance, League.Silver);
        Assert.True(loadout.Success);
        Assert.Equal(3, loadout.Value.Single(l => l.Type == PowerUpType.ChronoBomb).Quantity);
        Assert.Equal(1, loadout.Value.Single(l => l.Type == PowerUpType.Multiplier2x).Quantity);

        Assert.Equal(ErrorCode.PowerUpLocked, inventory.BuildLoadout(new[] { PowerUpType.Multiplier2x }, Fixtures.Balance, League.Bronze).Error);
        Assert.Equal(ErrorCode.NotEnoughItems, inventory.BuildLoadout(new[] { PowerUpType.FireStorm }, Fixtures.Balance, League.Master).Error);
    }

    [Fact]
    public void ConsumeUsed_IsAllOrNothing()
    {
        var inventory = new Inventory(new InventoryState());
        inventory.Add(PowerUpType.ChronoBomb, 1);
        var used = new Dictionary<PowerUpType, int> { [PowerUpType.ChronoBomb] = 1, [PowerUpType.FireStorm] = 1 };
        Assert.False(inventory.ConsumeUsed(used).Success);
        Assert.Equal(1, inventory.Count(PowerUpType.ChronoBomb));
    }

    [Fact]
    public void RewardData_GrantsEverything()
    {
        var clock = EconomyFixtures.Clock();
        var wallet = EconomyFixtures.Wallet(clock, 0, 0);
        var inventory = new Inventory(new InventoryState());
        var reward = RewardData.FromCurrency(100, 5).AddPowerUp(PowerUpType.FireStorm, 2);
        reward.Cosmetics.Add("title.legend");

        Assert.True(reward.GrantTo(wallet, inventory, TransactionReason.AchievementReward, "ach", "claim-1").Success);
        Assert.Equal(100, wallet.Coins);
        Assert.Equal(2, inventory.Count(PowerUpType.FireStorm));
        Assert.True(inventory.HasCosmetic("title.legend"));
        Assert.False(reward.GrantTo(wallet, inventory, TransactionReason.AchievementReward, "ach", "claim-1").Success);
    }
}
