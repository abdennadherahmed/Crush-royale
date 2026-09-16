using System;
using System.Collections.Generic;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Gameplay;
using CrushRoyale.Core.Replay;

namespace CrushRoyale.Core.Pvp
{
    /// <summary>
    /// Plays an opponent's replay through its own <see cref="GameSession"/> so their board, moves and score can be
    /// rendered live (semi-transparent overlay) and rewound on the timeline.
    /// </summary>
    public sealed class GhostPlayer
    {
        private readonly ReplayData _replay;
        private readonly SessionConfig _config;
        private readonly GameBalance _balance;
        private int _nextIndex;

        public GhostPlayer(ReplayData replay, SessionConfig config, GameBalance balance)
        {
            _replay = replay ?? throw new ArgumentNullException(nameof(replay));
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
            Reset();
        }

        public event Action<ActionOutcome> OnGhostAction;

        public ReplayData Replay => _replay;

        public GameSession Session { get; private set; }

        public int PlaybackTimeMs { get; private set; }

        /// <summary>True if the replay diverged from the simulation (corrupt or other rules): stop showing it.</summary>
        public bool Desynced { get; private set; }

        public long CurrentScore => Session.Score;

        public bool IsFinished => _nextIndex >= _replay.Actions.Count && PlaybackTimeMs >= _replay.EndTimeMs;

        /// <summary>Follows the live match with the configured display delay.</summary>
        public void AdvanceTo(int liveTimeMs) => PlayUntil(liveTimeMs - _balance.Timing.GhostPlaybackDelayMs, true);

        /// <summary>Timeline scrubbing: rewinding re-simulates from the start (cheap: pure integer simulation).</summary>
        public void SeekTo(int timeMs)
        {
            if (timeMs < PlaybackTimeMs)
            {
                Reset();
            }
            PlayUntil(timeMs, false);
        }

        public long ScoreAt(int timeMs) => _replay.ScoreAt(timeMs);

        private void PlayUntil(int timeMs, bool notify)
        {
            if (Desynced)
            {
                return;
            }

            while (_nextIndex < _replay.Actions.Count && _replay.Actions[_nextIndex].Action.TimestampMs <= timeMs)
            {
                PlayerAction action = _replay.Actions[_nextIndex].Action;
                if (action.Type == ActionType.Continue && Session.IsRunning)
                {
                    Session.FinishByTime();
                }

                ActionOutcome outcome = Session.Apply(action);
                _nextIndex++;
                if (!outcome.Accepted)
                {
                    Desynced = true;
                    return;
                }
                if (notify)
                {
                    OnGhostAction?.Invoke(outcome);
                }
            }

            if (timeMs > PlaybackTimeMs)
            {
                PlaybackTimeMs = timeMs;
            }
            Session.Tick(Math.Max(0, PlaybackTimeMs));
        }

        private void Reset()
        {
            Session = new GameSession(_config, _balance, _replay.PlayerId);
            _nextIndex = 0;
            PlaybackTimeMs = 0;
            Desynced = false;
        }
    }

    public sealed class PvpMatchResult
    {
        public string ChallengerId { get; internal set; }

        public string OpponentId { get; internal set; }

        /// <summary>Null on a draw.</summary>
        public string WinnerId { get; internal set; }

        public MatchOutcome ChallengerOutcome { get; internal set; }

        public long ChallengerRawScore { get; internal set; }

        public long OpponentRawScore { get; internal set; }

        /// <summary>Challenger points voided by the opponent's Freezing Gel.</summary>
        public long ChallengerFrozenPoints { get; internal set; }

        public long OpponentFrozenPoints { get; internal set; }

        public long ChallengerScore => ChallengerRawScore - ChallengerFrozenPoints;

        public long OpponentScore => OpponentRawScore - OpponentFrozenPoints;

        /// <summary>Prompt API shape: (winnerId, score1, score2).</summary>
        public Tuple<string, long, long> AsTuple() => Tuple.Create(WinnerId, ChallengerScore, OpponentScore);
    }

    /// <summary>
    /// Task 6: ghost-play PvP. Player 1 plays and uploads a replay; player 2 plays the same seed while watching
    /// player 1's ghost; both replays are compared (server-side after validation) to decide the winner.
    /// </summary>
    public sealed class PvpGhostPlay
    {
        private readonly GameBalance _balance;

        public PvpGhostPlay(GameBalance balance)
        {
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
        }

        public GameSession LiveSession { get; private set; }

        public GhostPlayer Ghost { get; private set; }

