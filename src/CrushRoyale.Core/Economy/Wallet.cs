using System;
using System.Collections.Generic;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;

namespace CrushRoyale.Core.Economy
{
    /// <summary>Why currency moved. Persisted and used by monetization analytics: append only.</summary>
    public enum TransactionReason : byte
    {
        StartingGrant = 0,
        StageReward = 1,
        PvpReward = 2,
        SeasonReward = 3,
        AchievementReward = 4,
        QuestReward = 5,
        LoginReward = 6,
        BattlePassReward = 7,
        GuildRankingReward = 8,
        GuildBossReward = 9,
        RealMoneyPurchase = 10,
        ShopPurchase = 11,
        LifePurchase = 12,
        ContinuePurchase = 13,
        GuildDonation = 14,
        GuildCreation = 15,
        ShopRefresh = 16,
        CoinExchange = 17,
        BattlePassPurchase = 18,
        AdminGrant = 19,
        Refund = 20,
        Chargeback = 21,
        RankUpBonus = 22,
        AdReward = 23,
        GhostDefense = 24,
        PetSummon = 25,
        PetAwaken = 26,
        ChapterChest = 27,
        VipGift = 28,
        DailyWheel = 29,
        Restoration = 30
    }

    /// <summary>One immutable line of the currency ledger.</summary>
    public sealed class LedgerEntry
    {
        public string Id { get; set; }

        public long TimestampUnixMs { get; set; }

        public Currency Currency { get; set; }

        /// <summary>Signed amount (credit &gt; 0, debit &lt; 0).</summary>
        public long Amount { get; set; }

        public long BalanceAfter { get; set; }

        public TransactionReason Reason { get; set; }

        public string Reference { get; set; }

        public string IdempotencyKey { get; set; }
    }

    public interface IWallet
    {
        long Coins { get; }

        long Orbes { get; }

        long Balance(Currency currency);

        bool CanAfford(Currency currency, long amount);

        OperationResult Debit(Currency currency, long amount, TransactionReason reason, string reference = null, string idempotencyKey = null);

        OperationResult Credit(Currency currency, long amount, TransactionReason reason, string reference = null, string idempotencyKey = null);
    }

    /// <summary>Serializable wallet state.</summary>
    public sealed class WalletState
    {
        public long Coins { get; set; }

        public long Orbes { get; set; }

        public List<LedgerEntry> Ledger { get; set; } = new List<LedgerEntry>();

        /// <summary>Balances before the first entry still kept in <see cref="Ledger"/> (old entries are archived server-side).</summary>
        public long LedgerOpeningCoins { get; set; }

        public long LedgerOpeningOrbes { get; set; }

        public List<string> ProcessedKeys { get; set; } = new List<string>();

        public long Sequence { get; set; }
    }

    /// <summary>
    /// Double-entry-style wallet: every balance change produces a ledger line with a reason, a reference and an
    /// optional idempotency key (retried network calls never pay twice). Balances can never go negative and the
    /// ledger always reconciles with the balance (see <see cref="Reconcile"/>).
    /// </summary>
    public sealed class Wallet : IWallet
    {
        public const int MaxProcessedKeys = 500;

        private readonly WalletState _state;
        private readonly IClock _clock;
        private readonly int _maxLedgerEntries;
        private readonly HashSet<string> _keys;

        public Wallet(WalletState state, IClock clock, int maxLedgerEntries = 1000)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _maxLedgerEntries = Math.Max(10, maxLedgerEntries);
            _keys = new HashSet<string>(state.ProcessedKeys, StringComparer.Ordinal);
        }

        public event Action<LedgerEntry> Transaction;

        public WalletState State => _state;

        public long Coins => _state.Coins;

        public long Orbes => _state.Orbes;

        public long Balance(Currency currency) => currency == Currency.Coins ? _state.Coins : _state.Orbes;

        public bool CanAfford(Currency currency, long amount) => amount >= 0 && Balance(currency) >= amount;

        public bool HasProcessed(string idempotencyKey) => !string.IsNullOrEmpty(idempotencyKey) && _keys.Contains(idempotencyKey);

