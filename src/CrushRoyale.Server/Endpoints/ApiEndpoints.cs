using System.Security.Claims;
using CrushRoyale.Contracts;
using CrushRoyale.Server.Auth;
using CrushRoyale.Server.Persistence;
using CrushRoyale.Server.Services;
using Microsoft.AspNetCore.Mvc;

namespace CrushRoyale.Server.Endpoints;

/// <summary>HTTP surface (see docs/API.md). Every /v1 route requires a Supabase access token unless stated otherwise.</summary>
public static class ApiEndpoints
{
    public const string PlayerRateLimit = "player";

    public static void MapCrushEndpoints(this WebApplication app)
    {
        app.MapGet(ApiRoutes.Health, async (IGameStore store, CancellationToken ct) =>
            await store.PingAsync(ct) ? Results.Ok(new { status = "ok" }) : Results.Json(new { status = "database unavailable" }, statusCode: 503))
            .AllowAnonymous();

        app.MapGet(ApiRoutes.Config, (ProfileService s) => s.GetConfig()).AllowAnonymous();

        RouteGroupBuilder api = app.MapGroup(string.Empty).RequireAuthorization().RequireRateLimiting(PlayerRateLimit);
        MapPlayer(api);
        MapStory(api);
        MapEconomy(api);
        MapPvp(api);
        MapSocial(api);
        MapGuild(api);
        MapProgression(api);
        MapAdmin(api);
    }

    private static Guid Me(ClaimsPrincipal user) => user.PlayerId();

    private static void MapPlayer(RouteGroupBuilder api)
    {
        api.MapPost(ApiRoutes.Login, (LoginRequest body, ClaimsPrincipal user, ProfileService s, CancellationToken ct) => s.LoginAsync(Me(user), body, ct));
        api.MapGet(ApiRoutes.Me, (ClaimsPrincipal user, ProfileService s, CancellationToken ct) => s.GetMeAsync(Me(user), ct));
        api.MapDelete(ApiRoutes.Me, (ClaimsPrincipal user, ProfileService s, CancellationToken ct) => s.DeleteAccountAsync(Me(user), ct));
        api.MapPut(ApiRoutes.Hero, (SetHeroRequest body, ClaimsPrincipal user, ProfileService s, CancellationToken ct) => s.SetHeroAsync(Me(user), body, ct));
        api.MapGet(ApiRoutes.PlayerStats, (Guid id, ProfileService s, CancellationToken ct) => s.GetStatsAsync(id, ct));
        api.MapGet(ApiRoutes.PlayerSearch, ([FromQuery] string? q, ProfileService s, CancellationToken ct) => s.SearchAsync(q, ct));
    }

    private static void MapStory(RouteGroupBuilder api)
    {
        api.MapGet(ApiRoutes.StageData, (int stageId, ClaimsPrincipal user, StoryService s, CancellationToken ct) => s.GetStageAsync(Me(user), stageId, ct));
        api.MapPost(ApiRoutes.StageStart, (int stageId, StartStageRequest? body, ClaimsPrincipal user, StoryService s, CancellationToken ct) => s.StartAsync(Me(user), stageId, body ?? new StartStageRequest(), ct));
        api.MapPost(ApiRoutes.StageContinue, (string matchId, ContinueRequest? body, ClaimsPrincipal user, StoryService s, CancellationToken ct) => s.ContinueAsync(Me(user), matchId, body ?? new ContinueRequest(), ct));
        api.MapPost(ApiRoutes.StageComplete, (string matchId, SubmitReplayRequest body, ClaimsPrincipal user, StoryService s, CancellationToken ct) => s.CompleteAsync(Me(user), matchId, body.ReplayBase64, ct));
        api.MapPost(ApiRoutes.StoryChoice, (ChoiceRequest body, ClaimsPrincipal user, StoryService s, CancellationToken ct) => s.MakeChoiceAsync(Me(user), body, ct));
        api.MapPost(ApiRoutes.StoryEventSeen, (string eventId, ClaimsPrincipal user, StoryService s, CancellationToken ct) => s.MarkEventSeenAsync(Me(user), eventId, ct));
    }

