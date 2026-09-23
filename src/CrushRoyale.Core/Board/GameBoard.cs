using System;
using System.Collections.Generic;
using System.Text;
using CrushRoyale.Core.Common;

namespace CrushRoyale.Core.Board
{
    /// <summary>
    /// The playfield: a grid of pieces plus an ice layer per cell.
    /// Pure data + small helpers; all rules live in MatchFinder / MoveFinder / ResolutionEngine.
    /// </summary>
    public sealed class GameBoard
    {
        public const int MinSize = 3;
        public const int MaxSize = 16;

        private readonly Piece[] _cells;
        private readonly byte[] _ice;

        /// <summary>
        /// Chains (stage 501+): a chained gem cannot be moved, though it can still be matched where it stands.
        ///
        /// This is the opposite of ice. Ice stops a gem being destroyed and is broken by clearing the gem under it;
        /// a chain stops a gem being swapped and is broken by clearing next to it. Nothing the player does to the
        /// gem itself frees it -- the answer is always somewhere else on the board.
        /// </summary>
        private readonly byte[] _chains;

        /// <summary>
        /// Curses (stage 601+): gems that must NOT be matched.
        ///
        /// Every other hazard asks the player to destroy something. This one asks them not to, which turns the
        /// highest-scoring move into the wrong move and is the first time the board rewards restraint.
        /// </summary>
        private readonly bool[] _cursed;

        public GameBoard(int width = 8, int height = 8)
        {
            if (width < MinSize || width > MaxSize)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }
            if (height < MinSize || height > MaxSize)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }

