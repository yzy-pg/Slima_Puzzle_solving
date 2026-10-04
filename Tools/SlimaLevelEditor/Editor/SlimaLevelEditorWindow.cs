using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SlimaPrototype.Editor
{
    public sealed class SlimaLevelEditorWindow : EditorWindow
    {
        private const string DefaultLevelFolder = "Assets/_Game/Data/Levels";
        private const string PrototypeScenePath = "Assets/_Game/Scenes/Prototype/SlimaPrototype.unity";
        private const string GlobalConfigPath = "Assets/_Game/Data/Configs/GlobalGameplayConfig.json";

        private enum BrushMode
        {
            Select,
            Terrain,
            Entity,
            Erase
        }

        private TextAsset sourceAsset;
        private SlimaLevelData level;
        private BrushMode brushMode = BrushMode.Select;
        private SlimaTerrainType terrainBrush = SlimaTerrainType.Wall;
        private SlimaEntityType entityBrush = SlimaEntityType.PaintStation;
        private SlimaRecipe recipeBrush = SlimaRecipe.Red;
        private Vector2 scroll;
        private Vector2 issueScroll;
        private Vector2Int selectedCell = new Vector2Int(-1, -1);
        private int selectedEntityIndex = -1;
        private List<SlimaLevelIssue> issues = new List<SlimaLevelIssue>();
        private bool dirty;
        private SlimaGlobalGameplayConfig globalConfig = SlimaGlobalGameplayConfig.CreateDefault();

        [MenuItem("Tools/Slima/关卡编辑器")]
        public static void Open()
        {
            var window = GetWindow<SlimaLevelEditorWindow>("Slima 关卡编辑器");
            window.minSize = new Vector2(980f, 640f);
            window.Show();
        }

        private void OnEnable()
        {
            TextAsset configAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(GlobalConfigPath);
            if (configAsset != null)
            {
                try { globalConfig = SlimaGlobalConfigJson.FromTextAsset(configAsset); }
                catch (System.Exception exception) { Debug.LogException(exception); }
            }

            if (sourceAsset == null)
            {
                string[] guids = AssetDatabase.FindAssets("t:TextAsset", new[] { DefaultLevelFolder });
                if (guids.Length > 0)
                {
                    sourceAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(AssetDatabase.GUIDToAssetPath(guids[0]));
                }
            }

            if (sourceAsset != null) LoadSelectedAsset();
        }

        private void OnGUI()
        {
            DrawToolbar();
            if (level == null)
            {
                EditorGUILayout.HelpBox("请选择关卡 JSON 或新建关卡。", MessageType.Info);
                return;
            }

            EditorGUILayout.BeginHorizontal();
            DrawPalette();
            DrawGrid();
            DrawProperties();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            TextAsset newAsset = (TextAsset)EditorGUILayout.ObjectField(sourceAsset, typeof(TextAsset), false, GUILayout.Width(300f));
            if (newAsset != sourceAsset)
            {
                if (ConfirmDiscard())
                {
                    sourceAsset = newAsset;
                    LoadSelectedAsset();
                }
            }

            if (GUILayout.Button("新建", EditorStyles.toolbarButton, GUILayout.Width(54f))) CreateLevel();
            GUI.enabled = level != null;
            if (GUILayout.Button(dirty ? "保存 *" : "保存", EditorStyles.toolbarButton, GUILayout.Width(62f))) Save();
            if (GUILayout.Button("重新读取", EditorStyles.toolbarButton, GUILayout.Width(72f)) && ConfirmDiscard()) LoadSelectedAsset();
            if (GUILayout.Button("验证", EditorStyles.toolbarButton, GUILayout.Width(54f))) ValidateLevel(true);
            if (GUILayout.Button("保存并试玩", EditorStyles.toolbarButton, GUILayout.Width(90f))) SaveAndPlay();
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            if (level != null) GUILayout.Label($"{level.width} × {level.height}", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawPalette()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(205f));
            EditorGUILayout.LabelField("绘制工具", EditorStyles.boldLabel);
            brushMode = (BrushMode)GUILayout.Toolbar((int)brushMode, new[] { "选择", "地块", "实体", "擦除" }, GUILayout.Height(42f));

            EditorGUILayout.Space(8f);
            if (brushMode == BrushMode.Terrain)
            {
                terrainBrush = (SlimaTerrainType)EditorGUILayout.EnumPopup("地块", terrainBrush);
            }
            else if (brushMode == BrushMode.Entity)
            {
                entityBrush = (SlimaEntityType)EditorGUILayout.EnumPopup("实体", entityBrush);
                recipeBrush = (SlimaRecipe)EditorGUILayout.EnumFlagsField("颜色成分", recipeBrush);
            }

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("关卡", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            level.levelId = EditorGUILayout.TextField("ID", level.levelId);
            level.displayName = EditorGUILayout.TextField("名称", level.displayName);
            level.width = EditorGUILayout.IntSlider("宽", level.width, 2, 32);
            level.height = EditorGUILayout.IntSlider("高", level.height, 2, 32);
            if (EditorGUI.EndChangeCheck()) MarkDirty();

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("临时规则参数", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("未勾选的项目继承全局配置", EditorStyles.miniLabel);
            EditorGUI.BeginChangeCheck();
            level.rules.winCondition = (SlimaWinCondition)EditorGUILayout.EnumPopup("胜利条件", level.rules.winCondition);
            level.rules.initialGel = DrawIntOverride("初始凝胶", level.rules.initialGel, globalConfig.gel.initialGel);
            level.rules.maxGel = DrawIntOverride("凝胶上限", level.rules.maxGel, globalConfig.gel.maximumGel);
            level.rules.baseMoveCost = DrawIntOverride("每格消耗", level.rules.baseMoveCost, globalConfig.gel.moveCostPerCell);
            level.rules.environmentDamage = DrawIntOverride("环境伤害", level.rules.environmentDamage, globalConfig.gel.environmentDamage);
            level.rules.splitThreshold = DrawIntOverride("分裂阈值", level.rules.splitThreshold, globalConfig.gel.splitMinimumGel);
            level.rules.buttonDurationSteps = DrawIntOverride("按钮步数", level.rules.buttonDurationSteps, globalConfig.mechanisms.buttonDurationSteps);
            level.rules.transferAmount = DrawIntOverride("传输量", level.rules.transferAmount, globalConfig.mechanisms.transferAmount);
            level.rules.maxSlimes = DrawIntOverride("史莱姆上限", level.rules.maxSlimes, globalConfig.system.maximumSlimes);
            level.rules.capGelAtMax = DrawBoolOverride("限制凝胶上限", level.rules.capGelAtMax, globalConfig.system.capGelAtMaximum);
            level.rules.anySlimeZeroFails = DrawBoolOverride("任一归零失败", level.rules.anySlimeZeroFails, globalConfig.system.anySlimeZeroFails);
            if (EditorGUI.EndChangeCheck()) MarkDirty();

            EditorGUILayout.Space(10f);
            EditorGUILayout.LabelField("验证结果", EditorStyles.boldLabel);
            issueScroll = EditorGUILayout.BeginScrollView(issueScroll, GUILayout.Height(180f));
            if (issues.Count == 0) EditorGUILayout.LabelField("尚未验证或没有问题", EditorStyles.wordWrappedMiniLabel);
            for (int i = 0; i < issues.Count; i++)
            {
                MessageType type = issues[i].severity == SlimaIssueSeverity.Error ? MessageType.Error : MessageType.Warning;
                EditorGUILayout.HelpBox(issues[i].message, type);
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawGrid()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            EditorGUILayout.LabelField("地图网格  北方向在上", EditorStyles.boldLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll, true, true);

            for (int z = level.height - 1; z >= 0; z--)
            {
                EditorGUILayout.BeginHorizontal();
                for (int x = 0; x < level.width; x++)
                {
                    Vector2Int cell = new Vector2Int(x, z);
                    SlimaEntityData entity = FirstEntityAt(cell, out _);
                    Color previous = GUI.backgroundColor;
                    GUI.backgroundColor = CellColor(cell, entity);
                    string label = entity != null ? EntityCode(entity.type) : TerrainCode(level.GetTerrain(cell));
                    if (selectedCell == cell) label = "[" + label + "]";
                    if (GUILayout.Button(label, GUILayout.Width(34f), GUILayout.Height(30f))) ApplyBrush(cell);
                    GUI.backgroundColor = previous;
                }
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawProperties()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(245f));
            EditorGUILayout.LabelField("格子属性", EditorStyles.boldLabel);
            if (selectedCell.x < 0)
            {
                EditorGUILayout.HelpBox("使用选择工具点击格子。", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.LabelField("坐标", $"{selectedCell.x}, {selectedCell.y}");
            EditorGUI.BeginChangeCheck();
            SlimaTerrainType terrain = (SlimaTerrainType)EditorGUILayout.EnumPopup("地块", level.GetTerrain(selectedCell));
            if (EditorGUI.EndChangeCheck())
            {
                level.SetTerrain(selectedCell, terrain);
                MarkDirty();
            }

            SlimaEntityData entity = SelectedEntity();
            if (entity == null)
            {
                EditorGUILayout.HelpBox("该格没有实体。", MessageType.None);
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("实体", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            entity.instanceId = EditorGUILayout.TextField("实例 ID", entity.instanceId);
            entity.type = (SlimaEntityType)EditorGUILayout.EnumPopup("类型", entity.type);
            entity.direction = EditorGUILayout.IntSlider("方向", entity.direction, 0, 3);
            entity.recipe = (SlimaRecipe)EditorGUILayout.EnumFlagsField("颜色成分", entity.recipe);
            entity.linkId = EditorGUILayout.TextField("连接 ID", entity.linkId);
            entity.value = EditorGUILayout.IntField("数值", entity.value);
            if (EditorGUI.EndChangeCheck()) MarkDirty();

            EditorGUILayout.Space(6f);
            if (GUILayout.Button("删除该实体"))
            {
                level.entities.Remove(entity);
                selectedEntityIndex = -1;
                MarkDirty();
            }
            EditorGUILayout.EndVertical();
        }

        private void ApplyBrush(Vector2Int cell)
        {
            selectedCell = cell;
            selectedEntityIndex = -1;

            switch (brushMode)
            {
                case BrushMode.Select:
                    FirstEntityAt(cell, out selectedEntityIndex);
                    break;
                case BrushMode.Terrain:
                    level.SetTerrain(cell, terrainBrush);
                    MarkDirty();
                    break;
                case BrushMode.Entity:
                    if (entityBrush == SlimaEntityType.Spawn)
                    {
                        level.entities.RemoveAll(item => item.type == SlimaEntityType.Spawn);
                    }
                    level.entities.RemoveAll(item => item.x == cell.x && item.z == cell.y);
                    var entity = NewEntity(entityBrush, cell);
                    level.entities.Add(entity);
                    selectedEntityIndex = level.entities.Count - 1;
                    MarkDirty();
                    break;
                case BrushMode.Erase:
                    level.SetTerrain(cell, SlimaTerrainType.Floor);
                    level.entities.RemoveAll(item => item.x == cell.x && item.z == cell.y);
                    MarkDirty();
                    break;
            }

            Repaint();
        }

        private SlimaEntityData NewEntity(SlimaEntityType type, Vector2Int cell)
        {
            string id = type.ToString().ToLowerInvariant();
            int suffix = 1;
            string candidate = id;
            while (level.entities.Exists(item => item.instanceId == candidate)) candidate = id + "_" + suffix++;
            return new SlimaEntityData
            {
                instanceId = candidate,
                type = type,
                x = cell.x,
                z = cell.y,
                recipe = recipeBrush,
                direction = 0,
                value = type == SlimaEntityType.PressurePlate ? 50 : 0
            };
        }

        private void CreateLevel()
        {
            if (!ConfirmDiscard()) return;
            string path = EditorUtility.SaveFilePanelInProject("新建 Slima 关卡", "PrototypeLevel", "json", "选择关卡保存位置", DefaultLevelFolder);
            if (string.IsNullOrEmpty(path)) return;
            level = SlimaLevelData.CreatePrototype();
            WriteLevel(path);
            sourceAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            dirty = false;
            ValidateLevel(false);
        }

        private void LoadSelectedAsset()
        {
            selectedCell = new Vector2Int(-1, -1);
            selectedEntityIndex = -1;
            issues.Clear();
            dirty = false;
            if (sourceAsset == null)
            {
                level = null;
                return;
            }

            try
            {
                level = SlimaLevelJson.FromTextAsset(sourceAsset);
                ValidateLevel(false);
            }
            catch (System.Exception exception)
            {
                level = null;
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Slima", "关卡读取失败：\n" + exception.Message, "确定");
            }
        }

        private void Save()
        {
            if (level == null) return;
            string path = sourceAsset != null ? AssetDatabase.GetAssetPath(sourceAsset) : "";
            if (string.IsNullOrEmpty(path))
            {
                path = EditorUtility.SaveFilePanelInProject("保存 Slima 关卡", level.levelId, "json", "选择关卡保存位置", DefaultLevelFolder);
                if (string.IsNullOrEmpty(path)) return;
            }

            WriteLevel(path);
            sourceAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            dirty = false;
            ValidateLevel(false);
        }

        private void WriteLevel(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
            string absolutePath = Path.Combine(projectRoot, assetPath);
            Directory.CreateDirectory(Path.GetDirectoryName(absolutePath) ?? projectRoot);
            File.WriteAllText(absolutePath, SlimaLevelJson.ToJson(level), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.Refresh();
        }

        private void SaveAndPlay()
        {
            Save();
            if (sourceAsset == null || level == null) return;
            if (ValidateLevel(true)) return;

            SlimaProjectSetup.OpenPrototypeScene();
            SlimaGameBootstrap bootstrap = Object.FindObjectOfType<SlimaGameBootstrap>();
            if (bootstrap == null)
            {
                EditorUtility.DisplayDialog("Slima", "试玩场景缺少 SlimaGameBootstrap。", "确定");
                return;
            }

            bootstrap.LevelJson = sourceAsset;
            EditorUtility.SetDirty(bootstrap);
            EditorSceneManager.MarkSceneDirty(bootstrap.gameObject.scene);
            EditorSceneManager.SaveOpenScenes();
            EditorApplication.isPlaying = true;
        }

        private bool ValidateLevel(bool showDialog)
        {
            issues = SlimaLevelValidator.Validate(level);
            int errors = issues.FindAll(item => item.severity == SlimaIssueSeverity.Error).Count;
            int warnings = issues.Count - errors;
            if (showDialog)
            {
                EditorUtility.DisplayDialog("Slima 关卡验证", $"错误 {errors} 个\n警告 {warnings} 个", "确定");
            }
            Repaint();
            return errors > 0;
        }

        private bool ConfirmDiscard()
        {
            return !dirty || EditorUtility.DisplayDialog("Slima", "当前关卡有未保存修改，确定放弃吗？", "放弃", "取消");
        }

        private void MarkDirty()
        {
            dirty = true;
            issues.Clear();
        }

        private static int DrawIntOverride(string label, int overrideValue, int globalValue)
        {
            bool enabled = overrideValue >= 0;
            EditorGUILayout.BeginHorizontal();
            bool nextEnabled = EditorGUILayout.ToggleLeft("覆盖 " + label, enabled, GUILayout.Width(105f));
            int shownValue = enabled ? overrideValue : globalValue;
            using (new EditorGUI.DisabledScope(!nextEnabled))
            {
                shownValue = EditorGUILayout.IntField(shownValue);
            }
            EditorGUILayout.EndHorizontal();
            return nextEnabled ? shownValue : -1;
        }

        private static int DrawBoolOverride(string label, int overrideValue, bool globalValue)
        {
            bool enabled = overrideValue >= 0;
            EditorGUILayout.BeginHorizontal();
            bool nextEnabled = EditorGUILayout.ToggleLeft("覆盖 " + label, enabled, GUILayout.Width(105f));
            bool shownValue = enabled ? overrideValue != 0 : globalValue;
            using (new EditorGUI.DisabledScope(!nextEnabled))
            {
                shownValue = EditorGUILayout.Toggle(shownValue);
            }
            EditorGUILayout.EndHorizontal();
            return nextEnabled ? (shownValue ? 1 : 0) : -1;
        }

        private SlimaEntityData SelectedEntity()
        {
            return selectedEntityIndex >= 0 && selectedEntityIndex < level.entities.Count
                ? level.entities[selectedEntityIndex]
                : null;
        }

        private SlimaEntityData FirstEntityAt(Vector2Int cell, out int index)
        {
            for (int i = 0; i < level.entities.Count; i++)
            {
                if (level.entities[i].x == cell.x && level.entities[i].z == cell.y)
                {
                    index = i;
                    return level.entities[i];
                }
            }
            index = -1;
            return null;
        }

        private Color CellColor(Vector2Int cell, SlimaEntityData entity)
        {
            if (entity != null)
            {
                if (entity.recipe != SlimaRecipe.None) return SlimaPrototypeView.RecipeColor(entity.recipe);
                switch (entity.type)
                {
                    case SlimaEntityType.Spawn: return new Color(0.2f, 0.8f, 0.95f);
                    case SlimaEntityType.Exit: return new Color(1f, 0.35f, 0.18f);
                    case SlimaEntityType.Door: return new Color(0.9f, 0.3f, 0.2f);
                    case SlimaEntityType.PressurePlate: return new Color(0.9f, 0.75f, 0.15f);
                    default: return new Color(0.65f, 0.65f, 0.72f);
                }
            }

            switch (level.GetTerrain(cell))
            {
                case SlimaTerrainType.Wall: return new Color(0.3f, 0.33f, 0.38f);
                case SlimaTerrainType.Acid: return new Color(0.5f, 0.95f, 0.2f);
                case SlimaTerrainType.Heat: return new Color(1f, 0.35f, 0.1f);
                case SlimaTerrainType.Cold: return new Color(0.2f, 0.7f, 1f);
                default: return new Color(0.75f, 0.78f, 0.82f);
            }
        }

        private static string TerrainCode(SlimaTerrainType type)
        {
            switch (type)
            {
                case SlimaTerrainType.Wall: return "墙";
                case SlimaTerrainType.Acid: return "酸";
                case SlimaTerrainType.Heat: return "热";
                case SlimaTerrainType.Cold: return "冷";
                default: return "·";
            }
        }

        private static string EntityCode(SlimaEntityType type)
        {
            switch (type)
            {
                case SlimaEntityType.Spawn: return "生";
                case SlimaEntityType.PaintStation: return "染";
                case SlimaEntityType.Splitter: return "分";
                case SlimaEntityType.LightEmitter: return "光";
                case SlimaEntityType.LightReceiver: return "收";
                case SlimaEntityType.PressurePlate: return "压";
                case SlimaEntityType.Button: return "钮";
                case SlimaEntityType.Lever: return "杆";
                case SlimaEntityType.Door: return "门";
                case SlimaEntityType.GelPotion: return "液";
                case SlimaEntityType.GelPill: return "丸";
                case SlimaEntityType.TransferPipe: return "管";
                case SlimaEntityType.LaserSplitter: return "激";
                case SlimaEntityType.CatapultSplitter: return "弹";
                case SlimaEntityType.Exit: return "终";
                default: return "?";
            }
        }
    }
}
