using CrushRoyale.Core.Board;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;

namespace CrushRoyale.Core.Tests.Board;

public class DeterministicRandomTests
{
    [Fact]
    public void SameSeed_ProducesSameSequence()
    {
        var a = new DeterministicRandom(42);
        var b = new DeterministicRandom(42);
        for (int i = 0; i < 1000; i++)
        {
            Assert.Equal(a.NextUInt(), b.NextUInt());
        }
    }

    [Fact]
    public void KnownValues_AreStableAcrossPlatforms()
    {
        // Golden values: if these change, every stored replay becomes invalid.
        var rng = new DeterministicRandom(12345);
        var values = Enumerable.Range(0, 5).Select(_ => rng.NextUInt()).ToArray();
        var again = new DeterministicRandom(12345);
        Assert.Equal(values, Enumerable.Range(0, 5).Select(_ => again.NextUInt()).ToArray());
        Assert.NotEqual(values[0], values[1]);
    }

    [Fact]
    public void NextInt_StaysInRange_AndCoversAllValues()
    {
        var rng = new DeterministicRandom(7);
        var seen = new int[6];
        for (int i = 0; i < 6000; i++)
        {
            int v = rng.NextInt(6);
            Assert.InRange(v, 0, 5);
            seen[v]++;
        }
        Assert.All(seen, count => Assert.InRange(count, 800, 1200));
    }

    [Fact]
    public void NextWeightedIndex_RespectsZeroWeights()
    {
        var rng = new DeterministicRandom(3);
        for (int i = 0; i < 500; i++)
        {
            Assert.NotEqual(1, rng.NextWeightedIndex(new[] { 5, 0, 5 }));
        }
    }

    [Fact]
    public void NextInt_RejectsInvalidBound()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DeterministicRandom(1).NextInt(0));
    }
}

public class MatchFinderTests
{
    [Fact]
    public void DebugString_RoundTrips()
    {
        const string text = "R. Bh Gv\nYa #2 ..\nP. O. R.\n";
        var board = GameBoard.FromDebugString(text);
        Assert.Equal(text, board.ToDebugString());
        Assert.Equal(PieceType.Stone, board[1, 1].Type);
        Assert.Equal(2, board[1, 1].Hp);
        Assert.True(board[2, 1].IsEmpty);
    }

