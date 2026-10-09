using UnityEngine;

namespace BlockMap
{
    /// <summary>
    /// 一种方块的定义 —— 这就是"外部素材库"的最小单元。
    /// 新建：Project 窗口右键 → Create → BlockMap → Block Definition
    /// 只要把资产放进任意 Resources/BlockDefinitions/ 文件夹，运行时会自动收集（不写死任何常量）。
    /// </summary>
    [CreateAssetMenu(menuName = "BlockMap/Block Definition", fileName = "NewBlock")]
    public class BlockDefinition : ScriptableObject
    {
        [Header("标识")]
        [Tooltip("唯一 id，存档用它记录方块类型，改 id 会让老存档认不出这个方块")]
        public string id = "new_block";
        public string displayName = "新方块";

        [Header("贴图（三选一）")]
        [Tooltip("单张贴图：六个面都用它，不做自动包边")]
        public Texture2D simpleTexture;

        [Tooltip("面板贴图：自动包边时的内部（如 untitled2.png）")]
        public Texture2D panelTexture;

        [Tooltip("边框贴图：自动包边时的外沿。只需画好四条边的边条和四角的角点（如 untitled.png）")]
        public Texture2D frameTexture;

        [Tooltip("可选：直接指定一张已经做好的 8x8 图集（有它就用它，忽略上面两张）")]
        public Texture2D atlasOverride;

        [Header("用途分类")]
        [Tooltip("勾上 = 交互道具（门/箱子/机关等）。烘焙静态几何时会自动跳过它们，避免被融并进静态资产；" +
                 "游戏运行时读 json / BlockWorld 里的这些格子，自行实例化你的交互物件即可。")]
        public bool interactive = false;

        [Header("显示")]
        public Color tint = Color.white;

        [Header("渲染方式")]
        [Tooltip("Opaque = 普通不透明（默认）；Cutout = 用 alpha 镂空（树叶/栅栏，阈值看 cutoff）；" +
                 "Transparent = 真半透明（玻璃/水）；Animated = 序列帧动画（岩浆/流水，贴图请用一条横向帧序列）")]
        public BlockRenderMode renderMode = BlockRenderMode.Opaque;

        [Tooltip("镂空阈值：alpha 低于它就被裁掉（Cutout / Animated 有效）")]
        [Range(0f, 1f)] public float cutoff = 0.5f;

        [Tooltip("勾上 = 双面可见（薄片类：树叶、纸、藤蔓）")]
        public bool doubleSided = false;

        [Header("序列帧动画（renderMode = Animated 时有效）")]
        [Tooltip("帧数：贴图横向切成 N 帧（一条 1×N 的序列图）")]
        public int animFrames = 4;
        [Tooltip("每秒播放帧数")]
        public float animSpeed = 8f;

        [Header("特殊模型 / 交互道具")]
        [Tooltip("真实外观 Prefab：编辑器与正式场景都会用它替代方块本身。" +
                 "门/箱子/家具/树这类非立方体或带行为的物件，全部走这里。")]
        public GameObject previewPrefab;

        [Header("墙贴")]
        [Tooltip("勾上 = 贴片模式：模型会吸附到'朝向反方向'那面墙上（海报 / 开关 / 壁灯 / 招牌）")]
        public bool mountOnWall = false;

        [Header("发光")]
        [Tooltip("自发光遮罩：与颜色贴图使用相同 UV；自动包边方块指定对应的 8×8 图集。黑色不发光。")]
        public Texture2D emissionMap;
        [ColorUsage(false, true)] public Color emissionColor = Color.white;
        [Min(0f)] public float emissionStrength;
        [Tooltip("勾上 = 运行时在这些格子生成点光源，照亮周围")]
        public bool emitLight = false;
        public Color lightColor = Color.white;
        [Tooltip("光照范围")] public float lightRange = 8f;
        [Tooltip("光照强度")] public float lightIntensity = 1f;
        [Tooltip("相对方块中心（或替代模型原点）的发光位置；关闭时沿用生成器的统一高度。")]
        public bool useLocalLightPosition;
        public Vector3 lightLocalPosition = new Vector3(0f, 1.5f, 0f);

        public bool IsLamp => !string.IsNullOrEmpty(id) &&
            id.EndsWith("_Lamp", System.StringComparison.OrdinalIgnoreCase);

        public Vector3 GetLightPosition(Vector3 cellCenter, int direction, float fallbackHeight)
        {
            if (!useLocalLightPosition) return cellCenter + Vector3.up * fallbackHeight;
            Quaternion rotation = Quaternion.Euler(0f, direction * 90f, 0f) *
                (previewPrefab != null ? previewPrefab.transform.rotation : Quaternion.identity);
            Vector3 origin = mountOnWall ? cellCenter - rotation * Vector3.forward * 0.5f : cellCenter;
            return origin + rotation * lightLocalPosition;
        }

        Texture2D _runtimeAtlas;

        /// <summary>是否使用自动包边（贴图空间 CT）。</summary>
        public bool UseAutoTile => atlasOverride != null || (panelTexture != null && frameTexture != null);

        /// <summary>取出这个方块要用的贴图：图集（包边）或单图。</summary>
        public Texture2D GetTexture()
        {
            if (atlasOverride != null) return atlasOverride;
            if (panelTexture == null || frameTexture == null) return simpleTexture;
            if (_runtimeAtlas == null)
            {
                _runtimeAtlas = AutoTileAtlas.Build(panelTexture, frameTexture);
                _runtimeAtlas.name = id + "_atlas";
            }
            return _runtimeAtlas;
        }

        public bool IsValid => UseAutoTile ? true : simpleTexture != null;
    }
}
