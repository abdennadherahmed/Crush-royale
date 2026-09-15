using System;
using System.Collections.Generic;
using System.Linq;
using CrushRoyale.Core.Board;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Gameplay;
using CrushRoyale.Core.Replay;

namespace CrushRoyale.Core.AntiCheat
{
    public enum CheatSeverity : byte
    {
        Info = 0,

        /// <summary>Statistical anomaly: human review, no automatic punishment.</summary>
        Suspicious = 1,

        /// <summary>Proven rule violation (impossible timing, forged timestamps): automatic escalating punishment.</summary>
        Confirmed = 2,

        /// <summary>Forged score / hacked client: immediate permanent ban.</summary>
        Severe = 3
    }

    public enum FlagReason : byte
    {
        ReplayMismatch = 0,
        ImpossibleTiming = 1,
        TimestampTampering = 2,
        WinRate = 3,
        SameOpponentWinRate = 4,
        TrophySpike = 5,
        ScoreOutlier = 6,
        MultiAccountDevice = 7,
        WinStreak = 8,
        InventoryMismatch = 9,
        ReceiptFraud = 10,
        PlayerReport = 11,
        Other = 12
    }

    public sealed class CheatFlag
    {
        public string Id { get; set; }

        public string PlayerId { get; set; }

        public FlagReason Reason { get; set; }

        public CheatSeverity Severity { get; set; }

        public string Details { get; set; }

        public string MatchId { get; set; }

        public long AtUnixMs { get; set; }

        public bool Reviewed { get; set; }

        public string ReviewOutcome { get; set; }
    }

    public interface IFlagStore
    {
        void Add(CheatFlag flag);

        IReadOnlyList<CheatFlag> ForPlayer(string playerId);

        IReadOnlyList<CheatFlag> Pending();
    }

    public sealed class InMemoryFlagStore : IFlagStore
    {
        private readonly List<CheatFlag> _flags = new List<CheatFlag>();
        private readonly object _lock = new object();

        public void Add(CheatFlag flag)
        {
            lock (_lock)
            {
                _flags.Add(flag);
            }
        }

        public IReadOnlyList<CheatFlag> ForPlayer(string playerId)
        {
            lock (_lock)
            {
                return _flags.Where(f => f.PlayerId == playerId).ToList();
            }
        }

        public IReadOnlyList<CheatFlag> Pending()
        {
            lock (_lock)
            {
                return _flags.Where(f => !f.Reviewed).ToList();
            }
        }
    }

    public sealed class MatchSummary
    {
        public string MatchId { get; set; }

        public string OpponentId { get; set; }

        public bool Won { get; set; }

        public int TrophyDelta { get; set; }

        public long Score { get; set; }

        public long AtUnixMs { get; set; }
    }

    /// <summary>Persisted integrity state of a player.</summary>
    public sealed class PlayerIntegrityRecord
    {
        public const int MaxRecentMatches = 100;
        public const int MaxRecentScores = 50;

        public string PlayerId { get; set; }

        public int OffenseCount { get; set; }

        public long SuspendedUntilUnixMs { get; set; }

        public bool PermanentlyBanned { get; set; }

        public long TrophiesFrozenUntilUnixMs { get; set; }

        public long BoostingCooldownUntilUnixMs { get; set; }

        public List<MatchSummary> RecentMatches { get; set; } = new List<MatchSummary>();

        /// <summary>UTC day index -> trophies gained that day.</summary>
        public Dictionary<int, int> DailyTrophyGain { get; set; } = new Dictionary<int, int>();

        public List<long> RecentScores { get; set; } = new List<long>();
    }

    public enum PunishmentAction : byte
    {
        None = 0,
        Warning = 1,
        TemporaryBan = 2,
        PermanentBan = 3
    }

    public sealed class PunishmentDecision
    {
        public PunishmentAction Action { get; internal set; }

        public int OffenseNumber { get; internal set; }

        public long SuspendedUntilUnixMs { get; internal set; }

        public bool FreezeTrophies { get; internal set; }

