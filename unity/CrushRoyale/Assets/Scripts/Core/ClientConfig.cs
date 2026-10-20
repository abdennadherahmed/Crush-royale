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

        /// <summary>
        /// Rempli uniquement par l'outillage, pour jouer contre un serveur local lance en Development.
        ///
        /// Ce n'est pas un reglage de build : le fichier livre ne le contient pas, et un serveur de production
        /// refuse de toute facon cette authentification. Il existe pour qu'une action puisse etre jouee en ligne
        /// de bout en bout depuis l'editeur, qui n'a pas de compte Google.
        /// </summary>
        [JsonIgnore] public string DevUserId;

        public bool IsConfigured =>
            !string.IsNullOrEmpty(ApiBaseUrl) && !ApiBaseUrl.Contains("example")
            && (!string.IsNullOrEmpty(DevUserId)
                || (!string.IsNullOrEmpty(SupabaseUrl) && !SupabaseUrl.Contains("YOUR-PROJECT-REF")
                    && !string.IsNullOrEmpty(SupabasePublishableKey) && !SupabasePublishableKey.Contains("REPLACE_ME")));

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
