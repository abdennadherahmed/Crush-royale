using System;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Gameplay;

namespace CrushRoyale.Core.Replay
{
    /// <summary>Outcome of re-simulating a replay.</summary>
    public sealed class ReplayVerification
    {
        public bool Valid { get; internal set; }

        public ErrorCode Error { get; internal set; }

        public string Reason { get; internal set; }

        /// <summary>Index of the first action that diverged, or -1.</summary>
        public int FailedActionIndex { get; internal set; } = -1;

        /// <summary>The re-simulated session (authoritative score, stats, used power-ups).</summary>
        public GameSession Session { get; internal set; }

        public long AuthoritativeScore => Session?.Score ?? 0;

        internal static ReplayVerification Fail(ErrorCode error, string reason, GameSession session, int index = -1) =>
            new ReplayVerification { Valid = false, Error = error, Reason = reason, Session = session, FailedActionIndex = index };
    }

    /// <summary>
    /// Re-runs a replay through a fresh <see cref="GameSession"/> and checks that every action is legal,
    /// every claimed score matches, the end state matches and the board-hash checkpoints are identical.
    /// Used by the server (anti-cheat, PvP results) and by the client (ghost playback sanity check).
    /// </summary>
    public static class ReplaySimulator
    {
        public static ReplayVerification Verify(ReplayData replay, SessionConfig config, GameBalance balance, bool requireFinished = true)
        {
            if (replay == null)
            {
                throw new ArgumentNullException(nameof(replay));
            }
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }
            if (balance == null)
            {
                throw new ArgumentNullException(nameof(balance));
            }

            if (replay.RulesVersion != GameBalance.RulesVersion || replay.BalanceHash != balance.ComputeHash())
            {
                // Not cheating: the client runs other rules. The caller should ask for an update, not punish.
                return ReplayVerification.Fail(ErrorCode.VersionMismatch, "Replay was recorded with different rules or balance.", null);
            }
            if (replay.Mode != config.Mode || replay.Seed != config.Seed || replay.StageId != config.StageId
                || replay.Pet != config.Pet || replay.PetLevel != config.PetLevel)
            {
                return ReplayVerification.Fail(ErrorCode.ReplayMismatch, "Replay header does not match the expected match.", null);
            }
            if (requireFinished && !replay.IsFinished)
            {
                return ReplayVerification.Fail(ErrorCode.ReplayInvalid, "Replay is not finished.", null);
            }

            GameSession session;
            try
            {
                session = new GameSession(config, balance, replay.PlayerId);
            }
            catch (ArgumentException ex)
            {
                return ReplayVerification.Fail(ErrorCode.ReplayInvalid, ex.Message, null);
            }

            for (int i = 0; i < replay.Actions.Count; i++)
            {
                ReplayAction ra = replay.Actions[i];
                PlayerAction action = ra.Action;

                if (action.Type == ActionType.Continue && session.State == SessionState.Running)
                {
                    // The stage was lost on time before the player paid to continue.
                    session.FinishByTime();
                }
                if (action.Type != ActionType.Continue && session.State != SessionState.Running)
                {
                    return ReplayVerification.Fail(ErrorCode.ReplayMismatch, "Action after the end of the match.", session, i);
                }

                ActionOutcome outcome = session.Apply(action);
                if (!outcome.Accepted)
                {
                    return ReplayVerification.Fail(ErrorCode.ReplayMismatch, "Illegal action " + action + ": " + outcome.Error, session, i);
                }
                // ScoreAfter is recorded before the end-of-stage bonus, which is only part of FinalScore.
                if (outcome.ScoreAfter != ra.ScoreAfter)
                {
                    return ReplayVerification.Fail(ErrorCode.ReplayMismatch, "Score mismatch after " + action + ": claimed " + ra.ScoreAfter + ", simulated " + outcome.ScoreAfter, session, i);
                }
            }

            if (replay.IsFinished && session.State == SessionState.Running)
            {
                if (replay.EndState == SessionState.Abandoned)
                {
                    session.Abandon(replay.EndTimeMs);
                }
                else
                {
                    session.FinishByTime();
                }
            }

            if (replay.IsFinished)
            {
                if (session.State != replay.EndState)
                {
                    return ReplayVerification.Fail(ErrorCode.ReplayMismatch, "End state mismatch: claimed " + replay.EndState + ", simulated " + session.State, session);
                }
                if (session.Score != replay.FinalScore)
                {
                    return ReplayVerification.Fail(ErrorCode.ReplayMismatch, "Final score mismatch: claimed " + replay.FinalScore + ", simulated " + session.Score, session);
                }
                if (session.EndTimeMs != replay.EndTimeMs)
                {
                    return ReplayVerification.Fail(ErrorCode.ReplayMismatch, "End time mismatch.", session);
                }
            }

            var simulated = session.Replay.Checkpoints;
            if (simulated.Count != replay.Checkpoints.Count)
            {
                return ReplayVerification.Fail(ErrorCode.ReplayMismatch, "Checkpoint count mismatch.", session);
            }
            for (int i = 0; i < simulated.Count; i++)
            {
                if (simulated[i].TimeMs != replay.Checkpoints[i].TimeMs || simulated[i].BoardHash != replay.Checkpoints[i].BoardHash || simulated[i].Score != replay.Checkpoints[i].Score)
                {
                    return ReplayVerification.Fail(ErrorCode.ReplayMismatch, "Board desync at " + replay.Checkpoints[i].TimeMs + " ms.", session);
                }
            }

            return new ReplayVerification { Valid = true, Error = ErrorCode.None, Session = session };
        }
    }
}
