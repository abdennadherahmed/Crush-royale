using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CrushRoyale.Contracts;
using CrushRoyale.Server.Persistence;

namespace CrushRoyale.Server.Services;

/// <summary>
/// Home-made analytics and crash reports: validates and trims client batches, then stores them best effort
/// (a telemetry failure never breaks the game).
/// </summary>
public sealed partial class TelemetryService
{
    public const int MaxEvents = 200;
    public const int MaxErrors = 20;
    public const int MaxProps = 16;

    private readonly IGameStore _store;
    private readonly ILogger<TelemetryService> _logger;

    public TelemetryService(IGameStore store, ILogger<TelemetryService> logger)
    {
        _store = store;
        _logger = logger;
    }

    public async Task<object> RecordAsync(Guid userId, TelemetryRequest request, CancellationToken ct)
    {
        TelemetryBatch batch = Validate(request);
        if (batch.Events.Count > 0 || batch.Errors.Count > 0)
        {
            try
            {
                await _store.InsertTelemetryAsync(userId, batch, ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Telemetry batch dropped for {Player}", userId);
            }
        }
        return new { accepted = batch.Events.Count, errors = batch.Errors.Count };
    }

    public static TelemetryBatch Validate(TelemetryRequest? request)
    {
        var batch = new TelemetryBatch
        {
            SessionId = Clip(request?.SessionId, 64),
            AppVersion = Clip(request?.AppVersion, 32),
            Device = Clip(request?.Device, 96),
            Os = Clip(request?.Os, 96)
        };
        if (request == null)
        {
            return batch;
        }

        foreach (TelemetryEventDto e in (request.Events ?? new()).Take(MaxEvents))
        {
            string name = Clip(e?.Name, 64);
            if (e == null || !EventName().IsMatch(name))
            {
                continue;
            }
            var props = new Dictionary<string, string>();
            foreach (KeyValuePair<string, string> kv in (e.Props ?? new()).Take(MaxProps))
            {
                string key = Clip(kv.Key, 32);
                if (EventName().IsMatch(key))
                {
                    props[key] = Clip(kv.Value, 128);
                }
            }
            DateTime? at = e.AtUnixMs > 1_600_000_000_000 && e.AtUnixMs < 4_000_000_000_000
                ? DateTimeOffset.FromUnixTimeMilliseconds(e.AtUnixMs).UtcDateTime
                : null;
            batch.Events.Add((name, at, JsonSerializer.Serialize(props)));
        }

        foreach (ClientErrorDto error in (request.Errors ?? new()).Take(MaxErrors))
        {
            if (error == null || string.IsNullOrWhiteSpace(error.Message))
            {
                continue;
            }
            string message = Clip(error.Message, 2000);
            string stack = Clip(error.Stack, 8000);
            batch.Errors.Add((Fingerprint(message, stack), message, stack, Math.Clamp(error.Count, 1, 10_000)));
        }
        return batch;
    }

    /// <summary>Groups identical crashes: message without numbers + first stack frame.</summary>
    private static string Fingerprint(string message, string stack)
    {
        string normalized = Digits().Replace(message, "#") + "|" + (stack.Split('\n').FirstOrDefault() ?? string.Empty);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)))[..32];
    }

    private static string Clip(string? value, int max) =>
        string.IsNullOrEmpty(value) ? string.Empty : value.Length <= max ? value : value[..max];

    [GeneratedRegex("^[a-z][a-z0-9_]{0,63}$")]
    private static partial Regex EventName();

    [GeneratedRegex("[0-9]+")]
    private static partial Regex Digits();
}
