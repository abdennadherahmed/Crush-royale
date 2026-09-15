using System;
using System.Collections.Generic;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Economy;

namespace CrushRoyale.Core.Progression
{
    /// <summary>Serializable lives state (lives_data.json on the client, a JSON column on the server).</summary>
    public sealed class StaminaState
    {
        public int Lives { get; set; } = -1;

        /// <summary>Start of the current recharge period (unix ms).</summary>
        public long RechargeAnchorUnixMs { get; set; }

        public int PurchaseDay { get; set; } = -1;

        public int PurchasesToday { get; set; }

        public int FreeLifeClaimDay { get; set; } = -1;

        public int FreeContinueDay { get; set; } = -1;

        public int FreeContinuesUsedToday { get; set; }
    }

    public sealed class LifePurchaseQuote
    {
        public int Count { get; internal set; }

        public long Coins { get; internal set; }

        public long Orbes { get; internal set; }

        public List<KeyValuePair<Currency, int>> UnitPrices { get; } = new List<KeyValuePair<Currency, int>>();

        public bool BulkDiscountApplied { get; internal set; }
    }

    /// <summary>
    /// Task 10: lives. Start with 2, regenerate one every 30 minutes up to the regen cap.
    /// Purchases per UTC day: the first 2 cost coins (100), the following cost orbes 100, 125, 156, 195...
    /// VIP 2+: 1 free life per day. VIP 4+: faster recharge. VIP 8+: 1 free continue per day.
    /// </summary>
    public sealed class StaminaManager
    {
        private readonly GameBalance _balance;
        private readonly IClock _clock;
        private readonly VipBenefit _vip;
        private readonly int _guildExtraLives;
        private readonly int _guildRechargeReductionPermille;

        public StaminaManager(GameBalance balance, StaminaState state, IClock clock, VipTier vip = VipTier.None, int guildExtraLives = 0, int guildRechargeReductionPermille = 0)
        {
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
            State = state ?? throw new ArgumentNullException(nameof(state));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _vip = new VipSystem(balance).GetBenefit(vip);
            _guildExtraLives = Math.Max(0, guildExtraLives);
            _guildRechargeReductionPermille = Math.Max(0, Math.Min(500, guildRechargeReductionPermille));

            if (State.Lives < 0)
            {
                State.Lives = balance.Stamina.StartingLives;
                State.RechargeAnchorUnixMs = NowMs;
            }
            Refresh();
        }

        public StaminaState State { get; }

        public int MaxRegenLives => _balance.Stamina.MaxRegenLives + _guildExtraLives;

        public long RechargeIntervalMs
        {
            get
            {
                long ms = _balance.Stamina.RechargeMinutes * 60000L;
                if (_vip.RechargeSpeedup)
                {
                    ms = ms * 1000 / _balance.Vip.RechargeSpeedupPermille;
                }
                return ms * (1000 - _guildRechargeReductionPermille) / 1000;
            }
        }

        private long NowMs => TimeUtil.ToUnixMs(_clock.UtcNow);

        private int Today => TimeUtil.DayIndex(_clock.UtcNow);

        /// <summary>Applies regeneration and daily resets.</summary>
        public void Refresh()
        {
            long now = NowMs;
            if (State.Lives >= MaxRegenLives)
            {
                State.RechargeAnchorUnixMs = now;
            }
            else
            {
                long interval = RechargeIntervalMs;
                long elapsed = now - State.RechargeAnchorUnixMs;
                if (elapsed >= interval)
                {
                    long gained = elapsed / interval;
                    long lives = Math.Min(MaxRegenLives, State.Lives + gained);
                    State.RechargeAnchorUnixMs += (lives - State.Lives) * interval;
                    State.Lives = (int)lives;
                    if (State.Lives >= MaxRegenLives)
                    {
                        State.RechargeAnchorUnixMs = now;
                    }
                }
            }

            if (State.PurchaseDay != Today)
            {
                State.PurchaseDay = Today;
                State.PurchasesToday = 0;
            }
            if (State.FreeContinueDay != Today)
            {
                State.FreeContinueDay = Today;
                State.FreeContinuesUsedToday = 0;
            }
        }

        public int Lives
        {
            get
            {
                Refresh();
                return State.Lives;
            }
        }

        public bool HasLives() => Lives > 0;

        /// <summary>Prompt API: spends a life when a story stage starts.</summary>
        public bool ConsumeLive()
        {
            Refresh();
            if (State.Lives <= 0)
            {
                return false;
            }
            if (State.Lives >= MaxRegenLives)
            {
                State.RechargeAnchorUnixMs = NowMs;
            }
            State.Lives--;
            return true;
        }

        /// <summary>Winning gives the life back (standard match-3 rule: only failures cost a life).</summary>
        public void RefundLife()
        {
            Refresh();
            State.Lives++;
        }

        public void AddLives(int count)
        {
            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
            Refresh();
            State.Lives += count;
        }

