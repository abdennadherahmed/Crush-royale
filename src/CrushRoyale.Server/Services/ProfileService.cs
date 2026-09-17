using CrushRoyale.Contracts;
using CrushRoyale.Core.AntiCheat;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Economy;
using CrushRoyale.Core.Progression;
using CrushRoyale.Core.Pvp;
using CrushRoyale.Core.Social;
using CrushRoyale.Server.Infrastructure;
using CrushRoyale.Server.Persistence;

namespace CrushRoyale.Server.Services;

/// <summary>Login/registration, hero setup, public profiles and config download.</summary>
public sealed class ProfileService
{
    private static readonly (PowerUpType Type, int Count)[] StarterPowerUps =
    {
        (PowerUpType.ChronoBomb, 3), (PowerUpType.CoinBooster, 2), (PowerUpType.BrightSpark, 2)
    };

    private readonly PlayerOperations _ops;
    private readonly GuildService _guilds;
    private readonly ILogger<ProfileService> _logger;

    public ProfileService(PlayerOperations ops, GuildService guilds, ILogger<ProfileService> logger)
    {
        _ops = ops;
        _guilds = guilds;
        _logger = logger;
    }

    public async Task<LoginResponse> LoginAsync(Guid userId, LoginRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RulesVersion != GameBalance.RulesVersion)
        {
            throw new ApiException(ErrorCode.VersionMismatch, "Please update the game (rules v" + GameBalance.RulesVersion + ").");
        }
        string deviceHash = (request.DeviceHash ?? string.Empty).Trim();
        if (deviceHash.Length is < 16 or > 128)
        {
            throw new ApiException(ErrorCode.InvalidArgument, "deviceHash must be 16-128 characters.");
        }

        bool isNew = await _ops.RunStoreAsync(async tx =>
        {
            if (await tx.GetPlayerAsync(userId, forUpdate: false).ConfigureAwait(false) != null)
            {
                return false;
            }
            (PlayerRecord record, List<LedgerEntry> ledger) = CreatePlayer(userId, request);
            await tx.InsertPlayerAsync(record).ConfigureAwait(false);
            await tx.AppendLedgerAsync(userId, ledger).ConfigureAwait(false);
            return true;
        }, ct).ConfigureAwait(false);

        if (isNew)
        {
            _logger.LogInformation("New player {PlayerId}", userId);
        }

