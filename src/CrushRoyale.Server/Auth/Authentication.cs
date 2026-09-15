using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using CrushRoyale.Server.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CrushRoyale.Server.Auth;

public static class CrushClaims
{
    public const string Subject = "sub";
    public const string IsAnonymous = "is_anonymous";
    public const string AppMetadata = "app_metadata";
    public const string DevAdmin = "crush_admin";

    public static Guid PlayerId(this ClaimsPrincipal user)
    {
        string? sub = user.FindFirstValue(Subject) ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(sub, out Guid id))
        {
            throw new ApiException(Core.Common.ErrorCode.PermissionDenied, "Token has no valid subject.");
        }
        return id;
    }

    public static bool IsAnonymousUser(this ClaimsPrincipal user) =>
        string.Equals(user.FindFirstValue(IsAnonymous), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Admin = app_metadata.role == "admin" (app_metadata is server-controlled; user_metadata is never trusted)
    /// or an id listed in Game:AdminUserIds.
    /// </summary>
    public static bool IsAdmin(this ClaimsPrincipal user, IReadOnlyCollection<string> adminIds)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return false;
        }
        string? sub = user.FindFirstValue(Subject);
        if (sub != null && adminIds.Contains(sub, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }
        if (string.Equals(user.FindFirstValue(DevAdmin), "true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string? appMetadata = user.FindFirstValue(AppMetadata);
        if (string.IsNullOrEmpty(appMetadata))
        {
            return false;
        }
        try
        {
            using JsonDocument doc = JsonDocument.Parse(appMetadata);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("role", out JsonElement role)
                && role.ValueKind == JsonValueKind.String
                && role.GetString() == "admin";
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

/// <summary>Caches the Supabase JWKS (asymmetric signing keys) and refreshes on unknown key ids.</summary>
public sealed class SupabaseJwksProvider
{
    public const string HttpClientName = "supabase-jwks";

    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan MinRefreshInterval = TimeSpan.FromSeconds(30);

    private readonly IHttpClientFactory _httpFactory;
    private readonly SupabaseOptions _options;
    private readonly ILogger<SupabaseJwksProvider> _logger;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private IList<SecurityKey> _keys = new List<SecurityKey>();
    private DateTime _fetchedAt = DateTime.MinValue;
    private DateTime _lastAttempt = DateTime.MinValue;

    public SupabaseJwksProvider(IHttpClientFactory httpFactory, SupabaseOptions options, ILogger<SupabaseJwksProvider> logger)
    {
        _httpFactory = httpFactory;
        _options = options;
        _logger = logger;
    }

    public IEnumerable<SecurityKey> GetKeys(string? kid)
    {
        bool stale = DateTime.UtcNow - _fetchedAt > CacheDuration;
        bool unknown = kid != null && !_keys.Any(k => k.KeyId == kid);
        if ((stale || unknown) && DateTime.UtcNow - _lastAttempt > MinRefreshInterval)
        {
            // The resolver is synchronous; refreshes are rare (cache + throttling).
            RefreshAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        IList<SecurityKey> keys = _keys;
        return kid == null ? keys : keys.Where(k => k.KeyId == kid).ToList();
    }

    public async Task RefreshAsync(CancellationToken cancellationToken)
    {
        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _lastAttempt = DateTime.UtcNow;
            HttpClient http = _httpFactory.CreateClient(HttpClientName);
            http.Timeout = TimeSpan.FromSeconds(10);
            string json = await http.GetStringAsync(_options.EffectiveJwksUrl, cancellationToken).ConfigureAwait(false);
            _keys = new JsonWebKeySet(json).GetSigningKeys();
            _fetchedAt = DateTime.UtcNow;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or ArgumentException)
        {
            _logger.LogWarning(ex, "Could not refresh Supabase JWKS from {Url}", _options.EffectiveJwksUrl);
        }
        finally
        {
            _refreshGate.Release();
        }
    }
}

/// <summary>Development/testing authentication: X-Dev-User: &lt;uuid&gt; (+ X-Dev-Admin: true).</summary>
public sealed class DevAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string UserHeader = "X-Dev-User";
    public const string AdminHeader = "X-Dev-Admin";

    public DevAuthHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(UserHeader, out var values) || !Guid.TryParse(values.ToString(), out Guid id))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new List<Claim> { new(CrushClaims.Subject, id.ToString()), new(CrushClaims.IsAnonymous, "false") };
        if (string.Equals(Request.Headers[AdminHeader].ToString(), "true", StringComparison.OrdinalIgnoreCase))
        {
            claims.Add(new Claim(CrushClaims.DevAdmin, "true"));
        }
        var identity = new ClaimsIdentity(claims, Scheme.Name, CrushClaims.Subject, "role");
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }
}

public static class AuthenticationSetup
{
    public const string DevScheme = "Dev";
    public const string AdminPolicy = "admin";

    public static IServiceCollection AddCrushAuthentication(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        SupabaseOptions supabase = configuration.GetSection("Supabase").Get<SupabaseOptions>() ?? new SupabaseOptions();
        GameServerOptions game = configuration.GetSection("Game").Get<GameServerOptions>() ?? new GameServerOptions();
        services.AddSingleton(supabase);

        bool hasSupabase = !string.IsNullOrWhiteSpace(supabase.Url);
        if (supabase.DevAuthEnabled && environment.IsProduction())
        {
            throw new InvalidOperationException("Supabase:DevAuthEnabled must never be enabled in Production.");
        }
        if (!hasSupabase && !supabase.DevAuthEnabled)
        {
            throw new InvalidOperationException("Configure Supabase:Url (or enable Supabase:DevAuthEnabled outside Production).");
        }

        AuthenticationBuilder builder = services.AddAuthentication(options =>
        {
            options.DefaultScheme = supabase.DevAuthEnabled ? DevScheme : JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = options.DefaultScheme;
        });

        if (hasSupabase)
        {
            services.AddHttpClient(SupabaseJwksProvider.HttpClientName);
            services.AddSingleton<SupabaseJwksProvider>();
            builder.AddJwtBearer();
            services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).Configure<SupabaseJwksProvider>((options, jwks) =>
            {
                options.MapInboundClaims = false;
                options.RequireHttpsMetadata = true;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = supabase.Issuer,
                    ValidateAudience = true,
                    ValidAudience = supabase.Audience,
                    ValidateLifetime = true,
                    RequireExpirationTime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    ValidAlgorithms = new[] { SecurityAlgorithms.EcdsaSha256, SecurityAlgorithms.RsaSha256 },
                    IssuerSigningKeyResolver = (_, _, kid, _) => jwks.GetKeys(kid),
                    NameClaimType = CrushClaims.Subject
                };
            });
        }

        if (supabase.DevAuthEnabled)
        {
            builder.AddScheme<AuthenticationSchemeOptions, DevAuthHandler>(DevScheme, null);
        }

        IReadOnlyCollection<string> adminIds = game.AdminUserIds;
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
            options.AddPolicy(AdminPolicy, policy => policy.RequireAssertion(ctx => ctx.User.IsAdmin(adminIds)));
        });
        return services;
    }
}
