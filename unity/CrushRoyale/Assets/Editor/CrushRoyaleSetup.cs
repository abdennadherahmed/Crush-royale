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
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel24;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
            PlayerSettings.Android.forceInternetPermission = true;
            PlayerSettings.runInBackground = false;
            EditorUserBuildSettings.buildAppBundle = true;
            Debug.Log("Crush Royale: Android settings applied (IL2CPP, ARM64, API 24+, portrait, AAB). " +
                      "Remember: Player > Other Settings > Active Input Handling = 'Input Manager (Old)' or 'Both', and set up keystore signing.");
        }

        [MenuItem("Crush Royale/2. Create Boot Scene", priority = 2)]
        public static void CreateBootScene()
        {
            Directory.CreateDirectory("Assets/Scenes");
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var cameraObject = new GameObject("Main Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
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
    }
}
