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
        const float ExpandedHeight = 370f;
        const float CollapsedHeight = 42f;

        BlockWorld _world;
        BlockMapEditorTool _editor;
        Rect _panelRect;
        Vector2 _groupScroll;
        string _activeGroup = string.Empty;
        string _message = string.Empty;
        readonly BlockMechanismLabels _mechanismLabels = new BlockMechanismLabels();
        bool _colorMode;
        bool _hasColorSelection;
        Vector3Int _colorCell;
        Color _selectedColor = Color.gray;
        readonly string[] _rgbText = { "128", "128", "128" };

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
            if (_colorMode) { SelectColorCell(cell); return; }

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
            if (enabled) _colorMode = false;
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
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("机关关联 (L)")) SetLinkMode(true);
                if (GUILayout.Button("RGB 颜色")) { SetLinkMode(true); _colorMode = true; }
                GUILayout.EndHorizontal();
                GUILayout.EndArea();
                return;
            }

            _colorMode = GUILayout.Toolbar(_colorMode ? 1 : 0, new[] { "机关关联", "RGB 颜色" }) == 1;
            if (_colorMode) DrawColorPanel();
            else DrawPanel();
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
            GUILayout.Label("机关独立编号；门显示同组机关的全部编号");
            GUILayout.Label("保存地图时关联组与编号一起写入 JSON");
        }

        void SelectColorCell(Vector3Int cell)
        {
            if (!TryGetLinkableKind(cell, out var kind, out _) || !BlockLinkVisuals.CanEditColor(kind))
            { _message = "请选择门、压力板、按钮或拉杆"; return; }
            _colorCell = cell; _hasColorSelection = true;
            _selectedColor = _world.TryGetMechanismColor(cell, out var saved) ? saved : Color.gray;
            foreach (var view in _world.GetComponentsInChildren<GameplayBlockView>())
            {
                if (view.Cell != cell) continue;
                var control = view.GetComponentInChildren<GameplayControlArt>();
                var renderer = control != null ? control.colorParts[0] : view.GetComponentInChildren<Renderer>();
                if (renderer == null) break;
                var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
                if (block.GetFloat("_LinkColorCount") > 0) _selectedColor = block.GetColor("_BaseColor");
                break;
            }
            for (int i = 0; i < 3; i++) _rgbText[i] = Mathf.RoundToInt(_selectedColor[i] * 255f).ToString();
            _message = string.Empty;
        }

        void DrawColorPanel()
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b>机关 RGB 颜色</b>", new GUIStyle(GUI.skin.label) { richText = true });
            if (GUILayout.Button("退出", GUILayout.Width(54f))) SetLinkMode(false);
            GUILayout.EndHorizontal();
            GUILayout.Label("左键点击地图中的物件来选择");
            string name = string.Empty;
            GameplayBlockKind kind = GameplayBlockKind.None;
            bool selected = _hasColorSelection && TryGetLinkableKind(_colorCell, out kind, out name) && BlockLinkVisuals.CanEditColor(kind);
            GUILayout.Label(selected ? name + "  " + _colorCell : "尚未选择门或机关");
            var swatch = GUILayoutUtility.GetRect(1f, 30f, GUILayout.ExpandWidth(true));
            Color oldColor = GUI.color; GUI.color = _selectedColor;
            GUI.DrawTexture(swatch, Texture2D.whiteTexture); GUI.color = oldColor;
            for (int i = 0; i < 3; i++) DrawRgbChannel(i);
            GUILayout.Label("#" + ColorUtility.ToHtmlStringRGB(_selectedColor));
            bool oldEnabled = GUI.enabled; GUI.enabled = selected;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("应用颜色"))
            {
                BlockLinkVisuals.SetEditorColor(_world, _colorCell, _selectedColor);
                _message = "颜色已保存到地图；F5 保存文件";
            }
            if (GUILayout.Button("恢复默认"))
            {
                BlockLinkVisuals.SetEditorColor(_world, _colorCell, null);
                SelectColorCell(_colorCell); _message = "已恢复默认颜色";
            }
            GUILayout.EndHorizontal(); GUI.enabled = oldEnabled;
            var wrap = new GUIStyle(GUI.skin.label) { wordWrap = true };
            GUILayout.Label("门换色会同步关联机关；单独修改机关颜色，门自动显示对应色带。", wrap);
            GUILayout.Label("支持 Ctrl+Z / Ctrl+Y 撤销与重做；颜色随地图保存。", wrap);
            GUILayout.Label(_message, wrap);
        }

        void DrawRgbChannel(int index)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(new[] { "R", "G", "B" }[index], GUILayout.Width(18f));
            int previous = Mathf.RoundToInt(_selectedColor[index] * 255f);
            int value = Mathf.RoundToInt(GUILayout.HorizontalSlider(previous, 0, 255));
            if (value != previous) _rgbText[index] = value.ToString();
            _rgbText[index] = GUILayout.TextField(_rgbText[index], 3, GUILayout.Width(42f));
            if (int.TryParse(_rgbText[index], out int entered)) value = Mathf.Clamp(entered, 0, 255);
            _selectedColor[index] = value / 255f;
            GUILayout.EndHorizontal();
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
            if (_world == null) return;
            _mechanismLabels.Refresh(_world);
            GameplayOverlayDrawing.DrawMechanismLabels(_mechanismLabels.Labels);
        }

        bool IsPointerOverAnyPanel()
        {
            Vector2 guiMouse = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            return IsPointerOverPanel(guiMouse) || (_editor != null && _editor.IsPointerOverPanel());
        }

        void UpdatePanelRect()
        {
            float height = LinkMode ? (_colorMode ? 395f : ExpandedHeight) : CollapsedHeight;
            _panelRect = new Rect(Mathf.Max(8f, Screen.width - PanelWidth - 8f), 8f, PanelWidth, height);
        }

        void EnsureReferences()
        {
            if (_world == null) _world = GetComponent<BlockWorld>();
            if (_editor == null) _editor = GetComponent<BlockMapEditorTool>();
        }
    }
}
