using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Economy;
using CrushRoyale.Core.Progression;
using CrushRoyale.Core.Tests.Economy;
using CrushRoyale.Core.Tests.Gameplay;

namespace CrushRoyale.Core.Tests.Progression;

public class StaminaTests
{
    [Fact]
    public void StartsWithTwoLives_AndRegenerates()
    {
        var clock = EconomyFixtures.Clock();
        var stamina = new StaminaManager(Fixtures.Balance, new StaminaState(), clock);
        Assert.Equal(2, stamina.Lives);
        Assert.True(stamina.ConsumeLive());
        Assert.True(stamina.ConsumeLive());
        Assert.False(stamina.ConsumeLive());
        Assert.False(stamina.HasLives());
        Assert.Equal(1800f, stamina.GetRechargeTimer());

        clock.Advance(TimeSpan.FromMinutes(31));
        Assert.Equal(1, stamina.Lives);
        Assert.Equal(1740f, stamina.GetRechargeTimer());

        clock.Advance(TimeSpan.FromHours(5));
        Assert.Equal(5, stamina.Lives);
        Assert.Equal(0f, stamina.GetRechargeTimer());
    }

    [Fact]
    public void Quote_FollowsGddEscalation()
    {
        var stamina = new StaminaManager(Fixtures.Balance, new StaminaState(), EconomyFixtures.Clock());
        Assert.Equal(100, stamina.OrbePriceForIndex(0));
        Assert.Equal(125, stamina.OrbePriceForIndex(1));
        Assert.Equal(156, stamina.OrbePriceForIndex(2));

        var three = stamina.QuoteLives(3);
        Assert.Equal(200, three.Coins);
        Assert.Equal(100, three.Orbes);

        var bulk = stamina.QuoteLives(5);
        Assert.True(bulk.BulkDiscountApplied);
        Assert.Equal(100, bulk.Coins);
        Assert.Equal(100 + 125 + 156, bulk.Orbes);
    }

    [Fact]
    public void BuyLives_DebitsBothCurrencies_AndResetsDaily()
    {
        var clock = EconomyFixtures.Clock();
        var wallet = EconomyFixtures.Wallet(clock, 1000, 1000);
        var stamina = new StaminaManager(Fixtures.Balance, new StaminaState(), clock);

        Assert.True(stamina.BuyLives(3, wallet).Success);
        Assert.Equal(5, stamina.Lives);
        Assert.Equal(800, wallet.Coins);
        Assert.Equal(900, wallet.Orbes);
        Assert.Equal(125, stamina.QuoteLives(1).Orbes);

        var poor = EconomyFixtures.Wallet(clock, 0, 10);
        Assert.Equal(ErrorCode.NotEnoughOrbes, stamina.BuyLives(1, poor).Error);

        clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(100, stamina.QuoteLives(1).Coins);
    }

    [Fact]
    public void VipPerks_RechargeDailyLifeAndContinue()
    {
        var clock = EconomyFixtures.Clock();
        Assert.Equal(25 * 60000L, new StaminaManager(Fixtures.Balance, new StaminaState(), clock, VipTier.Vip4).RechargeIntervalMs);
        Assert.Equal(30 * 60000L * 950 / 1000, new StaminaManager(Fixtures.Balance, new StaminaState(), clock, VipTier.None, 1, 50).RechargeIntervalMs);

        var vip2 = new StaminaManager(Fixtures.Balance, new StaminaState(), clock, VipTier.Vip2);
        Assert.True(vip2.ClaimVipDailyLife());
        Assert.False(vip2.ClaimVipDailyLife());
        Assert.Equal(0, vip2.GetContinueCount());

        var vip8 = new StaminaManager(Fixtures.Balance, new StaminaState(), clock, VipTier.Vip8);
        Assert.Equal(1, vip8.GetContinueCount());
        Assert.True(vip8.UseFreeContinue());
        Assert.Equal(0, vip8.GetContinueCount());
        clock.Advance(TimeSpan.FromDays(1));
        Assert.Equal(1, vip8.GetContinueCount());
        // 50 -> 62 (62.5 floored) -> 77 (77.5 floored)
        Assert.Equal(77, vip8.GetContinueOrbePrice(2));
    }
}

public class AchievementTests
{
    [Fact]
    public void Catalog_Has60AchievementsOn12Pages()
    {
        Assert.Equal(60, AchievementCatalog.All.Count);
        Assert.Equal(12, AchievementCatalog.PageCount);
        Assert.Equal(60, AchievementCatalog.All.Select(a => a.Id).Distinct().Count());
        Assert.All(Enumerable.Range(1, 12), p => Assert.Equal(5, AchievementCatalog.GetPage(p).Count));
    }

