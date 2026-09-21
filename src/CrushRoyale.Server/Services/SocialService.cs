using CrushRoyale.Contracts;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Economy;
using CrushRoyale.Core.Gameplay;
using CrushRoyale.Core.PowerUps;
using CrushRoyale.Core.Progression;
using CrushRoyale.Core.Replay;
using CrushRoyale.Core.Social;
using CrushRoyale.Core.Story;
using CrushRoyale.Server.Infrastructure;
using CrushRoyale.Server.Persistence;

namespace CrushRoyale.Server.Services;

/// <summary>Friends, friendly challenges (vs the friend's latest ghost, no trophies) and the world map.</summary>
public sealed class SocialService
{
    private readonly PlayerOperations _ops;

    public SocialService(PlayerOperations ops)
    {
        _ops = ops;
    }

    public Task<FriendsResponse> GetFriendsAsync(Guid userId, CancellationToken ct) =>
        _ops.ReadAsync(userId, async (ws, _) =>
        {
            FriendsState f = ws.State.Friends;
            List<Guid> ids = f.Friends.Concat(f.Incoming).Concat(f.Outgoing)
                .Select(s => Guid.TryParse(s, out Guid g) ? g : Guid.Empty)
                .Where(g => g != Guid.Empty)
                .Distinct()
                .ToList();
            Dictionary<string, PublicProfileDto> profiles = (await _ops.Store.GetPlayerSummariesAsync(ids, ct).ConfigureAwait(false))
                .ToDictionary(p => p.Id.ToString(), Mappers.Public);

            var manager = new FriendsManager(ws.Balance, ws.Clock);
            List<PublicProfileDto> Resolve(IEnumerable<string> list) => list.Where(profiles.ContainsKey).Select(id => profiles[id]).ToList();

            // Life gift senders may not be in the lists above (a friend removed meanwhile): resolve them separately.
            IReadOnlyList<PlayerSummary> giftSummaries = await _ops.Store.GetPlayerSummariesAsync(
                f.LifeGifts.Select(s => Guid.TryParse(s, out Guid g) ? g : Guid.Empty).Where(g => g != Guid.Empty).ToList(), ct).ConfigureAwait(false);
            int today = TimeUtil.DayIndex(ws.Clock.UtcNow);

            return new FriendsResponse
            {
                Friends = Resolve(f.Friends).OrderByDescending(p => p.Trophies).ToList(),
                Incoming = Resolve(f.Incoming),
                Outgoing = Resolve(f.Outgoing),
                Suggestions = await SuggestionsAsync(ws, f, ct).ConfigureAwait(false),
                LifeGifts = giftSummaries.Select(Mappers.Public).ToList(),
                Duels = f.Duels.Select(Mappers.Duel).ToList(),
                CanSendLife = FriendsManager.CanSendLife(f, today),
                CanAcceptLife = FriendsManager.CanAcceptLife(f, today, ws.Stamina.Lives, ws.Balance.Stamina.MaxRegenLives),
                Lives = Mappers.Lives(ws),
                ChallengeCooldownMs = f.Friends
                    .Select(id => (id, ms: manager.ChallengeCooldownRemainingMs(f, id)))
                    .Where(x => x.ms > 0)
                    .ToDictionary(x => x.id, x => x.ms)
            };
        }, ct);

    /// <summary>
    /// Players to invite when the friend list is empty or short: the most active players of the moment, minus
    /// everyone already connected to this player. Shuffled per player and per day so everyone is not invited at once.
    /// </summary>
    private async Task<List<PublicProfileDto>> SuggestionsAsync(PlayerWorkspace ws, FriendsState f, CancellationToken ct)
    {
        var known = new HashSet<string>(f.Friends.Concat(f.Incoming).Concat(f.Outgoing).Concat(f.Blocked)) { ws.IdString };
        IReadOnlyList<LeaderboardRow> active = await _ops.Store.TopPlayersAsync(null, 60, ct).ConfigureAwait(false);
        List<LeaderboardRow> pool = active.Where(r => !known.Contains(r.PlayerId.ToString())).ToList();
        if (pool.Count == 0)
        {
            return new List<PublicProfileDto>();
        }
        var rng = new DeterministicRandom(StableHash.Fnv1a(ws.IdString), (ulong)TimeUtil.DayIndex(ws.Clock.UtcNow));
        rng.Shuffle(pool);
        List<Guid> ids = pool.Take(8).Select(r => r.PlayerId).ToList();
        IReadOnlyList<PlayerSummary> summaries = await _ops.Store.GetPlayerSummariesAsync(ids, ct).ConfigureAwait(false);
        return summaries.Select(Mappers.Public).ToList();
    }

