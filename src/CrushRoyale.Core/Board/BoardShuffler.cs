using System.Collections.Generic;
using CrushRoyale.Core.Common;

namespace CrushRoyale.Core.Board
{
    /// <summary>
    /// Deadlock recovery (missing from the GDD): when no valid move exists the board is reshuffled,
    /// keeping stones and ice in place and pieces' identities (so views can animate them), until it
    /// has no pre-existing match and at least one valid move.
    /// </summary>
    public static class BoardShuffler
    {
        private const int ShuffleAttempts = 60;

        /// <summary>Returns true if the board had to be modified.</summary>
        public static bool EnsurePlayable(GameBoard board, DeterministicRandom rng, int colorCount)
        {
            if (!MatchFinder.HasAnyMatch(board) && MoveFinder.HasValidMove(board))
            {
                return false;
            }

            var positions = new List<Pos>();
            var pieces = new List<Piece>();
            foreach (Pos p in board.AllPositions())
            {
                if (board[p].CanSwap)
                {
                    positions.Add(p);
                    pieces.Add(board[p]);
                }
            }

            for (int attempt = 0; attempt < ShuffleAttempts; attempt++)
            {
                rng.Shuffle(pieces);
                for (int i = 0; i < positions.Count; i++)
                {
                    board[positions[i]] = pieces[i];
                }
                if (!MatchFinder.HasAnyMatch(board) && MoveFinder.HasValidMove(board))
                {
                    return true;
                }
            }

            Recolor(board, positions, rng, colorCount);
            if (!MoveFinder.HasValidMove(board))
            {
                ForceMove(board, colorCount);
            }
            return true;
        }

        /// <summary>Re-rolls colors of plain gems (specials keep theirs) so that no run of 3 exists.</summary>
        private static void Recolor(GameBoard board, List<Pos> positions, DeterministicRandom rng, int colorCount)
        {
            var colors = new List<PieceColor>(colorCount);
            foreach (Pos p in positions)
            {
                Piece piece = board[p];
                if (piece.IsSpecial)
                {
                    continue;
                }

                colors.Clear();
                for (int c = 0; c < colorCount; c++)
                {
                    colors.Add((PieceColor)c);
                }
                rng.Shuffle(colors);

                foreach (PieceColor color in colors)
                {
                    board[p] = new Piece(piece.Id, color, piece.Type, piece.Hp);
                    if (!MatchFinder.HasMatchAt(board, p) && !NeighbourMatches(board, p))
                    {
                        break;
                    }
                }
            }
        }

        private static bool NeighbourMatches(GameBoard board, Pos p)
        {
            for (int d = -2; d <= 2; d++)
            {
                if (d == 0)
                {
                    continue;
                }
                var h = new Pos(p.X + d, p.Y);
                var v = new Pos(p.X, p.Y + d);
                if (board.InBounds(h) && MatchFinder.HasMatchAt(board, h))
                {
                    return true;
                }
                if (board.InBounds(v) && MatchFinder.HasMatchAt(board, v))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Last resort: paint the pattern "X X . X" (third gem one row above) somewhere so a single swap
        /// completes a line, while making sure no match exists yet.
        /// </summary>
        private static void ForceMove(GameBoard board, int colorCount)
        {
            for (int y = 0; y + 1 < board.Height; y++)
            {
                for (int x = 0; x + 2 < board.Width; x++)
                {
                    var a = new Pos(x, y);
                    var b = new Pos(x + 1, y);
                    var c = new Pos(x + 2, y + 1);
                    var d = new Pos(x + 2, y);
                    if (!IsPlain(board, a) || !IsPlain(board, b) || !IsPlain(board, c) || !board[d].CanSwap)
                    {
                        continue;
                    }

                    Piece oa = board[a];
                    Piece ob = board[b];
                    Piece oc = board[c];
                    for (int color = 0; color < colorCount; color++)
                    {
                        var pc = (PieceColor)color;
                        if (board[d].Color == pc)
                        {
                            continue;
                        }
                        board[a] = new Piece(oa.Id, pc, PieceType.Normal);
                        board[b] = new Piece(ob.Id, pc, PieceType.Normal);
                        board[c] = new Piece(oc.Id, pc, PieceType.Normal);
                        if (!MatchFinder.HasAnyMatch(board) && MoveFinder.HasValidMove(board))
                        {
                            return;
                        }
                    }
                    board[a] = oa;
                    board[b] = ob;
                    board[c] = oc;
                }
            }
        }

        private static bool IsPlain(GameBoard board, Pos p)
        {
            Piece piece = board[p];
            return piece.IsPlainGem;
        }
    }
}
