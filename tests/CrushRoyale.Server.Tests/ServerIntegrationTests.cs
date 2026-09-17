using System.Net;
using System.Net.Http.Json;
using CrushRoyale.Contracts;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Gameplay;
using CrushRoyale.Core.Pvp;
using CrushRoyale.Core.Replay;
using CrushRoyale.Core.Story;
using CrushRoyale.Server.Auth;
using CrushRoyale.Server.Infrastructure;
using CrushRoyale.Server.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace CrushRoyale.Server.Tests;

/// <summary>Real HTTP pipeline, in-memory store, dev authentication, fake receipts and a controllable clock.</summary>
public sealed class CrushApp : WebApplicationFactory<Program>
{
    static CrushApp()
    {
        Environment.SetEnvironmentVariable("Game__EnableBackgroundJobs", "false");
        Environment.SetEnvironmentVariable("Game__RateLimitPerMinute", "100000");
    }

    public ManualClock Clock { get; } = new(new DateTime(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc));

    public GameBalance Balance { get; } = GameBalance.CreateDefault();

    public StageCatalog Catalog => new(Balance);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Game:EnableBackgroundJobs", "false");
        builder.ConfigureTestServices(services => services.AddSingleton<IClock>(Clock));
    }

    public HttpClient ClientFor(Guid user, bool admin = false)
    {
        HttpClient client = CreateClient();
        client.DefaultRequestHeaders.Add(DevAuthHandler.UserHeader, user.ToString());
        if (admin)
        {
            client.DefaultRequestHeaders.Add(DevAuthHandler.AdminHeader, "true");
        }
        return client;
    }

    public IGameStore Store => Services.GetRequiredService<IGameStore>();

    public async Task<(Guid Id, HttpClient Http, LoginResponse Login)> NewPlayerAsync(int unlockStage = 1)
    {
        Guid id = Guid.NewGuid();
        HttpClient http = ClientFor(id);
        LoginResponse login = await http.PostOk<LoginResponse>(ApiRoutes.Login, new LoginRequest
        {
            DeviceHash = Convert.ToHexString(Guid.NewGuid().ToByteArray()),
            Platform = "android",
            ClientVersion = "test",
            RulesVersion = GameBalance.RulesVersion,
            BalanceHash = Balance.ComputeHash().ToString("x16"),
            Region = "eu",
            Language = "fr"
        });
        if (unlockStage > 1)
        {
            await MutateAsync(id, s => s.Story.HighestUnlockedStage = unlockStage);
        }
        return (id, http, login);
    }

    public Task MutateAsync(Guid id, Action<PlayerState> change) =>
        Store.TransactAsync(async tx =>
        {
            PlayerRecord player = (await tx.GetPlayerAsync(id))!;
            change(player.State);
            await tx.UpdatePlayerAsync(player);
            return true;
        }, CancellationToken.None);

    public Task<PlayerState> StateAsync(Guid id) =>
        Store.TransactAsync(async tx => (await tx.GetPlayerAsync(id, false))!.State, CancellationToken.None);

    /// <summary>What the Unity client does: rebuild the exact session, play it (bot), serialize the replay.</summary>
    public SubmitReplayRequest Play(Guid player, MatchStartResponse start, IBotStrategy? bot = null, Func<ReplayData, ReplayData>? tamper = null)
    {
        var mode = Enum.Parse<GameMode>(start.Mode);
        var loadout = start.Loadout.Select(l => new LoadoutEntry(Enum.Parse<PowerUpType>(l.Type), l.Quantity)).ToList();
        var league = Enum.Parse<League>(start.HighestLeague);
        ulong seed = ulong.Parse(start.Seed);

        SessionConfig config = mode switch
        {
            GameMode.Story => SessionConfig.ForStage(Catalog.Get(start.StageId), Balance, loadout, league, start.AssistExtraMoves).WithStartBoosters(start.StartBoosters),
            GameMode.GuildBoss => SessionConfig.ForGuildBoss(seed, start.StageId, Balance, loadout, league),
            _ => SessionConfig.ForPvp(seed, Balance, mode, loadout, league)
        };
        config.WithPet(start.Pet == null ? PetType.None : Enum.Parse<PetType>(start.Pet), start.PetLevel, Balance);

        var session = new GameSession(config, Balance, player.ToString());
        HeadlessRunner.Run(session, bot ?? new GreedyBot());
        ReplayData replay = tamper == null ? session.Replay : tamper(session.Replay);
        Clock.Advance(TimeSpan.FromMilliseconds(replay.EndTimeMs + 1000));
        return new SubmitReplayRequest { MatchId = start.MatchId, ReplayBase64 = Convert.ToBase64String(ReplaySerializer.Serialize(replay)) };
    }
}

