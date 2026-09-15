using CrushRoyale.Contracts;
using CrushRoyale.Core.AntiCheat;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Economy;
using CrushRoyale.Server.Infrastructure;
using CrushRoyale.Server.Persistence;

namespace CrushRoyale.Server.Services;

/// <summary>Player reports, the anti-cheat review queue, support grants and live balance updates.</summary>
public sealed class AdminService
{
    private readonly PlayerOperations _ops;
    private readonly ILogger<AdminService> _logger;

    public AdminService(PlayerOperations ops, ILogger<AdminService> logger)
    {
        _ops = ops;
        _logger = logger;
    }

    /// <summary>Prompt endpoint POST /admin/report/cheat: any player can report another; it lands in the review queue.</summary>
    public async Task<bool> ReportAsync(Guid reporterId, ReportCheatRequest request, CancellationToken ct)
    {
        if (!Guid.TryParse(request?.PlayerId, out Guid target) || target == reporterId)
        {
            throw new ApiException(ErrorCode.InvalidArgument, "Invalid reported player.");
        }
        string reason = (request!.Reason ?? string.Empty).Trim();
        if (reason.Length is 0 or > 300)
        {
            throw new ApiException(ErrorCode.InvalidArgument, "Reason must be 1-300 characters.");
        }

        if ((await _ops.Store.GetPlayerSummariesAsync(new[] { target }, ct).ConfigureAwait(false)).Count == 0)
        {
            throw new ApiException(ErrorCode.NotFound);
        }

        await _ops.RunStoreAsync(async tx =>
        {
            await tx.InsertFlagAsync(new CheatFlag
            {
                PlayerId = target.ToString(),
                Reason = FlagReason.PlayerReport,
                Severity = CheatSeverity.Suspicious,
                Details = "Reported by " + reporterId + ": " + reason,
                MatchId = request.MatchId?.Length <= 80 ? request.MatchId : null,
                AtUnixMs = TimeUtil.ToUnixMs(_ops.Clock.UtcNow)
            }).ConfigureAwait(false);
            return true;
        }, ct).ConfigureAwait(false);
        return true;
    }

    public async Task<List<FlagDto>> PendingFlagsAsync(int limit, CancellationToken ct)
    {
        IReadOnlyList<FlagRow> rows = await _ops.Store.PendingFlagsAsync(Math.Clamp(limit, 1, 500), ct).ConfigureAwait(false);
        return rows.Select(f => new FlagDto
        {
            Id = f.Id,
            PlayerId = f.PlayerId.ToString(),
            Reason = f.Reason.ToString(),
            Severity = f.Severity.ToString(),
            Details = f.Details,
            MatchId = f.MatchId,
            CreatedAtUnixMs = TimeUtil.ToUnixMs(f.CreatedAt)
        }).ToList();
    }

    /// <summary>Human decision on a flag. Punishing escalates through the ladder (or bans at once for severe cases).</summary>
    public Task<bool> ReviewAsync(Guid adminId, long flagId, ReviewFlagRequest request, CancellationToken ct) =>
        _ops.RunStoreAsync(async tx =>
        {
            FlagRow flag = await tx.GetFlagAsync(flagId).ConfigureAwait(false) ?? throw new ApiException(ErrorCode.NotFound);
            if (flag.Reviewed)
            {
                throw new ApiException(ErrorCode.AlreadyClaimed, "Flag already reviewed.");
            }

            if (request?.Punish == true)
            {
                PlayerRecord player = await tx.GetPlayerAsync(flag.PlayerId).ConfigureAwait(false) ?? throw new ApiException(ErrorCode.NotFound);
                var ws = new PlayerWorkspace(player, _ops.Balance.Current, _ops.Balance.Catalog, _ops.Clock, null);
                PunishmentDecision decision = ws.AntiCheat.EnforcePunishment(player.State.Integrity, request.Severe);
                if (decision.TrophyResetPermille > 0)
                {
                    player.State.Pvp.Trophies = AntiCheatManager.ApplyTrophyReset(player.State.Pvp.Trophies, decision);
                }
                await ws.FlushAsync(tx).ConfigureAwait(false);
                _logger.LogWarning("Admin {AdminId} punished {PlayerId}: {Action} (offense {Offense})", adminId, flag.PlayerId, decision.Action, decision.OffenseNumber);
            }

            flag.Reviewed = true;
            flag.ReviewOutcome = (request?.Outcome ?? (request?.Punish == true ? "punished" : "dismissed")).Trim();
            flag.ReviewedBy = adminId;
            await tx.UpdateFlagAsync(flag).ConfigureAwait(false);
            return true;
        }, ct);

    public Task<WalletDto> GrantAsync(Guid adminId, AdminGrantRequest request, CancellationToken ct)
    {
        if (!Guid.TryParse(request?.PlayerId, out Guid target))
        {
            throw new ApiException(ErrorCode.InvalidArgument);
        }
        if (request!.Coins < 0 || request.Orbes < 0 || (request.Coins == 0 && request.Orbes == 0) || request.Coins > 1_000_000 || request.Orbes > 100_000)
        {
            throw new ApiException(ErrorCode.InvalidArgument, "Grant amounts out of range.");
        }
        string note = (request.Note ?? string.Empty).Trim();
        if (note.Length is 0 or > 200)
        {
            throw new ApiException(ErrorCode.InvalidArgument, "A note (1-200 chars) is required for the audit trail.");
        }

        string reference = "admin:" + adminId + ":" + note;
        string key = "admin:" + Guid.NewGuid().ToString("N");
        return _ops.RunAsync(target, ctx =>
        {
            if (request.Coins > 0)
            {
                ctx.Player.Wallet.Credit(Currency.Coins, request.Coins, TransactionReason.AdminGrant, reference, key + ":c").ThrowIfFailed();
            }
            if (request.Orbes > 0)
            {
                ctx.Player.Wallet.Credit(Currency.Orbes, request.Orbes, TransactionReason.AdminGrant, reference, key + ":o").ThrowIfFailed();
            }
            _logger.LogWarning("Admin {AdminId} granted {Coins} coins / {Orbes} orbes to {PlayerId}: {Note}", adminId, request.Coins, request.Orbes, target, note);
            return Mappers.Wallet(ctx.Player);
        }, ct, allowBanned: true);
    }

    public async Task<ConfigResponse> UpdateBalanceAsync(Guid adminId, UpdateBalanceRequest request, CancellationToken ct)
    {
        OperationResult<GameBalance> parsed = _ops.Balance.Parse(request?.BalanceJson ?? string.Empty);
        GameBalance balance = parsed.ValueOrThrow();
        string json = Json.Serialize(balance);

        await _ops.RunStoreAsync(async tx =>
        {
            await tx.SetConfigAsync(BalanceProvider.ConfigKey, json, adminId).ConfigureAwait(false);
            return true;
        }, ct).ConfigureAwait(false);

        _ops.Balance.Apply(balance);
        _logger.LogWarning("Admin {AdminId} published a new game balance ({Hash})", adminId, _ops.Balance.HashHex);
        return new ConfigResponse
        {
            RulesVersion = GameBalance.RulesVersion,
            BalanceHash = _ops.Balance.HashHex,
            BalanceJson = _ops.Balance.Json,
            ServerTimeUnixMs = TimeUtil.ToUnixMs(_ops.Clock.UtcNow)
        };
    }
}
