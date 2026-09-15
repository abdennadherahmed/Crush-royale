using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrushRoyale.Server.Infrastructure;

/// <summary>Single JSON configuration for the database documents and the HTTP API.</summary>
public static class Json
{
    public static readonly JsonSerializerOptions Options = Create();

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException("Null document for " + typeof(T).Name);

    /// <summary>Deep copy through JSON (used by the in-memory store to emulate transactional isolation).</summary>
    public static T Clone<T>(T value) => Deserialize<T>(Serialize(value));

    public static void Configure(JsonSerializerOptions options)
    {
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.DictionaryKeyPolicy = null;
        // Must stay false: anonymous response objects only have read-only properties. Computed getters of Core types
        // (e.g. Guild.TotalTrophies) are written but safely ignored when reading documents back.
        options.IgnoreReadOnlyProperties = false;
        options.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
        options.NumberHandling = JsonNumberHandling.AllowReadingFromString;
        options.Converters.Add(new JsonStringEnumConverter());
    }

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions();
        Configure(options);
        return options;
    }
}
