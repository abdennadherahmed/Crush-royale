using System;
using System.Collections.Generic;
using CrushRoyale.Core.Board;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.PowerUps;
using CrushRoyale.Core.Replay;
using CrushRoyale.Core.Scoring;

namespace CrushRoyale.Core.Gameplay
{
    public enum SessionState : byte
    {
        Running = 0,
        Won = 1,
        Lost = 2,

        /// <summary>Time-based modes (PvP, guild boss) ended normally; the winner is decided elsewhere.</summary>
        Completed = 3,

        Abandoned = 4
    }

    public enum ActionType : byte
    {
        Swap = 0,
        PowerUp = 1,
        Continue = 2
    }

    /// <summary>Input to a session. Timestamps are milliseconds since the match started (session clock, not wall clock).</summary>
    public sealed class PlayerAction
    {
        private PlayerAction(ActionType type, int timestampMs, Pos from, Pos to, PowerUpType powerUp, bool hasTarget, Pos target)
        {
            Type = type;
            TimestampMs = timestampMs;
            From = from;
            To = to;
            PowerUp = powerUp;
            HasTarget = hasTarget;
            Target = target;
        }

        public ActionType Type { get; }

        public int TimestampMs { get; }

        public Pos From { get; }

        public Pos To { get; }

        public PowerUpType PowerUp { get; }

        public bool HasTarget { get; }

        public Pos Target { get; }

        public static PlayerAction Swap(Pos from, Pos to, int timestampMs) =>
            new PlayerAction(ActionType.Swap, timestampMs, from, to, default, false, default);

        public static PlayerAction UsePowerUp(PowerUpType type, int timestampMs, Pos? target = null) =>
            new PlayerAction(ActionType.PowerUp, timestampMs, default, default, type, target.HasValue, target ?? default);

        public static PlayerAction Continue(int timestampMs) =>
            new PlayerAction(ActionType.Continue, timestampMs, default, default, default, false, default);

        public PlayerAction WithTimestamp(int timestampMs) =>
            new PlayerAction(Type, timestampMs, From, To, PowerUp, HasTarget, Target);

        public override string ToString()
        {
            switch (Type)
            {
                case ActionType.Swap: return "@" + TimestampMs + " swap " + From + "->" + To;
                case ActionType.PowerUp: return "@" + TimestampMs + " " + PowerUp + (HasTarget ? " at " + Target : string.Empty);
                default: return "@" + TimestampMs + " continue";
            }
        }
    }

    /// <summary>What happened after an action: everything the view needs to animate it.</summary>
    public sealed class ActionOutcome
    {
        internal ActionOutcome(PlayerAction action)
        {
            Action = action;
        }

        public PlayerAction Action { get; }

        public bool Accepted { get; internal set; }

        public ErrorCode Error { get; internal set; }

        /// <summary>Null for invalid actions and for power-ups without a board effect.</summary>
        public ResolutionResult Resolution { get; internal set; }

        public long PointsGained { get; internal set; }

        public int ScoreMultiplierPermille { get; internal set; } = 1000;

        public bool RedSurgeActivated { get; internal set; }

        public bool RedSurgeActive { get; internal set; }

        public int AnimationDurationMs { get; internal set; }

        public long ScoreAfter { get; internal set; }

        /// <summary>Free best move played by the equipped pet right after this swap (null when the pet did not act).</summary>
        public Move? PetMove { get; internal set; }

        public ResolutionResult PetResolution { get; internal set; }

        /// <summary>Points of the pet move (already included in <see cref="PointsGained"/>).</summary>
        public long PetPoints { get; internal set; }

        public int MovesLeft { get; internal set; }

        /// <summary>New boss phase reached by this action, or -1.</summary>
        public int BossPhaseReached { get; internal set; } = -1;

        public List<Pos> BossStones { get; } = new List<Pos>();

        public SessionState StateAfter { get; internal set; }
    }

    /// <summary>Final (or current) statistics of a session.</summary>
    public sealed class StageResult
    {
        public GameMode Mode { get; internal set; }

        public int StageId { get; internal set; }

