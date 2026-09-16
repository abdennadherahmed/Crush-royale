using CrushRoyale.Core.AntiCheat;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Economy;

namespace CrushRoyale.Server.Persistence;

/// <summary>
/// Storage abstraction. Implemented by <see cref="PostgresGameStore"/> (Supabase) and
/// <see cref="InMemoryGameStore"/> (development and integration tests).
/// All writes go through <see cref="TransactAsync{T}"/>: one database transaction per business operation.
/// </summary>
public interface IGameStore
{
    Task<T> TransactAsync<T>(Func<IStoreTransaction, Task<T>> work, CancellationToken cancellationToken);

    Task<IReadOnlyList<LeaderboardRow>> TopPlayersAsync(League? league, int limit, CancellationToken cancellationToken);

    /// <summary>1-based rank of the player (inside a league when given), or null.</summary>
    Task<int?> PlayerRankAsync(Guid playerId, League? league, CancellationToken cancellationToken);

    Task<IReadOnlyList<GuildRankingRow>> TopGuildsAsync(int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<GhostRow>> FindGhostsAsync(int minTrophies, int maxTrophies, DateTime recordedAfter, int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<PlayerSummary>> SearchPlayersAsync(string namePrefix, int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<PlayerSummary>> GetPlayerSummariesAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    Task<IReadOnlyList<GuildSummary>> SearchGuildsAsync(string namePrefix, int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<GuildMessageRow>> GetGuildMessagesAsync(long guildId, long? beforeId, int limit, CancellationToken cancellationToken);

    Task<IReadOnlyList<FlagRow>> PendingFlagsAsync(int limit, CancellationToken cancellationToken);

    Task<string?> GetConfigAsync(string key, CancellationToken cancellationToken);

    /// <summary>Probe used by the health endpoint.</summary>
    Task<bool> PingAsync(CancellationToken cancellationToken);

    /// <summary>Analytics events and client exceptions (outside game transactions: best effort).</summary>
    Task InsertTelemetryAsync(Guid playerId, TelemetryBatch batch, CancellationToken cancellationToken);
}

public interface IStoreTransaction
{
    Task<PlayerRecord?> GetPlayerAsync(Guid id, bool forUpdate = true);

    Task InsertPlayerAsync(PlayerRecord player);

    /// <summary>Saves with optimistic concurrency (version check). Throws <see cref="ConcurrencyException"/>.</summary>
    Task UpdatePlayerAsync(PlayerRecord player);

    /// <summary>
    /// Deletes the account for good: the auth user and player row, with dependent rows cascading and the financial
    /// audit trail anonymised (see migration 20260915170000_account_deletion).
    /// </summary>
    Task DeletePlayerAccountAsync(Guid id);

    /// <summary>Players ordered by id, for batch jobs (weekly reset).</summary>
    Task<IReadOnlyList<PlayerRecord>> GetPlayersPageAsync(Guid? afterId, int limit);

    Task AppendLedgerAsync(Guid playerId, IReadOnlyList<LedgerEntry> entries);

    Task AppendPurchaseLogAsync(Guid playerId, TransactionRecord record);

    /// <summary>Returns false if the store transaction id was already granted.</summary>
    Task<bool> TryInsertIapAsync(IapRow row);

    Task<long> InsertReplayAsync(ReplayRow row);

    Task<ReplayRow?> GetReplayAsync(long id);

    Task<ReplayRow?> LatestGhostOfPlayerAsync(Guid playerId);

    Task<MatchRow?> GetMatchAsync(string id, bool forUpdate = true);

    Task InsertMatchAsync(MatchRow match);

    Task UpdateMatchAsync(MatchRow match);

    Task<int> ExpireOpenMatchesAsync(DateTime startedBefore);

    Task<GuildRecord?> GetGuildAsync(long id, bool forUpdate = true);

    /// <summary>Returns the new id, or null if the name is taken (case-insensitive).</summary>
    Task<long?> InsertGuildAsync(GuildRecord guild);

    Task UpdateGuildAsync(GuildRecord guild);

    Task DeleteGuildAsync(long id);

    Task<IReadOnlyList<GuildRecord>> GetAllGuildsAsync();

    Task<long> InsertGuildMessageAsync(GuildMessageRow message);

    Task<long> InsertFlagAsync(CheatFlag flag);

    Task<FlagRow?> GetFlagAsync(long id);

    Task UpdateFlagAsync(FlagRow flag);

    /// <summary>Registers a (device, account) pair and returns all accounts seen on the device.</summary>
    Task<IReadOnlyList<Guid>> RegisterDeviceAsync(string deviceHash, Guid playerId);

    Task ArchiveSeasonAsync(int week, League league, string entriesJson);

    Task ArchiveGuildSeasonAsync(int week, string entriesJson);

    Task<string?> GetConfigAsync(string key);

    Task SetConfigAsync(string key, string json, Guid? updatedBy);

    /// <summary>Transaction-scoped advisory lock (released at commit). Returns false if another instance holds it.</summary>
    Task<bool> TryAdvisoryLockAsync(long key);
}
