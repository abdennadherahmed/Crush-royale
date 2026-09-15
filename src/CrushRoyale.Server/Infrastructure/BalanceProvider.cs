using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Story;
using CrushRoyale.Server.Persistence;

namespace CrushRoyale.Server.Infrastructure;

/// <summary>Current game balance (remote config) and the stage catalog derived from it.</summary>
public interface IBalanceProvider
{
    GameBalance Current { get; }

    StageCatalog Catalog { get; }

    ulong Hash { get; }

    string HashHex { get; }

    string Json { get; }

    Task RefreshAsync(IGameStore store, CancellationToken cancellationToken);

    OperationResult<GameBalance> Parse(string json);

    void Apply(GameBalance balance);
}

/// <summary>
/// Holds an immutable snapshot swapped atomically. Remote updates are validated before use, and a changed hash makes
/// clients re-download the config (replays recorded with another hash are rejected as VersionMismatch, never punished).
/// </summary>
public sealed class BalanceProvider : IBalanceProvider
{
    public const string ConfigKey = "balance";

    private readonly ILogger<BalanceProvider> _logger;
    private volatile Snapshot _snapshot;

    public BalanceProvider(ILogger<BalanceProvider> logger)
    {
        _logger = logger;
        _snapshot = Snapshot.From(GameBalance.CreateDefault());
    }

    public GameBalance Current => _snapshot.Balance;

    public StageCatalog Catalog => _snapshot.Catalog;

    public ulong Hash => _snapshot.Hash;

    public string HashHex => _snapshot.Hash.ToString("x16");

    public string Json => _snapshot.Json;

    public async Task RefreshAsync(IGameStore store, CancellationToken cancellationToken)
    {
        string? json = await store.GetConfigAsync(ConfigKey, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        OperationResult<GameBalance> parsed = Parse(json);
        if (!parsed.Success)
        {
            _logger.LogError("Remote balance rejected, keeping the current one: {Reason}", parsed.Message);
            return;
        }
        if (parsed.Value.ComputeHash() != Hash || Infrastructure.Json.Serialize(parsed.Value) != Json)
        {
            Apply(parsed.Value);
            _logger.LogInformation("Game balance updated, hash {Hash}", HashHex);
        }
    }

    public OperationResult<GameBalance> Parse(string json)
    {
        try
        {
            GameBalance balance = Infrastructure.Json.Deserialize<GameBalance>(json);
            balance.Validate();
            return OperationResult<GameBalance>.Ok(balance);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or ArgumentException or KeyNotFoundException)
        {
            return OperationResult<GameBalance>.Fail(ErrorCode.InvalidArgument, ex.Message);
        }
    }

    public void Apply(GameBalance balance)
    {
        ArgumentNullException.ThrowIfNull(balance);
        balance.Validate();
        _snapshot = Snapshot.From(balance);
    }

    private sealed record Snapshot(GameBalance Balance, StageCatalog Catalog, ulong Hash, string Json)
    {
        public static Snapshot From(GameBalance balance) =>
            new(balance, new StageCatalog(balance), balance.ComputeHash(), Infrastructure.Json.Serialize(balance));
    }
}
