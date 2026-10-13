using UnityEditor;
using UnityEngine;

namespace SlimaPuzzle.EditorTools
{
    // Keep the generated UI's hard pixel edges in the editor and in builds.
    public sealed class SlimaUiTextureImport : AssetPostprocessor
    {
        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/_Game/UI/Resources/SlimaUI/")) return;
            var importer = (TextureImporter)assetImporter;
            bool illustration = assetPath.EndsWith("/cover-beaker.png") || assetPath.EndsWith("/cover-slime.png");
            importer.textureType = illustration ? TextureImporterType.Sprite : TextureImporterType.Default;
            if (illustration) importer.spriteImportMode = SpriteImportMode.Single;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.maxTextureSize = 4096;
            importer.alphaIsTransparency = true;
        }
    }
}