        public SessionState State { get; internal set; }

        public bool Won => State == SessionState.Won;

        public long FinalScore { get; internal set; }

        public long BonusPoints { get; internal set; }

        public int Stars { get; internal set; }

        public int MovesUsed { get; internal set; }

        public int DurationMs { get; internal set; }

        public int TotalCascades { get; internal set; }

        public int MaxCascadeLevel { get; internal set; }

        public int MegaCascades { get; internal set; }

        public int RedSurgeActivations { get; internal set; }

        public int SpecialsCreated { get; internal set; }

        public int SpecialsActivated { get; internal set; }

        public int Line5Matches { get; internal set; }

        public int[] ClearedByColor { get; internal set; } = new int[6];

        public int StonesDestroyed { get; internal set; }

        public int IceBroken { get; internal set; }

        public int ContinuesUsed { get; internal set; }

        public Dictionary<PowerUpType, int> PowerUpsUsed { get; internal set; } = new Dictionary<PowerUpType, int>();

        public ReplayData Replay { get; internal set; }
    }

    /// <summary>
    /// Task 3: one match. Deterministic state machine driven by timestamped actions:
    /// swap -> resolve -> cascades -> score -> objectives -> win/lose. The exact same class runs on the
    /// client (live play), in ghost playback, and on the server (replay validation).
    /// It never reads a clock: time only enters through action timestamps and <see cref="Tick"/>.
    /// </summary>
    public sealed class GameSession
    {
        private readonly GameBalance _balance;
        private readonly BoardManager _boardManager;
        private readonly CascadeCalculator _cascade;
        private readonly PowerUpManager _powerUps;
        private readonly ObjectiveTracker _objectives;
        private readonly ReplayRecorder _recorder;
        private readonly int[] _clearedByColor = new int[6];

        private int _lastActionMs;
        private int _nextAllowedMs;
        private int _nextCheckpointMs;
        private int _continueExtraTimeMs;
        private int _endTimeMs = -1;
        private bool _timeWarningSent;
        private int _bossPhase;
        private long _bonusPoints;
        private int _stonesDestroyed;
        private int _iceBroken;
        private int _specialsCreated;
        private int _specialsActivated;
        private int _line5Matches;
        private readonly DeterministicRandom _petRng;

        public GameSession(SessionConfig config, GameBalance balance, string playerId = null)
        {
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
            Config = config ?? throw new ArgumentNullException(nameof(config));

            ErrorCode error = config.Validate(balance);
            if (error != ErrorCode.None)
            {
                throw new ArgumentException("Invalid session config: " + error, nameof(config));
            }

            _boardManager = new BoardManager(config.Seed, balance, config.Board.ColorCount);
            _boardManager.GenerateNewBoard(config.Board);
            PlaceStartBoosters(config.StartBoosters);
            _cascade = new CascadeCalculator(balance);
            _powerUps = new PowerUpManager(balance, config);
            _objectives = new ObjectiveTracker(config.Objectives, _boardManager.Board, config.BossHp);
            _recorder = new ReplayRecorder(config, balance, playerId);
            _cascade.AnimationTriggered += trigger => OnCascadeAnimation?.Invoke(trigger);

            MovesLeft = config.HasMoveLimit ? config.MoveLimit + config.AssistExtraMoves : 0;
            if (config.Pet != PetType.None && config.PetLevel > 0)
            {
                // Own stream: pet rolls never disturb board refills, and replays re-simulate them identically.
                _petRng = new DeterministicRandom(config.Seed, 0x9E7A11CE5UL);
            }
            _nextCheckpointMs = balance.Timing.CheckpointIntervalMs;
            InitialBoardHash = _boardManager.Board.ComputeHash();
            State = SessionState.Running;
        }

