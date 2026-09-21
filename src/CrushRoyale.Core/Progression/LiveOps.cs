using System;
using System.Collections.Generic;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Economy;

namespace CrushRoyale.Core.Progression
{
    // ======================================================================= Battle pass

    public sealed class BattlePassState
    {
        public int SeasonIndex { get; set; } = -1;

        public long Xp { get; set; }

        public HashSet<int> ClaimedFree { get; set; } = new HashSet<int>();

        public HashSet<int> ClaimedPremium { get; set; } = new HashSet<int>();
    }

    public sealed class BattlePassTierReward
    {
        public int Tier { get; internal set; }

        public RewardData Free { get; internal set; }

        public RewardData Premium { get; internal set; }
    }

    /// <summary>4-week seasons, 50 tiers, free + premium tracks (GDD "Battle Pass").</summary>
    public static class BattlePass
    {
        /// <summary>Authored schedule (SeasonCatalog), not days-since-epoch: index 0 is the beta season, then 1, 2, 3...</summary>
        public static int SeasonIndex(DateTime utc, LiveOpsBalance balance) => SeasonCatalog.IndexAt(utc);

        public static DateTime SeasonEndUtc(int seasonIndex, LiveOpsBalance balance) => SeasonCatalog.EndUtc(seasonIndex);

        public static int TierForXp(long xp, LiveOpsBalance balance) =>
            (int)Math.Min(balance.BattlePassTiers, Math.Max(0, xp) / Math.Max(1, balance.BattlePassXpPerTier));

        /// <summary>Resets progress when a new season starts.</summary>
        public static bool EnsureSeason(BattlePassState state, DateTime utcNow, LiveOpsBalance balance)
        {
            int season = SeasonIndex(utcNow, balance);
            if (state.SeasonIndex == season)
            {
                return false;
            }
            state.SeasonIndex = season;
            state.Xp = 0;
            state.ClaimedFree.Clear();
            state.ClaimedPremium.Clear();
            return true;
        }

        public static void AddXp(BattlePassState state, int amount, int bonusPermille = 0)
        {
            if (amount <= 0)
            {
                return;
            }
            state.Xp += (long)amount * (1000 + Math.Max(0, bonusPermille)) / 1000;
        }

        public static BattlePassTierReward GetTierReward(int season, int tier)
        {
            if (tier < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(tier));
            }

            // Every tier gives something different from its neighbours (coins, lives, bonuses, orbes) so the track reads
            // as a varied path rather than a column of coin piles.
            long coins = 100 + tier * 10;
            RewardData free;
            switch (tier % 5)
            {
                case 1:
                    free = RewardData.FromCurrency(coins);
                    break;
                case 2:
                    free = new RewardData { Coins = coins / 2, Lives = 1 };
                    break;
                case 3:
                    free = RewardData.FromCurrency(coins / 2).AddPowerUp((PowerUpType)(tier / 5 % 3), 1);
                    break;
                case 4:
                    free = RewardData.FromCurrency(coins, 3);
                    break;
                default:
                    free = RewardData.FromCurrency(coins).AddPowerUp((PowerUpType)(tier / 5 % 3), 1);
                    break;
            }
            if (tier % 10 == 0)
            {
                free.Orbes += 10;
            }

            RewardData premium;
            switch (tier % 4)
            {
                case 1:
                    premium = RewardData.FromCurrency(coins * 2);
                    break;
                case 2:
                    premium = RewardData.FromCurrency(coins, 10);
                    break;
                case 3:
                    premium = RewardData.FromCurrency(coins).AddPowerUp((PowerUpType)(3 + tier / 4 % 3), 1);
                    break;
                default:
                    premium = new RewardData { Coins = coins, Lives = 2 };
                    break;
            }
            if (tier % 10 == 0)
            {
                premium.AddPowerUp((PowerUpType)(6 + tier / 10 % 3), 1);
            }
            if (tier == 25)
            {
                premium.Cosmetics.Add(CosmeticCatalog.BattlePassPrefix + season + ".pieces");
            }
            if (tier == 40)
            {
                premium.Cosmetics.Add(CosmeticCatalog.BattlePassPrefix + season + ".frame");
            }
            if (tier == 50)
            {
                premium.Cosmetics.Add(CosmeticCatalog.BattlePassPrefix + season + ".board");
                // Capstone of the paid track: the outfit that only this season ever grants.
                premium.Cosmetics.Add(SeasonCatalog.ByIndex(season).ExclusiveCosmeticId);
            }
            return new BattlePassTierReward { Tier = tier, Free = free, Premium = premium };
        }

