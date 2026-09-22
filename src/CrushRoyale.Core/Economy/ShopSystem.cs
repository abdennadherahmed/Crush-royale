using System;
using System.Collections.Generic;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.PowerUps;
using CrushRoyale.Core.Progression;

namespace CrushRoyale.Core.Economy
{
    public enum PaymentMethod : byte
    {
        Coins = 0,
        Orbes = 1,
        RealMoney = 2
    }

    public enum ShopItemKind : byte
    {
        PowerUp = 0,
        PowerUpBundle = 1,
        CoinPack = 2,
        OrbePack = 3,
        Cosmetic = 4,
        LivesPack = 5,
        RemoveAds = 6,
        BattlePass = 7,
        RarePerk = 8,

        /// <summary>The piggy bank: a real-money purchase that hands back orbes the player already earned.</summary>
        PiggyBank = 9
    }

    public sealed class ShopItem
    {
        public string Id { get; set; }

        public ShopItemKind Kind { get; set; }

        public PowerUpType PowerUp { get; set; }

        public int Quantity { get; set; } = 1;

        /// <summary>0 = not payable in coins.</summary>
        public int PriceCoins { get; set; }

        /// <summary>0 = not payable in orbes.</summary>
        public int PriceOrbes { get; set; }

        /// <summary>0 = not a real-money item.</summary>
        public int PriceCents { get; set; }

        public string Sku { get; set; }

        public bool IsDeal { get; set; }

        public int DiscountPermille { get; set; }

        public int RequiredVip { get; set; }

        public string CosmeticId { get; set; }

        /// <summary>For orbe packs: total orbes the next purchase grants (base + bonus + repeat bonus).</summary>
        public int OrbesGranted { get; set; }

        public int CoinsGranted { get; set; }

        /// <summary>Transparent value shown to players, e.g. orbes per euro (replaces the GDD's "obfuscated math").</summary>
        public decimal OrbesPerEuro { get; set; }

        public bool SoldOut { get; set; }
    }

    public sealed class ShopState
    {
        public int RefreshDay { get; set; } = -1;

        public int ManualRefreshesToday { get; set; }

        public List<ShopItem> DailyOffers { get; set; } = new List<ShopItem>();

        public HashSet<string> SoldOutOffers { get; set; } = new HashSet<string>();

        /// <summary>SKU -> number of purchases (for the "3rd purchase of the same pack" bonus).</summary>
        public Dictionary<string, int> PackPurchaseCounts { get; set; } = new Dictionary<string, int>();

        public HashSet<string> ProcessedTransactions { get; set; } = new HashSet<string>();

        public int SpendMonth { get; set; } = -1;

        public long MonthSpendCents { get; set; }
    }

    /// <summary>Everything the shop needs to know about the buyer.</summary>
    public sealed class PlayerContext
    {
        public string PlayerId { get; set; }

        public IWallet Wallet { get; set; }

        public Inventory Inventory { get; set; }

        public ShopState Shop { get; set; }

        /// <summary>The jar that fills as the player plays; the shop reads it to price the offer.</summary>
        public PiggyBankState PiggyBank { get; set; } = new PiggyBankState();

        public StaminaManager Stamina { get; set; }

        public VipStatus Vip { get; set; }

        public League HighestLeague { get; set; }

        public int HighestUnlockedStage { get; set; }

        /// <summary>Declared age (age gate at first launch). Null = unknown, treated as a minor for spending caps.</summary>
        public int? DeclaredAge { get; set; }

        public int BattlePassSeason { get; set; }

        /// <summary>Guild tech "power-up discount" applied to daily offers.</summary>
        public int GuildDiscountPermille { get; set; }
    }

    public sealed class PurchaseResult
    {
        public bool Success { get; internal set; }

        public ErrorCode Error { get; internal set; }

        public string Message { get; internal set; }

        public ShopItem Item { get; internal set; }

        public long CoinsSpent { get; internal set; }

        public long OrbesSpent { get; internal set; }

        public int CentsCharged { get; internal set; }

        public RewardData Granted { get; } = new RewardData();