        /// <summary>Price of the next <paramref name="count"/> lives (escalating, bulk: 5 for the price of 4).</summary>
        public LifePurchaseQuote QuoteLives(int count)
        {
            if (count <= 0 || count > 50)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
            Refresh();

            StaminaBalance s = _balance.Stamina;
            var quote = new LifePurchaseQuote { Count = count };
            for (int i = 0; i < count; i++)
            {
                int index = State.PurchasesToday + i;
                if (index < s.CoinPurchasesPerDay)
                {
                    quote.UnitPrices.Add(new KeyValuePair<Currency, int>(Currency.Coins, s.CoinLifePrice));
                }
                else
                {
                    quote.UnitPrices.Add(new KeyValuePair<Currency, int>(Currency.Orbes, OrbePriceForIndex(index - s.CoinPurchasesPerDay)));
                }
            }

            // Bulk: when buying exactly BulkCount lives, the most expensive units beyond BulkPaidCount are free.
            var paid = new List<KeyValuePair<Currency, int>>(quote.UnitPrices);
            if (count == s.BulkCount && s.BulkPaidCount < s.BulkCount)
            {
                paid.RemoveRange(0, s.BulkCount - s.BulkPaidCount);
                quote.BulkDiscountApplied = true;
            }
            foreach (KeyValuePair<Currency, int> p in paid)
            {
                if (p.Key == Currency.Coins)
                {
                    quote.Coins += p.Value;
                }
                else
                {
                    quote.Orbes += p.Value;
                }
            }
            return quote;
        }

        /// <summary>Orbe price of the n-th orbe-priced life of the day (0-based): base * 1.25^n, rounded down.</summary>
        public int OrbePriceForIndex(int n)
        {
            StaminaBalance s = _balance.Stamina;
            long price = s.OrbeLifeBasePrice;
            for (int i = 0; i < n && price < 1000000; i++)
            {
                price = price * s.OrbeEscalationPermille / 1000;
            }
            return (int)price;
        }

        /// <summary>Prompt API: buys lives immediately, all-or-nothing.</summary>
        public OperationResult BuyLives(int count, IWallet wallet)
        {
            if (wallet == null)
            {
                throw new ArgumentNullException(nameof(wallet));
            }
            if (count <= 0 || count > 50)
            {
                return OperationResult.Fail(ErrorCode.InvalidArgument);
            }

            LifePurchaseQuote quote = QuoteLives(count);
            if (!wallet.CanAfford(Currency.Coins, quote.Coins))
            {
                return OperationResult.Fail(ErrorCode.NotEnoughCoins);
            }
            if (!wallet.CanAfford(Currency.Orbes, quote.Orbes))
            {
                return OperationResult.Fail(ErrorCode.NotEnoughOrbes);
            }

            if (quote.Coins > 0)
            {
                OperationResult r = wallet.Debit(Currency.Coins, quote.Coins, TransactionReason.LifePurchase, "lives:" + count);
                if (!r.Success)
                {
                    return r;
                }
            }
            if (quote.Orbes > 0)
            {
                OperationResult r = wallet.Debit(Currency.Orbes, quote.Orbes, TransactionReason.LifePurchase, "lives:" + count);
                if (!r.Success)
                {
                    if (quote.Coins > 0)
                    {
                        wallet.Credit(Currency.Coins, quote.Coins, TransactionReason.Refund, "lives:" + count);
                    }
                    return r;
                }
            }

            State.Lives += count;
            State.PurchasesToday += count;
            return OperationResult.Ok();
        }

        /// <summary>Prompt API: seconds until the next regenerated life (0 when at the regen cap).</summary>
        public float GetRechargeTimer()
        {
            Refresh();
            if (State.Lives >= MaxRegenLives)
            {
                return 0f;
            }
            long remaining = State.RechargeAnchorUnixMs + RechargeIntervalMs - NowMs;
            return Math.Max(0, remaining) / 1000f;
        }

        /// <summary>Prompt API: free continues left today (VIP 8+).</summary>
        public int GetContinueCount()
        {
            Refresh();
            return _vip.FreeContinueDaily ? Math.Max(0, 1 - State.FreeContinuesUsedToday) : 0;
        }

        public bool UseFreeContinue()
        {
            if (GetContinueCount() <= 0)
            {
                return false;
            }
            State.FreeContinuesUsedToday++;
            return true;
        }

        /// <summary>Orbe price of a paid continue, escalating within one stage attempt.</summary>
        public int GetContinueOrbePrice(int continuesAlreadyBought)
        {
            long price = _balance.Stamina.ContinueBaseOrbes;
            for (int i = 0; i < continuesAlreadyBought; i++)
            {
                price = price * _balance.Stamina.OrbeEscalationPermille / 1000;
            }
            return (int)price;
        }

        public bool CanClaimVipDailyLife()
        {
            Refresh();
            return _vip.FreeLifeDaily && State.FreeLifeClaimDay != Today;
        }

        public bool ClaimVipDailyLife()
        {
            if (!CanClaimVipDailyLife())
            {
                return false;
            }
            State.FreeLifeClaimDay = Today;
            State.Lives++;
            return true;
        }
    }
}
