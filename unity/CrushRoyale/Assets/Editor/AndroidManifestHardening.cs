using System.IO;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

namespace CrushRoyale.EditorTools
{
    /// <summary>
    /// Security audit S-09: local saves must never be restorable from a cloud/adb backup, so Android backup is turned off
    /// explicitly in the generated Gradle project (launcher and library manifests).
    /// </summary>
    public sealed class AndroidManifestHardening : IPostGenerateGradleAndroidProject
    {
        private const string AndroidNs = "http://schemas.android.com/apk/res/android";
        private const string ToolsNs = "http://schemas.android.com/tools";

        public int callbackOrder => 10;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            // "path" is the unityLibrary module; the launcher module sits next to it.
            Harden(Path.Combine(path, "src", "main", "AndroidManifest.xml"));
            Harden(Path.Combine(path, "..", "launcher", "src", "main", "AndroidManifest.xml"));
        }

        private static void Harden(string manifestPath)
        {
            if (!File.Exists(manifestPath))
            {
                return;
            }
            var doc = new XmlDocument();
            doc.Load(manifestPath);
            XmlElement manifest = doc.DocumentElement;
            if (manifest == null)
            {
                return;
            }
            if (!manifest.HasAttribute("xmlns:tools"))
            {
                manifest.SetAttribute("xmlns:tools", ToolsNs);
            }
            XmlElement application = manifest.SelectSingleNode("application") as XmlElement;
            if (application == null)
            {
                application = doc.CreateElement("application");
                manifest.AppendChild(application);
            }
            application.SetAttribute("allowBackup", AndroidNs, "false");
            application.SetAttribute("fullBackupContent", AndroidNs, "false");
            string replace = application.GetAttribute("replace", ToolsNs);
            if (!replace.Contains("android:allowBackup"))
            {
                replace = string.IsNullOrEmpty(replace) ? "android:allowBackup,android:fullBackupContent" : replace + ",android:allowBackup,android:fullBackupContent";
                application.SetAttribute("replace", ToolsNs, replace);
            }
            doc.Save(manifestPath);
            Debug.Log("Android backup disabled in " + manifestPath);
        }
    }
}
