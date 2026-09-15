using CrushRoyale.Core.Common;
using CrushRoyale.Server.Infrastructure;
using CrushRoyale.Server.Persistence;

namespace CrushRoyale.Server.Services;

public sealed class OperationContext
{
    public OperationContext(IStoreTransaction tx, PlayerWorkspace player)
    {
        Tx = tx;
        Player = player;
    }

    public IStoreTransaction Tx { get; }

    public PlayerWorkspace Player { get; }
}

/// <summary>
/// Transaction pipeline for player operations: lock the player row, build the workspace, run the business logic,
/// persist state + ledger + logs atomically, retry on optimistic-concurrency conflicts.
/// Lock order is always player(s) by ascending id, then guild, which prevents deadlocks.
/// </summary>
public sealed class PlayerOperations
{
    public const int MaxAttempts = 3;

    private readonly IGameStore _store;
    private readonly IBalanceProvider _balance;
    private readonly IClock _clock;

    public PlayerOperations(IGameStore store, IBalanceProvider balance, IClock clock)
    {
        _store = store;
        _balance = balance;
        _clock = clock;
    }

    public IGameStore Store => _store;

    public IBalanceProvider Balance => _balance;

    public IClock Clock => _clock;

    public Task<T> RunAsync<T>(Guid playerId, Func<OperationContext, Task<T>> work, CancellationToken cancellationToken, bool allowBanned = false) =>
        WithRetry(() => _store.TransactAsync(async tx =>
        {
            PlayerWorkspace player = await LoadAsync(tx, playerId, allowBanned).ConfigureAwait(false);
            var context = new OperationContext(tx, player);
            T result = await work(context).ConfigureAwait(false);
            await player.FlushAsync(tx).ConfigureAwait(false);
            return result;
        }, cancellationToken), cancellationToken);

    public Task<T> RunAsync<T>(Guid playerId, Func<OperationContext, T> work, CancellationToken cancellationToken, bool allowBanned = false) =>
        RunAsync(playerId, ctx => Task.FromResult(work(ctx)), cancellationToken, allowBanned);

    /// <summary>Two players in one transaction (friends). Rows are locked by ascending id.</summary>
    public Task<T> RunPairAsync<T>(Guid actorId, Guid otherId, Func<OperationContext, OperationContext, Task<T>> work, CancellationToken cancellationToken)
    {
        if (actorId == otherId)
        {
            throw new ApiException(ErrorCode.InvalidArgument, "Cannot target yourself.");
        }

        return WithRetry(() => _store.TransactAsync(async tx =>
        {
            bool actorFirst = actorId.CompareTo(otherId) < 0;
            PlayerWorkspace first = await LoadAsync(tx, actorFirst ? actorId : otherId, !actorFirst).ConfigureAwait(false);
            PlayerWorkspace second = await LoadAsync(tx, actorFirst ? otherId : actorId, actorFirst).ConfigureAwait(false);
            PlayerWorkspace actor = actorFirst ? first : second;
            PlayerWorkspace other = actorFirst ? second : first;

            T result = await work(new OperationContext(tx, actor), new OperationContext(tx, other)).ConfigureAwait(false);
            await first.FlushAsync(tx).ConfigureAwait(false);
            await second.FlushAsync(tx).ConfigureAwait(false);
            return result;
        }, cancellationToken), cancellationToken);
    }

    /// <summary>Read-only access (no lock, no write).</summary>
    public Task<T> ReadAsync<T>(Guid playerId, Func<PlayerWorkspace, IStoreTransaction, Task<T>> read, CancellationToken cancellationToken) =>
        _store.TransactAsync(async tx =>
        {
            PlayerRecord record = await tx.GetPlayerAsync(playerId, forUpdate: false).ConfigureAwait(false)
                ?? throw new ApiException(ErrorCode.NotFound, "Unknown player: call " + Contracts.ApiRoutes.Login + " first.");
            GuildRecord? guild = record.State.GuildId is long gid ? await tx.GetGuildAsync(gid, forUpdate: false).ConfigureAwait(false) : null;
            return await read(new PlayerWorkspace(record, _balance.Current, _balance.Catalog, _clock, guild), tx).ConfigureAwait(false);
        }, cancellationToken);

    /// <summary>Non-player transaction with the same retry policy (jobs).</summary>
    public Task<T> RunStoreAsync<T>(Func<IStoreTransaction, Task<T>> work, CancellationToken cancellationToken) =>
        WithRetry(() => _store.TransactAsync(work, cancellationToken), cancellationToken);

    private async Task<PlayerWorkspace> LoadAsync(IStoreTransaction tx, Guid playerId, bool allowBanned)
    {
        PlayerRecord record = await tx.GetPlayerAsync(playerId, forUpdate: true).ConfigureAwait(false)
            ?? throw new ApiException(ErrorCode.NotFound, "Unknown player: call " + Contracts.ApiRoutes.Login + " first.");
        GuildRecord? guild = record.State.GuildId is long gid ? await tx.GetGuildAsync(gid, forUpdate: false).ConfigureAwait(false) : null;
        var workspace = new PlayerWorkspace(record, _balance.Current, _balance.Catalog, _clock, guild);
        if (!allowBanned && workspace.IsBanned)
        {
            throw new ApiException(ErrorCode.Banned, workspace.State.Integrity.PermanentlyBanned
                ? "Account permanently banned."
                : "Account suspended until " + TimeUtil.FromUnixMs(workspace.State.Integrity.SuspendedUntilUnixMs).ToString("u"));
        }
        return workspace;
    }

    private static async Task<T> WithRetry<T>(Func<Task<T>> attempt, CancellationToken cancellationToken)
    {
        for (int i = 1; ; i++)
        {
            try
            {
                return await attempt().ConfigureAwait(false);
            }
            catch (ConcurrencyException) when (i < MaxAttempts)
            {
                await Task.Delay(15 * i * i, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
