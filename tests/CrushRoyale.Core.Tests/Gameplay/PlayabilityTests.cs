using System;
using System.Collections.Generic;
using CrushRoyale.Core.Board;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Gameplay;
using CrushRoyale.Core.Story;
using CrushRoyale.Core.Tests.Gameplay;
using Xunit;

namespace CrushRoyale.Core.Tests.Gameplay;

/// <summary>
/// "On rajoute 5 mouvements et on ne peut pas jouer." The bug that cost a weekend of trust was a board the player
/// could not touch, and no unit test saw it because every test drove the engine through a hint that happened to
/// exist. These tests walk real stages from every band of the campaign and check the only rule that matters on a
/// device: while a match is running, there is always a move to play — including right after a paid continue.
/// </summary>
public sealed class PlayabilityTests
{
    private static readonly GameBalance Balance = GameBalance.CreateDefault();

    /// <summary>Stages from every band, including the three mechanic introductions and a few bosses.</summary>
    public static TheoryData<int> SampledStages()
    {
        var data = new TheoryData<int>();
        foreach (int id in new[]
                 {
                     1, 7, 23, 50, 99,
                     StageCatalog.TimeBombIntroStage, 137, 175, 200,
                     StageCatalog.BlightIntroStage, 244, 280,
                     StageCatalog.EggIntroStage, 355, 400,
                     461, 500, 577, 640, 700, 783, 850, 921, 1000
                 })
        {
            data.Add(id);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(SampledStages))]
    public void RunningStage_AlwaysOffersAMove(int stageId)
    {
        var catalog = new StageCatalog(Balance);
        StageData stage = catalog.Get(stageId);
        var session = new GameSession(SessionConfig.ForStage(stage, Balance, null, League.Master), Balance, "p1");

        int played = 0;
        while (session.State == SessionState.Running && played < 200)
        {
            Move? hint = session.BoardManager.GetHint();
            Assert.True(hint.HasValue, $"Stage {stageId} left the player with a dead board after {played} moves.");
            ActionOutcome outcome = session.AttemptMove(hint.Value.From, hint.Value.To, session.NextActionAllowedAtMs + 300);
            Assert.True(outcome.Accepted, $"Stage {stageId} refused its own hint move at move {played}.");
            played++;
        }
        Assert.True(played > 0, $"Stage {stageId} was over before the first move.");
    }

    [Theory]
    [MemberData(nameof(SampledStages))]
    public void Continue_OnAnyStage_GivesBackSomethingToPlayWith(int stageId)
    {
        var catalog = new StageCatalog(Balance);
        StageData stage = catalog.Get(stageId);
        var session = new GameSession(SessionConfig.ForStage(stage, Balance, null, League.Master), Balance, "p1");

        // Lose the stage the way a player does: play until the moves or the clock run out.
        int guard = 0;
        while (session.State == SessionState.Running && guard++ < 400)
        {
            Move? hint = session.BoardManager.GetHint();
            if (!hint.HasValue)
            {
                break;
            }
            session.AttemptMove(hint.Value.From, hint.Value.To, session.NextActionAllowedAtMs + 300);
        }
        if (session.State != SessionState.Lost)
        {
            // Stages the bot wins are not what this test is about; force the other ending with a fresh, starved run.
            stage = catalog.Get(stageId);
            stage.TargetScore = int.MaxValue / 4;
            stage.Objectives.Clear();
            stage.Objectives.Add(new StageObjective { Type = ObjectiveType.ReachScore, Target = stage.TargetScore });
            session = new GameSession(SessionConfig.ForStage(stage, Balance, null, League.Master), Balance, "p1");
            guard = 0;
            while (session.State == SessionState.Running && guard++ < 400)
            {
                Move? hint = session.BoardManager.GetHint();
                if (!hint.HasValue)
                {
                    break;
                }
                session.AttemptMove(hint.Value.From, hint.Value.To, session.NextActionAllowedAtMs + 300);
            }
            session.Tick(session.TimeLimitMs);
        }

        Assert.True(session.State == SessionState.Lost,
            $"Stage {stageId}: expected a lost stage, got {session.State} (moves {session.MovesLeft}, hint {session.BoardManager.GetHint().HasValue}, t {session.LastKnownTimeMs}/{session.TimeLimitMs}).");

        ActionOutcome resumed = session.Continue(Math.Max(session.EndTimeMs, session.LastKnownTimeMs) + 2000);
        Assert.True(resumed.Accepted, $"Stage {stageId} refused a paid continue.");
        Assert.Equal(SessionState.Running, session.State);

        if (session.Config.HasMoveLimit)
        {
            Assert.True(session.MovesLeft >= Balance.Stamina.ContinueExtraMoves,
                $"Stage {stageId} continued with {session.MovesLeft} moves.");
        }
        else
        {
            Assert.True(session.TimeLimitMs > session.LastKnownTimeMs,
                $"Stage {stageId} continued with no time left on the clock.");
        }

        // The whole point: the player can actually play those moves.
        Move? next = session.BoardManager.GetHint();
        Assert.True(next.HasValue, $"Stage {stageId} continued onto a dead board.");
        ActionOutcome first = session.AttemptMove(next.Value.From, next.Value.To, session.NextActionAllowedAtMs + 300);
        Assert.True(first.Accepted, $"Stage {stageId} refused the first move after a continue.");
        // Running, or Won when that first move was the one that finished the stage: both mean the board answered.
        Assert.True(session.State == SessionState.Running || session.State == SessionState.Won,
            $"Stage {stageId} ended as {session.State} on the first move after a continue.");
    }

