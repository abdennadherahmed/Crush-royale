using System.Globalization;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Economy;
using CrushRoyale.Core.Pvp;
using CrushRoyale.Core.Social;
using CrushRoyale.Server.Infrastructure;
using CrushRoyale.Server.Persistence;

namespace CrushRoyale.Server.Services;

/// <summary>
/// Weekly close (Sunday midnight UTC): archive top 100 per league and the guild ranking, pay league / top-rank /
/// guild ranking rewards to players active that week, and reset trophies (soft reset by default).
/// Safe to run on several instances and to re-run after a crash: each player is processed once per season
/// (PlayerTrophyRecord.SeasonWeek) and every credit carries an idempotency key.
/// </summary>
public sealed class SeasonJobService
{
    public const string LastClosedWeekKey = "season.lastClosedWeek";
    private const long AdvisoryLockKey = 0x4352_5345;
    private const int PageSize = 200;

    private readonly PlayerOperations _ops;
    private readonly ILogger<SeasonJobService> _logger;

    public SeasonJobService(PlayerOperations ops, ILogger<SeasonJobService> logger)
    {
        _ops = ops;
        _logger = logger;
    }

    /// <summary>Runs the close of the previous week if not done yet. Returns the number of players processed, or null if nothing was due.</summary>
    public async Task<int?> RunIfDueAsync(bool force, CancellationToken ct)
    {
        int closing = TimeUtil.WeekIndex(_ops.Clock.UtcNow) - 1;
        string? last = await _ops.Store.GetConfigAsync(LastClosedWeekKey, ct).ConfigureAwait(false);
        int? lastClosed = int.TryParse(last?.Trim('"'), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : null;

        if (lastClosed == null && !force)
        {
            // First deployment: start counting from now instead of resetting a week that never existed.
            await SetLastClosedAsync(closing, ct).ConfigureAwait(false);
            return null;
        }
        if (!force && lastClosed >= closing)
        {
            return null;
        }

        GameBalance balance = _ops.Balance.Current;
        _logger.LogInformation("Closing PvP season week {Week}", closing);

        // 1) Top 100 per league (before any reset).
        var rankLookup = new Dictionary<Guid, int>();
        var leagueTops = new Dictionary<League, IReadOnlyList<LeaderboardRow>>();
        foreach (League league in Enum.GetValues<League>())
        {
            IReadOnlyList<LeaderboardRow> rows = await _ops.Store.TopPlayersAsync(league, balance.Trophies.TopRankRewardCount, ct).ConfigureAwait(false);
            leagueTops[league] = rows;
            foreach (LeaderboardRow row in rows)
            {
                rankLookup[row.PlayerId] = row.Rank;
            }
        }

        // 2) Guild ranking with fresh member trophies, rewards and archives.
        Dictionary<Guid, RewardData> guildRewards = await _ops.RunStoreAsync(async tx =>
        {
            var rewards = new Dictionary<Guid, RewardData>();
            if (!await tx.TryAdvisoryLockAsync(AdvisoryLockKey).ConfigureAwait(false))
            {
                throw new ConcurrencyException("Another instance is closing the season.");
            }

            IReadOnlyList<GuildRecord> guilds = await tx.GetAllGuildsAsync().ConfigureAwait(false);
            foreach (GuildRecord record in guilds)
            {
                List<Guid> ids = record.Guild.Members.Select(m => Guid.Parse(m.PlayerId)).ToList();
                Dictionary<string, PlayerSummary> summaries = (await _ops.Store.GetPlayerSummariesAsync(ids, ct).ConfigureAwait(false)).ToDictionary(s => s.Id.ToString());
                foreach (GuildMember member in record.Guild.Members)
                {
                    if (summaries.TryGetValue(member.PlayerId, out PlayerSummary? s))
                    {
                        member.Trophies = s.Trophies;
                    }
                }
                await tx.UpdateGuildAsync(record).ConfigureAwait(false);
            }

            List<GuildRankingEntry> ranking = GuildManager.CalculateGuildRanking(guilds.Select(g => g.Guild));
            List<GuildMemberReward> memberRewards = new GuildManager(balance, _ops.Clock).DistributeRankingRewards(ranking, guilds.ToDictionary(g => g.Guild.Id, g => g.Guild));
            foreach (GuildMemberReward reward in memberRewards)
            {
                rewards[Guid.Parse(reward.PlayerId)] = reward.Reward;
            }

            foreach (KeyValuePair<League, IReadOnlyList<LeaderboardRow>> top in leagueTops)
            {
                await tx.ArchiveSeasonAsync(closing, top.Key, Json.Serialize(top.Value)).ConfigureAwait(false);
            }
            await tx.ArchiveGuildSeasonAsync(closing, Json.Serialize(ranking.Take(100))).ConfigureAwait(false);
            return rewards;
        }, ct).ConfigureAwait(false);

        // 3) Players, page by page.
        int processed = 0;
        Guid? after = null;
        while (true)
        {
            IReadOnlyList<PlayerRecord> page = await _ops.RunStoreAsync(async tx =>
            {
                IReadOnlyList<PlayerRecord> players = await tx.GetPlayersPageAsync(after, PageSize).ConfigureAwait(false);
                foreach (PlayerRecord player in players)
                {
                    if (player.State.Pvp.SeasonWeek > closing)
                    {
                        continue;
                    }
                    var ws = new PlayerWorkspace(player, balance, _ops.Balance.Catalog, _ops.Clock, null);
                    ApplySeasonClose(ws, closing, rankLookup, guildRewards);
                    await ws.FlushAsync(tx).ConfigureAwait(false);
                }
                return players;
            }, ct).ConfigureAwait(false);

            if (page.Count == 0)
            {
                break;
            }
            processed += page.Count;
            after = page[^1].Id;
        }

        await SetLastClosedAsync(closing, ct).ConfigureAwait(false);
        _logger.LogInformation("Season week {Week} closed: {Count} players processed", closing, processed);
        return processed;
    }

    private static void ApplySeasonClose(PlayerWorkspace ws, int closingWeek, IReadOnlyDictionary<Guid, int> ranks, IReadOnlyDictionary<Guid, RewardData> guildRewards)
    {
        PlayerTrophyRecord pvp = ws.State.Pvp;
        TrophyBalance t = ws.Balance.Trophies;
        League league = LeagueTable.GetLeague(pvp.Trophies, t);
        bool active = pvp.History.Any(h => h.Week == closingWeek);
        string key = "season:" + closingWeek;

        if (active)
        {
            ranks.TryGetValue(ws.Id, out int rank);
            long coins = t.SeasonCoinsByLeague[(int)league];
            long orbes = t.SeasonOrbesByLeague[(int)league] + SeasonService.TopRankOrbes(rank, t, t.TopRankRewardCount);
            if (coins > 0)
            {
                ws.Wallet.Credit(Currency.Coins, coins, TransactionReason.SeasonReward, key + ":" + league, key + ":coins").ThrowIfFailed();
            }
            if (orbes > 0)
            {
                ws.Wallet.Credit(Currency.Orbes, orbes, TransactionReason.SeasonReward, key + ":" + league + ":rank" + rank, key + ":orbes").ThrowIfFailed();
            }
        }

        if (guildRewards.TryGetValue(ws.Id, out RewardData? guildReward))
        {
            ws.GrantReward(guildReward, TransactionReason.GuildRankingReward, "guild" + key, "guild" + key);
        }

        pvp.Trophies = SeasonService.ApplyReset(pvp.Trophies, t);
        pvp.CurrentWinStreak = 0;
        pvp.SeasonWeek = closingWeek + 1;
        pvp.SeasonBestTrophies = pvp.Trophies;
    }

    private Task<bool> SetLastClosedAsync(int week, CancellationToken ct) =>
        _ops.RunStoreAsync(async tx =>
        {
            await tx.SetConfigAsync(LastClosedWeekKey, week.ToString(CultureInfo.InvariantCulture), null).ConfigureAwait(false);
            return true;
        }, ct);
}
