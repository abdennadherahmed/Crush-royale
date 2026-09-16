using System;
using System.Collections.Generic;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Gameplay;

namespace CrushRoyale.Core.Replay
{
    /// <summary>Prompt struct: one swap with the score reached after it.</summary>
    public readonly struct PlayerMove
    {
        public readonly int TimestampMs;
        public readonly Pos PieceA;
        public readonly Pos PieceB;
        public readonly long ResultingScore;

        public PlayerMove(int timestampMs, Pos pieceA, Pos pieceB, long resultingScore)
        {
            TimestampMs = timestampMs;
            PieceA = pieceA;
            PieceB = pieceB;
            ResultingScore = resultingScore;
        }
    }

    public sealed class ReplayAction
    {
        public ReplayAction(PlayerAction action, long scoreAfter)
        {
            Action = action ?? throw new ArgumentNullException(nameof(action));
            ScoreAfter = scoreAfter;
        }

        public PlayerAction Action { get; }

        /// <summary>Score claimed by the client after this action (server recomputes and compares).</summary>
        public long ScoreAfter { get; }
    }

    /// <summary>Board hash + score at a fixed session time ("snapshot every 5 seconds"), for desync diagnostics.</summary>
    public readonly struct ReplayCheckpoint
    {
        public readonly int TimeMs;
        public readonly ulong BoardHash;
        public readonly long Score;

        public ReplayCheckpoint(int timeMs, ulong boardHash, long score)
        {
            TimeMs = timeMs;
            BoardHash = boardHash;
            Score = score;
        }
    }

    /// <summary>
    /// A complete, re-simulatable record of a match: seed + config essentials + timestamped actions.
    /// Typically 1-3 KB for a 90 s PvP match.
    /// </summary>
    public sealed class ReplayData
    {
        public int RulesVersion { get; set; } = GameBalance.RulesVersion;

        public ulong BalanceHash { get; set; }

        public GameMode Mode { get; set; }

        public ulong Seed { get; set; }

        public int StageId { get; set; }

        public string PlayerId { get; set; } = string.Empty;

        public League HighestLeague { get; set; }

        public int AssistExtraMoves { get; set; }

        public List<LoadoutEntry> Loadout { get; set; } = new List<LoadoutEntry>();

        /// <summary>Pet equipped for the match and the level it played at (format v2+).</summary>
        public PetType Pet { get; set; }

        public int PetLevel { get; set; }

        public List<ReplayAction> Actions { get; set; } = new List<ReplayAction>();

        public List<ReplayCheckpoint> Checkpoints { get; set; } = new List<ReplayCheckpoint>();

        public SessionState EndState { get; set; } = SessionState.Running;

        public int EndTimeMs { get; set; }

        public long FinalScore { get; set; }

        public bool IsFinished => EndState != SessionState.Running;

        public int ContinuesUsed
        {
            get
            {
                int n = 0;
                foreach (ReplayAction a in Actions)
                {
                    if (a.Action.Type == ActionType.Continue)
                    {
                        n++;
                    }
                }
                return n;
            }
        }

        public IEnumerable<PlayerMove> GetMoves()
        {
            foreach (ReplayAction a in Actions)
            {
                if (a.Action.Type == ActionType.Swap)
                {
                    yield return new PlayerMove(a.Action.TimestampMs, a.Action.From, a.Action.To, a.ScoreAfter);
                }
            }
        }

        public Dictionary<PowerUpType, int> CountPowerUps()
        {
            var counts = new Dictionary<PowerUpType, int>();
            foreach (ReplayAction a in Actions)
            {
                if (a.Action.Type == ActionType.PowerUp)
                {
                    counts.TryGetValue(a.Action.PowerUp, out int n);
                    counts[a.Action.PowerUp] = n + 1;
                }
            }
            return counts;
        }

        /// <summary>Score the player had at session time <paramref name="timeMs"/> (last action at or before it).</summary>
        public long ScoreAt(int timeMs)
        {
            long score = 0;
            foreach (ReplayAction a in Actions)
            {
                if (a.Action.TimestampMs > timeMs)
                {
                    break;
                }
                score = a.ScoreAfter;
            }
            return score;
        }
    }

    /// <summary>Builds the replay while a session runs.</summary>
    public sealed class ReplayRecorder
    {
        public ReplayRecorder(SessionConfig config, GameBalance balance, string playerId)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }
            if (balance == null)
            {
                throw new ArgumentNullException(nameof(balance));
            }

            Data = new ReplayData
            {
                RulesVersion = GameBalance.RulesVersion,
                BalanceHash = balance.ComputeHash(),
                Mode = config.Mode,
                Seed = config.Seed,
                StageId = config.StageId,
                PlayerId = playerId ?? string.Empty,
                HighestLeague = config.HighestLeague,
                AssistExtraMoves = config.AssistExtraMoves,
                Pet = config.Pet,
                PetLevel = config.PetLevel
            };
            foreach (LoadoutEntry e in config.Loadout)
            {
                Data.Loadout.Add(new LoadoutEntry(e.Type, e.Quantity));
            }
        }

        public ReplayData Data { get; }

        /// <summary>Prompt API: records a swap.</summary>
        public void RecordMove(Pos from, Pos to, int timestampMs, long scoreAfter) => RecordAction(PlayerAction.Swap(from, to, timestampMs), scoreAfter);

        public void RecordAction(PlayerAction action, long scoreAfter)
        {
            if (Data.IsFinished)
            {
                throw new InvalidOperationException("Replay is finished.");
            }
            Data.Actions.Add(new ReplayAction(action, scoreAfter));
        }

        public void RecordCheckpoint(int timeMs, ulong boardHash, long score) => Data.Checkpoints.Add(new ReplayCheckpoint(timeMs, boardHash, score));

        public void Finish(SessionState state, int endTimeMs, long finalScore)
        {
            Data.EndState = state;
            Data.EndTimeMs = endTimeMs;
            Data.FinalScore = finalScore;
        }

        public void Reopen()
        {
            Data.EndState = SessionState.Running;
        }

        /// <summary>Prompt API: binary form for upload.</summary>
        public byte[] SerializeMoves() => ReplaySerializer.Serialize(Data);
    }
}
