using System;
using System.Collections.Generic;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;

namespace CrushRoyale.Core.Economy
{
    public enum WheelPrize : byte
    {
        Coins = 0,
        Orbes = 1,
        Boost = 2,
        Lives = 3,
        PetFragments = 4,
        Jackpot = 5
    }

    /// <summary>One slice of the daily wheel (weights are shown to players as odds).</summary>
    public sealed class WheelSlice
    {
        public WheelPrize Prize { get; internal set; }

        public long Amount { get; internal set; }

        /// <summary>Relative weight out of <see cref="DailyWheel.TotalWeight"/>.</summary>
        public int Weight { get; internal set; }
    }

    public sealed class WheelSpin
    {
        public int SliceIndex { get; internal set; }

        public WheelSlice Slice { get; internal set; }

        public RewardData Reward { get; } = new RewardData();

        public int PetFragments { get; internal set; }

        public PetType FragmentsPet { get; internal set; }
    }

    /// <summary>
    /// Free daily wheel: one spin per UTC day, eight slices from small coins to a rare jackpot. The server rolls it
    /// (seeded by player and day, so a retry lands on the same slice); odds are public in <see cref="Slices"/>.
    /// </summary>
    public static class DailyWheel
    {
        public static readonly IReadOnlyList<WheelSlice> Slices = new List<WheelSlice>
        {
            new WheelSlice { Prize = WheelPrize.Coins, Amount = 300, Weight = 24 },
            new WheelSlice { Prize = WheelPrize.Boost, Amount = 1, Weight = 16 },
            new WheelSlice { Prize = WheelPrize.Orbes, Amount = 5, Weight = 14 },
            new WheelSlice { Prize = WheelPrize.Lives, Amount = 2, Weight = 12 },
            new WheelSlice { Prize = WheelPrize.Coins, Amount = 1000, Weight = 12 },
            new WheelSlice { Prize = WheelPrize.PetFragments, Amount = 5, Weight = 10 },
            new WheelSlice { Prize = WheelPrize.Orbes, Amount = 25, Weight = 9 },
            new WheelSlice { Prize = WheelPrize.Jackpot, Amount = 100, Weight = 3 }
        };

        private static readonly PowerUpType[] Boosts = { PowerUpType.ChronoBomb, PowerUpType.BrightSpark, PowerUpType.Multiplier2x, PowerUpType.GoldenChain };

        private static readonly PetType[] Pets = { PetType.FrostFox, PetType.SunFennec, PetType.ForestOwl, PetType.EmberSalamander, PetType.CrystalDrake };

        public static int TotalWeight
        {
            get
            {
                int total = 0;
                foreach (WheelSlice slice in Slices)
                {
                    total += slice.Weight;
                }
                return total;
            }
        }

        public static bool CanSpin(int lastSpinDay, int today) => lastSpinDay != today;

        /// <summary>Rolls the spin of <paramref name="playerId"/> on <paramref name="day"/> (deterministic).</summary>
        public static WheelSpin Spin(string playerId, int day)
        {
            var rng = DeterministicRandom.Derive(StableHash.Fnv1a(playerId ?? string.Empty), 0x3EE15917UL, (ulong)Math.Max(0, day));
            int roll = rng.NextInt(TotalWeight);
            int index = 0;
            for (; index < Slices.Count - 1; index++)
            {
                roll -= Slices[index].Weight;
                if (roll < 0)
                {
                    break;
                }
            }

            WheelSlice slice = Slices[index];
            var spin = new WheelSpin { SliceIndex = index, Slice = slice };
            switch (slice.Prize)
            {
                case WheelPrize.Coins:
                    spin.Reward.Coins = slice.Amount;
                    break;
                case WheelPrize.Orbes:
                    spin.Reward.Orbes = slice.Amount;
                    break;
                case WheelPrize.Boost:
                    spin.Reward.AddPowerUp(Boosts[rng.NextInt(Boosts.Length)], (int)slice.Amount);
                    break;
                case WheelPrize.Lives:
                    spin.Reward.Lives = (int)slice.Amount;
                    break;
                case WheelPrize.PetFragments:
                    spin.PetFragments = (int)slice.Amount;
                    spin.FragmentsPet = Pets[rng.NextInt(Pets.Length)];
                    break;
                case WheelPrize.Jackpot:
                    spin.Reward.Orbes = slice.Amount;
                    spin.Reward.Coins = 3000;
                    spin.Reward.AddPowerUp(PowerUpType.NuclearBomb, 1);
                    break;
            }
            return spin;
        }
    }
}
