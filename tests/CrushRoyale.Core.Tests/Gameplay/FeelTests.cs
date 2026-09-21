using CrushRoyale.Core.Board;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Gameplay;
using CrushRoyale.Core.Story;
using Xunit;

namespace CrushRoyale.Core.Tests.Gameplay;

/// <summary>
/// Things the owner hit while playing: square matches, stars that ignored how easily a stage was won, and a paid
/// continue that has to give a usable board back.
/// </summary>
public sealed class FeelTests
{
    private static readonly GameBalance Balance = GameBalance.CreateDefault();

    private static ResolutionEngine Engine(GameBoard board) =>
        new(Balance, new RefillSpawner(1, board.Width, 6), new DeterministicRandom(1));

    [Fact]
    public void SquareOfFour_IsAValidSwap_AndCreatesACrossBomb()
    {
        // Swapping (1,1) with (1,2) puts a red 2x2 block at x=0..1, y=1..2 without any line of three.
        var board = GameBoard.FromDebugString(@"
            B. G. Y. P. O.
            G. Y. P. O. B.
            R. R. P. G. G.
            R. B. B. O. Y.
            P. R. G. B. P.");
        Assert.False(MatchFinder.HasAnyMatch(board));
        Assert.Equal(ErrorCode.None, MoveFinder.CheckSwap(board, new Pos(1, 0), new Pos(1, 1)));

        ResolutionStep step = Engine(board).ResolveSwap(board, new Pos(1, 0), new Pos(1, 1)).Value.Steps[0];
        Assert.Equal(MatchShape.Square, step.Groups[0].Shape);
        PieceSpawn special = Assert.Single(step.SpecialsCreated);
        Assert.Equal(PieceType.Cross, special.Piece.Type);
        Assert.Equal(PieceColor.Red, special.Piece.Color);
    }

    [Fact]
    public void CrossBomb_ClearsItsRowAndColumn()
    {
        var board = GameBoard.FromDebugString(@"
            B. G. Y. P. O.
            G. Y. P. O. B.
            Y. P. Rx G. G.
            R. R. B. R. Y.
            P. O. G. B. P.");
        // The cross is dragged into the red line below: it detonates with the match.
        ResolutionStep step = Engine(board).ResolveSwap(board, new Pos(2, 1), new Pos(2, 2)).Value.Steps[0];
        Assert.Equal(1, step.SpecialsActivated);
        // Row 1 and column 2 are fully cleared (5 + 5 cells, sharing the cross cell).
        Assert.True(step.Cleared.Count >= 9);
    }

    [Fact]
    public void Stars_RewardWinningWithMovesToSpare()
    {
        // A goal reached almost instantly used to be worth a single star because the score stayed low: the star
        // thresholds here are out of reach on purpose, only the unused moves can earn the stars.
        StageData stage = Fixtures.Stage(moves: 20, target: 200);
        stage.TwoStarScore = 1_000_000;
        stage.ThreeStarScore = 2_000_000;
        var won = new GameSession(SessionConfig.ForStage(stage, Balance, null, League.Master), Balance, "p1");
        while (won.IsRunning)
        {
            Fixtures.PlayHint(won, 300);
        }
        StageResult result = won.GetResult();
        Assert.True(result.Won);
        // Two moves out of twenty means 90% of the moves left: a three-star performance whatever the score.
        Assert.Equal(3, result.Stars);
        Assert.True(result.MovesUsed <= 4);
    }

    [Fact]
    public void Continue_AfterLoss_GivesMovesBack()
    {
        StageData stage = Fixtures.Stage(moves: 2, target: 5_000_000);
        var session = new GameSession(SessionConfig.ForStage(stage, Balance, null, League.Master), Balance, "p1");
        Fixtures.PlayHint(session, 300);
        Fixtures.PlayHint(session, 300);
        Assert.Equal(SessionState.Lost, session.State);

        ActionOutcome resumed = session.Continue(session.EndTimeMs + 2000);
        Assert.True(resumed.Accepted);
        Assert.Equal(SessionState.Running, session.State);
        Assert.Equal(Balance.Stamina.ContinueExtraMoves, session.MovesLeft);
        // The board must still accept a move (the UI bug was on the client, this guards the engine side).
        Assert.True(Fixtures.PlayHint(session, 300).Accepted);
    }
}
