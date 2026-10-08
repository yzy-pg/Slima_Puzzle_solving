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
        // ==================================================================
        // 把内置分组 + Mask Shader 真正写进 Renderer 资产（Inspector 里能看见）
        // ==================================================================
        static readonly string[] BuiltinGroupNames = { "地面", "墙面", "起点", "出口", "主角" };
        static readonly string[] BuiltinMaterialFilters = { "_Floor", "_Wall", "", "", "" };
        static readonly string[] BuiltinNameFilters = { "", "", "Spawn", "Exit", "Player|Slime" };
        static readonly float[] BuiltinThickness = { 2f, 2f, 1f, 1f, 2f };

        [MenuItem("Tools/Slima/地图编辑器/把描边分组写进 Renderer 资产（Inspector 可见）", false, 431)]
        public static void FillBuiltinGroups()
        {
            string[] guids = AssetDatabase.FindAssets("t:UniversalRendererData");
            var lines = new List<string>();
            int touched = 0;

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                if (data == null) continue;

                var serialized = new SerializedObject(data);
                SerializedProperty features = serialized.FindProperty("m_RendererFeatures");
                if (features == null) continue;

                bool changedThisAsset = false;

                for (int i = 0; i < features.arraySize; i++)
                {
                    var feature = features.GetArrayElementAtIndex(i).objectReferenceValue as SlimaToonOutlineFeature;
                    if (feature == null) continue;

                    var featureSo = new SerializedObject(feature);
                    SerializedProperty shaderProp = featureSo.FindProperty("settings.maskShader");
                    SerializedProperty sourceProp = featureSo.FindProperty("settings.edgeSource");
                    SerializedProperty groupsProp = featureSo.FindProperty("settings.maskGroups");

                    bool changed = false;

                    // 1) 补 Mask Shader
                    if (shaderProp != null && shaderProp.objectReferenceValue == null)
                    {
                        shaderProp.objectReferenceValue = Shader.Find("Slima/Outline Mask");
                        changed = true;
                        lines.Add($"{Path.GetFileName(path)}：Mask Shader 已补上");
                    }

                    // 2) 写入 5 个内置分组：**按名字覆盖字段**（修正写错的），缺的补上
                    if (groupsProp != null)
                    {
                        for (int g = 0; g < BuiltinGroupNames.Length; g++)
                        {
                            int found = -1;
                            for (int k = 0; k < groupsProp.arraySize; k++)
                            {
                                SerializedProperty nameProp = groupsProp.GetArrayElementAtIndex(k)
                                    .FindPropertyRelative("name");
                                if (nameProp != null && nameProp.stringValue == BuiltinGroupNames[g]) { found = k; break; }
                            }

                            if (found < 0)
                            {
                                groupsProp.arraySize++;
                                found = groupsProp.arraySize - 1;
                                lines.Add($"{Path.GetFileName(path)}：新增「{BuiltinGroupNames[g]}」组");
                            }

                            SerializedProperty item = groupsProp.GetArrayElementAtIndex(found);
                            item.FindPropertyRelative("name").stringValue = BuiltinGroupNames[g];
                            item.FindPropertyRelative("materialContains").stringValue = BuiltinMaterialFilters[g];
                            item.FindPropertyRelative("nameContains").stringValue = BuiltinNameFilters[g];
                            item.FindPropertyRelative("layerMask").intValue = 0;
                            item.FindPropertyRelative("thickness").floatValue = BuiltinThickness[g];
                            item.FindPropertyRelative("enabled").boolValue = true;
                            changed = true;
                        }
                    }

                    if (changed)
                    {
                        featureSo.ApplyModifiedPropertiesWithoutUndo();
                        EditorUtility.SetDirty(feature);
                        changedThisAsset = true;
                    }
                }

                if (changedThisAsset)
                {
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(data);
                    touched++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string message = touched == 0
                ? "所有 Renderer 里的分组都齐了（或没装 Feature），没有需要补的。"
                : $"已更新 {touched} 个 Renderer（内置分组按名字重置为正确字段）：\n\n" + string.Join("\n", lines);

            Debug.Log("[Slima] 补齐描边分组：" + message.Replace("\n", " | "));
            EditorUtility.DisplayDialog("Slima 描边分组", message +
                "\n\n现在去 Renderer 资产的 Render Features → Slima Toon Outline 就能看到这几组了。", "好");
        }

        [MenuItem("Tools/Slima/地图编辑器/给 URP Renderer 装上全屏描边 Feature", false, 430)]
        public static void Install()
        {            string[] guids = AssetDatabase.FindAssets("t:UniversalRendererData");
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

            // ★ 描边要读深度：URP Asset 上的「Depth Texture」必须打开，
            //   否则 _CameraDepthTexture 恒为 0（远平面）→ 落差恒为 0 → 永远不出线。
            //   pass 里的 ConfigureInput(Depth) 在这个工程里没兜住，所以这里直接把开关打开。
            int depthEnabled = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
                if (pipeline == null) continue;

                var serializedPipeline = new SerializedObject(pipeline);
                SerializedProperty requireDepth = serializedPipeline.FindProperty("m_RequireDepthTexture");
                if (requireDepth == null || requireDepth.boolValue) continue;

                requireDepth.boolValue = true;
                serializedPipeline.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(pipeline);
                depthEnabled++;
                lines.Add($"✔ {Path.GetFileName(path)}：已开启 Depth Texture（描边必须）");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            // 关键提示：Feature 挂在哪个 Renderer 上，取决于当前生效的管线 —— 编辑错的那个会"完全没反应"
            RenderPipelineAsset activePipeline = GraphicsSettings.currentRenderPipeline;
            string activeName = activePipeline != null ? activePipeline.name : "（无，用的是默认管线）";

            string message = $"★ 当前生效的管线：{activeName}\n" +
                             "   你要编辑的是它对应的那个 Renderer 资产！\n" +
                             "   （在别的 Renderer 上调参数是完全没反应的 ✗）\n\n" +
                             $"装了 {installed} 个 Renderer，跳过 {already} 个（已装过）\n" +
                             $"开了 {depthEnabled} 个 URP Asset 的 Depth Texture\n\n" +
                             string.Join("\n", lines) +
                             "\n\n接下来：进 Play。Console 里应该出现\n" +
                             "[Slima] Toon Outline 已生效：PixelSize=… Outline=… DepthThreshold=…\n\n" +
                             "改参数时还会打一条「全屏描边参数已更新」——\n" +
                             "拖滑块却没有这条日志 = 你编辑的不是当前生效的那个 Renderer。";

            Debug.Log("[Slima] 全屏描边安装结果：" + string.Join(" | ", lines));
            EditorUtility.DisplayDialog("Slima 全屏描边", message, "好");

            // 顺手把内置分组 + Mask Shader 写进资产（Inspector 里就能看见并调）
            FillBuiltinGroups();
        }
    }
}
