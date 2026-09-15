using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CrushRoyale.Core.Board;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Story;

namespace CrushRoyale.Core.Gameplay
{
    /// <summary>Session clock (ms since match start). Unity: pause-aware stopwatch; tests: manual.</summary>
    public interface IGameTimeSource
    {
        int NowMs { get; }
    }

    /// <summary>Supplies player actions (touch input, bot, network ghost...).</summary>
    public interface IActionSource
    {
        bool TryGetAction(GameSession session, int nowMs, out PlayerAction action);
    }

    /// <summary>Animates outcomes. Awaiting lets the loop wait for animations without blocking the frame.</summary>
    public interface IActionPresenter
    {
        Task PresentAsync(ActionOutcome outcome, CancellationToken cancellationToken);

        Task NextFrameAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// Task 3 orchestrator: runs a session against an input source and a presenter.
    /// Gameplay time never uses Time.deltaTime: actions carry session timestamps, and input is clamped to
    /// the moment the previous animation finished, so the client can never produce a replay the server rejects.
    /// </summary>
    public sealed class GameplayCore
    {
        private readonly GameBalance _balance;

        public GameplayCore(GameBalance balance)
        {
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
        }

        public GameSession CreateStageSession(StageData stage, IEnumerable<LoadoutEntry> loadout, League highestLeague, int assistExtraMoves = 0, string playerId = null)
        {
            return new GameSession(SessionConfig.ForStage(stage, _balance, loadout, highestLeague, assistExtraMoves), _balance, playerId);
        }

        /// <summary>Prompt API: plays a story stage to completion.</summary>
        public Task<StageResult> PlayStage(StageData stage, IActionSource input, IActionPresenter presenter, IGameTimeSource time, CancellationToken cancellationToken = default)
        {
            GameSession session = CreateStageSession(stage, null, League.Bronze);
            return RunAsync(session, input, presenter, time, cancellationToken);
        }

        public async Task<StageResult> RunAsync(GameSession session, IActionSource input, IActionPresenter presenter, IGameTimeSource time, CancellationToken cancellationToken = default)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (input == null)
            {
                throw new ArgumentNullException(nameof(input));
            }
            if (presenter == null)
            {
                throw new ArgumentNullException(nameof(presenter));
            }
            if (time == null)
            {
                throw new ArgumentNullException(nameof(time));
            }

            while (session.IsRunning)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int now = time.NowMs;
                session.Tick(now);
                if (!session.IsRunning)
                {
                    break;
                }

                if (input.TryGetAction(session, now, out PlayerAction action) && action != null)
                {
                    if (action.Type != ActionType.Continue && action.TimestampMs < session.NextActionAllowedAtMs)
                    {
                        action = action.WithTimestamp(session.NextActionAllowedAtMs);
                    }

                    ActionOutcome outcome = session.Apply(action);
                    if (outcome.Accepted || outcome.Error == Common.ErrorCode.NoMatch)
                    {
                        await presenter.PresentAsync(outcome, cancellationToken).ConfigureAwait(false);
                    }
                }
                else
                {
                    await presenter.NextFrameAsync(cancellationToken).ConfigureAwait(false);
                }
            }

            return session.GetResult();
        }
    }

    /// <summary>Strategy for automated players (balancing, tests, tutorial demo, offline ghosts).</summary>
    public interface IBotStrategy
    {
        /// <summary>Returns the next action at time <paramref name="nowMs"/>, or null to stop acting.</summary>
        PlayerAction ChooseAction(GameSession session, int nowMs);
    }

    /// <summary>Picks the move that clears the most gems / builds the strongest bonus right now.</summary>
    public sealed class GreedyBot : IBotStrategy, IActionSource
    {
        private readonly int _thinkTimeMs;

        public GreedyBot(int thinkTimeMs = 900)
        {
            _thinkTimeMs = Math.Max(0, thinkTimeMs);
        }

        public PlayerAction ChooseAction(GameSession session, int nowMs)
        {
            Move? hint = session.BoardManager.GetHint();
            return hint.HasValue ? PlayerAction.Swap(hint.Value.From, hint.Value.To, nowMs) : null;
        }

        public bool TryGetAction(GameSession session, int nowMs, out PlayerAction action)
        {
            action = null;
            if (nowMs < session.NextActionAllowedAtMs + _thinkTimeMs)
            {
                return false;
            }
            action = ChooseAction(session, nowMs);
            return action != null;
        }
    }

    /// <summary>Uniformly random valid moves: a weak baseline player.</summary>
    public sealed class RandomBot : IBotStrategy
    {
        private readonly Common.DeterministicRandom _rng;

        public RandomBot(ulong seed)
        {
            _rng = new Common.DeterministicRandom(seed);
        }

        public PlayerAction ChooseAction(GameSession session, int nowMs)
        {
            List<Move> moves = session.BoardManager.GetValidMoves();
            if (moves.Count == 0)
            {
                return null;
            }
            Move m = moves[_rng.NextInt(moves.Count)];
            return PlayerAction.Swap(m.From, m.To, nowMs);
        }
    }

    /// <summary>Synchronous simulation without animations (server balancing, tests).</summary>
    public static class HeadlessRunner
    {
        public static StageResult Run(GameSession session, IBotStrategy bot, int thinkTimeMs = 900)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (bot == null)
            {
                throw new ArgumentNullException(nameof(bot));
            }

            int guard = 0;
            while (session.IsRunning && guard++ < 100000)
            {
                int t = session.NextActionAllowedAtMs + thinkTimeMs;
                if (t >= session.TimeLimitMs)
                {
                    session.FinishByTime();
                    break;
                }

                PlayerAction action = bot.ChooseAction(session, t);
                if (action == null)
                {
                    session.FinishByTime();
                    break;
                }

                ActionOutcome outcome = session.Apply(action);
                if (!outcome.Accepted)
                {
                    session.Abandon(t);
                    break;
                }
            }
            return session.GetResult();
        }
    }
}
