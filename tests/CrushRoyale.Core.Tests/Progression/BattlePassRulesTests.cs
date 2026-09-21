using System.Linq;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Economy;
using CrushRoyale.Core.Progression;
using CrushRoyale.Core.Tests.Economy;
using CrushRoyale.Core.Tests.Gameplay;
using Xunit;

namespace CrushRoyale.Core.Tests.Progression;

/// <summary>
/// The three rules the owner set for the season pass: it is bought with real money and nothing else, its exclusive
/// outfit is the very last thing the paid track gives, and orbes may only buy impatience one tier at a time.
/// </summary>
public sealed class BattlePassRulesTests
{
    private static readonly GameBalance Balance = GameBalance.CreateDefault();

    [Fact]
    public void ThePass_IsOnlyEverOfferedForRealMoney()
    {
        var clock = EconomyFixtures.Clock();
        var shop = new ShopSystem(Fixtures.Balance, clock);
        PlayerContext ctx = EconomyFixtures.Context(clock);

        ShopItem pass = shop.GetShopItems(ctx).Single(i => i.Kind == ShopItemKind.BattlePass);
        Assert.False(string.IsNullOrEmpty(pass.Sku), "The pass must carry a store SKU.");
        Assert.True(pass.PriceCents > 0, "The pass must have a real-money price.");
        Assert.Equal(0, pass.PriceOrbes);

        // And the in-game currency path refuses it outright, whatever a future caller passes.
        Assert.False(shop.BuyItem(pass.Id, PaymentMethod.Orbes, ctx).Success);
        Assert.False(shop.BuyItem(pass.Id, PaymentMethod.Coins, ctx).Success);
    }

    [Fact]
    public void TheExclusiveOutfit_IsTheLastRewardOfThePaidTrack()
    {
        int last = Balance.LiveOps.BattlePassTiers;
        for (int season = 0; season <= 12; season++)
        {
            string exclusive = SeasonCatalog.ByIndex(season).ExclusiveCosmeticId;
            Assert.False(string.IsNullOrEmpty(exclusive), $"Season {season} has no exclusive outfit.");

            BattlePassTierReward top = BattlePass.GetTierReward(season, last);
            Assert.Contains(exclusive, top.Premium.Cosmetics);

            for (int tier = 1; tier < last; tier++)
            {
                BattlePassTierReward reward = BattlePass.GetTierReward(season, tier);
                Assert.DoesNotContain(exclusive, reward.Premium.Cosmetics);
                Assert.DoesNotContain(exclusive, reward.Free.Cosmetics);
            }
        }
    }

    [Fact]
    public void TheFreeTrack_NeverGivesACosmeticOfThePaidTrack()
    {
        for (int tier = 1; tier <= Balance.LiveOps.BattlePassTiers; tier++)
        {
            BattlePassTierReward reward = BattlePass.GetTierReward(1, tier);
            Assert.Empty(reward.Free.Cosmetics);
        }
    }

    [Fact]
    public void BuyingATier_AdvancesExactlyOneTier_AndCostsMoreEveryTime()
    {
        var state = new BattlePassState { SeasonIndex = 1 };
        int first = BattlePass.TierPrice(state, Balance.Economy);
        Assert.Equal(Balance.Economy.BattlePassTierPriceOrbes, first);

        Assert.True(BattlePass.BuyTier(state, Balance.LiveOps).Success);
        Assert.Equal(1, BattlePass.TierForXp(state.Xp, Balance.LiveOps));

        int second = BattlePass.TierPrice(state, Balance.Economy);
        Assert.True(second > first, $"The second tier costs {second}, the first cost {first}.");

        Assert.True(BattlePass.BuyTier(state, Balance.LiveOps).Success);
        Assert.Equal(2, BattlePass.TierForXp(state.Xp, Balance.LiveOps));
    }

    /// <summary>Progress already made inside the current tier must not be thrown away by buying the next one.</summary>
    [Fact]
    public void BuyingATier_KeepsTheProgressAlreadyEarned()
    {
        var state = new BattlePassState { SeasonIndex = 1 };
        BattlePass.AddXp(state, Balance.LiveOps.BattlePassXpPerTier * 3 / 2);
        Assert.Equal(1, BattlePass.TierForXp(state.Xp, Balance.LiveOps));

        Assert.True(BattlePass.BuyTier(state, Balance.LiveOps).Success);
        Assert.Equal(2, BattlePass.TierForXp(state.Xp, Balance.LiveOps));
    }

    [Fact]
    public void ACompletedPass_CannotBeBoughtFurther()
    {
        var state = new BattlePassState { SeasonIndex = 1 };
        BattlePass.AddXp(state, Balance.LiveOps.BattlePassXpPerTier * Balance.LiveOps.BattlePassTiers);
        Assert.Equal(Balance.LiveOps.BattlePassTiers, BattlePass.TierForXp(state.Xp, Balance.LiveOps));
        Assert.False(BattlePass.BuyTier(state, Balance.LiveOps).Success);
    }

    [Fact]
    public void ANewSeason_ResetsTheBoughtTierPrice()
    {
        var state = new BattlePassState { SeasonIndex = 1 };
        BattlePass.BuyTier(state, Balance.LiveOps);
        BattlePass.BuyTier(state, Balance.LiveOps);
        Assert.True(BattlePass.TierPrice(state, Balance.Economy) > Balance.Economy.BattlePassTierPriceOrbes);

        state.SeasonIndex = -1;
        BattlePass.EnsureSeason(state, SeasonCatalog.ByIndex(2).StartUtc, Balance.LiveOps);
        Assert.Equal(0, state.TiersBought);
        Assert.Equal(Balance.Economy.BattlePassTierPriceOrbes, BattlePass.TierPrice(state, Balance.Economy));
    }
}
