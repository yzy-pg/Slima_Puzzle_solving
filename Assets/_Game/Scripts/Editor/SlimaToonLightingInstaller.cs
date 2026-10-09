using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace SlimaPuzzle.Rendering.EditorTools
{
    public static class SlimaToonLightingInstaller
    {
        [MenuItem("Tools/Slima/光照/安装光源模式到全部 URP Renderer")]
        public static void Install()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:UniversalRendererData"))
            {
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data.rendererFeatures.Exists(f => f is SlimaToonLightingFeature)) continue;
                var feature = ScriptableObject.CreateInstance<SlimaToonLightingFeature>();
                feature.name = "Slima Toon Lighting";
                AssetDatabase.AddObjectToAsset(feature, data);
                data.rendererFeatures.Add(feature);
                data.SetDirty();
                EditorUtility.SetDirty(data);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Slima] 光源模式已安装到全部 URP Renderer。Light 添加 Slima Toon Light 即可切换。");
        }

        [MenuItem("Tools/Slima/光照/为选中 Light 添加光源模式")]
        public static void AddToSelectedLights()
        {
            foreach (var go in Selection.gameObjects)
            {
                if (go.TryGetComponent<Light>(out _) && !go.TryGetComponent<SlimaToonLight>(out _))
                    Undo.AddComponent<SlimaToonLight>(go);
            }
        }
    }
}
