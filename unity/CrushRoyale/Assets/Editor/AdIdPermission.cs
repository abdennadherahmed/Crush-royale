#if UNITY_ANDROID
using System.IO;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

namespace CrushRoyale.EditorTools
{
    /// <summary>
    /// Declares the advertising-id permission in the Android manifest.
    ///
    /// Mediation needs it: without <c>com.google.android.gms.permission.AD_ID</c> on Android 13 and above the ad
    /// networks get no advertising identifier, fill drops and the reporting goes quiet. The LevelPlay package can
    /// add it too, but only from a settings asset created by clicking through its editor window, and this build
    /// runs headless on a machine where nobody clicks anything. So it is written here, where it is visible in the
    /// repository and cannot be lost by reimporting a package.
    ///
    /// The declaration has to match what the Play Console says: the store listing declares that the app collects
    /// an advertising id, and the manifest is the half of that statement a reviewer can actually check.
    /// </summary>
    public sealed class AdIdPermission : IPostGenerateGradleAndroidProject
    {
        private const string Namespace = "http://schemas.android.com/apk/res/android";
        private const string Permission = "com.google.android.gms.permission.AD_ID";

        public int callbackOrder => 100;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string file = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(file))
            {
                Debug.LogWarning("AdIdPermission: no manifest at " + file);
                return;
            }
            var doc = new XmlDocument();
            doc.Load(file);
            XmlNode manifest = doc.SelectSingleNode("manifest");
            if (manifest == null)
            {
                return;
            }
            foreach (XmlNode node in manifest.ChildNodes)
            {
                if (node is XmlElement element && element.Name == "uses-permission" &&
                    element.GetAttribute("name", Namespace) == Permission)
                {
                    return;
                }
            }
            XmlElement added = doc.CreateElement("uses-permission");
            added.SetAttribute("name", Namespace, Permission);
            manifest.AppendChild(added);
            doc.Save(file);
            Debug.Log("AdIdPermission: AD_ID declared in the manifest.");
        }
    }
}
#endif
