using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CrushRoyale.EditorTools
{
    /// <summary>
    /// Headless build entry point for GitHub Actions (game-ci/unity-builder buildMethod):
    /// applies the Android settings, creates the boot scene when missing and builds an APK.
    /// </summary>
    public static class CIBuild
    {
        private const string BootScenePath = "Assets/Scenes/Boot.unity";

        public static void BuildAndroid()
        {
            string output = GetArgument("-customBuildPath") ?? "build/Android/CrushRoyale.apk";
            if (!output.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
            {
                output = Path.Combine(output, "CrushRoyale.apk");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));

            CrushRoyaleSetup.ConfigureAndroid();
            EditorUserBuildSettings.buildAppBundle = false;
            if (!File.Exists(BootScenePath))
            {
                CrushRoyaleSetup.CreateBootScene();
            }

            ApplyReleaseSigning();

            string versionCode = GetArgument("-androidVersionCode");
            if (int.TryParse(versionCode, out int code) && code > 0)
            {
                PlayerSettings.Android.bundleVersionCode = code;
            }

            BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { BootScenePath },
                locationPathName = output,
                target = BuildTarget.Android,
                options = BuildOptions.None
            });

            BuildSummary summary = report.summary;
            Debug.Log("CIBuild: " + summary.result + ", " + summary.totalErrors + " error(s), size " + summary.totalSize + " bytes, output " + output);
            if (summary.result != BuildResult.Succeeded)
            {
                EditorApplication.Exit(1);
            }
        }

        /// <summary>
        /// Release signing from the environment (set by the workflow from GitHub secrets, see tools/create-keystore.py).
        /// Without a keystore the APK stays debug-signed: installable for testing, not uploadable to Google Play.
        /// </summary>
        private static void ApplyReleaseSigning()
        {
            string path = Environment.GetEnvironmentVariable("ANDROID_KEYSTORE_PATH");
            string password = Environment.GetEnvironmentVariable("ANDROID_KEYSTORE_PASS");
            if (string.IsNullOrEmpty(path) || !File.Exists(path) || string.IsNullOrEmpty(password))
            {
                PlayerSettings.Android.useCustomKeystore = false;
                Debug.Log("CIBuild: no release keystore, debug signing.");
                return;
            }

            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = path;
            PlayerSettings.Android.keystorePass = password;
            PlayerSettings.Android.keyaliasName = Environment.GetEnvironmentVariable("ANDROID_KEY_ALIAS") ?? "crushroyale";
            PlayerSettings.Android.keyaliasPass = password;
            Debug.Log("CIBuild: release signing with the keystore from secrets.");
        }

        private static string GetArgument(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == name && !string.IsNullOrWhiteSpace(args[i + 1]))
                {
                    return args[i + 1];
                }
            }
            return null;
        }
    }
}
