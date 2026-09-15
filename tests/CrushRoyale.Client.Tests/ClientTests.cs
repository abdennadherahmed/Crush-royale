using System.Net;
using System.Text;
using CrushRoyale.Client;
using CrushRoyale.Contracts;
using CrushRoyale.Core.Config;
using CrushRoyale.Core.Gameplay;
using CrushRoyale.Core.Replay;
using CrushRoyale.Core.Story;
using Newtonsoft.Json.Linq;

namespace CrushRoyale.Client.Tests;

internal sealed class FakeHandler : HttpMessageHandler
{
    public Queue<Func<HttpRequestMessage, HttpResponseMessage>> Responses { get; } = new();

    public List<(HttpMethod Method, string Url, string? Auth, string? Body)> Requests { get; } = new();

    public FakeHandler Enqueue(HttpStatusCode status, object? body = null, string? raw = null)
    {
        Responses.Enqueue(_ => new HttpResponseMessage(status)
        {
            Content = new StringContent(raw ?? (body == null ? string.Empty : JsonSettings.Serialize(body)), Encoding.UTF8, "application/json")
        });
        return this;
    }

    public FakeHandler EnqueueNetworkError()
    {
        Responses.Enqueue(_ => throw new HttpRequestException("connection refused"));
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.Method, request.RequestUri!.ToString(), request.Headers.Authorization?.Parameter, body));
        Func<HttpRequestMessage, HttpResponseMessage> next = Responses.Count > 0 ? Responses.Dequeue() : _ => new HttpResponseMessage(HttpStatusCode.NotFound);
        return next(request);
    }
}

public class BalanceCompatibilityTests
{
    [Fact]
    public void ServerJson_ParsesOnClient_WithIdenticalHash()
    {
        var server = GameBalance.CreateDefault();
        string json = CrushRoyale.Server.Infrastructure.Json.Serialize(server);
        string hash = server.ComputeHash().ToString("x16");

        GameBalance client = GameClient.ParseBalance(json, hash);
        Assert.Equal(9, client.PowerUps.Definitions.Count);
        Assert.Equal(server.ComputeHash(), client.ComputeHash());
    }

    [Fact]
    public void ModifiedServerBalance_StillMatches_AndWrongHashIsRejected()
    {
        var server = GameBalance.CreateDefault();
        server.ComboMeter.CascadeFill = 500;
        server.Scoring.CascadeMultipliersPermille = new[] { 1000, 1100, 1600, 2100, 3100 };
        server.PowerUps.Get(PowerUpType.NuclearBomb).PriceCoins = 777;
        string json = CrushRoyale.Server.Infrastructure.Json.Serialize(server);

        GameBalance client = GameClient.ParseBalance(json, server.ComputeHash().ToString("x16"));
        Assert.Equal(500, client.ComboMeter.CascadeFill);
        Assert.Equal(5, client.Scoring.CascadeMultipliersPermille.Length);
        Assert.Throws<CrushApiException>(() => GameClient.ParseBalance(json, "0000000000000000"));
    }
}

public class ApiClientTests
{
    private static readonly ClientOptions Options = new() { ApiBaseUrl = "https://api.test", SupabaseUrl = "https://proj.supabase.co", SupabasePublishableKey = "sb_publishable_x" };

    private static ApiClient Client(FakeHandler handler, Func<CancellationToken, Task<bool>>? refresh = null, string token = "t1") =>
        new(Options, new HttpClient(handler), _ => Task.FromResult<string>(token), refresh, (_, _) => Task.CompletedTask);

    [Fact]
    public async Task Retries503_ThenSucceeds()
    {
        var handler = new FakeHandler().Enqueue(HttpStatusCode.ServiceUnavailable).Enqueue(HttpStatusCode.OK, new WalletDto { Coins = 42 });
        WalletDto wallet = await Client(handler).PostAsync<WalletDto>("/v1/x", new { a = 1 });
        Assert.Equal(42, wallet.Coins);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("t1", handler.Requests[0].Auth);
    }

    [Fact]
    public async Task Post500_IsNotRetried_ButGet500Is()
    {
        var post = new FakeHandler().Enqueue(HttpStatusCode.InternalServerError).Enqueue(HttpStatusCode.OK, new WalletDto());
        await Assert.ThrowsAsync<CrushApiException>(() => Client(post).PostAsync<WalletDto>("/v1/x", null!));
        Assert.Single(post.Requests);

        var get = new FakeHandler().Enqueue(HttpStatusCode.InternalServerError).Enqueue(HttpStatusCode.OK, new WalletDto { Orbes = 5 });
        Assert.Equal(5, (await Client(get).GetAsync<WalletDto>("/v1/x")).Orbes);
    }

