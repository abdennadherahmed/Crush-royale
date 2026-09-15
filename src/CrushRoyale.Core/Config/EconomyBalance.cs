using System.Collections.Generic;

namespace CrushRoyale.Core.Config
{
    /// <summary>A real-money Orbe pack. Prices are integer euro cents (never floats for money).</summary>
    public sealed class OrbePackDefinition
    {
        public int Level { get; set; }

        /// <summary>Google Play product id.</summary>
        public string Sku { get; set; }

        public int BaseOrbes { get; set; }

        public int BonusOrbes { get; set; }

        public int PriceCents { get; set; }

        public int TotalOrbes => BaseOrbes + BonusOrbes;
    }

    /// <summary>Orbes -> coins exchange offer.</summary>
    public sealed class CoinPackDefinition
    {
        public string Id { get; set; }

        public int PriceOrbes { get; set; }

        public int Coins { get; set; }
    }

    public sealed class EconomyBalance
    {
        public int StartingCoins { get; set; } = 500;

        public int StartingOrbes { get; set; } = 20;

        /// <summary>
        /// Orbe packs from the GDD. Fix: pack 3 bonus raised from 35 to 70, otherwise the 12.99€ pack gives
        /// FEWER orbes per euro (77.0) than the 4.99€ pack (79.2). Value per euro now strictly increases.
        /// </summary>
        public List<OrbePackDefinition> OrbePacks { get; set; } = new List<OrbePackDefinition>
        {
            new OrbePackDefinition { Level = 1, Sku = "crushroyale.orbes.pack1", BaseOrbes = 74, BonusOrbes = 0, PriceCents = 99 },
            new OrbePackDefinition { Level = 2, Sku = "crushroyale.orbes.pack2", BaseOrbes = 385, BonusOrbes = 10, PriceCents = 499 },
            new OrbePackDefinition { Level = 3, Sku = "crushroyale.orbes.pack3", BaseOrbes = 965, BonusOrbes = 70, PriceCents = 1299 },
            new OrbePackDefinition { Level = 4, Sku = "crushroyale.orbes.pack4", BaseOrbes = 2100, BonusOrbes = 100, PriceCents = 2499 },
            new OrbePackDefinition { Level = 5, Sku = "crushroyale.orbes.pack5", BaseOrbes = 5250, BonusOrbes = 250, PriceCents = 4999 },
            new OrbePackDefinition { Level = 6, Sku = "crushroyale.orbes.pack6", BaseOrbes = 11000, BonusOrbes = 1000, PriceCents = 9999 },
            new OrbePackDefinition { Level = 7, Sku = "crushroyale.orbes.mega", BaseOrbes = 27500, BonusOrbes = 2500, PriceCents = 19999 }
        };

        /// <summary>GDD "Buy same pack 3x = +20% bonus orbes": every 3rd purchase of the same pack.</summary>
        public int RepeatPurchaseEvery { get; set; } = 3;

        public int RepeatPurchaseBonusPermille { get; set; } = 200;

        public List<CoinPackDefinition> CoinPacks { get; set; } = new List<CoinPackDefinition>
        {
            new CoinPackDefinition { Id = "coins.small", PriceOrbes = 100, Coins = 1200 },
            new CoinPackDefinition { Id = "coins.medium", PriceOrbes = 500, Coins = 6500 },
            new CoinPackDefinition { Id = "coins.large", PriceOrbes = 1200, Coins = 16500 }
        };

        public string RemoveAdsSku { get; set; } = "crushroyale.removeads";

        public int RemoveAdsPriceCents { get; set; } = 499;

        public string BattlePassSku { get; set; } = "crushroyale.battlepass";

        public int BattlePassPriceCents { get; set; } = 999;

        public int BattlePassPriceOrbes { get; set; } = 950;

        public string RarePerkSku { get; set; } = "crushroyale.crown";

        public int RarePerkPriceCents { get; set; } = 1999;

        /// <summary>Rare perk "Crown of Crystalheim": unlocked after this much play time (GDD ~1 month x 2h/day).</summary>
        public int RarePerkPlaytimeHours { get; set; } = 60;

        /// <summary>Crown effect: coin bonus only, never score (not pay-to-win).</summary>
        public int RarePerkCoinBonusPermille { get; set; } = 100;

