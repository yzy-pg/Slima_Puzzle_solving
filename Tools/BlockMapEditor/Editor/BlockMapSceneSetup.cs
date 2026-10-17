#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BlockMap
{
    /// <summary>
    /// 一键生成"地图编辑专用场景"：
    ///   · 固定的等轴正交机位（可再按 1~4 切俯视/正视/侧视）
    ///   · 地面网格 + 世界边界线
    ///   · 鼠标虚影、地图编辑器面板
    ///   · 世界尺寸上限 256 x 36 x 256
    /// 菜单：Tools / BlockMap / 创建地图编辑场景
    /// </summary>
    public static class BlockMapSceneSetup
    {
        public const string ScenePath = "Assets/Scenes/BlockMapEditor.unity";

        [MenuItem("Tools/Slima/地图编辑器/① 创建地图编辑场景", false, 100)]
        public static void CreateEditorScene()
        {
            // 先给机会保存当前场景，再开新的编辑场景（避免未保存的改动丢失）
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // ---- 固定机位相机 ----
            var camGo = new GameObject("Editor Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.13f, 0.13f, 0.16f);
            cam.nearClipPlane = 0.05f;
            var orbit = camGo.AddComponent<OrthoOrbitCamera>();
            orbit.yaw = 45f;
            orbit.pitch = 35f;
            orbit.size = 20f;                 // 居中拉近（不塞整张画布）
            orbit.centerOnStart = true;
            orbit.minSize = 2f;
            orbit.maxSize = 200f;
            orbit.pixelPerfect = false;      // 编辑场景用自由缩放更顺手
            orbit.Focus(new Vector3(128f, 18f, 128f), 20f);       // 视野中心 = 画布中心
            orbit.worldCenter = new Vector3(128f, 18f, 128f);
            // 俯视机位：相机在画布中心正上方 75m 处垂直往下看（每格 1m）
            orbit.fixedOffset = new Vector3(0f, 75f, 0f);
            orbit.fixedPosition = true;                      // 俯视固定机位；想要自由环绕就勾掉这项
            orbit.forceTopDownOnStart = true;                // 启动强制正俯视（覆盖 Inspector 旧值）
            orbit.topDownHeight = 75f;
            orbit.topDownTiltDegrees = 34f;                  // 同时看到顶面 + 一个侧面
            orbit.forceSizeOnStart = 20f;

            // ---- 地图编辑器本体 ----
            var go = new GameObject("BlockMap");
            var world = go.AddComponent<BlockWorld>();
            world.chunkSize = 16;
            world.maxSize = new Vector3Int(256, 36, 256);

            var renderer = go.AddComponent<BlockChunkRenderer>();
            renderer.blockSize = 1f;                         // 每格 = 1m（Unity 1 单位 = 1 米，网格线间距也是 1）
            var tool = go.AddComponent<BlockMapEditorTool>();
            var guides = go.AddComponent<BlockMapGuides>();
            guides.tool = tool;
            var boot = go.AddComponent<BlockMapBootstrap>();
            boot.startInEditorMode = true;
            boot.loadLastSave = true;
            boot.buildDemoIfEmpty = false;

            // 场景里留一盏平行光（材质虽是无光照，方便你以后加东西）
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[BlockMap] 地图编辑场景已创建：" + ScenePath + "  （世界上限 256x36x256）");
            EditorUtility.DisplayDialog("BlockMap",
                "地图编辑场景已创建：\n" + ScenePath +
                "\n\n直接按 Play 即可开始编辑。\n世界上限：256 x 36 x 256 格", "好");
        }

        // ------------------------------------------------------------------
        // 关卡文件（多人协作）：一个纯文本、可进 Git 的 .json，里面只记
        //   ① 每格的方块 id  ② chunk 尺寸
        // 素材（Resources/BlockTextures/<id>_panel.png + <id>_frame.png）本身也在工程里，
        // 所以队友拉下代码后点一次导入菜单，就能把同一张地图搭出来。
        // ------------------------------------------------------------------

        public const string LevelsDir = "Assets/BlockMapEditor/Levels";

        [MenuItem("Tools/Slima/地图编辑器/导出地图 json 到工程（Git）", false, 400)]
        public static void ExportLevel()
        {
            var world = Object.FindObjectOfType<BlockWorld>();
            if (world == null) { EditorUtility.DisplayDialog("BlockMap", "当前场景里没有 BlockWorld。", "好"); return; }

            world.RestoreIfNeeded();
            if (world.Count == 0)
            {
                EditorUtility.DisplayDialog("BlockMap",
                    "当前场景里的 BlockWorld 是空的，导出的 json 不会有任何方块。\n\n" +
                    "原因：编辑模式下的场景本身没有方块数据 —— Play 里的编辑结果不会自动写回场景。\n\n" +
                    "正确顺序：\n" +
                    "  ① Play → 面板「保存」/F5 → 退出 Play\n" +
                    "  ② 菜单「② 把地图烘成场景（持久化）」把存档搭进场景\n" +
                    "  ③ 这时就能导出 / 直接 Ctrl+S 保存场景了", "好");
                return;
            }

            Directory.CreateDirectory(LevelsDir);
            string name = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (string.IsNullOrEmpty(name)) name = "level";
            string path = LevelsDir + "/" + name + ".json";
            File.WriteAllText(path, world.ToJson());
            AssetDatabase.Refresh();
            Debug.Log("[BlockMap] 关卡已导出：" + path);
            EditorUtility.DisplayDialog("BlockMap", "关卡已导出：\n" + path + "\n\n可以直接提交到 Git 给队友。", "好");
        }

        [MenuItem("Tools/Slima/地图编辑器/从工程导入地图 json", false, 410)]
        public static void ImportLevel()
        {
            string path = EditorUtility.OpenFilePanel("选择关卡 json", LevelsDir, "json");
            if (string.IsNullOrEmpty(path)) return;

            var world = Object.FindObjectOfType<BlockWorld>();
            if (world == null)
            {
                EditorUtility.DisplayDialog("BlockMap", "当前场景里没有 BlockWorld，请先用“创建地图编辑场景”。", "好");
                return;
            }

            world.FromJson(File.ReadAllText(path));
            var renderer = world.GetComponent<BlockChunkRenderer>();
            if (renderer != null) renderer.RebuildAll();
            UnityEditor.SceneManagement.EditorSceneManager.MarkAllScenesDirty();
            Debug.Log("[BlockMap] 关卡已导入：" + path + "，共 " + world.Count + " 个方块");
            EditorUtility.DisplayDialog("BlockMap", "已导入 " + world.Count + " 个方块。\n缺失的方块 id 会在 Console 里列出来。", "好");
        }
    }
}
#endif
