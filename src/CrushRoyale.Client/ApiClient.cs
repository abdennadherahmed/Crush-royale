using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CrushRoyale.Contracts;

namespace CrushRoyale.Client
{
    /// <summary>
    /// HTTP transport: bearer token, JSON, 10 s timeout, up to 3 attempts with exponential backoff,
    /// honours Retry-After on 429/503, transparently refreshes the token once on 401.
    /// Non-idempotent POSTs are only retried when the request could not reach the server or was explicitly refused (429/503).
    /// </summary>
    public sealed class ApiClient
    {
        private readonly ClientOptions _options;
        private readonly HttpClient _http;
        private readonly Func<CancellationToken, Task<string>> _accessToken;
        private readonly Func<CancellationToken, Task<bool>> _refresh;
        private readonly Func<TimeSpan, CancellationToken, Task> _delay;

        public ApiClient(ClientOptions options, HttpClient http, Func<CancellationToken, Task<string>> accessToken, Func<CancellationToken, Task<bool>> refresh, Func<TimeSpan, CancellationToken, Task> delay = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _http = http ?? throw new ArgumentNullException(nameof(http));
            _accessToken = accessToken;
            _refresh = refresh;
            _delay = delay ?? Task.Delay;
        }

        /// <summary>Raised for every failed call (analytics / debug overlay).</summary>
        public event Action<string, CrushApiException> RequestFailed;

        public async Task<T> SendAsync<T>(HttpMethod method, string route, object body, CancellationToken cancellationToken = default)
        {
            string json = await SendRawAsync(method, route, body == null ? null : JsonSettings.Serialize(body), cancellationToken).ConfigureAwait(false);
            return string.IsNullOrWhiteSpace(json) ? default : JsonSettings.Deserialize<T>(json);
        }

        public Task<T> GetAsync<T>(string route, CancellationToken cancellationToken = default) => SendAsync<T>(HttpMethod.Get, route, null, cancellationToken);

        public Task<T> PostAsync<T>(string route, object body, CancellationToken cancellationToken = default) => SendAsync<T>(HttpMethod.Post, route, body ?? new object(), cancellationToken);

        public Task<T> PutAsync<T>(string route, object body, CancellationToken cancellationToken = default) => SendAsync<T>(HttpMethod.Put, route, body ?? new object(), cancellationToken);

        public Task<T> DeleteAsync<T>(string route, CancellationToken cancellationToken = default) => SendAsync<T>(HttpMethod.Delete, route, null, cancellationToken);

        /// <summary>Sends a pre-serialized body and returns the raw response JSON (used by the offline queue).</summary>
        public async Task<string> SendRawAsync(HttpMethod method, string route, string bodyJson, CancellationToken cancellationToken = default)
        {
            bool refreshed = false;
            int maxAttempts = Math.Max(1, _options.MaxAttempts);

            for (int attempt = 1; ; attempt++)
            {
                using (var request = new HttpRequestMessage(method, _options.ApiBaseUrl.TrimEnd('/') + route))
                {
                    string token = _accessToken == null ? null : await _accessToken(cancellationToken).ConfigureAwait(false);
                    if (token != null)
                    {
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                    }
                    if (bodyJson != null)
                    {
                        request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
                    }

                    HttpResponseMessage response;
                    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                    {
                        timeout.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
                        try
                        {
                            response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
                        }
                        catch (HttpRequestException ex)
                        {
                            if (attempt < maxAttempts)
                            {
                                await _delay(Backoff(attempt), cancellationToken).ConfigureAwait(false);
                                continue;
                            }
                            throw Fail(route, new CrushApiException("Network", 0, ex.Message, isNetwork: true));
                        }
                        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                        {
                            if (IsIdempotent(method) && attempt < maxAttempts)
                            {
                                await _delay(Backoff(attempt), cancellationToken).ConfigureAwait(false);
                                continue;
                            }
                            throw Fail(route, new CrushApiException("Timeout", 0, "The server did not answer in time.", isNetwork: true));
                        }
                    }

                    using (response)
                    {
                        string text = response.Content == null ? string.Empty : await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (response.IsSuccessStatusCode)
                        {
                            return text;
                        }

                        int status = (int)response.StatusCode;
                        if (status == 401 && !refreshed && _refresh != null && await _refresh(cancellationToken).ConfigureAwait(false))
                        {
                            refreshed = true;
                            attempt--;
                            continue;
                        }

                        bool retry = status == 429 || status == 503 || (status >= 500 && IsIdempotent(method));
                        if (retry && attempt < maxAttempts)
                        {
                            await _delay(RetryAfter(response) ?? Backoff(attempt), cancellationToken).ConfigureAwait(false);
                            continue;
                        }

                        throw Fail(route, ToException(status, text));
                    }
                }
            }
        }