        /// <summary>
        /// Win-streak bonuses: turns plain gems into a line, a bomb and a line (same colour, so no match appears).
        /// Own random stream: identical on client and server, and board refills are untouched.
        /// </summary>
        private void PlaceStartBoosters(int count)
        {
            count = Math.Min(count, SessionConfig.MaxStartBoosters);
            if (count <= 0)
            {
                return;
            }
            GameBoard board = _boardManager.Board;
            var candidates = new List<Pos>();
            foreach (Pos p in board.AllPositions())
            {
                Piece piece = board[p];
                if (!piece.IsEmpty && piece.Type == PieceType.Normal && board.IceAt(p) == 0)
                {
                    candidates.Add(p);
                }
            }
            var rng = new DeterministicRandom(Config.Seed, 0x57EA4B0057UL);
            PieceType[] types = { PieceType.LineHorizontal, PieceType.AreaBomb, PieceType.LineVertical };
            for (int i = 0; i < count && candidates.Count > 0; i++)
            {
                int k = rng.NextInt(candidates.Count);
                Pos p = candidates[k];
                candidates.RemoveAt(k);
                Piece old = board[p];
                board[p] = new Piece(old.Id, old.Color, types[i], old.Hp);
            }
        }

        // ------------------------------------------------------------------ events (UI integration)

        /// <summary>A wave with at least one match was resolved.</summary>
        public event Action<ResolutionStep> OnMatch;

        /// <summary>Automatic cascade of the given level (1+).</summary>
        public event Action<int> OnCascade;

        public event Action<CascadeAnimationTrigger> OnCascadeAnimation;

        /// <summary>Fired once when remaining time drops under the warning threshold (10 s). Argument: remaining ms.</summary>
        public event Action<int> OnTimeWarning;

        /// <summary>(won, finalScore).</summary>
        public event Action<bool, long> OnGameEnd;

        /// <summary>(newScore, gained).</summary>
        public event Action<long, long> OnScoreChanged;

        public event Action<int> OnComboMeterChanged;

        public event Action<PowerUpType> OnPowerUpActivated;

        /// <summary>(phase, stones added).</summary>
        public event Action<int, IReadOnlyList<Pos>> OnBossPhaseChanged;

        public event Action<ActionOutcome> OnActionResolved;

        // ------------------------------------------------------------------ state

        public SessionConfig Config { get; }

        public SessionState State { get; private set; }

        public bool IsRunning => State == SessionState.Running;

        public GameBoard Board => _boardManager.Board;

        public IBoardManager BoardManager => _boardManager;

        public ulong InitialBoardHash { get; }

        public long Score { get; private set; }

        /// <summary>Remaining moves (meaningless when Config.HasMoveLimit is false).</summary>
        public int MovesLeft { get; private set; }

        public int MovesUsed { get; private set; }

        public int ContinuesUsed { get; private set; }

        public int TimeLimitMs => Config.TimeLimitMs + _powerUps.ExtraTimeMs + _continueExtraTimeMs
            + (Config.HasMoveLimit ? 0 : Config.AssistExtraMoves * AssistMsPerMove);

        /// <summary>On timed stages the difficulty assist gives time instead of moves.</summary>
        public const int AssistMsPerMove = 2500;

        public int LastKnownTimeMs { get; private set; }

        /// <summary>Input stays locked until this time (animation of the previous action).</summary>
        public int NextActionAllowedAtMs => _nextAllowedMs;

        public int EndTimeMs => _endTimeMs;

        public int ComboMeterPermille => _cascade.ComboMeterPermille;

        public CascadeCalculator Cascade => _cascade;

        public PowerUpManager PowerUps => _powerUps;

        public ObjectiveTracker Objectives => _objectives;

        public int BossPhase => _bossPhase;

        public ReplayData Replay => _recorder.Data;

        public bool IsRedSurgeActive(int nowMs) => _cascade.IsRedSurgeActive(nowMs);

        public int RemainingTimeMs(int nowMs) => Math.Max(0, TimeLimitMs - nowMs);

        // ------------------------------------------------------------------ actions

        /// <summary>Prompt API: swap two adjacent pieces at the given session time.</summary>
        public ActionOutcome AttemptMove(Pos pieceA, Pos pieceB, int timestampMs) => Apply(PlayerAction.Swap(pieceA, pieceB, timestampMs));

        public ActionOutcome ActivatePowerUp(PowerUpType type, Pos? target, int timestampMs) => Apply(PlayerAction.UsePowerUp(type, timestampMs, target));

