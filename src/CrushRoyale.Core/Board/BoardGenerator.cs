using System;
using System.Collections.Generic;
using CrushRoyale.Core.Common;

namespace CrushRoyale.Core.Board
{
    /// <summary>Inputs of a board generation.</summary>
    public sealed class BoardGenerationOptions
    {
        public int Width { get; set; } = 8;

        public int Height { get; set; } = 8;

        public int ColorCount { get; set; } = 6;

        /// <summary>0 (easiest) to 1000 (hardest).</summary>
        public int DifficultyPermille { get; set; }

        public int StoneCount { get; set; }

        public int StoneHp { get; set; } = 1;

        public int IceCells { get; set; }

        public int IceLayers { get; set; } = 1;

        /// <summary>Corrupted crystals placed at the start (they spread during the stage).</summary>
        public int BlightCount { get; set; }

        /// <summary>Dragon eggs (2 hits, hatch into a bonus gem).</summary>
        public int EggCount { get; set; }

        /// <summary>Corruption forges (3 hits): each one corrupts a neighbouring gem after every move.</summary>
        public int ForgeCount { get; set; }

        /// <summary>Chained cells (stage 501+): held in place until a match is cleared next to them.</summary>
        public int ChainCells { get; set; }

        /// <summary>Links on each chained cell: two means two neighbouring clears are needed to free it.</summary>
        public int ChainLinks { get; set; } = 1;

        /// <summary>Cursed gems (stage 601+): matching one costs the player a move or seconds on the clock.</summary>
        public int CursedCells { get; set; }

        /// <summary>Mirror gems (stage 701+): clearing one also clears the cell opposite it.</summary>
        public int MirrorCells { get; set; }

        /// <summary>Wardens (stage 801+): 4-hit blocks that heal on any move that does not hit them.</summary>
        public int WardenCount { get; set; }

        public int MaxAttempts { get; set; } = 400;

        public int LowDifficultyBiasPermille { get; set; } = 350;

        public void Validate()
        {
            if (Width < 5 || Height < 5)
            {
                throw new ArgumentException("Board must be at least 5x5.");
            }
            if (ColorCount < 3 || ColorCount > 6)
            {
                throw new ArgumentException("ColorCount must be 3-6.");
            }
            if (DifficultyPermille < 0 || DifficultyPermille > 1000)
            {
                throw new ArgumentException("DifficultyPermille must be 0-1000.");
            }
            int cells = Width * Height;
            if (StoneCount < 0 || StoneCount > cells / 4)
            {
                throw new ArgumentException("StoneCount must be between 0 and a quarter of the board.");
            }
            if (StoneHp < 1 || StoneHp > 3)
            {
                throw new ArgumentException("StoneHp must be 1-3.");
            }
            if (WardenCount < 0 || ForgeCount < 0 || BlightCount < 0 || EggCount < 0
                || StoneCount + BlightCount + EggCount + ForgeCount + WardenCount > cells / 4)
            {
                throw new ArgumentException("Too many blocks (stones, blight, eggs).");
            }
            if (IceCells < 0 || IceCells + StoneCount > cells)
            {
                throw new ArgumentException("Too many ice cells.");
            }
            // A board where most gems are pinned has no moves at all, so chains are capped well under half of it.
            if (ChainCells < 0 || ChainCells > cells / 4)
            {
                throw new ArgumentException("Too many chained cells.");
            }
            if (ChainLinks < 1 || ChainLinks > 3)
            {
                throw new ArgumentException("ChainLinks must be 1-3.");
            }
            // A board where every other gem punishes you has no good move left, only least-bad ones.
            if (CursedCells < 0 || CursedCells > cells / 6)
            {
                throw new ArgumentException("Too many cursed cells.");
            }
            // Mirrors are a gift, and a board made of gifts clears itself.
            if (MirrorCells < 0 || MirrorCells > cells / 8)
            {
                throw new ArgumentException("Too many mirror cells.");
            }
            if (IceLayers < 1 || IceLayers > 3)
            {
                throw new ArgumentException("IceLayers must be 1-3.");
            }
            if (MaxAttempts < 1)
            {
                throw new ArgumentException("MaxAttempts must be >= 1.");
            }
        }
    }

