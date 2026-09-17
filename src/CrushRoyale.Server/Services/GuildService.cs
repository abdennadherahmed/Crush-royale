using CrushRoyale.Contracts;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Economy;
using CrushRoyale.Core.Gameplay;
using CrushRoyale.Core.Progression;
using CrushRoyale.Core.Replay;
using CrushRoyale.Core.Social;
using CrushRoyale.Server.Infrastructure;
using CrushRoyale.Server.Persistence;

namespace CrushRoyale.Server.Services;

/// <summary>Guild membership, levels and tech, weekly boss (replay-validated attacks) and moderated chat.</summary>
public sealed class GuildService
{
    private readonly PlayerOperations _ops;
    private readonly ChatModerator _chat;

    public GuildService(PlayerOperations ops, ChatModerator chat)
    {
        _ops = ops;
        _chat = chat;
    }

    private int Week => TimeUtil.WeekIndex(_ops.Clock.UtcNow);

    public Task<GuildDto> CreateAsync(Guid userId, CreateGuildRequest request, CancellationToken ct) =>
        _ops.RunAsync(userId, async ctx =>
        {
            PlayerWorkspace ws = ctx.Player;
            var manager = new GuildManager(ws.Balance, ws.Clock);
            Guild guild = manager.CreateGuild("pending", request?.Name ?? string.Empty, ws.IdString, ws.State.DisplayName, ws.State.Pvp.Trophies,
                ws.Wallet, ws.State.Story.HighestUnlockedStage, ws.State.GuildId != null).ValueOrThrow();
            ApplySettings(guild, request?.Description, request?.IsOpen ?? true, request?.MinTrophies ?? 0);

            var record = new GuildRecord { Guild = guild, CreatedAt = ws.Now };
            long id = await ctx.Tx.InsertGuildAsync(record).ConfigureAwait(false) ?? throw new ApiException(ErrorCode.NameTaken);
            ws.State.GuildId = id;
            ws.Achievements.IncrementStat(StatKey.GuildsJoined, 1);
            return await ToDtoAsync(record, ws, ct).ConfigureAwait(false);
        }, ct);

    /// <summary>The player's guild. Also rolls the weekly boss, refreshes member stats and delivers pending boss rewards.</summary>
    public Task<GuildDto> GetMineAsync(Guid userId, CancellationToken ct) =>
        _ops.RunAsync(userId, async ctx =>
        {
            GuildRecord record = await LockMyGuildAsync(ctx).ConfigureAwait(false);
            new GuildManager(ctx.Player.Balance, ctx.Player.Clock).EnsureBossWeek(record.Guild, Week);
            ClaimPendingRewards(ctx.Player, record);
            await RefreshMembersAsync(record, ct).ConfigureAwait(false);
            await ctx.Tx.UpdateGuildAsync(record).ConfigureAwait(false);
            return await ToDtoAsync(record, ctx.Player, ct).ConfigureAwait(false);
        }, ct);

    public async Task<GuildDto> GetAsync(Guid userId, long guildId, CancellationToken ct)
    {
        GuildRecord record = await _ops.Store.TransactAsync(tx => tx.GetGuildAsync(guildId, forUpdate: false), ct).ConfigureAwait(false)
            ?? throw new ApiException(ErrorCode.NotFound);
        return await _ops.ReadAsync(userId, (ws, _) => ToDtoAsync(record, ws, ct), ct).ConfigureAwait(false);
    }

    public async Task<GuildSearchResponse> SearchAsync(string? query, CancellationToken ct)
    {
        string q = (query ?? string.Empty).Trim();
        IReadOnlyList<GuildSummary> rows;
        if (q.Length == 0)
        {
            IReadOnlyList<GuildRankingRow> top = await _ops.Store.TopGuildsAsync(50, ct).ConfigureAwait(false);
            rows = await _ops.Store.SearchGuildsAsync(string.Empty, 50, ct).ConfigureAwait(false);
            rows = rows.OrderBy(r => top.FirstOrDefault(t => t.GuildId == r.Id)?.Rank ?? int.MaxValue).ToList();
        }
        else
        {
            rows = await _ops.Store.SearchGuildsAsync(q, 30, ct).ConfigureAwait(false);
        }

        return new GuildSearchResponse
        {
            Guilds = rows.Where(r => r.Members > 0).Select(r => new GuildSummaryDto
            {
                Id = r.Id, Name = r.Name, Level = r.Level, Members = r.Members, TotalTrophies = r.TotalTrophies, IsOpen = r.IsOpen, MinTrophies = r.MinTrophies
            }).ToList()
        };
    }

