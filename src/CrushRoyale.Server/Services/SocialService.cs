using CrushRoyale.Contracts;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
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

            return new FriendsResponse
            {
                Friends = Resolve(f.Friends).OrderByDescending(p => p.Trophies).ToList(),
                Incoming = Resolve(f.Incoming),
                Outgoing = Resolve(f.Outgoing),
                ChallengeCooldownMs = f.Friends
                    .Select(id => (id, ms: manager.ChallengeCooldownRemainingMs(f, id)))
                    .Where(x => x.ms > 0)
                    .ToDictionary(x => x.id, x => x.ms)
            };
        }, ct);

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