            Width = width;
            Height = height;
            _cells = new Piece[width * height];
            _ice = new byte[width * height];
            _chains = new byte[width * height];
            _cursed = new bool[width * height];
            NextPieceId = 1;
        }

        public int Width { get; }

        public int Height { get; }

        public int CellCount => _cells.Length;

        /// <summary>Id given to the next created piece. Part of the deterministic state.</summary>
        public int NextPieceId { get; private set; }

        public Piece this[Pos p]
        {
            get => _cells[Index(p)];
            set => _cells[Index(p)] = value;
        }

        public Piece this[int x, int y]
        {
            get => this[new Pos(x, y)];
            set => this[new Pos(x, y)] = value;
        }

        public bool InBounds(Pos p) => p.X >= 0 && p.Y >= 0 && p.X < Width && p.Y < Height;

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        /// <summary>Creates a new piece with a fresh unique id (does not place it).</summary>
        public Piece CreatePiece(PieceColor color, PieceType type = PieceType.Normal, byte hp = 0)
        {
            return new Piece(NextPieceId++, color, type, hp);
        }

        public int IceAt(Pos p) => _ice[Index(p)];

        public void SetIce(Pos p, int layers)
        {
            if (layers < 0 || layers > 3)
            {
                throw new ArgumentOutOfRangeException(nameof(layers), "Ice layers must be 0-3.");
            }
            _ice[Index(p)] = (byte)layers;
        }

        /// <summary>Removes one ice layer. Returns true if a layer was actually broken.</summary>
        public bool ReduceIce(Pos p)
        {
            int i = Index(p);
            if (_ice[i] == 0)
            {
                return false;
            }
            _ice[i]--;
            return true;
        }

        public bool IsCursed(Pos p) => _cursed[Index(p)];

        public void SetCursed(Pos p, bool cursed) => _cursed[Index(p)] = cursed;

        public int CursedCells
        {
            get
            {
                int total = 0;
                for (int i = 0; i < _cursed.Length; i++)
                {
                    total += _cursed[i] ? 1 : 0;
                }
                return total;
            }
        }

        public int ChainAt(Pos p) => _chains[Index(p)];

        public void SetChain(Pos p, int links)
        {
            if (links < 0 || links > 3)
            {
                throw new ArgumentOutOfRangeException(nameof(links), "Chain links must be 0-3.");
            }
            _chains[Index(p)] = (byte)links;
        }

        /// <summary>Removes one link. Returns true if a link was actually broken.</summary>
        public bool BreakChain(Pos p)
        {
            int i = Index(p);
            if (_chains[i] == 0)
            {
                return false;
            }
            _chains[i]--;
            return true;
        }

        public int ChainedCells
        {
            get
            {
                int total = 0;
                for (int i = 0; i < _chains.Length; i++)
                {
                    total += _chains[i] > 0 ? 1 : 0;
                }
                return total;
            }
        }

        public int TotalIceLayers
        {
            get
            {
                int total = 0;
                for (int i = 0; i < _ice.Length; i++)
                {
                    total += _ice[i];
                }
                return total;
            }
        }

        public void Swap(Pos a, Pos b)
        {
            int ia = Index(a);
            int ib = Index(b);
            Piece tmp = _cells[ia];
            _cells[ia] = _cells[ib];
            _cells[ib] = tmp;
        }

        /// <summary>All positions, row by row from the bottom-left. Deterministic order.</summary>
        public IEnumerable<Pos> AllPositions()
        {
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    yield return new Pos(x, y);
                }
            }
        }

        public int CountColor(PieceColor color)
        {
            int count = 0;
            for (int i = 0; i < _cells.Length; i++)
            {
                if (_cells[i].IsMatchable && _cells[i].Color == color)
                {
                    count++;
                }
            }
            return count;
        }

        public int CountStones()
        {
            int count = 0;
            for (int i = 0; i < _cells.Length; i++)
            {
                if (_cells[i].IsStone)
                {
                    count++;
                }
            }
            return count;
        }

        public bool TryFindPiece(int pieceId, out Pos position)
        {
            for (int i = 0; i < _cells.Length; i++)
            {
                if (_cells[i].Id == pieceId && pieceId != 0)
                {
                    position = new Pos(i % Width, i / Width);
                    return true;
                }
            }
            position = default;
            return false;
        }

        public GameBoard Clone()
        {
            var copy = new GameBoard(Width, Height);
            Array.Copy(_cells, copy._cells, _cells.Length);
            Array.Copy(_ice, copy._ice, _ice.Length);
            Array.Copy(_chains, copy._chains, _chains.Length);
            Array.Copy(_cursed, copy._cursed, _cursed.Length);
            copy.NextPieceId = NextPieceId;
            return copy;
        }

        /// <summary>
        /// Stable hash of the full state (pieces, ice, id counter). Used for replay checkpoints:
        /// client and server must produce identical values at identical times.
        /// </summary>
        public ulong ComputeHash()
        {
            var h = new HashBuilder();
            h.Add(Width).Add(Height).Add(NextPieceId);
            for (int i = 0; i < _cells.Length; i++)
            {
                Piece p = _cells[i];
                h.Add(p.Id).Add((byte)p.Color).Add((byte)p.Type).Add(p.Hp).Add(_ice[i]).Add(_chains[i]).Add(_cursed[i] ? (byte)1 : (byte)0);
            }
            return h.Value;
        }

        internal int Index(Pos p)
        {
            if (!InBounds(p))
            {
                throw new ArgumentOutOfRangeException(nameof(p), "Position " + p + " is outside the " + Width + "x" + Height + " board.");
            }
            return p.Y * Width + p.X;
        }

        /// <summary>
        /// Human-readable dump, top row first. Tokens: "R." normal red, "Rh"/"Rv" line bombs,
        /// "Ra" area bomb, "#1" stone with hp, ".." empty. Ice is shown as a trailing "*" count per row.
        /// </summary>
        public string ToDebugString()
        {
            var sb = new StringBuilder();
            for (int y = Height - 1; y >= 0; y--)
            {
                for (int x = 0; x < Width; x++)
                {
                    if (x > 0)
                    {
                        sb.Append(' ');
                    }
                    sb.Append(Token(this[x, y]));
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>Parses the format produced by <see cref="ToDebugString"/> (used heavily by tests).</summary>
        public static GameBoard FromDebugString(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                throw new ArgumentException("Board text is empty.", nameof(text));
            }

            string[] rawRows = text.Replace("\r", string.Empty).Trim('\n').Split('\n');
            var rows = new List<string[]>();
            foreach (string raw in rawRows)
            {
                string trimmed = raw.Trim();
                if (trimmed.Length == 0)
                {
                    continue;
                }
                rows.Add(trimmed.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            }

            int height = rows.Count;
            int width = rows[0].Length;
            var board = new GameBoard(width, height);
            for (int r = 0; r < height; r++)
            {
                if (rows[r].Length != width)
                {
                    throw new FormatException("Row " + r + " has " + rows[r].Length + " cells, expected " + width + ".");
                }
                int y = height - 1 - r;
                for (int x = 0; x < width; x++)
                {
                    board[x, y] = ParseToken(board, rows[r][x]);
                }
            }
            return board;
        }

        private static string Token(Piece p)
        {
            if (p.IsEmpty)
            {
                return "..";
            }
            if (p.IsStone)
            {
                return "#" + p.Hp;
            }
            if (p.IsBlight)
            {
                return "%" + p.Hp;
            }
            if (p.IsEgg)
            {
                return "@" + p.Hp;
            }
            if (p.IsTimeBomb)
            {
                return ColorChar(p.Color) + "t";
            }

            char c = ColorChar(p.Color);
            switch (p.Type)
            {
                case PieceType.LineHorizontal: return c + "h";
                case PieceType.LineVertical: return c + "v";
                case PieceType.AreaBomb: return c + "a";
                case PieceType.Cross: return c + "x";
                default: return c + ".";
            }
        }

        private static Piece ParseToken(GameBoard board, string token)
        {
            if (token.Length != 2)
            {
                throw new FormatException("Invalid token '" + token + "'.");
            }
            if (token == "..")
            {
                return Piece.Empty;
            }
            if (token[0] == '#')
            {
                return board.CreatePiece(PieceColor.None, PieceType.Stone, (byte)(token[1] - '0'));
            }
            if (token[0] == '%')
            {
                return board.CreatePiece(PieceColor.None, PieceType.Blight, (byte)(token[1] - '0'));
            }
            if (token[0] == '@')
            {
                return board.CreatePiece(PieceColor.None, PieceType.Egg, (byte)(token[1] - '0'));
            }

            PieceColor color = ParseColor(token[0]);
            switch (token[1])
            {
                case '.': return board.CreatePiece(color);
                case 'h': return board.CreatePiece(color, PieceType.LineHorizontal);
                case 'v': return board.CreatePiece(color, PieceType.LineVertical);
                case 'a': return board.CreatePiece(color, PieceType.AreaBomb);
                case 'x': return board.CreatePiece(color, PieceType.Cross);
                case 't': return board.CreatePiece(color, PieceType.TimeBomb, 5);
                default: throw new FormatException("Invalid piece type in token '" + token + "'.");
            }
        }

        public static char ColorChar(PieceColor color)
        {
            switch (color)
            {
                case PieceColor.Red: return 'R';
                case PieceColor.Blue: return 'B';
                case PieceColor.Green: return 'G';
                case PieceColor.Yellow: return 'Y';
                case PieceColor.Purple: return 'P';
                case PieceColor.Orange: return 'O';
                default: return '?';
            }
        }

        public static PieceColor ParseColor(char c)
        {
            switch (c)
            {
                case 'R': return PieceColor.Red;
                case 'B': return PieceColor.Blue;
                case 'G': return PieceColor.Green;
                case 'Y': return PieceColor.Yellow;
                case 'P': return PieceColor.Purple;
                case 'O': return PieceColor.Orange;
                default: throw new FormatException("Unknown color '" + c + "'.");
            }
        }
    }
}
