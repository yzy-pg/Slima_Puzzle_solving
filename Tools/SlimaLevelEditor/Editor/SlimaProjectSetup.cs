using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SlimaPrototype.Editor
{
    public static class SlimaProjectSetup
    {
        public const string PrototypeScenePath = "Assets/_Game/Scenes/Prototype/SlimaPrototype.unity";
        public const string PrototypeLevelPath = "Assets/_Game/Data/Levels/Prototype_01.json";
        public const string GlobalConfigPath = "Assets/_Game/Data/Configs/GlobalGameplayConfig.json";

        [MenuItem("Tools/Slima/打开原型场景")]
        public static void OpenPrototypeScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(PrototypeScenePath) == null)
            {
                CreateOrRepairPrototypeScene();
            }
            EditorSceneManager.OpenScene(PrototypeScenePath);
        }

        [MenuItem("Tools/Slima/创建或修复原型场景")]
        public static void CreateOrRepairPrototypeScene()
        {
            System.IO.Directory.CreateDirectory("Assets/_Game/Scenes/Prototype");
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            GameObject root = new GameObject("SlimaPrototype");
            SlimaGameBootstrap bootstrap = root.AddComponent<SlimaGameBootstrap>();
            bootstrap.LevelJson = AssetDatabase.LoadAssetAtPath<TextAsset>(PrototypeLevelPath);
            bootstrap.GlobalConfigJson = AssetDatabase.LoadAssetAtPath<TextAsset>(GlobalConfigPath);
            EditorSceneManager.SaveScene(scene, PrototypeScenePath);

            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(PrototypeScenePath, true)
            };
            AssetDatabase.SaveAssets();
            Debug.Log("[Slima] 原型场景已创建并加入 Build Settings：" + PrototypeScenePath);
        }

        public static void CreatePrototypeSceneBatch()
        {
            CreateOrRepairPrototypeScene();
        }

        [MenuItem("Tools/Slima/选中全局配置")]
        public static void SelectGlobalConfig()
        {
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<TextAsset>(GlobalConfigPath);
            EditorGUIUtility.PingObject(Selection.activeObject);
        }

        [MenuItem("Tools/Slima/验证所有关卡")]
        public static void ValidateAllLevels()
        {
            string[] guids = AssetDatabase.FindAssets("t:TextAsset", new[] { "Assets/_Game/Data/Levels" });
            int errors = 0;
            int warnings = 0;

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                try
                {
                    var issues = SlimaLevelValidator.Validate(SlimaLevelJson.FromTextAsset(asset));
                    errors += issues.Count(item => item.severity == SlimaIssueSeverity.Error);
                    warnings += issues.Count(item => item.severity == SlimaIssueSeverity.Warning);
                    for (int issueIndex = 0; issueIndex < issues.Count; issueIndex++)
                    {
                        Debug.Log($"[Slima][{path}] {issues[issueIndex]}", asset);
                    }
                }
                catch (System.Exception exception)
                {
                    errors++;
                    Debug.LogException(exception, asset);
                }
            }

            EditorUtility.DisplayDialog("Slima 关卡验证", $"检查 {guids.Length} 个关卡\n错误 {errors} 个\n警告 {warnings} 个", "确定");
        }
    }
}