    // ------------------------------------------------------------------ duels

    /// <summary>
    /// Opens a real duel: both friends play the SAME board and the scores decide. The challenger plays first; the
    /// friend then gets an invitation with the score to beat and the challenger's run as a live ghost.
    /// </summary>
    public Task<MatchStartResponse> StartDuelAsync(Guid userId, string friendId, StartStageRequest? request, CancellationToken ct)
    {
        Guid friend = ParsePlayer(friendId);
        return _ops.RunPairAsync(userId, friend, async (me, them) =>
        {
            PlayerWorkspace ws = me.Player;
            RequireFriendsUnlocked(ws);
            var manager = new FriendsManager(ws.Balance, ws.Clock);
            ErrorCode canChallenge = manager.CanChallenge(ws.State.Friends, friend.ToString());
            if (canChallenge != ErrorCode.None)
            {
                throw new ApiException(canChallenge);
            }
            manager.RecordChallenge(ws.State.Friends, friend.ToString()).ThrowIfFailed();

            string duelId = Mappers.NewId("duel");
            ulong seed = StableHash.Mix(StableHash.Fnv1a(duelId), (ulong)ws.NowMs);
            manager.StartDuel(ws.State.Friends, ws.State.DisplayName, them.Player.State.Friends, them.Player.State.DisplayName,
                ws.IdString, them.Player.IdString, duelId, seed, ws.NowMs).ThrowIfFailed();

            MatchRow match = await CreateDuelMatchAsync(me, seed, friend, duelId, request, null).ConfigureAwait(false);
            return Mappers.MatchStart(match, ws, _ops.Balance.HashHex, null);
        }, ct);
    }

    /// <summary>Accepts an invitation: same seed, and the challenger's run plays as the ghost bar.</summary>
    public Task<MatchStartResponse> AcceptDuelAsync(Guid userId, DuelRequest request, CancellationToken ct)
    {
        string duelId = request?.DuelId ?? throw new ApiException(ErrorCode.InvalidArgument, "Duel id missing.");
        return _ops.RunAsync(userId, async ctx =>
        {
            PlayerWorkspace ws = ctx.Player;
            RequireFriendsUnlocked(ws);
            FriendDuel duel = FriendsManager.Find(ws.State.Friends, duelId)
                ?? throw new ApiException(ErrorCode.NotFound, "Duel not found.");
            if (duel.State != DuelState.Invited)
            {
                throw new ApiException(ErrorCode.SessionOver, "This duel is not waiting for you.");
            }

            ReplayRow? ghost = duel.OpponentReplayId > 0 ? await ctx.Tx.GetReplayAsync(duel.OpponentReplayId).ConfigureAwait(false) : null;
            MatchRow match = await CreateDuelMatchAsync(ctx, duel.Seed, ParsePlayer(duel.OpponentId), duelId, null, ghost).ConfigureAwait(false);
            GhostDto? ghostDto = ghost != null ? await PvpService.BuildGhostAsync(_ops.Store, ws.Balance, ghost, ct).ConfigureAwait(false) : null;
            return Mappers.MatchStart(match, ws, _ops.Balance.HashHex, ghostDto);
        }, ct);
    }

