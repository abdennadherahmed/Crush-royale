using System.Collections.Concurrent;
using CrushRoyale.Contracts;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Economy;
using CrushRoyale.Core.AntiCheat;
using CrushRoyale.Core.Gameplay;
using CrushRoyale.Core.Progression;
using CrushRoyale.Core.Pvp;
using CrushRoyale.Core.Replay;
using CrushRoyale.Core.Social;
using CrushRoyale.Core.Story;
using CrushRoyale.Server.Infrastructure;
using CrushRoyale.Server.Persistence;

namespace CrushRoyale.Server.Services;

/// <summary>Recent ranked ghosts kept in memory for the matchmaking engine (refreshed from the database).</summary>
public sealed class GhostPoolCache : IGhostPool
{
    private volatile List<GhostCandidate> _ghosts = new();
    private readonly object _writeLock = new();

    public int Count => _ghosts.Count;

    public IEnumerable<GhostCandidate> FindGhosts(int minTrophies, int maxTrophies, long recordedAfterMs) =>
        _ghosts.Where(g => g.Trophies >= minTrophies && g.Trophies <= maxTrophies && g.RecordedAtMs >= recordedAfterMs);

    public void Add(GhostCandidate ghost)
    {
        lock (_writeLock)
        {
            _ghosts = new List<GhostCandidate>(_ghosts) { ghost };
        }
    }

    public async Task RefreshAsync(IGameStore store, IClock clock, int maxAgeHours, CancellationToken cancellationToken)
    {
        IReadOnlyList<GhostRow> rows = await store.FindGhostsAsync(0, int.MaxValue, clock.UtcNow.AddHours(-maxAgeHours), 5000, cancellationToken).ConfigureAwait(false);
        var list = rows.Select(r => new GhostCandidate
        {
            ReplayId = r.ReplayId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            PlayerId = r.PlayerId.ToString(),
            Trophies = r.Trophies,
            RecordedAtMs = TimeUtil.ToUnixMs(r.CreatedAt),
            Region = r.Region,
            Seed = r.Seed
        }).ToList();
        lock (_writeLock)
        {
            _ghosts = list;
        }
    }
}

/// <summary>
/// Ranked PvP (ghost play). A match row per player; results are computed from validated replays:
/// vs a recorded ghost (only the challenger's trophies move, the ghost owner earns defense coins),
/// vs a live opponent (both settled when the second replay arrives), or by forfeit when the opponent never submits.
/// Friendly challenges reuse the same pipeline without trophies.
/// </summary>
public sealed class PvpService
{
    private readonly PlayerOperations _ops;
    private readonly MatchmakingEngine _engine;
    private readonly GhostPoolCache _ghosts;
    private readonly GameServerOptions _options;
    private readonly ConcurrentDictionary<Guid, PendingRequest> _pending = new();
    private readonly ConcurrentDictionary<Guid, StartedMatch> _started = new();

    public PvpService(PlayerOperations ops, MatchmakingEngine engine, GhostPoolCache ghosts, GameServerOptions options, SocialService social, GuildService guilds)
    {
        _ops = ops;
        _engine = engine;
        _ghosts = ghosts;
        _options = options;
        _social = social;
        _guilds = guilds;
    }

    private readonly GuildService _guilds;

    /// <summary>An arena win counts for the guild's weekly race, the same way a cleared stage does.</summary>
    private Task AwardGuildAsync(OperationContext ctx, PvpResultDto dto) =>
        dto.Outcome == nameof(MatchOutcome.Win)
            ? _guilds.AwardTournamentPointsAsync(ctx, GuildTournament.ForArenaWin(ctx.Player.Balance.Guild.Tournament))
            : Task.CompletedTask;

    private readonly SocialService _social;

    private TimeSpan Expiry => TimeSpan.FromMinutes(_options.MatchExpiryMinutes);

    // ------------------------------------------------------------------ matchmaking

