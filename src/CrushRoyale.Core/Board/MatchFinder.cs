using System.Collections.Generic;
using CrushRoyale.Core.Common;

namespace CrushRoyale.Core.Board
{
    /// <summary>Classification of a connected match, from weakest to strongest.</summary>
    public enum MatchShape
    {
        /// <summary>3 in a line.</summary>
        Line3 = 0,

        /// <summary>4 in a line: creates a Line bomb.</summary>
        Line4 = 1,

        /// <summary>A horizontal and a vertical run crossing (L or T): creates an Area bomb.</summary>
        Cross = 2,

        /// <summary>5+ in a line: clears every gem of that color (GDD "Match 5").</summary>
        Line5 = 3,

        /// <summary>2x2 block of the same color: creates a Cross bomb (row + column).</summary>
        Square = 4
    }

    /// <summary>One straight run of 3+ same-colored gems.</summary>
    public sealed class MatchRun
    {
        public MatchRun(PieceColor color, bool horizontal, List<Pos> cells)
        {
            Color = color;
            Horizontal = horizontal;
            Cells = cells;
        }

        public PieceColor Color { get; }

        public bool Horizontal { get; }

        /// <summary>Cells ordered left-to-right (horizontal) or bottom-to-top (vertical).</summary>
        public List<Pos> Cells { get; }

        public int Length => Cells.Count;
    }

    /// <summary>Runs of the same color that share at least one cell, merged into a single match.</summary>
    public sealed class MatchGroup
    {
        /// <summary>Square match: the four cells of a 2x2 block, kept as one "run" so spawn logic stays uniform.</summary>
        internal MatchGroup(PieceColor color, List<Pos> cells, MatchShape shape)
        {
            Color = color;
            Cells = cells;
            Runs = new List<MatchRun> { new MatchRun(color, true, new List<Pos>(cells)) };
            Shape = shape;
            LongestRun = 2;
        }

        internal MatchGroup(PieceColor color, List<MatchRun> runs, List<Pos> cells)
        {
            Color = color;
            Runs = runs;
            Cells = cells;

            int longest = 0;
            bool hasH = false;
            bool hasV = false;
            foreach (MatchRun run in runs)
            {
                if (run.Length > longest)
                {
                    longest = run.Length;
                }
                hasH |= run.Horizontal;
                hasV |= !run.Horizontal;
            }

            LongestRun = longest;
            if (longest >= 5)
            {
                Shape = MatchShape.Line5;
            }
            else if (hasH && hasV)
            {
                Shape = MatchShape.Cross;
            }
            else if (longest == 4)
            {
                Shape = MatchShape.Line4;
            }
            else
            {
                Shape = MatchShape.Line3;
            }
        }

        public PieceColor Color { get; }

        public IReadOnlyList<MatchRun> Runs { get; }

        /// <summary>Distinct cells, sorted bottom-to-top then left-to-right.</summary>
        public IReadOnlyList<Pos> Cells { get; }

        public MatchShape Shape { get; }

        public int LongestRun { get; }

        public bool Contains(Pos p)
        {
            for (int i = 0; i < Cells.Count; i++)
            {
                if (Cells[i] == p)
                {
                    return true;
                }
            }
            return false;
        }
    }

    /// <summary>Stateless match detection. All outputs are in a deterministic order.</summary>
    public static class MatchFinder
    {
        /// <summary>
        /// Finds every horizontal and vertical run of 3+ matchable gems of the same color, then merges
        /// runs that share a cell (L/T shapes) into groups.
        /// </summary>
        public static List<MatchGroup> FindMatches(GameBoard board)
        {
            var runs = new List<MatchRun>();
            CollectRuns(board, true, runs);
            CollectRuns(board, false, runs);

            var groups = new List<MatchGroup>();
            if (runs.Count == 0)
            {
                // No line, but a 2x2 block is a match on its own.
                AddSquares(board, groups);
                groups.Sort((a, b) => ComparePos(a.Cells[0], b.Cells[0]));
                return groups;
            }

            // Union-find over runs: same color + shared cell => same group.
            int[] parent = new int[runs.Count];
            for (int i = 0; i < parent.Length; i++)
            {
                parent[i] = i;
            }

            var owner = new Dictionary<Pos, int>();
            for (int r = 0; r < runs.Count; r++)
            {
                foreach (Pos cell in runs[r].Cells)
                {
                    if (owner.TryGetValue(cell, out int other))
                    {
                        Union(parent, r, other);
                    }
                    else
                    {
                        owner[cell] = r;
                    }
                }
            }

            var byRoot = new SortedDictionary<int, List<MatchRun>>();
            for (int r = 0; r < runs.Count; r++)
            {
                int root = Find(parent, r);
                if (!byRoot.TryGetValue(root, out List<MatchRun> list))
                {
                    list = new List<MatchRun>();
                    byRoot[root] = list;
                }
                list.Add(runs[r]);
            }

            foreach (List<MatchRun> groupRuns in byRoot.Values)
            {
                var seen = new HashSet<Pos>();
                var cells = new List<Pos>();
                foreach (MatchRun run in groupRuns)
                {
                    foreach (Pos cell in run.Cells)
                    {
                        if (seen.Add(cell))
                        {
                            cells.Add(cell);
                        }
                    }
                }
                cells.Sort(ComparePos);
                groups.Add(new MatchGroup(groupRuns[0].Color, groupRuns, cells));
            }

            AddSquares(board, groups);
            groups.Sort((a, b) => ComparePos(a.Cells[0], b.Cells[0]));
            return groups;
        }

