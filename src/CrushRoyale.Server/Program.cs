using System.Security.Claims;
using System.Security.Cryptography;
using System.Threading.RateLimiting;
using CrushRoyale.Core.Common;
using CrushRoyale.Core.Pvp;
using CrushRoyale.Core.Social;
using CrushRoyale.Server.Auth;
using CrushRoyale.Server.Endpoints;
using CrushRoyale.Server.Infrastructure;
using CrushRoyale.Server.Persistence;
using CrushRoyale.Server.Services;
using Npgsql;

if (args.Length > 0 && args[0] == "export-stages")
{
    string output = args.Length > 1 ? args[1] : "export";
    ConfigExporter.Export(output);
    Console.WriteLine("Game data exported to " + Path.GetFullPath(output));
    return;
}

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = 256 * 1024);

DatabaseOptions database = builder.Configuration.GetSection("Database").Get<DatabaseOptions>() ?? new DatabaseOptions();
IapOptions iap = builder.Configuration.GetSection("Iap").Get<IapOptions>() ?? new IapOptions();
GameServerOptions game = builder.Configuration.GetSection("Game").Get<GameServerOptions>() ?? new GameServerOptions();
builder.Services.AddSingleton(database);
builder.Services.AddSingleton(iap);
builder.Services.AddSingleton(game);

builder.Services.ConfigureHttpJsonOptions(options => Json.Configure(options.SerializerOptions));
builder.Services.AddSingleton<IClock>(SystemClock.Instance);
builder.Services.AddSingleton<IBalanceProvider, BalanceProvider>();

if (database.UseInMemory)
{
    if (builder.Environment.IsProduction())
    {
        throw new InvalidOperationException("Database:UseInMemory is not allowed in Production (no durability).");
    }
    builder.Services.AddSingleton<IGameStore>(sp => new InMemoryGameStore(() => sp.GetRequiredService<IBalanceProvider>().Current));
}
else
{
    if (string.IsNullOrWhiteSpace(database.ConnectionString))
    {
        throw new InvalidOperationException("Configure Database:ConnectionString (crush_api role, SSL Mode=Require) or Database:UseInMemory.");
    }
    builder.Services.AddSingleton(_ => new NpgsqlDataSourceBuilder(database.ConnectionString).Build());
    builder.Services.AddSingleton<IGameStore>(sp => new PostgresGameStore(sp.GetRequiredService<NpgsqlDataSource>(), () => sp.GetRequiredService<IBalanceProvider>().Current));
}

if (iap.AllowFakeReceipts)
{
    if (builder.Environment.IsProduction())
    {
        throw new InvalidOperationException("Iap:AllowFakeReceipts is not allowed in Production.");
    }
    builder.Services.AddSingleton<IReceiptValidator, FakeReceiptValidator>();
}
else if (!string.IsNullOrWhiteSpace(iap.ServiceAccountJsonPath))
{
    builder.Services.AddSingleton<IReceiptValidator, GooglePlayReceiptValidator>();
}
else
{
    builder.Services.AddSingleton<IReceiptValidator, DisabledReceiptValidator>();
}

builder.Services.AddSingleton<PlayerOperations>();
builder.Services.AddSingleton<ProfileService>();
builder.Services.AddSingleton<StoryService>();
builder.Services.AddSingleton<EconomyService>();
builder.Services.AddSingleton<PetService>();
builder.Services.AddSingleton<ProgressionService>();
builder.Services.AddSingleton<PvpService>();
builder.Services.AddSingleton<SocialService>();
builder.Services.AddSingleton<GuildService>();
builder.Services.AddSingleton<AdminService>();
builder.Services.AddSingleton<SeasonJobService>();
builder.Services.AddSingleton<GhostPoolCache>();
builder.Services.AddSingleton(sp => new MatchmakingEngine(
    sp.GetRequiredService<IBalanceProvider>().Current,
    sp.GetRequiredService<IClock>(),
    sp.GetRequiredService<GhostPoolCache>(),
    BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(8))));
builder.Services.AddSingleton(sp => new ChatModerator(sp.GetRequiredService<IBalanceProvider>().Current, sp.GetRequiredService<IClock>()));

builder.Services.AddCrushAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(ApiEndpoints.PlayerRateLimit, http => RateLimitPartition.GetFixedWindowLimiter(
        http.User.FindFirstValue(CrushClaims.Subject) ?? http.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = Math.Max(10, game.RateLimitPerMinute), Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

if (game.EnableBackgroundJobs)
{
    builder.Services.AddHostedService<GameWorker>();
}

WebApplication app = builder.Build();
app.UseApiErrorHandling();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapCrushEndpoints();

if (app.Services.GetService<SupabaseJwksProvider>() is SupabaseJwksProvider jwks)
{
    await jwks.RefreshAsync(CancellationToken.None);
}
await app.Services.GetRequiredService<IBalanceProvider>().RefreshAsync(app.Services.GetRequiredService<IGameStore>(), CancellationToken.None);

app.Run();

/// <summary>Entry point marker for WebApplicationFactory integration tests.</summary>
public partial class Program
{
}
