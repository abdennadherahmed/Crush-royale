using CrushRoyale.Contracts;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Economy;
using CrushRoyale.Core.Gameplay;
using CrushRoyale.Core.Progression;
using CrushRoyale.Core.Replay;
using CrushRoyale.Core.Story;
using CrushRoyale.Server.Infrastructure;
using CrushRoyale.Server.Persistence;

namespace CrushRoyale.Server.Services;

/// <summary>
/// Story campaign: stage data, match start (life + loadout), paid continues, server-validated completion with
/// rewards, and branching choices.
/// </summary>
public sealed class StoryService
{
    private readonly PlayerOperations _ops;

    public StoryService(PlayerOperations ops)
    {
        _ops = ops;
    }

    public Task<StageData> GetStageAsync(Guid userId, int stageId, CancellationToken ct) =>
        _ops.ReadAsync(userId, (ws, _) =>
        {
            if (!ws.Story.CanPlay(stageId))
            {
                throw new ApiException(ErrorCode.StageLocked);
            }
            return Task.FromResult(ws.Catalog.Get(stageId));
        }, ct);

    public Task<MatchStartResponse> StartAsync(Guid userId, int stageId, StartStageRequest request, CancellationToken ct) =>
        _ops.RunAsync(userId, async ctx =>
        {
            PlayerWorkspace ws = ctx.Player;
            if (!ws.Story.CanPlay(stageId))
            {
                throw new ApiException(ErrorCode.StageLocked);
            }

            List<LoadoutEntry> loadout = ws.Inventory.BuildLoadout(Mappers.ParseLoadout(request?.Loadout, ws.Balance), ws.Balance, ws.State.Pvp.HighestLeague).ValueOrThrow();
            if (!ws.Stamina.ConsumeLive())
            {
                throw new ApiException(ErrorCode.NotEnoughLives);
            }

            StageData stage = ws.Catalog.Get(stageId);
            var match = new MatchRow
            {
                Id = Mappers.NewId("st"),
                PlayerId = userId,
                Mode = GameMode.Story,
                Seed = stage.Seed,
                StageId = stageId,
                Status = MatchStatus.Started,
                StartedAt = ws.Now,
                Config = new MatchConfigSnapshot
                {
                    Loadout = loadout,
                    HighestLeague = ws.State.Pvp.HighestLeague,
                    AssistExtraMoves = ws.Story.GetAssistExtraMoves(stageId),
                    StartBoosters = ws.Story.GetStreakBoosters(stageId)
                }
            };
            ws.SnapshotPet(match.Config, GameMode.Story);
            await ctx.Tx.InsertMatchAsync(match).ConfigureAwait(false);
            return Mappers.MatchStart(match, ws, _ops.Balance.HashHex);
        }, ct);

    public Task<ContinueResponse> ContinueAsync(Guid userId, string matchId, ContinueRequest request, CancellationToken ct) =>
        _ops.RunAsync(userId, async ctx =>
        {
            PlayerWorkspace ws = ctx.Player;
            MatchRow match = await OwnedOpenMatch(ctx, matchId, GameMode.Story).ConfigureAwait(false);
            if (match.Config.ContinuesAuthorized >= SessionConfig.MaxContinues)
            {
                throw new ApiException(ErrorCode.LimitReached);
            }

            var response = new ContinueResponse();
            if (request?.UseFreeContinue == true && ws.Stamina.UseFreeContinue())
            {
                response.Free = true;
            }
            else
            {
                int price = ws.Stamina.GetContinueOrbePrice(match.Config.ContinuesAuthorized);
                ws.Wallet.Debit(Currency.Orbes, price, TransactionReason.ContinuePurchase, match.Id, match.Id + ":continue:" + match.Config.ContinuesAuthorized).ThrowIfFailed();
                response.OrbesSpent = price;
            }

            match.Config.ContinuesAuthorized++;
            await ctx.Tx.UpdateMatchAsync(match).ConfigureAwait(false);
            response.ContinuesAuthorized = match.Config.ContinuesAuthorized;
            response.Wallet = Mappers.Wallet(ws);
            return response;
        }, ct);