        public GameSession StartMatch(SessionConfig config, string playerId)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }
            if (!config.IsPvp)
            {
                throw new ArgumentException("Ghost play requires a PvP session.", nameof(config));
            }
            LiveSession = new GameSession(config, _balance, playerId);
            return LiveSession;
        }

        /// <summary>Prompt API: records (and applies) a move of the live player.</summary>
        public ActionOutcome RecordMove(Pos from, Pos to, int timestampMs)
        {
            RequireLive();
            return LiveSession.AttemptMove(from, to, timestampMs);
        }

        /// <summary>Prompt API: binary replay of the live player.</summary>
        public byte[] SerializeMoves()
        {
            RequireLive();
            return ReplaySerializer.Serialize(LiveSession.Replay);
        }

        /// <summary>Prompt API: loads the opponent's replay as a ghost. The seed must match the live match.</summary>
        public OperationResult LoadOpponentGhost(byte[] moveData)
        {
            RequireLive();
            OperationResult<ReplayData> parsed = ReplaySerializer.TryDeserialize(moveData);
            if (!parsed.Success)
            {
                return parsed.WithoutValue();
            }

            ReplayData replay = parsed.Value;
            if (replay.Seed != LiveSession.Config.Seed || !IsPvpMode(replay.Mode))
            {
                return OperationResult.Fail(ErrorCode.ReplayMismatch, "Ghost was recorded on another board.");
            }
            if (replay.BalanceHash != _balance.ComputeHash())
            {
                return OperationResult.Fail(ErrorCode.VersionMismatch, "Ghost was recorded with other rules.");
            }

            Ghost = new GhostPlayer(replay, GhostConfig(replay, _balance), _balance);
            return OperationResult.Ok();
        }

        /// <summary>Call every frame with the live session clock.</summary>
        public void Update(int liveTimeMs)
        {
            RequireLive();
            LiveSession.Tick(liveTimeMs);
            Ghost?.AdvanceTo(liveTimeMs);
        }

        /// <summary>Prompt API: result of the live player vs the loaded ghost.</summary>
        public PvpMatchResult GetMatchResult()
        {
            RequireLive();
            if (Ghost == null)
            {
                throw new InvalidOperationException("No ghost loaded.");
            }
            return ComputeResult(LiveSession.Replay, Ghost.Replay, _balance);
        }

        /// <summary>The config a ghost must be re-simulated with (its own loadout and league).</summary>
        public static SessionConfig GhostConfig(ReplayData replay, GameBalance balance) =>
            SessionConfig.ForPvp(replay.Seed, balance, replay.Mode, replay.Loadout, replay.HighestLeague).WithPet(replay.Pet, replay.PetLevel, balance);

        /// <summary>Compares two (already validated) replays of the same seed, applying Freezing Gel windows.</summary>
        public static PvpMatchResult ComputeResult(ReplayData challenger, ReplayData opponent, GameBalance balance)
        {
            if (challenger == null)
            {
                throw new ArgumentNullException(nameof(challenger));
            }
            if (opponent == null)
            {
                throw new ArgumentNullException(nameof(opponent));
            }
            if (challenger.Seed != opponent.Seed)
            {
                throw new ArgumentException("Replays were played on different boards.");
            }

            var result = new PvpMatchResult
            {
                ChallengerId = challenger.PlayerId,
                OpponentId = opponent.PlayerId,
                ChallengerRawScore = challenger.FinalScore,
                OpponentRawScore = opponent.FinalScore,
                ChallengerFrozenPoints = FrozenPoints(challenger, opponent, balance),
                OpponentFrozenPoints = FrozenPoints(opponent, challenger, balance)
            };

            if (result.ChallengerScore > result.OpponentScore)
            {
                result.WinnerId = challenger.PlayerId;
                result.ChallengerOutcome = MatchOutcome.Win;
            }
            else if (result.ChallengerScore < result.OpponentScore)
            {
                result.WinnerId = opponent.PlayerId;
                result.ChallengerOutcome = MatchOutcome.Loss;
            }
            else
            {
                result.ChallengerOutcome = MatchOutcome.Draw;
            }
            return result;
        }

        /// <summary>Points the victim scored while the attacker's Freezing Gel was active.</summary>
        public static long FrozenPoints(ReplayData victim, ReplayData attacker, GameBalance balance)
        {
            PowerUpDefinition gel = balance.PowerUps.Get(PowerUpType.FreezingGel);
            var windows = new List<KeyValuePair<int, int>>();
            foreach (ReplayAction a in attacker.Actions)
            {
                if (a.Action.Type == ActionType.PowerUp && a.Action.PowerUp == PowerUpType.FreezingGel)
                {
                    windows.Add(new KeyValuePair<int, int>(a.Action.TimestampMs, a.Action.TimestampMs + gel.DurationMs));
                }
            }
            if (windows.Count == 0)
            {
                return 0;
            }

            long frozen = 0;
            long previousScore = 0;
            foreach (ReplayAction a in victim.Actions)
            {
                long gained = a.ScoreAfter - previousScore;
                previousScore = a.ScoreAfter;
                foreach (KeyValuePair<int, int> w in windows)
                {
                    if (a.Action.TimestampMs >= w.Key && a.Action.TimestampMs < w.Value)
                    {
                        frozen += gained * gel.EffectValue / 1000;
                        break;
                    }
                }
            }
            return frozen;
        }

        private static bool IsPvpMode(GameMode mode) => mode == GameMode.PvpRanked || mode == GameMode.FriendlyChallenge;

        private void RequireLive()
        {
            if (LiveSession == null)
            {
                throw new InvalidOperationException("Call StartMatch first.");
            }
        }
    }
}
