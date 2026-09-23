using System.Collections.Generic;
using System.Linq;
using CrushRoyale.Core.Board;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Story;
using Xunit;

namespace CrushRoyale.Core.Tests.Board;

/// <summary>
/// Wardens, the mechanic of stage 801 and the last one the campaign teaches.
///
/// Everything else on the board can be chipped away at leisure across twenty moves. A warden cannot: it heals
/// whatever was not finished, so the end of the campaign asks for a plan rather than a run of individually good
/// moves.
/// </summary>
public sealed class WardenTests
{
    private const int MaxHp = 4;

    private static BoardManager BoardWithWarden(ulong seed = 5)
    {
        var manager = new BoardManager(seed, GameBalance.CreateDefault());
        manager.GenerateNewBoard(new BoardGenerationOptions
        {
            Width = 8,
            Height = 8,
            ColorCount = 6,
            DifficultyPermille = 600,
            WardenCount = 1
        });
        return manager;
    }

    [Fact]
    public void AWardenStartsAtFourHitPoints_AndIsABlock()
    {
        BoardManager manager = BoardWithWarden();
        Pos warden = manager.Board.AllPositions().First(p => manager.Board[p].IsWarden);
        Assert.Equal(MaxHp, manager.Board[warden].Hp);
        Assert.True(manager.Board[warden].IsBlock);
        Assert.False(manager.Board[warden].IsMatchable);
    }

    [Fact]
    public void ItHealsOnAMoveThatDidNotHitIt()
    {
        BoardManager manager = BoardWithWarden();
        GameBoard board = manager.Board;
        Pos warden = board.AllPositions().First(p => board[p].IsWarden);

        Piece damaged = board[warden];
        board[warden] = new Piece(damaged.Id, damaged.Color, damaged.Type, 2);

        List<Pos> healed = manager.HealWardens(new HashSet<Pos>(), MaxHp);

        Assert.Contains(warden, healed);
        Assert.Equal(3, board[warden].Hp);
    }

    [Fact]
    public void ItDoesNotHealOnAMoveThatHitIt()
    {
        BoardManager manager = BoardWithWarden();
        GameBoard board = manager.Board;
        Pos warden = board.AllPositions().First(p => board[p].IsWarden);
        Piece damaged = board[warden];
        board[warden] = new Piece(damaged.Id, damaged.Color, damaged.Type, 2);

        List<Pos> healed = manager.HealWardens(new HashSet<Pos> { warden }, MaxHp);

        Assert.Empty(healed);
        Assert.Equal(2, board[warden].Hp);
    }

    [Fact]
    public void ItNeverHealsPastItsFullHealth()
    {
        BoardManager manager = BoardWithWarden();
        GameBoard board = manager.Board;
        Pos warden = board.AllPositions().First(p => board[p].IsWarden);
        Assert.Equal(MaxHp, board[warden].Hp);

        Assert.Empty(manager.HealWardens(new HashSet<Pos>(), MaxHp));
        Assert.Equal(MaxHp, board[warden].Hp);
    }

    [Fact]
    public void WardensAppear_FromStage801_AndNeverBefore()
    {
        var catalog = new StageCatalog(GameBalance.CreateDefault());
        Assert.Equal(801, StageCatalog.WardenIntroStage);
        Assert.True(catalog.Get(StageCatalog.WardenIntroStage).WardenCount > 0);
        for (int id = 1; id < StageCatalog.WardenIntroStage; id++)
        {
            Assert.Equal(0, catalog.Get(id).WardenCount);
        }
    }

    [Fact]
    public void NoStage_EverHasMoreThanOne()
    {
        // Two would mean splitting attention between two things that both undo themselves.
        var catalog = new StageCatalog(GameBalance.CreateDefault());
        for (int id = 1; id <= 1000; id++)
        {
            Assert.True(catalog.Get(id).WardenCount <= 1, "Stage " + id + " has more than one warden.");
        }
    }

    [Fact]
    public void TheBlockBudgetStillHolds_WithEveryMechanicInPlay()
    {
        var catalog = new StageCatalog(GameBalance.CreateDefault());
        for (int id = 1; id <= 1000; id++)
        {
            StageData s = catalog.Get(id);
            int blocks = s.StoneCount + s.BlightCount + s.EggCount + s.ForgeCount + s.WardenCount;
            Assert.True(blocks <= 16, "Stage " + id + " blocks " + blocks + " cells of 64.");
        }
    }
}
