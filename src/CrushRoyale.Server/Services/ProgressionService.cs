using CrushRoyale.Contracts;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Economy;
using CrushRoyale.Core.Progression;
using CrushRoyale.Server.Infrastructure;

namespace CrushRoyale.Server.Services;

/// <summary>Achievements / collection book, daily quests, login calendar and battle pass.</summary>
public sealed class ProgressionService
{
    private readonly PlayerOperations _ops;

    public ProgressionService(PlayerOperations ops)
    {
        _ops = ops;
    }

    public Task<AchievementsResponse> GetAchievementsAsync(Guid userId, CancellationToken ct) =>
        _ops.ReadAsync(userId, (ws, _) =>
        {
            AchievementSystem system = ws.Achievements;
            return Task.FromResult(new AchievementsResponse
            {
                Achievements = AchievementCatalog.All.Select(a => new AchievementDto
                {
                    Id = a.Id,
                    Type = a.Type.ToString(),
                    Page = a.Page,
                    Target = a.Target,
                    Progress = Math.Min(a.Target, system.GetStat(a.Stat)),
                    Unlocked = system.IsUnlocked(a.Id),
                    Claimed = system.IsClaimed(a.Id),
                    Reward = Mappers.Reward(a.Reward)
                }).ToList(),
                PagesCompleted = ws.State.Achievements.PagesCompleted.OrderBy(p => p).ToList(),
                CoinBonus = system.GetTotalCoinBonus()
            });
        }, ct);

    public Task<ClaimResponse> ClaimAchievementAsync(Guid userId, ClaimAchievementRequest request, CancellationToken ct) =>
        _ops.RunAsync(userId, ctx =>
        {
            PlayerWorkspace ws = ctx.Player;
            string id = request?.Id ?? string.Empty;
            if (AchievementCatalog.Get(id) == null)
            {
                throw new ApiException(ErrorCode.NotFound);
            }
            if (ws.Achievements.IsClaimed(id))
            {
                throw new ApiException(ErrorCode.AlreadyClaimed);
            }

            int pagesBefore = ws.State.Achievements.PagesCompleted.Count;
            RewardData reward = ws.Achievements.UnlockAchievement(id) ?? throw new ApiException(ErrorCode.FeatureLocked, "Achievement not unlocked yet.");
            ws.GrantReward(reward, TransactionReason.AchievementReward, "ach:" + id, "ach:" + id);

            return new ClaimResponse
            {
                Reward = Mappers.Reward(reward),
                Wallet = Mappers.Wallet(ws),
                PageCompleted = ws.State.Achievements.PagesCompleted.Count > pagesBefore
            };
        }, ct);

    public Task<QuestsResponse> GetQuestsAsync(Guid userId, CancellationToken ct) =>
        _ops.RunAsync(userId, ctx =>
        {
            PlayerWorkspace ws = ctx.Player;
            ws.EnsureDailyState();
            return new QuestsResponse
            {
                Quests = ws.State.Quests.Quests.Select(q => new QuestDto { Id = q.Id, Type = q.Type.ToString(), Target = q.Target, Progress = q.Progress, Claimed = q.Claimed }).ToList(),
                ResetAtUnixMs = TimeUtil.ToUnixMs(TimeUtil.NextDailyReset(ws.Now))
            };
        }, ct);

    public Task<ClaimResponse> ClaimQuestAsync(Guid userId, string questId, CancellationToken ct) =>
        _ops.RunAsync(userId, ctx =>
        {
            PlayerWorkspace ws = ctx.Player;
            ws.EnsureDailyState();
            RewardData reward = DailyQuests.Claim(ws.State.Quests, questId ?? string.Empty, ws.Balance.LiveOps).ValueOrThrow();
            ws.GrantReward(reward, TransactionReason.QuestReward, "quest:" + questId, "quest:" + questId);
            ws.Achievements.IncrementStat(StatKey.QuestsCompleted, 1);
            return new ClaimResponse { Reward = Mappers.Reward(reward), Wallet = Mappers.Wallet(ws) };
        }, ct);

    public Task<LoginBonusResponse> ClaimLoginBonusAsync(Guid userId, CancellationToken ct) =>
        _ops.RunAsync(userId, ctx =>
        {
            PlayerWorkspace ws = ctx.Player;
            int day = TimeUtil.DayIndex(ws.Now);
            RewardData reward = LoginCalendar.Claim(ws.State.Login, ws.Now, ws.Balance.LiveOps).ValueOrThrow();
            ws.GrantReward(reward, TransactionReason.LoginReward, "login:" + day, "login:" + day);
            ws.Achievements.SetStatMax(StatKey.LoginDays, ws.State.Login.TotalLoginDays);
            return new LoginBonusResponse
            {
                Reward = Mappers.Reward(reward),
                NextSlot = ws.State.Login.NextSlot,
                TotalLoginDays = ws.State.Login.TotalLoginDays,
                Wallet = Mappers.Wallet(ws)
            };
        }, ct);

