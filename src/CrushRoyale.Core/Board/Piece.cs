using System;

namespace CrushRoyale.Core.Board
{
    /// <summary>The 6 gem colors from the GDD. Each color also has a distinct SHAPE on screen (color-blind support).</summary>
    public enum PieceColor : byte
    {
        Red = 0,
        Blue = 1,
        Green = 2,
        Yellow = 3,
        Purple = 4,
        Orange = 5,
        None = 255
    }

    /// <summary>What a piece does when it is cleared.</summary>
    public enum PieceType : byte
    {
        /// <summary>Plain gem.</summary>
        Normal = 0,

        /// <summary>Bonus from a horizontal Match-4: clears its whole row when cleared.</summary>
        LineHorizontal = 1,

        /// <summary>Bonus from a vertical Match-4: clears its whole column when cleared.</summary>
        LineVertical = 2,

        /// <summary>Bonus from an L/T match: clears the surrounding 3x3 when cleared.</summary>
        AreaBomb = 3,

        /// <summary>Obstacle: cannot be swapped or matched, falls with gravity, broken by adjacent matches/blasts.</summary>
        Stone = 4
    }

    /// <summary>
    /// Immutable piece value. Id is unique per board (never reused) so views can track a piece
    /// through swaps, falls and shuffles. Id 0 means "empty cell".
    /// </summary>
    public readonly struct Piece : IEquatable<Piece>
    {
        public static readonly Piece Empty = default;

        public readonly int Id;
        public readonly PieceColor Color;
        public readonly PieceType Type;

        /// <summary>Remaining hit points (stones only; 0 for gems).</summary>
        public readonly byte Hp;

        public Piece(int id, PieceColor color, PieceType type, byte hp = 0)
        {
            if (id <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(id), "Piece ids start at 1.");
            }
            if (type == PieceType.Stone && color != PieceColor.None)
            {
                throw new ArgumentException("Stones have no color.", nameof(color));
            }
            if (type != PieceType.Stone && color == PieceColor.None)
            {
                throw new ArgumentException("Gems need a color.", nameof(color));
            }

            Id = id;
            Color = color;
            Type = type;
            Hp = type == PieceType.Stone ? (byte)Math.Max(1, (int)hp) : (byte)0;
        }

        public bool IsEmpty => Id == 0;

        public bool IsStone => !IsEmpty && Type == PieceType.Stone;

        /// <summary>Line or area bomb.</summary>
        public bool IsSpecial => !IsEmpty && (Type == PieceType.LineHorizontal || Type == PieceType.LineVertical || Type == PieceType.AreaBomb);

        /// <summary>Can take part in a color match.</summary>
        public bool IsMatchable => !IsEmpty && Type != PieceType.Stone;

        /// <summary>Can be moved by the player.</summary>
        public bool CanSwap => IsMatchable;

        public Piece WithHp(byte hp) => new Piece(Id, Color, Type, hp);

        public bool Equals(Piece other) => Id == other.Id && Color == other.Color && Type == other.Type && Hp == other.Hp;

        public override bool Equals(object obj) => obj is Piece other && Equals(other);

        public override int GetHashCode() => Id;

        public static bool operator ==(Piece a, Piece b) => a.Equals(b);

        public static bool operator !=(Piece a, Piece b) => !a.Equals(b);

        public override string ToString() => IsEmpty ? "Empty" : "#" + Id + " " + Color + " " + Type;
    }
}