        /// <summary>Prompt API: activate by id as soon as input allows (untargeted power-ups).</summary>
        public ActionOutcome ActivatePowerUp(int powerUpId)
        {
            int t = Math.Max(LastKnownTimeMs, _nextAllowedMs);
            if (powerUpId < 0 || !Enum.IsDefined(typeof(PowerUpType), (PowerUpType)powerUpId))
            {
                var outcome = new ActionOutcome(PlayerAction.UsePowerUp(PowerUpType.ChronoBomb, t)) { Error = ErrorCode.InvalidArgument, StateAfter = State };
                return outcome;
            }
            return Apply(PlayerAction.UsePowerUp((PowerUpType)powerUpId, t));
        }

        /// <summary>Story only: resume a lost stage with extra moves/time (paid in orbes by the caller).</summary>
        public ActionOutcome Continue(int timestampMs) => Apply(PlayerAction.Continue(timestampMs));

        public ActionOutcome Apply(PlayerAction action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }
            if (action.Type == ActionType.Continue)
            {
                return ApplyContinue(action);
            }
            if (State != SessionState.Running)
            {
                return Reject(action, ErrorCode.SessionOver);
            }

            ErrorCode timing = CheckTiming(action.TimestampMs);
            if (timing != ErrorCode.None)
            {
                return Reject(action, timing);
            }

            int t = action.TimestampMs;
            if (t > LastKnownTimeMs)
            {
                LastKnownTimeMs = t;
            }
            _cascade.Update(t);
            RecordCheckpointsUpTo(t);

            ResolutionResult resolution = null;
            int baseDuration;

            if (action.Type == ActionType.Swap)
            {
                OperationResult<ResolutionResult> swap = _boardManager.TrySwap(action.From, action.To, OptionsAt(t));
                if (!swap.Success)
                {
                    return Reject(action, swap.Error);
                }
                resolution = swap.Value;
                MovesUsed++;
                if (Config.HasMoveLimit)
                {
                    MovesLeft--;
                }
                baseDuration = _balance.Timing.SwapAnimationMs;
            }
            else
            {
                ErrorCode error = _powerUps.CanActivate(action.PowerUp, action.HasTarget);
                if (error != ErrorCode.None)
                {
                    return Reject(action, error);
                }
                if (action.HasTarget && !Board.InBounds(action.Target))
                {
                    return Reject(action, ErrorCode.InvalidArgument);
                }

                _powerUps.Consume(action.PowerUp, t);
                OnPowerUpActivated?.Invoke(action.PowerUp);

                if (action.PowerUp == PowerUpType.NuclearBomb)
                {
                    resolution = _boardManager.ClearCells(PowerUpManager.GetNuclearCells(Board, action.Target, _balance.PowerUps.NuclearRadius), ClearCause.PowerUp, OptionsAt(t));
                }
                else if (action.PowerUp == PowerUpType.FireStorm)
                {
                    resolution = _boardManager.ClearCells(PowerUpManager.GetFireStormCells(Board), ClearCause.PowerUp, OptionsAt(t));
                }
                baseDuration = _balance.Timing.PowerUpAnimationMs;
            }

            var outcome = new ActionOutcome(action) { Accepted = true, Resolution = resolution };
            if (resolution != null)
            {
                if (resolution.GoldenChainConsumed)
                {
                    _powerUps.OnGoldenChainConsumed();
                }
                ScoreResolution(resolution, t, outcome);
            }

            if (action.Type == ActionType.Swap)
            {
                TryPetMove(t, outcome);
            }

            int duration = baseDuration;
            if (resolution != null)
            {
                duration += resolution.Steps.Count * _balance.Timing.CascadeStepMs;
                if (resolution.Shuffled)
                {
                    duration += _balance.Timing.ShuffleAnimationMs;
                }
            }
            if (outcome.PetResolution != null)
            {
                duration += _balance.Timing.SwapAnimationMs + outcome.PetResolution.Steps.Count * _balance.Timing.CascadeStepMs;
                if (outcome.PetResolution.Shuffled)
                {
                    duration += _balance.Timing.ShuffleAnimationMs;
                }
            }
            outcome.AnimationDurationMs = duration;

