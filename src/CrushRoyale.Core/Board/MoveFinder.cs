using System;
using System.Collections.Generic;
using CrushRoyale.Core.Common;

namespace CrushRoyale.Core.Board
{
    /// <summary>A player swap between two adjacent cells.</summary>
    public readonly struct Move : IEquatable<Move>
    {
        public readonly Pos From;
        public readonly Pos To;

        public Move(Pos from, Pos to)
        {
            From = from;
            To = to;
        }

        public bool Equals(Move other) => From == other.From && To == other.To;

        public override bool Equals(object obj) => obj is Move other && Equals(other);

        public override int GetHashCode() => From.GetHashCode() * 31 + To.GetHashCode();

        public override string ToString() => From + "->" + To;
    }

    /// <summary>Finds swaps that produce at least one match.</summary>
    public static class MoveFinder
    {
        /// <summary>Why a swap is (in)valid, without mutating the board.</summary>
        public static ErrorCode CheckSwap(GameBoard board, Pos a, Pos b)
        {
            if (!board.InBounds(a) || !board.InBounds(b))
            {
                return ErrorCode.InvalidArgument;
            }
            if (!a.IsAdjacentTo(b))
            {
                return ErrorCode.NotAdjacent;
            }
            if (!board[a].CanSwap || !board[b].CanSwap)
            {
                return ErrorCode.NotSwappable;
            }
            if (IsSpecialCombo(board, a, b))
            {
                return ErrorCode.None;
            }
            return CreatesMatch(board, a, b) ? ErrorCode.None : ErrorCode.NoMatch;
        }

        public static bool IsValidSwap(GameBoard board, Pos a, Pos b) => CheckSwap(board, a, b) == ErrorCode.None;

        /// <summary>Two adjacent bonuses (any kinds, any colors) can always be swapped: their effects combine.</summary>
        public static bool IsSpecialCombo(GameBoard board, Pos a, Pos b) => board[a].IsSpecial && board[b].IsSpecial;

        /// <summary>All valid swaps. Each unordered pair appears once (From is left/below To).</summary>
        public static List<Move> FindValidMoves(GameBoard board)
        {
            var moves = new List<Move>();
            Scan(board, int.MaxValue, moves);
            return moves;
        }

        /// <summary>Counts valid swaps, stopping early once <paramref name="stopAt"/> is reached (fast path for generation).</summary>
        public static int CountValidMoves(GameBoard board, int stopAt = int.MaxValue)
        {
            return Scan(board, stopAt, null);
        }

        public static bool HasValidMove(GameBoard board) => CountValidMoves(board, 1) > 0;

        private static int Scan(GameBoard board, int stopAt, List<Move> output)
        {
            int count = 0;
            for (int y = 0; y < board.Height; y++)
            {
                for (int x = 0; x < board.Width; x++)
                {
                    var a = new Pos(x, y);
                    if (!board[a].CanSwap)
                    {
                        continue;
                    }

                    // Only look right and up: every unordered pair is visited exactly once.
                    if (x + 1 < board.Width && TryPair(board, a, new Pos(x + 1, y), output))
                    {
                        if (++count >= stopAt)
                        {
                            return count;
                        }
                    }
                    if (y + 1 < board.Height && TryPair(board, a, new Pos(x, y + 1), output))
                    {
                        if (++count >= stopAt)
                        {
                            return count;
                        }
                    }
                }
            }
            return count;
        }

        private static bool TryPair(GameBoard board, Pos a, Pos b, List<Move> output)
        {
            Piece pb = board[b];
            if (!pb.CanSwap)
            {
                return false;
            }
            if (IsSpecialCombo(board, a, b))
            {
                output?.Add(new Move(a, b));
                return true;
            }
            if (pb.Color == board[a].Color)
            {
                return false;
            }
            if (!CreatesMatch(board, a, b))
            {
                return false;
            }
            output?.Add(new Move(a, b));
            return true;
        }

        private static bool CreatesMatch(GameBoard board, Pos a, Pos b)
        {
            board.Swap(a, b);
            try
            {
                return MatchFinder.HasMatchAt(board, a) || MatchFinder.HasMatchAt(board, b);
            }
            finally
            {
                board.Swap(a, b);
            }
        }
    }
}
