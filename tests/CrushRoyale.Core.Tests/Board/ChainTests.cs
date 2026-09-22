using System.Linq;
using CrushRoyale.Core.Board;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Story;
using Xunit;

namespace CrushRoyale.Core.Tests.Board;

/// <summary>
/// Chains, the mechanic of stage 501.
///
/// Every obstacle before it stopped a gem being destroyed. A chain stops one being moved, and nothing done to that
/// gem frees it: the answer is always a match somewhere beside it.
/// </summary>
public sealed class ChainTests
{
    private static BoardManager BoardWithChains(int cells, int links = 1)
    {
        var manager = new BoardManager(2024, GameBalance.CreateDefault());
        manager.GenerateNewBoard(new BoardGenerationOptions
        {
            Width = 8,
            Height = 8,
            ColorCount = 6,
            DifficultyPermille = 400,
            ChainCells = cells,
            ChainLinks = links
        });
        return manager;
    }

    [Fact]
    public void AChainedGem_CannotBeSwapped()
    {
        BoardManager manager = BoardWithChains(8);
        GameBoard board = manager.Board;
        Pos chained = board.AllPositions().First(p => board.ChainAt(p) > 0);

        foreach (Pos n in new[] { chained.Offset(1, 0), chained.Offset(-1, 0), chained.Offset(0, 1), chained.Offset(0, -1) })
        {
            if (board.InBounds(n))
            {
                Assert.Equal(ErrorCode.NotSwappable, MoveFinder.CheckSwap(board, chained, n));
                Assert.Equal(ErrorCode.NotSwappable, MoveFinder.CheckSwap(board, n, chained));
            }
        }
    }

    [Fact]
    public void EveryOfferedMove_IsActuallyPlayable()
    {
        // The finder used to check only the first cell of a pair, so it offered swaps the validator refused: every
        // bot and every hint proposed an illegal move and a chained board froze on its first turn.
        BoardManager manager = BoardWithChains(10, links: 2);
        GameBoard board = manager.Board;
        var moves = MoveFinder.FindValidMoves(board);
        Assert.NotEmpty(moves);
        foreach (Move m in moves)
        {
            Assert.Equal(ErrorCode.None, MoveFinder.CheckSwap(board, m.From, m.To));
        }
    }

    [Fact]
    public void AChainedBoard_AlwaysHasSomethingToPlay()
    {
        for (int seed = 1; seed <= 25; seed++)
        {
            var manager = new BoardManager((ulong)seed, GameBalance.CreateDefault());
            manager.GenerateNewBoard(new BoardGenerationOptions
            {
                Width = 8, Height = 8, ColorCount = 6, DifficultyPermille = 900, ChainCells = 12, ChainLinks = 2
            });
            Assert.True(MoveFinder.HasValidMove(manager.Board), "Seed " + seed + " produced a board with no legal swap.");
        }
    }

    [Fact]
    public void ChainsAppear_FromStage501_AndNeverBefore()
    {
        var catalog = new StageCatalog(GameBalance.CreateDefault());
        Assert.Equal(501, StageCatalog.ChainIntroStage);
        Assert.True(catalog.Get(StageCatalog.ChainIntroStage).ChainCells > 0);
        for (int id = 1; id < StageCatalog.ChainIntroStage; id++)
        {
            Assert.Equal(0, catalog.Get(id).ChainCells);
        }
    }

    [Fact]
    public void ChainsNeverPinMoreThanAQuarterOfTheBoard()
    {
        var catalog = new StageCatalog(GameBalance.CreateDefault());
        for (int id = 1; id <= 1000; id++)
        {
            Assert.True(catalog.Get(id).ChainCells <= 16, "Stage " + id + " pins too many cells.");
        }
    }
}