        return await _ops.RunAsync(userId, async ctx =>
        {
            PlayerWorkspace ws = ctx.Player;
            IReadOnlyList<Guid> accounts = await ctx.Tx.RegisterDeviceAsync(deviceHash, userId).ConfigureAwait(false);
            if (accounts.Count > ws.Balance.AntiCheat.MaxAccountsPerDevice)
            {
                ws.AntiCheat.FlagSuspiciousActivity(ws.IdString, FlagReason.MultiAccountDevice, CheatSeverity.Suspicious, accounts.Count + " accounts on this device");
            }

            if (!string.IsNullOrWhiteSpace(request.Region) && request.Region.Length <= 16)
            {
                ws.State.Region = request.Region.Trim();
            }
            if (!string.IsNullOrWhiteSpace(request.Language) && request.Language.Length <= 8)
            {
                ws.State.Language = request.Language.Trim();
            }

            ws.EnsureDailyState();
            ws.Stamina.Refresh();

            return new LoginResponse
            {
                Profile = Mappers.Profile(ws),
                IsNewPlayer = isNew,
                ConfigOutdated = !string.Equals(request.BalanceHash, _ops.Balance.HashHex, StringComparison.OrdinalIgnoreCase),
                BalanceHash = _ops.Balance.HashHex,
                RulesVersion = GameBalance.RulesVersion,
                ServerTimeUnixMs = ws.NowMs
            };
        }, ct, allowBanned: true).ConfigureAwait(false);
    }

    public Task<ProfileDto> GetMeAsync(Guid userId, CancellationToken ct) =>
        _ops.ReadAsync(userId, (ws, _) => Task.FromResult(Mappers.Profile(ws)), ct);

    /// <summary>
    /// Permanent account deletion requested by the player (Google Play / GDPR). The guild is left first so the other
    /// members keep a consistent roster and leadership; then the account and its data are erased in the store.
    /// </summary>
    public async Task<bool> DeleteAccountAsync(Guid userId, CancellationToken ct)
    {
        bool inGuild = await _ops.ReadAsync(userId, (ws, _) => Task.FromResult(ws.State.GuildId != null), ct).ConfigureAwait(false);
        if (inGuild)
        {
            try
            {
                await _guilds.LeaveAsync(userId, ct).ConfigureAwait(false);
            }
            catch (ApiException ex) when (ex.Code is ErrorCode.NotMember or ErrorCode.NotFound)
            {
                // Guild already gone: nothing to leave.
            }
        }

        await _ops.RunStoreAsync(async tx =>
        {
            await tx.DeletePlayerAccountAsync(userId).ConfigureAwait(false);
            return true;
        }, ct).ConfigureAwait(false);

        _logger.LogInformation("Player {PlayerId} deleted their account", userId);
        return true;
    }

    public Task<ProfileDto> SetHeroAsync(Guid userId, SetHeroRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        string gender = (request.Gender ?? string.Empty).Trim().ToLowerInvariant();
        if (gender is not ("male" or "female"))
        {
            throw new ApiException(ErrorCode.InvalidArgument, "gender must be male or female.");
        }
        string heroName = (request.HeroName ?? string.Empty).Trim();
        if (heroName.Length is < 1 or > 16 || !heroName.All(c => char.IsLetter(c) || c is ' ' or '-' or '\'') || ChatModerator.ContainsProfanity(heroName))
        {
            throw new ApiException(ErrorCode.NameInvalid, "Invalid hero name.");
        }
        string? displayName = request.DisplayName?.Trim();
        if (displayName != null && (displayName.Length is < 3 or > 20 || !displayName.All(c => char.IsLetterOrDigit(c) || c is ' ' or '_' or '-') || ChatModerator.ContainsProfanity(displayName)))
        {
            throw new ApiException(ErrorCode.NameInvalid, "Invalid display name.");
        }
        if (request.Age is < 4 or > 120)
        {
            throw new ApiException(ErrorCode.InvalidArgument, "Invalid age.");
        }
        if (request.Appearance is < 0 or > 64)
        {
            throw new ApiException(ErrorCode.InvalidArgument, "Invalid appearance.");
        }

        return _ops.RunAsync(userId, ctx =>
        {
            PlayerState s = ctx.Player.State;
            s.Hero = new HeroProfile { Gender = gender, Name = heroName, Appearance = request.Appearance };
            s.HeroChosen = true;
            if (displayName != null)
            {
                s.DisplayName = displayName;
            }
            if (request.Age.HasValue)
            {
                // Declared once: changing it later would let minors bypass the spending cap.
                if (s.DeclaredAge.HasValue && s.DeclaredAge != request.Age)
                {
                    throw new ApiException(ErrorCode.PermissionDenied, "Age can only be declared once. Contact support to change it.");
                }
                s.DeclaredAge = request.Age;
            }
            if (!string.IsNullOrWhiteSpace(request.Language) && request.Language.Length <= 8)
            {
                s.Language = request.Language.Trim();
            }
            return Mappers.Profile(ctx.Player);
        }, ct);
    }

    public async Task<PlayerStatsDto> GetStatsAsync(Guid playerId, CancellationToken ct)
    {
        PlayerStatsDto? stats = await _ops.Store.TransactAsync(async tx =>
        {
            PlayerRecord? record = await tx.GetPlayerAsync(playerId, forUpdate: false).ConfigureAwait(false);
            if (record == null)
            {
                return null;
            }
            PlayerState s = record.State;
            (int Coins, int PassXp, int Counted) bonus = CosmeticBonuses.Total(s.Inventory.Cosmetics);
            return new PlayerStatsDto
            {
                Profile = new PublicProfileDto
                {
                    Id = record.Id.ToString(),
                    DisplayName = s.DisplayName,
                    Trophies = s.Pvp.Trophies,
                    League = LeagueTable.GetLeague(s.Pvp.Trophies, _ops.Balance.Current.Trophies).ToString(),
                    HighestStage = s.Story.HighestUnlockedStage,
                    GuildId = s.GuildId,
                    Frame = s.Inventory.EquippedFrame,
                    Title = s.Inventory.EquippedTitle
                },
                PvpWins = s.Pvp.Wins,
                PvpLosses = s.Pvp.Losses,
                BestWinStreak = s.Pvp.BestWinStreak,
                HighestLeague = s.Pvp.HighestLeague.ToString(),
                TotalStars = s.Story.TotalStars,
                AchievementsUnlocked = s.Achievements.UnlockedAt.Count,
                HeroGender = s.Hero?.Gender,
                VipTier = (int)s.Vip.Tier,
                Outfit = s.Inventory.EquippedOutfit,
                BoardSkin = s.Inventory.EquippedBoardSkin,
                PieceSkin = s.Inventory.EquippedPieceSkin,
                Cosmetics = s.Inventory.Cosmetics.OrderBy(c => c, StringComparer.Ordinal).ToList(),
                CosmeticsCounted = bonus.Counted,
                CosmeticCoinBonusPermille = bonus.Coins,
                CosmeticPassXpBonusPermille = bonus.PassXp,
                Pet = s.Pets == null || s.Pets.Equipped == Core.Config.PetType.None ? null : s.Pets.Equipped.ToString(),
                PetLevel = s.Pets != null && s.Pets.Pets.TryGetValue(s.Pets.Equipped, out Core.Pets.PetState pet) ? pet.Level : 0,
                PetsOwned = s.Pets == null ? 0 : s.Pets.Pets.Values.Count(p => p.Owned),
                CollectionPages = s.Achievements.PagesCompleted.Count
            };
        }, ct).ConfigureAwait(false);

        if (stats == null)
        {
            throw new ApiException(ErrorCode.NotFound);
        }
        stats.WeeklyRank = await _ops.Store.PlayerRankAsync(playerId, null, ct).ConfigureAwait(false);
        return stats;
    }

    public async Task<SearchPlayersResponse> SearchAsync(string? query, CancellationToken ct)
    {
        string q = (query ?? string.Empty).Trim();
        if (q.Length < 2)
        {
            throw new ApiException(ErrorCode.InvalidArgument, "Type at least 2 characters.");
        }

        // Exact id lookup first ("add friends by ID").
        if (Guid.TryParse(q, out Guid id))
        {
            IReadOnlyList<PlayerSummary> byId = await _ops.Store.GetPlayerSummariesAsync(new[] { id }, ct).ConfigureAwait(false);
            return new SearchPlayersResponse { Players = byId.Select(Mappers.Public).ToList() };
        }

        IReadOnlyList<PlayerSummary> rows = await _ops.Store.SearchPlayersAsync(q, 20, ct).ConfigureAwait(false);
        return new SearchPlayersResponse { Players = rows.Select(Mappers.Public).ToList() };
    }

    public ConfigResponse GetConfig() => new()
    {
        RulesVersion = GameBalance.RulesVersion,
        BalanceHash = _ops.Balance.HashHex,
        BalanceJson = _ops.Balance.Json,
        ServerTimeUnixMs = TimeUtil.ToUnixMs(_ops.Clock.UtcNow)
    };

    private (PlayerRecord Record, List<LedgerEntry> Ledger) CreatePlayer(Guid userId, LoginRequest request)
    {
        GameBalance balance = _ops.Balance.Current;
        var state = new PlayerState
        {
            DisplayName = "Hero" + (Math.Abs(BitConverter.ToInt32(userId.ToByteArray(), 0)) % 10000).ToString("D4"),
            Region = (request.Region ?? string.Empty).Trim(),
            Language = string.IsNullOrWhiteSpace(request.Language) ? "en" : request.Language.Trim()
        };

        var ledger = new List<LedgerEntry>();
        var wallet = new Wallet(state.Wallet, _ops.Clock, PlayerWorkspace.WalletLedgerKept);
        wallet.Transaction += ledger.Add;
        if (balance.Economy.StartingCoins > 0)
        {
            wallet.Credit(Currency.Coins, balance.Economy.StartingCoins, TransactionReason.StartingGrant, "welcome", "starting-grant:coins").ThrowIfFailed();
        }
        if (balance.Economy.StartingOrbes > 0)
        {
            wallet.Credit(Currency.Orbes, balance.Economy.StartingOrbes, TransactionReason.StartingGrant, "welcome", "starting-grant:orbes").ThrowIfFailed();
        }

        var inventory = new Inventory(state.Inventory);
        foreach ((PowerUpType type, int count) in StarterPowerUps)
        {
            inventory.Add(type, count);
        }

        _ = new StaminaManager(balance, state.Stamina, _ops.Clock);
        state.Pvp.PlayerId = userId.ToString();
        state.Integrity.PlayerId = userId.ToString();
        state.Vip.PlayerId = userId.ToString();
        state.LastSeenUnixMs = TimeUtil.ToUnixMs(_ops.Clock.UtcNow);

        return (new PlayerRecord { Id = userId, CreatedAt = _ops.Clock.UtcNow, State = state }, ledger);
    }
}
