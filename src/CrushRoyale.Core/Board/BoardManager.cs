using System;
using System.Collections.Generic;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;

namespace CrushRoyale.Core.Board
{
    /// <summary>Board operations used by gameplay. Interface so UI / session code can be tested with a mock.</summary>
    public interface IBoardManager
    {
        GameBoard Board { get; }

        ulong Seed { get; }

        /// <summary>Generates a fresh board. <paramref name="difficulty"/> is a percentage 0-100 (GDD).</summary>
        GameBoard GenerateNewBoard(float difficulty);

        GameBoard GenerateNewBoard(BoardGenerationOptions options);

        List<Move> GetValidMoves();

        List<MatchGroup> FindMatches();

        ErrorCode CheckSwap(Pos a, Pos b);

        OperationResult<ResolutionResult> TrySwap(Pos a, Pos b, ResolveOptions options = null);

        ResolutionResult ClearCells(IReadOnlyList<Pos> cells, ClearCause cause, ResolveOptions options = null);

        /// <summary>Hint for idle players: the valid move that clears the most gems right now.</summary>
        Move? GetHint();
    }

    /// <summary>
    /// Task 1 facade. Owns one board and the three independent random streams derived from the match seed:
    /// generation, per-column refills, and deadlock shuffles.
    /// </summary>
    public sealed class BoardManager : IBoardManager
    {
        private const ulong GenerationSalt = 0x47454E;
        private const ulong RefillSalt = 0x524546;
        private const ulong ShuffleSalt = 0x534855;
        private const ulong BossSalt = 0x424F53;

        private readonly GameBalance _balance;
        private readonly int _colorCount;
        private readonly DeterministicRandom _generationRng;
        private readonly DeterministicRandom _shuffleRng;
        private readonly RefillSpawner _spawner;
        private readonly ResolutionEngine _engine;
        private GameBoard _board;

        /// <param name="seed">Match seed; every random stream is derived from it.</param>
        /// <param name="balance">Game balance.</param>
        /// <param name="colorCount">Colors in play (early stages use 5); 0 = balance default.</param>
        public BoardManager(ulong seed, GameBalance balance, int colorCount = 0)
        {
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
            Seed = seed;
            _colorCount = colorCount > 0 ? colorCount : balance.Board.ColorCount;
            _generationRng = DeterministicRandom.Derive(seed, GenerationSalt);
            _shuffleRng = DeterministicRandom.Derive(seed, ShuffleSalt);
            _spawner = new RefillSpawner(StableHash.Mix(seed, RefillSalt), balance.Board.Width, _colorCount);
            _engine = new ResolutionEngine(balance, _spawner, _shuffleRng);
        }

        public ulong Seed { get; }

        public GameBoard Board
        {
            get
            {
                if (_board == null)
                {
                    throw new InvalidOperationException("No board yet: call GenerateNewBoard first.");
                }
                return _board;
            }
        }

        public bool HasBoard => _board != null;

        public GameBoard GenerateNewBoard(float difficulty)
        {
            if (float.IsNaN(difficulty) || difficulty < 0f || difficulty > 100f)
            {
                throw new ArgumentOutOfRangeException(nameof(difficulty), "Difficulty is a percentage between 0 and 100.");
            }

            return GenerateNewBoard(new BoardGenerationOptions
            {
                Width = _balance.Board.Width,
                Height = _balance.Board.Height,
                ColorCount = _colorCount,
                DifficultyPermille = (int)Math.Round(difficulty * 10f),
                MaxAttempts = _balance.Board.MaxGenerationAttempts,
                LowDifficultyBiasPermille = _balance.Board.LowDifficultyBiasPermille
            });
        }

        public GameBoard GenerateNewBoard(BoardGenerationOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            if (options.Width != _balance.Board.Width)
            {
                throw new ArgumentException("Board width must match the balance (refill streams are per column).", nameof(options));
            }
            _board = BoardGenerator.Generate(options, _generationRng);
            return _board;
        }

        /// <summary>Uses an externally built board (tests, tutorials with scripted layouts).</summary>
        public void LoadBoard(GameBoard board)
        {
            if (board == null)
            {
                throw new ArgumentNullException(nameof(board));
            }
            if (board.Width != _balance.Board.Width)
            {
                throw new ArgumentException("Board width must match the balance.", nameof(board));
            }
            _board = board;
        }

        public List<Move> GetValidMoves() => MoveFinder.FindValidMoves(Board);

        public List<MatchGroup> FindMatches() => MatchFinder.FindMatches(Board);

        public ErrorCode CheckSwap(Pos a, Pos b) => MoveFinder.CheckSwap(Board, a, b);

        public OperationResult<ResolutionResult> TrySwap(Pos a, Pos b, ResolveOptions options = null) => _engine.ResolveSwap(Board, a, b, options);

        public ResolutionResult ClearCells(IReadOnlyList<Pos> cells, ClearCause cause, ResolveOptions options = null) =>
            _engine.ResolveForcedClear(Board, cells, cause, options);

        /// <summary>
        /// Boss phase transition: turns <paramref name="stoneCount"/> plain gems into stones at positions derived from
        /// (seed, phase), then guarantees the board is still playable. Returns the converted positions.
        /// </summary>
        public List<Pos> ApplyBossPhase(int phase, int stoneCount)
        {
            var converted = new List<Pos>();
            if (stoneCount <= 0)
            {
                return converted;
            }

            var candidates = new List<Pos>();
            foreach (Pos p in Board.AllPositions())
            {
                Piece piece = Board[p];
                if (piece.CanSwap && !piece.IsSpecial)
                {
                    candidates.Add(p);
                }
            }

            var rng = DeterministicRandom.Derive(Seed, BossSalt, (ulong)phase);
            int count = Math.Min(stoneCount, candidates.Count / 4);
            for (int i = 0; i < count; i++)
            {
                int j = i + rng.NextInt(candidates.Count - i);
                Pos tmp = candidates[i];
                candidates[i] = candidates[j];
                candidates[j] = tmp;

                Pos target = candidates[i];
                Board[target] = Board.CreatePiece(PieceColor.None, PieceType.Stone, 1);
                converted.Add(target);
            }

            BoardShuffler.EnsurePlayable(Board, _shuffleRng, _colorCount);
            return converted;
        }

        public Move? GetHint()
        {
            List<Move> moves = GetValidMoves();
            if (moves.Count == 0)
            {
                return null;
            }

            Move best = moves[0];
            int bestScore = -1;
            foreach (Move move in moves)
            {
                GameBoard board = Board;
                if (MoveFinder.IsSpecialCombo(board, move.From, move.To))
                {
                    // Fusing two bonuses beats any plain match.
                    if (100 > bestScore)
                    {
                        bestScore = 100;
                        best = move;
                    }
                    continue;
                }
                board.Swap(move.From, move.To);
                int score = 0;
                foreach (MatchGroup g in MatchFinder.FindMatches(board))
                {
                    score += g.Cells.Count + (int)g.Shape * 3;
                }
                board.Swap(move.From, move.To);

                if (score > bestScore)
                {
                    bestScore = score;
                    best = move;
                }
            }
            return best;
        }
    }
}