    public Task<GuildDto> JoinAsync(Guid userId, long guildId, CancellationToken ct) =>
        _ops.RunAsync(userId, async ctx =>
        {
            PlayerWorkspace ws = ctx.Player;
            if (ws.State.Story.HighestUnlockedStage <= ws.Balance.Story.UnlockGuildsStage)
            {
                throw new ApiException(ErrorCode.FeatureLocked, "Guilds unlock after stage " + ws.Balance.Story.UnlockGuildsStage + ".");
            }
            GuildRecord record = await ctx.Tx.GetGuildAsync(guildId).ConfigureAwait(false) ?? throw new ApiException(ErrorCode.NotFound);
            bool invited = record.Invited.Remove(ws.IdString);
            new GuildManager(ws.Balance, ws.Clock).JoinGuild(record.Guild, ws.IdString, ws.State.DisplayName, ws.State.Pvp.Trophies, ws.State.GuildId != null, invited).ThrowIfFailed();

            ws.State.GuildId = guildId;
            ws.Achievements.IncrementStat(StatKey.GuildsJoined, 1);
            await ctx.Tx.UpdateGuildAsync(record).ConfigureAwait(false);
            return await ToDtoAsync(record, ws, ct).ConfigureAwait(false);
        }, ct);

    public Task<bool> LeaveAsync(Guid userId, CancellationToken ct) =>
        _ops.RunAsync(userId, async ctx =>
        {
            GuildRecord record = await LockMyGuildAsync(ctx).ConfigureAwait(false);
            bool disband = new GuildManager(ctx.Player.Balance, ctx.Player.Clock).Leave(record.Guild, ctx.Player.IdString).ValueOrThrow();
            ctx.Player.State.GuildId = null;
            if (disband)
            {
                await ctx.Tx.DeleteGuildAsync(record.Id).ConfigureAwait(false);
            }
            else
            {
                await ctx.Tx.UpdateGuildAsync(record).ConfigureAwait(false);
            }
            return true;
        }, ct);