    /// <summary>Turns down an invitation; the challenger sees "declined" instead of waiting.</summary>
    public async Task<FriendsResponse> DeclineDuelAsync(Guid userId, DuelRequest request, CancellationToken ct)
    {
        string duelId = request?.DuelId ?? throw new ApiException(ErrorCode.InvalidArgument, "Duel id missing.");
        Guid opponent = await _ops.ReadAsync(userId, (ws, _) =>
        {
            FriendDuel duel = FriendsManager.Find(ws.State.Friends, duelId) ?? throw new ApiException(ErrorCode.NotFound, "Duel not found.");
            return Task.FromResult(ParsePlayer(duel.OpponentId));
        }, ct).ConfigureAwait(false);

        await _ops.RunPairAsync(userId, opponent, (me, them) =>
        {
            var manager = new FriendsManager(me.Player.Balance, me.Player.Clock);
            manager.DeclineDuel(me.Player.State.Friends, them.Player.State.Friends, duelId, me.Player.NowMs).ThrowIfFailed();
            return Task.FromResult(true);
        }, ct).ConfigureAwait(false);
        return await GetFriendsAsync(userId, ct).ConfigureAwait(false);
    }

    /// <summary>Clears a finished duel from the list once the player has read the result.</summary>
    public async Task<FriendsResponse> DismissDuelAsync(Guid userId, DuelRequest request, CancellationToken ct)
    {
        string duelId = request?.DuelId ?? throw new ApiException(ErrorCode.InvalidArgument, "Duel id missing.");
        await _ops.RunAsync(userId, ctx =>
        {
            var manager = new FriendsManager(ctx.Player.Balance, ctx.Player.Clock);
            if (!manager.DismissDuel(ctx.Player.State.Friends, duelId))
            {
                throw new ApiException(ErrorCode.NotFound, "Duel not found.");
            }
            return Task.FromResult(true);
        }, ct).ConfigureAwait(false);
        return await GetFriendsAsync(userId, ct).ConfigureAwait(false);
    }

    /// <summary>The duel match itself: friendly mode, fixed seed, the duel id carried in the match config.</summary>
    private async Task<MatchRow> CreateDuelMatchAsync(OperationContext ctx, ulong seed, Guid opponent, string duelId, StartStageRequest? request, ReplayRow? ghost)
    {
        PlayerWorkspace ws = ctx.Player;
        League league = ws.State.Pvp.HighestLeague;
        List<LoadoutEntry> loadout = ws.Inventory.BuildLoadout(Mappers.ParseLoadout(request?.Loadout, ws.Balance), ws.Balance, league).ValueOrThrow();
        ws.Achievements.IncrementStat(StatKey.FriendlyChallenges, 1);
        var match = new MatchRow
        {
            Id = Mappers.NewId("duel"),
            PlayerId = ws.Id,
            Mode = GameMode.FriendlyChallenge,
            Seed = seed,
            Status = MatchStatus.Started,
            StartedAt = ws.Now,
            OpponentId = opponent,
            GhostReplayId = ghost?.Id,
            Config = new MatchConfigSnapshot
            {
                Loadout = loadout,
                HighestLeague = league,
                PlayerTrophies = ws.State.Pvp.Trophies,
                OpponentTrophies = ghost?.Trophies ?? ws.State.Pvp.Trophies,
                Ranked = false,
                DuelId = duelId
            }
        };
        ws.SnapshotPet(match.Config, GameMode.FriendlyChallenge);
        await ctx.Tx.InsertMatchAsync(match).ConfigureAwait(false);
        return match;
    }