    [Fact]
    public async Task Unauthorized_RefreshesOnce()
    {
        int refreshes = 0;
        var handler = new FakeHandler().Enqueue(HttpStatusCode.Unauthorized).Enqueue(HttpStatusCode.OK, new WalletDto { Coins = 1 });
        var client = Client(handler, _ => { refreshes++; return Task.FromResult(true); });
        Assert.Equal(1, (await client.GetAsync<WalletDto>("/v1/me")).Coins);
        Assert.Equal(1, refreshes);
    }

    [Fact]
    public async Task BusinessErrors_KeepServerCode()
    {
        var handler = new FakeHandler().Enqueue(HttpStatusCode.Conflict, new ApiErrorDto { Code = "NotEnoughLives", Message = "No lives" });
        var ex = await Assert.ThrowsAsync<CrushApiException>(() => Client(handler).PostAsync<object>("/v1/story/1/start", new { }));
        Assert.Equal("NotEnoughLives", ex.Code);
        Assert.Equal(409, ex.StatusCode);
        Assert.False(ex.IsRetryable);
    }

    [Fact]
    public async Task NetworkFailure_AfterAllAttempts_IsNetwork()
    {
        var handler = new FakeHandler().EnqueueNetworkError().EnqueueNetworkError().EnqueueNetworkError();
        var ex = await Assert.ThrowsAsync<CrushApiException>(() => Client(handler).GetAsync<object>("/v1/me"));
        Assert.True(ex.IsNetwork);
        Assert.Equal(3, handler.Requests.Count);
    }
}

public class OfflineQueueTests
{
    private static readonly ClientOptions Options = new() { ApiBaseUrl = "https://api.test", SupabaseUrl = "https://proj.supabase.co", SupabasePublishableKey = "sb_publishable_x", MaxAttempts = 1 };

