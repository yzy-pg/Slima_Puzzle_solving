using UnityEngine;

namespace BlockMap
{
    /// <summary>
    /// 编辑场景的参考物：
    ///   1) 地面网格 + 世界边界竖线（贴图看不清格子时靠它对齐）
    ///   2) 鼠标位置虚影 —— 半透明立方体，指示"点下去会放在哪一格"
    /// 由 BlockMapEditorTool 提供 CursorCell。
    /// </summary>
    [RequireComponent(typeof(BlockWorld))]
    public class BlockMapGuides : MonoBehaviour
    {
        [Tooltip("留空会自动找同物体上的 BlockMapEditorTool")]
        public BlockMapEditorTool tool;

        [Header("网格")]
        public bool showGrid = true;
        public Color gridColor = new Color(1f, 1f, 1f, 0.10f);
        public Color boundsColor = new Color(1f, 0.75f, 0.3f, 0.35f);

        [Header("虚影")]
        public Color ghostColor = new Color(0.55f, 0.85f, 1f, 0.35f);
        public bool showGhost = true;

        BlockWorld _world;
        Transform _ghost;
        Material _ghostMat;

        void Awake()
        {
            _world = GetComponent<BlockWorld>();
            if (tool == null) tool = GetComponent<BlockMapEditorTool>();
        }

        void Start()
        {
            if (showGrid) BuildGrid();
            BuildGhost();
        }

        void LateUpdate()
        {
            if (_ghost == null) return;
            bool has = showGhost && tool != null && tool.HasCursor;
            if (_ghost.gameObject.activeSelf != has) _ghost.gameObject.SetActive(has);
            if (has) _ghost.position = (Vector3)tool.CursorCell + Vector3.one * 0.5f;
        }

        /// <summary>用 MeshTopology.Lines 画地面网格 + 世界边界，零贴图、零额外 draw call 负担。</summary>
        void BuildGrid()
        {
            Vector3Int size = _world.maxSize;
            var verts = new System.Collections.Generic.List<Vector3>();
            var colors = new System.Collections.Generic.List<Color>();

            for (int x = 0; x <= size.x; x++)
            {
                verts.Add(new Vector3(x, 0f, 0f)); verts.Add(new Vector3(x, 0f, size.z));
                colors.Add(gridColor); colors.Add(gridColor);
            }
            for (int z = 0; z <= size.z; z++)
            {
                verts.Add(new Vector3(0f, 0f, z)); verts.Add(new Vector3(size.x, 0f, z));
                colors.Add(gridColor); colors.Add(gridColor);
            }

            // 世界边界的八条竖线 + 顶面四边（提示高度上限）
            Vector3[] c =
            {
                new Vector3(0, 0, 0), new Vector3(size.x, 0, 0),
                new Vector3(0, 0, size.z), new Vector3(size.x, 0, size.z)
            };
            foreach (var p in c)
            {
                verts.Add(p); verts.Add(p + Vector3.up * size.y);
                colors.Add(boundsColor); colors.Add(boundsColor);
            }
            for (int i = 0; i < 4; i++)
            {
                Vector3 a = c[i], b = c[(i + 1) % 4];
                verts.Add(a + Vector3.up * size.y); verts.Add(b + Vector3.up * size.y);
                colors.Add(boundsColor); colors.Add(boundsColor);
            }

            var mesh = new Mesh { name = "BlockMapGrid" };
            var indices = new int[verts.Count];
            for (int i = 0; i < indices.Length; i++) indices[i] = i;
            mesh.indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetIndices(indices, MeshTopology.Lines, 0);

            var go = new GameObject("Grid");
            go.transform.SetParent(transform, false);
            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            var shader = Resources.Load<Shader>("BlockMap/GridLine") ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader) { name = "BlockMapGrid" };
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        void BuildGhost()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "PlaceGhost";
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);
            var shader = Shader.Find("BlockMap/GhostUnlit");
            if (shader == null) shader = Resources.Load<Shader>("BlockMap/GhostUnlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            _ghostMat = new Material(shader) { name = "BlockMapGhost" };
            _ghostMat.color = ghostColor;
            if (_ghostMat.HasProperty("_Color")) _ghostMat.SetColor("_Color", ghostColor);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = _ghostMat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.transform.SetParent(transform, false);
            go.transform.localScale = Vector3.one * 1.02f;
            go.SetActive(false);
            _ghost = go.transform;
        }
    }
}