            _lastActionMs = t;
            _nextAllowedMs = t + Math.Max(_balance.Timing.MinActionIntervalMs, duration);

            CheckBossPhase(outcome);
            _objectives.SetScore(Score);
            _recorder.RecordAction(action, Score);

            outcome.ScoreAfter = Score;
            outcome.MovesLeft = MovesLeft;
            OnActionResolved?.Invoke(outcome);

            if (Config.Mode == GameMode.Story)
            {
                if (_objectives.AllComplete)
                {
                    End(SessionState.Won, t);
                }
                else if (Config.HasMoveLimit && MovesLeft <= 0)
                {
                    End(SessionState.Lost, t);
                }
            }

            outcome.StateAfter = State;
            return outcome;
        }

        /// <summary>Advances the session clock: time warning, Red Surge end, time-up.</summary>
        public void Tick(int nowMs)
        {
            if (nowMs > LastKnownTimeMs)
            {
                LastKnownTimeMs = nowMs;
            }
            _cascade.Update(nowMs);
            if (State != SessionState.Running)
            {
                return;
            }

            int remaining = TimeLimitMs - nowMs;
            // Moves stages have no visible clock: no "hurry up" warning there.
            if (!_timeWarningSent && !Config.HasMoveLimit && remaining > 0 && remaining <= _balance.Timing.TimeWarningMs)
            {
                _timeWarningSent = true;
                OnTimeWarning?.Invoke(remaining);
            }
            if (nowMs >= TimeLimitMs)
            {
                FinishByTime();
            }
        }

        /// <summary>Ends the session at its time limit (used by Tick, replay validation and ghost playback).</summary>
        public void FinishByTime()
        {
            if (State != SessionState.Running)
            {
                return;
            }
            SessionState state = Config.Mode == GameMode.Story
                ? (_objectives.AllComplete ? SessionState.Won : SessionState.Lost)
                : SessionState.Completed;
            End(state, Math.Max(TimeLimitMs, _lastActionMs));
        }

        public void Abandon(int nowMs)
        {
            if (State != SessionState.Running)
            {
                return;
            }
            End(SessionState.Abandoned, Math.Max(nowMs, _lastActionMs));
        }

        /// <summary>Snapshot of the statistics (final once the session is over).</summary>
        public StageResult GetResult()
        {
            var used = new Dictionary<PowerUpType, int>();
            foreach (KeyValuePair<PowerUpType, int> kv in _powerUps.UsedThisMatch)
            {
                used[kv.Key] = kv.Value;
            }

            return new StageResult
            {
                Mode = Config.Mode,
                StageId = Config.StageId,
                State = State,
                FinalScore = Score,
                BonusPoints = _bonusPoints,
                Stars = ComputeStars(),
                MovesUsed = MovesUsed,
                DurationMs = _endTimeMs >= 0 ? _endTimeMs : LastKnownTimeMs,
                TotalCascades = _cascade.TotalCascades,
                MaxCascadeLevel = _cascade.MaxCascadeLevel,
                MegaCascades = _cascade.MegaCascades,
                RedSurgeActivations = _cascade.SurgeActivations,
                SpecialsCreated = _specialsCreated,
                SpecialsActivated = _specialsActivated,
                Line5Matches = _line5Matches,
                ClearedByColor = (int[])_clearedByColor.Clone(),
                StonesDestroyed = _stonesDestroyed,
                IceBroken = _iceBroken,
                ContinuesUsed = ContinuesUsed,
                PowerUpsUsed = used,
                Replay = _recorder.Data
            };
        }

        // ------------------------------------------------------------------ internals

        private ResolveOptions OptionsAt(int t) => new ResolveOptions
        {
            GoldenChainArmed = _powerUps.GoldenChainArmed,
            BrightSparkActive = _powerUps.IsBrightSparkActive(t)
        };

