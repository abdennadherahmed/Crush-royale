using CrushRoyale.Core.Pvp;
using CrushRoyale.Server.Infrastructure;

namespace CrushRoyale.Server.Services;

/// <summary>
/// Background loop: matchmaking ticks, ghost pool refresh, remote balance refresh, match expiry and the weekly
/// season close. Each job is isolated so one failure never stops the others.
/// </summary>
public sealed class GameWorker : BackgroundService
{
    private static readonly TimeSpan GhostRefreshEvery = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan BalanceRefreshEvery = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ExpiryEvery = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan SeasonCheckEvery = TimeSpan.FromMinutes(10);

    private readonly PlayerOperations _ops;
    private readonly MatchmakingEngine _engine;
    private readonly GhostPoolCache _ghosts;
    private readonly SeasonJobService _season;
    private readonly GameServerOptions _options;
    private readonly ILogger<GameWorker> _logger;

    public GameWorker(PlayerOperations ops, MatchmakingEngine engine, GhostPoolCache ghosts, SeasonJobService season, GameServerOptions options, ILogger<GameWorker> logger)
    {
        _ops = ops;
        _engine = engine;
        _ghosts = ghosts;
        _season = season;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        DateTime lastGhosts = DateTime.MinValue, lastBalance = DateTime.MinValue, lastExpiry = DateTime.MinValue, lastSeason = DateTime.MinValue;
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(Math.Max(100, _options.MatchmakingTickMs)));

        do
        {
            DateTime now = DateTime.UtcNow;
            Run("matchmaking", () =>
            {
                _engine.ProcessQueue();
                return Task.CompletedTask;
            });

            if (now - lastBalance >= BalanceRefreshEvery)
            {
                lastBalance = now;
                await RunAsync("balance", () => _ops.Balance.RefreshAsync(_ops.Store, stoppingToken)).ConfigureAwait(false);
            }
            if (now - lastGhosts >= GhostRefreshEvery)
            {
                lastGhosts = now;
                await RunAsync("ghosts", () => _ghosts.RefreshAsync(_ops.Store, _ops.Clock, _ops.Balance.Current.Matchmaking.GhostMaxAgeHours, stoppingToken)).ConfigureAwait(false);
            }
            if (now - lastExpiry >= ExpiryEvery)
            {
                lastExpiry = now;
                await RunAsync("expiry", async () =>
                {
                    int expired = await _ops.RunStoreAsync(tx => tx.ExpireOpenMatchesAsync(_ops.Clock.UtcNow.AddMinutes(-_options.MatchExpiryMinutes)), stoppingToken).ConfigureAwait(false);
                    if (expired > 0)
                    {
                        _logger.LogInformation("Expired {Count} abandoned matches", expired);
                    }
                }).ConfigureAwait(false);
            }
            if (now - lastSeason >= SeasonCheckEvery)
            {
                lastSeason = now;
                await RunAsync("season", () => _season.RunIfDueAsync(false, stoppingToken)).ConfigureAwait(false);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private void Run(string job, Func<Task> action) => RunAsync(job, action).GetAwaiter().GetResult();

    private async Task RunAsync(string job, Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background job {Job} failed", job);
        }
    }
}