    [Fact]
    public async Task QueuedSubmissions_SurviveRestart_AndFlushInOrder()
    {
        string path = Path.Combine(Path.GetTempPath(), "crush-queue-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var queue = new OfflineQueue(new FileQueueStorage(path));
            queue.Enqueue("stage", HttpMethod.Post, "/v1/a", new { n = 1 });
            queue.Enqueue("pvp", HttpMethod.Post, "/v1/b", new { n = 2 });
            queue.Enqueue("boss", HttpMethod.Post, "/v1/c", new { n = 3 });

            var restarted = new OfflineQueue(new FileQueueStorage(path));
            Assert.Equal(3, restarted.Count);

            var offline = new FakeHandler().EnqueueNetworkError();
            var api = new ApiClient(Options, new HttpClient(offline), _ => Task.FromResult<string>("t"), null, (_, _) => Task.CompletedTask);
            Assert.Equal(0, await restarted.FlushAsync(api));
            Assert.Equal(3, restarted.Count);

            var online = new FakeHandler()
                .Enqueue(HttpStatusCode.OK, new { ok = 1 })
                .Enqueue(HttpStatusCode.UnprocessableEntity, new ApiErrorDto { Code = "ReplayMismatch", Message = "bad" })
                .Enqueue(HttpStatusCode.OK, new { ok = 3 });
            var delivered = new List<string>();
            var dropped = new List<string>();
            restarted.Delivered += (r, _) => delivered.Add(r.Kind);
            restarted.Dropped += (r, _) => dropped.Add(r.Kind);

            api = new ApiClient(Options, new HttpClient(online), _ => Task.FromResult<string>("t"), null, (_, _) => Task.CompletedTask);
            Assert.Equal(2, await restarted.FlushAsync(api));
            Assert.Equal(new[] { "stage", "boss" }, delivered);
            Assert.Equal(new[] { "pvp" }, dropped);
            Assert.Equal(0, new OfflineQueue(new FileQueueStorage(path)).Count);
            Assert.Equal(new[] { "https://api.test/v1/a", "https://api.test/v1/b", "https://api.test/v1/c" }, online.Requests.Select(r => r.Url));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class SupabaseAuthTests
{
    private static readonly ClientOptions Options = new() { ApiBaseUrl = "https://api.test", SupabaseUrl = "https://proj.supabase.co", SupabasePublishableKey = "sb_publishable_x" };

    private static object SessionJson(string access, string refresh, long expiresAt) => new JObject
    {
        ["access_token"] = access,
        ["refresh_token"] = refresh,
        ["token_type"] = "bearer",
        ["expires_in"] = 3600,
        ["expires_at"] = expiresAt,
        ["user"] = new JObject { ["id"] = "11111111-1111-1111-1111-111111111111", ["is_anonymous"] = true }
    };

    [Fact]
    public async Task AnonymousSignIn_RefreshBeforeExpiry_AndRevokedRefreshSignsOut()
    {
        DateTime now = new(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc);
        long nowSec = new DateTimeOffset(now).ToUnixTimeSeconds();
        var handler = new FakeHandler();
        handler.Responses.Enqueue(_ => Json(SessionJson("a1", "r1", nowSec + 3600)));
        handler.Responses.Enqueue(_ => Json(SessionJson("a2", "r2", nowSec + 7200)));
        handler.Responses.Enqueue(_ => new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{\"error\":\"invalid_grant\",\"error_description\":\"Refresh Token Not Found\"}") });

        var store = new InMemorySessionStore();
        var auth = new SupabaseAuthClient(Options, new HttpClient(handler), store, () => now);

        SupabaseSession session = await auth.SignInAnonymouslyAsync();
        Assert.True(auth.IsAnonymous);
        Assert.Equal("a1", store.Load().AccessToken);
        Assert.Equal("https://proj.supabase.co/auth/v1/signup", handler.Requests[0].Url);
        Assert.Equal("a1", await auth.GetAccessTokenAsync());

        now = now.AddSeconds(3590);
        Assert.Equal("a2", await auth.GetAccessTokenAsync());
        Assert.Contains("grant_type=refresh_token", handler.Requests[1].Url);
        Assert.Contains("\"refresh_token\":\"r1\"", handler.Requests[1].Body);

        now = now.AddHours(3);
        Assert.Null(await auth.GetAccessTokenAsync());
        Assert.False(auth.IsSignedIn);
        Assert.Null(store.Load());
        Assert.Equal("11111111-1111-1111-1111-111111111111", session.User.Id);
    }

    [Fact]
    public void Options_RejectSecretKeys()
    {
        var options = new ClientOptions { ApiBaseUrl = "https://a.b", SupabaseUrl = "https://c.d", SupabasePublishableKey = "sb_secret_nope" };
        Assert.Throws<ArgumentException>(options.Validate);
        Assert.Equal(64, DeviceHash.Compute("android-id", "salt").Length);
        Assert.Equal(DeviceHash.Compute("x", "s"), DeviceHash.Compute("x", "s"));
    }

    private static HttpResponseMessage Json(object body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body.ToString()!, Encoding.UTF8, "application/json") };
}

public class RealtimeAndMatchTests
{
    [Fact]
    public void ParsesPostgresChangeFrames()
    {
        JObject frame = JObject.Parse(@"{
          ""topic"": ""realtime:guild-chat"", ""event"": ""postgres_changes"", ""ref"": null,
          ""payload"": { ""ids"": [1], ""data"": { ""schema"": ""public"", ""table"": ""guild_messages"", ""type"": ""INSERT"",
            ""commit_timestamp"": ""2026-09-16T12:00:00Z"",
            ""record"": { ""id"": 77, ""guild_id"": 5, ""player_id"": ""abc"", ""display_name"": ""Lyra"", ""body"": ""gg"", ""masked"": false, ""created_at"": ""2026-09-16T12:00:00.123+00:00"" } } } }");
        ChatMessageDto? message = RealtimeChatClient.ParseChange(frame);
        Assert.NotNull(message);
        Assert.Equal(77, message!.Id);
        Assert.Equal("gg", message.Body);
        Assert.Equal(new DateTimeOffset(2026, 9, 16, 12, 0, 0, 123, TimeSpan.Zero).ToUnixTimeMilliseconds(), message.CreatedAtUnixMs);
        Assert.Null(RealtimeChatClient.ParseChange(JObject.Parse(@"{""event"":""phx_reply"",""payload"":{}}")));
    }

    [Fact]
    public void MatchTicket_BuildsTheConfigTheServerValidates()
    {
        var balance = GameBalance.CreateDefault();
        var catalog = new StageCatalog(balance);
        var start = new MatchStartResponse
        {
            MatchId = "st_1",
            Mode = "Story",
            Seed = catalog.Get(3).Seed.ToString(),
            StageId = 3,
            HighestLeague = "Bronze",
            AssistExtraMoves = 2,
            Loadout = { new LoadoutEntryDto { Type = "ChronoBomb", Quantity = 2 } }
        };

        GameSession session = MatchConfigFactory.CreateSession(start, balance, catalog, "player-1");
        HeadlessRunner.Run(session, new GreedyBot());
        SubmitReplayRequest submission = MatchConfigFactory.ToSubmission(start.MatchId, session.Replay);

        ReplayData decoded = ReplayCodec.Decode(submission.ReplayBase64);
        SessionConfig serverConfig = SessionConfig.ForStage(catalog.Get(3), balance, new[] { new LoadoutEntry(PowerUpType.ChronoBomb, 2) }, League.Bronze, 2);
        Assert.True(ReplaySimulator.Verify(decoded, serverConfig, balance).Valid);
        Assert.Equal(session.Config.MoveLimit + 2, session.MovesUsed + Math.Max(0, session.MovesLeft));
    }
}
