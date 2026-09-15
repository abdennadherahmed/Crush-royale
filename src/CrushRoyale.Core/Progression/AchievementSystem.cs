using System;
using System.Collections.Generic;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Economy;
using CrushRoyale.Core.Gameplay;
using CrushRoyale.Core.Story;

namespace CrushRoyale.Core.Progression
{
    public enum AchievementType : byte
    {
        Story = 0,
        Gameplay = 1,
        Collection = 2
    }

    /// <summary>Tracked statistics. Values are persisted by name: append only.</summary>
    public enum StatKey
    {
        HighestStage,
        StagesWon,
        ThreeStarStages,
        TotalStars,
        BossesDefeated,
        CharactersMet,
        ChoicesMade,
        EndingsReached,
        CascadesTriggered,
        MegaCascades,
        RedSurges,
        MaxCascadeLevel,
        SpecialsCreated,
        SpecialsActivated,
        Line5Matches,
        StonesDestroyed,
        IceBroken,
        PvpMatches,
        PvpWins,
        BestWinStreak,
        HighestLeague,
        MaxTrophies,
        FriendsCount,
        GuildsJoined,
        GuildBossAttacks,
        GuildDonations,
        GuildBossesDefeated,
        FriendlyChallenges,
        PowerUpTypesUsed,
        PowerUpsUsed,
        NuclearBombsUsed,
        ChronoBombsUsed,
        FreezingGelsUsed,
        CosmeticsOwned,
        LoginDays,
        QuestsCompleted,
        BattlePassTiers,
        CoinsEarned,
        OrbesEarned
    }

    public sealed class AchievementData
    {
        public string Id { get; set; }

        public AchievementType Type { get; set; }

        public StatKey Stat { get; set; }

        public long Target { get; set; }

        /// <summary>1-based collection book page.</summary>
        public int Page { get; set; }

        public string TitleKey => "ach." + Id + ".title";

        public string DescriptionKey => "ach." + Id + ".desc";

        public RewardData Reward { get; set; } = new RewardData();
    }

    /// <summary>60 achievements on 12 pages of 5 (GDD: pages of 5-10, +5% coins per completed page).</summary>
    public static class AchievementCatalog
    {
        public const int PageSize = 5;

        public static readonly IReadOnlyList<AchievementData> All = Build();

        public static int PageCount => (All.Count + PageSize - 1) / PageSize;

        public static AchievementData Get(string id)
        {
            foreach (AchievementData a in All)
            {
                if (a.Id == id)
                {
                    return a;
                }
            }
            return null;
        }

        public static List<AchievementData> GetPage(int page)
        {
            var list = new List<AchievementData>();
            foreach (AchievementData a in All)
            {
                if (a.Page == page)
                {
                    list.Add(a);
                }
            }
            return list;
        }