    /// <summary>Every stage must state a reachable goal: an objective nobody can finish is a wall, not a level.</summary>
    [Fact]
    public void EveryStage_HasObjectivesThatTheBoardCanActuallyServe()
    {
        var catalog = new StageCatalog(Balance);
        var problems = new List<string>();
        for (int id = 1; id <= catalog.TotalCampaignStages; id++)
        {
            StageData stage = catalog.Get(id);
            if (stage.Objectives.Count == 0)
            {
                problems.Add($"stage {id}: no objective");
                continue;
            }
            if (stage.MoveLimit <= 0 && stage.TimeLimitMs <= 0)
            {
                problems.Add($"stage {id}: neither moves nor time");
            }
            foreach (StageObjective objective in stage.Objectives)
            {
                if (objective.Target <= 0)
                {
                    problems.Add($"stage {id}: objective {objective.Type} targets {objective.Target}");
                }
            }
        }
        Assert.Empty(problems);
    }

    /// <summary>
    /// Boosts bought before a stage felt useless because a timed effect does nothing on a stage counted in moves.
    /// Anything that lasts must therefore last in moves too, unless it only exists in PvP (which is always timed).
    /// </summary>
    [Fact]
    public void EveryTimedBoost_AlsoLastsInMoves()
    {
        var useless = new List<string>();
        foreach (PowerUpDefinition def in Balance.PowerUps.Definitions)
        {
            if (def.DurationMs > 0 && def.DurationMoves <= 0 && !def.PvpOnly)
            {
                useless.Add($"{def.Type} lasts {def.DurationMs} ms and 0 moves");
            }
        }
        Assert.Empty(useless);
    }

    /// <summary>
    /// No score target may ask for more than a mid-skill run actually reaches on that board. Before this rule the
    /// campaign had 22 stages an average run never cleared even with the difficulty assist; a board the expert bot
    /// exploits is a wall for everyone else, and no global difficulty curve can see that.
    /// </summary>
    [Fact]
    public void NoStageAsksForMoreThanAMidRunReaches()
    {
        var catalog = new StageCatalog(Balance);
        var problems = new List<string>();
        for (int id = 1; id <= catalog.TotalCampaignStages; id++)
        {
            StageData stage = catalog.Get(id);
            foreach (StageObjective objective in stage.Objectives)
            {
                if (objective.Type != ObjectiveType.ReachScore)
                {
                    continue;
                }
                int reach = StageCatalog.MidReach(id);
                if (reach > 0 && objective.Target > reach)
                {
                    problems.Add($"stage {id}: asks {objective.Target}, a mid run reaches {reach}");
                }
            }
        }
        Assert.Empty(problems);
    }
}