        internal static CrushApiException ToException(int status, string text)
        {
            try
            {
                ApiErrorDto error = JsonSettings.Deserialize<ApiErrorDto>(text);
                if (error?.Code != null)
                {
                    return new CrushApiException(error.Code, status, error.Message ?? error.Code);
                }
            }
            catch (Newtonsoft.Json.JsonException)
            {
                // Non-JSON body (proxy error page...).
            }
            return new CrushApiException(status == 401 ? "Unauthorized" : status == 429 ? "RateLimited" : "Http" + status, status, string.IsNullOrWhiteSpace(text) ? "HTTP " + status : text);
        }

        private CrushApiException Fail(string route, CrushApiException ex)
        {
            RequestFailed?.Invoke(route, ex);
            return ex;
        }

        private static bool IsIdempotent(HttpMethod method) => method == HttpMethod.Get || method == HttpMethod.Put || method == HttpMethod.Delete;

        private static TimeSpan Backoff(int attempt) => TimeSpan.FromMilliseconds(300 * Math.Pow(2, attempt - 1));

        private static TimeSpan? RetryAfter(HttpResponseMessage response)
        {
            RetryConditionHeaderValue header = response.Headers.RetryAfter;
            if (header?.Delta != null)
            {
                return header.Delta.Value > TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(30) : header.Delta.Value;
            }
            return null;
        }
    }

    /// <summary>Typed Crush Royale API (routes and contracts shared with the server).</summary>
    public sealed class CrushApi
    {
        public CrushApi(ApiClient transport)
        {
            Transport = transport ?? throw new ArgumentNullException(nameof(transport));
        }

        public ApiClient Transport { get; }

        private static string Id(object value) => Convert.ToString(value, CultureInfo.InvariantCulture);

        // Profile
        public Task<ConfigResponse> GetConfigAsync(CancellationToken ct = default) => Transport.GetAsync<ConfigResponse>(ApiRoutes.Config, ct);
        public Task<LoginResponse> LoginAsync(LoginRequest request, CancellationToken ct = default) => Transport.PostAsync<LoginResponse>(ApiRoutes.Login, request, ct);
        public Task<ProfileDto> GetMeAsync(CancellationToken ct = default) => Transport.GetAsync<ProfileDto>(ApiRoutes.Me, ct);
        public Task<bool> DeleteMeAsync(CancellationToken ct = default) => Transport.DeleteAsync<bool>(ApiRoutes.Me, ct);
        public Task<ProfileDto> SetHeroAsync(SetHeroRequest request, CancellationToken ct = default) => Transport.PutAsync<ProfileDto>(ApiRoutes.Hero, request, ct);
        public Task<PlayerStatsDto> GetPlayerStatsAsync(string playerId, CancellationToken ct = default) => Transport.GetAsync<PlayerStatsDto>(ApiRoutes.Fill(ApiRoutes.PlayerStats, "id", playerId), ct);
        public Task<SearchPlayersResponse> SearchPlayersAsync(string query, CancellationToken ct = default) => Transport.GetAsync<SearchPlayersResponse>(ApiRoutes.PlayerSearch + "?q=" + Uri.EscapeDataString(query ?? string.Empty), ct);