        private static List<AchievementData> Build()
        {
            var pages = new List<(AchievementType type, StatKey stat, long target, string id)[]>
            {
                new[] { (AchievementType.Story, StatKey.HighestStage, 11L, "first_steps"), (AchievementType.Story, StatKey.ThreeStarStages, 5L, "perfectionist_1"), (AchievementType.Gameplay, StatKey.CascadesTriggered, 10L, "cascade_1"), (AchievementType.Gameplay, StatKey.SpecialsCreated, 5L, "bonus_1"), (AchievementType.Story, StatKey.BossesDefeated, 1L, "first_boss") },
                new[] { (AchievementType.Story, StatKey.HighestStage, 101L, "stage_100"), (AchievementType.Story, StatKey.BossesDefeated, 10L, "boss_10"), (AchievementType.Story, StatKey.HighestStage, 201L, "act_1"), (AchievementType.Story, StatKey.CharactersMet, 3L, "companions_3"), (AchievementType.Story, StatKey.TotalStars, 100L, "stars_100") },
                new[] { (AchievementType.Gameplay, StatKey.CascadesTriggered, 50L, "cascade_50"), (AchievementType.Gameplay, StatKey.CascadesTriggered, 500L, "cascade_500"), (AchievementType.Gameplay, StatKey.MegaCascades, 10L, "mega_10"), (AchievementType.Gameplay, StatKey.RedSurges, 25L, "surge_25"), (AchievementType.Gameplay, StatKey.MaxCascadeLevel, 6L, "chain_6") },
                new[] { (AchievementType.Gameplay, StatKey.PvpMatches, 10L, "pvp_10"), (AchievementType.Gameplay, StatKey.PvpWins, 10L, "pvp_wins_10"), (AchievementType.Gameplay, StatKey.PvpWins, 100L, "pvp_wins_100"), (AchievementType.Gameplay, StatKey.BestWinStreak, 5L, "streak_5"), (AchievementType.Gameplay, StatKey.HighestLeague, (long)League.Silver, "league_silver") },
                new[] { (AchievementType.Gameplay, StatKey.HighestLeague, (long)League.Gold, "league_gold"), (AchievementType.Gameplay, StatKey.HighestLeague, (long)League.Platinum, "league_platinum"), (AchievementType.Gameplay, StatKey.HighestLeague, (long)League.Diamond, "league_diamond"), (AchievementType.Gameplay, StatKey.HighestLeague, (long)League.Master, "league_master"), (AchievementType.Gameplay, StatKey.MaxTrophies, 3000L, "trophies_3000") },
                new[] { (AchievementType.Gameplay, StatKey.Line5Matches, 20L, "line5_20"), (AchievementType.Gameplay, StatKey.SpecialsCreated, 500L, "bonus_500"), (AchievementType.Gameplay, StatKey.SpecialsActivated, 1000L, "blast_1000"), (AchievementType.Gameplay, StatKey.StonesDestroyed, 500L, "stones_500"), (AchievementType.Gameplay, StatKey.IceBroken, 500L, "ice_500") },
                new[] { (AchievementType.Story, StatKey.HighestStage, 501L, "stage_500"), (AchievementType.Story, StatKey.BossesDefeated, 50L, "boss_50"), (AchievementType.Story, StatKey.HighestStage, 601L, "act_3"), (AchievementType.Story, StatKey.TotalStars, 1000L, "stars_1000"), (AchievementType.Story, StatKey.ChoicesMade, 3L, "choices_3") },
                new[] { (AchievementType.Collection, StatKey.FriendsCount, 5L, "friends_5"), (AchievementType.Collection, StatKey.GuildsJoined, 1L, "guild_join"), (AchievementType.Gameplay, StatKey.GuildBossAttacks, 20L, "guild_boss_20"), (AchievementType.Collection, StatKey.GuildDonations, 5L, "donations_5"), (AchievementType.Gameplay, StatKey.FriendlyChallenges, 10L, "challenges_10") },
                new[] { (AchievementType.Collection, StatKey.PowerUpTypesUsed, 9L, "powerup_set"), (AchievementType.Gameplay, StatKey.PowerUpsUsed, 100L, "powerups_100"), (AchievementType.Gameplay, StatKey.NuclearBombsUsed, 10L, "nuclear_10"), (AchievementType.Gameplay, StatKey.ChronoBombsUsed, 25L, "chrono_25"), (AchievementType.Gameplay, StatKey.FreezingGelsUsed, 10L, "gel_10") },
                new[] { (AchievementType.Collection, StatKey.CosmeticsOwned, 10L, "cosmetics_10"), (AchievementType.Collection, StatKey.CosmeticsOwned, 25L, "cosmetics_25"), (AchievementType.Collection, StatKey.LoginDays, 30L, "login_30"), (AchievementType.Collection, StatKey.QuestsCompleted, 50L, "quests_50"), (AchievementType.Collection, StatKey.BattlePassTiers, 50L, "pass_50") },
                new[] { (AchievementType.Story, StatKey.HighestStage, 1001L, "stage_1000"), (AchievementType.Story, StatKey.EndingsReached, 1L, "valdorax"), (AchievementType.Story, StatKey.EndingsReached, 3L, "all_endings"), (AchievementType.Story, StatKey.HighestStage, 1101L, "endless_100"), (AchievementType.Story, StatKey.ThreeStarStages, 900L, "perfectionist_900") },
                new[] { (AchievementType.Collection, StatKey.CoinsEarned, 1000000L, "millionaire"), (AchievementType.Collection, StatKey.OrbesEarned, 5000L, "orbe_hoarder"), (AchievementType.Gameplay, StatKey.PvpWins, 1000L, "pvp_wins_1000"), (AchievementType.Gameplay, StatKey.CascadesTriggered, 10000L, "cascade_10000"), (AchievementType.Gameplay, StatKey.GuildBossesDefeated, 10L, "guild_slayer") }
            };

            var list = new List<AchievementData>();
            for (int p = 0; p < pages.Count; p++)
            {
                int page = p + 1;
                foreach (var (type, stat, target, id) in pages[p])
                {
                    var reward = RewardData.FromCurrency(100 * page, page >= 5 ? 5 * (page / 2) : 0);
                    if (id == "cascade_10000")
                    {
                        reward.Cosmetics.Add("title.cascade_master");
                    }
                    if (id == "valdorax")
                    {
                        reward.Cosmetics.Add("title.dragon_slayer");
                    }
                    if (id == "pvp_wins_1000")
                    {
                        reward.Cosmetics.Add("title.legend");
                    }
                    list.Add(new AchievementData { Id = id, Type = type, Stat = stat, Target = target, Page = page, Reward = reward });
                }
            }
            return list;
        }
    }