    /// <summary>
    /// Called after a duel run was validated: stores the score on both sides and settles the duel when both have
    /// played. Runs in its own pair transaction, after the replay transaction has committed.
    /// </summary>
    public async Task SettleDuelAsync(Guid userId, string duelId, long score, long replayId, CancellationToken ct)
    {
        Guid opponent = await _ops.ReadAsync(userId, (ws, _) =>
        {
            FriendDuel duel = FriendsManager.Find(ws.State.Friends, duelId);
            return Task.FromResult(duel == null ? Guid.Empty : ParsePlayer(duel.OpponentId));
        }, ct).ConfigureAwait(false);
        if (opponent == Guid.Empty)
        {
            return;
        }

        await _ops.RunPairAsync(userId, opponent, (me, them) =>
        {
            var manager = new FriendsManager(me.Player.Balance, me.Player.Clock);
            DuelOutcome outcome = manager.RecordDuelRun(me.Player.State.Friends, them.Player.State.Friends, duelId, score, replayId, me.Player.NowMs);
            if (outcome == DuelOutcome.Won)
            {
                me.Player.Wallet.Credit(Currency.Coins, me.Player.Balance.Economy.FriendlyDuelWinCoins, TransactionReason.PvpReward, duelId);
            }
            else if (outcome == DuelOutcome.Lost)
            {
                them.Player.Wallet.Credit(Currency.Coins, me.Player.Balance.Economy.FriendlyDuelWinCoins, TransactionReason.PvpReward, duelId);
            }
            return Task.FromResult(true);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>Offers one of your lives to a friend (once per day, and only one waiting gift per friend).</summary>
    public async Task<FriendsResponse> SendLifeAsync(Guid userId, FriendTargetRequest request, CancellationToken ct)
    {
        Guid target = ParsePlayer(request?.PlayerId);
        await _ops.RunPairAsync(userId, target, (me, them) =>
        {
            RequireFriendsUnlocked(me.Player);
            var manager = new FriendsManager(me.Player.Balance, me.Player.Clock);
            manager.SendLife(me.Player.IdString, me.Player.State.Friends, them.Player.IdString, them.Player.State.Friends,
                TimeUtil.DayIndex(me.Player.Clock.UtcNow)).ThrowIfFailed();
            return Task.FromResult(true);
        }, ct).ConfigureAwait(false);
        return await GetFriendsAsync(userId, ct).ConfigureAwait(false);
    }

    /// <summary>Accepts one waiting life. Refused when already full, so a gift is never wasted.</summary>
    public async Task<FriendsResponse> AcceptLifeAsync(Guid userId, CancellationToken ct)
    {
        await _ops.RunAsync(userId, ctx =>
        {
            PlayerWorkspace ws = ctx.Player;
            RequireFriendsUnlocked(ws);
            var manager = new FriendsManager(ws.Balance, ws.Clock);
            manager.AcceptLife(ws.State.Friends, TimeUtil.DayIndex(ws.Clock.UtcNow), ws.Stamina.Lives, ws.Balance.Stamina.MaxRegenLives).ThrowIfFailed();
            ws.Stamina.AddLives(1);
            return Task.FromResult(true);
        }, ct).ConfigureAwait(false);
        return await GetFriendsAsync(userId, ct).ConfigureAwait(false);
    }

    public async Task<FriendsResponse> ActAsync(Guid userId, string action, FriendTargetRequest request, CancellationToken ct)
    {
        Guid target = ParsePlayer(request?.PlayerId);
        await _ops.RunPairAsync(userId, target, (me, them) =>
        {
            RequireFriendsUnlocked(me.Player);
            var manager = new FriendsManager(me.Player.Balance, me.Player.Clock);
            FriendsState mine = me.Player.State.Friends;
            FriendsState theirs = them.Player.State.Friends;
            OperationResult result = action switch
            {
                "request" => manager.SendRequest(me.Player.IdString, mine, them.Player.IdString, theirs),
                "accept" => manager.Accept(me.Player.IdString, mine, them.Player.IdString, theirs),
                "decline" => manager.Decline(me.Player.IdString, mine, them.Player.IdString, theirs),
                "remove" => manager.Remove(me.Player.IdString, mine, them.Player.IdString, theirs),
                "block" => manager.Block(me.Player.IdString, mine, them.Player.IdString, theirs),
                _ => OperationResult.Fail(ErrorCode.InvalidArgument)
            };
            result.ThrowIfFailed();
            me.Player.Achievements.SetStatMax(StatKey.FriendsCount, mine.Friends.Count);
            them.Player.Achievements.SetStatMax(StatKey.FriendsCount, theirs.Friends.Count);
            return Task.FromResult(true);
        }, ct).ConfigureAwait(false);
        return await GetFriendsAsync(userId, ct).ConfigureAwait(false);
    }

    public Task<MatchStartResponse> ChallengeAsync(Guid userId, string friendId, StartStageRequest? request, CancellationToken ct)
    {
        Guid friend = ParsePlayer(friendId);
        return _ops.RunAsync(userId, async ctx =>
        {
            PlayerWorkspace ws = ctx.Player;
            RequireFriendsUnlocked(ws);
            var manager = new FriendsManager(ws.Balance, ws.Clock);
            manager.RecordChallenge(ws.State.Friends, friend.ToString()).ThrowIfFailed();

            ReplayRow ghost = await ctx.Tx.LatestGhostOfPlayerAsync(friend).ConfigureAwait(false)
                ?? throw new ApiException(ErrorCode.NotFound, "Your friend has not played a ranked match yet.");
            ReplayData ghostReplay = ReplaySerializer.Deserialize(ghost.Data);

            League league = ws.State.Pvp.HighestLeague;
            List<LoadoutEntry> loadout = ws.Inventory.BuildLoadout(Mappers.ParseLoadout(request?.Loadout, ws.Balance), ws.Balance, league).ValueOrThrow();

            PowerUpType? stolen = null;
            ulong rollSeed = StableHash.Mix(ghost.Seed, StableHash.Fnv1a(ws.IdString), (ulong)ws.NowMs);
            if (loadout.Count < ws.Balance.PowerUps.LoadoutSlots && FriendsManager.RollPowerUpSteal(ws.Balance, ws.State.Vip.Tier, GameMode.FriendlyChallenge, rollSeed))
            {
                stolen = ghostReplay.Loadout
                    .Select(l => (PowerUpType?)l.Type)
                    .FirstOrDefault(t => loadout.All(x => x.Type != t) && PowerUpManager.IsUnlocked(ws.Balance, t!.Value, league));
                if (stolen.HasValue)
                {
                    loadout.Add(new LoadoutEntry(stolen.Value, 1));
                }
            }

            ws.Achievements.IncrementStat(StatKey.FriendlyChallenges, 1);
            var match = new MatchRow
            {
                Id = Mappers.NewId("fc"),
                PlayerId = userId,
                Mode = GameMode.FriendlyChallenge,
                Seed = ghost.Seed,
                Status = MatchStatus.Started,
                StartedAt = ws.Now,
                OpponentId = friend,
                GhostReplayId = ghost.Id,
                Config = new MatchConfigSnapshot
                {
                    Loadout = loadout,
                    HighestLeague = league,
                    PlayerTrophies = ws.State.Pvp.Trophies,
                    OpponentTrophies = ghost.Trophies,
                    Ranked = false,
                    StolenPowerUp = stolen
                }
            };
            ws.SnapshotPet(match.Config, GameMode.FriendlyChallenge);
            await ctx.Tx.InsertMatchAsync(match).ConfigureAwait(false);
            GhostDto ghostDto = await PvpService.BuildGhostAsync(_ops.Store, ws.Balance, ghost, ct).ConfigureAwait(false);
            return Mappers.MatchStart(match, ws, _ops.Balance.HashHex, ghostDto);
        }, ct);
    }

    public Task<WorldMapResponse> WorldMapAsync(Guid userId, CancellationToken ct) =>
        _ops.ReadAsync(userId, async (ws, _) =>
        {
            List<Guid> ids = ws.State.Friends.Friends.Select(s => Guid.TryParse(s, out Guid g) ? g : Guid.Empty).Where(g => g != Guid.Empty).ToList();
            IReadOnlyList<PlayerSummary> friends = await _ops.Store.GetPlayerSummariesAsync(ids, ct).ConfigureAwait(false);
            return new WorldMapResponse
            {
                MyStage = ws.State.Story.HighestUnlockedStage,
                Friends = friends.Select(f => new WorldMapFriendDto { PlayerId = f.Id.ToString(), DisplayName = f.DisplayName, Stage = f.HighestStage, Frame = f.Frame }).ToList()
            };
        }, ct);

    private static void RequireFriendsUnlocked(PlayerWorkspace ws)
    {
        if (!ws.Story.IsFeatureUnlocked(Feature.Friends))
        {
            throw new ApiException(ErrorCode.FeatureLocked, "Friends unlock after stage " + ws.Balance.Story.UnlockFriendsStage + ".");
        }
    }

    private static Guid ParsePlayer(string? id) =>
        Guid.TryParse(id, out Guid g) ? g : throw new ApiException(ErrorCode.InvalidArgument, "Invalid player id.");
}
