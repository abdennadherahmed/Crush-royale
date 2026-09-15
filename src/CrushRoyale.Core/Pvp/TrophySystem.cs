using System;
using System.Collections.Generic;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;

namespace CrushRoyale.Core.Pvp
{
    public enum MatchOutcome : byte
    {
        Win = 0,
        Loss = 1,
        Draw = 2
    }

    /// <summary>League thresholds (Bronze 0-299 ... Master 2500+).</summary>
    public static class LeagueTable
    {
        public static League GetLeague(int trophies, TrophyBalance balance)
        {
            if (balance == null)
            {
                throw new ArgumentNullException(nameof(balance));
            }
            int[] t = balance.LeagueThresholds;
            for (int i = t.Length - 1; i >= 0; i--)
            {
                if (trophies >= t[i])
                {
                    return (League)i;
                }
            }
            return League.Bronze;
        }

        public static string GetLeagueName(int trophies, TrophyBalance balance) => GetLeague(trophies, balance).ToString();

        public static int LowerBound(League league, TrophyBalance balance) => balance.LeagueThresholds[(int)league];

        /// <summary>Inclusive upper bound, or null for Master.</summary>
        public static int? UpperBound(League league, TrophyBalance balance)
        {
            int next = (int)league + 1;
            return next < balance.LeagueThresholds.Length ? balance.LeagueThresholds[next] - 1 : (int?)null;
        }
    }

    public sealed class TrophyChange
    {
        public int Before { get; internal set; }

        public int After { get; internal set; }

        public int Delta => After - Before;

        public int BaseDelta { get; internal set; }

        public int StreakBonus { get; internal set; }

        public bool LargeGapBonus { get; internal set; }

        /// <summary>How many recent games vs the same opponent reduced the gain (anti-boosting).</summary>
        public int RepeatGamesPenalized { get; internal set; }

        public League LeagueBefore { get; internal set; }

        public League LeagueAfter { get; internal set; }

        public bool Promoted => LeagueAfter > LeagueBefore;

        public bool Demoted => LeagueAfter < LeagueBefore;

        /// <summary>Orbes for reaching a league for the first time ever.</summary>
        public int FirstReachOrbes { get; internal set; }
    }

    /// <summary>
    /// Task 5 trophy formulas (pure functions).
    ///  Win:  vs stronger (beyond the "≈" band) 40 + gap% * 0.5 (x1.25 if gap > 200, capped); vs equal +25; vs weaker +10;
    ///        + 5 per current streak win (max +20); halved per recent game against the same opponent.
    ///  Loss: vs stronger -5; vs equal -15; vs weaker -35. Never below 0.
    /// </summary>
    public static class TrophyCalculator
    {
        /// <summary>Half-width of the "equal opponent" band for a player.</summary>
        public static int EvenThreshold(TrophyBalance b, int playerTrophies) =>
            Math.Max(b.EvenThresholdMinTrophies, Math.Max(0, playerTrophies) * b.EvenThresholdPercent / 100);

        /// <summary>Prompt API: signed trophy delta without streak or repeat-opponent context.</summary>
        public static int CalculateTrophyGain(TrophyBalance balance, int playerTrophies, int opponentTrophies, bool won) =>
            Calculate(balance, playerTrophies, opponentTrophies, won ? MatchOutcome.Win : MatchOutcome.Loss).Delta;

        public static TrophyChange Calculate(TrophyBalance b, int playerTrophies, int opponentTrophies, MatchOutcome outcome, int winStreakBefore = 0, int recentGamesVsOpponent = 0)
        {
            if (b == null)
            {
                throw new ArgumentNullException(nameof(b));
            }

            int player = Math.Max(0, playerTrophies);
            int opponent = Math.Max(0, opponentTrophies);
            int diff = opponent - player;
            int threshold = EvenThreshold(b, player);
            var change = new TrophyChange { Before = player, LeagueBefore = LeagueTable.GetLeague(player, b) };

            int delta = 0;
            if (outcome == MatchOutcome.Win)
            {
                int baseGain;
                if (diff > threshold)
                {
                    long percent = (long)diff * 100 / Math.Max(player, 100);
                    long gain = b.WinVsStrongerBase + percent * b.WinVsStrongerPerPercentPermille / 1000;
                    if (diff > b.LargeGapTrophies)
                    {
                        gain = gain * b.LargeGapGainMultiplierPermille / 1000;
                        change.LargeGapBonus = true;
                    }
                    baseGain = (int)Math.Min(b.MaxWinGain, gain);
                }
                else if (diff >= -threshold)
                {
                    baseGain = b.WinEven;
                }
                else
                {
                    baseGain = b.WinVsWeaker;
                }

                change.BaseDelta = baseGain;
                change.StreakBonus = Math.Min(b.StreakBonusMax, Math.Max(0, winStreakBefore) * b.StreakBonusPerWin);
                long total = baseGain + change.StreakBonus;
                int repeats = Math.Max(0, recentGamesVsOpponent);
                for (int i = 0; i < repeats; i++)
                {
                    total = total * b.RepeatOpponentDecayPermille / 1000;
                }
                change.RepeatGamesPenalized = repeats;
                delta = (int)Math.Max(1, total);
            }
            else if (outcome == MatchOutcome.Loss)
            {
                int loss;
                if (diff > threshold)
                {
                    loss = b.LossVsStronger;
                }
                else if (diff >= -threshold)
                {
                    loss = b.LossEven;
                }
                else
                {
                    loss = b.LossVsWeaker;
                }
                change.BaseDelta = -loss;
                delta = -Math.Min(player, loss);
            }

            change.After = player + delta;
            change.LeagueAfter = LeagueTable.GetLeague(change.After, b);
            return change;
        }
    }