        // Story
        public Task<MatchStartResponse> StartStageAsync(int stageId, StartStageRequest request, CancellationToken ct = default) => Transport.PostAsync<MatchStartResponse>(ApiRoutes.Fill(ApiRoutes.StageStart, "stageId", Id(stageId)), request, ct);
        public Task<ContinueResponse> ContinueStageAsync(string matchId, bool useFree, CancellationToken ct = default) => Transport.PostAsync<ContinueResponse>(ApiRoutes.Fill(ApiRoutes.StageContinue, "matchId", matchId), new ContinueRequest { UseFreeContinue = useFree }, ct);
        public Task<StageCompleteResponse> CompleteStageAsync(SubmitReplayRequest request, CancellationToken ct = default) => Transport.PostAsync<StageCompleteResponse>(ApiRoutes.Fill(ApiRoutes.StageComplete, "matchId", request.MatchId), request, ct);
        public Task<ChoiceResponse> MakeChoiceAsync(string choiceId, string optionId, CancellationToken ct = default) => Transport.PostAsync<ChoiceResponse>(ApiRoutes.StoryChoice, new ChoiceRequest { ChoiceId = choiceId, OptionId = optionId }, ct);
        public Task<VipGiftResponse> ClaimVipGiftAsync(CancellationToken ct = default) => Transport.PostAsync<VipGiftResponse>(ApiRoutes.VipGift, null, ct);

        public Task<RestorationBuildResponse> BuildRestorationAsync(string taskId, CancellationToken ct = default) =>
            Transport.PostAsync<RestorationBuildResponse>(ApiRoutes.RestorationBuild, new RestorationBuildRequest { TaskId = taskId }, ct);

        public Task<WheelSpinResponse> SpinWheelAsync(CancellationToken ct = default) => Transport.PostAsync<WheelSpinResponse>(ApiRoutes.WheelSpin, null, ct);

        public Task<ChapterChestResponse> ClaimChapterChestAsync(int chapter, int tier, CancellationToken ct = default) =>
            Transport.PostAsync<ChapterChestResponse>(ApiRoutes.ChapterChest, new ChapterChestRequest { Chapter = chapter, Tier = tier }, ct);

        public Task<bool> MarkEventSeenAsync(string eventId, CancellationToken ct = default) => Transport.PostAsync<bool>(ApiRoutes.Fill(ApiRoutes.StoryEventSeen, "eventId", eventId), null, ct);

        // Economy
        public Task<PurchaseResponse> BuyLivesAsync(int count, CancellationToken ct = default) => Transport.PostAsync<PurchaseResponse>(ApiRoutes.LivesBuy, new BuyLivesRequest { Count = count }, ct);
        public Task<PurchaseResponse> ClaimVipLifeAsync(CancellationToken ct = default) => Transport.PostAsync<PurchaseResponse>(ApiRoutes.LivesVipClaim, null, ct);
        public Task<ChallengeCreateResponse> CreateChallengeAsync(CancellationToken ct = default) => Transport.PostAsync<ChallengeCreateResponse>(ApiRoutes.ChallengeCreate, null, ct);

        public Task<MatchStartResponse> AcceptChallengeAsync(string code, List<string> loadout, CancellationToken ct = default) =>
            Transport.PostAsync<MatchStartResponse>(ApiRoutes.Fill(ApiRoutes.ChallengeStart, "code", Uri.EscapeDataString(code)), new StartStageRequest { Loadout = loadout ?? new List<string>() }, ct);

        public Task<ChestsDto> UnlockChestAsync(int slot, CancellationToken ct = default) => Transport.PostAsync<ChestsDto>(ApiRoutes.Fill(ApiRoutes.ChestUnlock, "slot", slot), null, ct);

        public Task<ChestOpenResponse> OpenChestAsync(int slot, bool useOrbes, CancellationToken ct = default) => Transport.PostAsync<ChestOpenResponse>(ApiRoutes.Fill(ApiRoutes.ChestOpen, "slot", slot), new ChestOpenRequest { UseOrbes = useOrbes }, ct);

        public Task<object> SendTelemetryAsync(TelemetryRequest request, CancellationToken ct = default) => Transport.PostAsync<object>(ApiRoutes.Telemetry, request, ct);

