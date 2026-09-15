using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace CrushRoyale.Client
{
    /// <summary>Endpoints and tuning of the client SDK.</summary>
    public sealed class ClientOptions
    {
        /// <summary>Base URL of the Crush Royale API, e.g. https://api.crushroyale.com</summary>
        public string ApiBaseUrl { get; set; }

        /// <summary>https://&lt;project-ref&gt;.supabase.co</summary>
        public string SupabaseUrl { get; set; }

        /// <summary>Supabase publishable key (sb_publishable_...). Never ship a secret key in the app.</summary>
        public string SupabasePublishableKey { get; set; }

        public int TimeoutSeconds { get; set; } = 10;

        public int MaxAttempts { get; set; } = 3;

        public void Validate()
        {
            if (string.IsNullOrWhiteSpace(ApiBaseUrl) || !Uri.IsWellFormedUriString(ApiBaseUrl, UriKind.Absolute))
            {
                throw new ArgumentException("ApiBaseUrl must be an absolute URL.");
            }
            if (string.IsNullOrWhiteSpace(SupabaseUrl) || !Uri.IsWellFormedUriString(SupabaseUrl, UriKind.Absolute))
            {
                throw new ArgumentException("SupabaseUrl must be an absolute URL.");
            }
            if (string.IsNullOrWhiteSpace(SupabasePublishableKey))
            {
                throw new ArgumentException("SupabasePublishableKey is required.");
            }
            if (SupabasePublishableKey.StartsWith("sb_secret_", StringComparison.Ordinal))
            {
                throw new ArgumentException("A Supabase SECRET key must never be embedded in the client.");
            }
        }
    }

    /// <summary>JSON settings matching the server (camelCase, enums as names, collections replaced on read).</summary>
    public static class JsonSettings
    {
        public static readonly JsonSerializerSettings Default = new JsonSerializerSettings
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver { NamingStrategy = new CamelCaseNamingStrategy { ProcessDictionaryKeys = false } },
            Converters = { new StringEnumConverter() },
            NullValueHandling = NullValueHandling.Ignore,
            // Critical: default collections (e.g. power-up definitions) must be replaced, not appended to.
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            Culture = CultureInfo.InvariantCulture
        };

        public static string Serialize(object value) => JsonConvert.SerializeObject(value, Default);

        public static T Deserialize<T>(string json) => JsonConvert.DeserializeObject<T>(json, Default);
    }

    /// <summary>Error returned by the API or the auth server, or a transport failure.</summary>
    public sealed class CrushApiException : Exception
    {
        public CrushApiException(string code, int statusCode, string message, bool isNetwork = false) : base(message)
        {
            Code = code;
            StatusCode = statusCode;
            IsNetwork = isNetwork;
        }

        /// <summary>Server ErrorCode name (NotEnoughLives, Banned...) or "Network", "Timeout", "AuthFailed".</summary>
        public string Code { get; }

        public int StatusCode { get; }

        /// <summary>True when the server could not be reached (safe to queue and retry later).</summary>
        public bool IsNetwork { get; }

        public bool IsRetryable => IsNetwork || StatusCode == 429 || StatusCode >= 500;
    }

    /// <summary>Hashes the device identifier on the device: the server never sees the raw id.</summary>
    public static class DeviceHash
    {
        public static string Compute(string rawDeviceId, string appSalt)
        {
            if (string.IsNullOrEmpty(rawDeviceId))
            {
                throw new ArgumentException("Device id required.", nameof(rawDeviceId));
            }
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes((appSalt ?? string.Empty) + ":" + rawDeviceId));
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash)
                {
                    sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                }
                return sb.ToString();
            }
        }
    }
}