    public async Task<GuildDto> KickAsync(Guid userId, GuildMemberActionRequest request, CancellationToken ct)
    {
        Guid target = ParsePlayer(request?.PlayerId);
        return await _ops.RunPairAsync(userId, target, async (me, them) =>
        {
            GuildRecord record = await LockMyGuildAsync(me).ConfigureAwait(false);
            if (them.Player.State.GuildId != record.Id)
            {
                throw new ApiException(ErrorCode.NotMember);
            }
            new GuildManager(me.Player.Balance, me.Player.Clock).Kick(record.Guild, me.Player.IdString, them.Player.IdString).ThrowIfFailed();
            them.Player.State.GuildId = null;
            await me.Tx.UpdateGuildAsync(record).ConfigureAwait(false);
            return await ToDtoAsync(record, me.Player, ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    public Task<GuildDto> SetRoleAsync(Guid userId, GuildMemberActionRequest request, CancellationToken ct) =>
        _ops.RunAsync(userId, async ctx =>
        {
            GuildRole role = Mappers.ParseEnum<GuildRole>(request?.Role, "role");
            GuildRecord record = await LockMyGuildAsync(ctx).ConfigureAwait(false);
            new GuildManager(ctx.Player.Balance, ctx.Player.Clock).SetRole(record.Guild, ctx.Player.IdString, ParsePlayer(request!.PlayerId).ToString(), role).ThrowIfFailed();
            await ctx.Tx.UpdateGuildAsync(record).ConfigureAwait(false);
            return await ToDtoAsync(record, ctx.Player, ct).ConfigureAwait(false);
        }, ct);

    public Task<GuildDto> InviteAsync(Guid userId, GuildMemberActionRequest request, CancellationToken ct) =>
        _ops.RunAsync(userId, async ctx =>
        {
            GuildRecord record = await LockMyGuildAsync(ctx).ConfigureAwait(false);
            RequireOfficer(record, ctx.Player);
            string target = ParsePlayer(request?.PlayerId).ToString();
            if (record.Guild.Find(target) != null)
            {
                throw new ApiException(ErrorCode.AlreadyMember);
            }
            if (!record.Invited.Contains(target))
            {
                if (record.Invited.Count >= 50)
                {
                    record.Invited.RemoveAt(0);
                }
                record.Invited.Add(target);
            }
            await ctx.Tx.UpdateGuildAsync(record).ConfigureAwait(false);
            return await ToDtoAsync(record, ctx.Player, ct).ConfigureAwait(false);
        }, ct);

    public Task<GuildDto> UpdateSettingsAsync(Guid userId, GuildSettingsRequest request, CancellationToken ct) =>
        _ops.RunAsync(userId, async ctx =>
        {
            GuildRecord record = await LockMyGuildAsync(ctx).ConfigureAwait(false);
            RequireOfficer(record, ctx.Player);
            ApplySettings(record.Guild, request?.Description, request?.IsOpen ?? record.Guild.IsOpen, request?.MinTrophies ?? record.Guild.MinTrophies);
            await ctx.Tx.UpdateGuildAsync(record).ConfigureAwait(false);
            return await ToDtoAsync(record, ctx.Player, ct).ConfigureAwait(false);
        }, ct);

    public Task<DonateResponse> DonateAsync(Guid userId, DonateRequest request, CancellationToken ct) =>
        _ops.RunAsync(userId, async ctx =>
        {
            PlayerWorkspace ws = ctx.Player;
            GuildRecord record = await LockMyGuildAsync(ctx).ConfigureAwait(false);
            GuildDonationResult result = new GuildManager(ws.Balance, ws.Clock).Donate(record.Guild, ws.IdString, ws.Wallet,
                string.Equals(request?.Currency, "Coins", StringComparison.OrdinalIgnoreCase) ? Currency.Coins : Currency.Orbes, request?.Amount ?? 0).ValueOrThrow();
            ws.Achievements.IncrementStat(StatKey.GuildDonations, 1);
            await ctx.Tx.UpdateGuildAsync(record).ConfigureAwait(false);
            return new DonateResponse
            {
                Paid = result.Paid,
                Currency = result.Currency.ToString(),
                LeveledUp = result.LeveledUp,
                LevelsGained = result.LevelsGained,
                Points = result.Points,
                Guild = await ToDtoAsync(record, ws, ct).ConfigureAwait(false),
                Wallet = Mappers.Wallet(ws)
            };
        }, ct);

    public Task<GuildDto> SpendTechAsync(Guid userId, TechRequest request, CancellationToken ct) =>
        _ops.RunAsync(userId, async ctx =>
        {
            GuildTech tech = Mappers.ParseEnum<GuildTech>(request?.Tech, "tech");
            GuildRecord record = await LockMyGuildAsync(ctx).ConfigureAwait(false);
            new GuildManager(ctx.Player.Balance, ctx.Player.Clock).SpendTechPoint(record.Guild, ctx.Player.IdString, tech).ThrowIfFailed();
            await ctx.Tx.UpdateGuildAsync(record).ConfigureAwait(false);
            return await ToDtoAsync(record, ctx.Player, ct).ConfigureAwait(false);
        }, ct);

    public Task<MatchStartResponse> StartBossAsync(Guid userId, StartStageRequest? request, CancellationToken ct) =>
        _ops.RunAsync(userId, async ctx =>
        {
            PlayerWorkspace ws = ctx.Player;
            GuildRecord record = await LockMyGuildAsync(ctx).ConfigureAwait(false);
            int week = Week;
            GuildBossState boss = new GuildManager(ws.Balance, ws.Clock).EnsureBossWeek(record.Guild, week);
            if (boss.Defeated)
            {
                throw new ApiException(ErrorCode.SessionOver, "This week's boss is already defeated.");
            }
            GuildMember member = record.Guild.Find(ws.IdString)!;
            int attacksUsed = member.BossWeek == week ? member.BossAttacksThisWeek : 0;
            if (attacksUsed >= ws.Balance.Guild.BossAttacksPerMemberPerWeek)
            {
                throw new ApiException(ErrorCode.LimitReached, "No boss attacks left this week.");
            }

            List<LoadoutEntry> loadout = ws.Inventory.BuildLoadout(Mappers.ParseLoadout(request?.Loadout, ws.Balance), ws.Balance, ws.State.Pvp.HighestLeague).ValueOrThrow();
            var match = new MatchRow
            {
                Id = Mappers.NewId("gb"),
                PlayerId = userId,
                Mode = GameMode.GuildBoss,
                Seed = StableHash.Mix((ulong)record.Id, (ulong)week, StableHash.Fnv1a(ws.IdString + ":" + attacksUsed + ":" + ws.NowMs)),
                StageId = boss.BossIndex,
                Status = MatchStatus.Started,
                StartedAt = ws.Now,
                Config = new MatchConfigSnapshot
                {
                    Loadout = loadout,
                    HighestLeague = ws.State.Pvp.HighestLeague,
                    GuildId = record.Id,
                    GuildWeek = week
                }
            };
            ws.SnapshotPet(match.Config, GameMode.GuildBoss);
            await ctx.Tx.InsertMatchAsync(match).ConfigureAwait(false);
            await ctx.Tx.UpdateGuildAsync(record).ConfigureAwait(false);
            MatchStartResponse start = Mappers.MatchStart(match, ws, _ops.Balance.HashHex);
            start.BossMaxHp = boss.MaxHp;
            start.BossRemainingHp = boss.RemainingHp;
            return start;
        }, ct);

    public Task<GuildBossAttackResponse> SubmitBossDamageAsync(Guid userId, SubmitReplayRequest request, CancellationToken ct) =>
        _ops.RunAsync(userId, async ctx =>
        {
            PlayerWorkspace ws = ctx.Player;
            ReplayData replay = Mappers.DecodeReplay(request?.ReplayBase64);
            MatchRow? match = string.IsNullOrWhiteSpace(request!.MatchId) ? null : await ctx.Tx.GetMatchAsync(request.MatchId).ConfigureAwait(false);
            if (match == null || match.PlayerId != userId || match.Mode != GameMode.GuildBoss)
            {
                throw new ApiException(ErrorCode.NotFound);
            }
            if (match.Status != MatchStatus.Started)
            {
                throw new ApiException(ErrorCode.SessionOver);
            }

            ErrorCode header = ReplayGuard.CheckHeader(replay, match, userId);
            if (header != ErrorCode.None)
            {
                ReplayGuard.FlagHeaderMismatch(ws, match, header);
                return await RejectAsync(ctx, match, header).ConfigureAwait(false);
            }

            SessionConfig config = SessionConfig.ForGuildBoss(match.Seed, match.StageId, ws.Balance, match.Config.Loadout, match.Config.HighestLeague)
                .WithPet(match.Config.Pet, match.Config.PetLevel, ws.Balance);
            bool timingOk = ws.AntiCheat.ValidateTimestamps(ws.State.Integrity, replay, TimeUtil.ToUnixMs(match.StartedAt), ws.NowMs, match.Id);
            ReplayVerification verification = ws.AntiCheat.ValidateReplay(ws.State.Integrity, replay, config, match.Id);
            if (!verification.Valid || !timingOk)
            {
                return await RejectAsync(ctx, match, verification.Valid ? ErrorCode.ReplayMismatch : verification.Error).ConfigureAwait(false);
            }

            StageResult result = verification.Session.GetResult();
            ReplayGuard.ConsumePowerUps(ws, result.PowerUpsUsed, match.Id);

            GuildRecord record = await LockMyGuildAsync(ctx).ConfigureAwait(false);
            if (record.Id != match.Config.GuildId || Week != match.Config.GuildWeek)
            {
                return await RejectAsync(ctx, match, ErrorCode.SessionOver).ConfigureAwait(false);
            }

            var manager = new GuildManager(ws.Balance, ws.Clock);
            BossAttackResult attack = manager.SubmitBossDamage(record.Guild, ws.IdString, result.FinalScore, match.Config.GuildWeek, TimeUtil.ToUnixMs(match.StartedAt)).ValueOrThrow();

            RewardData? reward = null;
            if (attack.DefeatedNow)
            {
                reward = attack.RewardPerMember;
                string rewardId = "guildboss:" + record.Id + ":" + match.Config.GuildWeek + ":" + record.Guild.Boss.BossIndex;
                ws.GrantReward(reward, TransactionReason.GuildBossReward, rewardId, rewardId + ":" + ws.IdString);
                ws.Achievements.IncrementStat(StatKey.GuildBossesDefeated, 1);
                var others = attack.RewardedPlayers.Where(p => p != ws.IdString).ToList();
                if (others.Count > 0)
                {
                    record.PendingRewards.Add(new PendingGuildReward { Id = rewardId, Reward = reward, PlayerIds = others });
                }
            }
            else if (attack.DefeatedDuringAttack)
            {
                // Hand over the shared defeat reward right away so the result screen can show it.
                PendingGuildReward? mine = record.PendingRewards.FirstOrDefault(p => p.PlayerIds.Contains(ws.IdString));
                reward = mine?.Reward;
                ClaimPendingRewards(ws, record);
            }

            ws.Achievements.IncrementStat(StatKey.GuildBossAttacks, 1);
            ws.Achievements.RecordStageResult(result, null, null);
            ws.TrackQuest(QuestType.AttackGuildBoss, 1);
            ws.AddBattlePassXp(ws.Balance.LiveOps.XpGuildBossAttack);
            ws.AwardPetXp(match.Config, GameMode.GuildBoss, true);

            long replayId = await ctx.Tx.InsertReplayAsync(new ReplayRow
            {
                PlayerId = userId,
                Mode = GameMode.GuildBoss,
                Seed = match.Seed,
                StageId = match.StageId,
                Trophies = ws.State.Pvp.Trophies,
                FinalScore = result.FinalScore,
                Region = ws.State.Region,
                Data = Convert.FromBase64String(request.ReplayBase64),
                CreatedAt = ws.Now
            }).ConfigureAwait(false);

            match.Status = MatchStatus.Completed;
            match.ReplayId = replayId;
            match.FinishedAt = ws.Now;
            match.ResultJson = Json.Serialize(new { score = result.FinalScore, damage = attack.DamageApplied, defeated = attack.DefeatedNow });
            await ctx.Tx.UpdateMatchAsync(match).ConfigureAwait(false);
            await ctx.Tx.UpdateGuildAsync(record).ConfigureAwait(false);

            return new GuildBossAttackResponse
            {
                Accepted = true,
                Score = result.FinalScore,
                Damage = attack.DamageApplied,
                AttacksLeft = attack.AttacksLeft,
                DefeatedNow = attack.DefeatedNow,
                DefeatedDuringAttack = attack.DefeatedDuringAttack,
                Reward = reward == null ? null : Mappers.Reward(reward),
                Boss = BossDto(record.Guild.Boss),
                AchievementsUnlocked = ws.UnlockedAchievements.ToList()
            };
        }, ct);

    public Task<ChatMessageDto> SendChatAsync(Guid userId, ChatSendRequest request, CancellationToken ct) =>
        _ops.RunAsync(userId, async ctx =>
        {
            PlayerWorkspace ws = ctx.Player;
            long guildId = ws.State.GuildId ?? throw new ApiException(ErrorCode.NotMember);
            GuildRecord record = await ctx.Tx.GetGuildAsync(guildId, forUpdate: false).ConfigureAwait(false) ?? throw new ApiException(ErrorCode.NotMember);
            if (record.Guild.Find(ws.IdString) == null)
            {
                throw new ApiException(ErrorCode.NotMember);
            }

            ChatModerationResult moderation = _chat.Moderate(ws.IdString, request?.Text ?? string.Empty, ws.IsBanned);
            if (!moderation.Accepted)
            {
                throw new ApiException(moderation.Error, moderation.Reason);
            }

            var message = new GuildMessageRow
            {
                GuildId = guildId,
                PlayerId = userId,
                DisplayName = ws.State.DisplayName,
                Body = moderation.Text,
                Masked = moderation.Masked,
                CreatedAt = ws.Now
            };
            await ctx.Tx.InsertGuildMessageAsync(message).ConfigureAwait(false);
            return ToChat(message);
        }, ct);

    public Task<ChatHistoryResponse> ChatHistoryAsync(Guid userId, long? beforeId, CancellationToken ct) =>
        _ops.ReadAsync(userId, async (ws, tx) =>
        {
            long guildId = ws.State.GuildId ?? throw new ApiException(ErrorCode.NotMember);
            IReadOnlyList<GuildMessageRow> rows = await _ops.Store.GetGuildMessagesAsync(guildId, beforeId, 50, ct).ConfigureAwait(false);
            return new ChatHistoryResponse { Messages = rows.Select(ToChat).ToList() };
        }, ct);

    // ------------------------------------------------------------------ helpers

    private static async Task<GuildRecord> LockMyGuildAsync(OperationContext ctx)
    {
        long guildId = ctx.Player.State.GuildId ?? throw new ApiException(ErrorCode.NotMember, "You are not in a guild.");
        GuildRecord? record = await ctx.Tx.GetGuildAsync(guildId).ConfigureAwait(false);
        if (record == null || record.Guild.Find(ctx.Player.IdString) == null)
        {
            ctx.Player.State.GuildId = null;
            throw new ApiException(ErrorCode.NotMember, "You are not in a guild.");
        }
        return record;
    }

    private static void RequireOfficer(GuildRecord record, PlayerWorkspace ws)
    {
        if (record.Guild.Find(ws.IdString)?.Role < GuildRole.Officer)
        {
            throw new ApiException(ErrorCode.PermissionDenied);
        }
    }

    private static void ClaimPendingRewards(PlayerWorkspace ws, GuildRecord record)
    {
        foreach (PendingGuildReward pending in record.PendingRewards.ToList())
        {
            if (pending.PlayerIds.Remove(ws.IdString))
            {
                ws.GrantReward(pending.Reward, TransactionReason.GuildBossReward, pending.Id, pending.Id + ":" + ws.IdString);
                ws.Achievements.IncrementStat(StatKey.GuildBossesDefeated, 1);
            }
            if (pending.PlayerIds.Count == 0)
            {
                record.PendingRewards.Remove(pending);
            }
        }
    }

    private async Task RefreshMembersAsync(GuildRecord record, CancellationToken ct)
    {
        List<Guid> ids = record.Guild.Members.Select(m => Guid.Parse(m.PlayerId)).ToList();
        Dictionary<string, PlayerSummary> summaries = (await _ops.Store.GetPlayerSummariesAsync(ids, ct).ConfigureAwait(false)).ToDictionary(s => s.Id.ToString());
        foreach (GuildMember member in record.Guild.Members)
        {
            if (summaries.TryGetValue(member.PlayerId, out PlayerSummary? s))
            {
                member.Trophies = s.Trophies;
                member.DisplayName = s.DisplayName;
            }
        }
    }

    private static void ApplySettings(Guild guild, string? description, bool isOpen, int minTrophies)
    {
        string text = (description ?? string.Empty).Trim();
        if (text.Length > 200)
        {
            throw new ApiException(ErrorCode.InvalidArgument, "Description is limited to 200 characters.");
        }
        guild.Description = ChatModerator.MaskProfanity(text, out _);
        guild.IsOpen = isOpen;
        guild.MinTrophies = Math.Clamp(minTrophies, 0, 5000);
    }

    private async Task<GuildDto> ToDtoAsync(GuildRecord record, PlayerWorkspace ws, CancellationToken ct)
    {
        Guild g = record.Guild;
        var manager = new GuildManager(ws.Balance, ws.Clock);
        long cost = manager.GetNextLevelPoints(g);
        int today = TimeUtil.DayIndex(ws.Clock.UtcNow);
        int week = Week;
        int attacks = ws.Balance.Guild.BossAttacksPerMemberPerWeek;
        IReadOnlyList<GuildRankingRow> top = await _ops.Store.TopGuildsAsync(100, ct).ConfigureAwait(false);

        return new GuildDto
        {
            Id = record.Id,
            Name = g.Name,
            Description = g.Description,
            Level = g.Level,
            DonationProgress = g.DonationProgress,
            NextLevelCurrency = g.Level >= ws.Balance.Guild.MaxLevel ? null : "Points",
            NextLevelCost = g.Level >= ws.Balance.Guild.MaxLevel ? 0 : cost,
            CoinsPerPoint = ws.Balance.Guild.CoinsPerPoint,
            DailyCoinCap = ws.Balance.Guild.DailyCoinDonationCap,
            TechPoints = g.TechPointsAvailable,
            Tech = g.TechRanks.ToDictionary(kv => kv.Key.ToString(), kv => kv.Value),
            Members = g.Members
                .OrderByDescending(m => m.Role)
                .ThenByDescending(m => m.Trophies)
                .Select(m => new GuildMemberDto
                {
                    PlayerId = m.PlayerId,
                    DisplayName = m.DisplayName,
                    Role = m.Role.ToString(),
                    Trophies = m.Trophies,
                    DonatedCoins = m.DonatedCoins,
                    DonatedOrbes = m.DonatedOrbes,
                    BossAttacksLeft = m.BossWeek == week ? attacks - m.BossAttacksThisWeek : attacks,
                    BossDamage = m.BossWeek == week ? m.BossDamageThisWeek : 0,
                    CoinsDonatedToday = m.CoinDonationDay == today ? m.CoinsDonatedToday : 0,
                    JoinedAtUnixMs = m.JoinedAtUnixMs
                }).ToList(),
            IsOpen = g.IsOpen,
            MinTrophies = g.MinTrophies,
            TotalTrophies = g.TotalTrophies,
            Boss = BossDto(g.Boss),
            Rank = top.FirstOrDefault(t => t.GuildId == record.Id)?.Rank ?? 0
        };
    }

    private GuildBossDto? BossDto(GuildBossState? boss) => boss == null ? null : new GuildBossDto
    {
        Week = boss.Week,
        BossIndex = boss.BossIndex,
        MaxHp = boss.MaxHp,
        Damage = boss.Damage,
        Defeated = boss.Defeated,
        ResetAtUnixMs = TimeUtil.ToUnixMs(TimeUtil.NextWeeklyReset(_ops.Clock.UtcNow))
    };

    private static async Task<GuildBossAttackResponse> RejectAsync(OperationContext ctx, MatchRow match, ErrorCode error)
    {
        match.Status = MatchStatus.Rejected;
        match.FinishedAt = ctx.Player.Now;
        match.ResultJson = Json.Serialize(new { error = error.ToString() });
        await ctx.Tx.UpdateMatchAsync(match).ConfigureAwait(false);
        return new GuildBossAttackResponse { Accepted = false, Error = error.ToString() };
    }

    private static ChatMessageDto ToChat(GuildMessageRow m) => new()
    {
        Id = m.Id,
        PlayerId = m.PlayerId?.ToString(),
        DisplayName = m.DisplayName,
        Body = m.Body,
        Masked = m.Masked,
        CreatedAtUnixMs = TimeUtil.ToUnixMs(m.CreatedAt)
    };

    private static Guid ParsePlayer(string? id) =>
        Guid.TryParse(id, out Guid g) ? g : throw new ApiException(ErrorCode.InvalidArgument, "Invalid player id.");
}
