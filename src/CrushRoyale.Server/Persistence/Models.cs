using CrushRoyale.Core.AntiCheat;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Economy;
using CrushRoyale.Core.Gameplay;
using CrushRoyale.Core.Pets;
using CrushRoyale.Core.Progression;
using CrushRoyale.Core.Pvp;
using CrushRoyale.Core.Social;
using CrushRoyale.Core.Story;

namespace CrushRoyale.Server.Persistence;

/// <summary>Hero chosen at first launch (GDD: male/female, customizable name and appearance).</summary>
public sealed class HeroProfile
{
    public string Gender { get; set; } = "female";

    public string Name { get; set; } = string.Empty;

    public int Appearance { get; set; }
}

/// <summary>
/// Complete server-side state of a player, stored as one JSON document. Every sub-state is the Core type that
/// owns its rules, so the server never re-implements game logic.
/// </summary>
public sealed class PlayerState
{
    public string DisplayName { get; set; } = string.Empty;

    public HeroProfile Hero { get; set; } = new();

    public bool HeroChosen { get; set; }

    public int? DeclaredAge { get; set; }

    public string Region { get; set; } = string.Empty;

    public string Language { get; set; } = "en";

    public long PlaytimeSeconds { get; set; }

    public WalletState Wallet { get; set; } = new();

    public InventoryState Inventory { get; set; } = new();

    public StaminaState Stamina { get; set; } = new();

    public StoryProgress Story { get; set; } = new();

    public AchievementProgressState Achievements { get; set; } = new();

    public DailyQuestState Quests { get; set; } = new();

    public BattlePassState BattlePass { get; set; } = new();

    public LoginCalendarState Login { get; set; } = new();

    public AdState Ads { get; set; } = new();

    public ShopState Shop { get; set; } = new();

    public FriendsState Friends { get; set; } = new();

    public PlayerTrophyRecord Pvp { get; set; } = new();

    public PlayerIntegrityRecord Integrity { get; set; } = new();

    public VipStatus Vip { get; set; } = new();

    /// <summary>UTC day index of the last daily VIP gift claimed (VIP 6+).</summary>
    public int VipGiftDay { get; set; } = -1;

    /// <summary>UTC day of the last daily wheel spin.</summary>
    public int WheelDay { get; set; } = -1;

    /// <summary>Paid spins taken today; reset with the day (see EconomyService).</summary>
    public int WheelExtrasToday { get; set; }

    public long? GuildId { get; set; }

    public HashSet<StoryEnding> EndingsReached { get; set; } = new();

    public PetCollectionState Pets { get; set; } = new();

    public ChestState Chests { get; set; } = new();

    /// <summary>The jar that fills as the player plays (see CrushRoyale.Core.Economy.PiggyBank).</summary>
    public PiggyBankState PiggyBank { get; set; } = new();

    public long LastSeenUnixMs { get; set; }
}

/// <summary>Validated telemetry batch ready to be stored.</summary>
public sealed class TelemetryBatch
{
    public string SessionId { get; init; } = string.Empty;

    public string AppVersion { get; init; } = string.Empty;

    public string Device { get; init; } = string.Empty;

    public string Os { get; init; } = string.Empty;

    public List<(string Name, DateTime? ClientAt, string PropsJson)> Events { get; } = new();

    public List<(string Fingerprint, string Message, string Stack, int Count)> Errors { get; } = new();
}

public sealed class PlayerRecord
{
    public Guid Id { get; init; }

    public long Version { get; set; }

    public DateTime CreatedAt { get; init; }

    public PlayerState State { get; set; } = new();
}

public enum MatchStatus : short
{
    Started = 0,
    Completed = 1,
    Rejected = 2,
    Expired = 3
}

/// <summary>What the server issued when a match started; the submitted replay must match it.</summary>
public sealed class MatchConfigSnapshot
{
    public List<LoadoutEntry> Loadout { get; set; } = new();

    public League HighestLeague { get; set; }

    public int AssistExtraMoves { get; set; }

    /// <summary>Win-streak starting bonuses placed on the board.</summary>
    public int StartBoosters { get; set; }

    public int ContinuesAuthorized { get; set; }

    public int OpponentTrophies { get; set; }

    public int PlayerTrophies { get; set; }

    public bool Ranked { get; set; }

    public long? GuildId { get; set; }

    /// <summary>Set on a friendly match that settles a duel (see FriendsManager.FriendDuel).</summary>
    public string? DuelId { get; set; }

