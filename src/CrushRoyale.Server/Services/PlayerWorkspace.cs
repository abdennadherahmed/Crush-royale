using CrushRoyale.Core.AntiCheat;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Economy;
using CrushRoyale.Core.Gameplay;
using CrushRoyale.Core.Pets;
using CrushRoyale.Core.Progression;
using CrushRoyale.Core.Social;
using CrushRoyale.Core.Story;
using CrushRoyale.Server.Infrastructure;
using CrushRoyale.Server.Persistence;

namespace CrushRoyale.Server.Services;

/// <summary>
/// One player's state for the duration of a transaction, wrapped in the Core rule objects.
/// Collects side effects (ledger lines, purchase logs, cheat flags, unlocked achievements) that are persisted by
/// <see cref="FlushAsync"/> in the same database transaction.
/// </summary>
public sealed class PlayerWorkspace
{
    public const int WalletLedgerKept = 100;

    private Wallet? _wallet;
    private Inventory? _inventory;
    private StaminaManager? _stamina;
    private StoryManager? _story;
    private AchievementSystem? _achievements;
    private AntiCheatManager? _antiCheat;

    public PlayerWorkspace(PlayerRecord record, GameBalance balance, StageCatalog catalog, IClock clock, GuildRecord? guild)
    {
        Record = record ?? throw new ArgumentNullException(nameof(record));
        Balance = balance ?? throw new ArgumentNullException(nameof(balance));
        Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        Clock = clock ?? throw new ArgumentNullException(nameof(clock));
        Guild = guild;
    }

    public PlayerRecord Record { get; }

    public PlayerState State => Record.State;

    public Guid Id => Record.Id;

    public string IdString => Record.Id.ToString();

    public GameBalance Balance { get; }

    public StageCatalog Catalog { get; }

    public IClock Clock { get; }

    /// <summary>Guild used for member bonuses (read without lock).</summary>
    public GuildRecord? Guild { get; }

    public DateTime Now => Clock.UtcNow;

    public long NowMs => TimeUtil.ToUnixMs(Clock.UtcNow);

    public List<LedgerEntry> NewLedgerEntries { get; } = new();

    public List<TransactionRecord> NewPurchases { get; } = new();

    public List<CheatFlag> NewFlags { get; } = new();

    public List<string> UnlockedAchievements { get; } = new();

    public Wallet Wallet
    {
        get
        {
            if (_wallet == null)
            {
                _wallet = new Wallet(State.Wallet, Clock, WalletLedgerKept);
                _wallet.Transaction += NewLedgerEntries.Add;
            }
            return _wallet;
        }
    }

    public Inventory Inventory => _inventory ??= new Inventory(State.Inventory);

    public StaminaManager Stamina => _stamina ??= new StaminaManager(Balance, State.Stamina, Clock, State.Vip.Tier, GuildTech(Core.Social.GuildTech.MaxLives), GuildTech(Core.Social.GuildTech.LifeRecharge));

    public StoryManager Story => _story ??= new StoryManager(Balance, Catalog, State.Story, Clock);

    public AchievementSystem Achievements
    {
        get
        {
            if (_achievements == null)
            {
                _achievements = new AchievementSystem(State.Achievements, Clock);
                _achievements.OnAchievementUnlocked += id =>
                {
                    if (!UnlockedAchievements.Contains(id))
                    {
                        UnlockedAchievements.Add(id);
                    }
                };
            }
            return _achievements;
        }
    }

    /// <summary>Anti-cheat bound to this transaction: flags are stored by <see cref="FlushAsync"/>.</summary>
    public AntiCheatManager AntiCheat => _antiCheat ??= new AntiCheatManager(Balance, Clock, new TransactionFlagSink(NewFlags));

    public bool IsBanned => State.Integrity.PermanentlyBanned || State.Integrity.SuspendedUntilUnixMs > NowMs;

    public int BattlePassSeason => BattlePass.SeasonIndex(Now, Balance.LiveOps);

    public int GuildTech(GuildTech tech) => Guild == null ? 0 : GuildManager.GetTechValue(Guild.Guild, tech);

    public BonusContext Bonuses => new()
    {
        Vip = State.Vip.Tier,
        CollectionPagesCompleted = State.Achievements.PagesCompleted.Count,
        GuildCoinBonusPermille = GuildTech(Core.Social.GuildTech.CoinBonus),
        RarePerk = State.Inventory.RarePerkUnlocked
    };

    public PlayerContext ShopContext => new()
    {
        PlayerId = IdString,
        Wallet = Wallet,
        Inventory = Inventory,
        Shop = State.Shop,
        Stamina = Stamina,
        Vip = State.Vip,
        HighestLeague = State.Pvp.HighestLeague,
        HighestUnlockedStage = State.Story.HighestUnlockedStage,
        DeclaredAge = State.DeclaredAge,
        BattlePassSeason = BattlePassSeason,
        GuildDiscountPermille = GuildTech(Core.Social.GuildTech.PowerUpDiscount)
    };

    /// <summary>Credits earned coins with the capped bonus stack (VIP, collection book, guild, crown).</summary>
    public long CreditEarnedCoins(long baseCoins, TransactionReason reason, string reference, string? idempotencyKey = null)
    {
        long coins = RewardCalculator.ApplyCoinBonus(Balance, baseCoins, Bonuses);
        if (coins <= 0)
        {
            return 0;
        }
        Wallet.Credit(Currency.Coins, coins, reason, reference, idempotencyKey).ThrowIfFailed();
        Achievements.IncrementStat(StatKey.CoinsEarned, coins);
        return coins;
    }