        public int ShopDailyOffers { get; set; } = 6;

        public int ShopRefreshOrbes { get; set; } = 50;

        /// <summary>Weighted tier pick for daily offers, indexed Common/Rare/Epic.</summary>
        public int[] ShopTierWeights { get; set; } = { 60, 30, 10 };

        /// <summary>One daily offer is a "deal" with this discount.</summary>
        public int ShopDealDiscountPermille { get; set; } = 200;

        public int ShopBundleSize { get; set; } = 3;

        /// <summary>Sum of all additive coin bonuses (VIP + collection book + guild + crown) is capped.</summary>
        public int MaxTotalCoinBonusPermille { get; set; } = 1500;

        /// <summary>Consumer protection: declared age under this cannot buy.</summary>
        public int MinPurchaseAge { get; set; } = 13;

        /// <summary>Consumer protection: 13-17 year-olds have a monthly spending cap (cents).</summary>
        public int MinorMonthlyCapCents { get; set; } = 5000;

        public int AdultAge { get; set; } = 18;

        internal void Validate()
        {
            GameBalance.Require(OrbePacks.Count > 0, "Economy.OrbePacks");
            long previousValue = 0;
            OrbePackDefinition previous = null;
            foreach (OrbePackDefinition pack in OrbePacks)
            {
                GameBalance.Require(pack.PriceCents > 0 && pack.BaseOrbes > 0 && pack.BonusOrbes >= 0, "Economy pack " + pack.Sku);
                // Orbes per cent compared without division: a/b > c/d <=> a*d > c*b.
                if (previous != null)
                {
                    long lhs = (long)pack.TotalOrbes * previous.PriceCents;
                    long rhs = (long)previous.TotalOrbes * pack.PriceCents;
                    GameBalance.Require(lhs > rhs, "Economy pack " + pack.Sku + " must give more orbes per euro than " + previous.Sku);
                }
                previous = pack;
                previousValue = pack.TotalOrbes;
            }
            GameBalance.Require(previousValue > 0, "Economy.OrbePacks value");
            GameBalance.Require(ShopTierWeights.Length == 3, "Economy.ShopTierWeights");
            GameBalance.Require(RepeatPurchaseEvery >= 2, "Economy.RepeatPurchaseEvery");
        }
    }

    /// <summary>VIP tiers (Task 13). Spending thresholds in cents.</summary>
    public sealed class VipBalance
    {
        public int[] ThresholdsCents { get; set; } = { 150, 500, 2000, 5000, 10000, 50000, 100000, 200000, 500000, 1000000 };

        public int[] CoinBonusPermille { get; set; } = { 150, 150, 200, 250, 300, 350, 400, 450, 500, 600 };

        public int[] OrbeBonusPermille { get; set; } = { 0, 0, 0, 0, 0, 0, 50, 100, 150, 200 };

        public int FreeLifePerDayFromLevel { get; set; } = 2;

        public int BasicCosmeticsFromLevel { get; set; } = 3;

        /// <summary>
        /// GDD "speedup x1.2". Applying it to gameplay would be pay-to-win in PvP, so it speeds up life recharge instead.
        /// </summary>
        public int RechargeSpeedupFromLevel { get; set; } = 4;

        public int RechargeSpeedupPermille { get; set; } = 1200;

        public int RareCosmeticsFromLevel { get; set; } = 5;

        public int DoubleEventRewardsFromLevel { get; set; } = 6;

        public int SkipAdPerDayFromLevel { get; set; } = 7;

        public int FreeContinuePerDayFromLevel { get; set; } = 8;

        /// <summary>"Steal power-up" chance. Restricted to friendly challenges (no trophies at stake).</summary>
        public int StealPowerUpFromLevel { get; set; } = 9;

        public int[] StealChancePermille { get; set; } = { 0, 0, 0, 0, 0, 0, 0, 0, 600, 750 };

        public int RarePerkLevel { get; set; } = 10;

        public int MaxLevel => ThresholdsCents.Length;

