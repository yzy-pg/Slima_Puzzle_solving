#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BlockMap
{
    /// <summary>
    /// 只读一个关卡 json，就在【编辑模式】把场景真正搭出来 —— 不需要 Play。
    ///
    /// 持久化原理：
    ///   · 方块几何 = 每个 chunk 一个 MeshFilter + MeshRenderer（真实场景对象）
    ///   · Mesh 会被序列化进 .unity 场景文件 → 保存场景后就一直在
    ///   · 材质单独存成 .asset（否则运行时 new 出来的材质保存后会变成 Missing）
    ///
    /// 菜单：Tools / BlockMap / ② 读取存档并在当前场景搭建（持久化）
    /// </summary>
    public static class BlockMapLevelBuilder
    {
        const string RootName = "BlockMapStatic";
        const string BuiltDir = "Assets/BlockMapEditor/Built";

        /// <summary>把可能非法的文件名字符替换掉</summary>
        static string SafeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "BlockMapLevel";
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }

        [MenuItem("Tools/Slima/地图编辑器/② 把地图烘成场景（持久化）", false, 200)]
        public static void BuildFromDialog()
        {
            // 默认打开 Play 模式「保存」写到的存档目录（真正的数据在那儿）；也可以自己导航到工程内的 Levels
            string dir = Path.Combine(Application.persistentDataPath, "Maps");
            if (!Directory.Exists(dir))
            {
                string levels = Path.Combine(Application.dataPath, "BlockMapEditor/Levels");
                dir = Directory.Exists(levels) ? levels : Application.dataPath;
            }
            string path = EditorUtility.OpenFilePanel("选择关卡 json（存档目录或工程内 Levels 都行）", dir, "json");
            if (string.IsNullOrEmpty(path)) return;

            // ★ 每次搭建都用【全新场景】：先让用户保存当前场景，再新建空场景，
            //   这样绝对不会和场景里现有的模型 / 物件冲突。
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            Build(File.ReadAllText(path), Path.GetFileNameWithoutExtension(path));
        }

        /// <summary>按名字搭工程内 Assets/BlockMapEditor/Levels/&lt;名字&gt;.json（方便写进自动化流程）</summary>
        public static void BuildFromProjectLevel(string levelName)
        {
            string p = Path.Combine(Application.dataPath, "BlockMapEditor/Levels/" + levelName + ".json");
            if (!File.Exists(p)) { Debug.LogError("[BlockMap] 找不到关卡文件：" + p); return; }
            Build(File.ReadAllText(p), levelName);
        }

        static void Build(string json, string tag)
        {
            if (string.IsNullOrEmpty(json)) { Debug.LogError("[BlockMap] 关卡文件是空的"); return; }
            BlockLibrary.Reload();      // 清静态缓存：避免上一次运行"没读到贴图"留下的空方块库

            // 目标根节点（重复执行会先清掉上次的结果）
            var root = GameObject.Find(RootName);
            if (root == null) root = new GameObject(RootName);
            for (int i = root.transform.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(root.transform.GetChild(i).gameObject);

            var temp = new GameObject("__BlockMapBuildTemp");
            int chunkCount = 0;
            bool filledEditor = false;
            try
            {
                // 1) 解析数据（复用 BlockWorld 的读档逻辑）
                var world = temp.AddComponent<BlockWorld>();
                world.chunkSize = 16;
                world.maxSize = new Vector3Int(256, 36, 256);
                world.FromJson(json);
                Debug.Log($"[BlockMap] 解析结果：{world.Count} 个方块；方块库 {BlockLibrary.Count} 种");
                if (world.Count == 0)
                    Debug.LogWarning("[BlockMap] 这份 json 里没有任何方块，可能原因：\n" +
                        "  1) 是在【编辑模式】下用「导出当前关卡」导出的 —— 编辑模式的场景里本来就没有方块数据\n" +
                        "     （Play 里的编辑结果不会自动写回场景，必须先在 Play 里按「保存」/F5）\n" +
                        "  2) 选错了文件：Play 面板「保存」写的是 " + Path.Combine(Application.persistentDataPath, "Maps") + "\\<槽名>.json\n" +
                        "  3) 方块 id 在方块库里找不到（贴图命名要符合 <id>_panel.png + <id>_frame.png）");

                // 2) 复用运行时的网格生成（面剔除 + 图集 UV + 顶点色）
                var renderer = temp.AddComponent<BlockChunkRenderer>();
                renderer.blockSize = 1f;
                renderer.skipInteractiveBlocks = true;      // ★ 交互道具不烘进静态几何
                renderer.generateColliders = true;          // ★ 烘出来的地形要能站人：每个 chunk 一个 MeshCollider
                renderer.RebuildAll();

                // 统计被跳过的交互道具：它们仍留在 json / BlockWorld 的数据里，
                // 交给游戏运行时按格子位置实例化你自己的交互物件（不要焊进静态资产）。
                int interactiveCount = 0;
                foreach (var kv in world.Blocks)
                {
                    var d = BlockLibrary.Get(kv.Value);
                    if (d != null && d.interactive) interactiveCount++;
                }
                if (interactiveCount > 0)
                    Debug.Log($"[BlockMap] 已跳过 {interactiveCount} 个「交互道具」（不参与静态烘焙）；" +
                              "游戏里请读 json / BlockWorld 的这些格子自行实例化交互物件");

                // 3) 图集 + 材质都必须落成"工程资产"。
                //    只保存材质是不够的：自动包边的图集是运行时 new 出来的 Texture2D，
                //    材质资产引用不到它 → 重新打开场景就变粉红 Missing（这就是"材质丢失"的原因）。
                Directory.CreateDirectory(BuiltDir + "/Textures");
                Directory.CreateDirectory(BuiltDir + "/Materials");
                var byId = new Dictionary<string, Material>();
                foreach (var def in BlockLibrary.All)
                {
                    // 3.1 贴图：自动包边 → 图集导出成 png；单图方块 → 直接用工程里已有的贴图资产
                    Texture2D tex = def.atlasOverride != null ? def.atlasOverride : def.simpleTexture;
                    if (def.UseAutoTile && def.atlasOverride == null)
                    {
                        var runtimeAtlas = def.GetTexture();
                        if (runtimeAtlas != null)
                        {
                            string tp = BuiltDir + "/Textures/" + def.id + "_atlas.png";
                            File.WriteAllBytes(tp, runtimeAtlas.EncodeToPNG());
                            AssetDatabase.ImportAsset(tp, ImportAssetOptions.ForceUpdate);
                            if (AssetImporter.GetAtPath(tp) is TextureImporter ti)
                            {
                                ti.textureCompression = TextureImporterCompression.Uncompressed;
                                ti.filterMode = def.GeneratedAtlasFilter;
                                ti.wrapMode = TextureWrapMode.Clamp;
                                ti.mipmapEnabled = false;
                                ti.SaveAndReimport();
                            }
                            tex = AssetDatabase.LoadAssetAtPath<Texture2D>(tp);
                        }
                    }

                    // 3.2 材质：复用运行时材质的 shader/参数，但把贴图换成工程资产
                    var live = renderer.GetMaterial(def);
                    if (live == null) continue;
                    live.mainTexture = tex;

                    string p = BuiltDir + "/Materials/" + def.id + ".mat";
                    var asset = AssetDatabase.LoadAssetAtPath<Material>(p);
                    if (asset == null)
                    {
                        AssetDatabase.CreateAsset(live, p);
                        asset = live;
                    }
                    else
                    {
                        asset.shader = live.shader;
                        asset.mainTexture = tex;
                        // 把运行时材质上算好的参数原样搬过来（镂空阈值 / 双面 / 动画帧数…）
                        if (asset.HasProperty("_Cutoff") && live.HasProperty("_Cutoff")) asset.SetFloat("_Cutoff", live.GetFloat("_Cutoff"));
                        if (asset.HasProperty("_Cull") && live.HasProperty("_Cull")) asset.SetFloat("_Cull", live.GetFloat("_Cull"));
                        if (asset.HasProperty("_Frames") && live.HasProperty("_Frames")) asset.SetFloat("_Frames", live.GetFloat("_Frames"));
                        if (asset.HasProperty("_Speed") && live.HasProperty("_Speed")) asset.SetFloat("_Speed", live.GetFloat("_Speed"));
                        if (asset.renderQueue != live.renderQueue) asset.renderQueue = live.renderQueue;
                        EditorUtility.SetDirty(asset);
                    }
                    byId[def.id] = asset;
                }
                AssetDatabase.SaveAssets();

                // 4) 把 chunk 搬进根节点，并把运行时材质换成资产引用
                var chunks = new List<Transform>();
                foreach (Transform child in temp.transform) chunks.Add(child);
                foreach (var chunk in chunks)
                {
                    var mr = chunk.GetComponent<MeshRenderer>();
                    if (mr != null)
                    {
                        var mats = mr.sharedMaterials;
                        bool changed = false;
                        for (int i = 0; i < mats.Length; i++)
                        {
                            if (mats[i] == null || !mats[i].name.StartsWith("BlockMap_")) continue;
                            string id = mats[i].name.Substring("BlockMap_".Length);
                            if (byId.TryGetValue(id, out var asset) && asset != mats[i])
                            {
                                mats[i] = asset;
                                changed = true;
                            }
                        }
                        if (changed) mr.sharedMaterials = mats;
                    }
                    chunk.SetParent(root.transform, true);
                    chunkCount++;
                }
            }
            finally
            {
                Object.DestroyImmediate(temp);
            }

            // 5) 场景里若本来就挂着地图编辑器 —— 走"灌数据"模式：
            //    · 数据同步进场景的 BlockWorld
            //    · **重新启用** BlockChunkRenderer（上一版我在这里把它禁用了，导致"编辑不了地图"）
            //    · 清掉刚烘出来的静态几何，避免和运行时渲染重叠
            var sceneTool = Object.FindObjectOfType<BlockMapEditorTool>();
            var sceneWorld = sceneTool != null ? sceneTool.GetComponent<BlockWorld>() : null;
            if (sceneWorld != null)
            {
                sceneWorld.maxSize = new Vector3Int(256, 36, 256);
                sceneWorld.FromJson(json);

                var sceneRenderer = sceneWorld.GetComponent<BlockChunkRenderer>();
                if (sceneRenderer != null) sceneRenderer.enabled = true;

                var staleRoot = GameObject.Find(RootName);
                if (staleRoot != null) Object.DestroyImmediate(staleRoot);

                filledEditor = true;
                Debug.Log($"[BlockMap] 检测到地图编辑器场景：已把 {sceneWorld.Count} 个方块灌进 BlockWorld（可继续编辑），未生成静态几何");
            }
            else
            {
                // 成品场景：几何已经烘好，但"特殊模型 / 发光方块"还要生成出来。
                // 给静态根节点补一个只存数据的 BlockWorld + 一个生成器（不挂 ChunkRenderer，不会重复渲染）。
                var dataWorld = root.GetComponent<BlockWorld>();
                if (dataWorld == null) dataWorld = root.AddComponent<BlockWorld>();
                dataWorld.maxSize = new Vector3Int(256, 36, 256);
                dataWorld.FromJson(json);

                var spawner = root.GetComponent<BlockMapRuntimeSpawner>();
                if (spawner == null) spawner = root.AddComponent<BlockMapRuntimeSpawner>();
                spawner.container = root.transform;
                spawner.Rebuild();

                Debug.Log($"[BlockMap] 成品场景已补：数据 {dataWorld.Count} 格；特殊模型与光源由 BlockMapRuntimeSpawner 生成");
            }

            // 5.5) 空场景是没有灯的 —— 而三渲二卡通 shader 靠主光分五段，
            //      没光时会整片落在最暗段（看起来就是"地图一片灰"）。这里补一盏平行光。
            BlockMapPlayableScene.EnsureDirectionalLight();

            // 6) 场景标脏并保存 —— 这一步才叫"持久化"
            EditorSceneManager.MarkAllScenesDirty();
            var activeScene = EditorSceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(activeScene.path))
            {
                // 新建的空场景还没有路径：自动存成 Assets/Scenes/<关卡名>.unity
                Directory.CreateDirectory("Assets/Scenes");
                EditorSceneManager.SaveScene(activeScene, "Assets/Scenes/" + SafeName(tag) + ".unity");
            }
            else
            {
                EditorSceneManager.SaveScene(activeScene);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            string msg = filledEditor
                ? $"关卡「{tag}」已灌进场景里的地图编辑器（{(chunkCount > 0 ? chunkCount : 0)} 个临时 chunk 已换成运行时渲染）\n\n" +
                  "现在按 Play 就能继续编辑；改完记得在面板里「保存」。\n" +
                  "静态几何没有被烘焙（避免重叠），所以这个场景是「编辑用」的。"
                : $"关卡「{tag}」已烘成静态几何\nchunk 数：{chunkCount}\n\n" +
                  "场景已保存，关闭 Unity 再打开它依然在（材质/图集已落成工程资产）。";
            Debug.Log($"[BlockMap] 搭建完成「{tag}」：filledEditor={filledEditor} chunk={chunkCount} -> {EditorSceneManager.GetActiveScene().path}");
            EditorUtility.DisplayDialog("BlockMap", msg, "好");
        }
    }
}
#endif
