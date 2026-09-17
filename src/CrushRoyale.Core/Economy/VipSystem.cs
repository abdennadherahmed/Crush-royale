using System;
using System.Collections.Generic;
using CrushRoyale.Core.Config;

namespace CrushRoyale.Core.Economy
{
    public enum VipTier : byte
    {
        None = 0,
        Vip1 = 1,
        Vip2 = 2,
        Vip3 = 3,
        Vip4 = 4,
        Vip5 = 5,
        Vip6 = 6,
        Vip7 = 7,
        Vip8 = 8,
        Vip9 = 9,
        Vip10 = 10
    }

    /// <summary>Everything a VIP tier grants.</summary>
    public sealed class VipBenefit
    {
        public VipTier Tier { get; internal set; }

        public long ThresholdCents { get; internal set; }

        public int CoinBonusPermille { get; internal set; }

        public int OrbeBonusPermille { get; internal set; }

        public bool FreeLifeDaily { get; internal set; }

        public bool BasicCosmetics { get; internal set; }

        public bool RechargeSpeedup { get; internal set; }

        public bool RareCosmetics { get; internal set; }

        public bool DoubleEventRewards { get; internal set; }

        public bool SkipAdDaily { get; internal set; }

        public bool FreeContinueDaily { get; internal set; }

        /// <summary>Friendly challenges only.</summary>
        public int StealChancePermille { get; internal set; }

        public bool RarePerk { get; internal set; }
    }

    public sealed class VipProgress
    {
        public VipTier Tier { get; internal set; }

        public long LifetimeSpendCents { get; internal set; }

        public long CurrentThresholdCents { get; internal set; }

        /// <summary>Null at VIP 10.</summary>
        public long? NextThresholdCents { get; internal set; }

        /// <summary>0-1 progress toward the next tier (1 at max tier).</summary>
        public float ProgressToNext { get; internal set; }
    }

    public sealed class VipStatus
    {
        public string PlayerId { get; set; }

        public long LifetimeSpendCents { get; set; }

        public VipTier Tier { get; set; }
    }

    public sealed class VipUpgrade
    {
        public VipTier Before { get; internal set; }

        public VipTier After { get; internal set; }

        public List<VipTier> ReachedTiers { get; } = new List<VipTier>();

        public List<string> CosmeticsGranted { get; } = new List<string>();

        public bool RarePerkGranted { get; internal set; }
    }

    /// <summary>
    /// Task 13. VIP = cumulative real-money spend. Benefits are comfort / cosmetics / economy only; the GDD's
    /// gameplay-affecting items were redirected (speed-up -> life recharge, steal power-up -> friendly challenges,
    /// rare power-up -> coin perk) so ranked PvP stays fair.
    /// </summary>
    public sealed class VipSystem
    {
        private readonly VipBalance _vip;
        private readonly Func<string, long> _spendLookup;

        /// <param name="balance">Game balance.</param>
        /// <param name="spendLookup">Returns lifetime spend in cents for a player id (server: database).</param>
        public VipSystem(GameBalance balance, Func<string, long> spendLookup = null)
        {
            if (balance == null)
            {
                throw new ArgumentNullException(nameof(balance));
            }
            _vip = balance.Vip;
            _spendLookup = spendLookup;
        }

        public VipTier GetTierForSpend(long lifetimeSpendCents)
        {
            VipTier tier = VipTier.None;
            for (int i = 0; i < _vip.ThresholdsCents.Length; i++)
            {
                if (lifetimeSpendCents >= _vip.ThresholdsCents[i])
                {
                    tier = (VipTier)(i + 1);
                }
            }
            return tier;
        }

        /// <summary>Prompt API.</summary>
        public VipTier GetPlayerVipTier(string playerId)
        {
            if (_spendLookup == null)
            {
                throw new InvalidOperationException("No spend lookup configured.");
            }
            return GetTierForSpend(_spendLookup(playerId));
        }

        public VipBenefit GetBenefit(VipTier tier)
        {
            int level = (int)tier;
            var benefit = new VipBenefit { Tier = tier };
            if (level == 0)
            {
                return benefit;
            }

            int i = level - 1;
            benefit.ThresholdCents = _vip.ThresholdsCents[i];
            benefit.CoinBonusPermille = _vip.CoinBonusPermille[i];
            benefit.OrbeBonusPermille = _vip.OrbeBonusPermille[i];
            benefit.FreeLifeDaily = level >= _vip.FreeLifePerDayFromLevel;
            benefit.BasicCosmetics = level >= _vip.BasicCosmeticsFromLevel;
            benefit.RechargeSpeedup = level >= _vip.RechargeSpeedupFromLevel;
            benefit.RareCosmetics = level >= _vip.RareCosmeticsFromLevel;
            benefit.DoubleEventRewards = level >= _vip.DoubleEventRewardsFromLevel;
            benefit.SkipAdDaily = level >= _vip.SkipAdPerDayFromLevel;
            benefit.FreeContinueDaily = level >= _vip.FreeContinuePerDayFromLevel;
            benefit.StealChancePermille = _vip.StealChancePermille[i];
            benefit.RarePerk = level >= _vip.RarePerkLevel;
            return benefit;
        }

        /// <summary>Prompt API: coins after the VIP percentage (display helper; the economy uses RewardCalculator).</summary>
        public float ApplyVipBonuses(float baseCoins, VipTier tier) => baseCoins * (1000 + GetBenefit(tier).CoinBonusPermille) / 1000f;