        public Task<PetSummonResponse> SummonPetsAsync(int count, CancellationToken ct = default) => Transport.PostAsync<PetSummonResponse>(ApiRoutes.PetSummon, new PetSummonRequest { Count = count }, ct);

        public Task<PetActionResponse> EquipPetAsync(string pet, CancellationToken ct = default) => Transport.PostAsync<PetActionResponse>(ApiRoutes.PetEquip, new PetRequest { Pet = pet }, ct);

        public Task<PetActionResponse> UnlockPetAsync(string pet, CancellationToken ct = default) => Transport.PostAsync<PetActionResponse>(ApiRoutes.PetUnlock, new PetRequest { Pet = pet }, ct);

        public Task<PetActionResponse> ConvertPetFragmentsAsync(string from, string to, int count, CancellationToken ct = default) =>
            Transport.PostAsync<PetActionResponse>(ApiRoutes.PetConvert, new PetConvertRequest { From = from, To = to, Count = count }, ct);

        public Task<PetActionResponse> AwakenPetAsync(string pet, CancellationToken ct = default) => Transport.PostAsync<PetActionResponse>(ApiRoutes.PetAwaken, new PetRequest { Pet = pet }, ct);

        public Task<ShopResponse> GetShopAsync(CancellationToken ct = default) => Transport.GetAsync<ShopResponse>(ApiRoutes.Shop, ct);
        public Task<ShopResponse> RefreshShopAsync(CancellationToken ct = default) => Transport.PostAsync<ShopResponse>(ApiRoutes.ShopRefresh, null, ct);
        public Task<PurchaseResponse> PurchaseAsync(string itemId, string method, CancellationToken ct = default) => Transport.PostAsync<PurchaseResponse>(ApiRoutes.ShopPurchase, new PurchaseRequest { ItemId = itemId, Method = method }, ct);
        public Task<IapPrecheckResponse> PrecheckPurchaseAsync(string sku, CancellationToken ct = default) => Transport.PostAsync<IapPrecheckResponse>(ApiRoutes.IapPrecheck, new IapPrecheckRequest { Sku = sku }, ct);
        public Task<IapValidateResponse> ValidatePurchaseAsync(IapValidateRequest request, CancellationToken ct = default) => Transport.PostAsync<IapValidateResponse>(ApiRoutes.IapValidate, request, ct);
        public Task<InventoryDto> EquipAsync(string cosmeticId, CancellationToken ct = default) => Transport.PostAsync<InventoryDto>(ApiRoutes.Equip, new EquipRequest { CosmeticId = cosmeticId }, ct);
        public Task<RewardedAdResponse> RewardedAdAsync(string placement, string adToken, CancellationToken ct = default) => Transport.PostAsync<RewardedAdResponse>(ApiRoutes.RewardedAd, new RewardedAdRequest { Placement = placement, AdToken = adToken }, ct);

        // PvP
        public Task<MatchmakingStatusResponse> RequestMatchAsync(MatchmakingRequest request, CancellationToken ct = default) => Transport.PostAsync<MatchmakingStatusResponse>(ApiRoutes.Matchmaking, request, ct);
        public Task<MatchmakingStatusResponse> GetMatchmakingStatusAsync(CancellationToken ct = default) => Transport.GetAsync<MatchmakingStatusResponse>(ApiRoutes.Matchmaking, ct);
        public Task<object> CancelMatchmakingAsync(CancellationToken ct = default) => Transport.DeleteAsync<object>(ApiRoutes.Matchmaking, ct);
        public Task<MatchInfoResponse> GetMatchAsync(string matchId, CancellationToken ct = default) => Transport.GetAsync<MatchInfoResponse>(ApiRoutes.Fill(ApiRoutes.PvpMatch, "matchId", matchId), ct);
        public Task<PvpResultDto> RecordPvpAsync(SubmitReplayRequest request, CancellationToken ct = default) => Transport.PostAsync<PvpResultDto>(ApiRoutes.PvpRecord, request, ct);
        public Task<LeaderboardResponse> GetWeeklyLeaderboardAsync(string league, int limit = 100, CancellationToken ct = default) => Transport.GetAsync<LeaderboardResponse>(ApiRoutes.LeaderboardWeekly + "?limit=" + Id(limit) + (string.IsNullOrEmpty(league) ? string.Empty : "&league=" + Uri.EscapeDataString(league)), ct);
        public Task<GuildLeaderboardResponse> GetGuildLeaderboardAsync(CancellationToken ct = default) => Transport.GetAsync<GuildLeaderboardResponse>(ApiRoutes.LeaderboardGuilds, ct);

