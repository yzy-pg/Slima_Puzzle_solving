using UnityEngine;

namespace BlockMap
{
    /// <summary>
    /// 一键启动器：自动补齐 BlockWorld / BlockChunkRenderer / BlockMapEditorTool，并给 Main Camera 补 OrthoOrbitCamera。
    /// 默认**不生成任何演示内容** —— 地图来源是"上次保存的存档"，没有就是空地图并进入编辑模式。
    /// 打包发版时把 startInEditorMode 关掉即可（玩家进去看到的就是做好的场景）。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class BlockMapBootstrap : MonoBehaviour
    {
        [Header("发版保护")]
        [Tooltip("勾上：地图编辑器只在 Unity Editor / Development Build 里启用；正式包自动关闭，玩家看不到任何编辑器痕迹")]
        public bool editorOnlyInDevelopmentBuild = true;

        [Header("启动行为")]
        [Tooltip("启动就进入地图编辑模式（游戏内按 Tab 也能随时进出）")]
        public bool startInEditorMode = true;
        [Tooltip("启动自动读取上次保存的地图")]
        public bool loadLastSave = true;
        [Tooltip("地图为空时搭一个演示结构 —— 只在开发时勾，发版请关")]
        public bool buildDemoIfEmpty = false;

        void Awake()
        {
            var world = GetComponent<BlockWorld>();
            if (world == null) world = gameObject.AddComponent<BlockWorld>();

            var renderer = GetComponent<BlockChunkRenderer>();
            if (renderer == null) renderer = gameObject.AddComponent<BlockChunkRenderer>();

            var tool = GetComponent<BlockMapEditorTool>();
            if (tool == null) tool = gameObject.AddComponent<BlockMapEditorTool>();

            var linkEditor = GetComponent<BlockMapLinkEditorTool>();
            if (linkEditor == null) linkEditor = gameObject.AddComponent<BlockMapLinkEditorTool>();

            OrthoOrbitCamera orbit = null;
            if (Camera.main != null) orbit = Camera.main.GetComponent<OrthoOrbitCamera>();
            if (orbit == null)
            {
                GameObject camGo = Camera.main != null ? Camera.main.gameObject : new GameObject("Main Camera");
                if (Camera.main == null)
                {
                    camGo.tag = "MainCamera";
                    var cam = camGo.AddComponent<Camera>();
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = new Color(0.12f, 0.12f, 0.15f);
                }
                orbit = camGo.GetComponent<OrthoOrbitCamera>();
                if (orbit == null) orbit = camGo.AddComponent<OrthoOrbitCamera>();
            }

            // 发版保护：正式包（非 Development Build）里自动关掉编辑器、网格与虚影，
            // 只留下玩家该看到的内容。Unity Editor 里始终启用，方便继续调试。
            bool allowEditor = !editorOnlyInDevelopmentBuild || Application.isEditor || Debug.isDebugBuild;
            if (!allowEditor)
            {
                var guides = GetComponent<BlockMapGuides>();
                if (guides != null) guides.enabled = false;      // 关掉网格与放置虚影
                tool.SetEditorMode(false);                       // 退出编辑模式（同时把相机输入交回游戏）
                tool.enabled = false;                            // 面板、鼠标、快捷键全部失效
                linkEditor.enabled = false;
                Debug.Log("[BlockMap] 正式包：地图编辑器已自动关闭（玩家只会看到场景内容）");
            }
            else
            {
                tool.SetEditorMode(startInEditorMode);
            }
            if (orbit != null && world.Count > 0) orbit.Focus(new Vector3(4f, 0f, 4f), 16f);

            // 特殊模型 / 发光方块由它实例化（正式包里也要保留：这是游戏内容，不是编辑器）
            if (GetComponent<BlockMapRuntimeSpawner>() == null)
                gameObject.AddComponent<BlockMapRuntimeSpawner>();
        }

        /// <summary>
        /// 读档放在 Start 而不是 Awake：本组件带 DefaultExecutionOrder(-100)，
        /// Awake 阶段同物体上其它组件的 Awake 还没执行，它们的引用都还是 null
        /// （之前的 NullReferenceException 就是这么来的）。Start 在所有 Awake 之后跑。
        /// </summary>
        void Start()
        {
            var world = GetComponent<BlockWorld>();
            var tool = GetComponent<BlockMapEditorTool>();
            if (tool == null) return;
            if (loadLastSave) tool.TryLoadSilently();
            if (buildDemoIfEmpty && world != null && world.Count == 0 && BlockLibrary.Count > 0) BuildDemo(world);
        }

        static void BuildDemo(BlockWorld world)
        {
            if (BlockLibrary.Count < 1) return;
            for (int x = 0; x < 6; x++) world.Set(new Vector3Int(x, 0, 0), 0);
            for (int z = 0; z < 6; z++) world.Set(new Vector3Int(0, 0, z), 0);
            if (BlockLibrary.Count > 1)
                for (int x = 2; x < 5; x++)
                    for (int y = 1; y < 4; y++)
                        world.Set(new Vector3Int(x, y, 2), 1);
        }
    }
}
