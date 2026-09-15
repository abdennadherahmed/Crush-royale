using System;

namespace CrushRoyale.Core.Common
{
    /// <summary>
    /// Integer grid coordinate. X = column (0 = left), Y = row (0 = bottom).
    /// Gravity pulls pieces toward Y = 0. Unity adapters convert to/from Vector2Int.
    /// </summary>
    public readonly struct Pos : IEquatable<Pos>
    {
        public readonly int X;
        public readonly int Y;

        public Pos(int x, int y)
        {
            X = x;
            Y = y;
        }

        /// <summary>True when the two cells share an edge (no diagonals).</summary>
        public bool IsAdjacentTo(Pos other) => Math.Abs(X - other.X) + Math.Abs(Y - other.Y) == 1;

        public Pos Offset(int dx, int dy) => new Pos(X + dx, Y + dy);

        public bool Equals(Pos other) => X == other.X && Y == other.Y;

        public override bool Equals(object obj) => obj is Pos other && Equals(other);

        public override int GetHashCode() => (X * 397) ^ Y;

        public static bool operator ==(Pos a, Pos b) => a.Equals(b);

        public static bool operator !=(Pos a, Pos b) => !a.Equals(b);

        public override string ToString() => "(" + X + "," + Y + ")";
    }
}
