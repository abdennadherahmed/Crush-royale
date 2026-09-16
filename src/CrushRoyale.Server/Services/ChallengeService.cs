using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using CrushRoyale.Contracts;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Gameplay;
using CrushRoyale.Server.Infrastructure;
using CrushRoyale.Server.Persistence;

namespace CrushRoyale.Server.Services;

/// <summary>
/// "Challenge me" links: a player shares a code pointing at their latest duel replay; anyone who opens it plays the
/// same board against that replay (friendly, no trophies), no friendship needed. Codes are stateless: replay id plus
/// a check value derived from the replay owner and seed, so ids cannot be enumerated into valid codes.
/// </summary>
public sealed class ChallengeService
{
    public const string ShareBaseUrl = "https://crushroyale-legal.onrender.com/challenge.html?c=";

    private readonly PlayerOperations _ops;

    public ChallengeService(PlayerOperations ops)
    {
        _ops = ops;
    }

    public Task<ChallengeCreateResponse> CreateAsync(Guid userId, CancellationToken ct) =>
        _ops.RunAsync(userId, async ctx =>
        {
            ReplayRow replay = await ctx.Tx.LatestChallengeReplayAsync(userId).ConfigureAwait(false)
                ?? throw new ApiException(ErrorCode.NotFound, "Play an online duel first.");
            string code = Encode(replay.Id, replay.PlayerId, replay.Seed);
            return new ChallengeCreateResponse
            {
                Code = code,
                Url = ShareBaseUrl + code,
                Score = replay.FinalScore,
                DisplayName = ctx.Player.State.DisplayName
            };
        }, ct);

    public Task<MatchStartResponse> StartAsync(Guid userId, string code, StartStageRequest? request, CancellationToken ct) =>
        _ops.RunAsync(userId, async ctx =>
        {
            PlayerWorkspace ws = ctx.Player;
            long replayId = DecodeId(code);
            ReplayRow ghost = await ctx.Tx.GetReplayAsync(replayId).ConfigureAwait(false) ?? throw new ApiException(ErrorCode.NotFound, "Challenge not found.");
            if (!string.Equals(Encode(ghost.Id, ghost.PlayerId, ghost.Seed), code.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                throw new ApiException(ErrorCode.NotFound, "Challenge not found.");
            }
            if (ghost.PlayerId == userId)
            {
                throw new ApiException(ErrorCode.InvalidArgument, "You cannot accept your own challenge.");
            }

            League league = ws.State.Pvp.HighestLeague;
            List<LoadoutEntry> loadout = ws.Inventory.BuildLoadout(Mappers.ParseLoadout(request?.Loadout, ws.Balance), ws.Balance, league).ValueOrThrow();
            var match = new MatchRow
            {
                Id = Mappers.NewId("ch"),
                PlayerId = userId,
                Mode = GameMode.FriendlyChallenge,
                Seed = ghost.Seed,
                Status = MatchStatus.Started,
                StartedAt = ws.Now,
                OpponentId = ghost.PlayerId,
                GhostReplayId = ghost.Id,
                Config = new MatchConfigSnapshot
                {
                    Loadout = loadout,
                    HighestLeague = league,
                    PlayerTrophies = ws.State.Pvp.Trophies,
                    OpponentTrophies = ghost.Trophies,
                    Ranked = false
                }
            };
            ws.SnapshotPet(match.Config, GameMode.FriendlyChallenge);
            await ctx.Tx.InsertMatchAsync(match).ConfigureAwait(false);
            GhostDto ghostDto = await PvpService.BuildGhostAsync(_ops.Store, ws.Balance, ghost, ct).ConfigureAwait(false);
            return Mappers.MatchStart(match, ws, _ops.Balance.HashHex, ghostDto);
        }, ct);

    /// <summary>CR-{replay id in base 36}-{5 hex check}.</summary>
    public static string Encode(long replayId, Guid owner, ulong seed)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes("crush-challenge:" + replayId.ToString(CultureInfo.InvariantCulture) + ":" + owner.ToString("N") + ":" + seed.ToString(CultureInfo.InvariantCulture)));
        return "CR-" + ToBase36(replayId) + "-" + Convert.ToHexString(hash)[..5];
    }

    private static long DecodeId(string? code)
    {
        string[] parts = (code ?? string.Empty).Trim().ToUpperInvariant().Split('-');
        if (parts.Length != 3 || parts[0] != "CR" || parts[1].Length is < 1 or > 12 || parts[2].Length != 5)
        {
            throw new ApiException(ErrorCode.InvalidArgument, "Invalid challenge code.");
        }
        long value = 0;
        foreach (char c in parts[1])
        {
            int digit = c is >= '0' and <= '9' ? c - '0' : c is >= 'A' and <= 'Z' ? c - 'A' + 10 : -1;
            if (digit < 0)
            {
                throw new ApiException(ErrorCode.InvalidArgument, "Invalid challenge code.");
            }
            value = checked(value * 36 + digit);
        }
        return value;
    }

    private static string ToBase36(long value)
    {
        const string digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        if (value <= 0)
        {
            return "0";
        }
        var sb = new StringBuilder();
        while (value > 0)
        {
            sb.Insert(0, digits[(int)(value % 36)]);
            value /= 36;
        }
        return sb.ToString();
    }
}
