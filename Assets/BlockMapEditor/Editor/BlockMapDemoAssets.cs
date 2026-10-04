#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

namespace BlockMap
{
    /// <summary>
    /// 一键生成"示例门"Prefab（自带开关与旋转脚本）。
    ///
    /// 为什么改成用 Unity 基本体搭、而不是导入 .obj：
    ///   .obj / .fbx 进 Unity 会经过"坐标轴 + 单位"换算（OBJ 默认按 Z-up 处理），
    ///   自己写的 Y-up 模型很容易被转歪 —— 表现为门竖成一根柱子或躺平。
    ///   用 Cube 基本体搭则 **完全确定性**：尺寸、朝向、原点都精确可控。
    ///
    /// 生成物：Assets/BlockMapEditor/Resources/BlockModels/Door.prefab
    ///   · 1m × 1m × 0.12m 门扇 + 把手，材质是 Unlit 木色（与地图方块一致，不受光照影响）
    ///   · 整扇门一个 BoxCollider
    ///   · BlockMapDoor（按 E 开关）+ BlockMapRotateOnClick（按 R 转 90°）
    ///   · 顺手清掉 Resources 里的裸模型副本，保证 id「Door」唯一
    /// </summary>
    public static class BlockMapDemoAssets
    {
        const string OutDir = "Assets/BlockMapEditor/Resources/BlockModels";

        [MenuItem("Tools/BlockMap/③ 生成示例门 Prefab（Door）")]
        public static void CreateDoorPrefab()
        {
            Directory.CreateDirectory(OutDir);

            var root = new GameObject("Door");       // 外层：原点 = 门中心，负责「朝向」（按 R 转 90°）
            var hinge = new GameObject("Hinge");     // 内层：门轴，负责「开关」（按 E 摆动）
            hinge.transform.SetParent(root.transform, false);

            // ── 门扇：1m 宽 × 1m 高 × 0.12m 厚 ──
            var leaf = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leaf.name = "Leaf";
            leaf.transform.SetParent(hinge.transform, false);
            leaf.transform.localScale = new Vector3(1f, 1f, 0.12f);
            Object.DestroyImmediate(leaf.GetComponent<Collider>());
            leaf.GetComponent<MeshRenderer>().sharedMaterial = UnlitMat("DoorWood", new Color(0.45f, 0.27f, 0.13f));

            // ── 门把手 ──
            var knob = GameObject.CreatePrimitive(PrimitiveType.Cube);
            knob.name = "Knob";
            knob.transform.SetParent(hinge.transform, false);
            knob.transform.localScale = new Vector3(0.08f, 0.08f, 0.08f);
            knob.transform.localPosition = new Vector3(0.36f, 0f, 0.10f);
            Object.DestroyImmediate(knob.GetComponent<Collider>());
            knob.GetComponent<MeshRenderer>().sharedMaterial = UnlitMat("DoorKnob", new Color(0.72f, 0.70f, 0.42f));

            // ── 交互用碰撞体（整扇门一个盒子）──
            var box = root.AddComponent<BoxCollider>();
            box.center = Vector3.zero;
            box.size = new Vector3(1f, 1f, 0.2f);

            // ── 双层结构：外层管朝向、内层管开关，两者互不覆盖 ──
            root.AddComponent<BlockMapRotateOnClick>();    // 按 R：整体转 90°（改朝向）
            hinge.AddComponent<BlockMapDoor>();            // 按 E：绕门轴摆动（开/关）

            string outPath = OutDir + "/Door.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, outPath);
            Object.DestroyImmediate(root);

            // 保证 id 唯一：清掉之前的裸模型副本（DemoModels 里的源文件保留）
            AssetDatabase.DeleteAsset(OutDir + "/Door.obj");
            AssetDatabase.DeleteAsset(OutDir + "/Door.mtl");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[BlockMap] 示例门 Prefab 已生成：" + outPath);
            EditorUtility.DisplayDialog("BlockMap",
                "已生成：" + outPath + "\n\n" +
                "用法：Play → 方块库选 [模型]Door → 刷到地图上\n" +
                "  · 按 R 原地转 90°（朝向，外层）\n" +
                "  · 走近按 E 开关门（摆动，内层 Hinge）\n\n" +
                "双层结构：外层管朝向、内层管开关，互不覆盖。\n" +
                "门扇是 Unlit 木色，和地图方块一样不受场景光照影响（不会发黑）。", "好");
        }

        /// <summary>生成/复用一个 Unlit 材质资产（运行时 new 出来的材质不能直接塞进 Prefab，会丢）</summary>
        static Material UnlitMat(string name, Color c)
        {
            string p = OutDir + "/" + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(p);
            if (m != null)
            {
                if (m.HasProperty("_Color")) m.SetColor("_Color", c);
                m.color = c;
                EditorUtility.SetDirty(m);
                return m;
            }

            var sh = Shader.Find("Unlit/Color");
            if (sh == null) sh = Shader.Find("BlockMap/BlockUnlit");
            m = new Material(sh) { name = name };
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            m.color = c;
            AssetDatabase.CreateAsset(m, p);
            return m;
        }
    }
}
#endif
