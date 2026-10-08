using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SlimaPuzzle.Rendering.EditorTools
{
    /// <summary>
    /// 一键把「全屏像素化 + 描边」的 Renderer Feature 装到项目里所有 URP Renderer 上。
    ///
    /// 为什么需要这个：URP 的 ScriptableRendererFeature **不会自动生效**，
    /// 必须挂到一个 Renderer 资产（URP Asset → Renderer List 里那个）上。
    /// 十有八九"看不到描边"就是漏了这一步。手动挂也行：
    ///   选中 Renderer 资产 → Inspector 底部 Add Renderer Feature → Slima Toon Outline。
    /// </summary>
    public static class SlimaToonOutlineInstaller
    {
        [MenuItem("Tools/Slima/地图编辑器/给 URP Renderer 装上全屏描边 Feature", false, 430)]
        public static void Install()
        {
            string[] guids = AssetDatabase.FindAssets("t:UniversalRendererData");
            if (guids == null || guids.Length == 0)
            {
                EditorUtility.DisplayDialog("Slima",
                    "项目里没找到任何 URP Renderer 资产。\n\n（如果确实用的是 URP，Renderer 资产一般在 Assets/Settings 下。）", "好");
                return;
            }

            var lines = new List<string>();
            int installed = 0;
            int already = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                if (data == null) continue;

                var serialized = new SerializedObject(data);
                SerializedProperty features = serialized.FindProperty("m_RendererFeatures");
                if (features == null) continue;

                bool has = false;
                for (int i = 0; i < features.arraySize; i++)
                {
                    if (features.GetArrayElementAtIndex(i).objectReferenceValue is SlimaToonOutlineFeature)
                    {
                        has = true;
                        break;
                    }
                }

                if (has)
                {
                    already++;
                    lines.Add($"· {Path.GetFileName(path)}：已经装过了");
                    continue;
                }

                var feature = ScriptableObject.CreateInstance<SlimaToonOutlineFeature>();
                feature.name = "Slima Toon Outline";
                AssetDatabase.AddObjectToAsset(feature, data);

                features.arraySize++;
                features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                EditorUtility.SetDirty(data);
                installed++;
                lines.Add($"✔ {Path.GetFileName(path)}：已装上 Slima Toon Outline");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // 关键提示：Feature 挂在哪个 Renderer 上，取决于当前生效的管线 —— 编辑错的那个会"完全没反应"
            RenderPipelineAsset activePipeline = GraphicsSettings.currentRenderPipeline;
            string activeName = activePipeline != null ? activePipeline.name : "（无，用的是默认管线）";

            string message = $"★ 当前生效的管线：{activeName}\n" +
                             "   你要编辑的是它对应的那个 Renderer 资产！\n" +
                             "   （在别的 Renderer 上调参数是完全没反应的 ✓）\n\n" +
                             $"装了 {installed} 个 Renderer，跳过 {already} 个（已装过）\n\n" +
                             string.Join("\n", lines) +
                             "\n\n接下来：进 Play。Console 里应该出现\n" +
                             "[Slima] Toon Outline 已生效：PixelSize=… Outline=… DepthThreshold=…\n\n" +
                             "改参数时还会打一条「全屏描边参数已更新」——\n" +
                             "拖滑块却没有这条日志 = 你编辑的不是当前生效的那个 Renderer。";

            Debug.Log("[Slima] 全屏描边安装结果：" + string.Join(" | ", lines));
            EditorUtility.DisplayDialog("Slima 全屏描边", message, "好");
        }
    }
}