        /// <summary>Share of trophies removed (500 = 50%).</summary>
        public int TrophyResetPermille { get; internal set; }
    }

    /// <summary>Accounts seen per device fingerprint (hashed on the client; never raw hardware ids).</summary>
    public sealed class DeviceRegistry
    {
        public Dictionary<string, HashSet<string>> AccountsByDevice { get; set; } = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        public int Register(string deviceHash, string playerId)
        {
            if (string.IsNullOrEmpty(deviceHash) || string.IsNullOrEmpty(playerId))
            {
                throw new ArgumentException("Device hash and player id required.");
            }
            if (!AccountsByDevice.TryGetValue(deviceHash, out HashSet<string> accounts))
            {
                accounts = new HashSet<string>(StringComparer.Ordinal);
                AccountsByDevice[deviceHash] = accounts;
            }
            accounts.Add(playerId);
            return accounts.Count;
        }

        public IReadOnlyCollection<string> Accounts(string deviceHash) =>
            deviceHash != null && AccountsByDevice.TryGetValue(deviceHash, out HashSet<string> a) ? a : (IReadOnlyCollection<string>)new string[0];
    }

    /// <summary>
    /// Task 14 (server-side). Four layers:
    ///  1. Replay re-simulation: every move legal, every score recomputed, board checkpoints identical.
    ///  2. Timing: animation-based minimum action interval (in the simulation) + wall-clock duration check.
    ///  3. Behavioral statistics: win rate, same-opponent boosting, trophy spikes, streaks, score outliers.
    ///  4. Device fingerprinting: many accounts on one device.
    /// Statistical flags go to a human review queue; proven violations escalate automatically
    /// (warning + 1 day + trophy freeze -> 7 days + 50% trophy reset -> permanent), forged scores are banned at once.
    /// A different rules version is never treated as cheating.
    /// </summary>
    public sealed class AntiCheatManager
    {
        public const int WallClockToleranceMs = 5000;

        private readonly GameBalance _balance;
        private readonly IClock _clock;
        private readonly IFlagStore _flags;
        private long _sequence;

        public AntiCheatManager(GameBalance balance, IClock clock, IFlagStore flags)
        {
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _flags = flags ?? throw new ArgumentNullException(nameof(flags));
        }

        public event Action<CheatFlag> OnFlagged;

        public event Action<string, PunishmentDecision> OnPunished;

        private long Now => TimeUtil.ToUnixMs(_clock.UtcNow);

        // ------------------------------------------------------------------ validation

        /// <summary>Re-simulates a submitted replay and flags/punishes on divergence.</summary>
        public ReplayVerification ValidateReplay(PlayerIntegrityRecord record, ReplayData replay, SessionConfig config, string matchId = null)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }

            ReplayVerification verification = ReplaySimulator.Verify(replay, config, _balance);
            if (verification.Valid || verification.Error == ErrorCode.VersionMismatch)
            {
                return verification;
            }

            FlagReason reason;
            CheatSeverity severity;
            string details = verification.Reason ?? string.Empty;
            if (details.Contains(ErrorCode.TooFast.ToString()) || details.Contains(ErrorCode.TimestampOutOfOrder.ToString()))
            {
                reason = FlagReason.ImpossibleTiming;
                severity = CheatSeverity.Confirmed;
            }
            else if (verification.Session != null && replay.FinalScore > verification.AuthoritativeScore)
            {
                reason = FlagReason.ReplayMismatch;
                severity = CheatSeverity.Severe;
            }
            else
            {
                // Divergence that does not benefit the player is more likely a client bug: review, don't punish.
                reason = FlagReason.ReplayMismatch;
                severity = CheatSeverity.Suspicious;
            }