        public VipUpgrade VipUpgrade { get; internal set; }

        internal static PurchaseResult Fail(ErrorCode error, string message = null) =>
            new PurchaseResult { Success = false, Error = error, Message = message ?? error.ToString() };
    }

    /// <summary>Analytics record of a completed purchase.</summary>
    public sealed class TransactionRecord
    {
        public string TransactionId { get; set; }

        public string PlayerId { get; set; }

        public string ItemId { get; set; }

        public ShopItemKind Kind { get; set; }

        public PaymentMethod Method { get; set; }

        public long CoinsSpent { get; set; }

        public long OrbesSpent { get; set; }

        public int CentsCharged { get; set; }

        public long TimestampUnixMs { get; set; }
    }

    public sealed class OrbePackQuote
    {
        public int Level { get; internal set; }

        public string Sku { get; internal set; }

        public int BaseOrbes { get; internal set; }

        public int BonusOrbes { get; internal set; }

        public int RepeatBonusOrbes { get; internal set; }

        public int TotalOrbes => BaseOrbes + BonusOrbes + RepeatBonusOrbes;

        public int PriceCents { get; internal set; }

        public decimal PriceEur => PriceCents / 100m;

        public decimal OrbesPerEuro => Math.Round(TotalOrbes / PriceEur, 1);
    }

    /// <summary>
    /// Task 12: shop. Daily rotating power-up offers (seeded per player/day so the server can re-derive them),
    /// coin/orbe/lives/cosmetic packs, remove-ads, battle pass, crown. Every purchase is logged.
    /// Real-money purchases are granted only after receipt validation (server) and are subject to consumer
    /// protection: age gate and monthly cap for minors. Prices are always shown in real currency.
    /// </summary>
    public sealed class ShopSystem
    {
        private readonly GameBalance _balance;
        private readonly IClock _clock;
        private readonly VipSystem _vip;

        public ShopSystem(GameBalance balance, IClock clock)
        {
            _balance = balance ?? throw new ArgumentNullException(nameof(balance));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _vip = new VipSystem(balance);
        }

        public event Action<TransactionRecord> OnTransaction;

        private int Today => TimeUtil.DayIndex(_clock.UtcNow);

        /// <summary>Id prefix of the always-available single power-up items ("powerup.NuclearBomb").</summary>
        public const string DirectPowerUpPrefix = "powerup.";

        public bool IsShopUnlocked(PlayerContext ctx) => ctx.HighestUnlockedStage > _balance.Story.UnlockShopStage;

        /// <summary>Prompt API: automatic refresh at UTC midnight.</summary>
        public void RefreshShop(PlayerContext ctx)
        {
            Validate(ctx);
            if (ctx.Shop.RefreshDay == Today && ctx.Shop.DailyOffers.Count > 0)
            {
                return;
            }
            ctx.Shop.RefreshDay = Today;
            ctx.Shop.ManualRefreshesToday = 0;
            RegenerateOffers(ctx);
        }

        /// <summary>Paid refresh (50 orbes).</summary>
        public OperationResult RefreshManually(PlayerContext ctx)
        {
            Validate(ctx);
            RefreshShop(ctx);
            OperationResult pay = ctx.Wallet.Debit(Currency.Orbes, _balance.Economy.ShopRefreshOrbes, TransactionReason.ShopRefresh, "day:" + Today);
            if (!pay.Success)
            {
                return pay;
            }
            ctx.Shop.ManualRefreshesToday++;
            RegenerateOffers(ctx);
            return OperationResult.Ok();
        }

