using UnityEngine;

namespace BlockMap.Gameplay
{
    /// <summary>机关的纯表现层。开关状态由 BlockGameplayController 决定。</summary>
    public sealed class GameplayBlockView : MonoBehaviour
    {
        Renderer[] _renderers;
        Color _baseColor;
        Color[] _materialColors;
        float[] _emissionStrengths;
        bool _active;
        BlockMapDoor _door;
        GameplayControlArt _control;
        GameplayOpticGlow _opticGlow;
        Texture2D _linkPalette;
        Color[] _linkColors = new Color[0];
        readonly System.Collections.Generic.List<Material> _doorMaterials = new System.Collections.Generic.List<Material>();
        Bounds _doorBounds;
        [SerializeField, HideInInspector] int _capturedDirection = -1;

        public Vector3Int Cell { get; private set; }
        public GameplayBlockProfile Profile { get; private set; }

        public void Initialize(Vector3Int cell, GameplayBlockProfile profile)
        {
            Cell = cell;
            Profile = profile;
            _capturedDirection = Mathf.RoundToInt(transform.eulerAngles.y / 90f) & 3;
            _baseColor = profile.Tint;
            _renderers = GetComponentsInChildren<Renderer>(true);
            if (GetComponentInChildren<GameplayMechanismArt>(true) != null)
            {
                _materialColors = new Color[_renderers.Length];
                _emissionStrengths = new float[_renderers.Length];
                for (int i = 0; i < _renderers.Length; i++)
                {
                    _materialColors[i] = _renderers[i].sharedMaterial.color;
                    _emissionStrengths[i] = _renderers[i].sharedMaterial.GetFloat("_EmissionStrength");
                }
            }
            _door = GetComponentInChildren<BlockMapDoor>(true);
            _control = GetComponentInChildren<GameplayControlArt>(true);
            _opticGlow = GetComponentInChildren<GameplayOpticGlow>(true);
            if (profile.Kind == GameplayBlockKind.Door) InitializeDoorMaterials(Application.isPlaying);
            ApplyColor(_baseColor);
            var gameRoot = GetComponentInParent<BlockMapGameRoot>();
            if (gameRoot != null)
                foreach (Renderer item in _renderers) gameRoot.ApplyAtmosphereTo(item);
            // Create transparent halos after collecting the opaque artwork renderers.
            if (_opticGlow != null) _opticGlow.Initialize();
        }

        public void AnimatePush(Vector3Int cell, float duration)
        {
            Cell = cell;
            name = $"Gameplay_{Profile.id}_{cell.x}_{cell.y}_{cell.z}";
            var animation = GetComponent<GameplayPushAnimation>();
            if (animation == null) animation = gameObject.AddComponent<GameplayPushAnimation>();
            animation.MoveTo(new Vector3(cell.x + .5f, cell.y, cell.z + .5f), duration);
        }

        public void SetActivated(bool active)
        {
            if (_active == active) return;
            _active = active;
            if (_door != null) _door.SetOpen(active);
            if (_control != null) _control.SetActivated(active);
            if (_opticGlow != null) _opticGlow.SetActivated(active);
            ApplyColor(active ? Color.Lerp(_baseColor, Color.white, 0.45f) : _baseColor);
        }

        internal bool TryCaptureEditedDirection(out int direction)
        {
            direction = Mathf.RoundToInt(transform.eulerAngles.y / 90f) & 3;
            if (direction == _capturedDirection) return false;
            _capturedDirection = direction;
            return true;
        }

        public void SetReceiverFaces(int worldFaces)
        {
            if (_opticGlow == null || !_opticGlow.receiver) return;
            int localFaces = 0;
            int rotation = Mathf.RoundToInt(_opticGlow.transform.eulerAngles.y / 90f) & 3;
            for (int face = 0; face < 4; face++)
                if ((worldFaces & (1 << face)) != 0) localFaces |= 1 << ((face - rotation + 4) & 3);
            _opticGlow.SetReceivingFaces(localFaces);
        }