    public int GuildWeek { get; set; }

    /// <summary>Pet equipped when the match started and the level it plays at (capped in PvP).</summary>
    public PetType Pet { get; set; }

    public int PetLevel { get; set; }

    /// <summary>VIP 9-10 friendly-challenge perk: a power-up copied from the friend (not taken from the inventory).</summary>
    public PowerUpType? StolenPowerUp { get; set; }
}

public sealed class MatchRow
{
    public string Id { get; set; } = string.Empty;

    public Guid PlayerId { get; set; }

    public GameMode Mode { get; set; }

    public ulong Seed { get; set; }

    public int StageId { get; set; }

    public MatchConfigSnapshot Config { get; set; } = new();

    public MatchStatus Status { get; set; }

    public Guid? OpponentId { get; set; }

    public long? GhostReplayId { get; set; }

    public long? ReplayId { get; set; }

    public string? ResultJson { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }
}

public sealed class ReplayRow
{
    public long Id { get; set; }

    public Guid PlayerId { get; set; }

    public GameMode Mode { get; set; }

    public ulong Seed { get; set; }

    public int StageId { get; set; }

    public int Trophies { get; set; }

    public long FinalScore { get; set; }

    public string Region { get; set; } = string.Empty;

    public byte[] Data { get; set; } = Array.Empty<byte>();

    public bool IsGhost { get; set; }

    public DateTime CreatedAt { get; set; }
}

public sealed class GuildRecord
{
    public long Id { get; set; }

    public long Version { get; set; }

    public DateTime CreatedAt { get; set; }

    /// <summary>Core aggregate. Its string Id always equals <see cref="Id"/>.</summary>
    public Guild Guild { get; set; } = new();

    /// <summary>Boss rewards waiting for members who were not the one landing the final blow.</summary>
    public List<PendingGuildReward> PendingRewards { get; set; } = new();

    /// <summary>Player ids invited by officers (required to join closed guilds).</summary>
    public List<string> Invited { get; set; } = new();
}

public sealed class PendingGuildReward
{
    public string Id { get; set; } = string.Empty;

    public RewardData Reward { get; set; } = new();

    public List<string> PlayerIds { get; set; } = new();
}

public sealed class GuildMessageRow
{
    public long Id { get; set; }

    public long GuildId { get; set; }

    public Guid? PlayerId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    public bool Masked { get; set; }

    public DateTime CreatedAt { get; set; }
}

public enum IapStatus : short
{
    Granted = 0,
    Refunded = 1,
    Chargeback = 2
}

public sealed class IapRow
{
    public string StoreTransactionId { get; set; } = string.Empty;

    public Guid PlayerId { get; set; }

    public string Sku { get; set; } = string.Empty;

    public int PriceCents { get; set; }

    public string PurchaseTokenHash { get; set; } = string.Empty;

    public IapStatus Status { get; set; }

    public DateTime CreatedAt { get; set; }
}

public sealed class FlagRow
{
    public long Id { get; set; }

    public Guid PlayerId { get; set; }

    public FlagReason Reason { get; set; }

    public CheatSeverity Severity { get; set; }

    public string? Details { get; set; }

    public string? MatchId { get; set; }

    public bool Reviewed { get; set; }

    public string? ReviewOutcome { get; set; }

    public Guid? ReviewedBy { get; set; }

    public DateTime CreatedAt { get; set; }
}

public sealed record LeaderboardRow(int Rank, Guid PlayerId, string DisplayName, int Trophies, League League);

public sealed record GuildRankingRow(int Rank, long GuildId, string Name, long TotalTrophies, int Members, int Level);

/// <summary>One line of the weekly guild race: only guilds large enough to be ranked appear.</summary>
public sealed record GuildRaceRow(int Rank, long GuildId, string Name, long Points, int Members);

public sealed record PlayerSummary(Guid Id, string DisplayName, int Trophies, League League, int HighestStage, long? GuildId, string? Frame, string? Title);

public sealed record GuildSummary(long Id, string Name, int Level, int Members, long TotalTrophies, bool IsOpen, int MinTrophies);

public sealed record GhostRow(long ReplayId, Guid PlayerId, int Trophies, DateTime CreatedAt, string Region, ulong Seed);

/// <summary>Thrown when an optimistic-concurrency update lost the race; services retry the whole transaction.</summary>
public sealed class ConcurrencyException : Exception
{
    public ConcurrencyException(string message) : base(message)
    {
    }
}