            FlagSuspiciousActivity(record.PlayerId, reason, severity, details, matchId);
            if (severity >= CheatSeverity.Confirmed)
            {
                EnforcePunishment(record, severity == CheatSeverity.Severe);
            }
            return verification;
        }

        /// <summary>Prompt API: the claimed score is exactly what the replay produces.</summary>
        public bool ValidateScore(long claimedScore, ReplayData replay, SessionConfig config)
        {
            ReplayVerification v = ReplaySimulator.Verify(replay, config, _balance);
            return v.Valid && v.AuthoritativeScore == claimedScore;
        }

        /// <summary>Prompt API: also checks the starting board is the one derived from the match seed.</summary>
        public bool ValidateScore(long claimedScore, GameBoard boardBefore, ReplayData replay, SessionConfig config)
        {
            if (boardBefore == null)
            {
                throw new ArgumentNullException(nameof(boardBefore));
            }
            var fresh = new GameSession(config, _balance);
            return fresh.InitialBoardHash == boardBefore.ComputeHash() && ValidateScore(claimedScore, replay, config);
        }

        /// <summary>Prompt API: a single swap is legal on this board.</summary>
        public bool ValidateMove(GameBoard board, Pos from, Pos to) => board != null && MoveFinder.IsValidSwap(board, from, to);

        /// <summary>Prompt API: two consecutive actions respect the minimum interval and animation duration.</summary>
        public bool ValidateMoveTiming(int previousActionMs, int previousAnimationMs, int currentActionMs) =>
            currentActionMs >= previousActionMs + Math.Max(_balance.Timing.MinActionIntervalMs, previousAnimationMs);

        /// <summary>The session clock cannot run faster than the server's wall clock (time-travel / speed hacks).</summary>
        public bool ValidateTimestamps(PlayerIntegrityRecord record, ReplayData replay, long matchStartedUnixMs, long receivedUnixMs, string matchId = null)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }
            if (replay == null)
            {
                throw new ArgumentNullException(nameof(replay));
            }

            long wallClock = receivedUnixMs - matchStartedUnixMs;
            bool ok = wallClock >= 0 && replay.EndTimeMs <= wallClock + WallClockToleranceMs && receivedUnixMs <= Now + WallClockToleranceMs;
            if (!ok)
            {
                FlagSuspiciousActivity(record.PlayerId, FlagReason.TimestampTampering, CheatSeverity.Confirmed,
                    "Session lasted " + replay.EndTimeMs + " ms but only " + wallClock + " ms elapsed on the server.", matchId);
                EnforcePunishment(record, false);
            }
            return ok;
        }

        // ------------------------------------------------------------------ behavior

        /// <summary>Feeds a finished ranked match into the behavioral detectors. Returns new flags.</summary>
        public List<CheatFlag> AnalyzeMatch(PlayerIntegrityRecord record, MatchSummary match)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }
            if (match == null)
            {
                throw new ArgumentNullException(nameof(match));
            }

            AntiCheatBalance ac = _balance.AntiCheat;
            var raised = new List<CheatFlag>();

            List<long> previousScores = new List<long>(record.RecentScores);
            record.RecentMatches.Add(match);
            if (record.RecentMatches.Count > PlayerIntegrityRecord.MaxRecentMatches)
            {
                record.RecentMatches.RemoveAt(0);
            }
            record.RecentScores.Add(match.Score);
            if (record.RecentScores.Count > PlayerIntegrityRecord.MaxRecentScores)
            {
                record.RecentScores.RemoveAt(0);
            }

            int day = TimeUtil.DayIndex(TimeUtil.FromUnixMs(match.AtUnixMs));
            record.DailyTrophyGain.TryGetValue(day, out int gained);
            record.DailyTrophyGain[day] = gained + Math.Max(0, match.TrophyDelta);
            foreach (int oldDay in record.DailyTrophyGain.Keys.Where(d => d < day - 30).ToList())
            {
                record.DailyTrophyGain.Remove(oldDay);
            }

            // Win rate over the last N games.
            if (record.RecentMatches.Count >= ac.WinRateMinGames)
            {
                int wins = record.RecentMatches.Skip(record.RecentMatches.Count - ac.WinRateMinGames).Count(m => m.Won);
                if (wins * 1000 / ac.WinRateMinGames >= ac.WinRateFlagPermille)
                {
                    Add(raised, FlagSuspiciousActivity(record.PlayerId, FlagReason.WinRate, CheatSeverity.Suspicious, wins + "/" + ac.WinRateMinGames + " wins", match.MatchId));
                }
            }

            // Boosting: farming the same opponent.
            if (!string.IsNullOrEmpty(match.OpponentId))
            {
                var vs = record.RecentMatches.Where(m => m.OpponentId == match.OpponentId).ToList();
                if (vs.Count >= ac.SameOpponentMinGames && vs.Count(m => m.Won) * 1000 / vs.Count >= ac.SameOpponentWinRateFlagPermille)
                {
                    record.BoostingCooldownUntilUnixMs = Now + ac.BoostingCooldownMinutes * 60000L;
                    Add(raised, FlagSuspiciousActivity(record.PlayerId, FlagReason.SameOpponentWinRate, CheatSeverity.Suspicious, vs.Count(m => m.Won) + "/" + vs.Count + " vs " + match.OpponentId, match.MatchId));
                }
            }

            // Streak.
            int streak = 0;
            for (int i = record.RecentMatches.Count - 1; i >= 0 && record.RecentMatches[i].Won; i--)
            {
                streak++;
            }
            if (streak >= ac.PvpWinStreakFlag)
            {
                Add(raised, FlagSuspiciousActivity(record.PlayerId, FlagReason.WinStreak, CheatSeverity.Suspicious, streak + " wins in a row", match.MatchId));
            }

            // Consistent trophy spikes.
            int consecutive = 0;
            for (int d = day; record.DailyTrophyGain.TryGetValue(d, out int g) && g >= ac.DailyTrophyGainFlag; d--)
            {
                consecutive++;
            }
            if (consecutive >= ac.DailyTrophyGainConsecutiveDays)
            {
                Add(raised, FlagSuspiciousActivity(record.PlayerId, FlagReason.TrophySpike, CheatSeverity.Suspicious, consecutive + " days above " + ac.DailyTrophyGainFlag + " trophies", match.MatchId));
            }

            // Score outlier vs own history.
            if (previousScores.Count >= ac.ScoreOutlierMinHistory)
            {
                previousScores.Sort();
                long median = previousScores[previousScores.Count / 2];
                if (median > 0 && match.Score > median * ac.ScoreOutlierMedianMultiple)
                {
                    Add(raised, FlagSuspiciousActivity(record.PlayerId, FlagReason.ScoreOutlier, CheatSeverity.Suspicious, "Score " + match.Score + " vs median " + median, match.MatchId));
                }
            }

            return raised;
        }

        /// <summary>Registers a login; flags every account of a device that exceeds the account limit.</summary>
        public int RegisterDeviceLogin(DeviceRegistry registry, string deviceHash, string playerId)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }
            int count = registry.Register(deviceHash, playerId);
            if (count > _balance.AntiCheat.MaxAccountsPerDevice)
            {
                foreach (string account in registry.Accounts(deviceHash))
                {
                    FlagSuspiciousActivity(account, FlagReason.MultiAccountDevice, CheatSeverity.Suspicious, count + " accounts on one device");
                }
            }
            return count;
        }

        // ------------------------------------------------------------------ flags & punishment

        /// <summary>Prompt API. Suspicious flags of the same reason are de-duplicated for 24 h.</summary>
        public CheatFlag FlagSuspiciousActivity(string playerId, FlagReason reason, CheatSeverity severity, string details, string matchId = null)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                throw new ArgumentException("Player id required.", nameof(playerId));
            }

            long now = Now;
            if (severity <= CheatSeverity.Suspicious)
            {
                CheatFlag existing = _flags.ForPlayer(playerId).LastOrDefault(f => f.Reason == reason && now - f.AtUnixMs < 86400000L);
                if (existing != null)
                {
                    return null;
                }
            }

            var flag = new CheatFlag
            {
                Id = "flag_" + now.ToString("x") + "_" + (++_sequence),
                PlayerId = playerId,
                Reason = reason,
                Severity = severity,
                Details = details,
                MatchId = matchId,
                AtUnixMs = now
            };
            _flags.Add(flag);
            OnFlagged?.Invoke(flag);
            return flag;
        }

        public CheatFlag FlagSuspiciousActivity(string playerId, string reason) =>
            FlagSuspiciousActivity(playerId, FlagReason.Other, CheatSeverity.Suspicious, reason);

        public PunishmentDecision DecidePunishment(int offenseNumber, bool severe)
        {
            AntiCheatBalance ac = _balance.AntiCheat;
            long now = Now;
            if (severe || offenseNumber >= 3)
            {
                return new PunishmentDecision { Action = PunishmentAction.PermanentBan, OffenseNumber = offenseNumber };
            }
            if (offenseNumber == 2)
            {
                return new PunishmentDecision
                {
                    Action = PunishmentAction.TemporaryBan,
                    OffenseNumber = 2,
                    SuspendedUntilUnixMs = now + ac.SecondOffenseSuspensionHours * 3600000L,
                    TrophyResetPermille = ac.SecondOffenseTrophyResetPermille
                };
            }
            if (offenseNumber == 1)
            {
                return new PunishmentDecision
                {
                    Action = PunishmentAction.TemporaryBan,
                    OffenseNumber = 1,
                    SuspendedUntilUnixMs = now + ac.FirstOffenseSuspensionHours * 3600000L,
                    FreezeTrophies = true
                };
            }
            return new PunishmentDecision { Action = PunishmentAction.None };
        }

        /// <summary>Records one more offense and applies the resulting sanction to the record.</summary>
        public PunishmentDecision EnforcePunishment(PlayerIntegrityRecord record, bool severe)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }
            record.OffenseCount++;
            PunishmentDecision decision = DecidePunishment(record.OffenseCount, severe);
            Apply(record, decision);
            return decision;
        }

        /// <summary>Prompt API: applies the sanction for a given offense count. Returns true if something was applied.</summary>
        public bool EnforcePunishment(PlayerIntegrityRecord record, int offenseCount)
        {
            if (record == null)
            {
                throw new ArgumentNullException(nameof(record));
            }
            record.OffenseCount = Math.Max(record.OffenseCount, offenseCount);
            PunishmentDecision decision = DecidePunishment(offenseCount, false);
            Apply(record, decision);
            return decision.Action != PunishmentAction.None;
        }

        public bool IsBanned(PlayerIntegrityRecord record) => record != null && (record.PermanentlyBanned || record.SuspendedUntilUnixMs > Now);

        public bool AreTrophiesFrozen(PlayerIntegrityRecord record) => record != null && record.TrophiesFrozenUntilUnixMs > Now;

        public bool IsInBoostingCooldown(PlayerIntegrityRecord record) => record != null && record.BoostingCooldownUntilUnixMs > Now;

        public static int ApplyTrophyReset(int trophies, PunishmentDecision decision) =>
            decision == null ? trophies : Math.Max(0, trophies) * (1000 - decision.TrophyResetPermille) / 1000;

        private void Apply(PlayerIntegrityRecord record, PunishmentDecision decision)
        {
            switch (decision.Action)
            {
                case PunishmentAction.PermanentBan:
                    record.PermanentlyBanned = true;
                    break;
                case PunishmentAction.TemporaryBan:
                    record.SuspendedUntilUnixMs = Math.Max(record.SuspendedUntilUnixMs, decision.SuspendedUntilUnixMs);
                    if (decision.FreezeTrophies)
                    {
                        record.TrophiesFrozenUntilUnixMs = Math.Max(record.TrophiesFrozenUntilUnixMs, decision.SuspendedUntilUnixMs);
                    }
                    break;
                default:
                    return;
            }
            OnPunished?.Invoke(record.PlayerId, decision);
        }

        private static void Add(List<CheatFlag> list, CheatFlag flag)
        {
            if (flag != null)
            {
                list.Add(flag);
            }
        }
    }
}