    /// <summary>Serializable achievements state (achievement_progress.json).</summary>
    public sealed class AchievementProgressState
    {
        public Dictionary<StatKey, long> Stats { get; set; } = new Dictionary<StatKey, long>();

        /// <summary>id -> unlock time (unix ms).</summary>
        public Dictionary<string, long> UnlockedAt { get; set; } = new Dictionary<string, long>();

        public HashSet<string> Claimed { get; set; } = new HashSet<string>();

        public HashSet<int> PagesCompleted { get; set; } = new HashSet<int>();

        /// <summary>Bit set of power-up types ever used.</summary>
        public int PowerUpTypesMask { get; set; }
    }

    /// <summary>Task 9: achievements, collection book and the permanent per-page coin bonus.</summary>
    public sealed class AchievementSystem
    {
        private readonly IClock _clock;

        public AchievementSystem(AchievementProgressState state, IClock clock)
        {
            State = state ?? throw new ArgumentNullException(nameof(state));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public event Action<string> OnAchievementUnlocked;

        public event Action<int> OnPageCompleted;

        public AchievementProgressState State { get; }

        public long GetStat(StatKey key) => State.Stats.TryGetValue(key, out long v) ? v : 0;

        public void IncrementStat(StatKey key, long amount)
        {
            if (amount <= 0)
            {
                return;
            }
            State.Stats[key] = GetStat(key) + amount;
            Evaluate(key);
        }

        /// <summary>For "best ever" stats (highest stage, league, trophies...).</summary>
        public void SetStatMax(StatKey key, long value)
        {
            if (value <= GetStat(key))
            {
                return;
            }
            State.Stats[key] = value;
            Evaluate(key);
        }

        public bool IsUnlocked(string id) => State.UnlockedAt.ContainsKey(id);

        public bool IsClaimed(string id) => State.Claimed.Contains(id);

        /// <summary>Prompt API: evaluates one achievement, unlocking it if its target is reached.</summary>
        public bool CheckAchievementUnlock(string achievementId)
        {
            AchievementData a = AchievementCatalog.Get(achievementId);
            if (a == null)
            {
                return false;
            }
            if (IsUnlocked(a.Id))
            {
                return true;
            }
            if (GetStat(a.Stat) < a.Target)
            {
                return false;
            }
            State.UnlockedAt[a.Id] = TimeUtil.ToUnixMs(_clock.UtcNow);
            OnAchievementUnlocked?.Invoke(a.Id);
            return true;
        }

        /// <summary>0-1 progress for multi-step achievements.</summary>
        public float GetProgress(string achievementId)
        {
            AchievementData a = AchievementCatalog.Get(achievementId);
            if (a == null)
            {
                return 0f;
            }
            return Math.Min(1f, GetStat(a.Stat) / (float)a.Target);
        }

        public RewardData PreviewReward(string achievementId) => AchievementCatalog.Get(achievementId)?.Reward;

        /// <summary>
        /// Prompt API: claims an unlocked achievement and returns its reward (null if locked or already claimed).
        /// Completing a page adds the page frame to the returned reward.
        /// </summary>
        public RewardData UnlockAchievement(string achievementId)
        {
            AchievementData a = AchievementCatalog.Get(achievementId);
            if (a == null || !CheckAchievementUnlock(a.Id) || IsClaimed(a.Id))
            {
                return null;
            }

            State.Claimed.Add(a.Id);
            var reward = new RewardData().Add(a.Reward);
            if (!State.PagesCompleted.Contains(a.Page) && GetPageCompletion(a.Page) >= 1f)
            {
                State.PagesCompleted.Add(a.Page);
                reward.Cosmetics.Add(CosmeticCatalog.PageFrame(a.Page));
                OnPageCompleted?.Invoke(a.Page);
            }
            return reward;
        }

        /// <summary>Prompt API: fraction of the page's achievements claimed.</summary>
        public float GetPageCompletion(int pageNum)
        {
            List<AchievementData> page = AchievementCatalog.GetPage(pageNum);
            if (page.Count == 0)
            {
                return 0f;
            }
            int claimed = 0;
            foreach (AchievementData a in page)
            {
                if (IsClaimed(a.Id))
                {
                    claimed++;
                }
            }
            return claimed / (float)page.Count;
        }

        /// <summary>Prompt API: total permanent coin bonus from completed pages (0.05 per page).</summary>
        public float GetTotalCoinBonus() => State.PagesCompleted.Count * RewardCalculator.CollectionPageBonusPermille / 1000f;

        public List<string> GetUnclaimed()
        {
            var list = new List<string>();
            foreach (AchievementData a in AchievementCatalog.All)
            {
                if (IsUnlocked(a.Id) && !IsClaimed(a.Id))
                {
                    list.Add(a.Id);
                }
            }
            return list;
        }

        // -------------------------------------------------------------- event feeders

        public void RecordStageResult(StageResult result, StageData stage, StoryProgress progress)
        {
            if (result == null)
            {
                throw new ArgumentNullException(nameof(result));
            }
            RecordMatchStats(result);
            if (result.Won)
            {
                IncrementStat(StatKey.StagesWon, 1);
                if (stage != null && stage.IsBoss)
                {
                    IncrementStat(StatKey.BossesDefeated, 1);
                }
            }
            if (progress != null)
            {
                SetStatMax(StatKey.HighestStage, progress.HighestUnlockedStage);
                SetStatMax(StatKey.TotalStars, progress.TotalStars);
                int threeStars = 0;
                foreach (StageProgress p in progress.Stages.Values)
                {
                    if (p.BestStars >= 3)
                    {
                        threeStars++;
                    }
                }
                SetStatMax(StatKey.ThreeStarStages, threeStars);
                SetStatMax(StatKey.ChoicesMade, progress.Choices.Count);
            }
        }

        public void RecordPvpResult(StageResult result, bool won, int trophies, League highestLeague, int bestWinStreak)
        {
            if (result != null)
            {
                RecordMatchStats(result);
            }
            IncrementStat(StatKey.PvpMatches, 1);
            if (won)
            {
                IncrementStat(StatKey.PvpWins, 1);
            }
            SetStatMax(StatKey.MaxTrophies, trophies);
            SetStatMax(StatKey.HighestLeague, (long)highestLeague);
            SetStatMax(StatKey.BestWinStreak, bestWinStreak);
        }

        public void RecordEnding(int distinctEndingsReached) => SetStatMax(StatKey.EndingsReached, distinctEndingsReached);

        private void RecordMatchStats(StageResult r)
        {
            IncrementStat(StatKey.CascadesTriggered, r.TotalCascades);
            IncrementStat(StatKey.MegaCascades, r.MegaCascades);
            IncrementStat(StatKey.RedSurges, r.RedSurgeActivations);
            SetStatMax(StatKey.MaxCascadeLevel, r.MaxCascadeLevel);
            IncrementStat(StatKey.SpecialsCreated, r.SpecialsCreated);
            IncrementStat(StatKey.SpecialsActivated, r.SpecialsActivated);
            IncrementStat(StatKey.Line5Matches, r.Line5Matches);
            IncrementStat(StatKey.StonesDestroyed, r.StonesDestroyed);
            IncrementStat(StatKey.IceBroken, r.IceBroken);

            foreach (KeyValuePair<PowerUpType, int> kv in r.PowerUpsUsed)
            {
                IncrementStat(StatKey.PowerUpsUsed, kv.Value);
                State.PowerUpTypesMask |= 1 << (int)kv.Key;
                switch (kv.Key)
                {
                    case PowerUpType.NuclearBomb: IncrementStat(StatKey.NuclearBombsUsed, kv.Value); break;
                    case PowerUpType.ChronoBomb: IncrementStat(StatKey.ChronoBombsUsed, kv.Value); break;
                    case PowerUpType.FreezingGel: IncrementStat(StatKey.FreezingGelsUsed, kv.Value); break;
                }
            }
            SetStatMax(StatKey.PowerUpTypesUsed, CountBits(State.PowerUpTypesMask));
        }

        private void Evaluate(StatKey key)
        {
            foreach (AchievementData a in AchievementCatalog.All)
            {
                if (a.Stat == key && !IsUnlocked(a.Id))
                {
                    CheckAchievementUnlock(a.Id);
                }
            }
        }

        private static int CountBits(int v)
        {
            int n = 0;
            while (v != 0)
            {
                n += v & 1;
                v >>= 1;
            }
            return n;
        }
    }
}
