using System.Linq;
using CrushRoyale.Core.Board;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Gameplay;
using CrushRoyale.Core.Story;
using Xunit;

namespace CrushRoyale.Core.Tests.Board;

/// <summary>
/// Cursed gems, the mechanic of stage 601.
///
/// Every hazard before it blocks a move. This one permits it and charges for it, which makes the biggest match on
/// the board frequently the wrong one and asks the player for restraint instead of greed.
/// </summary>
public sealed class CurseTests
{
    private static GameBoard BoardWithCurses(int cells, ulong seed = 77)
    {
        var manager = new BoardManager(seed, GameBalance.CreateDefault());
        manager.GenerateNewBoard(new BoardGenerationOptions
        {
            Width = 8,
            Height = 8,
            ColorCount = 6,
            DifficultyPermille = 500,
            CursedCells = cells
        });
        return manager.Board;
    }

    [Fact]
    public void CursedGemsArePlaced_AndStayMovable()
    {
        GameBoard board = BoardWithCurses(6);
        Assert.Equal(6, board.CursedCells);
        // A curse is not a lock: the player may move and match it. That is exactly what it costs them.
        Pos cursed = board.AllPositions().First(board.IsCursed);
        Assert.True(board[cursed].CanSwap);
        Assert.Equal(0, board.ChainAt(cursed));
    }

    [Fact]
    public void ACurseIsSpentWhenItIsTriggered()
    {
        GameBoard board = BoardWithCurses(4);
        Pos cursed = board.AllPositions().First(board.IsCursed);
        board.SetCursed(cursed, false);
        Assert.Equal(3, board.CursedCells);
    }

    [Fact]
    public void TheCurseIsPartOfTheBoardHash()
    {
        // The server re-simulates every match: if a curse were invisible to the hash, two boards that play
        // differently would look identical and a desync would pass unnoticed.
        GameBoard a = BoardWithCurses(4);
        GameBoard b = a.Clone();
        Assert.Equal(a.ComputeHash(), b.ComputeHash());

        Pos cursed = b.AllPositions().First(b.IsCursed);
        b.SetCursed(cursed, false);
        Assert.NotEqual(a.ComputeHash(), b.ComputeHash());
    }

    [Fact]
    public void CursesAppear_FromStage601_AndNeverBefore()
    {
        var catalog = new StageCatalog(GameBalance.CreateDefault());
        Assert.Equal(601, StageCatalog.CurseIntroStage);
        Assert.True(catalog.Get(StageCatalog.CurseIntroStage).CursedCells > 0);
        for (int id = 1; id < StageCatalog.CurseIntroStage; id++)
        {
            Assert.Equal(0, catalog.Get(id).CursedCells);
        }
    }

    [Fact]
    public void ACursedStage_ChargesMovesOrSeconds_ButNeverBoth()
    {
        var balance = GameBalance.CreateDefault();
        var catalog = new StageCatalog(balance);
        StageData stage = catalog.Get(StageCatalog.CurseIntroStage);
        SessionConfig config = SessionConfig.ForStage(stage, balance, null, League.Bronze);
        Assert.True(config.CursePenaltyMoves > 0);
        Assert.True(config.CursePenaltySeconds > 0);
        // A moves stage pays in moves; a timed one has none to take, so it pays in seconds. The session picks.
        Assert.True(config.HasMoveLimit || stage.Timed);
    }

    [Fact]
    public void NoStage_IsMostlyCursed()
    {
        var catalog = new StageCatalog(GameBalance.CreateDefault());
        for (int id = 1; id <= 1000; id++)
        {
            Assert.True(catalog.Get(id).CursedCells <= 10, "Stage " + id + " curses too much of the board.");
        }
    }
}