        /// <summary>
        /// After every player swap the pet rolls Level x 1% (one roll per swap, always consumed so the sequence is
        /// replay-stable). On success it plays the current best move for free: scored, but no move is spent.
        /// </summary>
        private void TryPetMove(int t, ActionOutcome outcome)
        {
            if (_petRng == null)
            {
                return;
            }
            bool acts = _petRng.ChancePermille(Config.PetLevel * _balance.Pets.AutoMovePermillePerLevel);
            if (!acts || State != SessionState.Running)
            {
                return;
            }
            Move? hint = _boardManager.GetHint();
            if (!hint.HasValue)
            {
                return;
            }
            OperationResult<ResolutionResult> swap = _boardManager.TrySwap(hint.Value.From, hint.Value.To, OptionsAt(t));
            if (!swap.Success)
            {
                return;
            }

            var petOutcome = new ActionOutcome(outcome.Action);
            if (swap.Value.GoldenChainConsumed)
            {
                _powerUps.OnGoldenChainConsumed();
            }
            ScoreResolution(swap.Value, t, petOutcome);
            outcome.PetMove = hint;
            outcome.PetResolution = swap.Value;
            outcome.PetPoints = petOutcome.PointsGained;
            outcome.PointsGained += petOutcome.PointsGained;
            outcome.RedSurgeActivated |= petOutcome.RedSurgeActivated;
            outcome.RedSurgeActive |= petOutcome.RedSurgeActive;
        }

        private ErrorCode CheckTiming(int t)
        {
            if (t < 0)
            {
                return ErrorCode.InvalidArgument;
            }
            if (t < _lastActionMs)
            {
                return ErrorCode.TimestampOutOfOrder;
            }
            if (t < _nextAllowedMs)
            {
                return ErrorCode.TooFast;
            }
            if (t >= TimeLimitMs)
            {
                return ErrorCode.TimeExpired;
            }
            return ErrorCode.None;
        }

        private void ScoreResolution(ResolutionResult resolution, int t, ActionOutcome outcome)
        {
            _cascade.BeginSequence(t);
            int boost = _powerUps.GetCascadeBoostPermille(t);
            int fill = 0;

            foreach (ResolutionStep step in resolution.Steps)
            {
                _cascade.ScoreStep(step, boost);
                fill += _cascade.GetComboFill(step);
                _objectives.ApplyStep(step);

                for (int c = 0; c < _clearedByColor.Length; c++)
                {
                    _clearedByColor[c] += step.ClearedByColor[c];
                }
                _stonesDestroyed += step.StonesDestroyed;
                _iceBroken += step.IceBroken.Count;
                _specialsCreated += step.SpecialsCreated.Count;
                _specialsActivated += step.SpecialsActivated;
                foreach (MatchGroup g in step.Groups)
                {
                    if (g.Shape == MatchShape.Line5)
                    {
                        _line5Matches++;
                    }
                }

                if (step.Groups.Count > 0)
                {
                    OnMatch?.Invoke(step);
                }
                if (step.CascadeLevel >= 1)
                {
                    OnCascade?.Invoke(step.CascadeLevel);
                }
            }

            long total = _cascade.FinishSequence(resolution.CascadeCount);
            int surge = _cascade.GetComboMultiplierPermille(t);
            int power = _powerUps.GetScoreMultiplierPermille(t);
            long multiplier = Math.Min(_balance.Scoring.MaxScoreMultiplierPermille, (long)surge * power / 1000);
            long gained = total * multiplier / 1000;

            Score += gained;
            outcome.PointsGained = gained;
            outcome.ScoreMultiplierPermille = (int)multiplier;
            outcome.RedSurgeActive = surge > 1000;
            outcome.RedSurgeActivated = _cascade.AddComboFill(fill, t);

            OnComboMeterChanged?.Invoke(_cascade.ComboMeterPermille);
            OnScoreChanged?.Invoke(Score, gained);
        }

