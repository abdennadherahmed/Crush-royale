namespace CrushRoyale.Core.Config
{
    /// <summary>Story structure and stage tuning (Task 8).</summary>
    public sealed class StoryBalance
    {
        public int Acts { get; set; } = 5;

        public int ChaptersPerAct { get; set; } = 10;

        public int StagesPerChapter { get; set; } = 20;

        /// <summary>Stage index inside a chapter (1-based) of the mini-boss.</summary>
        public int MiniBossIndex { get; set; } = 10;

        /// <summary>Stage index inside a chapter (1-based) of the chapter boss.</summary>
        public int FinalBossIndex { get; set; } = 20;

        public int EasyTimeLimitMs { get; set; } = 90000;

        public int HardTimeLimitMs { get; set; } = 60000;

        public int EasyMoveLimit { get; set; } = 30;

        public int HardMoveLimit { get; set; } = 20;

        public int BossExtraMoves { get; set; } = 5;

        public int BossExtraTimeMs { get; set; } = 15000;

        /// <summary>Expected points per move used to derive target scores (validated by the balancing bot tests).</summary>
        public int ExpectedPointsPerMove { get; set; } = 110;

        /// <summary>Target = moves * expected * factor, factor going from Easy to Hard (permille).</summary>
        public int EasyTargetFactorPermille { get; set; } = 450;

        public int HardTargetFactorPermille { get; set; } = 1000;

        /// <summary>2 stars at 150% of target, 3 stars at 200%.</summary>
        public int TwoStarPermille { get; set; } = 1500;

        public int ThreeStarPermille { get; set; } = 2000;

        public int BaseStageCoins { get; set; } = 50;

        public int MaxDifficultyStageCoins { get; set; } = 200;

        public int BossCoinMultiplierPermille { get; set; } = 2000;

        /// <summary>Replaying an already-won stage pays a fraction (prevents coin farming on stage 1).</summary>
        public int ReplayCoinPermille { get; set; } = 250;

        /// <summary>GDD "every 50 story stages +10 orbes".</summary>
        public int OrbeMilestoneInterval { get; set; } = 50;

        public int OrbeMilestoneReward { get; set; } = 10;

        public int UnlockDailyQuestsStage { get; set; } = 5;

        public int UnlockAdsStage { get; set; } = 10;

        public int UnlockShopStage { get; set; } = 15;

        public int UnlockFriendsStage { get; set; } = 20;

        public int UnlockPvpStage { get; set; } = 25;

        public int UnlockBattlePassStage { get; set; } = 30;

        /// <summary>GDD "Level X: Guilds unlocked (TBD)": set to 40 (after Act 1 chapter 2 boss).</summary>
        public int UnlockGuildsStage { get; set; } = 40;

        public int TotalStages => Acts * ChaptersPerAct * StagesPerChapter;

        internal void Validate()
        {
            GameBalance.Require(Acts >= 1 && ChaptersPerAct >= 1 && StagesPerChapter >= 2, "Story structure");
            GameBalance.Require(MiniBossIndex >= 1 && MiniBossIndex < StagesPerChapter, "Story.MiniBossIndex");
            GameBalance.Require(FinalBossIndex == StagesPerChapter, "Story.FinalBossIndex must be the last stage of a chapter");
            GameBalance.Require(HardMoveLimit >= 5 && EasyMoveLimit >= HardMoveLimit, "Story move limits");
            GameBalance.Require(HardTimeLimitMs >= 20000 && EasyTimeLimitMs >= HardTimeLimitMs, "Story time limits");
        }
    }

    /// <summary>Lives (Task 10).</summary>
    public sealed class StaminaBalance
    {
        public int StartingLives { get; set; } = 2;

        /// <summary>Regeneration stops at this count (purchases can exceed it).</summary>
        public int MaxRegenLives { get; set; } = 5;

        public int RechargeMinutes { get; set; } = 30;

        /// <summary>Purchases per UTC day payable in coins ("Lives 1-2: coins").</summary>
        public int CoinPurchasesPerDay { get; set; } = 2;

        public int CoinLifePrice { get; set; } = 100;

        /// <summary>First Orbe-priced life ("Life 3: 100 orbes").</summary>
        public int OrbeLifeBasePrice { get; set; } = 100;

        /// <summary>Each further purchase costs 25% more (the prompt's "-25% malus" is read as +25%, as in the GDD example).</summary>
        public int OrbeEscalationPermille { get; set; } = 1250;

        /// <summary>Bulk: buy 5, pay 4 (per-unit price of the next escalation step).</summary>
        public int BulkCount { get; set; } = 5;

        public int BulkPaidCount { get; set; } = 4;

        /// <summary>Continue after a failed story stage: +moves/+time, priced in orbes and escalating per session.</summary>
        public int ContinueBaseOrbes { get; set; } = 50;

        public int ContinueExtraMoves { get; set; } = 5;

        public int ContinueExtraTimeMs { get; set; } = 15000;

        internal void Validate()
        {
            GameBalance.Require(StartingLives >= 1, "Stamina.StartingLives");
            GameBalance.Require(MaxRegenLives >= StartingLives, "Stamina.MaxRegenLives");
            GameBalance.Require(RechargeMinutes >= 1, "Stamina.RechargeMinutes");
            GameBalance.Require(OrbeEscalationPermille >= 1000, "Stamina.OrbeEscalationPermille");
            GameBalance.Require(BulkPaidCount >= 1 && BulkPaidCount <= BulkCount, "Stamina bulk");
        }
    }
}
