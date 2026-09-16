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
            // The output extension picks the format: .aab (Google Play upload) or .apk (direct install).
            string output = GetArgument("-customBuildPath") ?? "build/Android/CrushRoyale.apk";
            bool appBundle = output.EndsWith(".aab", StringComparison.OrdinalIgnoreCase);
            if (!appBundle && !output.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
            {
                output = Path.Combine(output, "CrushRoyale.apk");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));

            CrushRoyaleSetup.ConfigureAndroid();
            EditorUserBuildSettings.buildAppBundle = appBundle;
            if (!File.Exists(BootScenePath))
            {
                CrushRoyaleSetup.CreateBootScene();
            }

            ApplyReleaseSigning();
            ApplyBuildSpeed(!appBundle && GetArgument("-fastTestBuild") == "true");

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
        /// Test APKs (installed by hand) only target 64-bit ARM phones and use the faster IL2CPP code generation,
        /// which roughly halves native compilation. Play Store bundles keep 32 + 64 bit and the fastest runtime code.
        /// </summary>
        private static void ApplyBuildSpeed(bool fastTest)
        {
            var android = UnityEditor.Build.NamedBuildTarget.Android;
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            if (fastTest)
            {
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
                PlayerSettings.SetIl2CppCodeGeneration(android, UnityEditor.Build.Il2CppCodeGeneration.OptimizeSize);
                EditorUserBuildSettings.androidCreateSymbols = AndroidCreateSymbols.Disabled;
                Debug.Log("CIBuild: fast test APK (ARM64 only, IL2CPP faster builds).");
            }
            else
            {
                PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARMv7 | AndroidArchitecture.ARM64;
                PlayerSettings.SetIl2CppCodeGeneration(android, UnityEditor.Build.Il2CppCodeGeneration.OptimizeSpeed);
                Debug.Log("CIBuild: release build (ARMv7 + ARM64, IL2CPP faster runtime).");
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