    public Task<MatchmakingStatusResponse> RequestAsync(Guid userId, MatchmakingRequest? request, CancellationToken ct) =>
        _ops.ReadAsync(userId, (ws, tx) =>
        {
            if (ws.IsBanned)
            {
                throw new ApiException(ErrorCode.Banned);
            }
            if (!ws.Story.IsFeatureUnlocked(Feature.Pvp))
            {
                throw new ApiException(ErrorCode.FeatureLocked, "PvP unlocks after stage " + ws.Balance.Story.UnlockPvpStage + ".");
            }
            if (ws.AntiCheat.IsInBoostingCooldown(ws.State.Integrity))
            {
                throw new ApiException(ErrorCode.CooldownActive, "Please take a short break before your next ranked match.");
            }

            List<LoadoutEntry> loadout = ws.Balance.Pvp.PowerUpsAllowed
                ? ws.Inventory.BuildLoadout(Mappers.ParseLoadout(request?.Loadout, ws.Balance), ws.Balance, ws.State.Pvp.HighestLeague).ValueOrThrow()
                : new List<LoadoutEntry>();

            _started.TryRemove(userId, out _);
            _pending[userId] = new PendingRequest(loadout, ws.State.Pvp.HighestLeague, ws.Now);
            MatchmakingTicket ticket = _engine.RequestMatch(ws.IdString, ws.State.Pvp.Trophies, ws.State.Pvp.CurrentWinStreak, ws.State.Region);
            return Task.FromResult(Status(ticket));
        }, ct);

    public bool Cancel(Guid userId)
    {
        _pending.TryRemove(userId, out _);
        return _engine.CancelMatchRequest(userId.ToString());
    }

    public async Task<MatchmakingStatusResponse> StatusAsync(Guid userId, CancellationToken ct)
    {
        if (_started.TryGetValue(userId, out StartedMatch? started) && _ops.Clock.UtcNow - started.At < TimeSpan.FromMinutes(2))
        {
            return new MatchmakingStatusResponse { Status = "Matched", Match = started.Response };
        }

        MatchmakingTicket? ticket = _engine.GetTicket(userId.ToString());
        if (ticket == null)
        {
            return new MatchmakingStatusResponse { Status = "None" };
        }
        if (ticket.Status == TicketStatus.Waiting)
        {
            return Status(ticket);
        }
        if (ticket.Status != TicketStatus.Matched || !_pending.TryGetValue(userId, out PendingRequest? pending))
        {
            _engine.Acknowledge(userId.ToString());
            _pending.TryRemove(userId, out _);
            return new MatchmakingStatusResponse { Status = ticket.Status == TicketStatus.Matched ? TicketStatus.Cancelled.ToString() : ticket.Status.ToString() };
        }

        Guid opponentId = Guid.Parse(ticket.OpponentId);
        IReadOnlyList<PlayerSummary> opponent = await _ops.Store.GetPlayerSummariesAsync(new[] { opponentId }, ct).ConfigureAwait(false);

        MatchStartResponse response = await _ops.RunAsync(userId, async ctx =>
        {
            PlayerWorkspace ws = ctx.Player;
            string rowId = ticket.MatchId + "-" + userId.ToString("N");
            MatchRow? existing = await ctx.Tx.GetMatchAsync(rowId).ConfigureAwait(false);
            if (existing != null)
            {
                return Mappers.MatchStart(existing, ws, _ops.Balance.HashHex, await GhostForAsync(ctx.Tx, existing, ct).ConfigureAwait(false));
            }

            int opponentTrophies = opponent.FirstOrDefault()?.Trophies ?? ws.State.Pvp.Trophies;
            long? ghostReplayId = null;
            if (ticket.VsGhost)
            {
                ghostReplayId = long.Parse(ticket.GhostReplayId, System.Globalization.CultureInfo.InvariantCulture);
                ReplayRow ghost = await ctx.Tx.GetReplayAsync(ghostReplayId.Value).ConfigureAwait(false) ?? throw new ApiException(ErrorCode.NotFound, "Ghost no longer available.");
                opponentTrophies = ghost.Trophies;
            }

            var match = new MatchRow
            {
                Id = rowId,
                PlayerId = userId,
                Mode = GameMode.PvpRanked,
                Seed = ticket.Seed,
                Status = MatchStatus.Started,
                StartedAt = ws.Now,
                OpponentId = opponentId,
                GhostReplayId = ghostReplayId,
                Config = new MatchConfigSnapshot
                {
                    Loadout = pending.Loadout,
                    HighestLeague = pending.HighestLeague,
                    PlayerTrophies = ws.State.Pvp.Trophies,
                    OpponentTrophies = opponentTrophies,
                    Ranked = true
                }
            };
            ws.SnapshotPet(match.Config, match.Mode);
            await ctx.Tx.InsertMatchAsync(match).ConfigureAwait(false);
            return Mappers.MatchStart(match, ws, _ops.Balance.HashHex, await GhostForAsync(ctx.Tx, match, ct).ConfigureAwait(false));
        }, ct).ConfigureAwait(false);

        _engine.Acknowledge(userId.ToString());
        _pending.TryRemove(userId, out _);
        _started[userId] = new StartedMatch(response, _ops.Clock.UtcNow);
        return new MatchmakingStatusResponse { Status = "Matched", Match = response };
    }

