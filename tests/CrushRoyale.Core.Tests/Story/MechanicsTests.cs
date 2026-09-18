using System.Linq;
using CrushRoyale.Core.Board;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Gameplay;
using CrushRoyale.Core.Replay;
using CrushRoyale.Core.Story;
using CrushRoyale.Core.Tests.Gameplay;
using Xunit;

namespace CrushRoyale.Core.Tests.Story;

/// <summary>New mechanics every 100 stages: countdown bombs (101), spreading blight (201), dragon eggs (301).</summary>
public sealed class MechanicsTests
{
    private static readonly GameBalance Balance = GameBalance.CreateDefault();

    private static ResolutionEngine Engine(GameBoard board) =>
        new(Balance, new RefillSpawner(1, board.Width, 6), new DeterministicRandom(1));

    [Fact]
    public void Catalog_IntroducesOneMechanicEvery100Stages_NeverOnBosses()
    {
        var catalog = new StageCatalog(Balance);
        for (int id = 1; id <= 100; id++)
        {
            StageData early = catalog.Get(id);
            Assert.Equal(0, early.TimeBombCount + early.BlightCount + early.EggCount);
        }
        Assert.True(catalog.Get(StageCatalog.TimeBombIntroStage).TimeBombCount > 0);
        Assert.Equal(0, catalog.Get(StageCatalog.TimeBombIntroStage).BlightCount);
        Assert.True(catalog.Get(StageCatalog.BlightIntroStage).BlightCount > 0);
        Assert.True(catalog.Get(StageCatalog.EggIntroStage).EggCount > 0);
        Assert.All(catalog.GetRange(1, 1000).Where(s => s.IsBoss), s => Assert.Equal(0, s.TimeBombCount + s.BlightCount + s.EggCount));
        // Each mechanic keeps coming back after its introduction.
        Assert.True(catalog.GetRange(102, 200).Count(s => s.TimeBombCount > 0) >= 15);
        Assert.True(catalog.GetRange(302, 400).Count(s => s.EggCount > 0) >= 15);
    }

    [Fact]
    public void Blight_IsBrokenByAnAdjacentMatch()
    {
        var board = GameBoard.FromDebugString(@"
            B. G. Y. P. O.
            G. Y. P. O. B.
            Y. P. R. G. G.
            R. R. B. R. Y.
            P. %1 G. B. P.");
        ResolutionStep step = Engine(board).ResolveSwap(board, new Pos(2, 1), new Pos(2, 2)).Value.Steps[0];
        Assert.Equal(1, step.BlightCleared);
        Assert.Equal(0, step.StonesDestroyed);
    }

    [Fact]
    public void DragonEgg_TakesTwoHits_ThenHatchesIntoABonus()
    {
        var board = GameBoard.FromDebugString(@"
            B. G. Y. P. O.
            G. Y. P. O. B.
            Y. P. R. G. G.
            R. R. B. R. Y.
            P. @1 G. B. P.");
        ResolutionStep step = Engine(board).ResolveSwap(board, new Pos(2, 1), new Pos(2, 2)).Value.Steps[0];
        Assert.Equal(1, step.EggsHatched);
        Assert.Contains(step.SpecialsCreated, s => s.Position == new Pos(1, 0) && s.Piece.IsSpecial);

        var tough = GameBoard.FromDebugString(@"
            B. G. Y. P. O.
            G. Y. P. O. B.
            Y. P. R. G. G.
            R. R. B. R. Y.
            P. @2 G. B. P.");
        ResolutionStep hit = Engine(tough).ResolveSwap(tough, new Pos(2, 1), new Pos(2, 2)).Value.Steps[0];
        Assert.Equal(0, hit.EggsHatched);
        Assert.Contains(hit.StoneHits, h => h.Type == PieceType.Egg && h.RemainingHp == 1);
    }

    [Fact]
    public void TimeBomb_InAMatch_IsDefused()
    {
        var board = GameBoard.FromDebugString(@"
            B. G. Y. P. O.
            G. Y. P. O. B.
            Y. P. R. G. G.
            Rt R. B. R. Y.
            P. O. G. B. P.");
        ResolutionStep step = Engine(board).ResolveSwap(board, new Pos(2, 1), new Pos(2, 2)).Value.Steps[0];
        Assert.Equal(1, step.BombsDefused);
    }

    [Fact]
    public void TimeBombs_CountDown_AndExplodeAtZero()
    {
        var manager = new BoardManager(5, Balance);
        manager.GenerateNewBoard(30f);
        Pos bomb = manager.SpawnTimeBomb(new DeterministicRandom(3), 2)!.Value;
        Assert.True(manager.Board[bomb].IsTimeBomb);
        Assert.Empty(manager.TickTimeBombs());
        Assert.Equal(1, manager.Board[bomb].Hp);
        Assert.Equal(new[] { bomb }, manager.TickTimeBombs());
        manager.RewindTimeBombs(GameSession.ContinueBombMoves);
        Assert.Equal(GameSession.ContinueBombMoves, manager.Board[bomb].Hp);
    }

    [Fact]
    public void Blight_SpreadsToANeighbourGem()
    {
        var manager = new BoardManager(5, Balance);
        manager.LoadBoard(GameBoard.FromDebugString(@"
            B. G. Y. P. O. B. G. Y.
            G. Y. P. O. B. G. Y. P.
            Y. P. O. G. B. Y. P. O.
            R. %1 B. R. Y. R. B. R.
            P. O. G. B. P. O. G. B.
            B. G. Y. P. O. B. G. Y.
            G. Y. P. O. B. G. Y. P.
            Y. P. O. G. B. Y. P. O."));
        Pos? spread = manager.SpreadBlight(new DeterministicRandom(9));
        Assert.True(spread.HasValue);
        Assert.Equal(2, manager.Count(PieceType.Blight));
    }

    [Theory]
    [InlineData(101)]
    [InlineData(203)]
    [InlineData(301)]
    [InlineData(456)]
    public void MechanicStages_PlayAndResimulate(int stageId)
    {
        StageData stage = new StageCatalog(Balance).Get(stageId);
        var config = SessionConfig.ForStage(stage, Balance, null, League.Master);
        var session = new GameSession(config, Balance, "player-1");
        if (stage.TimeBombCount > 0)
        {
            Assert.Equal(stage.TimeBombCount, session.BoardManager is BoardManager bm ? bm.Count(PieceType.TimeBomb) : 0);
        }
        for (int i = 0; i < 40 && session.IsRunning; i++)
        {
            Fixtures.PlayHint(session, 300);
        }
        if (session.IsRunning)
        {
            session.Abandon(session.NextActionAllowedAtMs + 100);
        }
        ReplayVerification verification = ReplaySimulator.Verify(session.Replay, config, Balance);
        Assert.True(verification.Valid, verification.Reason);
        Assert.Equal(session.Score, verification.AuthoritativeScore);
    }
}