    private static void MapEconomy(RouteGroupBuilder api)
    {
        api.MapPost(ApiRoutes.LivesBuy, (BuyLivesRequest body, ClaimsPrincipal user, EconomyService s, CancellationToken ct) => s.BuyLivesAsync(Me(user), body, ct));
        api.MapPost(ApiRoutes.LivesVipClaim, (ClaimsPrincipal user, EconomyService s, CancellationToken ct) => s.ClaimVipLifeAsync(Me(user), ct));
        api.MapPost(ApiRoutes.ChestUnlock, (int slot, ClaimsPrincipal user, ChestService s, CancellationToken ct) => s.UnlockAsync(Me(user), slot, ct));
        api.MapPost(ApiRoutes.ChestOpen, (int slot, ChestOpenRequest body, ClaimsPrincipal user, ChestService s, CancellationToken ct) => s.OpenAsync(Me(user), slot, body, ct));
        api.MapPost(ApiRoutes.ChallengeCreate, (ClaimsPrincipal user, ChallengeService s, CancellationToken ct) => s.CreateAsync(Me(user), ct));
        api.MapPost(ApiRoutes.ChallengeStart, (string code, StartStageRequest body, ClaimsPrincipal user, ChallengeService s, CancellationToken ct) => s.StartAsync(Me(user), code, body, ct));
        api.MapPost(ApiRoutes.Telemetry, (TelemetryRequest body, ClaimsPrincipal user, TelemetryService s, CancellationToken ct) => s.RecordAsync(Me(user), body, ct));
        api.MapPost(ApiRoutes.PetSummon, (PetSummonRequest body, ClaimsPrincipal user, PetService s, CancellationToken ct) => s.SummonAsync(Me(user), body, ct));
        api.MapPost(ApiRoutes.PetEquip, (PetRequest body, ClaimsPrincipal user, PetService s, CancellationToken ct) => s.EquipAsync(Me(user), body, ct));
        api.MapPost(ApiRoutes.PetAwaken, (PetRequest body, ClaimsPrincipal user, PetService s, CancellationToken ct) => s.AwakenAsync(Me(user), body, ct));
        api.MapGet(ApiRoutes.Shop, (ClaimsPrincipal user, EconomyService s, CancellationToken ct) => s.GetShopAsync(Me(user), ct));
        api.MapPost(ApiRoutes.ShopRefresh, (ClaimsPrincipal user, EconomyService s, CancellationToken ct) => s.RefreshShopAsync(Me(user), ct));
        api.MapPost(ApiRoutes.ShopPurchase, (PurchaseRequest body, ClaimsPrincipal user, EconomyService s, CancellationToken ct) => s.PurchaseAsync(Me(user), body, ct));
        api.MapPost(ApiRoutes.IapPrecheck, (IapPrecheckRequest body, ClaimsPrincipal user, EconomyService s, CancellationToken ct) => s.PrecheckAsync(Me(user), body, ct));
        api.MapPost(ApiRoutes.IapValidate, (IapValidateRequest body, ClaimsPrincipal user, EconomyService s, CancellationToken ct) => s.ValidatePurchaseAsync(Me(user), body, ct));
        api.MapPost(ApiRoutes.Equip, (EquipRequest body, ClaimsPrincipal user, EconomyService s, CancellationToken ct) => s.EquipAsync(Me(user), body, ct));
        api.MapPost(ApiRoutes.RewardedAd, (RewardedAdRequest body, ClaimsPrincipal user, EconomyService s, CancellationToken ct) => s.RewardedAdAsync(Me(user), body, ct));
    }

    private static void MapPvp(RouteGroupBuilder api)
    {
        api.MapPost(ApiRoutes.Matchmaking, (MatchmakingRequest? body, ClaimsPrincipal user, PvpService s, CancellationToken ct) => s.RequestAsync(Me(user), body, ct));
        api.MapGet(ApiRoutes.Matchmaking, (ClaimsPrincipal user, PvpService s, CancellationToken ct) => s.StatusAsync(Me(user), ct));
        api.MapDelete(ApiRoutes.Matchmaking, (ClaimsPrincipal user, PvpService s) => Results.Ok(new { cancelled = s.Cancel(Me(user)) }));
        api.MapGet(ApiRoutes.PvpMatch, (string matchId, ClaimsPrincipal user, PvpService s, CancellationToken ct) => s.GetMatchAsync(Me(user), matchId, ct));
        api.MapPost(ApiRoutes.PvpRecord, (SubmitReplayRequest body, ClaimsPrincipal user, PvpService s, CancellationToken ct) => s.RecordAsync(Me(user), body, ct));
        api.MapGet(ApiRoutes.LeaderboardWeekly, ([FromQuery] string? league, [FromQuery] int? limit, ClaimsPrincipal user, PvpService s, CancellationToken ct) => s.WeeklyLeaderboardAsync(Me(user), league, limit ?? 100, ct));
        api.MapGet(ApiRoutes.LeaderboardGuilds, (ClaimsPrincipal user, PvpService s, CancellationToken ct) => s.GuildLeaderboardAsync(Me(user), ct));
    }

