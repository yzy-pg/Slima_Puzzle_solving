using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SlimaPuzzle.Rendering.EditorTools
{
    /// <summary>
    /// 一次性调好"所有方块材质"的 Toon 参数。
    ///
    /// 用法：在 Project 里先把**一个**材质调满意（比如 Lab_Wall_01）→ 选中它 →
    ///       菜单 Tools/Slima/地图编辑器/把选中材质的 Toon 参数同步到全部同类材质
    ///
    /// 注意：**只同步颜色/数值/开关，不动贴图** —— 每个方块材质的图集是各自不同的，
    ///       一起复制会把图集互相覆盖。
    /// </summary>
    public static class SlimaToonMaterialSync
    {
        [MenuItem("Tools/Slima/地图编辑器/把选中材质的 Toon 参数同步到全部同类材质", false, 440)]
        public static void SyncFromSelection()
        {
            var source = Selection.activeObject as Material;
            if (source == null)
            {
                EditorUtility.DisplayDialog("Slima",
                    "请先在 Project 面板里选中一个材质（作为「参考」），再点这个菜单。\n\n" +
                    "建议选一个方块材质（Assets/BlockMapEditor/Built/Materials 下），把参数调满意后再同步。", "好");
                return;
            }

            Shader shader = source.shader;
            string sourcePath = AssetDatabase.GetAssetPath(source);
            int count = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:Material"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path == sourcePath) continue;

                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null || material.shader != shader) continue;

                Undo.RecordObject(material, "Sync Slima toon material");
                CopyScalarProperties(source, material);
                EditorUtility.SetDirty(material);
                count++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[Slima] 已把「{source.name}」的 Toon 参数同步到 {count} 个使用同一 shader 的材质（贴图未改动）");
            EditorUtility.DisplayDialog("Slima",
                $"已同步 → {count} 个材质（shader = {shader.name}）。\n\n" +
                "只同步了颜色 / 数值 / 开关，每个材质自己的贴图保持不变。", "好");
        }

        /// <summary>只复制颜色与数值属性，跳过 Texture（避免图集互相覆盖）。</summary>
        static void CopyScalarProperties(Material source, Material target)
        {
            Shader shader = source.shader;
            int count = shader.GetPropertyCount();

            for (int i = 0; i < count; i++)
            {
                ShaderPropertyType type = shader.GetPropertyType(i);
                if (type == ShaderPropertyType.Texture) continue;

                string name = shader.GetPropertyName(i);
                switch (type)
                {
                    case ShaderPropertyType.Color:
                        target.SetColor(name, source.GetColor(name));
                        break;
                    case ShaderPropertyType.Float:
                    case ShaderPropertyType.Range:
                        target.SetFloat(name, source.GetFloat(name));
                        break;
                }
            }
        }
    }
}
