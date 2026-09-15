namespace CrushRoyale.Core.Common
{
    /// <summary>
    /// Platform-independent hashing. string.GetHashCode() is randomized per process in .NET Core,
    /// so it must never be used for anything that feeds the simulation or crosses the network.
    /// </summary>
    public static class StableHash
    {
        public const ulong FnvOffset = 14695981039346656037UL;
        public const ulong FnvPrime = 1099511628211UL;

        /// <summary>SplitMix64 finalizer: excellent bit diffusion, used to derive independent seeds.</summary>
        public static ulong SplitMix64(ulong x)
        {
            unchecked
            {
                x += 0x9E3779B97F4A7C15UL;
                x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
                x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
                return x ^ (x >> 31);
            }
        }

        public static ulong Mix(ulong a, ulong b) => SplitMix64(a ^ SplitMix64(b));

        public static ulong Mix(ulong a, ulong b, ulong c) => Mix(Mix(a, b), c);

        /// <summary>FNV-1a over the UTF-16 code units of a string.</summary>
        public static ulong Fnv1a(string value)
        {
            var builder = new HashBuilder();
            if (value != null)
            {
                foreach (char ch in value)
                {
                    builder.Add(ch);
                }
            }
            return builder.Value;
        }
    }

    /// <summary>Incremental FNV-1a hasher for board states, configs and replays.</summary>
    public sealed class HashBuilder
    {
        private ulong _hash = StableHash.FnvOffset;

        public ulong Value => _hash;

        public HashBuilder Add(byte value)
        {
            unchecked
            {
                _hash ^= value;
                _hash *= StableHash.FnvPrime;
            }
            return this;
        }

        public HashBuilder Add(char value)
        {
            Add((byte)value);
            return Add((byte)(value >> 8));
        }

        public HashBuilder Add(int value)
        {
            unchecked
            {
                Add((byte)value);
                Add((byte)(value >> 8));
                Add((byte)(value >> 16));
                return Add((byte)(value >> 24));
            }
        }

        public HashBuilder Add(long value)
        {
            unchecked
            {
                Add((int)value);
                return Add((int)(value >> 32));
            }
        }

        public HashBuilder Add(ulong value) => Add(unchecked((long)value));

        public HashBuilder Add(string value)
        {
            if (value == null)
            {
                return Add(-1);
            }
            Add(value.Length);
            foreach (char ch in value)
            {
                Add(ch);
            }
            return this;
        }
    }
}
