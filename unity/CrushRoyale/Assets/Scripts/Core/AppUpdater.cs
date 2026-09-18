using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace CrushRoyale.Game
{
    /// <summary>
    /// Self-update of directly-installed APKs: at launch the game reads releases/android/latest.json (published by the
    /// CI in the public Supabase Storage bucket), downloads a newer APK (split in parts under the free 50 MB file limit),
    /// checks its SHA-256 and opens the system installer. Google Play installs are left to the store.
    /// </summary>
    public sealed class AppUpdater
    {
        [Serializable]
        public sealed class Manifest
        {
            [JsonProperty("versionCode")] public long VersionCode;
            [JsonProperty("minVersionCode")] public long MinVersionCode;
            [JsonProperty("size")] public long Size;
            [JsonProperty("sha256")] public string Sha256;
            [JsonProperty("parts")] public List<string> Parts = new List<string>();
        }

        public enum Outcome
        {
            /// <summary>No update (or none reachable): continue into the game.</summary>
            UpToDate,

            /// <summary>The system installer was opened.</summary>
            InstallerOpened,

            /// <summary>An update exists but could not be installed (download error, permission refused).</summary>
            Failed
        }

        private const string InstallerClass = "com.crushroyale.updater.ApkInstaller";
        private const string ApkName = "crushroyale-update.apk";
        private const int CheckTimeoutSeconds = 5;

        private readonly string _baseUrl;

        public AppUpdater(ClientConfig config)
        {
            _baseUrl = string.IsNullOrEmpty(config?.SupabaseUrl) ? null : config.SupabaseUrl.TrimEnd('/') + "/storage/v1/object/public/releases/";
        }

        public Manifest Available { get; private set; }

        /// <summary>True when the running build can update itself (Android APK not installed by Google Play).</summary>
        public bool Supported
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return _baseUrl != null && Application.installerName != "com.android.vending" && CurrentVersionCode > 0;
#else
                return false;
#endif
            }
        }

        public long CurrentVersionCode => CallInstaller<long>("versionCode", -1L);

        /// <summary>Must the player update before playing (the server no longer accepts this build)?</summary>
        public bool Required => Available != null && Available.MinVersionCode > CurrentVersionCode;

        /// <summary>Reads the latest release; true when it is newer than this build.</summary>
        public async Task<bool> CheckAsync()
        {
            if (!Supported)
            {
                return false;
            }
            string json = await GetTextAsync(_baseUrl + "android/latest.json?t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds(), CheckTimeoutSeconds);
            if (string.IsNullOrEmpty(json))
            {
                return false;
            }
            try
            {
                Manifest manifest = JsonConvert.DeserializeObject<Manifest>(json);
                if (manifest == null || manifest.Parts == null || manifest.Parts.Count == 0 || string.IsNullOrEmpty(manifest.Sha256))
                {
                    return false;
                }
                Available = manifest;
                return manifest.VersionCode > CurrentVersionCode;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        /// <summary>Downloads (or reuses) the verified APK, then opens the installer. Progress goes from 0 to 1.</summary>
        public async Task<Outcome> DownloadAndInstallAsync(Action<float> progress, Func<Task<bool>> askInstallPermission)
        {
            if (Available == null)
            {
                return Outcome.UpToDate;
            }
            string folder = CallInstaller<string>("updatesDir", null);
            if (string.IsNullOrEmpty(folder))
            {
                return Outcome.Failed;
            }
            string apk = Path.Combine(folder, ApkName);
            if (!await IsValidAsync(apk, Available))
            {
                if (!await DownloadAsync(apk, progress))
                {
                    return Outcome.Failed;
                }
                if (!await IsValidAsync(apk, Available))
                {
                    TryDelete(apk);
                    return Outcome.Failed;
                }
            }
            progress?.Invoke(1f);

            if (!CallInstaller("canInstall", false))
            {
                if (askInstallPermission == null || !await askInstallPermission())
                {
                    return Outcome.Failed;
                }
                CallInstaller<bool>("openInstallPermission", false, returnsVoid: true);
                // Back from the settings screen: the permission applies immediately.
                float deadline = Time.realtimeSinceStartup + 120f;
                while (!CallInstaller("canInstall", false) && Time.realtimeSinceStartup < deadline)
                {
                    await Task.Delay(500);
                }
                if (!CallInstaller("canInstall", false))
                {
                    return Outcome.Failed;
                }
            }
            return CallInstaller("install", false, ApkName) ? Outcome.InstallerOpened : Outcome.Failed;
        }

        private async Task<bool> DownloadAsync(string apk, Action<float> progress)
        {
            string partial = apk + ".part";
            TryDelete(partial);
            long done = 0;
            long total = Math.Max(1, Available.Size);
            try
            {
                using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write))
                {
                    foreach (string part in Available.Parts)
                    {
                        using (UnityWebRequest request = UnityWebRequest.Get(_baseUrl + part))
                        {
                            request.timeout = 600;
                            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
                            while (!operation.isDone)
                            {
                                progress?.Invoke(Mathf.Clamp01((done + (long)request.downloadedBytes) / (float)total));
                                await Task.Yield();
                            }
                            if (!Succeeded(request))
                            {
                                Debug.LogWarning("Update download failed: " + request.error);
                                return false;
                            }
                            byte[] bytes = request.downloadHandler.data;
                            await output.WriteAsync(bytes, 0, bytes.Length);
                            done += bytes.Length;
                        }
                    }
                }
                TryDelete(apk);
                File.Move(partial, apk);
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Update download failed: " + ex.Message);
                TryDelete(partial);
                return false;
            }
        }

        private static Task<bool> IsValidAsync(string path, Manifest manifest) =>
            Task.Run(() =>
            {
                try
                {
                    if (!File.Exists(path) || new FileInfo(path).Length != manifest.Size)
                    {
                        return false;
                    }
                    using (SHA256 sha = SHA256.Create())
                    using (FileStream stream = File.OpenRead(path))
                    {
                        string hash = BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
                        return string.Equals(hash, manifest.Sha256, StringComparison.OrdinalIgnoreCase);
                    }
                }
                catch (Exception)
                {
                    return false;
                }
            });

        private static async Task<string> GetTextAsync(string url, int timeoutSeconds)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.timeout = timeoutSeconds;
                request.SetRequestHeader("Cache-Control", "no-cache");
                UnityWebRequestAsyncOperation operation = request.SendWebRequest();
                while (!operation.isDone)
                {
                    await Task.Yield();
                }
                return Succeeded(request) ? request.downloadHandler.text : null;
            }
        }

        private static bool Succeeded(UnityWebRequest request) =>
            string.IsNullOrEmpty(request.error) && request.responseCode >= 200 && request.responseCode < 300;

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
        }

        private static T CallInstaller<T>(string method, T fallback, params object[] args) => CallInstaller(method, fallback, false, args);

        private static T CallInstaller<T>(string method, T fallback, bool returnsVoid, params object[] args)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var installer = new AndroidJavaClass(InstallerClass))
                {
                    var callArgs = new object[args.Length + 1];
                    callArgs[0] = activity;
                    Array.Copy(args, 0, callArgs, 1, args.Length);
                    if (returnsVoid)
                    {
                        installer.CallStatic(method, callArgs);
                        return fallback;
                    }
                    return installer.CallStatic<T>(method, callArgs);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("ApkInstaller." + method + " failed: " + ex.Message);
                return fallback;
            }
#else
            return fallback;
#endif
        }
    }
}