        internal void Validate()
        {
            GameBalance.Require(ThresholdsCents.Length == 10, "Vip.ThresholdsCents needs 10 values");
            GameBalance.Require(CoinBonusPermille.Length == 10 && OrbeBonusPermille.Length == 10 && StealChancePermille.Length == 10, "Vip tables need 10 values");
            for (int i = 1; i < ThresholdsCents.Length; i++)
            {
                GameBalance.Require(ThresholdsCents[i] > ThresholdsCents[i - 1], "Vip.ThresholdsCents must increase");
            }
        }
    }

    /// <summary>Guilds (Task 11).</summary>
    public sealed class GuildBalance
    {
        public int MaxMembers { get; set; } = 20;

        public int MaxLevel { get; set; } = 20;

        public int CreateCostCoins { get; set; } = 100;

        public int MinNameLength { get; set; } = 3;

        public int MaxNameLength { get; set; } = 20;

        public int MaxOfficers { get; set; } = 4;

        /// <summary>Level 1 -> 2 is paid in coins.</summary>
        public int Level2CostCoins { get; set; } = 500;

        /// <summary>Level 2 -> 3 costs this many orbes, then +25% per level.</summary>
        public int OrbeLevelBaseCost { get; set; } = 100;

        public int OrbeLevelEscalationPermille { get; set; } = 1250;

        /// <summary>Tech points granted per level-up.</summary>
        public int TechPointsPerLevel { get; set; } = 1;

        public int BossBaseHp { get; set; } = 60000;

        public int BossHpGrowthPermille { get; set; } = 1350;

        public int BossAttacksPerMemberPerWeek { get; set; } = 3;

        public int BossBaseRewardCoins { get; set; } = 500;

        public int BossRewardCoinsGrowthPermille { get; set; } = 250;

        public int BossBaseRewardOrbes { get; set; } = 5;

        /// <summary>Weekly guild ranking reward pools (rank 1, 2-3, 4-10, 11-50, 51-100), split evenly among members.</summary>
        public int[] RankingPoolOrbes { get; set; } = { 600, 400, 250, 120, 60 };

        public int[] RankingPoolCoins { get; set; } = { 30000, 20000, 12000, 6000, 3000 };

        public int ChatMaxLength { get; set; } = 200;

        public int ChatMinIntervalMs { get; set; } = 2000;

        internal void Validate()
        {
            GameBalance.Require(MaxMembers >= 2 && MaxMembers <= 100, "Guild.MaxMembers");
            GameBalance.Require(MaxLevel >= 2, "Guild.MaxLevel");
            GameBalance.Require(OrbeLevelEscalationPermille >= 1000, "Guild.OrbeLevelEscalationPermille");
            GameBalance.Require(RankingPoolOrbes.Length == 5 && RankingPoolCoins.Length == 5, "Guild ranking pools need 5 values");
        }
    }

    public sealed class SocialBalance
    {
        public int MaxFriends { get; set; } = 100;

        /// <summary>GDD "Cooldown: 1 challenge per 5 minutes (anti-spam)".</summary>
        public int ChallengeCooldownMs { get; set; } = 300000;

        public int PendingRequestsMax { get; set; } = 50;
    }

    /// <summary>Retention systems the GDD implies but does not specify: battle pass, quests, login calendar, ads.</summary>
    public sealed class LiveOpsBalance
    {
        public int BattlePassSeasonDays { get; set; } = 28;

        public int BattlePassTiers { get; set; } = 50;

        public int BattlePassXpPerTier { get; set; } = 1000;

        public int XpStoryWin { get; set; } = 100;

        public int XpStoryLoss { get; set; } = 30;

        public int XpPvpWin { get; set; } = 150;

        public int XpPvpLoss { get; set; } = 50;

        public int XpGuildBossAttack { get; set; } = 120;

        public int XpQuest { get; set; } = 250;

        public int DailyQuestCount { get; set; } = 3;

        public int QuestRewardCoins { get; set; } = 150;

        /// <summary>7-day login calendar rewards: positive = coins, negative = orbes (abs value).</summary>
        public int[] LoginCalendar { get; set; } = { 100, 150, -5, 200, 250, 300, -15 };

        public int StoryInterstitialEveryWins { get; set; } = 2;

        public int PvpInterstitialEveryBattles { get; set; } = 3;

        public int InterstitialMinIntervalSeconds { get; set; } = 120;

        public int InterstitialDailyCap { get; set; } = 12;

        public int RewardedAdDailyCap { get; set; } = 5;
    }
}