    public sealed class TrophyHistoryEntry
    {
        public long TimestampUnixMs { get; set; }

        public string MatchId { get; set; }

        public string OpponentId { get; set; }

        public int OpponentTrophies { get; set; }

        public MatchOutcome Outcome { get; set; }

        public int Delta { get; set; }

        public int TrophiesAfter { get; set; }

        public int Week { get; set; }
    }

    /// <summary>Persisted PvP state of one player (PlayerTrophyHistory).</summary>
    public sealed class PlayerTrophyRecord
    {
        public const int MaxHistory = 200;

        public string PlayerId { get; set; }

        public int Trophies { get; set; }

        public League HighestLeague { get; set; }

        public int CurrentWinStreak { get; set; }

        public int BestWinStreak { get; set; }

        public int Wins { get; set; }

        public int Losses { get; set; }

        public int Draws { get; set; }

        public int SeasonWeek { get; set; }

        /// <summary>Best trophies this season (for "top of the week" displays).</summary>
        public int SeasonBestTrophies { get; set; }

        public List<TrophyHistoryEntry> History { get; set; } = new List<TrophyHistoryEntry>();

        public int GamesPlayed => Wins + Losses + Draws;
    }

    /// <summary>Stateful wrapper that applies trophy changes to a player's record.</summary>
    public sealed class TrophySystem
    {
        private readonly GameBalance _balance;
        private readonly IClock _clock;

        public TrophySystem(GameBalance balance, IClock clock)
        {
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        public int CalculateTrophyGain(int playerTrophies, int opponentTrophies, bool won) =>
            TrophyCalculator.CalculateTrophyGain(_balance.Trophies, playerTrophies, opponentTrophies, won);

        public string GetLeagueName(int trophies) => LeagueTable.GetLeagueName(trophies, _balance.Trophies);

        public int GetCurrentWinStreak(PlayerTrophyRecord record) => record?.CurrentWinStreak ?? 0;

        /// <summary>Games vs this opponent inside the repeat window (anti-boosting input).</summary>
        public int CountRecentGamesVs(PlayerTrophyRecord record, string opponentId)
        {
            if (string.IsNullOrEmpty(opponentId))
            {
                return 0;
            }
            long since = TimeUtil.ToUnixMs(_clock.UtcNow) - _balance.Trophies.RepeatOpponentWindowHours * 3600000L;
            int count = 0;
            foreach (TrophyHistoryEntry e in record.History)
            {
                if (e.OpponentId == opponentId && e.TimestampUnixMs >= since)
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>Applies a ranked result: trophies, streaks, highest league, history.</summary>
        public TrophyChange RecordMatch(PlayerTrophyRecord record, string opponentId, int opponentTrophies, MatchOutcome outcome, string matchId)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            int recent = outcome == MatchOutcome.Win ? CountRecentGamesVs(record, opponentId) : 0;
            TrophyChange change = TrophyCalculator.Calculate(_balance.Trophies, record.Trophies, opponentTrophies, outcome, record.CurrentWinStreak, recent);

            record.Trophies = change.After;
            switch (outcome)
            {
                case MatchOutcome.Win:
                    record.Wins++;
                    record.CurrentWinStreak++;
                    record.BestWinStreak = Math.Max(record.BestWinStreak, record.CurrentWinStreak);
                    break;
                case MatchOutcome.Loss:
                    record.Losses++;
                    record.CurrentWinStreak = 0;
                    break;
                default:
                    record.Draws++;
                    break;
            }

            if (change.LeagueAfter > record.HighestLeague)
            {
                int orbes = 0;
                for (int l = (int)record.HighestLeague + 1; l <= (int)change.LeagueAfter; l++)
                {
                    orbes += _balance.Trophies.FirstReachOrbesByLeague[l];
                }
                change.FirstReachOrbes = orbes;
                record.HighestLeague = change.LeagueAfter;
            }
            record.SeasonBestTrophies = Math.Max(record.SeasonBestTrophies, record.Trophies);

            record.History.Add(new TrophyHistoryEntry
            {
                TimestampUnixMs = TimeUtil.ToUnixMs(_clock.UtcNow),
                MatchId = matchId,
                OpponentId = opponentId,
                OpponentTrophies = opponentTrophies,
                Outcome = outcome,
                Delta = change.Delta,
                TrophiesAfter = record.Trophies,
                Week = TimeUtil.WeekIndex(_clock.UtcNow)
            });
            if (record.History.Count > PlayerTrophyRecord.MaxHistory)
            {
                record.History.RemoveRange(0, record.History.Count - PlayerTrophyRecord.MaxHistory);
            }
            return change;
        }
    }

    /// <summary>Coins paid for ranked matches.</summary>
    public static class PvpRewardCalculator
    {
        public static int BaseCoins(PvpBalance balance, MatchOutcome outcome)
        {
            switch (outcome)
            {
                case MatchOutcome.Win: return balance.WinCoins;
                case MatchOutcome.Loss: return balance.LossCoins;
                default: return balance.DrawCoins;
            }
        }
    }
}
