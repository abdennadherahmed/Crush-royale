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
        Stone = 4,

        /// <summary>
        /// Corrupted crystal (from stage 201): a colorless block broken like a stone, which spreads to a neighbouring
        /// gem after every move that destroyed none of it.
        /// </summary>
        Blight = 5,

        /// <summary>Dragon egg (from stage 301): a colorless 2-hit block that hatches into a bonus gem.</summary>
        Egg = 6,

        /// <summary>
        /// Countdown bomb (from stage 101): a normal colored gem whose Hp is the number of moves left; the stage is lost
        /// when it reaches 0. Matching or blasting it defuses it.
        /// </summary>
        TimeBomb = 7,

        /// <summary>Cross bomb from a 2x2 match: clears its whole row AND column.</summary>
        Cross = 8,

        /// <summary>
        /// Corruption forge (from stage 401): a colorless block, tougher than a stone, that turns one neighbouring
        /// gem into blight after every move until it is destroyed. The blight it makes still spreads on its own, so
        /// a forge left standing does not merely add work, it compounds.
        /// </summary>
        Forge = 9,

        /// <summary>
        /// Warden (from stage 801): a block that heals a point of damage on any move that did not hit it.
        ///
        /// Everything else on the board can be chipped away at leisure. A warden cannot: it has to be finished in
        /// consecutive moves or the work is undone, which is the first time the game asks for a plan rather than a
        /// sequence of individually good moves.
        /// </summary>
        Warden = 10
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
            bool block = IsBlockType(type);
            if (block && color != PieceColor.None)
            {
                throw new ArgumentException("Stones, blight and eggs have no color.", nameof(color));
            }
            if (!block && color == PieceColor.None)
            {
                throw new ArgumentException("Gems need a color.", nameof(color));
            }

            Id = id;
            Color = color;
            Type = type;
            Hp = block || type == PieceType.TimeBomb ? (byte)Math.Max(1, (int)hp) : (byte)0;
        }

        /// <summary>Colorless obstacles hit by neighbouring matches: stones, blight, eggs, forges.</summary>
        public static bool IsBlockType(PieceType type) =>
            type == PieceType.Stone || type == PieceType.Blight || type == PieceType.Egg
            || type == PieceType.Forge || type == PieceType.Warden;

        public bool IsEmpty => Id == 0;

        public bool IsStone => !IsEmpty && Type == PieceType.Stone;

        /// <summary>Stone, blight or egg: not swappable nor matchable, falls with gravity, hit by neighbouring matches.</summary>
        public bool IsBlock => !IsEmpty && IsBlockType(Type);

        public bool IsBlight => !IsEmpty && Type == PieceType.Blight;

        public bool IsEgg => !IsEmpty && Type == PieceType.Egg;

        public bool IsForge => !IsEmpty && Type == PieceType.Forge;

        public bool IsWarden => !IsEmpty && Type == PieceType.Warden;

        public bool IsTimeBomb => !IsEmpty && Type == PieceType.TimeBomb;

        /// <summary>Plain gem (no bonus, no bomb): the only kind hazards may convert.</summary>
        public bool IsPlainGem => !IsEmpty && Type == PieceType.Normal;

        /// <summary>Line or area bomb.</summary>
        public bool IsSpecial => !IsEmpty && (Type == PieceType.LineHorizontal || Type == PieceType.LineVertical || Type == PieceType.AreaBomb || Type == PieceType.Cross);

        /// <summary>Can take part in a color match.</summary>
        public bool IsMatchable => !IsEmpty && !IsBlockType(Type);

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
