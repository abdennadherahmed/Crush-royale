namespace CrushRoyale.Server.Infrastructure;

/// <summary>Supabase Auth settings (appsettings section "Supabase").</summary>
public sealed class SupabaseOptions
{
    /// <summary>https://&lt;project-ref&gt;.supabase.co</summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>Supabase access tokens carry aud = "authenticated".</summary>
    public string Audience { get; set; } = "authenticated";

    /// <summary>Defaults to {Url}/auth/v1/.well-known/jwks.json (asymmetric signing keys).</summary>
    public string? JwksUrl { get; set; }

    /// <summary>Development/testing only: authenticate with the X-Dev-User header. Refused in Production.</summary>
    public bool DevAuthEnabled { get; set; }

    public string Issuer => Url.TrimEnd('/') + "/auth/v1";

    public string EffectiveJwksUrl => string.IsNullOrWhiteSpace(JwksUrl) ? Issuer + "/.well-known/jwks.json" : JwksUrl!;
}

/// <summary>Database settings (section "Database").</summary>
public sealed class DatabaseOptions
{
    /// <summary>Npgsql connection string for the crush_api role (SSL Mode=Require).</summary>
    public string? ConnectionString { get; set; }

    /// <summary>Use the in-memory store (development without Supabase).</summary>
    public bool UseInMemory { get; set; }
}

/// <summary>Google Play billing (section "Iap").</summary>
public sealed class IapOptions
{
    public string PackageName { get; set; } = "com.crushroyale.game";

    /// <summary>Path to the Google Play Console service-account JSON with Android Publisher access.</summary>
    public string? ServiceAccountJsonPath { get; set; }

    /// <summary>Development/testing only: accept receipts whose token starts with "fake:". Refused in Production.</summary>
    public bool AllowFakeReceipts { get; set; }
}

/// <summary>Game server behavior (section "Game").</summary>
public sealed class GameServerOptions
{
    /// <summary>Started matches not submitted within this delay expire.</summary>
    public int MatchExpiryMinutes { get; set; } = 20;

    public int MatchmakingTickMs { get; set; } = 1000;

    /// <summary>Supabase user ids with admin rights (in addition to app_metadata.role = "admin").</summary>
    public List<string> AdminUserIds { get; set; } = new();

    /// <summary>Requests per minute per player on write endpoints.</summary>
    public int RateLimitPerMinute { get; set; } = 120;

    public bool EnableBackgroundJobs { get; set; } = true;
}