        /// <summary>Prompt API: all items currently visible to this player.</summary>
        public List<ShopItem> GetShopItems(PlayerContext ctx)
        {
            RefreshShop(ctx);
            var items = new List<ShopItem>();
            int vipLevel = (int)(ctx.Vip?.Tier ?? VipTier.None);

            foreach (ShopItem offer in ctx.Shop.DailyOffers)
            {
                offer.SoldOut = ctx.Shop.SoldOutOffers.Contains(offer.Id);
                items.Add(offer);
            }

            // Every unlocked power-up can always be bought one at a time at its full (VIP-discounted) price:
            // "Get it" buttons on the loadout screens point here when the player runs out.
            foreach (PowerUpDefinition def in _balance.PowerUps.Definitions)
            {
                if (ctx.HighestLeague < def.UnlockLeague)
                {
                    continue;
                }
                PowerUpPrice unit = PowerUpManager.GetPowerUpPrice(_balance, def.Type, vipLevel);
                items.Add(new ShopItem { Id = DirectPowerUpPrefix + def.Type, Kind = ShopItemKind.PowerUp, PowerUp = def.Type, Quantity = 1, PriceCoins = unit.Coins, PriceOrbes = unit.Orbes });
            }

            foreach (CoinPackDefinition pack in _balance.Economy.CoinPacks)
            {
                items.Add(new ShopItem { Id = pack.Id, Kind = ShopItemKind.CoinPack, PriceOrbes = pack.PriceOrbes, CoinsGranted = pack.Coins });
            }

            foreach (OrbePackDefinition pack in _balance.Economy.OrbePacks)
            {
                ctx.Shop.PackPurchaseCounts.TryGetValue(pack.Sku, out int bought);
                OrbePackQuote quote = CalculateOrbePricing(pack.Level, bought);
                items.Add(new ShopItem
                {
                    Id = "orbes.pack" + pack.Level,
                    Kind = ShopItemKind.OrbePack,
                    Sku = pack.Sku,
                    PriceCents = pack.PriceCents,
                    OrbesGranted = quote.TotalOrbes,
                    OrbesPerEuro = quote.OrbesPerEuro
                });
            }

            if (ctx.Stamina != null)
            {
                LifePurchaseQuote one = ctx.Stamina.QuoteLives(1);
                LifePurchaseQuote bulk = ctx.Stamina.QuoteLives(_balance.Stamina.BulkCount);
                items.Add(new ShopItem { Id = "lives.1", Kind = ShopItemKind.LivesPack, Quantity = 1, PriceCoins = (int)one.Coins, PriceOrbes = (int)one.Orbes });
                items.Add(new ShopItem { Id = "lives." + _balance.Stamina.BulkCount, Kind = ShopItemKind.LivesPack, Quantity = _balance.Stamina.BulkCount, PriceCoins = (int)bulk.Coins, PriceOrbes = (int)bulk.Orbes, IsDeal = true });
            }

            foreach (CosmeticDefinition c in CosmeticCatalog.All)
            {
                if (c.PriceOrbes > 0 && !ctx.Inventory.HasCosmetic(c.Id))
                {
                    items.Add(new ShopItem { Id = "cosmetic." + c.Id, Kind = ShopItemKind.Cosmetic, CosmeticId = c.Id, PriceOrbes = c.PriceOrbes, RequiredVip = c.RequiredVip, SoldOut = c.RequiredVip > vipLevel });
                }
            }

            if (!ctx.Inventory.State.AdsRemoved)
            {
                items.Add(new ShopItem { Id = "removeads", Kind = ShopItemKind.RemoveAds, Sku = _balance.Economy.RemoveAdsSku, PriceCents = _balance.Economy.RemoveAdsPriceCents });
            }
            if (ctx.Inventory.State.PremiumPassSeason != ctx.BattlePassSeason)
            {
                // Real money only: no PriceOrbes, so the pass cannot be ground out for free.
                items.Add(new ShopItem { Id = "battlepass", Kind = ShopItemKind.BattlePass, Sku = _balance.Economy.BattlePassSku, PriceCents = _balance.Economy.BattlePassPriceCents });
            }
            // The jar only appears once it holds enough to be worth the price: an almost empty piggy bank on the
            // shelf teaches the player to ignore it.
            if (PiggyBank.CanOffer(ctx.PiggyBank, _balance.Economy.PiggyBank))
            {
                items.Add(new ShopItem
                {
                    Id = "piggybank",
                    Kind = ShopItemKind.PiggyBank,
                    Sku = _balance.Economy.PiggyBank.Sku,
                    PriceCents = _balance.Economy.PiggyBank.PriceCents,
                    OrbesGranted = (int)ctx.PiggyBank.Orbes
                });
            }
            if (!ctx.Inventory.State.RarePerkUnlocked)
            {
                items.Add(new ShopItem { Id = "crown", Kind = ShopItemKind.RarePerk, Sku = _balance.Economy.RarePerkSku, PriceCents = _balance.Economy.RarePerkPriceCents });
            }
            return items;
        }