        void ApplyColor(Color color)
        {
            if (_renderers == null) return;
            var block = new MaterialPropertyBlock();
            for (int i = 0; i < _renderers.Length; i++)
            {
                Renderer item = _renderers[i];
                if (item == null) continue;
                Color materialColor = _materialColors == null ? color :
                    (_active && _control == null && _opticGlow == null ? Color.Lerp(_materialColors[i], Color.white, 0.25f) : _materialColors[i]);
                if (_opticGlow != null && (item == _opticGlow.opticalSurface || item == _opticGlow.topIndicator))
                    materialColor = _opticGlow.color;
                item.GetPropertyBlock(block);
                block.SetColor("_BaseColor", materialColor);
                block.SetColor("_Color", materialColor);
                bool linkedPart = Profile.Kind == GameplayBlockKind.Door ||
                    (_control != null && System.Array.IndexOf(_control.colorParts, item) >= 0);
                if (linkedPart)
                {
                    Color neutral = Profile.Kind == GameplayBlockKind.Door && _materialColors == null ? Color.gray : Color.white;
                    // 活跃状态由模型动画表达，关联色始终保持一致。
                    block.SetColor("_BaseColor", _linkColors.Length == 0 ? neutral : _linkColors[0]);
                    block.SetColor("_Color", _linkColors.Length == 0 ? neutral : _linkColors[0]);
                    block.SetFloat("_LinkColorCount", _linkColors.Length);
                    if (_linkPalette != null) block.SetTexture("_LinkPalette", _linkPalette);
                    var filter = item.GetComponent<MeshFilter>();
                    Bounds bounds = filter != null && filter.sharedMesh != null ? filter.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one);
                    bool alongX = bounds.size.x >= bounds.size.z;
                    Matrix4x4 relative = Matrix4x4.identity;
                    if (Profile.Kind == GameplayBlockKind.Door)
                    {
                        bounds = _doorBounds;
                        alongX = bounds.size.x >= bounds.size.z;
                        Transform doorSpace = _door.hinge != null ? _door.hinge : _door.transform;
                        relative = doorSpace.worldToLocalMatrix * item.transform.localToWorldMatrix;
                    }
                    block.SetVector("_LinkAxis", relative.GetRow(alongX ? 0 : 2));
                    block.SetVector("_LinkRange", new Vector4(alongX ? bounds.min.x : bounds.min.z,
                        Mathf.Max(.001f, alongX ? bounds.size.x : bounds.size.z), 0, 0));
                }
                if (_emissionStrengths != null && (_opticGlow == null ||
                    (item != _opticGlow.opticalSurface && item != _opticGlow.topIndicator)))
                    block.SetFloat("_EmissionStrength", _emissionStrengths[i] * (_active ? 2.5f : 1f));
                item.SetPropertyBlock(block);
            }
        }

        public void SetLinkColors(Color[] colors)
        {
            colors = colors ?? new Color[0];
            bool same = colors.Length == _linkColors.Length;
            for (int i = 0; same && i < colors.Length; i++) same = colors[i] == _linkColors[i];
            if (same) { ApplyColor(_baseColor); return; }
            _linkColors = (Color[])colors.Clone();
            ReleasePalette();
            if (colors.Length > 0)
            {
                _linkPalette = new Texture2D(colors.Length, 1, TextureFormat.RGBA32, false)
                { name = "Mechanism link colors", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                _linkPalette.SetPixels(colors);
                _linkPalette.Apply(false, true);
            }
            ApplyColor(_baseColor);
        }

        void InitializeDoorMaterials(bool playing)
        {
            bool first = true;
            foreach (var item in _renderers)
            {
                var materials = item.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    // 旧 OBJ 门也使用同一套 Toon shader，保留原几何和门动画。
                    var source = materials[i];
                    var material = source.shader.name == "Slima/Toon (Cel + Pixel)" ? new Material(source) :
                        new Material(Shader.Find("Slima/Toon (Cel + Pixel)"));
                    if (source.mainTexture != null) material.mainTexture = source.mainTexture;
                    material.SetFloat("_OutlineWidth", playing ? 2f : .6f);
                    if (playing) material.SetColor("_OutlineColor", new Color(1f,.72f,.06f,1f));
                    _doorMaterials.Add(material); materials[i] = material;
                }
                item.sharedMaterials = materials;
                var filter = item.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                Bounds meshBounds = filter.sharedMesh.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 vertex = meshBounds.center + Vector3.Scale(meshBounds.extents,
                        new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                    Transform doorSpace = _door.hinge != null ? _door.hinge : _door.transform;
                    Vector3 local = doorSpace.InverseTransformPoint(item.transform.TransformPoint(vertex));
                    if (first) { _doorBounds = new Bounds(local, Vector3.zero); first = false; }
                    else _doorBounds.Encapsulate(local);
                }
            }
        }

        void OnDestroy()
        {
            ReleasePalette();
            foreach (var material in _doorMaterials)
            {
                if (Application.isPlaying) Destroy(material);
                else DestroyImmediate(material);
            }
        }

        void ReleasePalette()
        {
            if (_linkPalette == null) return;
            if (Application.isPlaying) Destroy(_linkPalette);
            else DestroyImmediate(_linkPalette);
            _linkPalette = null;
        }
    }
}
