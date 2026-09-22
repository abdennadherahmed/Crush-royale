using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace CrushRoyale.EditorTools
{
    /// <summary>
    /// Packs the small, numerous UI sprites into one texture per family.
    ///
    /// The board draws six gem colours side by side and the hub draws a dozen different icons at once; every distinct
    /// texture in a row breaks the batch and costs a draw call. One texture per family collapses those switches.
    ///
    /// Two families are deliberately left out:
    /// - Art/Kit, because UiKit slices those frames and buttons from the raw Texture2D. A nine-sliced sprite taken
    ///   from an atlas would slice the whole atlas instead of the button.
    /// - the full-screen pictures (backdrops, boss portraits, characters), shown one at a time: atlasing them costs
    ///   memory and saves no call at all.
    ///
    /// One atlas per leaf folder, never per tree: Art/Gems and Art/Gems/runes hold files of the same names, and a
    /// single atlas over both would contain two sprites called "red".
    ///
    /// The whole thing is guarded. If it fails, no atlas exists and ArtLibrary keeps loading straight from Resources,
    /// which is exactly what the game did before.
    /// </summary>
    public static class SpriteAtlasBuilder
    {
        public const string AtlasFolder = "Assets/Resources/Atlases";

        /// <summary>
        /// Roots under Resources/Art whose sprites are small, numerous and drawn together.
        ///
        /// Pets are deliberately not in this list. Two of the five came out upside down on the device while being
        /// perfectly upright in the editor, in the source file, and in every capture -- because atlases are packed
        /// when the APK is built and never in the editor, so the editor cannot even reproduce the fault. Five
        /// sprites of 384 px, drawn one at a time on a card, were the worst candidates for packing in the first
        /// place: they save almost no draw calls and they were the only place the damage showed.
        /// </summary>
        public static readonly string[] Roots = { "Icons", "Gems", "PowerUps", "Chests", "Frames" };

        [MenuItem("Crush Royale/6. Build Sprite Atlases", priority = 22)]
        public static void BuildAll()
        {
            try
            {
                Build();
            }
            catch (Exception ex)
            {
                // Never fail a build over this: without atlases the game renders the same, only with more draw calls.
                Debug.LogWarning("SpriteAtlasBuilder: atlases were not built; sprites will load directly from Resources.");
                Debug.LogException(ex);
            }
        }

        private static void Build()
        {
            EditorSettings.spritePackerMode = SpritePackerMode.SpriteAtlasV2Build;
            Directory.CreateDirectory(AtlasFolder);

            var built = new List<string>();
            foreach (string root in Roots)
            {
                string start = "Assets/Resources/Art/" + root;
                if (!AssetDatabase.IsValidFolder(start))
                {
                    Debug.LogWarning("SpriteAtlasBuilder: no folder at " + start);
                    continue;
                }
                foreach (string folder in LeafFolders(start))
                {
                    if (BuildOne(folder))
                    {
                        built.Add(AtlasName(folder));
                    }
                }
            }

            AssetDatabase.Refresh();
            Debug.Log("SpriteAtlasBuilder: " + built.Count + " atlas(es): " + string.Join(", ", built));
        }

        /// <summary>The folder itself plus every folder under it, each packed on its own.</summary>
        private static IEnumerable<string> LeafFolders(string start)
        {
            yield return start;
            foreach (string sub in AssetDatabase.GetSubFolders(start))
            {
                foreach (string deeper in LeafFolders(sub))
                {
                    yield return deeper;
                }
            }
        }

        /// <summary>"Assets/Resources/Art/Gems/runes" becomes the atlas "gems.runes".</summary>
        private static string AtlasName(string folder) =>
            folder.Substring("Assets/Resources/Art/".Length).Replace('/', '.').ToLowerInvariant();

        private static bool BuildOne(string folder)
        {
            // Only the files of this folder: a sub-folder gets its own atlas, so nothing is packed twice.
            UnityEngine.Object[] sprites = AssetDatabase.FindAssets("t:Sprite", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(p => string.Equals(Path.GetDirectoryName(p)?.Replace('\\', '/'), folder, StringComparison.Ordinal))
                .Select(AssetDatabase.LoadAssetAtPath<Sprite>)
                .Where(s => s != null)
                .Cast<UnityEngine.Object>()
                .ToArray();
            if (sprites.Length < 2)
            {
                // One sprite alone is already one draw call; an atlas around it would only add a texture.
                return false;
            }

            var atlas = new SpriteAtlasAsset();
            atlas.SetIncludeInBuild(true);
            // Rotation and tight packing both break nine-sliced sprites, and padding keeps neighbours from bleeding
            // into each other once the texture is filtered.
            atlas.SetPackingSettings(new SpriteAtlasPackingSettings
            {
                blockOffset = 1,
                enableAlphaDilation = true,
                enableRotation = false,
                enableTightPacking = false,
                padding = 4
            });
            atlas.SetTextureSettings(new SpriteAtlasTextureSettings
            {
                anisoLevel = 1,
                filterMode = FilterMode.Bilinear,
                generateMipMaps = false,
                readable = false
            });
            atlas.Add(sprites);
            SpriteAtlasAsset.Save(atlas, AtlasFolder + "/" + AtlasName(folder) + ".spriteatlasv2");
            return true;
        }
    }
}
