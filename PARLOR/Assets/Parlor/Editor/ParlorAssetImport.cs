using UnityEditor;
using UnityEngine;

namespace Parlor.EditorTools
{
    /// <summary>
    /// Import settings for everything under Assets/Parlor/Resources, applied automatically: ASTC textures with
    /// mipmaps at Quest-appropriate sizes, compressed mono audio, music streamed.
    /// </summary>
    public sealed class ParlorAssetImport : AssetPostprocessor
    {
        bool Ours => assetPath.StartsWith("Assets/Parlor/Resources/");

        void OnPreprocessTexture()
        {
            if (!Ours) return;
            var t = (TextureImporter)assetImporter;
            string name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            bool sprite = name == "audience" || name == "coin";
            t.textureType = TextureImporterType.Default;
            t.sRGBTexture = true;
            t.mipmapEnabled = true;
            t.anisoLevel = 4;
            t.filterMode = FilterMode.Trilinear;
            t.alphaIsTransparency = sprite;
            t.wrapMode = (sprite || name == "stagefloor" || name == "cards" || name == "cardback" || name == "poster") ? TextureWrapMode.Clamp : TextureWrapMode.Repeat;
            t.maxTextureSize = name == "wallpaper" || name == "coin" ? 512 : 1024;
            t.npotScale = TextureImporterNPOTScale.ToNearest;
            var android = t.GetPlatformTextureSettings("Android");
            android.overridden = true;
            android.maxTextureSize = t.maxTextureSize;
            android.format = TextureImporterFormat.ASTC_6x6;
            t.SetPlatformTextureSettings(android);
        }

        void OnPreprocessAudio()
        {
            if (!Ours) return;
            var a = (AudioImporter)assetImporter;
            bool music = assetPath.Contains("music_loop");
            a.forceToMono = true;
            a.loadInBackground = true;
            var s = a.defaultSampleSettings;
            s.compressionFormat = AudioCompressionFormat.Vorbis;
            s.quality = music ? 0.6f : 0.7f;
            s.loadType = music ? AudioClipLoadType.Streaming : AudioClipLoadType.CompressedInMemory;
            s.preloadAudioData = !music;
            a.defaultSampleSettings = s;
        }
    }
}
