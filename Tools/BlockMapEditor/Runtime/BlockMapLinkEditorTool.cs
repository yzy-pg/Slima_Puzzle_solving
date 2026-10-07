using System.Collections.Generic;
using BlockMap.Gameplay;
using UnityEngine;

namespace BlockMap
{
    /// <summary>只负责机关实例的分组编辑与可视化；存档和运行时规则由各自模块处理。</summary>
    [DefaultExecutionOrder(-10)]
    [RequireComponent(typeof(BlockWorld), typeof(BlockMapEditorTool))]
    public sealed class BlockMapLinkEditorTool : MonoBehaviour
    {
        const float PanelWidth = 270f;
        const float ExpandedHeight = 330f;
        const float CollapsedHeight = 42f;

        BlockWorld _world;
        BlockMapEditorTool _editor;
        Rect _panelRect;
        Vector2 _groupScroll;
        string _activeGroup = string.Empty;
        string _message = string.Empty;

        public bool LinkMode { get; private set; }

        void Awake()
        {
            _world = GetComponent<BlockWorld>();
            _editor = GetComponent<BlockMapEditorTool>();
            UpdatePanelRect();
        }

        void Update()
        {
            EnsureReferences();
            if (_editor == null || !_editor.IsEditing) return;

            if (Input.GetKeyDown(KeyCode.L)) SetLinkMode(!LinkMode);
            if (!LinkMode) return;
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                SetLinkMode(false);
                return;
            }

            if (!Input.GetMouseButtonDown(0) || IsPointerOverAnyPanel()) return;
            if (Input.GetMouseButton(1) || Input.GetMouseButton(2) || Input.GetKey(KeyCode.LeftAlt)) return;
            if (!_editor.TryGetHoveredOccupiedCell(out Vector3Int cell)) return;

            bool unlink = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (unlink) Unlink(cell);
            else Link(cell);
        }

        public bool IsPointerOverPanel(Vector2 guiMouse)
        {
            UpdatePanelRect();
            return _editor != null && _editor.IsEditing && _panelRect.Contains(guiMouse);
        }

        void SetLinkMode(bool enabled)
        {
            LinkMode = enabled;
            _message = enabled ? "先点机关创建组，再点门或其他机关加入该组" : string.Empty;
            _editor?.ShowMessage(enabled ? "进入机关关联模式（Esc 或 L 退出）" : "退出机关关联模式");
        }

        void Link(Vector3Int cell)
        {
            if (!TryGetLinkableKind(cell, out GameplayBlockKind kind, out string displayName))
            {
                _message = "这里只能关联按钮、拉杆、光接收器和门";
                return;
            }

            string existing = _world.GetLinkId(cell);
            if (_activeGroup.Length == 0)
            {
                if (existing.Length > 0)
                {
                    SelectGroup(existing);
                    return;
                }

                if (!BlockLinkRules.IsSignalSource(kind))
                {
                    _message = "请先点击一个机关，再点击需要控制的门";
                    return;
                }

                _activeGroup = CreateNextGroupId();
            }

            _world.BeginStroke();
            bool changed = _world.SetLinkId(cell, _activeGroup);
            _world.EndStroke();
            _message = changed
                ? $"{displayName} 已加入 {_activeGroup}"
                : $"{displayName} 已属于 {_activeGroup}";
        }

        void Unlink(Vector3Int cell)
        {
            string existing = _world.GetLinkId(cell);
            if (existing.Length == 0)
            {
                _message = "该物件没有实例关联";
                return;
            }

            _world.BeginStroke();
            _world.SetLinkId(cell, string.Empty);
            _world.EndStroke();
            _message = $"已解除 {existing}";
        }

        bool TryGetLinkableKind(Vector3Int cell, out GameplayBlockKind kind, out string displayName)
        {
            BlockDefinition definition = BlockLibrary.Get(_world.GetIndex(cell));
            GameplayBlockProfile profile = definition != null ? BlockGameplayCatalog.Get(definition.id) : null;
            kind = profile != null ? profile.Kind : GameplayBlockKind.None;
            displayName = definition != null ? definition.displayName : "物件";
            return profile != null && BlockLinkRules.IsLinkable(kind);
        }

        void SelectGroup(string groupId)
        {
            _activeGroup = groupId ?? string.Empty;
            _message = _activeGroup.Length > 0 ? $"当前组：{_activeGroup}" : "未选择关联组";
        }

        string CreateNextGroupId()
        {
            var used = new HashSet<string>(CollectGroups());
            for (int number = 1; number < 10000; number++)
            {
                string candidate = $"G{number:000}";
                if (!used.Contains(candidate)) return candidate;
            }
            return $"G{used.Count + 1:00000}";
        }