        public long ApplyVipCoinBonus(long baseCoins, VipTier tier) => baseCoins * (1000 + GetBenefit(tier).CoinBonusPermille) / 1000;

        /// <summary>Prompt API: VIP 9-10 only, and never where trophies are at stake.</summary>
        public bool CanStealPowerUp(VipTier tier, GameMode mode) => mode == GameMode.FriendlyChallenge && GetBenefit(tier).StealChancePermille > 0;

        public float GetOrbeBonusMultiplier(VipTier tier) => (1000 + GetBenefit(tier).OrbeBonusPermille) / 1000f;

        public VipProgress GetProgress(long lifetimeSpendCents)
        {
            VipTier tier = GetTierForSpend(lifetimeSpendCents);
            int level = (int)tier;
            long current = level == 0 ? 0 : _vip.ThresholdsCents[level - 1];
            long? next = level < _vip.ThresholdsCents.Length ? _vip.ThresholdsCents[level] : (long?)null;
            float progress = next.HasValue ? (float)(lifetimeSpendCents - current) / (next.Value - current) : 1f;
            return new VipProgress
            {
                Tier = tier,
                LifetimeSpendCents = lifetimeSpendCents,
                CurrentThresholdCents = current,
                NextThresholdCents = next,
                ProgressToNext = Math.Max(0f, Math.Min(1f, progress))
            };
        }

        /// <summary>Adds a verified real-money purchase and reports newly reached tiers and their automatic grants.</summary>
        public VipUpgrade RecordPurchase(VipStatus status, long cents)
        {
            if (status == null)
            {
                throw new ArgumentNullException(nameof(status));
            }
            if (cents <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cents));
            }

            var upgrade = new VipUpgrade { Before = status.Tier };
            status.LifetimeSpendCents += cents;
            status.Tier = GetTierForSpend(status.LifetimeSpendCents);
            upgrade.After = status.Tier;

            for (int level = (int)upgrade.Before + 1; level <= (int)upgrade.After; level++)
            {
                upgrade.ReachedTiers.Add((VipTier)level);
                upgrade.CosmeticsGranted.AddRange(CosmeticCatalog.VipRewards(level));
                if (level == _vip.RarePerkLevel)
                {
                    upgrade.RarePerkGranted = true;
                }
            }
            return upgrade;
        }

        /// <summary>Refunds/chargebacks lower lifetime spend (tiers can drop; granted cosmetics are kept).</summary>
        public void RecordRefund(VipStatus status, long cents)
        {
            if (status == null)
            {
                throw new ArgumentNullException(nameof(status));
            }
            status.LifetimeSpendCents = Math.Max(0, status.LifetimeSpendCents - Math.Max(0, cents));
            status.Tier = GetTierForSpend(status.LifetimeSpendCents);
        }
    }

    /// <summary>Inputs of the coin/orbe bonus stack.</summary>
    public sealed class BonusContext
    {
        public VipTier Vip { get; set; }

        public int CollectionPagesCompleted { get; set; }

        public int GuildCoinBonusPermille { get; set; }

        public bool RarePerk { get; set; }

        public bool EventActive { get; set; }

        /// <summary>Stacked cosmetic collection bonus (see <see cref="CosmeticBonuses"/>).</summary>
        public int CosmeticCoinBonusPermille { get; set; }
    }

    /// <summary>Applies additive coin bonuses (VIP + collection book + guild + crown), capped by the economy balance.</summary>
    public static class RewardCalculator
    {
        public const int CollectionPageBonusPermille = 50;

        public static int TotalCoinBonusPermille(GameBalance balance, BonusContext context)
        {
            if (balance == null)
            {
                throw new ArgumentNullException(nameof(balance));
            }
            if (context == null)
            {
                return 0;
            }

            var vip = new VipSystem(balance).GetBenefit(context.Vip);
            long total = vip.CoinBonusPermille
                + (long)Math.Max(0, context.CollectionPagesCompleted) * CollectionPageBonusPermille
                + Math.Max(0, context.GuildCoinBonusPermille)
                + (context.RarePerk ? balance.Economy.RarePerkCoinBonusPermille : 0)
                + Math.Max(0, Math.Min(CosmeticBonuses.MaxCoinPermille, context.CosmeticCoinBonusPermille));
            return (int)Math.Min(balance.Economy.MaxTotalCoinBonusPermille, total);
        }

        public static long ApplyCoinBonus(GameBalance balance, long baseCoins, BonusContext context, bool isEventReward = false)
        {
            if (baseCoins <= 0)
            {
                return 0;
            }
            long coins = baseCoins * (1000 + TotalCoinBonusPermille(balance, context)) / 1000;
            if (isEventReward && context != null && context.EventActive && new VipSystem(balance).GetBenefit(context.Vip).DoubleEventRewards)
            {
                coins *= 2;
            }
            return coins;
        }

        /// <summary>VIP 7+ "orbes gains": applies to earned orbes (rewards), never to purchased packs.</summary>
        public static long ApplyOrbeBonus(GameBalance balance, long earnedOrbes, VipTier tier)
        {
            if (earnedOrbes <= 0)
            {
                return 0;
            }
            return earnedOrbes * (1000 + new VipSystem(balance).GetBenefit(tier).OrbeBonusPermille) / 1000;
        }
    }
}
