#if UNITY_ANDROID
using System;
using System.IO;
using System.Text;
using UnityEditor.Android;
using UnityEngine;

namespace CrushRoyale.EditorTools
{
    /// <summary>
    /// Adds the Maven dependencies the two Java bridges need, by editing the Gradle project Unity has just
    /// generated.
    ///
    /// The obvious way to do this is a `mainTemplate.gradle` in Plugins/Android, and it cost a build to learn why
    /// that is a trap. A template is a copy of Unity's own file for one Unity version: the one written here by
    /// 6000.6 applied `../shared/common.gradle`, which 6000.0 does not generate, so the build machine, which runs
    /// 6000.0, failed to read a script that did not exist and the launcher module lost its `compileSdk` with it.
    /// A template also freezes whatever else Unity puts in that file, so it rots quietly at every upgrade.
    ///
    /// Appending to the generated file instead means Unity writes its own build.gradle, for its own version, and
    /// only the dependency lines are ours.
    ///
    /// What is declared here, and why:
    /// - androidx.credentials and the Google identity library, for GoogleSignInBridge.java, which signs the
    ///   player in through the Credential Manager rather than the retired Google Sign-In SDK.
    /// - the LevelPlay mediation SDK. The Unity package ships the C# API and a Java bridge and nothing native;
    ///   the classes that bridge calls live in this artifact. The version is the one the 9.3.2 package validates
    ///   against. The UnityAds adapter and SDK come with it so the auction has a network to get fill from.
    /// </summary>
    public sealed class AndroidDependencies : IPostGenerateGradleAndroidProject
    {
        /// <summary>Before AdIdPermission, so a failure here surfaces first; order among the two does not matter.</summary>
        public int callbackOrder => 50;

        private static readonly string[] Dependencies =
        {
            "androidx.credentials:credentials:1.3.0",
            "androidx.credentials:credentials-play-services-auth:1.3.0",
            "com.google.android.libraries.identity.googleid:googleid:1.1.1",
            "com.unity3d.ads-mediation:mediation-sdk:9.3.0",
            "com.unity3d.ads-mediation:unityads-adapter:5.4.0",
            "com.unity3d.ads:unity-ads:4.16.5"
        };

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string file = Path.Combine(path, "build.gradle");
            if (!File.Exists(file))
            {
                Debug.LogError("AndroidDependencies: no build.gradle at " + file);
                return;
            }
            string text = File.ReadAllText(file);
            var added = new StringBuilder();
            foreach (string dependency in Dependencies)
            {
                // The coordinate without its version: a dependency Unity already declared is left alone rather
                // than declared twice at two versions, which Gradle resolves in nobody's favour.
                string artifact = dependency.Substring(0, dependency.LastIndexOf(':'));
                if (text.Contains(artifact, StringComparison.Ordinal))
                {
                    continue;
                }
                added.Append("    implementation '").Append(dependency).Append("'\n");
            }
            if (added.Length == 0)
            {
                return;
            }

            int start = text.IndexOf("dependencies {", StringComparison.Ordinal);
            if (start < 0)
            {
                Debug.LogError("AndroidDependencies: no dependencies block in " + file);
                return;
            }
            int insert = text.IndexOf('\n', start) + 1;
            File.WriteAllText(file, text.Insert(insert, added.ToString()));
            Debug.Log("AndroidDependencies: added " + added.ToString().Split('\n').Length + " dependencies to " + file);
        }
    }
}
#endif