        List<string> CollectGroups()
        {
            var groups = new HashSet<string>();
            if (_world != null)
            {
                foreach (KeyValuePair<Vector3Int, int> block in _world.Blocks)
                {
                    string linkId = _world.GetLinkId(block.Key);
                    if (linkId.Length > 0) groups.Add(linkId);
                }
            }

            var result = new List<string>(groups);
            result.Sort(System.StringComparer.Ordinal);
            return result;
        }

        void OnGUI()
        {
            EnsureReferences();
            if (_editor == null || !_editor.IsEditing) return;

            UpdatePanelRect();
            GUILayout.BeginArea(_panelRect, GUI.skin.box);
            if (!LinkMode)
            {
                if (GUILayout.Button("机关关联 (L)")) SetLinkMode(true);
                GUILayout.EndArea();
                return;
            }

            DrawPanel();
            GUILayout.EndArea();
            DrawWorldLabels();
        }

        void DrawPanel()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>机关关联</b>", new GUIStyle(GUI.skin.label) { richText = true });
            if (GUILayout.Button("退出", GUILayout.Width(54f))) SetLinkMode(false);
            GUILayout.EndHorizontal();

            GUILayout.Label("左键加入当前组；Shift+左键解除");
            GUILayout.Label("当前组：" + (_activeGroup.Length > 0 ? _activeGroup : "未选择"));

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("新建组")) SelectGroup(CreateNextGroupId());
            if (GUILayout.Button("取消选择")) SelectGroup(string.Empty);
            GUILayout.EndHorizontal();

            List<string> groups = CollectGroups();
            GUILayout.Label($"已有分组（{groups.Count}）");
            _groupScroll = GUILayout.BeginScrollView(_groupScroll, GUILayout.Height(145f));
            foreach (string group in groups)
            {
                Color previous = GUI.backgroundColor;
                GUI.backgroundColor = BlockLinkRules.ColorFor(group);
                string prefix = group == _activeGroup ? "▶ " : "   ";
                if (GUILayout.Button(prefix + GroupSummary(group))) SelectGroup(group);
                GUI.backgroundColor = previous;
            }
            GUILayout.EndScrollView();

            GUILayout.Label(_message, new GUIStyle(GUI.skin.label) { wordWrap = true });
            GUILayout.Label("保存地图时 linkId 会随每个物件写入 JSON");
        }

        string GroupSummary(string groupId)
        {
            int sources = 0;
            int targets = 0;
            foreach (KeyValuePair<Vector3Int, int> block in _world.Blocks)
            {
                if (_world.GetLinkId(block.Key) != groupId) continue;
                if (!TryGetLinkableKind(block.Key, out GameplayBlockKind kind, out _)) continue;
                if (BlockLinkRules.IsSignalSource(kind)) sources++;
                if (BlockLinkRules.IsSignalTarget(kind)) targets++;
            }
            return $"{groupId}  机关 {sources} / 门 {targets}";
        }

        void DrawWorldLabels()
        {
            Camera camera = Camera.main;
            if (camera == null || _world == null) return;

            GUIStyle style = new GUIStyle(GUI.skin.box)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                fontSize = 12
            };

            foreach (KeyValuePair<Vector3Int, int> block in _world.Blocks)
            {
                string linkId = _world.GetLinkId(block.Key);
                if (linkId.Length == 0) continue;

                Vector3 worldPosition = block.Key + new Vector3(0.5f, 1.35f, 0.5f);
                Vector3 screenPosition = camera.WorldToScreenPoint(worldPosition);
                if (screenPosition.z <= 0f) continue;

                Rect labelRect = new Rect(screenPosition.x - 29f, Screen.height - screenPosition.y - 12f, 58f, 24f);
                Color previous = GUI.backgroundColor;
                GUI.backgroundColor = BlockLinkRules.ColorFor(linkId);
                GUI.Box(labelRect, linkId, style);
                GUI.backgroundColor = previous;
            }
        }

        bool IsPointerOverAnyPanel()
        {
            Vector2 guiMouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            return IsPointerOverPanel(guiMouse) || (_editor != null && _editor.IsPointerOverPanel());
        }

        void UpdatePanelRect()
        {
            float height = LinkMode ? ExpandedHeight : CollapsedHeight;
            _panelRect = new Rect(Mathf.Max(8f, Screen.width - PanelWidth - 8f), 8f, PanelWidth, height);
        }

        void EnsureReferences()
        {
            if (_world == null) _world = GetComponent<BlockWorld>();
            if (_editor == null) _editor = GetComponent<BlockMapEditorTool>();
        }
    }
}