        /// <summary>Prompt API: buys an item with coins or orbes. Real-money items go through <see cref="GrantRealMoneyPurchase"/>.</summary>
        public PurchaseResult BuyItem(string itemId, PaymentMethod method, PlayerContext ctx)
        {
            Validate(ctx);
            if (!IsShopUnlocked(ctx))
            {
                return PurchaseResult.Fail(ErrorCode.FeatureLocked);
            }
            if (method == PaymentMethod.RealMoney)
            {
                return PurchaseResult.Fail(ErrorCode.InvalidArgument, "Real-money items require a verified store receipt.");
            }

            ShopItem item = GetShopItems(ctx).Find(i => i.Id == itemId);
            if (item == null)
            {
                return PurchaseResult.Fail(ErrorCode.NotFound);
            }
            if (item.SoldOut)
            {
                return PurchaseResult.Fail(item.RequiredVip > 0 ? ErrorCode.PermissionDenied : ErrorCode.LimitReached);
            }

            if (item.Kind == ShopItemKind.LivesPack)
            {
                OperationResult bought = ctx.Stamina.BuyLives(item.Quantity, ctx.Wallet);
                if (!bought.Success)
                {
                    return PurchaseResult.Fail(bought.Error);
                }
                var livesResult = new PurchaseResult { Success = true, Item = item, CoinsSpent = item.PriceCoins, OrbesSpent = item.PriceOrbes };
                livesResult.Granted.Lives = item.Quantity;
                Log(ctx, item, method, livesResult);
                return livesResult;
            }

            int price = method == PaymentMethod.Coins ? item.PriceCoins : item.PriceOrbes;
            if (price <= 0)
            {
                return PurchaseResult.Fail(ErrorCode.InvalidArgument, "Item cannot be paid with " + method + ".");
            }

            Currency currency = method == PaymentMethod.Coins ? Currency.Coins : Currency.Orbes;
            OperationResult pay = ctx.Wallet.Debit(currency, price, item.Kind == ShopItemKind.BattlePass ? TransactionReason.BattlePassPurchase : TransactionReason.ShopPurchase, item.Id);
            if (!pay.Success)
            {
                return PurchaseResult.Fail(pay.Error);
            }

            var result = new PurchaseResult { Success = true, Item = item };
            if (currency == Currency.Coins)
            {
                result.CoinsSpent = price;
            }
            else
            {
                result.OrbesSpent = price;
            }

            switch (item.Kind)
            {
                case ShopItemKind.PowerUp:
                case ShopItemKind.PowerUpBundle:
                    ctx.Inventory.Add(item.PowerUp, item.Quantity);
                    result.Granted.AddPowerUp(item.PowerUp, item.Quantity);
                    if (!item.Id.StartsWith(DirectPowerUpPrefix, StringComparison.Ordinal))
                    {
                        ctx.Shop.SoldOutOffers.Add(item.Id);
                    }
                    break;
                case ShopItemKind.CoinPack:
                    ctx.Wallet.Credit(Currency.Coins, item.CoinsGranted, TransactionReason.CoinExchange, item.Id);
                    result.Granted.Coins = item.CoinsGranted;
                    break;
                case ShopItemKind.Cosmetic:
                    ctx.Inventory.AddCosmetic(item.CosmeticId);
                    result.Granted.Cosmetics.Add(item.CosmeticId);
                    break;
                case ShopItemKind.PiggyBank:
                    ctx.Wallet.Credit(currency, price, TransactionReason.Refund, item.Id);
                    return PurchaseResult.Fail(ErrorCode.InvalidArgument, "The piggy bank is a real-money purchase.");
                case ShopItemKind.BattlePass:
                    // Unreachable through the shop list (no orbe price) and refused here as well, so no future
                    // caller can quietly hand out the season pass for in-game currency.
                    ctx.Wallet.Credit(currency, price, TransactionReason.Refund, item.Id);
                    return PurchaseResult.Fail(ErrorCode.InvalidArgument, "The season pass is a real-money purchase.");
                default:
                    ctx.Wallet.Credit(currency, price, TransactionReason.Refund, item.Id);
                    return PurchaseResult.Fail(ErrorCode.InvalidArgument, "Item is not purchasable with in-game currency.");
            }

            Log(ctx, item, method, result);
            return result;
        }