    // ------------------------------------------------------------------ submission & results

    /// <summary>Prompt endpoint POST /pvp/match/record: validates, stores the ghost, then settles the result.</summary>
    public async Task<PvpResultDto> RecordAsync(Guid userId, SubmitReplayRequest request, CancellationToken ct)
    {
        ReplayData replay = Mappers.DecodeReplay(request?.ReplayBase64);
        byte[] bytes = Convert.FromBase64String(request!.ReplayBase64);

        Submission submission = await _ops.RunAsync(userId, async ctx =>
        {
            PlayerWorkspace ws = ctx.Player;
            MatchRow? match = string.IsNullOrWhiteSpace(request.MatchId) ? null : await ctx.Tx.GetMatchAsync(request.MatchId).ConfigureAwait(false);
            if (match == null || match.PlayerId != userId || (match.Mode != GameMode.PvpRanked && match.Mode != GameMode.FriendlyChallenge))
            {
                throw new ApiException(ErrorCode.NotFound, "Match not found.");
            }
            if (match.Status != MatchStatus.Started)
            {
                return new Submission(match.Id, match.Status == MatchStatus.Completed ? null : Rejected(ws, "SessionOver"), null);
            }

            ErrorCode header = ReplayGuard.CheckHeader(replay, match, userId);
            if (header != ErrorCode.None)
            {
                ReplayGuard.FlagHeaderMismatch(ws, match, header);
                return new Submission(match.Id, await RejectAsync(ctx, match, header).ConfigureAwait(false), null);
            }

            SessionConfig config = SessionConfig.ForPvp(match.Seed, ws.Balance, match.Mode, match.Config.Loadout, match.Config.HighestLeague)
                .WithPet(match.Config.Pet, match.Config.PetLevel, ws.Balance);
            bool timingOk = ws.AntiCheat.ValidateTimestamps(ws.State.Integrity, replay, TimeUtil.ToUnixMs(match.StartedAt), ws.NowMs, match.Id);
            ReplayVerification verification = ws.AntiCheat.ValidateReplay(ws.State.Integrity, replay, config, match.Id);
            if (!verification.Valid || !timingOk)
            {
                return new Submission(match.Id, await RejectAsync(ctx, match, verification.Valid ? ErrorCode.ReplayMismatch : verification.Error).ConfigureAwait(false), null);
            }

            StageResult result = verification.Session.GetResult();
            var used = new Dictionary<PowerUpType, int>(result.PowerUpsUsed);
            if (match.Config.StolenPowerUp is PowerUpType stolen && used.TryGetValue(stolen, out int n))
            {
                if (n <= 1)
                {
                    used.Remove(stolen);
                }
                else
                {
                    used[stolen] = n - 1;
                }
            }
            ReplayGuard.ConsumePowerUps(ws, used, match.Id);
            ws.Achievements.RecordStageResult(result, null, null);
            ws.TrackQuest(QuestType.PlayPvp, 1);
            ws.TrackQuest(QuestType.TriggerCascades, result.TotalCascades);
            ws.TrackQuest(QuestType.UsePowerUps, used.Values.Sum());

            bool ranked = match.Mode == GameMode.PvpRanked;
            long replayId = await ctx.Tx.InsertReplayAsync(new ReplayRow
            {
                PlayerId = userId,
                Mode = match.Mode,
                Seed = match.Seed,
                Trophies = match.Config.PlayerTrophies,
                FinalScore = result.FinalScore,
                Region = ws.State.Region,
                Data = bytes,
                IsGhost = ranked,
                CreatedAt = ws.Now
            }).ConfigureAwait(false);

            match.Status = MatchStatus.Completed;
            match.ReplayId = replayId;
            match.FinishedAt = ws.Now;
            await ctx.Tx.UpdateMatchAsync(match).ConfigureAwait(false);

            GhostCandidate? ghost = ranked
                ? new GhostCandidate { ReplayId = replayId.ToString(System.Globalization.CultureInfo.InvariantCulture), PlayerId = ws.IdString, Trophies = match.Config.PlayerTrophies, RecordedAtMs = ws.NowMs, Region = ws.State.Region, Seed = match.Seed }
                : null;
            return new Submission(match.Id, null, ghost, match.Config.DuelId, result.FinalScore, replayId);
        }, ct).ConfigureAwait(false);

        if (submission.Rejection != null)
        {
            return submission.Rejection;
        }
        if (!string.IsNullOrEmpty(submission.DuelId))
        {
            // Friend duel: both entries get the score, and the duel settles as soon as the second player is done.
            await _social.SettleDuelAsync(userId, submission.DuelId!, submission.Score, submission.ReplayId, ct).ConfigureAwait(false);
        }
        if (submission.Ghost != null)
        {
            _ghosts.Add(submission.Ghost);
        }
        return await SettleAsync(userId, submission.MatchId, ct).ConfigureAwait(false);
    }

