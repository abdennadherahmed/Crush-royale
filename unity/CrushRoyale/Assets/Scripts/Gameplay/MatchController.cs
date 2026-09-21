using System;
using System.Collections;
using System.Collections.Generic;
using CrushRoyale.Core.Board;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Gameplay;
using CrushRoyale.Core.Pvp;
using CrushRoyale.Core.Scoring;
using CrushRoyale.Game.Audio;
using UnityEngine;

namespace CrushRoyale.Game.Gameplay
{
    /// <summary>
    /// Drives a <see cref="GameSession"/> on Unity's main thread: input queue, session clock, animations, ghost playback,
    /// idle hints, audio/haptic feedback. The simulation itself never touches Unity APIs or Time.deltaTime.
    /// </summary>
    public sealed class MatchController : MonoBehaviour
    {
        /// <summary>Idle time (after the board has settled) before the best move is suggested.</summary>
        private const int HintDelayMs = 5000;

        private readonly Queue<Func<int, PlayerAction>> _inputs = new Queue<Func<int, PlayerAction>>();
        private GameRoot _game;
        private bool _animating;
        private bool _finished;
        private int _lastActivityMs;
        private bool _hintShown;
        private GhostPlayer _ghost;
        private BoardView _ghostBoard;

        public GameSession Session { get; private set; }

        public MatchClock Clock { get; } = new MatchClock();

        public BoardView Board { get; private set; }

        public BoardInput Input { get; private set; }

        public GhostPlayer Ghost => _ghost;

        public bool IsPaused => Clock.IsPaused;

        /// <summary>Raised after every visible change (score, moves, meter...).</summary>
        public event Action Changed;

        /// <summary>Raised once when the session ends (won, lost, completed, abandoned).</summary>
        public event Action<StageResult> Finished;

        public event Action<ActionOutcome> ActionPresented;

        /// <summary>Raised when a resolution step starts animating (combo texts, pet and hero reactions).</summary>
        public event Action<ResolutionStep> StepPlayed;

        /// <summary>Raised when an accepted action starts animating (before the board moves).</summary>
        public event Action<ActionOutcome> ActionStarted;

        public void Begin(GameSession session, BoardView board, BoardInput input, GhostPlayer ghost, BoardView ghostBoard)
        {
            _game = GameRoot.Instance;
            Session = session;
            Board = board;
            Input = input;
            _ghost = ghost;
            _ghostBoard = ghostBoard;

            board.Bind(session.Board, _game.Backend.Balance, _game.Save.Settings.ColorBlindMode);
            input.Board = board;
            input.SwapRequested += (a, b) => Enqueue(t => PlayerAction.Swap(a, b, t));

            session.OnCascadeAnimation += OnCascadeAnimation;
            session.OnTimeWarning += _ => _game.Haptics.Medium();
            session.OnBossPhaseChanged += (_, __) =>
            {
                _game.Audio.PlaySFX(SoundIds.Explosion, 0.8f);
                _game.Haptics.Heavy();
            };

            if (_ghost != null && _ghostBoard != null)
            {
                _ghostBoard.Bind(_ghost.Session.Board, _game.Backend.Balance, _game.Save.Settings.ColorBlindMode);
                _ghost.OnGhostAction += outcome =>
                {
                    if (_ghostBoard != null && isActiveAndEnabled)
                    {
                        StartCoroutine(_ghostBoard.PlayOutcome(outcome, _ghost.Session.Board, null));
                    }
                };
            }

            Clock.Start();
            _lastActivityMs = 0;
            Changed?.Invoke();
        }

        public void Enqueue(Func<int, PlayerAction> actionAtTime)
        {
            if (_finished || Clock.IsPaused || _inputs.Count > 1)
            {
                return;
            }
            _inputs.Enqueue(actionAtTime);
        }

        public void UsePowerUp(PowerUpType type, Pos? target) => Enqueue(t => PlayerAction.UsePowerUp(type, t, target));

        public void Pause()
        {
            if (Session.Config.IsPvp || Session.Config.Mode == GameMode.GuildBoss)
            {
                return;
            }
            Clock.Pause();
        }

        public void Resume() => Clock.Resume();

        /// <summary>Applies a paid/free continue authorized by the server (story only).</summary>
        public bool Continue()
        {
            ActionOutcome outcome = Session.Continue(Math.Max(Clock.NowMs, Session.EndTimeMs));
            if (!outcome.Accepted)
            {
                return false;
            }
            _finished = false;
            // Finish() disabled the board when the stage was lost: without this the continued stage was unplayable.
            Input.Interactable = !_animating;
            Clock.Resume();
            Changed?.Invoke();
            return true;
        }

