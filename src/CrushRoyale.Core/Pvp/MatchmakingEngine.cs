using System;
using System.Collections.Generic;
using System.Linq;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;

namespace CrushRoyale.Core.Pvp
{
    public enum TicketStatus : byte
    {
        Waiting = 0,
        Matched = 1,
        TimedOut = 2,
        Cancelled = 3
    }

    public sealed class MatchmakingTicket
    {
        public string PlayerId { get; internal set; }

        public int Trophies { get; internal set; }

        public int WinStreak { get; internal set; }

        public string Region { get; internal set; }

        public long EnqueuedAtMs { get; internal set; }

        public TicketStatus Status { get; internal set; }

        public string MatchId { get; internal set; }

        public string OpponentId { get; internal set; }

        public bool VsGhost { get; internal set; }

        public string GhostReplayId { get; internal set; }

        public ulong Seed { get; internal set; }

        public int CurrentRange { get; internal set; }
    }

    /// <summary>A recorded PvP session that can be offered as an opponent.</summary>
    public sealed class GhostCandidate
    {
        public string ReplayId { get; set; }

        public string PlayerId { get; set; }

        public int Trophies { get; set; }

        public long RecordedAtMs { get; set; }

        public string Region { get; set; }

        public ulong Seed { get; set; }
    }

    public interface IGhostPool
    {
        IEnumerable<GhostCandidate> FindGhosts(int minTrophies, int maxTrophies, long recordedAfterMs);
    }

    /// <summary>In-process ghost pool (tests, single-instance server). Keeps the latest ghosts per player.</summary>
    public sealed class InMemoryGhostPool : IGhostPool
    {
        private readonly int _perPlayer;
        private readonly List<GhostCandidate> _ghosts = new List<GhostCandidate>();
        private readonly object _lock = new object();

        public InMemoryGhostPool(int perPlayer = 5)
        {
            _perPlayer = Math.Max(1, perPlayer);
        }

        public void Add(GhostCandidate ghost)
        {
            if (ghost == null)
            {
                throw new ArgumentNullException(nameof(ghost));
            }
            lock (_lock)
            {
                _ghosts.Add(ghost);
                var mine = _ghosts.Where(g => g.PlayerId == ghost.PlayerId).OrderByDescending(g => g.RecordedAtMs).Skip(_perPlayer).ToList();
                foreach (GhostCandidate old in mine)
                {
                    _ghosts.Remove(old);
                }
            }
        }

        public IEnumerable<GhostCandidate> FindGhosts(int minTrophies, int maxTrophies, long recordedAfterMs)
        {
            lock (_lock)
            {
                return _ghosts.Where(g => g.Trophies >= minTrophies && g.Trophies <= maxTrophies && g.RecordedAtMs >= recordedAfterMs).ToList();
            }
        }
    }

    public sealed class MatchFound
    {
        public string MatchId { get; internal set; }

        public ulong Seed { get; internal set; }

        public string PlayerA { get; internal set; }

        /// <summary>Live opponent id, or the ghost owner id when <see cref="VsGhost"/>.</summary>
        public string PlayerB { get; internal set; }

        public bool VsGhost { get; internal set; }

        public GhostCandidate Ghost { get; internal set; }
    }

    /// <summary>
    /// Task 7. Queue with widening trophy ranges (±100, +50 every 5 s, max ±300), mutual-range live pairing,
    /// ghost fallback (PvP is asynchronous, so a recorded opponent is always a valid match), fairness rules
    /// (no rematch within 5 minutes, max 3 games vs the same player per hour) and a 30 s timeout.
    /// Thread-safe; the server calls <see cref="ProcessQueue"/> on a short timer.
    /// </summary>
    public sealed class MatchmakingEngine
    {
        private readonly GameBalance _balance;
        private readonly IClock _clock;
        private readonly IGhostPool _ghosts;
        private readonly Dictionary<string, MatchmakingTicket> _tickets = new Dictionary<string, MatchmakingTicket>(StringComparer.Ordinal);
        private readonly List<PairingRecord> _pairings = new List<PairingRecord>();
        private readonly DeterministicRandom _seedRng;
        private readonly object _lock = new object();

        public MatchmakingEngine(GameBalance balance, IClock clock, IGhostPool ghosts, ulong seedEntropy)
        {
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _ghosts = ghosts ?? throw new ArgumentNullException(nameof(ghosts));
            _seedRng = new DeterministicRandom(seedEntropy);
        }

        public event Action<MatchFound> OnMatchFound;

        private long Now => TimeUtil.ToUnixMs(_clock.UtcNow);

