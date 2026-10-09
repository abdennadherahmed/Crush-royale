using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;

namespace CrushRoyale.EditorTools
{
    /// <summary>One-click project setup: Android player settings, boot scene, DLL sync, localization audit.</summary>
    public static class CrushRoyaleSetup
    {
        private const string BootScenePath = "Assets/Scenes/Boot.unity";

        [MenuItem("Crush Royale/1. Configure Android Player Settings", priority = 1)]
        public static void ConfigureAndroid()
        {
            PlayerSettings.companyName = "Crush Royale";
            PlayerSettings.productName = "Crush Royale";
            PlayerSettings.bundleVersion = "1.0.0";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.crushroyale.game");
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
            PlayerSettings.allowedAutorotateToLandscapeLeft = false;
            PlayerSettings.allowedAutorotateToLandscapeRight = false;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Android, ApiCompatibilityLevel.NET_Standard);
            PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.Android, ManagedStrippingLevel.Low);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64 | AndroidArchitecture.ARMv7;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.Android.targetSdkVersion = HighestTargetSdk();
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.runInBackground = false;
            EditorUserBuildSettings.buildAppBundle = true;
            ApplyAppIcon();
            EnsureAndroidDependencies();
            Debug.Log("Crush Royale: Android settings applied (IL2CPP, ARM64, API 24+, portrait, AAB). " +
                      "Remember: Player > Other Settings > Active Input Handling = 'Input Manager (Old)' or 'Both', and set up keystore signing.");
        }

        private const string AppIconPath = "Assets/Art/AppIcon/app_icon.png";

        /// <summary>Uses the illustrated 1024px icon as the default icon for every platform (Android scales it per density).</summary>
        private static void ApplyAppIcon()
        {
            AssetDatabase.ImportAsset(AppIconPath, ImportAssetOptions.ForceSynchronousImport);
            Texture2D icon = AssetDatabase.LoadAssetAtPath<Texture2D>(AppIconPath);
            if (icon == null)
            {
                Debug.LogWarning("Crush Royale: app icon not found at " + AppIconPath + ", keeping the Unity default icon.");
                return;
            }
            PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
#if UNITY_ANDROID
            ApplyAdaptiveIcons(icon);
#endif
        }

#if UNITY_ANDROID
        private const string AdaptiveBackgroundPath = "Assets/Art/AppIcon/adaptive_background.png";
        private const string AdaptiveForegroundPath = "Assets/Art/AppIcon/adaptive_foreground.png";

        /// <summary>
        /// Android 8+ launchers mask icons to their own shape: adaptive layers (blurred art behind, art inside the safe
        /// zone in front) avoid the white frame around a plain square icon. Round icons reuse the full art.
        /// </summary>
        private static void ApplyAdaptiveIcons(Texture2D legacy)
        {
            AssetDatabase.ImportAsset(AdaptiveBackgroundPath, ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(AdaptiveForegroundPath, ImportAssetOptions.ForceSynchronousImport);
            Texture2D background = AssetDatabase.LoadAssetAtPath<Texture2D>(AdaptiveBackgroundPath);
            Texture2D foreground = AssetDatabase.LoadAssetAtPath<Texture2D>(AdaptiveForegroundPath);
            if (background == null || foreground == null)
            {
                Debug.LogWarning("Crush Royale: adaptive icon layers missing, Android uses the legacy icon only.");
                return;
            }

            PlatformIcon[] adaptive = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, UnityEditor.Android.AndroidPlatformIconKind.Adaptive);
            foreach (PlatformIcon slot in adaptive)
            {
                slot.SetTextures(background, foreground);
            }
            PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, UnityEditor.Android.AndroidPlatformIconKind.Adaptive, adaptive);

            PlatformIcon[] round = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, UnityEditor.Android.AndroidPlatformIconKind.Round);
            foreach (PlatformIcon slot in round)
            {
                slot.SetTexture(legacy);
            }
            PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, UnityEditor.Android.AndroidPlatformIconKind.Round, round);
        }