        public void Quit()
        {
            if (Session.IsRunning)
            {
                Session.Abandon(Clock.NowMs);
            }
            _finished = true;
            Clock.Stop();
        }

        private void Update()
        {
            if (Session == null || _finished)
            {
                return;
            }

            int now = Clock.NowMs;
            if (!Clock.IsPaused)
            {
                Session.Tick(now);
                _ghost?.AdvanceTo(now);
            }

            if (!Session.IsRunning && !_animating)
            {
                Finish();
                return;
            }

            if (_animating || Clock.IsPaused || _inputs.Count == 0)
            {
                UpdateHint(now);
                return;
            }

            Func<int, PlayerAction> build = _inputs.Dequeue();
            int t = Math.Max(now, Session.NextActionAllowedAtMs);
            if (t >= Session.TimeLimitMs)
            {
                return;
            }

            ActionOutcome outcome = Session.Apply(build(t));
            _lastActivityMs = now;
            if (_hintShown)
            {
                Board.ClearHint();
                _hintShown = false;
            }

            if (outcome.Accepted)
            {
                StartCoroutine(Present(outcome));
            }
            else if (outcome.Error == ErrorCode.NoMatch && outcome.Action.Type == ActionType.Swap)
            {
                _game.Audio.PlaySFX(SoundIds.Invalid);
                StartCoroutine(PresentInvalid(outcome.Action.From, outcome.Action.To));
            }
            else if (outcome.Error != ErrorCode.None)
            {
                _game.UI.Toast(_game.Loc.T("error." + outcome.Error));
            }
        }

        private IEnumerator Present(ActionOutcome outcome)
        {
            _animating = true;
            Input.Interactable = false;
            if (outcome.Action.Type == ActionType.PowerUp)
            {
                _game.Audio.PlaySFX(outcome.Action.PowerUp == PowerUpType.NuclearBomb || outcome.Action.PowerUp == PowerUpType.FireStorm ? SoundIds.Explosion : SoundIds.PowerUp);
                _game.Haptics.Medium();
            }

            ActionStarted?.Invoke(outcome);
            yield return Board.PlayOutcome(outcome, Session.Board, OnStep);

            if (outcome.RedSurgeActivated)
            {
                _game.Audio.PlaySFX(SoundIds.RedSurge);
                _game.Haptics.Heavy();
            }
            if (outcome.Resolution != null && outcome.Resolution.CascadeCount >= 2)
            {
                _game.Audio.PlaySFX(SoundIds.CascadeComplete);
            }

            _animating = false;
            Input.Interactable = true;
            ActionPresented?.Invoke(outcome);
            Changed?.Invoke();
        }

        private IEnumerator PresentInvalid(Pos a, Pos b)
        {
            _animating = true;
            yield return Board.PlayInvalidSwap(a, b);
            _animating = false;
        }

        private void OnStep(ResolutionStep step)
        {
            StepPlayed?.Invoke(step);
            if (step.CascadeLevel >= 1)
            {
                _game.Audio.PlaySFX(SoundIds.Cascade, 1f + Mathf.Min(step.CascadeLevel, 6) * 0.08f);
                return;
            }
            MatchShape best = MatchShape.Line3;
            foreach (MatchGroup group in step.Groups)
            {
                if (group.Shape > best)
                {
                    best = group.Shape;
                }
            }
            _game.Audio.PlaySFX(best == MatchShape.Line5 ? SoundIds.Match5 : best >= MatchShape.Line4 ? SoundIds.Match4 : SoundIds.Match3);
            _game.Haptics.Light();
        }

        private void OnCascadeAnimation(CascadeAnimationTrigger trigger)
        {
            if (trigger.Kind == CascadeAnimationKind.MegaCascade)
            {
                _game.Audio.PlaySFX(SoundIds.MegaCascade);
                _game.Haptics.Heavy();
            }
            Changed?.Invoke();
        }

        private void UpdateHint(int now)
        {
            if (_animating || Clock.IsPaused)
            {
                // The idle countdown starts once cascades have finished.
                _lastActivityMs = now;
                return;
            }
            if (_hintShown || now - _lastActivityMs < HintDelayMs || !Session.IsRunning)
            {
                return;
            }
            Move? hint = Session.BoardManager.GetHint();
            if (hint.HasValue)
            {
                Board.ShowHint(hint.Value);
                _hintShown = true;
            }
        }

        private void Finish()
        {
            if (_finished)
            {
                return;
            }
            _finished = true;
            Clock.Pause();
            Input.Interactable = false;
            if (_ghost != null)
            {
                _ghost.SeekTo(int.MaxValue / 2);
            }
            Changed?.Invoke();
            Finished?.Invoke(Session.GetResult());
        }
    }
}
