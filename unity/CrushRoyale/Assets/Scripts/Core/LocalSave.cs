using System;
using System.IO;
using CrushRoyale.Client;
using Newtonsoft.Json;
using UnityEngine;

namespace CrushRoyale.Game
{
    /// <summary>Device-local preferences (never game progress: the server owns that).</summary>
    [Serializable]
    public sealed class PlayerSettings
    {
        public float MasterVolume = 1f;
        public float MusicVolume = 0.7f;
        public float SfxVolume = 1f;
        public bool Muted;
        public bool HapticsEnabled = true;
        public bool NotificationsEnabled = true;
        public bool ColorBlindMode;
        public string Language;
        public bool TutorialDone;
        public bool PrivacyAccepted;
        public int LastSeenStage;

        /// <summary>Hero chosen on this device (possibly offline, synced to the server at the next login).</summary>
        public bool HeroCreated;
        public string HeroGender;
        public string HeroPseudo;
        public int HeroAge;

        /// <summary>The first duel is played against a gentle bot (onboarding).</summary>
        public bool FirstDuelDone;
    }

    /// <summary>
    /// Files in Application.persistentDataPath (app-private storage on Android): settings, auth session,
    /// offline request queue and the last downloaded game rules.
    /// </summary>
    public sealed class LocalSave : IConfigCache
    {
        private readonly string _root;
        private readonly string _settingsPath;
        private readonly string _configPath;

        public LocalSave()
        {
            _root = Application.persistentDataPath;
            _settingsPath = Path.Combine(_root, "settings.json");
            _configPath = Path.Combine(_root, "game_config.json");
            Settings = LoadSettings();
            Sessions = new FileSessionStore(Path.Combine(_root, "session.json"));
            Queue = new FileQueueStorage(Path.Combine(_root, "pending_requests.json"));
        }

        public PlayerSettings Settings { get; }

        public ISessionStore Sessions { get; }

        public IQueueStorage Queue { get; }

        public void SaveSettings()
        {
            try
            {
                File.WriteAllText(_settingsPath, JsonConvert.SerializeObject(Settings, Formatting.Indented));
            }
            catch (IOException ex)
            {
                Debug.LogWarning("Could not save settings: " + ex.Message);
            }
        }

        string IConfigCache.Load() => File.Exists(_configPath) ? File.ReadAllText(_configPath) : null;

        void IConfigCache.Save(string configJson)
        {
            try
            {
                File.WriteAllText(_configPath, configJson);
            }
            catch (IOException ex)
            {
                Debug.LogWarning("Could not cache game config: " + ex.Message);
            }
        }

        /// <summary>SHA-256 of the platform device id, salted per game (the raw id never leaves the device).</summary>
        public string DeviceHash(string salt)
        {
            string raw = SystemInfo.deviceUniqueIdentifier;
            if (string.IsNullOrEmpty(raw) || raw == SystemInfo.unsupportedIdentifier)
            {
                raw = PlayerPrefs.GetString("crush.install_id", string.Empty);
                if (string.IsNullOrEmpty(raw))
                {
                    raw = Guid.NewGuid().ToString("N");
                    PlayerPrefs.SetString("crush.install_id", raw);
                    PlayerPrefs.Save();
                }
            }
            return CrushRoyale.Client.DeviceHash.Compute(raw, salt);
        }

        private PlayerSettings LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsPath))
                {
                    return JsonConvert.DeserializeObject<PlayerSettings>(File.ReadAllText(_settingsPath)) ?? new PlayerSettings();
                }
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException)
            {
                Debug.LogWarning("Settings reset: " + ex.Message);
            }
            return new PlayerSettings();
        }
    }
}