#endif

        private const string MainGradleTemplatePath = "Assets/Plugins/Android/mainTemplate.gradle";

        /// <summary>Gradle dependencies of Assets/Plugins/Android/GoogleSignInBridge.java (Credential Manager + Sign in with Google).</summary>
        private static readonly string[] AndroidDependencies =
        {
            "implementation 'androidx.credentials:credentials:1.3.0'",
            "implementation 'androidx.credentials:credentials-play-services-auth:1.3.0'",
            "implementation 'com.google.android.libraries.identity.googleid:googleid:1.1.1'"
        };

        /// <summary>
        /// Enables the custom main Gradle template (copied from the installed Unity version, so it always matches)
        /// and adds the dependencies of the Java plugins. Safe to run repeatedly.
        /// </summary>
        public static void EnsureAndroidDependencies()
        {
            if (!File.Exists(MainGradleTemplatePath))
            {
                string engine = BuildPipeline.GetPlaybackEngineDirectory(BuildTarget.Android, BuildOptions.None);
                string source = Path.Combine(engine, "Tools", "GradleTemplates", "mainTemplate.gradle");
                if (!File.Exists(source))
                {
                    throw new FileNotFoundException("Unity Android Gradle template not found (is Android Build Support installed?)", source);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(MainGradleTemplatePath));
                File.Copy(source, MainGradleTemplatePath);
            }

            const string marker = "**DEPS**";
            string template = File.ReadAllText(MainGradleTemplatePath);
            if (!template.Contains(marker))
            {
                throw new InvalidOperationException(MainGradleTemplatePath + " has no " + marker + " placeholder: add the dependencies manually.");
            }

            List<string> missing = AndroidDependencies.Where(d => !template.Contains(d)).ToList();
            if (missing.Count > 0)
            {
                template = template.Replace(marker, string.Join("\n    ", missing) + "\n    " + marker);
                File.WriteAllText(MainGradleTemplatePath, template);
                Debug.Log("Crush Royale: added Android Gradle dependencies: " + string.Join(", ", missing));
            }
            AssetDatabase.ImportAsset(MainGradleTemplatePath);
        }

        [MenuItem("Crush Royale/2. Create Boot Scene", priority = 2)]
        public static void CreateBootScene()
        {
            Directory.CreateDirectory("Assets/Scenes");
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraObject = new GameObject("Main Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            cameraObject.AddComponent<AudioListener>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.06f, 0.14f);
            camera.orthographic = true;
            cameraObject.tag = "MainCamera";
            EditorSceneManager.SaveScene(scene, BootScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(BootScenePath, true) };
            Debug.Log("Crush Royale: boot scene created at " + BootScenePath + ". GameBootstrap builds everything at runtime.");
        }

        [MenuItem("Crush Royale/3. Sync Core DLLs + Game Data", priority = 3)]
        public static void SyncDlls()
        {
            string repoRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", ".."));
            string script = Path.Combine(repoRoot, "tools", "sync-unity.ps1");
            if (!File.Exists(script))
            {
                Debug.LogError("tools/sync-unity.ps1 not found at " + script);
                return;
            }

            var info = new ProcessStartInfo("powershell", "-ExecutionPolicy Bypass -File \"" + script + "\"")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = repoRoot
            };
            using (Process process = Process.Start(info))
            {
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode == 0)
                {
                    Debug.Log(output);
                    AssetDatabase.Refresh();
                }
                else
                {
                    Debug.LogError(output + "\n" + error);
                }
            }
        }

        [MenuItem("Crush Royale/4. Audit Localization", priority = 20)]
        public static void AuditLocalization()
        {
            var used = new SortedSet<string>();
            var literal = new Regex("Loc(?:ale)?\\.T\\(\\s*\"([a-zA-Z0-9_.]+)\"");
            foreach (string file in Directory.GetFiles("Assets/Scripts", "*.cs", SearchOption.AllDirectories))
            {
                foreach (Match match in literal.Matches(File.ReadAllText(file)))
                {
                    used.Add(match.Groups[1].Value);
                }
            }

            var report = new StringBuilder();
            foreach (string path in Directory.GetFiles("Assets/Resources/Localization", "*.json"))
            {
                var table = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path));
                List<string> missing = used.Where(k => !table.ContainsKey(k)).ToList();
                report.AppendLine(Path.GetFileNameWithoutExtension(path) + ": " + table.Count + " strings, " + missing.Count + " missing" + (missing.Count > 0 ? " -> " + string.Join(", ", missing.Take(40)) : string.Empty));
            }
            Debug.Log("Localization audit (" + used.Count + " literal keys in code):\n" + report);
        }
            /// <summary>
        /// The Android API level to build against, chosen rather than inherited.
        ///
        /// This used to be AndroidApiLevelAuto, which means "whatever the machine compiling happens to have". That
        /// is not a decision, it is a consequence, and Google Play turns it into a rejection: since 31 August 2026
        /// a new application must target Android 16 (API 36) to be accepted at all, so a runner that shipped with
        /// an older SDK would produce a bundle nobody could upload, and the only clue would be the refusal.
        ///
        /// Asked of the editor rather than hardcoded: an editor that does not know level 36 cannot build against
        /// it, and failing the build over it would be worse than building what this editor can. The level is
        /// logged either way, so the answer is in the build log instead of in a guess.
        /// </summary>
        private static AndroidSdkVersions HighestTargetSdk()
        {
            const int playRequires = 36;
            // Exactly what Play asks for when this editor knows it, and not the highest it knows. The highest is a
            // preview: this machine ships android-37.0 beside android-36, Unity tried to install that preview
            // platform, needed Windows elevation to write under Program Files and failed the build -- and Play
            // refuses a production upload that targets a preview API anyway. Asking for more than is required
            // bought nothing and cost everything.
            if (Enum.IsDefined(typeof(AndroidSdkVersions), playRequires))
            {
                Debug.Log("CrushRoyaleSetup: targeting Android API " + playRequires + ", the level Play requires.");
                return (AndroidSdkVersions)playRequires;
            }

            int best = 0;
            foreach (object value in Enum.GetValues(typeof(AndroidSdkVersions)))
            {
                int level = (int)value;
                // AndroidApiLevelAuto is 0 and the enum has no sentinel above the real levels; the guard is there
                // in case a future version adds one.
                if (level > best && level < 1000)
                {
                    best = level;
                }
            }
            Debug.LogWarning("CrushRoyaleSetup: this editor does not know Android API " + playRequires
                + ", which Google Play requires for a new application, and tops out at " + best
                + ". The build will succeed and the upload will be refused. Move this build to a newer Unity.");
            return best > 0 ? (AndroidSdkVersions)best : AndroidSdkVersions.AndroidApiLevelAuto;
        }
}
}