        public OperationResult Debit(Currency currency, long amount, TransactionReason reason, string reference = null, string idempotencyKey = null)
        {
            if (amount <= 0)
            {
                return OperationResult.Fail(ErrorCode.InvalidArgument, "Debit amount must be positive.");
            }
            return Apply(currency, -amount, reason, reference, idempotencyKey);
        }

        public OperationResult Credit(Currency currency, long amount, TransactionReason reason, string reference = null, string idempotencyKey = null)
        {
            if (amount <= 0)
            {
                return OperationResult.Fail(ErrorCode.InvalidArgument, "Credit amount must be positive.");
            }
            return Apply(currency, amount, reason, reference, idempotencyKey);
        }

        /// <summary>Checks opening balance + sum(ledger) == balance for both currencies.</summary>
        public bool Reconcile(out string report)
        {
            long coins = _state.LedgerOpeningCoins;
            long orbes = _state.LedgerOpeningOrbes;
            foreach (LedgerEntry e in _state.Ledger)
            {
                if (e.Currency == Currency.Coins)
                {
                    coins += e.Amount;
                    if (coins != e.BalanceAfter)
                    {
                        report = "Coins running balance broken at " + e.Id;
                        return false;
                    }
                }
                else
                {
                    orbes += e.Amount;
                    if (orbes != e.BalanceAfter)
                    {
                        report = "Orbes running balance broken at " + e.Id;
                        return false;
                    }
                }
            }

            bool ok = coins == _state.Coins && orbes == _state.Orbes;
            report = ok ? "OK" : "Ledger coins " + coins + "/" + _state.Coins + ", orbes " + orbes + "/" + _state.Orbes;
            return ok;
        }

        private OperationResult Apply(Currency currency, long signedAmount, TransactionReason reason, string reference, string idempotencyKey)
        {
            if (HasProcessed(idempotencyKey))
            {
                return OperationResult.Fail(ErrorCode.DuplicateRequest, "Already processed.");
            }

            long balance = Balance(currency);
            long after;
            try
            {
                after = checked(balance + signedAmount);
            }
            catch (OverflowException)
            {
                return OperationResult.Fail(ErrorCode.InvalidArgument, "Balance overflow.");
            }
            if (after < 0)
            {
                return OperationResult.Fail(currency == Currency.Coins ? ErrorCode.NotEnoughCoins : ErrorCode.NotEnoughOrbes);
            }

            if (currency == Currency.Coins)
            {
                _state.Coins = after;
            }
            else
            {
                _state.Orbes = after;
            }

            long now = TimeUtil.ToUnixMs(_clock.UtcNow);
            var entry = new LedgerEntry
            {
                Id = "tx_" + now.ToString("x") + "_" + (++_state.Sequence).ToString("x"),
                TimestampUnixMs = now,
                Currency = currency,
                Amount = signedAmount,
                BalanceAfter = after,
                Reason = reason,
                Reference = reference,
                IdempotencyKey = idempotencyKey
            };
            _state.Ledger.Add(entry);
            TrimLedger();

            if (!string.IsNullOrEmpty(idempotencyKey))
            {
                _keys.Add(idempotencyKey);
                _state.ProcessedKeys.Add(idempotencyKey);
                if (_state.ProcessedKeys.Count > MaxProcessedKeys)
                {
                    _keys.Remove(_state.ProcessedKeys[0]);
                    _state.ProcessedKeys.RemoveAt(0);
                }
            }

            Transaction?.Invoke(entry);
            return OperationResult.Ok();
        }

        private void TrimLedger()
        {
            while (_state.Ledger.Count > _maxLedgerEntries)
            {
                LedgerEntry oldest = _state.Ledger[0];
                if (oldest.Currency == Currency.Coins)
                {
                    _state.LedgerOpeningCoins += oldest.Amount;
                }
                else
                {
                    _state.LedgerOpeningOrbes += oldest.Amount;
                }
                _state.Ledger.RemoveAt(0);
            }
        }
    }
}
