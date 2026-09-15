using UnityEditor;

namespace CrushRoyale.EditorTools
{
    /// <summary>Imports everything under Assets/Resources/Art as undistorted UI sprites (no power-of-two rescale, no mipmaps).</summary>
    public sealed class ArtImportSettings : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.Replace('\\', '/').Contains("/Resources/Art/"))
            {
                return;
            }

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Compressed;
        }

        /// <summary>Resources/Audio: music streams from disk (low memory), short effects are decompressed for instant playback.</summary>
        private void OnPreprocessAudio()
        {
            string path = assetPath.Replace('\\', '/');
            if (!path.Contains("/Resources/Audio/"))
            {
                return;
            }

            var importer = (AudioImporter)assetImporter;
            bool music = System.IO.Path.GetFileName(path).StartsWith("music_");
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            settings.loadType = music ? AudioClipLoadType.Streaming : AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = music ? 0.5f : 0.7f;
            importer.defaultSampleSettings = settings;
            importer.forceToMono = !music;
        }
    }
}