internal static class HttpExtensions
{
    public static async Task<T> PostOk<T>(this HttpClient http, string url, object? body = null)
    {
        HttpResponseMessage response = await http.PostAsJsonAsync(url, body ?? new { }, Json.Options);
        return await Read<T>(response, url);
    }

    public static async Task<T> PutOk<T>(this HttpClient http, string url, object body)
    {
        HttpResponseMessage response = await http.PutAsJsonAsync(url, body, Json.Options);
        return await Read<T>(response, url);
    }

    public static async Task<T> GetOk<T>(this HttpClient http, string url) => await Read<T>(await http.GetAsync(url), url);

    public static async Task<ApiErrorDto> PostError(this HttpClient http, string url, object? body, HttpStatusCode expected)
    {
        HttpResponseMessage response = await http.PostAsJsonAsync(url, body ?? new { }, Json.Options);
        string text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"{url}: expected {expected}, got {(int)response.StatusCode} {text}");
        return System.Text.Json.JsonSerializer.Deserialize<ApiErrorDto>(text, Json.Options)!;
    }

    private static async Task<T> Read<T>(HttpResponseMessage response, string url)
    {
        string text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{url}: {(int)response.StatusCode} {text}");
        return System.Text.Json.JsonSerializer.Deserialize<T>(text, Json.Options)!;
    }
}

public sealed class ServerIntegrationTests : IClassFixture<CrushApp>
{
    private readonly CrushApp _app;

    public ServerIntegrationTests(CrushApp app)
    {
        _app = app;
    }

