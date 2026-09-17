using CrushRoyale.Core.Config;

namespace CrushRoyale.Core.Economy
{
    /// <summary>Content of a daily VIP gift (pet fragments are granted by the caller, they are not a wallet item).</summary>
    public sealed class VipGift
    {
        public RewardData Reward { get; } = new RewardData();

        public int PetFragments { get; internal set; }

        public PetType FragmentsPet { get; internal set; }
    }

    /// <summary>
    /// Daily gift of the high VIP tiers (6 to 10), growing with the tier: orbes, coins, boosts (rare ones from VIP 8),
    /// lives and pet fragments. Claimed once per UTC day; content rotates with the day, identical on client and server.
    /// </summary>
    public static class VipGifts
    {
        public const int FromTier = 6;

        private static readonly PowerUpType[] Common = { PowerUpType.ChronoBomb, PowerUpType.Multiplier2x, PowerUpType.BrightSpark, PowerUpType.GoldenChain, PowerUpType.CoinBooster };
        private static readonly PowerUpType[] Rare = { PowerUpType.NuclearBomb, PowerUpType.FireStorm, PowerUpType.CascadeInfinity };
        private static readonly PetType[] Pets = { PetType.FrostFox, PetType.SunFennec, PetType.ForestOwl, PetType.EmberSalamander, PetType.CrystalDrake };

        public static bool HasGift(int tier) => tier >= FromTier;

        public static bool CanClaim(int tier, int lastClaimDay, int today) => HasGift(tier) && lastClaimDay != today;

        /// <summary>Gift of <paramref name="tier"/> on <paramref name="day"/>; null below VIP 6.</summary>
        public static VipGift For(int tier, int day)
        {
            if (!HasGift(tier))
            {
                return null;
            }
            int step = System.Math.Min(tier, 10) - FromTier; // 0..4
            int[] orbes = { 10, 15, 25, 35, 50 };
            int[] coins = { 1000, 1500, 2500, 4000, 6000 };
            int[] commons = { 1, 2, 1, 2, 2 };
            int[] rares = { 0, 0, 1, 1, 2 };
            int[] lives = { 0, 1, 2, 3, 5 };
            int[] fragments = { 5, 8, 12, 16, 25 };

            var gift = new VipGift();
            gift.Reward.Orbes = orbes[step];
            gift.Reward.Coins = coins[step];
            gift.Reward.Lives = lives[step];
            int seed = day < 0 ? 0 : day;
            for (int i = 0; i < commons[step]; i++)
            {
                gift.Reward.AddPowerUp(Common[(seed + i) % Common.Length], 1);
            }
            for (int i = 0; i < rares[step]; i++)
            {
                gift.Reward.AddPowerUp(Rare[(seed + i) % Rare.Length], 1);
            }
            gift.PetFragments = fragments[step];
            gift.FragmentsPet = Pets[seed % Pets.Length];
            return gift;
        }
    }
}