        /// <summary>Prompt API: enqueue (or return the existing ticket).</summary>
        public MatchmakingTicket RequestMatch(string playerId, int trophies, int winStreak, string region)
        {
            if (string.IsNullOrEmpty(playerId))
            {
                throw new ArgumentException("Player id required.", nameof(playerId));
            }

            lock (_lock)
            {
                if (_tickets.TryGetValue(playerId, out MatchmakingTicket existing) && existing.Status == TicketStatus.Waiting)
                {
                    return existing;
                }

                var ticket = new MatchmakingTicket
                {
                    PlayerId = playerId,
                    Trophies = Math.Max(0, trophies),
                    WinStreak = Math.Max(0, winStreak),
                    Region = region ?? string.Empty,
                    EnqueuedAtMs = Now,
                    Status = TicketStatus.Waiting,
                    CurrentRange = _balance.Matchmaking.InitialRange
                };
                _tickets[playerId] = ticket;
                return ticket;
            }
        }

        public bool CancelMatchRequest(string playerId)
        {
            lock (_lock)
            {
                if (playerId != null && _tickets.TryGetValue(playerId, out MatchmakingTicket t) && t.Status == TicketStatus.Waiting)
                {
                    t.Status = TicketStatus.Cancelled;
                    _tickets.Remove(playerId);
                    return true;
                }
                return false;
            }
        }

        public MatchmakingTicket GetTicket(string playerId)
        {
            lock (_lock)
            {
                return playerId != null && _tickets.TryGetValue(playerId, out MatchmakingTicket t) ? t : null;
            }
        }

        /// <summary>Removes a finished ticket once the client has read it.</summary>
        public void Acknowledge(string playerId)
        {
            lock (_lock)
            {
                if (playerId != null && _tickets.TryGetValue(playerId, out MatchmakingTicket t) && t.Status != TicketStatus.Waiting)
                {
                    _tickets.Remove(playerId);
                }
            }
        }

        public int WaitingCount
        {
            get
            {
                lock (_lock)
                {
                    return _tickets.Values.Count(t => t.Status == TicketStatus.Waiting);
                }
            }
        }

        /// <summary>Prompt API: tries to match this player right now. Returns the opponent (or ghost owner) id, or null.</summary>
        public string FindOpponent(string playerId)
        {
            ProcessQueue();
            MatchmakingTicket t = GetTicket(playerId);
            return t != null && t.Status == TicketStatus.Matched ? t.OpponentId : null;
        }

        /// <summary>One matchmaking pass. Longest-waiting players are served first.</summary>
        public List<MatchFound> ProcessQueue()
        {
            var found = new List<MatchFound>();
            lock (_lock)
            {
                long now = Now;
                PrunePairings(now);
                MatchmakingBalance mm = _balance.Matchmaking;

                List<MatchmakingTicket> waiting = _tickets.Values
                    .Where(t => t.Status == TicketStatus.Waiting)
                    .OrderBy(t => t.EnqueuedAtMs)
                    .ThenBy(t => t.PlayerId, StringComparer.Ordinal)
                    .ToList();

                foreach (MatchmakingTicket ticket in waiting)
                {
                    if (ticket.Status != TicketStatus.Waiting)
                    {
                        continue;
                    }

                    long waited = now - ticket.EnqueuedAtMs;
                    if (waited >= mm.TimeoutMs)
                    {
                        ticket.Status = TicketStatus.TimedOut;
                        continue;
                    }

                    ticket.CurrentRange = RangeFor(waited);
                    int target = TargetFor(ticket);

                    MatchmakingTicket live = waiting
                        .Where(o => o != ticket && o.Status == TicketStatus.Waiting)
                        .Where(o => Math.Abs(o.Trophies - target) <= ticket.CurrentRange)
                        .Where(o => Math.Abs(ticket.Trophies - TargetFor(o)) <= RangeFor(now - o.EnqueuedAtMs))
                        .Where(o => CanPair(ticket.PlayerId, o.PlayerId, now))
                        .OrderBy(o => Math.Abs(o.Trophies - target))
                        .ThenBy(o => o.Region == ticket.Region ? 0 : 1)
                        .ThenBy(o => o.EnqueuedAtMs)
                        .FirstOrDefault();

                    if (live != null)
                    {
                        found.Add(PairLive(ticket, live, now));
                        continue;
                    }

                    if (waited >= mm.GhostFallbackAfterMs)
                    {
                        long minAge = now - mm.GhostMaxAgeHours * 3600000L;
                        GhostCandidate ghost = _ghosts.FindGhosts(target - ticket.CurrentRange, target + ticket.CurrentRange, minAge)
                            .Where(g => g.PlayerId != ticket.PlayerId && CanPair(ticket.PlayerId, g.PlayerId, now))
                            .OrderBy(g => Math.Abs(g.Trophies - target))
                            .ThenBy(g => g.Region == ticket.Region ? 0 : 1)
                            .ThenByDescending(g => g.RecordedAtMs)
                            .FirstOrDefault();

                        if (ghost != null)
                        {
                            found.Add(PairGhost(ticket, ghost, now));
                        }
                    }
                }
            }

            foreach (MatchFound m in found)
            {
                OnMatchFound?.Invoke(m);
            }
            return found;
        }