    public async Task<MatchInfoResponse> GetMatchAsync(Guid userId, string matchId, CancellationToken ct)
    {
        (MatchRow match, GhostDto? ghost) = await _ops.Store.TransactAsync(async tx =>
        {
            MatchRow? m = await tx.GetMatchAsync(matchId, forUpdate: false).ConfigureAwait(false);
            if (m == null || m.PlayerId != userId)
            {
                throw new ApiException(ErrorCode.NotFound);
            }
            return (m, await GhostForAsync(tx, m, ct).ConfigureAwait(false));
        }, ct).ConfigureAwait(false);

        PvpResultDto? result = match.ResultJson != null
            ? Json.Deserialize<PvpResultDto>(match.ResultJson)
            : match.Status == MatchStatus.Completed ? await SettleAsync(userId, match.Id, ct).ConfigureAwait(false) : null;

        return new MatchInfoResponse
        {
            MatchId = match.Id,
            Mode = match.Mode.ToString(),
            Status = match.Status.ToString(),
            Seed = match.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture),
            Ghost = ghost,
            Result = result
        };
    }

    public async Task<LeaderboardResponse> WeeklyLeaderboardAsync(Guid userId, string? league, int limit, CancellationToken ct)
    {
        League? filter = string.IsNullOrWhiteSpace(league) ? null : Mappers.ParseEnum<League>(league, "league");
        limit = Math.Clamp(limit <= 0 ? 100 : limit, 1, 100);
        IReadOnlyList<LeaderboardRow> rows = await _ops.Store.TopPlayersAsync(filter, limit, ct).ConfigureAwait(false);
        int? rank = await _ops.Store.PlayerRankAsync(userId, filter, ct).ConfigureAwait(false);
        DateTime now = _ops.Clock.UtcNow;
        return new LeaderboardResponse
        {
            League = filter?.ToString() ?? "All",
            Week = TimeUtil.WeekIndex(now),
            ResetAtUnixMs = TimeUtil.ToUnixMs(TimeUtil.NextWeeklyReset(now)),
            Entries = rows.Select(r => new LeaderboardEntryDto { Rank = r.Rank, PlayerId = r.PlayerId.ToString(), DisplayName = r.DisplayName, Trophies = r.Trophies, League = r.League.ToString() }).ToList(),
            MyRank = rank
        };
    }

    public async Task<GuildLeaderboardResponse> GuildLeaderboardAsync(Guid userId, CancellationToken ct)
    {
        IReadOnlyList<GuildRankingRow> rows = await _ops.Store.TopGuildsAsync(100, ct).ConfigureAwait(false);
        long? myGuild = (await _ops.Store.GetPlayerSummariesAsync(new[] { userId }, ct).ConfigureAwait(false)).FirstOrDefault()?.GuildId;
        return new GuildLeaderboardResponse
        {
            Entries = rows.Select(r => new GuildRankDto { Rank = r.Rank, GuildId = r.GuildId, Name = r.Name, TotalTrophies = r.TotalTrophies, Members = r.Members, Level = r.Level }).ToList(),
            MyGuildRank = rows.FirstOrDefault(r => r.GuildId == myGuild)?.Rank,
            ResetAtUnixMs = TimeUtil.ToUnixMs(TimeUtil.NextWeeklyReset(_ops.Clock.UtcNow))
        };
    }

    /// <summary>Ghost payload for a replay row (opponent name, league, loadout, bytes).</summary>
    public static async Task<GhostDto> BuildGhostAsync(IGameStore store, GameBalance balance, ReplayRow row, CancellationToken ct)
    {
        ReplayData replay = ReplaySerializer.Deserialize(row.Data);
        PlayerSummary? owner = (await store.GetPlayerSummariesAsync(new[] { row.PlayerId }, ct).ConfigureAwait(false)).FirstOrDefault();
        return new GhostDto
        {
            ReplayBase64 = Convert.ToBase64String(row.Data),
            OpponentId = row.PlayerId.ToString(),
            OpponentName = owner?.DisplayName ?? "Unknown hero",
            OpponentTrophies = row.Trophies,
            OpponentLeague = LeagueTable.GetLeague(row.Trophies, balance.Trophies).ToString(),
            OpponentFrame = owner?.Frame,
            OpponentTitle = owner?.Title,
            OpponentLoadout = Mappers.Loadout(replay.Loadout)
        };
    }

    // ------------------------------------------------------------------ settlement

    private async Task<PvpResultDto> SettleAsync(Guid userId, string matchId, CancellationToken ct)
    {
        MatchRow my = await _ops.Store.TransactAsync(tx => tx.GetMatchAsync(matchId, forUpdate: false), ct).ConfigureAwait(false)
            ?? throw new ApiException(ErrorCode.NotFound);
        if (my.ResultJson != null)
        {
            return Json.Deserialize<PvpResultDto>(my.ResultJson);
        }
        if (my.Status != MatchStatus.Completed || my.ReplayId == null)
        {
            return new PvpResultDto { Accepted = true, Outcome = "Pending" };
        }

        if (my.GhostReplayId is long ghostReplayId && my.OpponentId is Guid ghostOwner)
        {
            bool ownerExists = (await _ops.Store.GetPlayerSummariesAsync(new[] { ghostOwner }, ct).ConfigureAwait(false)).Count > 0;
            return ownerExists && ghostOwner != userId
                ? await _ops.RunPairAsync(userId, ghostOwner, (me, them) => SettleVsGhostAsync(me, them, matchId, ghostReplayId), ct).ConfigureAwait(false)
                : await _ops.RunAsync(userId, ctx => SettleVsGhostAsync(ctx, null, matchId, ghostReplayId), ct).ConfigureAwait(false);
        }

        if (my.OpponentId is Guid opponentId)
        {
            string opponentRowId = my.Id[..my.Id.LastIndexOf('-')] + "-" + opponentId.ToString("N");
            MatchRow? opponentMatch = await _ops.Store.TransactAsync(tx => tx.GetMatchAsync(opponentRowId, forUpdate: false), ct).ConfigureAwait(false);
            if (opponentMatch is { Status: MatchStatus.Completed, ReplayId: not null })
            {
                return await _ops.RunPairAsync(userId, opponentId, (me, them) => SettleLiveAsync(me, them, matchId, opponentRowId), ct).ConfigureAwait(false);
            }

            DateTime now = _ops.Clock.UtcNow;
            bool forfeit = opponentMatch == null
                ? my.StartedAt < now - Expiry
                : opponentMatch.Status is MatchStatus.Expired or MatchStatus.Rejected || (opponentMatch.Status == MatchStatus.Started && opponentMatch.StartedAt < now - Expiry);
            if (forfeit)
            {
                return await _ops.RunAsync(userId, ctx => SettleForfeitAsync(ctx, matchId), ct).ConfigureAwait(false);
            }
            return new PvpResultDto { Accepted = true, Outcome = "Pending" };
        }

        throw new ApiException(ErrorCode.InvalidArgument, "Match has no opponent.");
    }

    private async Task<PvpResultDto> SettleVsGhostAsync(OperationContext me, OperationContext? owner, string matchId, long ghostReplayId)
    {
        MatchRow match = (await me.Tx.GetMatchAsync(matchId).ConfigureAwait(false))!;
        if (match.ResultJson != null)
        {
            return Json.Deserialize<PvpResultDto>(match.ResultJson);
        }

        ReplayData mine = ReplaySerializer.Deserialize((await me.Tx.GetReplayAsync(match.ReplayId!.Value).ConfigureAwait(false))!.Data);
        ReplayRow? ghostRow = await me.Tx.GetReplayAsync(ghostReplayId).ConfigureAwait(false);
        if (ghostRow == null)
        {
            return await SettleForfeitAsync(me, matchId).ConfigureAwait(false);
        }

        PvpMatchResult result = PvpGhostPlay.ComputeResult(mine, ReplaySerializer.Deserialize(ghostRow.Data), me.Player.Balance);
        bool ranked = match.Mode == GameMode.PvpRanked && match.Config.Ranked;
        PvpResultDto dto = ApplyOutcome(me, match, result.ChallengerOutcome, result.ChallengerScore, result.OpponentScore, result.ChallengerFrozenPoints, result.OpponentFrozenPoints, ranked);
        await AwardGuildAsync(me, dto).ConfigureAwait(false);

        if (owner != null && ranked && result.ChallengerOutcome == MatchOutcome.Loss)
        {
            owner.Player.CreditEarnedCoins(owner.Player.Balance.Pvp.GhostDefenseCoins, TransactionReason.GhostDefense, match.Id, match.Id + ":defense");
        }

        match.ResultJson = Json.Serialize(dto);
        await me.Tx.UpdateMatchAsync(match).ConfigureAwait(false);
        return dto;
    }

    private async Task<PvpResultDto> SettleLiveAsync(OperationContext me, OperationContext them, string matchId, string opponentRowId)
    {
        MatchRow mine = (await me.Tx.GetMatchAsync(matchId).ConfigureAwait(false))!;
        MatchRow theirs = (await me.Tx.GetMatchAsync(opponentRowId).ConfigureAwait(false))!;
        if (mine.ResultJson != null)
        {
            return Json.Deserialize<PvpResultDto>(mine.ResultJson);
        }

        ReplayData myReplay = ReplaySerializer.Deserialize((await me.Tx.GetReplayAsync(mine.ReplayId!.Value).ConfigureAwait(false))!.Data);
        ReplayData theirReplay = ReplaySerializer.Deserialize((await me.Tx.GetReplayAsync(theirs.ReplayId!.Value).ConfigureAwait(false))!.Data);
        PvpMatchResult result = PvpGhostPlay.ComputeResult(myReplay, theirReplay, me.Player.Balance);

        PvpResultDto dto = ApplyOutcome(me, mine, result.ChallengerOutcome, result.ChallengerScore, result.OpponentScore, result.ChallengerFrozenPoints, result.OpponentFrozenPoints, mine.Config.Ranked);
        await AwardGuildAsync(me, dto).ConfigureAwait(false);
        mine.ResultJson = Json.Serialize(dto);
        await me.Tx.UpdateMatchAsync(mine).ConfigureAwait(false);

        if (theirs.ResultJson == null)
        {
            MatchOutcome flipped = result.ChallengerOutcome switch
            {
                MatchOutcome.Win => MatchOutcome.Loss,
                MatchOutcome.Loss => MatchOutcome.Win,
                _ => MatchOutcome.Draw
            };
            PvpResultDto theirDto = ApplyOutcome(them, theirs, flipped, result.OpponentScore, result.ChallengerScore, result.OpponentFrozenPoints, result.ChallengerFrozenPoints, theirs.Config.Ranked);
            await AwardGuildAsync(them, theirDto).ConfigureAwait(false);
            theirs.ResultJson = Json.Serialize(theirDto);
            await me.Tx.UpdateMatchAsync(theirs).ConfigureAwait(false);
        }
        return dto;
    }

    private async Task<PvpResultDto> SettleForfeitAsync(OperationContext me, string matchId)
    {
        MatchRow match = (await me.Tx.GetMatchAsync(matchId).ConfigureAwait(false))!;
        if (match.ResultJson != null)
        {
            return Json.Deserialize<PvpResultDto>(match.ResultJson);
        }
        ReplayData mine = ReplaySerializer.Deserialize((await me.Tx.GetReplayAsync(match.ReplayId!.Value).ConfigureAwait(false))!.Data);
        PvpResultDto dto = ApplyOutcome(me, match, MatchOutcome.Win, mine.FinalScore, 0, 0, 0, match.Config.Ranked);
        await AwardGuildAsync(me, dto).ConfigureAwait(false);
        match.ResultJson = Json.Serialize(dto);
        await me.Tx.UpdateMatchAsync(match).ConfigureAwait(false);
        return dto;
    }

    private static PvpResultDto ApplyOutcome(OperationContext ctx, MatchRow match, MatchOutcome outcome, long score, long opponentScore, long frozen, long opponentFrozen, bool ranked)
    {
        PlayerWorkspace ws = ctx.Player;
        GameBalance balance = ws.Balance;
        var dto = new PvpResultDto
        {
            Accepted = true,
            Outcome = outcome.ToString(),
            Score = score,
            OpponentScore = opponentScore,
            FrozenPoints = frozen,
            OpponentFrozenPoints = opponentFrozen,
            Ranked = ranked
        };

        if (ranked && !ws.AntiCheat.AreTrophiesFrozen(ws.State.Integrity))
        {
            TrophyChange change = new TrophySystem(balance, ws.Clock).RecordMatch(ws.State.Pvp, match.OpponentId?.ToString() ?? string.Empty, match.Config.OpponentTrophies, outcome, match.Id);
            dto.TrophyDelta = change.Delta;
            dto.Promoted = change.Promoted;
            dto.Demoted = change.Demoted;
            dto.FirstReachOrbes = change.FirstReachOrbes;
            if (change.FirstReachOrbes > 0)
            {
                ws.Wallet.Credit(Currency.Orbes, change.FirstReachOrbes, TransactionReason.RankUpBonus, match.Id, match.Id + ":rankup").ThrowIfFailed();
            }

            ws.AntiCheat.AnalyzeMatch(ws.State.Integrity, new MatchSummary
            {
                MatchId = match.Id,
                OpponentId = match.OpponentId?.ToString(),
                Won = outcome == MatchOutcome.Win,
                TrophyDelta = change.Delta,
                Score = score,
                AtUnixMs = ws.NowMs
            });
        }

        if (ranked && outcome == MatchOutcome.Win)
        {
            var chestRng = new DeterministicRandom(StableHash.Fnv1a(match.Id));
            dto.ChestEarned = ws.GrantChest(ws.Chests.GrantVictoryChest(chestRng), "pvp:" + match.Id);
        }

        long baseCoins = PvpRewardCalculator.BaseCoins(balance.Pvp, outcome);
        dto.CoinsEarned = ws.CreditEarnedCoins(ranked ? baseCoins : baseCoins / 2, TransactionReason.PvpReward, match.Id, match.Id + ":coins");

        ws.Achievements.RecordPvpResult(null, outcome == MatchOutcome.Win, ws.State.Pvp.Trophies, ws.State.Pvp.HighestLeague, ws.State.Pvp.BestWinStreak);
        if (outcome == MatchOutcome.Win)
        {
            ws.TrackQuest(QuestType.WinPvp, 1);
        }
        ws.AddBattlePassXp(outcome == MatchOutcome.Win ? balance.LiveOps.XpPvpWin : balance.LiveOps.XpPvpLoss);
        ws.AwardPetXp(match.Config, match.Mode, outcome == MatchOutcome.Win);

        dto.ShowInterstitial = AdPolicy.OnEvent(ws.State.Ads, AdPlacement.PvpBattle, ws.Now, ws.State.Story.HighestUnlockedStage,
            ws.State.Inventory.AdsRemoved, new VipSystem(balance).GetBenefit(ws.State.Vip.Tier), balance);
        dto.Trophies = ws.State.Pvp.Trophies;
        dto.League = LeagueTable.GetLeague(ws.State.Pvp.Trophies, balance.Trophies).ToString();
        dto.AchievementsUnlocked = ws.UnlockedAchievements.ToList();
        dto.Wallet = Mappers.Wallet(ws);
        return dto;
    }

    private async Task<GhostDto?> GhostForAsync(IStoreTransaction tx, MatchRow match, CancellationToken ct)
    {
        long? replayId = match.GhostReplayId;
        if (replayId == null && match.OpponentId is Guid opponent && match.Id.Contains('-'))
        {
            MatchRow? opponentMatch = await tx.GetMatchAsync(match.Id[..match.Id.LastIndexOf('-')] + "-" + opponent.ToString("N"), forUpdate: false).ConfigureAwait(false);
            replayId = opponentMatch?.Status == MatchStatus.Completed ? opponentMatch.ReplayId : null;
        }
        if (replayId == null)
        {
            return null;
        }
        ReplayRow? row = await tx.GetReplayAsync(replayId.Value).ConfigureAwait(false);
        return row == null ? null : await BuildGhostAsync(_ops.Store, _ops.Balance.Current, row, ct).ConfigureAwait(false);
    }

    private static async Task<PvpResultDto> RejectAsync(OperationContext ctx, MatchRow match, ErrorCode error)
    {
        match.Status = MatchStatus.Rejected;
        match.FinishedAt = ctx.Player.Now;
        match.ResultJson = Json.Serialize(new PvpResultDto { Accepted = false, Error = error.ToString() });
        await ctx.Tx.UpdateMatchAsync(match).ConfigureAwait(false);
        return Rejected(ctx.Player, error.ToString());
    }

    private static PvpResultDto Rejected(PlayerWorkspace ws, string error) => new() { Accepted = false, Error = error, Wallet = Mappers.Wallet(ws) };

    private MatchmakingStatusResponse Status(MatchmakingTicket ticket) => new()
    {
        Status = ticket.Status.ToString(),
        WaitedMs = (int)Math.Max(0, TimeUtil.ToUnixMs(_ops.Clock.UtcNow) - ticket.EnqueuedAtMs),
        Range = ticket.CurrentRange
    };

    private sealed record PendingRequest(List<LoadoutEntry> Loadout, League HighestLeague, DateTime At);

    private sealed record StartedMatch(MatchStartResponse Response, DateTime At);

    private sealed record Submission(string MatchId, PvpResultDto? Rejection, GhostCandidate? Ghost, string? DuelId = null, long Score = 0, long ReplayId = 0);
}