        /// <summary>Must be checked BEFORE launching the store billing flow (money is taken by the store).</summary>
        public ErrorCode CanStartRealMoneyPurchase(string sku, PlayerContext ctx)
        {
            Validate(ctx);
            int cents = PriceCentsForSku(sku);
            if (cents <= 0)
            {
                return ErrorCode.NotFound;
            }
            if (sku == _balance.Economy.RemoveAdsSku && ctx.Inventory.State.AdsRemoved)
            {
                return ErrorCode.AlreadyClaimed;
            }
            if (sku == _balance.Economy.RarePerkSku && ctx.Inventory.State.RarePerkUnlocked)
            {
                return ErrorCode.AlreadyClaimed;
            }
            if (sku == _balance.Economy.BattlePassSku && ctx.Inventory.State.PremiumPassSeason == ctx.BattlePassSeason)
            {
                return ErrorCode.AlreadyClaimed;
            }

            int age = ctx.DeclaredAge ?? 0;
            if (ctx.DeclaredAge.HasValue && age < _balance.Economy.MinPurchaseAge)
            {
                return ErrorCode.PermissionDenied;
            }
            if (age < _balance.Economy.AdultAge)
            {
                RollSpendMonth(ctx.Shop);
                if (ctx.Shop.MonthSpendCents + cents > _balance.Economy.MinorMonthlyCapCents)
                {
                    return ErrorCode.LimitReached;
                }
            }
            return ErrorCode.None;
        }

        /// <summary>
        /// Grants a real-money purchase after the SERVER validated the store receipt. Idempotent per store transaction id.
        /// </summary>
        public PurchaseResult GrantRealMoneyPurchase(string sku, string storeTransactionId, PlayerContext ctx)
        {
            Validate(ctx);
            if (string.IsNullOrEmpty(storeTransactionId))
            {
                return PurchaseResult.Fail(ErrorCode.InvalidArgument, "Store transaction id required.");
            }
            if (ctx.Shop.ProcessedTransactions.Contains(storeTransactionId))
            {
                return PurchaseResult.Fail(ErrorCode.DuplicateRequest);
            }

            int cents = PriceCentsForSku(sku);
            if (cents <= 0)
            {
                return PurchaseResult.Fail(ErrorCode.NotFound);
            }

            var item = new ShopItem { Id = sku, Sku = sku, PriceCents = cents };
            var result = new PurchaseResult { Success = true, Item = item, CentsCharged = cents };

            OrbePackDefinition pack = _balance.Economy.OrbePacks.Find(p => p.Sku == sku);
            if (pack != null)
            {
                ctx.Shop.PackPurchaseCounts.TryGetValue(sku, out int bought);
                OrbePackQuote quote = CalculateOrbePricing(pack.Level, bought);
                ctx.Wallet.Credit(Currency.Orbes, quote.TotalOrbes, TransactionReason.RealMoneyPurchase, sku, "iap:" + storeTransactionId);
                ctx.Shop.PackPurchaseCounts[sku] = bought + 1;
                item.Kind = ShopItemKind.OrbePack;
                item.OrbesGranted = quote.TotalOrbes;
                result.Granted.Orbes = quote.TotalOrbes;
            }
            else if (sku == _balance.Economy.RemoveAdsSku)
            {
                item.Kind = ShopItemKind.RemoveAds;
                ctx.Inventory.State.AdsRemoved = true;
            }
            else if (sku == _balance.Economy.PiggyBank.Sku)
            {
                item.Kind = ShopItemKind.PiggyBank;
                // Only ever hands over orbes the player earned by playing: it cannot pay out more than went in.
                long contents = PiggyBank.Break(ctx.PiggyBank);
                if (contents > 0)
                {
                    ctx.Wallet.Credit(Currency.Orbes, contents, TransactionReason.RealMoneyPurchase, item.Id);
                }
                result.Granted.Orbes = contents;
            }
            else if (sku == _balance.Economy.BattlePassSku)
            {
                item.Kind = ShopItemKind.BattlePass;
                ctx.Inventory.State.PremiumPassSeason = ctx.BattlePassSeason;
            }
            else
            {
                item.Kind = ShopItemKind.RarePerk;
                ctx.Inventory.State.RarePerkUnlocked = true;
            }

            ctx.Shop.ProcessedTransactions.Add(storeTransactionId);
            RollSpendMonth(ctx.Shop);
            ctx.Shop.MonthSpendCents += cents;

            if (ctx.Vip != null)
            {
                result.VipUpgrade = _vip.RecordPurchase(ctx.Vip, cents);
                foreach (string cosmetic in result.VipUpgrade.CosmeticsGranted)
                {
                    if (ctx.Inventory.AddCosmetic(cosmetic))
                    {
                        result.Granted.Cosmetics.Add(cosmetic);
                    }
                }
                if (result.VipUpgrade.RarePerkGranted)
                {
                    ctx.Inventory.State.RarePerkUnlocked = true;
                }
            }

            Log(ctx, item, PaymentMethod.RealMoney, result, storeTransactionId);
            return result;
        }