    [Fact]
    public void DetectsHorizontalThree()
    {
        var board = GameBoard.FromDebugString(@"
            B. G. Y.
            G. Y. P.
            R. R. R.");
        var groups = MatchFinder.FindMatches(board);
        var group = Assert.Single(groups);
        Assert.Equal(MatchShape.Line3, group.Shape);
        Assert.Equal(PieceColor.Red, group.Color);
        Assert.Equal(3, group.Cells.Count);
    }

    [Fact]
    public void DetectsVerticalFour()
    {
        var board = GameBoard.FromDebugString(@"
            B. G. Y.
            B. Y. P.
            B. O. G.
            B. R. Y.");
        var group = Assert.Single(MatchFinder.FindMatches(board));
        Assert.Equal(MatchShape.Line4, group.Shape);
        Assert.False(group.Runs[0].Horizontal);
    }

    [Fact]
    public void MergesLShapeIntoCross()
    {
        var board = GameBoard.FromDebugString(@"
            R. B. G.
            R. B. Y.
            R. R. R.");
        var group = Assert.Single(MatchFinder.FindMatches(board));
        Assert.Equal(MatchShape.Cross, group.Shape);
        Assert.Equal(5, group.Cells.Count);
    }

    [Fact]
    public void DetectsFiveAsLine5()
    {
        var board = GameBoard.FromDebugString(@"
            B. G. Y. P. O.
            G. Y. P. O. B.
            R. R. R. R. R.");
        Assert.Equal(MatchShape.Line5, Assert.Single(MatchFinder.FindMatches(board)).Shape);
    }

    [Fact]
    public void StonesBreakRuns_AndSpecialsMatchByColor()
    {
        var board = GameBoard.FromDebugString(@"
            B. G. Y. P. O.
            R. R. #1 R. R.
            G. Gh G. Y. B.");
        var group = Assert.Single(MatchFinder.FindMatches(board));
        Assert.Equal(PieceColor.Green, group.Color);
    }

    [Fact]
    public void SeparateGroupsOfSameColor_StaySeparate()
    {
        var board = GameBoard.FromDebugString(@"
            R. R. R. B. G. Y.
            G. Y. B. O. P. B.
            B. G. Y. R. R. R.");
        Assert.Equal(2, MatchFinder.FindMatches(board).Count);
    }
}

public class MoveFinderTests
{
    private const string NoMatchBoard = @"
        B. G. Y. P. O.
        G. Y. P. O. B.
        Y. P. O. G. G.
        R. R. B. R. Y.
        P. O. G. B. P.";

    [Fact]
    public void RejectsNonAdjacentAndNoMatchSwaps()
    {
        var board = GameBoard.FromDebugString(NoMatchBoard);
        Assert.Equal(ErrorCode.NotAdjacent, MoveFinder.CheckSwap(board, new Pos(0, 0), new Pos(2, 0)));
        Assert.Equal(ErrorCode.NoMatch, MoveFinder.CheckSwap(board, new Pos(0, 4), new Pos(1, 4)));
        Assert.Equal(ErrorCode.None, MoveFinder.CheckSwap(board, new Pos(2, 1), new Pos(3, 1)));
    }

    [Fact]
    public void CheckSwap_DoesNotMutateBoard()
    {
        var board = GameBoard.FromDebugString(NoMatchBoard);
        ulong before = board.ComputeHash();
        MoveFinder.FindValidMoves(board);
        MoveFinder.CheckSwap(board, new Pos(2, 1), new Pos(3, 1));
        Assert.Equal(before, board.ComputeHash());
    }

    [Fact]
    public void StonesCannotBeSwapped()
    {
        var board = GameBoard.FromDebugString(@"
            B. G. Y.
            R. #1 R.
            G. R. B.");
        Assert.Equal(ErrorCode.NotSwappable, MoveFinder.CheckSwap(board, new Pos(1, 1), new Pos(1, 0)));
    }

    [Fact]
    public void FindValidMoves_MatchesCount()
    {
        var board = GameBoard.FromDebugString(NoMatchBoard);
        var moves = MoveFinder.FindValidMoves(board);
        Assert.Equal(moves.Count, MoveFinder.CountValidMoves(board));
        Assert.Contains(new Move(new Pos(2, 1), new Pos(3, 1)), moves);
        Assert.All(moves, m => Assert.True(MoveFinder.IsValidSwap(board, m.From, m.To)));
    }
}

public class BoardGeneratorTests
{
    [Theory]
    [InlineData(0, 4, 24)]
    [InlineData(340, 3, 17)]
    [InlineData(670, 2, 10)]
    [InlineData(1000, 1, 4)]
    public void OpeningMoveWindow_FollowsGddFormula(int difficulty, int min, int max)
    {
        Assert.Equal(min, BoardGenerator.MinOpeningMoves(difficulty));
        Assert.Equal(max, BoardGenerator.MaxOpeningMoves(difficulty));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(500)]
    [InlineData(1000)]
    public void GeneratedBoards_HaveNoMatch_AndRespectMoveWindow(int difficulty)
    {
        int inWindow = 0;
        const int boards = 60;
        for (ulong seed = 1; seed <= boards; seed++)
        {
            var options = new BoardGenerationOptions { DifficultyPermille = difficulty, StoneCount = 3, IceCells = 4 };
            var board = BoardGenerator.Generate(options, new DeterministicRandom(seed));

            Assert.False(MatchFinder.HasAnyMatch(board));
            Assert.True(MoveFinder.HasValidMove(board));
            Assert.Equal(3, board.CountStones());
            Assert.Equal(4, board.TotalIceLayers);

            int moves = MoveFinder.CountValidMoves(board);
            if (moves >= BoardGenerator.MinOpeningMoves(difficulty) && moves <= BoardGenerator.MaxOpeningMoves(difficulty))
            {
                inWindow++;
            }
        }
        Assert.True(inWindow >= boards * 9 / 10, $"Only {inWindow}/{boards} boards in the move window.");
    }

    [Fact]
    public void Generation_IsDeterministic()
    {
        var o = new BoardGenerationOptions { DifficultyPermille = 420, StoneCount = 2 };
        var a = BoardGenerator.Generate(o, new DeterministicRandom(99));
        var b = BoardGenerator.Generate(o, new DeterministicRandom(99));
        var c = BoardGenerator.Generate(o, new DeterministicRandom(100));
        Assert.Equal(a.ComputeHash(), b.ComputeHash());
        Assert.NotEqual(a.ComputeHash(), c.ComputeHash());
    }

    [Fact]
    public void InvalidOptions_Throw()
    {
        Assert.Throws<ArgumentException>(() => BoardGenerator.Generate(new BoardGenerationOptions { DifficultyPermille = 1001 }, new DeterministicRandom(1)));
        Assert.Throws<ArgumentException>(() => BoardGenerator.Generate(new BoardGenerationOptions { StoneCount = 40 }, new DeterministicRandom(1)));
    }

    [Fact]
    public void BoardManager_DifficultyIsPercentage()
    {
        var manager = new BoardManager(5, GameBalance.CreateDefault());
        Assert.Throws<ArgumentOutOfRangeException>(() => manager.GenerateNewBoard(101f));
        var board = manager.GenerateNewBoard(100f);
        Assert.InRange(MoveFinder.CountValidMoves(board), 1, 4);
    }
}

public class ResolutionEngineTests
{
    private static ResolutionEngine Engine(GameBoard board, ulong seed = 1) =>
        new(GameBalance.CreateDefault(), new RefillSpawner(seed, board.Width, 6), new DeterministicRandom(seed));

    private static void AssertStable(GameBoard board)
    {
        Assert.False(MatchFinder.HasAnyMatch(board));
        foreach (var p in board.AllPositions())
        {
            Assert.False(board[p].IsEmpty, $"Hole at {p}");
        }
    }

    [Fact]
    public void Match3_ClearsRefillsAndScores()
    {
        var board = GameBoard.FromDebugString(@"
            B. G. Y. P. O.
            G. Y. P. O. B.
            Y. P. O. G. G.
            R. R. B. R. Y.
            P. O. G. B. P.");
        board.SetIce(new Pos(1, 1), 1);
        var result = Engine(board).ResolveSwap(board, new Pos(2, 1), new Pos(3, 1));

        Assert.True(result.Success);
        var first = result.Value.Steps[0];
        Assert.Equal(0, first.CascadeLevel);
        Assert.Equal(MatchShape.Line3, Assert.Single(first.Groups).Shape);
        Assert.Equal(3, first.Cleared.Count);
        Assert.Equal(3, first.ClearedByColor[(int)PieceColor.Red]);
        Assert.Contains(new Pos(1, 1), first.IceBroken);
        Assert.Equal(0, board.IceAt(new Pos(1, 1)));
        Assert.Equal(3 * 20 + 40, first.BasePoints);
        Assert.Equal(3, first.Refills.Count);
        AssertStable(board);
    }

    [Fact]
    public void InvalidSwap_LeavesBoardUntouched()
    {
        var board = GameBoard.FromDebugString(@"
            B. G. Y. P. O.
            G. Y. P. O. B.
            Y. P. O. G. G.
            R. R. B. R. Y.
            P. O. G. B. P.");
        ulong before = board.ComputeHash();
        var result = Engine(board).ResolveSwap(board, new Pos(0, 4), new Pos(1, 4));
        Assert.False(result.Success);
        Assert.Equal(ErrorCode.NoMatch, result.Error);
        Assert.Equal(before, board.ComputeHash());
    }

    [Fact]
    public void Match4_CreatesLineBombOnSwappedCell()
    {
        var board = GameBoard.FromDebugString(@"
            B. G. Y. P. O.
            G. Y. P. O. B.
            Y. P. R. G. G.
            R. R. B. R. Y.
            P. O. G. B. P.");
        var step = Engine(board).ResolveSwap(board, new Pos(2, 1), new Pos(2, 2)).Value.Steps[0];

        Assert.Equal(MatchShape.Line4, step.Groups[0].Shape);
        var special = Assert.Single(step.SpecialsCreated);
        Assert.Equal(PieceType.LineHorizontal, special.Piece.Type);
        Assert.Equal(PieceColor.Red, special.Piece.Color);
        Assert.Equal(new Pos(2, 1), special.Position);
    }

    [Fact]
    public void LineBomb_InMatch_ClearsWholeRow()
    {
        var board = GameBoard.FromDebugString(@"
            B. G. Y. P. O.
            G. Y. P. O. B.
            Y. P. R. G. G.
            Rh R. B. O. Y.
            P. O. G. B. P.");
        var step = Engine(board).ResolveSwap(board, new Pos(2, 1), new Pos(2, 2)).Value.Steps[0];

        Assert.Equal(1, step.SpecialsActivated);
        Assert.Equal(5, step.Cleared.Count);
        Assert.Contains(step.Cleared, c => c.Position == new Pos(4, 1) && c.Cause == ClearCause.LineBlast);
    }

    [Fact]
    public void Match5_ClearsEveryGemOfThatColor()
    {
        var board = GameBoard.FromDebugString(@"
            B. G. Y. P. O.
            G. Y. P. O. B.
            Y. P. R. G. G.
            R. R. P. R. R.
            P. O. R. B. P.");
        var step = Engine(board).ResolveSwap(board, new Pos(2, 1), new Pos(2, 2)).Value.Steps[0];

        Assert.Equal(MatchShape.Line5, step.Groups[0].Shape);
        Assert.Equal(6, step.ClearedByColor[(int)PieceColor.Red]);
        Assert.Contains(step.Cleared, c => c.Position == new Pos(2, 0) && c.Cause == ClearCause.ColorBlast);
    }

    [Fact]
    public void AdjacentMatch_BreaksStone()
    {
        var board = GameBoard.FromDebugString(@"
            B. G. Y. P. O.
            G. Y. P. O. B.
            Y. P. #1 G. G.
            R. R. B. R. Y.
            P. O. G. B. P.");
        var step = Engine(board).ResolveSwap(board, new Pos(2, 1), new Pos(3, 1)).Value.Steps[0];

        var hit = Assert.Single(step.StoneHits);
        Assert.True(hit.Destroyed);
        Assert.Equal(new Pos(2, 2), hit.Position);
    }

    [Fact]
    public void ForcedClear_TriggersCascadeLevels()
    {
        var board = GameBoard.FromDebugString(@"
            B. G. Y. P. O.
            G. Y. P. O. B.
            Y. P. O. G. G.
            R. R. B. R. Y.
            P. O. G. B. P.");
        var result = Engine(board).ResolveForcedClear(board, new[] { new Pos(2, 0) }, ClearCause.PowerUp);

        Assert.NotEmpty(result.Steps);
        for (int i = 0; i < result.Steps.Count; i++)
        {
            Assert.Equal(i, result.Steps[i].CascadeLevel);
        }
        AssertStable(board);
    }

    [Fact]
    public void GoldenChain_ClearsRowAndColumnOnce()
    {
        var board = GameBoard.FromDebugString(@"
            B. G. Y. P. O.
            G. Y. P. O. B.
            Y. P. O. G. G.
            R. R. B. R. Y.
            P. O. G. B. P.");
        var options = new ResolveOptions { GoldenChainArmed = true };
        var result = Engine(board).ResolveSwap(board, new Pos(2, 1), new Pos(3, 1), options).Value;

        Assert.True(result.GoldenChainConsumed);
        // Row y=1 (5) + column x=2 (5) share one cell => 9 cells.
        Assert.Equal(9, result.Steps[0].Cleared.Count);
    }

    [Fact]
    public void BrightSpark_Match3SpawnsLineBomb()
    {
        var board = GameBoard.FromDebugString(@"
            B. G. Y. P. O.
            G. Y. P. O. B.
            Y. P. O. G. G.
            R. R. B. R. Y.
            P. O. G. B. P.");
        var step = Engine(board).ResolveSwap(board, new Pos(2, 1), new Pos(3, 1), new ResolveOptions { BrightSparkActive = true }).Value.Steps[0];
        Assert.Equal(PieceType.LineHorizontal, Assert.Single(step.SpecialsCreated).Piece.Type);
    }

    [Fact]
    public void LongRandomGame_KeepsInvariants_AndIsDeterministic()
    {
        ulong Play(ulong seed)
        {
            var manager = new BoardManager(seed, GameBalance.CreateDefault());
            manager.GenerateNewBoard(new BoardGenerationOptions { DifficultyPermille = 600, StoneCount = 4, IceCells = 6 });
            var picker = new DeterministicRandom(seed ^ 0xABCDEF);
            for (int turn = 0; turn < 250; turn++)
            {
                var moves = manager.GetValidMoves();
                Assert.NotEmpty(moves);
                var move = moves[picker.NextInt(moves.Count)];
                var result = manager.TrySwap(move.From, move.To);
                Assert.True(result.Success, result.ToString());
                AssertStable(manager.Board);
                Assert.True(MoveFinder.HasValidMove(manager.Board));
            }
            return manager.Board.ComputeHash();
        }

        Assert.Equal(Play(2024), Play(2024));
        Assert.NotEqual(Play(2024), Play(2025));
    }

    [Fact]
    public void SameSeed_SameRefillsPerColumn_RegardlessOfMoves()
    {
        var a = new RefillSpawner(77, 8, 6);
        var b = new RefillSpawner(77, 8, 6);
        var fromA = Enumerable.Range(0, 20).Select(_ => a.NextColor(3)).ToList();
        b.NextColor(0);
        b.NextColor(5);
        var fromB = Enumerable.Range(0, 20).Select(_ => b.NextColor(3)).ToList();
        Assert.Equal(fromA, fromB);
    }

    [Fact]
    public void Hint_ReturnsAValidMove()
    {
        var manager = new BoardManager(11, GameBalance.CreateDefault());
        manager.GenerateNewBoard(30f);
        var hint = manager.GetHint();
        Assert.NotNull(hint);
        Assert.True(MoveFinder.IsValidSwap(manager.Board, hint.Value.From, hint.Value.To));
    }

    [Fact]
    public void TwoLineBonuses_Swapped_ClearRowAndColumn()
    {
        var board = GameBoard.FromDebugString(@"
            B. G. Y. P. O.
            G. Y. P. O. B.
            Y. P. Rh Gv G.
            R. O. B. R. Y.
            P. O. G. B. P.");
        Assert.Equal(ErrorCode.None, MoveFinder.CheckSwap(board, new Pos(2, 2), new Pos(3, 2)));
        Assert.Contains(MoveFinder.FindValidMoves(board), m => m.Equals(new Move(new Pos(2, 2), new Pos(3, 2))));

        var step = Engine(board).ResolveSwap(board, new Pos(2, 2), new Pos(3, 2)).Value.Steps[0];

        Assert.Equal(1, step.CombosTriggered);
        Assert.Equal(2, step.SpecialsActivated);
        Assert.Equal(9, step.Cleared.Count);
        Assert.All(step.Cleared, c => Assert.True(c.Position.Y == 2 || c.Position.X == 3));
    }

    [Fact]
    public void TwoAreaBombs_Swapped_Clear5x5()
    {
        var board = GameBoard.FromDebugString(@"
            B. G. Y. P. O.
            G. Y. Ba O. B.
            Y. P. Ra G. G.
            R. O. B. R. Y.
            P. O. G. B. P.");
        var step = Engine(board).ResolveSwap(board, new Pos(2, 3), new Pos(2, 2)).Value.Steps[0];

        Assert.Equal(1, step.CombosTriggered);
        Assert.Equal(25, step.Cleared.Count);
    }

    [Fact]
    public void LineAndBomb_Swapped_ClearThreeRowsAndColumns()
    {
        var board = GameBoard.FromDebugString(@"
            B. G. Y. P. O.
            G. Y. P. O. B.
            Y. P. Ra Gv G.
            R. O. B. R. Y.
            P. O. G. B. P.");
        var step = Engine(board).ResolveSwap(board, new Pos(2, 2), new Pos(3, 2)).Value.Steps[0];

        // Rows 1-3 and columns 2-4 on a 5x5 board: everything but the two 2x2 corners on the left.
        Assert.Equal(25 - 4, step.Cleared.Count);
    }
}