    private static void MapSocial(RouteGroupBuilder api)
    {
        api.MapGet(ApiRoutes.Friends, (ClaimsPrincipal user, SocialService s, CancellationToken ct) => s.GetFriendsAsync(Me(user), ct));
        api.MapPost(ApiRoutes.FriendRequest, (FriendTargetRequest body, ClaimsPrincipal user, SocialService s, CancellationToken ct) => s.ActAsync(Me(user), "request", body, ct));
        api.MapPost(ApiRoutes.FriendAccept, (FriendTargetRequest body, ClaimsPrincipal user, SocialService s, CancellationToken ct) => s.ActAsync(Me(user), "accept", body, ct));
        api.MapPost(ApiRoutes.FriendDecline, (FriendTargetRequest body, ClaimsPrincipal user, SocialService s, CancellationToken ct) => s.ActAsync(Me(user), "decline", body, ct));
        api.MapPost(ApiRoutes.FriendRemove, (FriendTargetRequest body, ClaimsPrincipal user, SocialService s, CancellationToken ct) => s.ActAsync(Me(user), "remove", body, ct));
        api.MapPost(ApiRoutes.FriendBlock, (FriendTargetRequest body, ClaimsPrincipal user, SocialService s, CancellationToken ct) => s.ActAsync(Me(user), "block", body, ct));
        api.MapPost(ApiRoutes.FriendChallenge, (string friendId, StartStageRequest? body, ClaimsPrincipal user, SocialService s, CancellationToken ct) => s.ChallengeAsync(Me(user), friendId, body, ct));
        api.MapGet(ApiRoutes.WorldMap, (ClaimsPrincipal user, SocialService s, CancellationToken ct) => s.WorldMapAsync(Me(user), ct));
    }

    private static void MapGuild(RouteGroupBuilder api)
    {
        api.MapPost(ApiRoutes.GuildCreate, (CreateGuildRequest body, ClaimsPrincipal user, GuildService s, CancellationToken ct) => s.CreateAsync(Me(user), body, ct));
        api.MapGet(ApiRoutes.GuildMine, (ClaimsPrincipal user, GuildService s, CancellationToken ct) => s.GetMineAsync(Me(user), ct));
        api.MapGet(ApiRoutes.GuildSearch, ([FromQuery] string? q, GuildService s, CancellationToken ct) => s.SearchAsync(q, ct));
        api.MapGet(ApiRoutes.GuildById, (long guildId, ClaimsPrincipal user, GuildService s, CancellationToken ct) => s.GetAsync(Me(user), guildId, ct));
        api.MapPost(ApiRoutes.GuildJoin, (long guildId, ClaimsPrincipal user, GuildService s, CancellationToken ct) => s.JoinAsync(Me(user), guildId, ct));
        api.MapPost(ApiRoutes.GuildLeave, async (ClaimsPrincipal user, GuildService s, CancellationToken ct) => Results.Ok(new { left = await s.LeaveAsync(Me(user), ct) }));
        api.MapPost(ApiRoutes.GuildKick, (GuildMemberActionRequest body, ClaimsPrincipal user, GuildService s, CancellationToken ct) => s.KickAsync(Me(user), body, ct));
        api.MapPost(ApiRoutes.GuildRole, (GuildMemberActionRequest body, ClaimsPrincipal user, GuildService s, CancellationToken ct) => s.SetRoleAsync(Me(user), body, ct));
        api.MapPost(ApiRoutes.GuildInvite, (GuildMemberActionRequest body, ClaimsPrincipal user, GuildService s, CancellationToken ct) => s.InviteAsync(Me(user), body, ct));
        api.MapPut(ApiRoutes.GuildSettings, (GuildSettingsRequest body, ClaimsPrincipal user, GuildService s, CancellationToken ct) => s.UpdateSettingsAsync(Me(user), body, ct));
        api.MapPost(ApiRoutes.GuildDonate, (DonateRequest body, ClaimsPrincipal user, GuildService s, CancellationToken ct) => s.DonateAsync(Me(user), body, ct));
        api.MapPost(ApiRoutes.GuildTech, (TechRequest body, ClaimsPrincipal user, GuildService s, CancellationToken ct) => s.SpendTechAsync(Me(user), body, ct));
        api.MapPost(ApiRoutes.GuildBossStart, (StartStageRequest? body, ClaimsPrincipal user, GuildService s, CancellationToken ct) => s.StartBossAsync(Me(user), body, ct));
        api.MapPost(ApiRoutes.GuildBossDamage, (SubmitReplayRequest body, ClaimsPrincipal user, GuildService s, CancellationToken ct) => s.SubmitBossDamageAsync(Me(user), body, ct));
        api.MapPost(ApiRoutes.GuildChat, (ChatSendRequest body, ClaimsPrincipal user, GuildService s, CancellationToken ct) => s.SendChatAsync(Me(user), body, ct));
        api.MapGet(ApiRoutes.GuildChat, ([FromQuery] long? before, ClaimsPrincipal user, GuildService s, CancellationToken ct) => s.ChatHistoryAsync(Me(user), before, ct));
    }