    /// <summary>
    /// "Intelligent" board generation from the GDD:
    /// never starts with a match, always has a playable move, and the number of opening moves
    /// shrinks with difficulty (4+ at 0%, down to 1-4 at 100%).
    /// </summary>
    public static class BoardGenerator
    {
        /// <summary>GDD pseudo-code: moves >= 4 - difficulty*3, rounded up. 0% => 4, 34% => 3, 67% => 2, 100% => 1.</summary>
        public static int MinOpeningMoves(int difficultyPermille)
        {
            int d = Clamp(difficultyPermille);
            return Math.Max(1, (4000 - 3 * d + 999) / 1000);
        }

        /// <summary>"At 100% difficulty: minimal matches available" => cap the number of moves as difficulty rises (24 at 0%, 4 at 100%).</summary>
        public static int MaxOpeningMoves(int difficultyPermille)
        {
            int d = Clamp(difficultyPermille);
            return Math.Max(MinOpeningMoves(d), 4 + (1000 - d) * 20 / 1000);
        }

        /// <summary>Generates a board. Same options + same RNG state => identical board on every platform.</summary>
        public static GameBoard Generate(BoardGenerationOptions options, DeterministicRandom rng)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            if (rng == null)
            {
                throw new ArgumentNullException(nameof(rng));
            }
            options.Validate();

            int min = MinOpeningMoves(options.DifficultyPermille);
            int max = MaxOpeningMoves(options.DifficultyPermille);

            GameBoard best = null;
            int bestDistance = int.MaxValue;

            for (int attempt = 0; attempt < options.MaxAttempts; attempt++)
            {
                GameBoard candidate = FillOnce(options, rng);
                int moves = MoveFinder.CountValidMoves(candidate, max + 1);
                if (moves >= min && moves <= max)
                {
                    return candidate;
                }

                int distance = moves < min ? min - moves : moves - max;
                if (distance < bestDistance)
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }

            // Could not hit the window: keep the closest board, but never an unplayable one.
            if (!MoveFinder.HasValidMove(best))
            {
                BoardShuffler.EnsurePlayable(best, rng, options.ColorCount);
            }
            return best;
        }

        private static GameBoard FillOnce(BoardGenerationOptions o, DeterministicRandom rng)
        {
            var board = new GameBoard(o.Width, o.Height);

            var stoneCells = PickDistinctCells(o.Width, o.Height, o.StoneCount, rng, null);
            var stoneSet = new HashSet<Pos>(stoneCells);
            foreach (Pos p in stoneCells)
            {
                board[p] = board.CreatePiece(PieceColor.None, PieceType.Stone, (byte)o.StoneHp);
            }
            // Blight and eggs sit in the upper half: they fall onto the board as the player clears below.
            PlaceBlocks(board, o, rng, stoneSet, o.BlightCount, PieceType.Blight, 1);
            PlaceBlocks(board, o, rng, stoneSet, o.EggCount, PieceType.Egg, 2);
            // Three hits: a forge has to be a decision, not a nuisance you clear on the way past.
            PlaceBlocks(board, o, rng, stoneSet, o.ForgeCount, PieceType.Forge, 3);
            // Four, because the warden heals: fewer and a single lucky cascade would finish it by accident.
            PlaceBlocks(board, o, rng, stoneSet, o.WardenCount, PieceType.Warden, 4);

            int biasPermille = o.LowDifficultyBiasPermille * (1000 - Clamp(o.DifficultyPermille)) / 1000;
            var candidates = new List<PieceColor>(o.ColorCount);
            var friendly = new List<PieceColor>(4);

            for (int y = 0; y < o.Height; y++)
            {
                for (int x = 0; x < o.Width; x++)
                {
                    var pos = new Pos(x, y);
                    if (stoneSet.Contains(pos))
                    {
                        continue;
                    }

                    candidates.Clear();
                    for (int c = 0; c < o.ColorCount; c++)
                    {
                        var color = (PieceColor)c;
                        if (!WouldMatch(board, x, y, color))
                        {
                            candidates.Add(color);
                        }
                    }

                    PieceColor chosen;
                    if (rng.ChancePermille(biasPermille))
                    {
                        // Near-match setup: reuse a color seen diagonally below or two cells away.
                        friendly.Clear();
                        AddFriendly(board, x - 1, y - 1, candidates, friendly);
                        AddFriendly(board, x + 1, y - 1, candidates, friendly);
                        AddFriendly(board, x - 2, y, candidates, friendly);
                        AddFriendly(board, x, y - 2, candidates, friendly);
                        chosen = friendly.Count > 0 ? friendly[rng.NextInt(friendly.Count)] : candidates[rng.NextInt(candidates.Count)];
                    }
                    else
                    {
                        chosen = candidates[rng.NextInt(candidates.Count)];
                    }

                    board[pos] = board.CreatePiece(chosen);
                }
            }

            if (o.IceCells > 0)
            {
                foreach (Pos p in PickDistinctCells(o.Width, o.Height, o.IceCells, rng, stoneSet))
                {
                    board.SetIce(p, o.IceLayers);
                }
            }

            if (o.MirrorCells > 0)
            {
                foreach (Pos p in PickDistinctCells(o.Width, o.Height, o.MirrorCells, rng, stoneSet))
                {
                    // A mirror whose reflection is a block would do nothing, and a mirror on its own centre cell
                    // would reflect onto itself: both make the mechanic look broken rather than clever.
                    Pos opposite = board.Opposite(p);
                    if (board[p].IsPlainGem && !opposite.Equals(p) && board[opposite].IsPlainGem)
                    {
                        board.SetMirror(p, true);
                    }
                }
            }

            if (o.CursedCells > 0)
            {
                foreach (Pos p in PickDistinctCells(o.Width, o.Height, o.CursedCells, rng, stoneSet))
                {
                    if (board[p].IsPlainGem)
                    {
                        board.SetCursed(p, true);
                    }
                }
            }

            if (o.ChainCells > 0)
            {
                foreach (Pos p in PickDistinctCells(o.Width, o.Height, o.ChainCells, rng, stoneSet))
                {
                    if (board[p].IsPlainGem)
                    {
                        board.SetChain(p, o.ChainLinks);
                    }
                }
                // Pinning gems can leave a board with no legal swap at all: put one back if that happened.
                BoardShuffler.EnsurePlayable(board, rng, o.ColorCount);
            }

            return board;
        }

