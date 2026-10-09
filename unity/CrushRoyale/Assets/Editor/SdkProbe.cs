using System;
using UnityEditor;
using UnityEngine;

namespace CrushRoyale.EditorTools
{
    /// <summary>
    /// Prints which Android API levels this editor knows about.
    ///
    /// Google Play has required API 36 for a new application since 31 August 2026, and the project asks for
    /// "Auto", which means whatever the machine that compiles happens to have. That is not a decision, it is a
    /// consequence, and it decides whether an upload is accepted. Run this in an editor to find out what that
    /// editor would actually produce:
    ///
    ///   Unity.exe -quit -batchmode -projectPath unity/CrushRoyale -executeMethod CrushRoyale.EditorTools.SdkProbe.Report
    /// </summary>
    public static class SdkProbe
    {
        public static void Report()
        {
            Debug.Log("SdkProbe: editor " + Application.unityVersion);
            int highest = 0;
            foreach (object value in Enum.GetValues(typeof(AndroidSdkVersions)))
            {
                int level = (int)value;
                if (level > highest && level < 1000)
                {
                    highest = level;
                }
            }
            Debug.Log("SdkProbe: highest Android API level this editor can target = " + highest);
            Debug.Log("SdkProbe: API 36 supported = " + (highest >= 36));
            Debug.Log("SdkProbe: project target = " + PlayerSettings.Android.targetSdkVersion
                + ", minimum = " + PlayerSettings.Android.minSdkVersion);
        }
    }
}