        /// <summary>Prompt API: orbes and € price of a pack, including bonus and the every-3rd-purchase +20%.</summary>
        public OrbePackQuote CalculateOrbePricing(int packLevel, int previousPurchasesOfPack = 0)
        {
            OrbePackDefinition pack = _balance.Economy.OrbePacks.Find(p => p.Level == packLevel);
            if (pack == null)
            {
                throw new ArgumentOutOfRangeException(nameof(packLevel));
            }

            int purchaseNumber = Math.Max(0, previousPurchasesOfPack) + 1;
            int repeatBonus = purchaseNumber % _balance.Economy.RepeatPurchaseEvery == 0
                ? pack.BaseOrbes * _balance.Economy.RepeatPurchaseBonusPermille / 1000
                : 0;

            return new OrbePackQuote
            {
                Level = pack.Level,
                Sku = pack.Sku,
                BaseOrbes = pack.BaseOrbes,
                BonusOrbes = pack.BonusOrbes,
                RepeatBonusOrbes = repeatBonus,
                PriceCents = pack.PriceCents
            };
        }

        public Tuple<int, decimal> CalculateOrbePricingTuple(int packLevel)
        {
            OrbePackQuote q = CalculateOrbePricing(packLevel);
            return Tuple.Create(q.TotalOrbes, q.PriceEur);
        }