        /// <summary>Fairness: no rematch inside the window, bounded games per hour against the same player.</summary>
        public bool CanPair(string a, string b, long nowMs)
        {
            MatchmakingBalance mm = _balance.Matchmaking;
            int lastHour = 0;
            foreach (PairingRecord p in _pairings)
            {
                if (!p.Involves(a, b))
                {
                    continue;
                }
                if (nowMs - p.AtMs < mm.NoRematchWindowMs)
                {
                    return false;
                }
                if (nowMs - p.AtMs < 3600000L)
                {
                    lastHour++;
                }
            }
            return lastHour < mm.MaxSameOpponentPerHour;
        }

        /// <summary>Registers a pairing that happened outside the queue (e.g. friendly challenge) for fairness rules.</summary>
        public void RecordPairing(string a, string b)
        {
            lock (_lock)
            {
                _pairings.Add(new PairingRecord(a, b, Now));
            }
        }

        private int RangeFor(long waitedMs)
        {
            MatchmakingBalance mm = _balance.Matchmaking;
            long steps = Math.Max(0, waitedMs) / Math.Max(1, mm.RangeStepIntervalMs);
            return (int)Math.Min(mm.MaxRange, mm.InitialRange + steps * mm.RangeStep);
        }

        /// <summary>Streaking players look slightly upward ("± win_streak_bonus").</summary>
        private int TargetFor(MatchmakingTicket t) =>
            t.Trophies + Math.Min(_balance.Matchmaking.StreakTargetShiftMax, t.WinStreak * _balance.Matchmaking.StreakTargetShiftPerWin);

        private MatchFound PairLive(MatchmakingTicket a, MatchmakingTicket b, long now)
        {
            string matchId = NewMatchId();
            ulong seed = NextSeed();
            foreach (MatchmakingTicket t in new[] { a, b })
            {
                t.Status = TicketStatus.Matched;
                t.MatchId = matchId;
                t.Seed = seed;
                t.VsGhost = false;
            }
            a.OpponentId = b.PlayerId;
            b.OpponentId = a.PlayerId;
            _pairings.Add(new PairingRecord(a.PlayerId, b.PlayerId, now));
            return new MatchFound { MatchId = matchId, Seed = seed, PlayerA = a.PlayerId, PlayerB = b.PlayerId };
        }

        private MatchFound PairGhost(MatchmakingTicket t, GhostCandidate ghost, long now)
        {
            string matchId = NewMatchId();
            t.Status = TicketStatus.Matched;
            t.MatchId = matchId;
            t.Seed = ghost.Seed;
            t.VsGhost = true;
            t.OpponentId = ghost.PlayerId;
            t.GhostReplayId = ghost.ReplayId;
            _pairings.Add(new PairingRecord(t.PlayerId, ghost.PlayerId, now));
            return new MatchFound { MatchId = matchId, Seed = ghost.Seed, PlayerA = t.PlayerId, PlayerB = ghost.PlayerId, VsGhost = true, Ghost = ghost };
        }

        private string NewMatchId()
        {
            ulong hi = ((ulong)_seedRng.NextUInt() << 32) | _seedRng.NextUInt();
            ulong lo = ((ulong)_seedRng.NextUInt() << 32) | _seedRng.NextUInt();
            return "m_" + hi.ToString("x16") + lo.ToString("x16");
        }

        private ulong NextSeed() => ((ulong)_seedRng.NextUInt() << 32) | _seedRng.NextUInt();

        private void PrunePairings(long now) => _pairings.RemoveAll(p => now - p.AtMs > 3600000L);

        private readonly struct PairingRecord
        {
            public readonly string A;
            public readonly string B;
            public readonly long AtMs;

            public PairingRecord(string a, string b, long atMs)
            {
                A = a;
                B = b;
                AtMs = atMs;
            }

            public bool Involves(string x, string y) => (A == x && B == y) || (A == y && B == x);
        }
    }
}