        // Social
        public Task<FriendsResponse> GetFriendsAsync(CancellationToken ct = default) => Transport.GetAsync<FriendsResponse>(ApiRoutes.Friends, ct);
        public Task<FriendsResponse> SendFriendRequestAsync(string playerId, CancellationToken ct = default) => Transport.PostAsync<FriendsResponse>(ApiRoutes.FriendRequest, new FriendTargetRequest { PlayerId = playerId }, ct);
        public Task<FriendsResponse> AcceptFriendAsync(string playerId, CancellationToken ct = default) => Transport.PostAsync<FriendsResponse>(ApiRoutes.FriendAccept, new FriendTargetRequest { PlayerId = playerId }, ct);
        public Task<FriendsResponse> DeclineFriendAsync(string playerId, CancellationToken ct = default) => Transport.PostAsync<FriendsResponse>(ApiRoutes.FriendDecline, new FriendTargetRequest { PlayerId = playerId }, ct);
        public Task<FriendsResponse> RemoveFriendAsync(string playerId, CancellationToken ct = default) => Transport.PostAsync<FriendsResponse>(ApiRoutes.FriendRemove, new FriendTargetRequest { PlayerId = playerId }, ct);
        public Task<FriendsResponse> BlockPlayerAsync(string playerId, CancellationToken ct = default) => Transport.PostAsync<FriendsResponse>(ApiRoutes.FriendBlock, new FriendTargetRequest { PlayerId = playerId }, ct);
        public Task<MatchStartResponse> ChallengeFriendAsync(string friendId, StartStageRequest request, CancellationToken ct = default) => Transport.PostAsync<MatchStartResponse>(ApiRoutes.Fill(ApiRoutes.FriendChallenge, "friendId", friendId), request, ct);
        public Task<WorldMapResponse> GetWorldMapAsync(CancellationToken ct = default) => Transport.GetAsync<WorldMapResponse>(ApiRoutes.WorldMap, ct);