    public Task<StageCompleteResponse> CompleteAsync(Guid userId, string matchId, string? replayBase64, CancellationToken ct) =>
        _ops.RunAsync(userId, async ctx =>
        {
            PlayerWorkspace ws = ctx.Player;
            MatchRow match = await OwnedOpenMatch(ctx, matchId, GameMode.Story).ConfigureAwait(false);
            ReplayData replay = Mappers.DecodeReplay(replayBase64);

            ErrorCode header = ReplayGuard.CheckHeader(replay, match, userId);
            if (header != ErrorCode.None)
            {
                ReplayGuard.FlagHeaderMismatch(ws, match, header);
                return await Reject(ctx, match, header).ConfigureAwait(false);
            }

            StageData stage = ws.Catalog.Get(match.StageId);
            SessionConfig config = SessionConfig.ForStage(stage, ws.Balance, match.Config.Loadout, match.Config.HighestLeague, match.Config.AssistExtraMoves)
                .WithPet(match.Config.Pet, match.Config.PetLevel, ws.Balance)
                .WithStartBoosters(match.Config.StartBoosters);

            bool timingOk = ws.AntiCheat.ValidateTimestamps(ws.State.Integrity, replay, TimeUtil.ToUnixMs(match.StartedAt), ws.NowMs, match.Id);
            ReplayVerification verification = ws.AntiCheat.ValidateReplay(ws.State.Integrity, replay, config, match.Id);
            if (!verification.Valid || !timingOk)
            {
                return await Reject(ctx, match, verification.Valid ? ErrorCode.ReplayMismatch : verification.Error).ConfigureAwait(false);
            }

            StageResult result = verification.Session.GetResult();
            ReplayGuard.ConsumePowerUps(ws, result.PowerUpsUsed, match.Id);

            StageStatus status = result.State switch
            {
                SessionState.Won => StageStatus.Won,
                SessionState.Lost => StageStatus.Lost,
                _ => StageStatus.Abandoned
            };
            if (status == StageStatus.Won)
            {
                ws.Stamina.RefundLife();
            }

            StageCompletion completion = ws.Story.CompleteStage(stage.Id, status, result.FinalScore, result.Stars);
            if (!completion.Accepted)
            {
                return await Reject(ctx, match, completion.Error).ConfigureAwait(false);
            }

            string? chest = null;
            if (completion.FirstWin && stage.Id == ws.Balance.Chests.WelcomeChestStage)
            {
                chest = ws.GrantChest(ChestType.Silver, "welcome", ws.Balance.Chests.WelcomeChestUnlockSeconds);
            }
            else if (completion.FirstWin && stage.BossKind >= BossKind.ChapterBoss)
            {
                chest = ws.GrantChest(stage.BossKind >= BossKind.ActBoss ? ChestType.Crystal : ChestType.Gold, "boss:" + stage.Id);
            }

            long coins = ws.CreditEarnedCoins(completion.BaseCoins, TransactionReason.StageReward, match.Id, match.Id + ":coins");
            long orbes = ws.CreditEarnedOrbes(completion.Orbes, TransactionReason.StageReward, match.Id, match.Id + ":orbes");

            ws.Achievements.RecordStageResult(result, stage, ws.State.Story);
            ws.Achievements.SetStatMax(StatKey.CharactersMet, ws.Story.GetParty().Count - 1);
            if (status == StageStatus.Won)
            {
                ws.TrackQuest(QuestType.WinStages, 1);
                ws.TrackQuest(QuestType.EarnStars, result.Stars);
            }
            ws.TrackQuest(QuestType.TriggerCascades, result.TotalCascades);
            ws.TrackQuest(QuestType.CreateSpecials, result.SpecialsCreated);
            ws.TrackQuest(QuestType.BreakStones, result.StonesDestroyed);
            ws.TrackQuest(QuestType.UsePowerUps, result.PowerUpsUsed.Values.Sum());
            ws.AddBattlePassXp(status == StageStatus.Won ? ws.Balance.LiveOps.XpStoryWin : ws.Balance.LiveOps.XpStoryLoss);
            ws.AwardPetXp(match.Config, GameMode.Story, status == StageStatus.Won);

            bool showAd = status == StageStatus.Won && AdPolicy.OnEvent(ws.State.Ads, AdPlacement.StoryWin, ws.Now, ws.State.Story.HighestUnlockedStage,
                ws.State.Inventory.AdsRemoved, new VipSystem(ws.Balance).GetBenefit(ws.State.Vip.Tier), ws.Balance);

            long replayId = await ctx.Tx.InsertReplayAsync(new ReplayRow
            {
                PlayerId = userId,
                Mode = GameMode.Story,
                Seed = match.Seed,
                StageId = match.StageId,
                Trophies = ws.State.Pvp.Trophies,
                FinalScore = result.FinalScore,
                Region = ws.State.Region,
                Data = Convert.FromBase64String(replayBase64!),
                IsGhost = false,
                CreatedAt = ws.Now
            }).ConfigureAwait(false);

            match.Status = MatchStatus.Completed;
            match.ReplayId = replayId;
            match.FinishedAt = ws.Now;
            match.ResultJson = Json.Serialize(new { state = result.State.ToString(), score = result.FinalScore, stars = result.Stars, coins, orbes });
            await ctx.Tx.UpdateMatchAsync(match).ConfigureAwait(false);

            return new StageCompleteResponse
            {
                Accepted = true,
                State = result.State.ToString(),
                Score = result.FinalScore,
                Stars = completion.Stars,
                FirstWin = completion.FirstWin,
                CoinsEarned = coins,
                OrbesEarned = orbes,
                NextStage = completion.NextStage,
                FeaturesUnlocked = completion.FeaturesUnlocked.Select(f => f.ToString()).ToList(),
                Events = completion.Events.Select(Mappers.Event).ToList(),
                CharactersJoined = completion.CharactersJoined,
                AchievementsUnlocked = ws.UnlockedAchievements.ToList(),
                ShowInterstitial = showAd,
                ChestEarned = chest,
                WinStreak = ws.State.Story.WinStreak,
                StreakLost = completion.StreakLost,
                Wallet = Mappers.Wallet(ws),
                Lives = Mappers.Lives(ws)
            };
        }, ct);

