using System;
using CrushRoyale.Core.Common;

namespace CrushRoyale.Core.Board
{
    /// <summary>
    /// Produces the colors of gems that drop in from the top.
    /// Each column has its own independent random stream keyed by (seed, column, spawn index), so the
    /// N-th gem to fall into column X is the same for every player sharing a seed, whatever moves they make.
    /// This is what makes ghost PvP comparable: both players face the same board AND the same refills.
    /// </summary>
    public sealed class RefillSpawner
    {
        private readonly ulong _seed;
        private readonly int _colorCount;
        private readonly int[] _counters;

        public RefillSpawner(ulong seed, int width, int colorCount)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }
            if (colorCount < 3 || colorCount > 6)
            {
                throw new ArgumentOutOfRangeException(nameof(colorCount), "Color count must be 3-6.");
            }

            _seed = seed;
            _colorCount = colorCount;
            _counters = new int[width];
        }

        public int ColorCount => _colorCount;

        /// <summary>Next color for <paramref name="column"/>; advances that column's stream.</summary>
        public PieceColor NextColor(int column)
        {
            if (column < 0 || column >= _counters.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(column));
            }

            int index = _counters[column]++;
            var rng = DeterministicRandom.Derive(_seed, (ulong)column + 1UL, (ulong)index);
            return (PieceColor)rng.NextInt(_colorCount);
        }

        /// <summary>Peek without advancing (used by hints/tests).</summary>
        public PieceColor PeekColor(int column, int offset = 0)
        {
            if (column < 0 || column >= _counters.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(column));
            }
            var rng = DeterministicRandom.Derive(_seed, (ulong)column + 1UL, (ulong)(_counters[column] + offset));
            return (PieceColor)rng.NextInt(_colorCount);
        }

        public int SpawnedInColumn(int column) => _counters[column];

        public RefillSpawner Clone()
        {
            var copy = new RefillSpawner(_seed, _counters.Length, _colorCount);
            Array.Copy(_counters, copy._counters, _counters.Length);
            return copy;
        }
    }
}