    /// <summary>Credits earned (not purchased) orbes with the VIP orbe bonus.</summary>
    public long CreditEarnedOrbes(long baseOrbes, TransactionReason reason, string reference, string? idempotencyKey = null)
    {
        long orbes = RewardCalculator.ApplyOrbeBonus(Balance, baseOrbes, State.Vip.Tier);
        if (orbes <= 0)
        {
            return 0;
        }
        Wallet.Credit(Currency.Orbes, orbes, reason, reference, idempotencyKey).ThrowIfFailed();
        Achievements.IncrementStat(StatKey.OrbesEarned, orbes);
        return orbes;
    }

    /// <summary>Grants a fixed reward bundle (achievements, quests, battle pass, bosses): no bonus multipliers.</summary>
    public void GrantReward(RewardData reward, TransactionReason reason, string reference, string? idempotencyKey = null)
    {
        if (reward == null || reward.IsEmpty)
        {
            return;
        }
        reward.GrantTo(Wallet, Inventory, reason, reference, idempotencyKey).ThrowIfFailed();
        if (reward.Coins > 0)
        {
            Achievements.IncrementStat(StatKey.CoinsEarned, reward.Coins);
        }
        if (reward.Orbes > 0)
        {
            Achievements.IncrementStat(StatKey.OrbesEarned, reward.Orbes);
        }
        if (reward.Lives > 0)
        {
            Stamina.AddLives(reward.Lives);
        }
        if (reward.BattlePassXp > 0)
        {
            AddBattlePassXp(reward.BattlePassXp);
        }
    }

    public PetCollection Pets => new(State.Pets ??= new PetCollectionState(), Balance.Pets);

    /// <summary>Freezes the equipped pet into a match snapshot (PvP modes use the capped level).</summary>
    public void SnapshotPet(MatchConfigSnapshot snapshot, GameMode mode)
    {
        int level = Pets.MatchLevel(out PetType pet);
        bool pvp = mode == GameMode.PvpRanked || mode == GameMode.FriendlyChallenge;
        snapshot.Pet = pet;
        snapshot.PetLevel = pet == PetType.None ? 0 : Math.Min(level, pvp ? Balance.Pets.PvpLevelCap : Balance.Pets.MaxLevel);
    }

    /// <summary>XP for the pet that played the match (if it is still owned and equipped).</summary>
    public void AwardPetXp(MatchConfigSnapshot snapshot, GameMode mode, bool won)
    {
        PetCollection pets = Pets;
        if (snapshot.Pet != PetType.None && pets.State.Equipped == snapshot.Pet)
        {
            pets.AddXp(pets.XpFor(mode, won));
        }
    }

    public void AddBattlePassXp(int xp)
    {
        BattlePass.EnsureSeason(State.BattlePass, Now, Balance.LiveOps);
        BattlePass.AddXp(State.BattlePass, xp, GuildTech(Core.Social.GuildTech.BattlePassXp));
        Achievements.SetStatMax(StatKey.BattlePassTiers, BattlePass.TierForXp(State.BattlePass.Xp, Balance.LiveOps));
    }

    public void EnsureDailyState()
    {
        DailyQuests.EnsureDay(State.Quests, IdString, Now, Balance.LiveOps, Story.IsFeatureUnlocked(Feature.Pvp), State.GuildId != null);
        BattlePass.EnsureSeason(State.BattlePass, Now, Balance.LiveOps);
    }

    public void TrackQuest(QuestType type, int amount)
    {
        if (amount <= 0 || !Story.IsFeatureUnlocked(Feature.DailyQuests))
        {
            return;
        }
        EnsureDailyState();
        DailyQuests.Track(State.Quests, type, amount);
    }

    public async Task FlushAsync(IStoreTransaction tx)
    {
        if (_inventory != null || _achievements != null)
        {
            Achievements.SetStatMax(StatKey.CosmeticsOwned, State.Inventory.Cosmetics.Count);
            Achievements.SetStatMax(StatKey.FriendsCount, State.Friends.Friends.Count);
        }
        State.LastSeenUnixMs = NowMs;

        await tx.UpdatePlayerAsync(Record).ConfigureAwait(false);
        if (NewLedgerEntries.Count > 0)
        {
            await tx.AppendLedgerAsync(Id, NewLedgerEntries).ConfigureAwait(false);
        }
        foreach (TransactionRecord purchase in NewPurchases)
        {
            await tx.AppendPurchaseLogAsync(Id, purchase).ConfigureAwait(false);
        }
        foreach (CheatFlag flag in NewFlags)
        {
            await tx.InsertFlagAsync(flag).ConfigureAwait(false);
        }
    }

    /// <summary>Flag store that only buffers flags raised inside the current transaction.</summary>
    private sealed class TransactionFlagSink : IFlagStore
    {
        private readonly List<CheatFlag> _flags;

        public TransactionFlagSink(List<CheatFlag> flags)
        {
            _flags = flags;
        }

        public void Add(CheatFlag flag) => _flags.Add(flag);

        public IReadOnlyList<CheatFlag> ForPlayer(string playerId) => _flags.Where(f => f.PlayerId == playerId).ToList();

        public IReadOnlyList<CheatFlag> Pending() => _flags.ToList();
    }
}