    public Task<ChapterChestResponse> ClaimChapterChestAsync(Guid userId, ChapterChestRequest request, CancellationToken ct) =>
        _ops.RunAsync(userId, ctx =>
        {
            PlayerWorkspace ws = ctx.Player;
            int chapter = request?.Chapter ?? 0;
            int tier = request?.Tier ?? -1;
            ChapterChestReward chest = ChapterChests.Claim(ws.State.Story, ws.Balance.Story, chapter, tier).ValueOrThrow();
            string key = "chapter-chest:" + ChapterChests.Key(chapter, tier);
            ws.GrantReward(chest.Reward, TransactionReason.ChapterChest, key, key);
            if (chest.PetFragments > 0)
            {
                ws.Pets.Get(chest.FragmentsPet).Fragments += chest.PetFragments;
            }
            return new ChapterChestResponse
            {
                Reward = Mappers.Reward(chest.Reward),
                PetFragments = chest.PetFragments,
                FragmentsPet = chest.PetFragments > 0 ? chest.FragmentsPet.ToString() : null,
                Wallet = Mappers.Wallet(ws),
                Inventory = Mappers.Inventory(ws),
                Pets = Mappers.Pets(ws),
                Story = Mappers.Story(ws)
            };
        }, ct);

    public Task<ChoiceResponse> MakeChoiceAsync(Guid userId, ChoiceRequest request, CancellationToken ct) =>
        _ops.RunAsync(userId, ctx =>
        {
            PlayerWorkspace ws = ctx.Player;
            ws.Story.MakeChoice(request?.ChoiceId ?? string.Empty, request?.OptionId ?? string.Empty).ThrowIfFailed();
            ws.Achievements.SetStatMax(StatKey.ChoicesMade, ws.State.Story.Choices.Count);

            var response = new ChoiceResponse();
            if (request!.ChoiceId == StoryDatabase.FinalChoiceId && ws.State.Story.Ending is StoryEnding ending)
            {
                response.Ending = ending.ToString();
                ws.State.EndingsReached.Add(ending);
                ws.Achievements.RecordEnding(ws.State.EndingsReached.Count);

                var reward = new RewardData();
                reward.Cosmetics.Add("board.valdorax");
                if (ending == StoryEnding.Corruption)
                {
                    reward.Cosmetics.Add("outfit.corrupted");
                }
                if (ending == StoryEnding.Redemption)
                {
                    reward.Cosmetics.Add("outfit.purified");
                }
                foreach (string cosmetic in reward.Cosmetics)
                {
                    if (ws.Inventory.AddCosmetic(cosmetic))
                    {
                        response.CosmeticsGranted.Add(cosmetic);
                    }
                }
            }
            response.AchievementsUnlocked = ws.UnlockedAchievements.ToList();
            return response;
        }, ct);

    public Task<bool> MarkEventSeenAsync(Guid userId, string eventId, CancellationToken ct) =>
        _ops.RunAsync(userId, ctx =>
        {
            if (string.IsNullOrWhiteSpace(eventId) || eventId.Length > 64)
            {
                throw new ApiException(ErrorCode.InvalidArgument);
            }
            ctx.Player.Story.MarkEventSeen(eventId);
            return true;
        }, ct);

    private static async Task<MatchRow> OwnedOpenMatch(OperationContext ctx, string matchId, GameMode mode)
    {
        MatchRow? match = string.IsNullOrWhiteSpace(matchId) ? null : await ctx.Tx.GetMatchAsync(matchId).ConfigureAwait(false);
        if (match == null || match.PlayerId != ctx.Player.Id || match.Mode != mode)
        {
            throw new ApiException(ErrorCode.NotFound, "Match not found.");
        }
        if (match.Status != MatchStatus.Started)
        {
            throw new ApiException(ErrorCode.SessionOver, "Match already " + match.Status + ".");
        }
        return match;
    }

    private static async Task<StageCompleteResponse> Reject(OperationContext ctx, MatchRow match, ErrorCode error)
    {
        match.Status = MatchStatus.Rejected;
        match.FinishedAt = ctx.Player.Now;
        match.ResultJson = Json.Serialize(new { error = error.ToString() });
        await ctx.Tx.UpdateMatchAsync(match).ConfigureAwait(false);
        return new StageCompleteResponse
        {
            Accepted = false,
            Error = error.ToString(),
            Wallet = Mappers.Wallet(ctx.Player),
            Lives = Mappers.Lives(ctx.Player)
        };
    }
}
