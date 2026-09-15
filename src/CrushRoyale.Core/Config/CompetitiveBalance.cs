namespace CrushRoyale.Core.Config
{
    /// <summary>Trophy formulas (GDD "Trophy Calculation").</summary>
    public sealed class TrophyBalance
    {
        /// <summary>Lower bound of each league, indexed by <see cref="League"/>.</summary>
        public int[] LeagueThresholds { get; set; } = { 0, 300, 700, 1200, 1800, 2500 };

        /// <summary>Opponents within max(10%, 50 trophies) count as "equal" (the GDD's "≈").</summary>
        public int EvenThresholdPercent { get; set; } = 10;

        public int EvenThresholdMinTrophies { get; set; } = 50;

        public int WinVsStrongerBase { get; set; } = 40;

        /// <summary>GDD "+ X * 0.5" where X is the percentage gap (500 permille = 0.5).</summary>
        public int WinVsStrongerPerPercentPermille { get; set; } = 500;

        /// <summary>Hard cap so a Bronze beating a Master can't jump three leagues.</summary>
        public int MaxWinGain { get; set; } = 80;

        public int WinEven { get; set; } = 25;

        public int WinVsWeaker { get; set; } = 10;

        public int StreakBonusPerWin { get; set; } = 5;

        public int StreakBonusMax { get; set; } = 20;

        public int LossVsStronger { get; set; } = 5;

        public int LossEven { get; set; } = 15;

        public int LossVsWeaker { get; set; } = 35;

        /// <summary>GDD matchmaking: "If trophy_diff > 200: give extra points" (x1.25).</summary>
        public int LargeGapTrophies { get; set; } = 200;

        public int LargeGapGainMultiplierPermille { get; set; } = 1250;

        /// <summary>
        /// GDD "If trophy_diff &lt; 50: reduce points (anti-boosting)". Interpreted as diminishing returns when
        /// the SAME two players meet repeatedly within the window (each repeat halves the gain).
        /// </summary>
        public int RepeatOpponentWindowHours { get; set; } = 24;

        public int RepeatOpponentDecayPermille { get; set; } = 500;

        public SeasonResetPolicy ResetPolicy { get; set; } = SeasonResetPolicy.Soft;

        /// <summary>Soft reset: trophies above this value are halved.</summary>
        public int SoftResetFloor { get; set; } = 300;

        public int TopRankRewardCount { get; set; } = 100;

        /// <summary>End-of-week reward per final league (coins).</summary>
        public int[] SeasonCoinsByLeague { get; set; } = { 200, 500, 1000, 2000, 3500, 6000 };

        /// <summary>End-of-week reward per final league (orbes).</summary>
        public int[] SeasonOrbesByLeague { get; set; } = { 0, 5, 10, 20, 35, 60 };

        /// <summary>Extra orbes for top-100 of a league: rank 1, ranks 2-10, ranks 11-100.</summary>
        public int[] TopRankOrbes { get; set; } = { 100, 50, 20 };

        /// <summary>One-time orbes the first time a league is reached (GDD "PvP rank-up bonus").</summary>
        public int[] FirstReachOrbesByLeague { get; set; } = { 0, 10, 20, 30, 50, 100 };

        internal void Validate()
        {
            GameBalance.Require(LeagueThresholds != null && LeagueThresholds.Length == 6, "Trophies.LeagueThresholds needs 6 values");
            for (int i = 1; i < LeagueThresholds.Length; i++)
            {
                GameBalance.Require(LeagueThresholds[i] > LeagueThresholds[i - 1], "Trophies.LeagueThresholds must increase");
            }
            GameBalance.Require(SeasonCoinsByLeague.Length == 6 && SeasonOrbesByLeague.Length == 6 && FirstReachOrbesByLeague.Length == 6, "Trophies reward tables need 6 values");
            GameBalance.Require(TopRankOrbes.Length == 3, "Trophies.TopRankOrbes needs 3 values");
        }
    }

    /// <summary>Matchmaking (Task 7). Ranges widen while waiting, never beyond MaxRange (the prompt's 300).</summary>
    public sealed class MatchmakingBalance
    {
        public int InitialRange { get; set; } = 100;

        public int RangeStep { get; set; } = 50;

        public int RangeStepIntervalMs { get; set; } = 5000;

        public int MaxRange { get; set; } = 300;

        /// <summary>After this wait with no live opponent, a recorded ghost in range is used.</summary>
        public int GhostFallbackAfterMs { get; set; } = 8000;

        public int TimeoutMs { get; set; } = 30000;

        public int NoRematchWindowMs { get; set; } = 300000;

        public int MaxSameOpponentPerHour { get; set; } = 3;

        /// <summary>Ghosts older than this are not offered.</summary>
        public int GhostMaxAgeHours { get; set; } = 72;

        /// <summary>Win streak shifts the target upward: +10 trophies per streak win, capped.</summary>
        public int StreakTargetShiftPerWin { get; set; } = 10;

        public int StreakTargetShiftMax { get; set; } = 50;
    }

    /// <summary>Anti-cheat thresholds (Task 14).</summary>
    public sealed class AntiCheatBalance
    {
        public int WinRateFlagPermille { get; set; } = 950;

        public int WinRateMinGames { get; set; } = 50;

        public int SameOpponentWinRateFlagPermille { get; set; } = 850;

        public int SameOpponentMinGames { get; set; } = 10;

        public int DailyTrophyGainFlag { get; set; } = 200;

        /// <summary>"Consistently": number of consecutive days above the gain threshold.</summary>
        public int DailyTrophyGainConsecutiveDays { get; set; } = 3;

        /// <summary>
        /// The prompt says "scores > 1000x average": unreachable in practice. A score above 6x the player's
        /// median (with enough history) is flagged for review instead.
        /// </summary>
        public int ScoreOutlierMedianMultiple { get; set; } = 6;

        public int ScoreOutlierMinHistory { get; set; } = 20;

        public int MaxAccountsPerDevice { get; set; } = 3;

        public int PvpWinStreakFlag { get; set; } = 50;

        public int FirstOffenseSuspensionHours { get; set; } = 24;

        public int SecondOffenseSuspensionHours { get; set; } = 168;

        public int SecondOffenseTrophyResetPermille { get; set; } = 500;

        /// <summary>Soft action for boosting suspicion: forced pause before the next ranked match.</summary>
        public int BoostingCooldownMinutes { get; set; } = 10;
    }
}