    [Fact]
    public async Task Health_Config_AndAuthRequired()
    {
        HttpClient anonymous = _app.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(ApiRoutes.Health)).StatusCode);
        ConfigResponse config = await anonymous.GetOk<ConfigResponse>(ApiRoutes.Config);
        Assert.Equal(_app.Balance.ComputeHash().ToString("x16"), config.BalanceHash);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(ApiRoutes.Me)).StatusCode);
    }

    [Fact]
    public async Task Login_CreatesPlayerOnce_WithStarterPack()
    {
        var (id, http, login) = await _app.NewPlayerAsync();
        Assert.True(login.IsNewPlayer);
        Assert.False(login.ConfigOutdated);
        Assert.Equal(500, login.Profile.Wallet.Coins);
        Assert.Equal(20, login.Profile.Wallet.Orbes);
        Assert.Equal(2, login.Profile.Lives.Lives);
        Assert.Equal(3, login.Profile.Inventory.PowerUps["ChronoBomb"]);

        LoginResponse again = await http.PostOk<LoginResponse>(ApiRoutes.Login, new LoginRequest { DeviceHash = new string('a', 32), RulesVersion = GameBalance.RulesVersion, BalanceHash = "stale" });
        Assert.False(again.IsNewPlayer);
        Assert.True(again.ConfigOutdated);

        await http.PostError(ApiRoutes.Login, new LoginRequest { DeviceHash = new string('a', 32), RulesVersion = 999 }, HttpStatusCode.UpgradeRequired);

        ProfileDto hero = await http.PutOk<ProfileDto>(ApiRoutes.Hero, new SetHeroRequest { Gender = "male", HeroName = "Aziz", DisplayName = "Ahmed_Crush", Age = 30 });
        Assert.True(hero.HeroChosen);
        Assert.Equal("Ahmed_Crush", hero.DisplayName);

        var ledger = ((InMemoryGameStore)_app.Store).LedgerSnapshot().Where(l => l.PlayerId == id).ToList();
        Assert.Equal(2, ledger.Count);
    }

    [Fact]
    public async Task DeleteAccount_ErasesThePlayer_AndLeavesTheGuild()
    {
        var (leader, leaderHttp, _) = await _app.NewPlayerAsync(unlockStage: 400);
        var (member, memberHttp, _) = await _app.NewPlayerAsync(unlockStage: 400);
        await _app.MutateAsync(leader, s => s.Wallet.Coins = 100_000);
        GuildDto guild = await leaderHttp.PostOk<GuildDto>(ApiRoutes.GuildCreate, new CreateGuildRequest { Name = "Erasers" + Random.Shared.Next(1000, 9999), IsOpen = true });
        await memberHttp.PostOk<GuildDto>(ApiRoutes.Fill(ApiRoutes.GuildJoin, "guildId", guild.Id), new { });

        HttpResponseMessage deleted = await memberHttp.DeleteAsync(ApiRoutes.Me);
        Assert.Equal(HttpStatusCode.OK, deleted.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await memberHttp.GetAsync(ApiRoutes.Me)).StatusCode);
        GuildDto after = await leaderHttp.GetOk<GuildDto>(ApiRoutes.GuildMine);
        Assert.DoesNotContain(after.Members, m => m.PlayerId == member.ToString());
        Assert.Equal(HttpStatusCode.OK, (await leaderHttp.GetAsync(ApiRoutes.Me)).StatusCode);
    }

    [Fact]
    public async Task StoryStage_PlayedAndValidated_ByServer()
    {
        var (id, http, _) = await _app.NewPlayerAsync();
        MatchStartResponse start = await http.PostOk<MatchStartResponse>(ApiRoutes.Fill(ApiRoutes.StageStart, "stageId", 1), new StartStageRequest { Loadout = { "ChronoBomb" } });
        Assert.Equal(1, start.Lives.Lives);
        Assert.Equal(3, start.Loadout.Single().Quantity);

        SubmitReplayRequest replay = _app.Play(id, start);
        StageCompleteResponse result = await http.PostOk<StageCompleteResponse>(ApiRoutes.Fill(ApiRoutes.StageComplete, "matchId", start.MatchId), replay);

        Assert.True(result.Accepted, result.Error);
        Assert.Equal("Won", result.State);
        Assert.True(result.FirstWin);
        Assert.True(result.CoinsEarned > 0);
        Assert.Equal(2, result.NextStage);
        Assert.Equal(2, result.Lives.Lives);

        await http.PostError(ApiRoutes.Fill(ApiRoutes.StageComplete, "matchId", start.MatchId), replay, HttpStatusCode.Conflict);
        await http.PostError(ApiRoutes.Fill(ApiRoutes.StageStart, "stageId", 5), new StartStageRequest(), HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Chests_UnlockOneAtATime_OpenWithOrbes()
    {
        var (id, http, _) = await _app.NewPlayerAsync();
        await _app.MutateAsync(id, s =>
        {
            s.Wallet.Orbes = 1000;
            s.Chests.Slots = new List<CrushRoyale.Core.Progression.ChestSlot?>
            {
                new() { Type = CrushRoyale.Core.Progression.ChestType.Wood, UnlockSeconds = 10800 },
                new() { Type = CrushRoyale.Core.Progression.ChestType.Gold, UnlockSeconds = 43200 },
                null,
                null
            }!;
        });

        ChestsDto unlocking = await http.PostOk<ChestsDto>(ApiRoutes.Fill(ApiRoutes.ChestUnlock, "slot", 0));
        Assert.Equal("Unlocking", unlocking.Slots[0].Status);
        await http.PostError(ApiRoutes.Fill(ApiRoutes.ChestUnlock, "slot", 1), null, HttpStatusCode.Conflict);
        await http.PostError(ApiRoutes.Fill(ApiRoutes.ChestOpen, "slot", 0), new ChestOpenRequest(), HttpStatusCode.Conflict);

        ChestOpenResponse opened = await http.PostOk<ChestOpenResponse>(ApiRoutes.Fill(ApiRoutes.ChestOpen, "slot", 0), new ChestOpenRequest { UseOrbes = true });
        Assert.Equal("Wood", opened.Type);
        Assert.True(opened.Reward.Coins >= 40);
        Assert.Equal(1000 - 18, opened.Wallet.Orbes); // 3 h = 18 blocks of 10 minutes
        Assert.Null(opened.Chests.Slots[0]);
        Assert.True(opened.PetFragments > 0);
    }

    [Fact]
    public async Task Telemetry_StoresValidEvents_AndGroupsErrors()
    {
        var (id, http, _) = await _app.NewPlayerAsync();
        var request = new TelemetryRequest
        {
            SessionId = "s1",
            AppVersion = "1.0.3",
            Device = "Pixel 8",
            Events =
            {
                new TelemetryEventDto { Name = "stage_end", AtUnixMs = 1_790_000_000_000, Props = { ["stage"] = "3", ["result"] = "Won" } },
                new TelemetryEventDto { Name = "DROP TABLE players;--" }
            },
            Errors = { new ClientErrorDto { Message = "NullReferenceException at 0x1234", Stack = "BoardView.PlayStep\nMatchController.Update", Count = 3 } }
        };
        await http.PostOk<object>(ApiRoutes.Telemetry, request);

        var store = (InMemoryGameStore)_app.Store;
        (Guid player, TelemetryBatch batch) = store.Telemetry.Last();
        Assert.Equal(id, player);
        Assert.Single(batch.Events);
        Assert.Equal("stage_end", batch.Events[0].Name);
        Assert.Contains("\"stage\":\"3\"", batch.Events[0].PropsJson);
        Assert.Equal(3, batch.Errors.Single().Count);
    }

    [Fact]
    public async Task ChapterChest_ClaimOnceWithEnoughStars_AndPetFragmentsConvert()
    {
        var (id, http, _) = await _app.NewPlayerAsync(unlockStage: 21);
        await http.PostError(ApiRoutes.ChapterChest, new ChapterChestRequest { Chapter = 1, Tier = 0 }, HttpStatusCode.Conflict);

        await _app.MutateAsync(id, s =>
        {
            for (int stage = 1; stage <= 12; stage++)
            {
                s.Story.Stages[stage] = new StageProgress { StageId = stage, EverWon = true, BestStars = 3 };
            }
        });
        ChapterChestResponse chest = await http.PostOk<ChapterChestResponse>(ApiRoutes.ChapterChest, new ChapterChestRequest { Chapter = 1, Tier = 0 });
        Assert.True(chest.Reward.Coins > 0);
        Assert.Contains("1:0", chest.Story.ClaimedChapterChests);
        await http.PostError(ApiRoutes.ChapterChest, new ChapterChestRequest { Chapter = 1, Tier = 0 }, HttpStatusCode.Conflict);

        await _app.MutateAsync(id, s =>
        {
            s.Pets.Pets[PetType.FrostFox] = new CrushRoyale.Core.Pets.PetState { Owned = true, Level = 1, Fragments = 9 };
        });
        PetActionResponse converted = await http.PostOk<PetActionResponse>(ApiRoutes.PetConvert, new PetConvertRequest { From = "FrostFox", To = "ForestOwl", Count = 3 });
        Assert.Equal(0, converted.Pets.Pets.Single(p => p.Type == "FrostFox").Fragments);
        Assert.Equal(3, converted.Pets.Pets.Single(p => p.Type == "ForestOwl").Fragments);
    }

    [Fact]
    public async Task WinStreak_GivesStartBoosters_AndWheelSpinsOncePerDay()
    {
        var (id, http, _) = await _app.NewPlayerAsync(unlockStage: 40);
        await _app.MutateAsync(id, s => s.Story.WinStreak = 5);
        MatchStartResponse start = await http.PostOk<MatchStartResponse>(ApiRoutes.Fill(ApiRoutes.StageStart, "stageId", 40), new StartStageRequest());
        Assert.Equal(2, start.StartBoosters);
        StageCompleteResponse done = await http.PostOk<StageCompleteResponse>(ApiRoutes.Fill(ApiRoutes.StageComplete, "matchId", start.MatchId), _app.Play(id, start));
        Assert.True(done.Accepted);

        WheelSpinResponse spin = await http.PostOk<WheelSpinResponse>(ApiRoutes.WheelSpin, null);
        Assert.InRange(spin.SliceIndex, 0, 7);
        await http.PostError(ApiRoutes.WheelSpin, null, HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Pets_SummonEquipAwaken_AndPlayInMatches()
    {
        var (id, http, _) = await _app.NewPlayerAsync();
        await http.PostError(ApiRoutes.PetSummon, new PetSummonRequest { Count = 10 }, HttpStatusCode.Conflict);

        await _app.MutateAsync(id, s => s.Wallet.Orbes = 100_000);
        PetSummonResponse summon = null!;
        for (int i = 0; i < 12; i++)
        {
            summon = await http.PostOk<PetSummonResponse>(ApiRoutes.PetSummon, new PetSummonRequest { Count = 10 });
            Assert.Equal(10, summon.Pulls.Count);
        }
        Assert.Contains(summon.Pets.Pets, p => p.Owned); // pity guarantees a pet within 120 pulls
        Assert.NotNull(summon.Pets.Equipped);
        Assert.Equal(100_000 - 12 * 270, summon.Wallet.Orbes);

        string pet = summon.Pets.Equipped!;
        await _app.MutateAsync(id, s =>
        {
            s.Pets.Pets[Enum.Parse<PetType>(pet)].Level = 10;
            s.Pets.Pets[Enum.Parse<PetType>(pet)].Xp = _app.Balance.Pets.XpForLevel[9];
        });

        MatchStartResponse start = await http.PostOk<MatchStartResponse>(ApiRoutes.Fill(ApiRoutes.StageStart, "stageId", 1), new StartStageRequest());
        Assert.Equal(pet, start.Pet);
        Assert.Equal(10, start.PetLevel);
        StageCompleteResponse result = await http.PostOk<StageCompleteResponse>(ApiRoutes.Fill(ApiRoutes.StageComplete, "matchId", start.MatchId), _app.Play(id, start));
        Assert.True(result.Accepted, result.Error);

        await http.PostError(ApiRoutes.PetEquip, new PetRequest { Pet = "Unicorn" }, HttpStatusCode.BadRequest);
        PetActionResponse equip = await http.PostOk<PetActionResponse>(ApiRoutes.PetEquip, new PetRequest { Pet = pet });
        Assert.Equal(pet, equip.Pets.Equipped);
    }

    [Fact]
    public async Task ForgedScore_IsRejected_AndPlayerBanned()
    {
        var (id, http, _) = await _app.NewPlayerAsync();
        MatchStartResponse start = await http.PostOk<MatchStartResponse>(ApiRoutes.Fill(ApiRoutes.StageStart, "stageId", 1), new StartStageRequest());

        SubmitReplayRequest forged = _app.Play(id, start, tamper: r =>
        {
            r.Actions[^1] = new ReplayAction(r.Actions[^1].Action, r.Actions[^1].ScoreAfter + 50000);
            r.FinalScore += 50000;
            return r;
        });
        StageCompleteResponse result = await http.PostOk<StageCompleteResponse>(ApiRoutes.Fill(ApiRoutes.StageComplete, "matchId", start.MatchId), forged);

        Assert.False(result.Accepted);
        Assert.Equal("ReplayMismatch", result.Error);
        Assert.True((await _app.StateAsync(id)).Integrity.PermanentlyBanned);
        ApiErrorDto banned = await http.PostError(ApiRoutes.Fill(ApiRoutes.StageStart, "stageId", 1), new StartStageRequest(), HttpStatusCode.Forbidden);
        Assert.Equal("Banned", banned.Code);
    }

    [Fact]
    public async Task Shop_Iap_AndIdempotency()
    {
        var (id, http, _) = await _app.NewPlayerAsync(unlockStage: 20);
        await http.PutOk<ProfileDto>(ApiRoutes.Hero, new SetHeroRequest { Gender = "female", HeroName = "Lyra", Age = 25 });

        ShopResponse shop = await http.GetOk<ShopResponse>(ApiRoutes.Shop);
        Assert.Equal(6, shop.Items.Count(i => i.Id.StartsWith("daily.")));
        ShopItemDto offer = shop.Items.First(i => i.Id.StartsWith("daily.") && i.PriceCoins > 0 && i.PriceCoins <= 500);
        PurchaseResponse bought = await http.PostOk<PurchaseResponse>(ApiRoutes.ShopPurchase, new PurchaseRequest { ItemId = offer.Id, Method = "Coins" });
        Assert.Equal(500 - offer.PriceCoins, bought.Wallet.Coins);

        IapPrecheckResponse precheck = await http.PostOk<IapPrecheckResponse>(ApiRoutes.IapPrecheck, new IapPrecheckRequest { Sku = "crushroyale.orbes.pack2" });
        Assert.True(precheck.Allowed);

        var validate = new IapValidateRequest { Sku = "crushroyale.orbes.pack2", PurchaseToken = "fake:GPA.1234" };
        IapValidateResponse granted = await http.PostOk<IapValidateResponse>(ApiRoutes.IapValidate, validate);
        Assert.False(granted.AlreadyGranted);
        Assert.Equal(20 + 395, granted.Wallet.Orbes);
        Assert.Equal(1, granted.Vip.Tier);

        IapValidateResponse again = await http.PostOk<IapValidateResponse>(ApiRoutes.IapValidate, validate);
        Assert.True(again.AlreadyGranted);
        Assert.Equal(20 + 395, again.Wallet.Orbes);

        await http.PostError(ApiRoutes.IapValidate, new IapValidateRequest { Sku = "crushroyale.orbes.pack2", PurchaseToken = "forged" }, HttpStatusCode.Forbidden);
        Assert.Contains(((InMemoryGameStore)_app.Store).PurchaseSnapshot(), p => p.PlayerId == id && p.Record.CentsCharged == 499);
    }

    /// <summary>Plays a fixed number of moves then stops, so a match winner never depends on the random PvP seed.</summary>
    private sealed class LimitedBot : IBotStrategy
    {
        private readonly IBotStrategy _inner;
        private int _remaining;

        public LimitedBot(IBotStrategy inner, int moves)
        {
            _inner = inner;
            _remaining = moves;
        }

        public PlayerAction? ChooseAction(GameSession session, int nowMs) =>
            _remaining-- > 0 ? _inner.ChooseAction(session, nowMs) : null;
    }

    [Fact]
    public async Task LivePvp_BothPlayersSettled()
    {
        var a = await _app.NewPlayerAsync(unlockStage: 30);
        var b = await _app.NewPlayerAsync(unlockStage: 30);

        await a.Http.PostOk<MatchmakingStatusResponse>(ApiRoutes.Matchmaking, new MatchmakingRequest());
        await b.Http.PostOk<MatchmakingStatusResponse>(ApiRoutes.Matchmaking, new MatchmakingRequest());
        _app.Services.GetRequiredService<MatchmakingEngine>().ProcessQueue();

        MatchmakingStatusResponse sa = await a.Http.GetOk<MatchmakingStatusResponse>(ApiRoutes.Matchmaking);
        MatchmakingStatusResponse sb = await b.Http.GetOk<MatchmakingStatusResponse>(ApiRoutes.Matchmaking);
        Assert.Equal("Matched", sa.Status);
        Assert.Equal("Matched", sb.Status);
        Assert.Equal(sa.Match.Seed, sb.Match.Seed);

        // The matchmaking seed is random: a full greedy match against a single move keeps the winner deterministic.
        PvpResultDto first = await a.Http.PostOk<PvpResultDto>(ApiRoutes.PvpRecord, _app.Play(a.Id, sa.Match, new GreedyBot()));
        Assert.Equal("Pending", first.Outcome);

        PvpResultDto second = await b.Http.PostOk<PvpResultDto>(ApiRoutes.PvpRecord, _app.Play(b.Id, sb.Match, new LimitedBot(new RandomBot(3), 1)));
        Assert.True(second.Accepted, second.Error);
        Assert.True(second.Score < second.OpponentScore, $"single move {second.Score} vs full greedy match {second.OpponentScore}");
        Assert.Equal("Loss", second.Outcome);

        MatchInfoResponse info = await a.Http.GetOk<MatchInfoResponse>(ApiRoutes.Fill(ApiRoutes.PvpMatch, "matchId", sa.Match.MatchId));
        Assert.Equal("Win", info.Result.Outcome);
        Assert.Equal(25, info.Result.TrophyDelta);
        Assert.Equal(25, (await _app.StateAsync(a.Id)).Pvp.Trophies);
        Assert.NotNull(info.Ghost);

        LeaderboardResponse board = await a.Http.GetOk<LeaderboardResponse>(ApiRoutes.LeaderboardWeekly + "?league=Bronze");
        Assert.NotNull(board.MyRank);

        // The winner now has a ghost: a friend can challenge it (no trophies).
        await b.Http.PostOk<FriendsResponse>(ApiRoutes.FriendRequest, new FriendTargetRequest { PlayerId = a.Id.ToString() });
        FriendsResponse friends = await a.Http.PostOk<FriendsResponse>(ApiRoutes.FriendAccept, new FriendTargetRequest { PlayerId = b.Id.ToString() });
        Assert.Single(friends.Friends);

        MatchStartResponse challenge = await b.Http.PostOk<MatchStartResponse>(ApiRoutes.Fill(ApiRoutes.FriendChallenge, "friendId", a.Id), new StartStageRequest());
        Assert.NotNull(challenge.Ghost);
        PvpResultDto friendly = await b.Http.PostOk<PvpResultDto>(ApiRoutes.PvpRecord, _app.Play(b.Id, challenge));
        Assert.False(friendly.Ranked);
        Assert.Equal(0, friendly.TrophyDelta);
        await b.Http.PostError(ApiRoutes.Fill(ApiRoutes.FriendChallenge, "friendId", a.Id), new StartStageRequest(), HttpStatusCode.TooManyRequests);

        // "Challenge me" link: anyone with the code plays the same board against the winner's duel, no friendship needed.
        ChallengeCreateResponse link = await a.Http.PostOk<ChallengeCreateResponse>(ApiRoutes.ChallengeCreate);
        Assert.StartsWith("CR-", link.Code);
        Assert.EndsWith(link.Code, link.Url);
        var stranger = await _app.NewPlayerAsync(unlockStage: 30);
        MatchStartResponse accepted = await stranger.Http.PostOk<MatchStartResponse>(ApiRoutes.Fill(ApiRoutes.ChallengeStart, "code", link.Code), new StartStageRequest());
        Assert.NotNull(accepted.Ghost);
        Assert.Equal(sa.Match.Seed, accepted.Seed);
        PvpResultDto linkResult = await stranger.Http.PostOk<PvpResultDto>(ApiRoutes.PvpRecord, _app.Play(stranger.Id, accepted));
        Assert.True(linkResult.Accepted, linkResult.Error);
        Assert.False(linkResult.Ranked);

        string forged = link.Code[..^1] + (link.Code[^1] == '0' ? '1' : '0');
        await stranger.Http.PostError(ApiRoutes.Fill(ApiRoutes.ChallengeStart, "code", forged), new StartStageRequest(), HttpStatusCode.NotFound);
        await a.Http.PostError(ApiRoutes.Fill(ApiRoutes.ChallengeStart, "code", link.Code), new StartStageRequest(), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Guild_CreateJoinChatDonateAndBoss()
    {
        var leader = await _app.NewPlayerAsync(unlockStage: 45);
        var member = await _app.NewPlayerAsync(unlockStage: 45);
        string name = "Knights" + Random.Shared.Next(1000, 9999);

        GuildDto guild = await leader.Http.PostOk<GuildDto>(ApiRoutes.GuildCreate, new CreateGuildRequest { Name = name, Description = "Crystalheim forever" });
        Assert.Single(guild.Members);
        await member.Http.PostError(ApiRoutes.GuildCreate, new CreateGuildRequest { Name = name.ToUpperInvariant() }, HttpStatusCode.Conflict);

        guild = await member.Http.PostOk<GuildDto>(ApiRoutes.Fill(ApiRoutes.GuildJoin, "guildId", guild.Id), null);
        Assert.Equal(2, guild.Members.Count);

        ChatMessageDto message = await member.Http.PostOk<ChatMessageDto>(ApiRoutes.GuildChat, new ChatSendRequest { Text = "boss ce soir ?" });
        ChatHistoryResponse history = await leader.Http.GetOk<ChatHistoryResponse>(ApiRoutes.GuildChat);
        Assert.Contains(history.Messages, m => m.Id == message.Id);

        HttpClient admin = _app.ClientFor(Guid.NewGuid(), admin: true);
        await admin.PostOk<WalletDto>(ApiRoutes.AdminGrant, new AdminGrantRequest { PlayerId = leader.Id.ToString(), Coins = 1000, Note = "test donation budget" });
        DonateResponse donation = await leader.Http.PostOk<DonateResponse>(ApiRoutes.GuildDonate, new DonateRequest { Amount = 500, Currency = "Coins" });
        Assert.True(donation.LeveledUp);
        Assert.Equal(2, donation.Guild.Level);

        MatchStartResponse boss = await member.Http.PostOk<MatchStartResponse>(ApiRoutes.GuildBossStart, new StartStageRequest());
        GuildBossAttackResponse attack = await member.Http.PostOk<GuildBossAttackResponse>(ApiRoutes.GuildBossDamage, _app.Play(member.Id, boss));
        Assert.True(attack.Accepted, attack.Error);
        Assert.True(attack.Damage > 0);
        Assert.Equal(2, attack.AttacksLeft);

        GuildDto mine = await leader.Http.GetOk<GuildDto>(ApiRoutes.GuildMine);
        Assert.Equal(attack.Damage, mine.Boss.Damage);
    }

    [Fact]
    public async Task Progression_AchievementsQuestsLoginBonusBattlePass()
    {
        var (_, http, _) = await _app.NewPlayerAsync(unlockStage: 12);
        AchievementsResponse achievements = await http.GetOk<AchievementsResponse>(ApiRoutes.Achievements);
        Assert.Equal(60, achievements.Achievements.Count);
        Assert.True(achievements.Achievements.Single(a => a.Id == "first_steps").Unlocked == false);

        LoginBonusResponse bonus = await http.PostOk<LoginBonusResponse>(ApiRoutes.LoginBonus);
        Assert.Equal(600, bonus.Wallet.Coins);
        await http.PostError(ApiRoutes.LoginBonus, null, HttpStatusCode.Conflict);

        QuestsResponse quests = await http.GetOk<QuestsResponse>(ApiRoutes.Quests);
        Assert.Equal(3, quests.Quests.Count);

        BattlePassResponse pass = await http.GetOk<BattlePassResponse>(ApiRoutes.BattlePass);
        Assert.Equal(50, pass.Tiers.Count);
        await http.PostError(ApiRoutes.BattlePassClaim, new BattlePassClaimRequest { Tier = 1 }, HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Admin_RequiresRole_ReviewGrantAndSeasonReset()
    {
        var (target, http, _) = await _app.NewPlayerAsync();
        var reporter = await _app.NewPlayerAsync();
        await reporter.Http.PostOk<object>(ApiRoutes.ReportCheat, new ReportCheatRequest { PlayerId = target.ToString(), Reason = "impossible score" });

        Assert.Equal(HttpStatusCode.Forbidden, (await http.GetAsync(ApiRoutes.AdminFlags)).StatusCode);

        HttpClient admin = _app.ClientFor(Guid.NewGuid(), admin: true);
        List<FlagDto> flags = await admin.GetOk<List<FlagDto>>(ApiRoutes.AdminFlags);
        FlagDto flag = flags.First(f => f.PlayerId == target.ToString());
        await admin.PostOk<object>(ApiRoutes.Fill(ApiRoutes.AdminFlagReview, "flagId", flag.Id), new ReviewFlagRequest { Outcome = "confirmed", Punish = true });
        Assert.True((await _app.StateAsync(target)).Integrity.SuspendedUntilUnixMs > TimeUtil.ToUnixMs(_app.Clock.UtcNow));

        WalletDto wallet = await admin.PostOk<WalletDto>(ApiRoutes.AdminGrant, new AdminGrantRequest { PlayerId = target.ToString(), Orbes = 50, Note = "support ticket #42" });
        Assert.Equal(70, wallet.Orbes);

        await _app.MutateAsync(target, s => s.Pvp.Trophies = 1000);
        var result = await admin.PostOk<System.Text.Json.JsonElement>(ApiRoutes.AdminSeasonReset, null);
        Assert.True(result.TryGetProperty("processed", out var processed) && processed.ValueKind == System.Text.Json.JsonValueKind.Number && processed.GetInt32() >= 2, result.GetRawText());
        Assert.Equal(650, (await _app.StateAsync(target)).Pvp.Trophies);
    }
}
