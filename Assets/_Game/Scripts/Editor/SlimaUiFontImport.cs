using UnityEditor;
using UnityEngine;

namespace SlimaPuzzle.EditorTools
{
    public sealed class SlimaUiFontImport : AssetPostprocessor
    {
        void OnPreprocessAsset()
        {
            if (!assetPath.StartsWith("Assets/_Game/UI/Resources/SlimaUI/Fonts/") ||
                !(assetImporter is TrueTypeFontImporter importer)) return;
            importer.fontRenderingMode = FontRenderingMode.HintedRaster;
            importer.includeFontData = true;
            importer.fontSize = 24;
        }
    }
}