    public Task<BattlePassResponse> GetBattlePassAsync(Guid userId, CancellationToken ct) =>
        _ops.RunAsync(userId, ctx =>
        {
            PlayerWorkspace ws = ctx.Player;
            BattlePass.EnsureSeason(ws.State.BattlePass, ws.Now, ws.Balance.LiveOps);
            BattlePassState bp = ws.State.BattlePass;
            var live = ws.Balance.LiveOps;
            return new BattlePassResponse
            {
                Season = bp.SeasonIndex,
                Xp = bp.Xp,
                Tier = BattlePass.TierForXp(bp.Xp, live),
                XpPerTier = live.BattlePassXpPerTier,
                Premium = ws.State.Inventory.PremiumPassSeason == bp.SeasonIndex,
                TierPriceOrbes = BattlePass.TierPrice(bp, ws.Balance.Economy),
                SeasonEndUnixMs = TimeUtil.ToUnixMs(BattlePass.SeasonEndUtc(bp.SeasonIndex, live)),
                Tiers = Enumerable.Range(1, live.BattlePassTiers).Select(tier =>
                {
                    BattlePassTierReward reward = BattlePass.GetTierReward(bp.SeasonIndex, tier);
                    return new BattlePassTierDto
                    {
                        Tier = tier,
                        Free = Mappers.Reward(reward.Free),
                        Premium = Mappers.Reward(reward.Premium),
                        FreeClaimed = bp.ClaimedFree.Contains(tier),
                        PremiumClaimed = bp.ClaimedPremium.Contains(tier)
                    };
                }).ToList()
            };
        }, ct);

    /// <summary>A mis-tap must never empty a wallet: one call can never buy more than this.</summary>
    private const int MaxTiersPerPurchase = 10;

    /// <summary>
    /// Buys the next tier with orbes. The pass itself stays a real-money purchase; this only sells impatience, and
    /// the price climbs with every tier bought, so buying the whole track is never the cheap way through.
    /// </summary>
    public async Task<BattlePassResponse> BuyBattlePassTierAsync(Guid userId, BattlePassTierPurchase request, CancellationToken ct)
    {
        await _ops.RunAsync(userId, ctx =>
        {
            PlayerWorkspace ws = ctx.Player;
            BattlePass.EnsureSeason(ws.State.BattlePass, ws.Now, ws.Balance.LiveOps);
            BattlePassState bp = ws.State.BattlePass;
            int tiers = Math.Min(Math.Max(1, request?.Tiers ?? 1), MaxTiersPerPurchase);
            for (int i = 0; i < tiers; i++)
            {
                int price = BattlePass.TierPrice(bp, ws.Balance.Economy);
                ws.Wallet.Debit(Currency.Orbes, price, TransactionReason.BattlePassReward, "bp:tier:" + bp.SeasonIndex + ":" + bp.TiersBought).ThrowIfFailed();
                BattlePass.BuyTier(bp, ws.Balance.LiveOps).ThrowIfFailed();
            }
            return true;
        }, ct).ConfigureAwait(false);
        return await GetBattlePassAsync(userId, ct).ConfigureAwait(false);
    }

    public Task<ClaimResponse> ClaimBattlePassAsync(Guid userId, BattlePassClaimRequest request, CancellationToken ct) =>
        _ops.RunAsync(userId, ctx =>
        {
            PlayerWorkspace ws = ctx.Player;
            BattlePass.EnsureSeason(ws.State.BattlePass, ws.Now, ws.Balance.LiveOps);
            int season = ws.State.BattlePass.SeasonIndex;
            int tier = request?.Tier ?? 0;
            bool premium = request?.Premium ?? false;
            bool hasPremium = ws.State.Inventory.PremiumPassSeason == season;

            RewardData reward = BattlePass.Claim(ws.State.BattlePass, tier, premium, hasPremium, ws.Balance.LiveOps).ValueOrThrow();
            string key = "bp:" + season + ":" + tier + (premium ? ":p" : ":f");
            ws.GrantReward(reward, TransactionReason.BattlePassReward, key, key);
            return new ClaimResponse { Reward = Mappers.Reward(reward), Wallet = Mappers.Wallet(ws) };
        }, ct);
}