    [Fact]
    public void UnlockClaimAndPageBonus()
    {
        var system = new AchievementSystem(new AchievementProgressState(), EconomyFixtures.Clock());
        var unlocked = new List<string>();
        int? page = null;
        system.OnAchievementUnlocked += unlocked.Add;
        system.OnPageCompleted += p => page = p;

        system.IncrementStat(StatKey.CascadesTriggered, 9);
        Assert.False(system.CheckAchievementUnlock("cascade_1"));
        Assert.Null(system.UnlockAchievement("cascade_1"));
        system.IncrementStat(StatKey.CascadesTriggered, 1);
        Assert.Contains("cascade_1", unlocked);
        Assert.Equal(1f, system.GetProgress("cascade_1"));
        Assert.Equal(0.02f, system.GetProgress("cascade_500"));

        var reward = system.UnlockAchievement("cascade_1")!;
        Assert.Equal(100, reward.Coins);
        Assert.Null(system.UnlockAchievement("cascade_1"));

        system.SetStatMax(StatKey.HighestStage, 11);
        system.SetStatMax(StatKey.ThreeStarStages, 5);
        system.IncrementStat(StatKey.SpecialsCreated, 5);
        system.IncrementStat(StatKey.BossesDefeated, 1);
        foreach (string id in new[] { "first_steps", "perfectionist_1", "bonus_1" })
        {
            Assert.NotNull(system.UnlockAchievement(id));
        }
        Assert.Equal(0.8f, system.GetPageCompletion(1));
        var last = system.UnlockAchievement("first_boss")!;
        Assert.Contains("frame.page1", last.Cosmetics);
        Assert.Equal(1, page);
        Assert.Equal(0.05f, system.GetTotalCoinBonus());
        Assert.Empty(system.GetUnclaimed());
    }

    [Fact]
    public void StageResult_FeedsStats()
    {
        var session = Fixtures.StorySession(Fixtures.Stage(moves: 12, target: 1_000_000), League.Master, new CrushRoyale.Core.Gameplay.LoadoutEntry(PowerUpType.ChronoBomb, 1));
        session.ActivatePowerUp(PowerUpType.ChronoBomb, null, 100);
        CrushRoyale.Core.Gameplay.HeadlessRunner.Run(session, new CrushRoyale.Core.Gameplay.GreedyBot());
        var system = new AchievementSystem(new AchievementProgressState(), EconomyFixtures.Clock());
        system.RecordStageResult(session.GetResult(), null, new CrushRoyale.Core.Story.StoryProgress { HighestUnlockedStage = 12, TotalStars = 20 });

        Assert.Equal(12, system.GetStat(StatKey.HighestStage));
        Assert.Equal(1, system.GetStat(StatKey.PowerUpTypesUsed));
        Assert.Equal(1, system.GetStat(StatKey.ChronoBombsUsed));
        Assert.Equal(session.GetResult().TotalCascades, system.GetStat(StatKey.CascadesTriggered));
    }
}

public class LiveOpsTests
{
    private static readonly LiveOpsBalance Live = Fixtures.Balance.LiveOps;

    [Fact]
    public void BattlePass_TiersClaimsAndSeasons()
    {
        var clock = EconomyFixtures.Clock();
        var state = new BattlePassState();
        Assert.True(BattlePass.EnsureSeason(state, clock.UtcNow, Live));
        BattlePass.AddXp(state, 2500);
        Assert.Equal(2, BattlePass.TierForXp(state.Xp, Live));

        Assert.Equal(ErrorCode.FeatureLocked, BattlePass.Claim(state, 3, false, false, Live).Error);
        Assert.Equal(110, BattlePass.Claim(state, 1, false, false, Live).Value.Coins);
        Assert.Equal(ErrorCode.AlreadyClaimed, BattlePass.Claim(state, 1, false, false, Live).Error);
        Assert.Equal(ErrorCode.PermissionDenied, BattlePass.Claim(state, 1, true, false, Live).Error);
        Assert.True(BattlePass.Claim(state, 1, true, true, Live).Success);

        Assert.Contains($"bp.s{state.SeasonIndex}.board", BattlePass.GetTierReward(state.SeasonIndex, 50).Premium.Cosmetics);
        Assert.NotNull(Core.Economy.CosmeticCatalog.Get($"bp.s{state.SeasonIndex}.board"));

        clock.Set(BattlePass.SeasonEndUtc(state.SeasonIndex, Live));
        Assert.True(BattlePass.EnsureSeason(state, clock.UtcNow, Live));
        Assert.Equal(0, state.Xp);
    }

