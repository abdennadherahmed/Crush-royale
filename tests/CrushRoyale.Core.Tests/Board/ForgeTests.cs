using System.Collections.Generic;
using System.Linq;
using CrushRoyale.Core.Board;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Story;
using Xunit;

namespace CrushRoyale.Core.Tests.Board;

/// <summary>
/// The corruption forge, the mechanic of stage 401.
///
/// Every hazard before it waits for the player: a stone sits, ice sits, blight only grows when it was ignored. A
/// forge works on its own every move, which is the whole reason it exists.
/// </summary>
public sealed class ForgeTests
{
    private static BoardManager BoardWithForges(int forges)
    {
        var manager = new BoardManager(99, GameBalance.CreateDefault());
        manager.GenerateNewBoard(new BoardGenerationOptions
        {
            Width = 8,
            Height = 8,
            ColorCount = 6,
            DifficultyPermille = 500,
            ForgeCount = forges
        });
        return manager;
    }

    [Fact]
    public void AForge_CorruptsOneNeighbour_EveryMove()
    {
        BoardManager manager = BoardWithForges(1);
        Assert.Equal(1, manager.Count(PieceType.Forge));
        Assert.Equal(0, manager.Count(PieceType.Blight));

        var rng = new DeterministicRandom(7);
        List<Pos> first = manager.FeedForges(rng);

        Assert.Single(first);
        Assert.Equal(1, manager.Count(PieceType.Blight));
        // And it keeps working: a forge left standing costs a gem every single move.
        manager.FeedForges(rng);
        Assert.True(manager.Count(PieceType.Blight) >= 2);
    }

    [Fact]
    public void EachForge_Works_SoTwoCorruptTwiceAsFast()
    {
        BoardManager one = BoardWithForges(1);
        BoardManager two = BoardWithForges(2);
        Assert.Equal(2, two.Count(PieceType.Forge));

        one.FeedForges(new DeterministicRandom(3));
        two.FeedForges(new DeterministicRandom(3));

        Assert.Equal(1, one.Count(PieceType.Blight));
        Assert.Equal(2, two.Count(PieceType.Blight));
    }

    [Fact]
    public void AForge_IsABlock_ThatTakesThreeHits()
    {
        BoardManager manager = BoardWithForges(1);
        Pos forge = manager.Board.AllPositions().First(p => manager.Board[p].IsForge);
        Assert.True(manager.Board[forge].IsBlock);
        Assert.False(manager.Board[forge].IsMatchable);
        Assert.Equal(3, manager.Board[forge].Hp);
    }

    [Fact]
    public void ForgesAppear_FromStage401_WithAGoalToClearWhatTheyMake()
    {
        var catalog = new StageCatalog(GameBalance.CreateDefault());
        Assert.Equal(401, StageCatalog.ForgeIntroStage);

        StageData intro = catalog.Get(StageCatalog.ForgeIntroStage);
        Assert.True(intro.ForgeCount > 0);
        Assert.Contains(intro.Objectives, o => o.Type == ObjectiveType.DestroyBlight);

        // Nothing before 401 has one, so the mechanic really is an arrival rather than a drip.
        for (int id = 1; id < StageCatalog.ForgeIntroStage; id++)
        {
            Assert.Equal(0, catalog.Get(id).ForgeCount);
        }
    }

    [Fact]
    public void NoBoard_EverExceedsTheBlockBudget()
    {
        var catalog = new StageCatalog(GameBalance.CreateDefault());
        for (int id = 1; id <= 1000; id++)
        {
            StageData s = catalog.Get(id);
            int blocks = s.StoneCount + s.BlightCount + s.EggCount + s.ForgeCount;
            Assert.True(blocks <= 16, "Stage " + id + " blocks " + blocks + " cells of 64.");
        }
    }
}