        private static void PlaceBlocks(GameBoard board, BoardGenerationOptions o, DeterministicRandom rng, HashSet<Pos> taken, int count, PieceType type, int hp)
        {
            if (count <= 0)
            {
                return;
            }
            var excluded = new HashSet<Pos>(taken);
            for (int y = 0; y < o.Height / 2; y++)
            {
                for (int x = 0; x < o.Width; x++)
                {
                    excluded.Add(new Pos(x, y));
                }
            }
            foreach (Pos p in PickDistinctCells(o.Width, o.Height, count, rng, excluded))
            {
                board[p] = board.CreatePiece(PieceColor.None, type, (byte)hp);
                taken.Add(p);
            }
        }

        /// <summary>True if placing <paramref name="color"/> at (x,y) completes a run of 3 with already-filled cells to the left or below.</summary>
        private static bool WouldMatch(GameBoard board, int x, int y, PieceColor color)
        {
            if (x >= 2 && Same(board, x - 1, y, color) && Same(board, x - 2, y, color))
            {
                return true;
            }
            if (y >= 2 && Same(board, x, y - 1, color) && Same(board, x, y - 2, color))
            {
                return true;
            }
            // 2x2 blocks are matches too (they create a Cross bomb), so the opening board must not contain one.
            return x >= 1 && y >= 1 && Same(board, x - 1, y, color) && Same(board, x, y - 1, color) && Same(board, x - 1, y - 1, color);
        }

        private static bool Same(GameBoard board, int x, int y, PieceColor color)
        {
            Piece p = board[x, y];
            return p.IsMatchable && p.Color == color;
        }

        private static void AddFriendly(GameBoard board, int x, int y, List<PieceColor> allowed, List<PieceColor> output)
        {
            if (!board.InBounds(x, y))
            {
                return;
            }
            Piece p = board[x, y];
            if (p.IsMatchable && allowed.Contains(p.Color))
            {
                output.Add(p.Color);
            }
        }

        private static List<Pos> PickDistinctCells(int width, int height, int count, DeterministicRandom rng, HashSet<Pos> excluded)
        {
            var pool = new List<Pos>(width * height);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    var p = new Pos(x, y);
                    if (excluded == null || !excluded.Contains(p))
                    {
                        pool.Add(p);
                    }
                }
            }

            count = Math.Min(count, pool.Count);
            // Partial Fisher-Yates: only the first `count` slots need to be random.
            for (int i = 0; i < count; i++)
            {
                int j = i + rng.NextInt(pool.Count - i);
                Pos tmp = pool[i];
                pool[i] = pool[j];
                pool[j] = tmp;
            }
            return pool.GetRange(0, count);
        }

        private static int Clamp(int permille) => permille < 0 ? 0 : (permille > 1000 ? 1000 : permille);
    }
}