    [Fact]
    public void DailyQuests_DeterministicAndGated()
    {
        var clock = EconomyFixtures.Clock();
        var a = new DailyQuestState();
        var b = new DailyQuestState();
        DailyQuests.EnsureDay(a, "p1", clock.UtcNow, Live, pvpUnlocked: false, guildMember: false);
        DailyQuests.EnsureDay(b, "p1", clock.UtcNow, Live, pvpUnlocked: false, guildMember: false);

        Assert.Equal(3, a.Quests.Count);
        Assert.Equal(a.Quests.Select(q => q.Type), b.Quests.Select(q => q.Type));
        Assert.DoesNotContain(a.Quests, q => q.Type is QuestType.PlayPvp or QuestType.WinPvp or QuestType.AttackGuildBoss);

        var quest = a.Quests[0];
        Assert.Equal(ErrorCode.FeatureLocked, DailyQuests.Claim(a, quest.Id, Live).Error);
        DailyQuests.Track(a, quest.Type, 1000);
        var reward = DailyQuests.Claim(a, quest.Id, Live);
        Assert.Equal(150, reward.Value.Coins);
        Assert.Equal(250, reward.Value.BattlePassXp);
        Assert.False(DailyQuests.EnsureDay(a, "p1", clock.UtcNow, Live, true, true));
    }

    [Fact]
    public void LoginCalendar_OncePerDay()
    {
        var clock = EconomyFixtures.Clock();
        var state = new LoginCalendarState();
        Assert.Equal(100, LoginCalendar.Claim(state, clock.UtcNow, Live).Value.Coins);
        Assert.Equal(ErrorCode.AlreadyClaimed, LoginCalendar.Claim(state, clock.UtcNow, Live).Error);
        clock.Advance(TimeSpan.FromDays(1));
        LoginCalendar.Claim(state, clock.UtcNow, Live);
        clock.Advance(TimeSpan.FromDays(3));
        Assert.Equal(5, LoginCalendar.Claim(state, clock.UtcNow, Live).Value.Orbes);
        Assert.Equal(3, state.TotalLoginDays);
    }

    [Fact]
    public void Ads_FrequencyUnlockRemoveAdsAndVipSkip()
    {
        var clock = EconomyFixtures.Clock();
        var b = Fixtures.Balance;
        var none = new VipSystem(b).GetBenefit(VipTier.None);

        var early = new AdState();
        Assert.False(AdPolicy.OnEvent(early, AdPlacement.StoryWin, clock.UtcNow, 10, false, none, b));
        Assert.False(AdPolicy.OnEvent(early, AdPlacement.StoryWin, clock.UtcNow, 10, false, none, b));

        var state = new AdState();
        Assert.False(AdPolicy.OnEvent(state, AdPlacement.StoryWin, clock.UtcNow, 11, false, none, b));
        Assert.True(AdPolicy.OnEvent(state, AdPlacement.StoryWin, clock.UtcNow, 11, false, none, b));
        AdPolicy.OnEvent(state, AdPlacement.StoryWin, clock.UtcNow, 11, false, none, b);
        clock.Advance(TimeSpan.FromSeconds(60));
        Assert.False(AdPolicy.OnEvent(state, AdPlacement.StoryWin, clock.UtcNow, 11, false, none, b));
        clock.Advance(TimeSpan.FromSeconds(61));
        Assert.True(AdPolicy.OnEvent(state, AdPlacement.StoryWin, clock.UtcNow, 11, false, none, b));

        var removed = new AdState();
        Assert.False(AdPolicy.OnEvent(removed, AdPlacement.PvpBattle, clock.UtcNow, 100, true, none, b));

        var vipState = new AdState();
        var vip7 = new VipSystem(b).GetBenefit(VipTier.Vip7);
        AdPolicy.OnEvent(vipState, AdPlacement.StoryWin, clock.UtcNow, 50, false, vip7, b);
        Assert.False(AdPolicy.OnEvent(vipState, AdPlacement.StoryWin, clock.UtcNow, 50, false, vip7, b));
        AdPolicy.OnEvent(vipState, AdPlacement.StoryWin, clock.UtcNow, 50, false, vip7, b);
        Assert.True(AdPolicy.OnEvent(vipState, AdPlacement.StoryWin, clock.UtcNow, 50, false, vip7, b));

        Assert.True(AdPolicy.RegisterRewarded(state, clock.UtcNow, b.LiveOps));
    }
}