        public static OperationResult<RewardData> Claim(BattlePassState state, int tier, bool premiumTrack, bool hasPremium, LiveOpsBalance balance)
        {
            if (tier < 1 || tier > balance.BattlePassTiers)
            {
                return OperationResult<RewardData>.Fail(ErrorCode.InvalidArgument);
            }
            if (TierForXp(state.Xp, balance) < tier)
            {
                return OperationResult<RewardData>.Fail(ErrorCode.FeatureLocked, "Tier not reached.");
            }
            if (premiumTrack && !hasPremium)
            {
                return OperationResult<RewardData>.Fail(ErrorCode.PermissionDenied, "Premium track not purchased.");
            }

            HashSet<int> claimed = premiumTrack ? state.ClaimedPremium : state.ClaimedFree;
            if (!claimed.Add(tier))
            {
                return OperationResult<RewardData>.Fail(ErrorCode.AlreadyClaimed);
            }
            BattlePassTierReward reward = GetTierReward(state.SeasonIndex, tier);
            return OperationResult<RewardData>.Ok(premiumTrack ? reward.Premium : reward.Free);
        }
    }

    // ======================================================================= Daily quests

    public enum QuestType : byte
    {
        WinStages = 0,
        EarnStars = 1,
        TriggerCascades = 2,
        CreateSpecials = 3,
        BreakStones = 4,
        UsePowerUps = 5,
        PlayPvp = 6,
        WinPvp = 7,
        AttackGuildBoss = 8
    }

    public sealed class DailyQuest
    {
        public string Id { get; set; }

        public QuestType Type { get; set; }

        public int Target { get; set; }

        public int Progress { get; set; }

        public bool Claimed { get; set; }

        public bool IsComplete => Progress >= Target;
    }

    public sealed class DailyQuestState
    {
        public int Day { get; set; } = -1;

        public List<DailyQuest> Quests { get; set; } = new List<DailyQuest>();
    }

    public static class DailyQuests
    {
        private static readonly int[] Targets = { 3, 6, 15, 8, 10, 3, 3, 2, 2 };

        /// <summary>Rolls today's quests (deterministic per player/day). PvP / guild quests only if those features are unlocked.</summary>
        public static bool EnsureDay(DailyQuestState state, string playerId, DateTime utcNow, LiveOpsBalance balance, bool pvpUnlocked, bool guildMember)
        {
            int day = TimeUtil.DayIndex(utcNow);
            if (state.Day == day)
            {
                return false;
            }

            var pool = new List<QuestType> { QuestType.WinStages, QuestType.EarnStars, QuestType.TriggerCascades, QuestType.CreateSpecials, QuestType.BreakStones, QuestType.UsePowerUps };
            if (pvpUnlocked)
            {
                pool.Add(QuestType.PlayPvp);
                pool.Add(QuestType.WinPvp);
            }
            if (guildMember)
            {
                pool.Add(QuestType.AttackGuildBoss);
            }

            var rng = DeterministicRandom.Derive(StableHash.Fnv1a(playerId ?? string.Empty), (ulong)day, 0x51);
            rng.Shuffle(pool);

            state.Day = day;
            state.Quests.Clear();
            for (int i = 0; i < Math.Min(balance.DailyQuestCount, pool.Count); i++)
            {
                QuestType type = pool[i];
                state.Quests.Add(new DailyQuest { Id = "q" + day + "." + i, Type = type, Target = Targets[(int)type] });
            }
            return true;
        }

        public static void Track(DailyQuestState state, QuestType type, int amount)
        {
            if (amount <= 0)
            {
                return;
            }
            foreach (DailyQuest q in state.Quests)
            {
                if (q.Type == type && !q.Claimed)
                {
                    q.Progress = Math.Min(q.Target, q.Progress + amount);
                }
            }
        }

        public static OperationResult<RewardData> Claim(DailyQuestState state, string questId, LiveOpsBalance balance)
        {
            DailyQuest quest = state.Quests.Find(q => q.Id == questId);
            if (quest == null)
            {
                return OperationResult<RewardData>.Fail(ErrorCode.NotFound);
            }
            if (!quest.IsComplete)
            {
                return OperationResult<RewardData>.Fail(ErrorCode.FeatureLocked, "Quest not complete.");
            }
            if (quest.Claimed)
            {
                return OperationResult<RewardData>.Fail(ErrorCode.AlreadyClaimed);
            }
            quest.Claimed = true;
            return OperationResult<RewardData>.Ok(new RewardData { Coins = balance.QuestRewardCoins, BattlePassXp = balance.XpQuest });
        }
    }

    // ======================================================================= Login calendar

    public sealed class LoginCalendarState
    {
        public int LastClaimDay { get; set; } = -1;

        /// <summary>Next calendar slot (0-6). Advances each claimed day, never resets (no punishment for missing a day).</summary>
        public int NextSlot { get; set; }

        public int TotalLoginDays { get; set; }
    }

    public static class LoginCalendar
    {
        public static bool CanClaim(LoginCalendarState state, DateTime utcNow) => state.LastClaimDay != TimeUtil.DayIndex(utcNow);

        public static OperationResult<RewardData> Claim(LoginCalendarState state, DateTime utcNow, LiveOpsBalance balance)
        {
            if (!CanClaim(state, utcNow))
            {
                return OperationResult<RewardData>.Fail(ErrorCode.AlreadyClaimed);
            }
            int value = balance.LoginCalendar[state.NextSlot % balance.LoginCalendar.Length];
            state.LastClaimDay = TimeUtil.DayIndex(utcNow);
            state.NextSlot = (state.NextSlot + 1) % balance.LoginCalendar.Length;
            state.TotalLoginDays++;
            return OperationResult<RewardData>.Ok(value >= 0 ? RewardData.FromCurrency(value) : RewardData.FromCurrency(0, -value));
        }
    }

    // ======================================================================= Ads

    public sealed class AdState
    {
        public int Day { get; set; } = -1;

        public int InterstitialsToday { get; set; }

        public int RewardedToday { get; set; }

        public int VipSkipsToday { get; set; }

        public long LastInterstitialUnixMs { get; set; }

        public int StoryWinsSinceAd { get; set; }

        public int PvpBattlesSinceAd { get; set; }
    }

    public enum AdPlacement : byte
    {
        StoryWin = 0,
        PvpBattle = 1
    }

    /// <summary>
    /// Ad frequency (GDD): story every 2 wins from stage 10, PvP every 3 battles, never before the unlock stage,
    /// with a minimum interval and a daily cap. Remove Ads removes interstitials; rewarded ads stay opt-in.
    /// </summary>
    public static class AdPolicy
    {
        public static void RollDay(AdState state, DateTime utcNow)
        {
            int day = TimeUtil.DayIndex(utcNow);
            if (state.Day != day)
            {
                state.Day = day;
                state.InterstitialsToday = 0;
                state.RewardedToday = 0;
                state.VipSkipsToday = 0;
            }
        }

        /// <summary>Registers the event and returns true if an interstitial should be shown now.</summary>
        public static bool OnEvent(AdState state, AdPlacement placement, DateTime utcNow, int highestUnlockedStage, bool adsRemoved, VipBenefit vip, GameBalance balance)
        {
            RollDay(state, utcNow);
            LiveOpsBalance live = balance.LiveOps;
            if (placement == AdPlacement.StoryWin)
            {
                state.StoryWinsSinceAd++;
            }
            else
            {
                state.PvpBattlesSinceAd++;
            }

            if (adsRemoved || highestUnlockedStage <= balance.Story.UnlockAdsStage)
            {
                return false;
            }

            bool due = placement == AdPlacement.StoryWin
                ? state.StoryWinsSinceAd >= live.StoryInterstitialEveryWins
                : state.PvpBattlesSinceAd >= live.PvpInterstitialEveryBattles;
            if (!due)
            {
                return false;
            }

            long now = TimeUtil.ToUnixMs(utcNow);
            if (state.InterstitialsToday >= live.InterstitialDailyCap || now - state.LastInterstitialUnixMs < live.InterstitialMinIntervalSeconds * 1000L)
            {
                return false;
            }

            if (placement == AdPlacement.StoryWin)
            {
                state.StoryWinsSinceAd = 0;
            }
            else
            {
                state.PvpBattlesSinceAd = 0;
            }

            if (vip != null && vip.SkipAdDaily && state.VipSkipsToday < 1)
            {
                state.VipSkipsToday++;
                return false;
            }

            state.InterstitialsToday++;
            state.LastInterstitialUnixMs = now;
            return true;
        }

        public static bool CanWatchRewarded(AdState state, DateTime utcNow, LiveOpsBalance balance)
        {
            RollDay(state, utcNow);
            return state.RewardedToday < balance.RewardedAdDailyCap;
        }

        public static bool RegisterRewarded(AdState state, DateTime utcNow, LiveOpsBalance balance)
        {
            if (!CanWatchRewarded(state, utcNow, balance))
            {
                return false;
            }
            state.RewardedToday++;
            return true;
        }
    }
}