    private static void MapProgression(RouteGroupBuilder api)
    {
        api.MapGet(ApiRoutes.Achievements, (ClaimsPrincipal user, ProgressionService s, CancellationToken ct) => s.GetAchievementsAsync(Me(user), ct));
        api.MapPost(ApiRoutes.AchievementUnlock, (ClaimAchievementRequest body, ClaimsPrincipal user, ProgressionService s, CancellationToken ct) => s.ClaimAchievementAsync(Me(user), body, ct));
        api.MapGet(ApiRoutes.Quests, (ClaimsPrincipal user, ProgressionService s, CancellationToken ct) => s.GetQuestsAsync(Me(user), ct));
        api.MapPost(ApiRoutes.QuestClaim, (string questId, ClaimsPrincipal user, ProgressionService s, CancellationToken ct) => s.ClaimQuestAsync(Me(user), questId, ct));
        api.MapPost(ApiRoutes.LoginBonus, (ClaimsPrincipal user, ProgressionService s, CancellationToken ct) => s.ClaimLoginBonusAsync(Me(user), ct));
        api.MapGet(ApiRoutes.BattlePass, (ClaimsPrincipal user, ProgressionService s, CancellationToken ct) => s.GetBattlePassAsync(Me(user), ct));
        api.MapPost(ApiRoutes.BattlePassClaim, (BattlePassClaimRequest body, ClaimsPrincipal user, ProgressionService s, CancellationToken ct) => s.ClaimBattlePassAsync(Me(user), body, ct));
    }

    private static void MapAdmin(RouteGroupBuilder api)
    {
        api.MapPost(ApiRoutes.ReportCheat, async (ReportCheatRequest body, ClaimsPrincipal user, AdminService s, CancellationToken ct) => Results.Ok(new { reported = await s.ReportAsync(Me(user), body, ct) }));

        RouteGroupBuilder admin = api.MapGroup(string.Empty).RequireAuthorization(AuthenticationSetup.AdminPolicy);
        admin.MapGet(ApiRoutes.AdminFlags, ([FromQuery] int? limit, AdminService s, CancellationToken ct) => s.PendingFlagsAsync(limit ?? 100, ct));
        admin.MapPost(ApiRoutes.AdminFlagReview, async (long flagId, ReviewFlagRequest body, ClaimsPrincipal user, AdminService s, CancellationToken ct) => Results.Ok(new { reviewed = await s.ReviewAsync(Me(user), flagId, body, ct) }));
        admin.MapPost(ApiRoutes.AdminGrant, (AdminGrantRequest body, ClaimsPrincipal user, AdminService s, CancellationToken ct) => s.GrantAsync(Me(user), body, ct));
        admin.MapPut(ApiRoutes.AdminBalance, (UpdateBalanceRequest body, ClaimsPrincipal user, AdminService s, CancellationToken ct) => s.UpdateBalanceAsync(Me(user), body, ct));
        admin.MapPost(ApiRoutes.AdminSeasonReset, async (SeasonJobService s, CancellationToken ct) => Results.Ok(new { processed = await s.RunIfDueAsync(true, ct) }));
    }
}
