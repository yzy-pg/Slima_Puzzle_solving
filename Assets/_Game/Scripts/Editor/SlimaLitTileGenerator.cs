using System.IO;
using UnityEditor;
using UnityEngine;

namespace BlockMap.EditorTools
{
    /// <summary>
    /// 一键给所有方块生成「提亮版（_Lit）」——把灯光亮斑**烘焙到贴图上**，不依赖实时光。
    ///
    /// 用法：Tools/Slima/地图编辑器/给所有方块生成提亮版（灯光亮斑，_Lit）
    /// 结果：`Tools/BlockMapEditor/Resources/&lt;id&gt;_Lit/` 下出现同名贴图（提亮 1.34× + 微暖色），
    ///       地图编辑器里会多出一批 `&lt;id&gt;_Lit` 方块 → 灯周围刷它们就是"被照亮"的地面/墙。
    ///
    /// 为什么用烘焙而不是实时光：灯多了会撞 URP 的「每物体额外光上限」、噪声互相叠加、还很贵；
    /// 烘焙亮斑则零实时开销，像素化后观感也稳定。
    /// </summary>
    public static class SlimaLitTileGenerator
    {
        const string ResourcesRoot = "Tools/BlockMapEditor/Resources";
        const float Gain = 1.34f;     // 提亮倍数
        const float Warm = 1.06f;     // 暖色（红×Warm，蓝×(2-Warm)）

        [MenuItem("Tools/Slima/地图编辑器/给所有方块生成提亮版（灯光亮斑，_Lit）", false, 445)]
        public static void GenerateAll()
        {
            if (!Directory.Exists(ResourcesRoot))
            {
                EditorUtility.DisplayDialog("Slima", $"找不到目录：{ResourcesRoot}", "好");
                return;
            }

            int made = 0, skipped = 0;
            string[] dirs = Directory.GetDirectories(ResourcesRoot);

            try
            {
                AssetDatabase.StartAssetEditing();

                foreach (string dir in dirs)
                {
                    string id = Path.GetFileName(dir);
                    if (id.EndsWith("_Lit")) continue;

                    string litDir = Path.Combine(ResourcesRoot, id + "_Lit");
                    if (Directory.Exists(litDir)) { skipped++; continue; }

                    // 支持 _panel+_frame 与 _simple 两种形态
                    string[] kinds = { "panel", "frame", "simple" };
                    bool any = false;

                    foreach (string kind in kinds)
                    {
                        string src = Path.Combine(dir, $"{id}_{kind}.png");
                        if (!File.Exists(src)) continue;

                        Directory.CreateDirectory(litDir);
                        string dst = Path.Combine(litDir, $"{id}_Lit_{kind}.png");
                        if (Brighten(src, dst)) any = true;
                    }

                    if (any) made++; else { skipped++; Directory.Delete(litDir, true); }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.Refresh();
            }

            Debug.Log($"[BlockMap] 提亮版生成完成：新增 {made} 个，跳过 {skipped} 个");
            EditorUtility.DisplayDialog("BlockMap",
                $"提亮版（_Lit）生成完成：\n\n· 新增 {made} 个方块\n· 跳过 {skipped} 个（已存在或没有贴图）\n\n" +
                "回到地图编辑器场景 → Play → 方块库里会出现 `<原名>_Lit` ✓\n" +
                "把灯周围那几格刷成 _Lit，就是烘焙好的灯光亮斑 ✓", "好");
        }

        /// <summary>提亮 + 微暖色，写出新 PNG（保持 alpha）。</summary>
        static bool Brighten(string srcPath, string dstPath)
        {
            Texture2D tex = LoadReadable(srcPath);
            if (tex == null) return false;

            int w = tex.width, h = tex.height;
            Color[] px = tex.GetPixels();

            for (int i = 0; i < px.Length; i++)
            {
                Color c = px[i];
                px[i] = new Color(
                    Mathf.Clamp01(c.r * Gain * Warm),
                    Mathf.Clamp01(c.g * Gain),
                    Mathf.Clamp01(c.b * Gain * (2f - Warm)),
                    c.a);
            }

            var outTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            outTex.SetPixels(px);
            outTex.Apply();

            File.WriteAllBytes(dstPath, outTex.EncodeToPNG());
            Object.DestroyImmediate(outTex);
            Object.DestroyImmediate(tex);
            return true;
        }

        /// <summary>方块贴图默认不可读，这里临时打开 Read/Write 再读像素。</summary>
        static Texture2D LoadReadable(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return null;
            if (!importer.isReadable) { importer.isReadable = true; importer.SaveAndReimport(); }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>新生成的 _Lit 贴图按像素风导入：Point、无 Mipmap、无压缩。</summary>
        static void FixImporter(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;
            importer.textureType = TextureImporterType.Default;
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.SaveAndReimport();
        }

        [MenuItem("Tools/Slima/地图编辑器/修正 _Lit 贴图导入设置（Point / 无 Mipmap）", false, 446)]
        public static void FixAllImporters()
        {
            int n = 0;
            foreach (string dir in Directory.GetDirectories(ResourcesRoot))
            {
                if (!Path.GetFileName(dir).EndsWith("_Lit")) continue;
                foreach (string png in Directory.GetFiles(dir, "*.png"))
                {
                    FixImporter(png.Replace('\\', '/'));
                    n++;
                }
            }
            AssetDatabase.Refresh();
            Debug.Log($"[BlockMap] 已修正 {n} 张 _Lit 贴图的导入设置（Point / 无 Mipmap）");
            EditorUtility.DisplayDialog("BlockMap", $"已修正 {n} 张 _Lit 贴图的导入设置 ✓", "好");
        }
    }
}
