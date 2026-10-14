using System;
using Newtonsoft.Json;
using UnityEngine;

namespace CrushRoyale.Game
{
    /// <summary>Per-build settings loaded from Resources/Config/client.json (no secrets: publishable keys only).</summary>
    [Serializable]
    public sealed class ClientConfig
    {
        public const string ResourcePath = "Config/client";

        [JsonProperty("apiBaseUrl")] public string ApiBaseUrl;
        [JsonProperty("supabaseUrl")] public string SupabaseUrl;
        [JsonProperty("supabasePublishableKey")] public string SupabasePublishableKey;
        [JsonProperty("googleWebClientId")] public string GoogleWebClientId;
        [JsonProperty("deviceSalt")] public string DeviceSalt;
        [JsonProperty("iapEnabled")] public bool IapEnabled;
        [JsonProperty("adsEnabled")] public bool AdsEnabled;
        [JsonProperty("levelPlayAppKey")] public string LevelPlayAppKey;

        /// <summary>The LevelPlay ad unit the free-life video comes from.</summary>
        [JsonProperty("levelPlayRewardedUnitId")] public string LevelPlayRewardedUnitId;

        /// <summary>The ad unit the between-rounds interstitial comes from. The server decides when it shows.</summary>
        [JsonProperty("levelPlayInterstitialUnitId")] public string LevelPlayInterstitialUnitId;
        [JsonProperty("privacyPolicyUrl")] public string PrivacyPolicyUrl;
        [JsonProperty("termsUrl")] public string TermsUrl;
        [JsonProperty("supportEmail")] public string SupportEmail;

        public bool IsConfigured =>
            !string.IsNullOrEmpty(ApiBaseUrl) && !ApiBaseUrl.Contains("example")
            && !string.IsNullOrEmpty(SupabaseUrl) && !SupabaseUrl.Contains("YOUR-PROJECT-REF")
            && !string.IsNullOrEmpty(SupabasePublishableKey) && !SupabasePublishableKey.Contains("REPLACE_ME");

        public static ClientConfig Load()
        {
            TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                Debug.LogError("Missing Resources/" + ResourcePath + ".json");
                return new ClientConfig();
            }
            return JsonConvert.DeserializeObject<ClientConfig>(asset.text) ?? new ClientConfig();
        }
    }
}