        private void CheckBossPhase(ActionOutcome outcome)
        {
            if (Config.BossPhases <= 1 || Config.BossHp <= 0)
            {
                return;
            }

            while (_bossPhase < Config.BossPhases - 1 && Score * Config.BossPhases >= Config.BossHp * (_bossPhase + 1))
            {
                _bossPhase++;
                List<Pos> stones = _boardManager.ApplyBossPhase(_bossPhase, Config.BossStonesPerPhase);
                outcome.BossPhaseReached = _bossPhase;
                outcome.BossStones.AddRange(stones);
                OnBossPhaseChanged?.Invoke(_bossPhase, stones);
            }
        }

        private ActionOutcome ApplyContinue(PlayerAction action)
        {
            if (Config.Mode != GameMode.Story)
            {
                return Reject(action, ErrorCode.PowerUpNotAvailableInMode);
            }
            if (State != SessionState.Lost)
            {
                return Reject(action, ErrorCode.InvalidArgument);
            }
            if (action.TimestampMs < _endTimeMs)
            {
                return Reject(action, ErrorCode.TimestampOutOfOrder);
            }
            if (ContinuesUsed >= SessionConfig.MaxContinues)
            {
                return Reject(action, ErrorCode.LimitReached);
            }

            int t = action.TimestampMs;
            int idleGap = Math.Max(0, t - TimeLimitMs);
            _continueExtraTimeMs += _balance.Stamina.ContinueExtraTimeMs + idleGap;
            if (Config.HasMoveLimit)
            {
                MovesLeft += _balance.Stamina.ContinueExtraMoves;
            }

            ContinuesUsed++;
            State = SessionState.Running;
            _endTimeMs = -1;
            _timeWarningSent = false;
            _lastActionMs = t;
            _nextAllowedMs = t + _balance.Timing.MinActionIntervalMs;
            if (t > LastKnownTimeMs)
            {
                LastKnownTimeMs = t;
            }

            _recorder.Reopen();
            RecordCheckpointsUpTo(t);
            _recorder.RecordAction(action, Score);

            var outcome = new ActionOutcome(action)
            {
                Accepted = true,
                ScoreAfter = Score,
                MovesLeft = MovesLeft,
                AnimationDurationMs = _balance.Timing.MinActionIntervalMs,
                StateAfter = State
            };
            OnActionResolved?.Invoke(outcome);
            return outcome;
        }

        private void End(SessionState state, int endTimeMs)
        {
            RecordCheckpointsUpTo(endTimeMs);

            if (state == SessionState.Won)
            {
                long bonus = 0;
                if (Config.HasMoveLimit && MovesLeft > 0)
                {
                    bonus += (long)MovesLeft * _balance.Scoring.RemainingMoveBonus;
                }
                if (!Config.HasMoveLimit)
                {
                    // Only timed stages pay for the time left (moves stages pay for the moves left).
                    int secondsLeft = Math.Max(0, TimeLimitMs - endTimeMs) / 1000;
                    bonus += (long)secondsLeft * _balance.Scoring.RemainingSecondBonus;
                }
                _bonusPoints += bonus;
                Score += bonus;
                if (bonus > 0)
                {
                    OnScoreChanged?.Invoke(Score, bonus);
                }
            }

            State = state;
            _endTimeMs = endTimeMs;
            _recorder.Finish(state, endTimeMs, Score);
            OnGameEnd?.Invoke(state == SessionState.Won, Score);
        }

        private void RecordCheckpointsUpTo(int timeMs)
        {
            int interval = _balance.Timing.CheckpointIntervalMs;
            while (_nextCheckpointMs <= timeMs)
            {
                _recorder.RecordCheckpoint(_nextCheckpointMs, Board.ComputeHash(), Score);
                _nextCheckpointMs += interval;
            }
        }

        private int ComputeStars()
        {
            if (State != SessionState.Won)
            {
                return 0;
            }
            if (Config.ThreeStarScore > 0 && Score >= Config.ThreeStarScore)
            {
                return 3;
            }
            if (Config.TwoStarScore > 0 && Score >= Config.TwoStarScore)
            {
                return 2;
            }
            return 1;
        }

        private ActionOutcome Reject(PlayerAction action, ErrorCode error) =>
            new ActionOutcome(action) { Accepted = false, Error = error, ScoreAfter = Score, MovesLeft = MovesLeft, StateAfter = State };
    }
}
