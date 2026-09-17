using System;
using System.Collections.Generic;

namespace CrushRoyale.Core.Economy
{
    /// <summary>
    /// Every cosmetic owned adds a small stackable bonus to coins and battle pass XP, by rarity (default items excluded).
    /// Economy only, never gameplay: ranked PvP stays fair.
    /// </summary>
    public static class CosmeticBonuses
    {
        public const int MaxCoinPermille = 300;

        public const int MaxPassXpPermille = 200;

        public static int CoinPermille(CosmeticRarity rarity)
        {
            switch (rarity)
            {
                case CosmeticRarity.Rare: return 10;
                case CosmeticRarity.Epic: return 20;
                case CosmeticRarity.Legendary: return 40;
                default: return 5;
            }
        }

        public static int PassXpPermille(CosmeticRarity rarity)
        {
            switch (rarity)
            {
                case CosmeticRarity.Rare: return 5;
                case CosmeticRarity.Epic: return 10;
                case CosmeticRarity.Legendary: return 20;
                default: return 0;
            }
        }

        /// <summary>Stacked (capped) bonuses of a collection, in permille, and how many cosmetics count.</summary>
        public static (int Coins, int PassXp, int Counted) Total(IEnumerable<string> owned)
        {
            int coins = 0;
            int xp = 0;
            int counted = 0;
            if (owned == null)
            {
                return (0, 0, 0);
            }
            foreach (string id in owned)
            {
                CosmeticDefinition def = CosmeticCatalog.Get(id);
                if (def == null || def.Source == CosmeticSource.Default)
                {
                    continue;
                }
                counted++;
                coins += CoinPermille(def.Rarity);
                xp += PassXpPermille(def.Rarity);
            }
            return (Math.Min(MaxCoinPermille, coins), Math.Min(MaxPassXpPermille, xp), counted);
        }
    }
}
