using System.Linq;
using CrushRoyale.Core.Board;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Story;
using Xunit;

namespace CrushRoyale.Core.Tests.Board;

/// <summary>
/// Mirror gems, the mechanic of stage 701.
///
/// The first one that gives instead of taking: one move lands in two places. Everything before it made a move worth
/// less, so the question was always what is still possible; here it becomes where the second half should fall.
/// </summary>
public sealed class MirrorTests
{
    private static GameBoard BoardWithMirrors(int cells, ulong seed = 31)
    {
        var manager = new BoardManager(seed, GameBalance.CreateDefault());
        manager.GenerateNewBoard(new BoardGenerationOptions
        {
            Width = 8,
            Height = 8,
            ColorCount = 6,
            DifficultyPermille = 500,
            MirrorCells = cells
        });
        return manager.Board;
    }

    [Fact]
    public void TheOppositeCell_IsTheBoardRotatedHalfATurn()
    {
        GameBoard board = BoardWithMirrors(0);
        Assert.Equal(new Pos(7, 7), board.Opposite(new Pos(0, 0)));
        Assert.Equal(new Pos(0, 0), board.Opposite(new Pos(7, 7)));
        Assert.Equal(new Pos(5, 4), board.Opposite(new Pos(2, 3)));
        // And it is its own inverse, so a reflection can never point somewhere unexpected.
        foreach (Pos p in board.AllPositions())
        {
            Assert.Equal(p, board.Opposite(board.Opposite(p)));
        }
    }

    [Fact]
    public void MirrorsArePlaced_AndNeverReflectOntoThemselves()
    {
        GameBoard board = BoardWithMirrors(4);
        Assert.True(board.MirrorCells > 0);
        foreach (Pos p in board.AllPositions().Where(board.IsMirror))
        {
            Pos opposite = board.Opposite(p);
            Assert.NotEqual(p, opposite);
            // A mirror pointing at a block would do nothing and look broken.
            Assert.False(board[opposite].IsBlock);
        }
    }

    [Fact]
    public void AMirrorIsPartOfTheBoardHash()
    {
        GameBoard a = BoardWithMirrors(4);
        GameBoard b = a.Clone();
        Assert.Equal(a.ComputeHash(), b.ComputeHash());

        Pos mirror = b.AllPositions().First(b.IsMirror);
        b.SetMirror(mirror, false);
        Assert.NotEqual(a.ComputeHash(), b.ComputeHash());
    }

    [Fact]
    public void MirrorsAppear_FromStage701_AndNeverBefore()
    {
        var catalog = new StageCatalog(GameBalance.CreateDefault());
        Assert.Equal(701, StageCatalog.MirrorIntroStage);
        Assert.True(catalog.Get(StageCatalog.MirrorIntroStage).MirrorCells > 0);
        for (int id = 1; id < StageCatalog.MirrorIntroStage; id++)
        {
            Assert.Equal(0, catalog.Get(id).MirrorCells);
        }
    }

    [Fact]
    public void NoStage_IsMostlyMirrors()
    {
        // A board made of gifts clears itself.
        var catalog = new StageCatalog(GameBalance.CreateDefault());
        for (int id = 1; id <= 1000; id++)
        {
            Assert.True(catalog.Get(id).MirrorCells <= 8, "Stage " + id + " has too many mirrors.");
        }
    }
}
