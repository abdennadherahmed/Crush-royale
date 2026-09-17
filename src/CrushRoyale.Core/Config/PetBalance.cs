using System.Collections.Generic;
using CrushRoyale.Core.Common;

namespace CrushRoyale.Core.Config
{
    /// <summary>A companion pet: its kingdom and the power-up it offers once per match at max level.</summary>
    public sealed class PetDefinition
    {
        public PetType Type { get; set; }

        /// <summary>Kingdom index (0 Frostreach, 1 Sunspire, 2 Verdant Wilds, 3 Emberfall, 4 Crystalheim).</summary>
        public int Kingdom { get; set; }

        public PowerUpType PowerUp { get; set; }
    }

    /// <summary>
    /// Pets: summoned with orbes (1 in 120 for the whole pet, fragments otherwise, pity at 120 pulls) or unlocked with
    /// 100 of their fragments, levelled with
    /// match XP plus fragments and coins at the awakening gates. In a match the equipped pet has Level% chance after
    /// each player move to play the best move for free (not counted); at max level it also offers its power-up once.
    /// </summary>
    public sealed class PetBalance
    {
        public int MaxLevel { get; set; } = 10;

        /// <summary>Chance of a free automatic move per player move, per level (10 = 1% per level).</summary>
        public int AutoMovePermillePerLevel { get; set; } = 10;

        /// <summary>PvP stays skill-first: pets play at most at this level in duels.</summary>
        public int PvpLevelCap { get; set; } = 3;

        /// <summary>Level from which the pet offers its power-up (once per match).</summary>
        public int PowerUpLevel { get; set; } = 10;

        public int SummonCostOrbes { get; set; } = 30;

        public int Summon10CostOrbes { get; set; } = 270;

        /// <summary>Each pull: 1 chance in this number to get a whole pet.</summary>
        public int WholePetOneIn { get; set; } = 120;

        /// <summary>A whole pet is guaranteed at this many pulls without one.</summary>
        public int PityPulls { get; set; } = 120;

        public int FragmentsMin { get; set; } = 1;

        public int FragmentsMax { get; set; } = 5;

        /// <summary>A whole pet you already own turns into this many of its fragments.</summary>
        public int DuplicateFragments { get; set; } = 50;

        /// <summary>Collecting this many fragments of a pet you do not own lets you unlock it without luck.</summary>
        public int UnlockFragments { get; set; } = 100;

        /// <summary>Fragments of an owned pet traded for one fragment of a pet not owned yet.</summary>
        public int ConvertRatio { get; set; } = 3;

        public int StoryWinXp { get; set; } = 30;

        public int StoryLossXp { get; set; } = 10;

        public int PvpWinXp { get; set; } = 25;

        public int PvpLossXp { get; set; } = 10;

        public int GuildBossXp { get; set; } = 20;

        /// <summary>Total XP needed to reach level index+1 (index 0 = level 1).</summary>
        public int[] XpForLevel { get; set; } = { 0, 100, 250, 450, 700, 1000, 1400, 1900, 2500, 3200 };

        /// <summary>Levels that need an awakening (fragments of that pet + coins) on top of the XP.</summary>
        public int[] GateLevels { get; set; } = { 3, 6, 9 };

        public int[] GateFragments { get; set; } = { 30, 80, 150 };

        public int[] GateCoins { get; set; } = { 1000, 5000, 15000 };

        public List<PetDefinition> Pets { get; set; } = new List<PetDefinition>
        {
            new PetDefinition { Type = PetType.FrostFox, Kingdom = 0, PowerUp = PowerUpType.ChronoBomb },
            new PetDefinition { Type = PetType.SunFennec, Kingdom = 1, PowerUp = PowerUpType.CoinBooster },
            new PetDefinition { Type = PetType.ForestOwl, Kingdom = 2, PowerUp = PowerUpType.GoldenChain },
            new PetDefinition { Type = PetType.EmberSalamander, Kingdom = 3, PowerUp = PowerUpType.FireStorm },
            new PetDefinition { Type = PetType.CrystalDrake, Kingdom = 4, PowerUp = PowerUpType.NuclearBomb }
        };

        public PetDefinition Get(PetType type)
        {
            foreach (PetDefinition def in Pets)
            {
                if (def.Type == type)
                {
                    return def;
                }
            }
            return null;
        }

        /// <summary>Gate index for a level, or -1 when the level needs XP only.</summary>
        public int GateIndex(int level)
        {
            for (int i = 0; i < GateLevels.Length; i++)
            {
                if (GateLevels[i] == level)
                {
                    return i;
                }
            }
            return -1;
        }

        internal void Validate()
        {
            GameBalance.Require(MaxLevel >= 1 && XpForLevel != null && XpForLevel.Length >= MaxLevel, "Pet levels");
            GameBalance.Require(AutoMovePermillePerLevel >= 0 && AutoMovePermillePerLevel * MaxLevel <= 500, "Pet auto move chance");
            GameBalance.Require(PvpLevelCap >= 0 && PvpLevelCap <= MaxLevel, "Pet PvP cap");
            GameBalance.Require(WholePetOneIn >= 1 && PityPulls >= 1, "Pet summon odds");
            GameBalance.Require(FragmentsMin >= 1 && FragmentsMax >= FragmentsMin, "Pet fragments");
            GameBalance.Require(UnlockFragments >= 1, "Pet unlock fragments");
            GameBalance.Require(GateLevels != null && GateFragments != null && GateCoins != null
                && GateLevels.Length == GateFragments.Length && GateLevels.Length == GateCoins.Length, "Pet gates");
            for (int i = 1; i < XpForLevel.Length; i++)
            {
                GameBalance.Require(XpForLevel[i] >= XpForLevel[i - 1], "Pet XP table must increase");
            }
            GameBalance.Require(Pets != null && Pets.Count > 0, "Pet list");
        }

        internal void AddToHash(HashBuilder h)
        {
            h.Add(MaxLevel).Add(AutoMovePermillePerLevel).Add(PvpLevelCap).Add(PowerUpLevel);
            foreach (PetDefinition def in Pets)
            {
                h.Add((int)def.Type).Add((int)def.PowerUp);
            }
        }
    }
}