        /// <summary>Deterministic daily offers: same player + day + refresh index => same offers on client and server.</summary>
        public List<ShopItem> GenerateDailyOffers(string playerId, int day, int refreshIndex, League highestLeague, VipTier vip, int extraDiscountPermille = 0)
        {
            var rng = DeterministicRandom.Derive(StableHash.Fnv1a(playerId ?? string.Empty), (ulong)day, (ulong)refreshIndex);
            int[] weights = (int[])_balance.Economy.ShopTierWeights.Clone();
            for (int tier = 1; tier <= 3; tier++)
            {
                bool unlocked = false;
                foreach (PowerUpDefinition d in _balance.PowerUps.Definitions)
                {
                    unlocked |= (int)d.Tier == tier && highestLeague >= d.UnlockLeague;
                }
                if (!unlocked)
                {
                    weights[tier - 1] = 0;
                }
            }

            var offers = new List<ShopItem>();
            int dealIndex = rng.NextInt(_balance.Economy.ShopDailyOffers);
            for (int i = 0; i < _balance.Economy.ShopDailyOffers; i++)
            {
                int tier = rng.NextWeightedIndex(weights) + 1;
                var candidates = _balance.PowerUps.Definitions.FindAll(d => (int)d.Tier == tier && highestLeague >= d.UnlockLeague);
                PowerUpDefinition def = candidates[rng.NextInt(candidates.Count)];

                bool bundle = rng.ChancePermille(300);
                int quantity = bundle ? _balance.Economy.ShopBundleSize : 1;
                PowerUpPrice unit = PowerUpManager.GetPowerUpPrice(_balance, def.Type, (int)vip);
                int discount = Math.Min(500, (bundle ? 100 : 0) + (i == dealIndex ? _balance.Economy.ShopDealDiscountPermille : 0) + Math.Max(0, extraDiscountPermille));

                offers.Add(new ShopItem
                {
                    Id = "daily." + day + "." + refreshIndex + "." + i + "." + def.Type,
                    Kind = bundle ? ShopItemKind.PowerUpBundle : ShopItemKind.PowerUp,
                    PowerUp = def.Type,
                    Quantity = quantity,
                    PriceCoins = Discounted(unit.Coins * quantity, discount),
                    PriceOrbes = Discounted(unit.Orbes * quantity, discount),
                    IsDeal = i == dealIndex,
                    DiscountPermille = discount
                });
            }
            return offers;
        }

        private void RegenerateOffers(PlayerContext ctx)
        {
            ctx.Shop.DailyOffers = GenerateDailyOffers(ctx.PlayerId, Today, ctx.Shop.ManualRefreshesToday, ctx.HighestLeague, ctx.Vip?.Tier ?? VipTier.None, ctx.GuildDiscountPermille);
            ctx.Shop.SoldOutOffers.Clear();
        }

        private int PriceCentsForSku(string sku)
        {
            if (string.IsNullOrEmpty(sku))
            {
                return 0;
            }
            OrbePackDefinition pack = _balance.Economy.OrbePacks.Find(p => p.Sku == sku);
            if (pack != null)
            {
                return pack.PriceCents;
            }
            if (sku == _balance.Economy.RemoveAdsSku)
            {
                return _balance.Economy.RemoveAdsPriceCents;
            }
            if (sku == _balance.Economy.BattlePassSku)
            {
                return _balance.Economy.BattlePassPriceCents;
            }
            return sku == _balance.Economy.RarePerkSku ? _balance.Economy.RarePerkPriceCents : 0;
        }

        private void RollSpendMonth(ShopState shop)
        {
            DateTime now = _clock.UtcNow;
            int month = now.Year * 12 + now.Month;
            if (shop.SpendMonth != month)
            {
                shop.SpendMonth = month;
                shop.MonthSpendCents = 0;
            }
        }

        private void Log(PlayerContext ctx, ShopItem item, PaymentMethod method, PurchaseResult result, string transactionId = null)
        {
            OnTransaction?.Invoke(new TransactionRecord
            {
                TransactionId = transactionId ?? "shop_" + TimeUtil.ToUnixMs(_clock.UtcNow).ToString("x") + "_" + item.Id,
                PlayerId = ctx.PlayerId,
                ItemId = item.Id,
                Kind = item.Kind,
                Method = method,
                CoinsSpent = result.CoinsSpent,
                OrbesSpent = result.OrbesSpent,
                CentsCharged = result.CentsCharged,
                TimestampUnixMs = TimeUtil.ToUnixMs(_clock.UtcNow)
            });
        }

        private static int Discounted(int price, int discountPermille) => price <= 0 ? 0 : Math.Max(1, price * (1000 - discountPermille) / 1000);

        private static void Validate(PlayerContext ctx)
        {
            if (ctx == null)
            {
                throw new ArgumentNullException(nameof(ctx));
            }
            if (ctx.Wallet == null || ctx.Inventory == null || ctx.Shop == null)
            {
                throw new ArgumentException("Player context needs Wallet, Inventory and Shop.", nameof(ctx));
            }
        }
    }
}
