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

    /// <summary>
    /// The same credentials as a value rather than a path, for hosts that hand secrets over as environment
    /// variables and give you no durable disk. Takes precedence over the path when both are set.
    /// </summary>
    public string? ServiceAccountJson { get; set; }

    /// <summary>True when credentials are available by either route.</summary>
    public bool HasServiceAccount =>
        !string.IsNullOrWhiteSpace(ServiceAccountJson) || !string.IsNullOrWhiteSpace(ServiceAccountJsonPath);

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

    /// <summary>
    /// Rewarded ad grants. Safe to leave on now that a reward is only paid against a signed callback from the ad
    /// network itself: a modified client that never watches anything has nothing to claim (audit S-01).
    /// </summary>
    public bool RewardedAdsEnabled { get; set; }
}

/// <summary>LevelPlay mediation (section "Ads").</summary>
public sealed class AdsOptions
{
    /// <summary>
    /// The private key typed into the LevelPlay dashboard beside the callback URL. The callback's signature is the
    /// MD5 of timestamp, event id, user id, reward count and this key, concatenated; without the key the signature
    /// cannot be forged, and without the signature the callback is refused.
    /// </summary>
    public string? LevelPlayPrivateKey { get; set; }

    /// <summary>True when the callback can be verified at all. Nothing is credited while this is false.</summary>
    public bool CanVerifyCallbacks => !string.IsNullOrWhiteSpace(LevelPlayPrivateKey);

    /// <summary>The rewarded ad unit the game should ask LevelPlay for. Handed to the client at startup.</summary>
    public string? RewardedUnitId { get; set; }

    /// <summary>The interstitial ad unit. Empty if interstitials are not wanted yet.</summary>
    public string? InterstitialUnitId { get; set; }
}
