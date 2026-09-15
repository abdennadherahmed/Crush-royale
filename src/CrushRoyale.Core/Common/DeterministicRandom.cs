using System;
using System.Collections.Generic;

namespace CrushRoyale.Core.Common
{
    /// <summary>
    /// PCG32 random generator (O'Neill, pcg-random.org).
    /// Bit-exact on every platform (Unity/IL2CPP, Mono, CoreCLR), which is what makes
    /// server-side replay validation and ghost PvP possible. System.Random is NOT used because
    /// its algorithm is an implementation detail that differs between runtimes.
    /// Only integer outputs are exposed on purpose: floating point is banned from the simulation.
    /// </summary>
    public sealed class DeterministicRandom
    {
        private const ulong Multiplier = 6364136223846793005UL;

        private ulong _state;
        private readonly ulong _increment;

        public DeterministicRandom(ulong seed, ulong stream = 0xDA3E39CB94B95BDBUL)
        {
            unchecked
            {
                _state = 0UL;
                _increment = (stream << 1) | 1UL;
                NextUInt();
                _state += seed;
                NextUInt();
            }
        }

        /// <summary>Uniform 32-bit value.</summary>
        public uint NextUInt()
        {
            unchecked
            {
                ulong oldState = _state;
                _state = oldState * Multiplier + _increment;
                uint xorShifted = (uint)(((oldState >> 18) ^ oldState) >> 27);
                int rotation = (int)(oldState >> 59);
                return (xorShifted >> rotation) | (xorShifted << ((-rotation) & 31));
            }
        }

        /// <summary>Uniform integer in [0, maxExclusive) without modulo bias.</summary>
        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), "Must be > 0.");
            }

            uint bound = (uint)maxExclusive;
            uint threshold = (uint)((0x100000000UL - bound) % bound);
            while (true)
            {
                uint r = NextUInt();
                if (r >= threshold)
                {
                    return (int)(r % bound);
                }
            }
        }

        /// <summary>Uniform integer in [minInclusive, maxExclusive).</summary>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
            {
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), "max must be > min.");
            }
            return minInclusive + NextInt(maxExclusive - minInclusive);
        }

        /// <summary>Returns true with probability permille / 1000.</summary>
        public bool ChancePermille(int permille)
        {
            if (permille <= 0)
            {
                return false;
            }
            if (permille >= 1000)
            {
                return true;
            }
            return NextInt(1000) < permille;
        }

        /// <summary>Picks an index according to integer weights (all weights must be >= 0).</summary>
        public int NextWeightedIndex(IReadOnlyList<int> weights)
        {
            if (weights == null || weights.Count == 0)
            {
                throw new ArgumentException("Weights must not be empty.", nameof(weights));
            }

            long total = 0;
            for (int i = 0; i < weights.Count; i++)
            {
                if (weights[i] < 0)
                {
                    throw new ArgumentException("Weights must be >= 0.", nameof(weights));
                }
                total += weights[i];
            }
            if (total <= 0)
            {
                throw new ArgumentException("At least one weight must be > 0.", nameof(weights));
            }
            if (total > int.MaxValue)
            {
                throw new ArgumentException("Sum of weights overflows.", nameof(weights));
            }

            int roll = NextInt((int)total);
            for (int i = 0; i < weights.Count; i++)
            {
                roll -= weights[i];
                if (roll < 0)
                {
                    return i;
                }
            }
            return weights.Count - 1;
        }

        /// <summary>In-place Fisher-Yates shuffle.</summary>
        public void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = NextInt(i + 1);
                T tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }

        /// <summary>Derives an independent generator (e.g. one stream per board column).</summary>
        public static DeterministicRandom Derive(ulong seed, ulong salt1, ulong salt2 = 0)
        {
            return new DeterministicRandom(StableHash.Mix(seed, salt1, salt2));
        }
    }
}