        // Guild
        public Task<GuildDto> CreateGuildAsync(CreateGuildRequest request, CancellationToken ct = default) => Transport.PostAsync<GuildDto>(ApiRoutes.GuildCreate, request, ct);
        public Task<GuildDto> GetMyGuildAsync(CancellationToken ct = default) => Transport.GetAsync<GuildDto>(ApiRoutes.GuildMine, ct);
        public Task<GuildDto> GetGuildAsync(long guildId, CancellationToken ct = default) => Transport.GetAsync<GuildDto>(ApiRoutes.Fill(ApiRoutes.GuildById, "guildId", Id(guildId)), ct);
        public Task<GuildSearchResponse> SearchGuildsAsync(string query, CancellationToken ct = default) => Transport.GetAsync<GuildSearchResponse>(ApiRoutes.GuildSearch + "?q=" + Uri.EscapeDataString(query ?? string.Empty), ct);
        public Task<GuildDto> JoinGuildAsync(long guildId, CancellationToken ct = default) => Transport.PostAsync<GuildDto>(ApiRoutes.Fill(ApiRoutes.GuildJoin, "guildId", Id(guildId)), null, ct);
        public Task<object> LeaveGuildAsync(CancellationToken ct = default) => Transport.PostAsync<object>(ApiRoutes.GuildLeave, null, ct);
        public Task<GuildDto> KickAsync(string playerId, CancellationToken ct = default) => Transport.PostAsync<GuildDto>(ApiRoutes.GuildKick, new GuildMemberActionRequest { PlayerId = playerId }, ct);
        public Task<GuildDto> SetRoleAsync(string playerId, string role, CancellationToken ct = default) => Transport.PostAsync<GuildDto>(ApiRoutes.GuildRole, new GuildMemberActionRequest { PlayerId = playerId, Role = role }, ct);
        public Task<GuildDto> InviteAsync(string playerId, CancellationToken ct = default) => Transport.PostAsync<GuildDto>(ApiRoutes.GuildInvite, new GuildMemberActionRequest { PlayerId = playerId }, ct);
        public Task<GuildDto> UpdateGuildSettingsAsync(GuildSettingsRequest request, CancellationToken ct = default) => Transport.PutAsync<GuildDto>(ApiRoutes.GuildSettings, request, ct);
        public Task<DonateResponse> DonateAsync(long amount, string currency = "Orbes", CancellationToken ct = default) =>
            Transport.PostAsync<DonateResponse>(ApiRoutes.GuildDonate, new DonateRequest { Amount = amount, Currency = currency }, ct);
        public Task<GuildDto> SpendTechAsync(string tech, CancellationToken ct = default) => Transport.PostAsync<GuildDto>(ApiRoutes.GuildTech, new TechRequest { Tech = tech }, ct);
        public Task<MatchStartResponse> StartGuildBossAsync(StartStageRequest request, CancellationToken ct = default) => Transport.PostAsync<MatchStartResponse>(ApiRoutes.GuildBossStart, request, ct);
        public Task<GuildBossAttackResponse> SubmitGuildBossAsync(SubmitReplayRequest request, CancellationToken ct = default) => Transport.PostAsync<GuildBossAttackResponse>(ApiRoutes.GuildBossDamage, request, ct);
        public Task<ChatMessageDto> SendChatAsync(string text, CancellationToken ct = default) => Transport.PostAsync<ChatMessageDto>(ApiRoutes.GuildChat, new ChatSendRequest { Text = text }, ct);
        public Task<ChatHistoryResponse> GetChatHistoryAsync(long? beforeId = null, CancellationToken ct = default) => Transport.GetAsync<ChatHistoryResponse>(ApiRoutes.GuildChat + (beforeId.HasValue ? "?before=" + Id(beforeId.Value) : string.Empty), ct);

        // Progression
        public Task<AchievementsResponse> GetAchievementsAsync(CancellationToken ct = default) => Transport.GetAsync<AchievementsResponse>(ApiRoutes.Achievements, ct);
        public Task<ClaimResponse> ClaimAchievementAsync(string id, CancellationToken ct = default) => Transport.PostAsync<ClaimResponse>(ApiRoutes.AchievementUnlock, new ClaimAchievementRequest { Id = id }, ct);
        public Task<QuestsResponse> GetQuestsAsync(CancellationToken ct = default) => Transport.GetAsync<QuestsResponse>(ApiRoutes.Quests, ct);
        public Task<ClaimResponse> ClaimQuestAsync(string questId, CancellationToken ct = default) => Transport.PostAsync<ClaimResponse>(ApiRoutes.Fill(ApiRoutes.QuestClaim, "questId", questId), null, ct);
        public Task<LoginBonusResponse> ClaimLoginBonusAsync(CancellationToken ct = default) => Transport.PostAsync<LoginBonusResponse>(ApiRoutes.LoginBonus, null, ct);
        public Task<BattlePassResponse> GetBattlePassAsync(CancellationToken ct = default) => Transport.GetAsync<BattlePassResponse>(ApiRoutes.BattlePass, ct);
        public Task<ClaimResponse> ClaimBattlePassAsync(int tier, bool premium, CancellationToken ct = default) => Transport.PostAsync<ClaimResponse>(ApiRoutes.BattlePassClaim, new BattlePassClaimRequest { Tier = tier, Premium = premium }, ct);

        // Reports
        public Task<object> ReportCheatAsync(string playerId, string matchId, string reason, CancellationToken ct = default) => Transport.PostAsync<object>(ApiRoutes.ReportCheat, new ReportCheatRequest { PlayerId = playerId, MatchId = matchId, Reason = reason }, ct);
    }
}