        /// <summary>
        /// 2x2 blocks of one color also count (players expect it from other match-3 games) and create a Cross bomb.
        /// Cells already taken by a line match are skipped: a line always wins over the square it overlaps.
        /// </summary>
        private static void AddSquares(GameBoard board, List<MatchGroup> groups)
        {
            var taken = new HashSet<Pos>();
            foreach (MatchGroup group in groups)
            {
                foreach (Pos cell in group.Cells)
                {
                    taken.Add(cell);
                }
            }
            for (int y = 0; y + 1 < board.Height; y++)
            {
                for (int x = 0; x + 1 < board.Width; x++)
                {
                    var corner = new Pos(x, y);
                    if (!IsSquareAt(board, corner))
                    {
                        continue;
                    }
                    var cells = new List<Pos> { corner, new Pos(x + 1, y), new Pos(x, y + 1), new Pos(x + 1, y + 1) };
                    bool free = true;
                    foreach (Pos cell in cells)
                    {
                        free &= !taken.Contains(cell);
                    }
                    if (!free)
                    {
                        continue;
                    }
                    foreach (Pos cell in cells)
                    {
                        taken.Add(cell);
                    }
                    cells.Sort(ComparePos);
                    groups.Add(new MatchGroup(board[corner].Color, cells, MatchShape.Square));
                }
            }
        }

        /// <summary>True if (x,y) is the bottom-left corner of a 2x2 block of the same color.</summary>
        public static bool IsSquareAt(GameBoard board, Pos corner)
        {
            Piece piece = board[corner];
            if (!piece.IsMatchable || !board.InBounds(corner.X + 1, corner.Y + 1))
            {
                return false;
            }
            return Same(board, corner.X + 1, corner.Y, piece.Color)
                && Same(board, corner.X, corner.Y + 1, piece.Color)
                && Same(board, corner.X + 1, corner.Y + 1, piece.Color);
        }

        private static bool Same(GameBoard board, int x, int y, PieceColor color)
        {
            Piece p = board[x, y];
            return p.IsMatchable && p.Color == color;
        }

        /// <summary>True if the gem at <paramref name="p"/> belongs to a 2x2 block of its color.</summary>
        public static bool HasSquareAt(GameBoard board, Pos p)
        {
            for (int dy = -1; dy <= 0; dy++)
            {
                for (int dx = -1; dx <= 0; dx++)
                {
                    var corner = new Pos(p.X + dx, p.Y + dy);
                    if (board.InBounds(corner) && IsSquareAt(board, corner))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>True if the gem at <paramref name="p"/> is part of a horizontal or vertical run of 3+.</summary>
        public static bool HasMatchAt(GameBoard board, Pos p)
        {
            Piece piece = board[p];
            if (!piece.IsMatchable)
            {
                return false;
            }

            int horizontal = 1 + CountDirection(board, p, -1, 0, piece.Color) + CountDirection(board, p, 1, 0, piece.Color);
            if (horizontal >= 3)
            {
                return true;
            }

            int vertical = 1 + CountDirection(board, p, 0, -1, piece.Color) + CountDirection(board, p, 0, 1, piece.Color);
            return vertical >= 3 || HasSquareAt(board, p);
        }

        /// <summary>True if the board contains any run of 3+.</summary>
        public static bool HasAnyMatch(GameBoard board)
        {
            for (int y = 0; y < board.Height; y++)
            {
                for (int x = 0; x < board.Width; x++)
                {
                    if (HasMatchAt(board, new Pos(x, y)))
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        internal static int ComparePos(Pos a, Pos b)
        {
            if (a.Y != b.Y)
            {
                return a.Y.CompareTo(b.Y);
            }
            return a.X.CompareTo(b.X);
        }

        private static int CountDirection(GameBoard board, Pos start, int dx, int dy, PieceColor color)
        {
            int count = 0;
            int x = start.X + dx;
            int y = start.Y + dy;
            while (board.InBounds(x, y))
            {
                Piece p = board[x, y];
                if (!p.IsMatchable || p.Color != color)
                {
                    break;
                }
                count++;
                x += dx;
                y += dy;
            }
            return count;
        }

        private static void CollectRuns(GameBoard board, bool horizontal, List<MatchRun> output)
        {
            int outer = horizontal ? board.Height : board.Width;
            int inner = horizontal ? board.Width : board.Height;

            for (int o = 0; o < outer; o++)
            {
                int i = 0;
                while (i < inner)
                {
                    Piece first = horizontal ? board[i, o] : board[o, i];
                    if (!first.IsMatchable)
                    {
                        i++;
                        continue;
                    }

                    int end = i + 1;
                    while (end < inner)
                    {
                        Piece next = horizontal ? board[end, o] : board[o, end];
                        if (!next.IsMatchable || next.Color != first.Color)
                        {
                            break;
                        }
                        end++;
                    }

                    int length = end - i;
                    if (length >= 3)
                    {
                        var cells = new List<Pos>(length);
                        for (int k = i; k < end; k++)
                        {
                            cells.Add(horizontal ? new Pos(k, o) : new Pos(o, k));
                        }
                        output.Add(new MatchRun(first.Color, horizontal, cells));
                    }
                    i = end;
                }
            }
        }

        private static int Find(int[] parent, int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }
            return i;
        }

        private static void Union(int[] parent, int a, int b)
        {
            int ra = Find(parent, a);
            int rb = Find(parent, b);
            if (ra == rb)
            {
                return;
            }
            // Keep the smallest index as root so group order is stable.
            if (ra < rb)
            {
                parent[rb] = ra;
            }
            else
            {
                parent[ra] = rb;
            }
        }
    }
}
