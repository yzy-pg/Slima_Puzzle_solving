using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SlimaPuzzle.EditorTools
{
    public static class SlimaFrontEndSetup
    {
        [MenuItem("Tools/Slima/主菜单/创建主菜单并配置启动场景")]
        public static void Setup()
        {
            if (!File.Exists(SlimaFrontEnd.MenuScene))
            {
                Directory.CreateDirectory("Assets/_Game/Scenes");
                Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                    Application.isBatchMode ? NewSceneMode.Single : NewSceneMode.Additive);
                var root = new GameObject("Slima Main Menu");
                SceneManager.MoveGameObjectToScene(root, scene);
                root.AddComponent<SlimaFrontEnd>();
                var cameraObject = new GameObject("Main Camera");
                SceneManager.MoveGameObjectToScene(cameraObject, scene);
                cameraObject.tag = "MainCamera";
                Camera camera = cameraObject.AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.035f, 0.06f, 0.095f);
                cameraObject.AddComponent<AudioListener>();
                EditorSceneManager.SaveScene(scene, SlimaFrontEnd.MenuScene);
                if (!Application.isBatchMode) EditorSceneManager.CloseScene(scene, true);
            }

            var scenes = new List<EditorBuildSettingsScene>
            {
                new EditorBuildSettingsScene(SlimaFrontEnd.MenuScene, true)
            };
            string[] levels = SlimaFrontEnd.DefaultLevelScenes;
            foreach (string path in levels)
                if (!string.IsNullOrEmpty(path)) scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.Refresh();
            Debug.Log($"[Slima] 主菜单已创建，启动场景与 {scenes.Count - 1} 个关卡已加入 Build Settings。");
        }

        [MenuItem("Tools/Slima/主菜单/打开主菜单")]
        public static void OpenMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            Setup();
            EditorSceneManager.OpenScene(SlimaFrontEnd.MenuScene);
        }

        // Runs in batch mode so validation never changes a scene open in the user's editor.
        public static void Validate()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use Unity batch mode for validation.");
            Setup();
            string directory = Path.Combine(Path.GetTempPath(), "SlimaSaveValidation-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                for (int i = 0; i < SlimaSaveStore.SlotCount; i++)
                {
                    if (SlimaSaveStore.Read(i, directory) != null) throw new Exception("New slot was not empty.");
                    SlimaSaveStore.Write(i, new SlimaSave { scenePath = $"Assets/Scenes/map0{i + 1}.unity" }, directory);
                }
                SlimaSaveStore.Write(1, new SlimaSave { scenePath = "Assets/Scenes/map03.unity" }, directory);
                if (SlimaSaveStore.Read(0, directory).scenePath != "Assets/Scenes/map01.unity" ||
                    SlimaSaveStore.Read(1, directory).scenePath != "Assets/Scenes/map03.unity" ||
                    SlimaSaveStore.Read(2, directory).scenePath != "Assets/Scenes/map03.unity")
                    throw new Exception("Saving one slot changed another slot.");
                File.WriteAllText(SlimaSaveStore.SlotPath(0, directory), "{broken json");
                bool rejected = false;
                try { SlimaSaveStore.Read(0, directory); }
                catch (Exception error) when (error is ArgumentException || error is IOException || error is InvalidDataException)
                { rejected = true; }
                if (!rejected) throw new Exception("Corrupt slot was accepted.");
                File.WriteAllText(SlimaSaveStore.SlotPath(0, directory),
                    JsonUtility.ToJson(new SlimaSave { version = 99, scenePath = "Assets/Scenes/map01.unity", savedUtc = DateTime.UtcNow.ToString("O") }));
                rejected = false;
                try { SlimaSaveStore.Read(0, directory); }
                catch (InvalidDataException) { rejected = true; }
                if (!rejected) throw new Exception("Unsupported save version was accepted.");
                if (EditorBuildSettings.scenes[0].path != SlimaFrontEnd.MenuScene)
                    throw new Exception("Main menu is not the startup scene.");
                foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
                    if (scene.enabled && !File.Exists(scene.path)) throw new Exception("Missing scene: " + scene.path);
                Debug.Log("[Slima] VALIDATION PASSED: independent slots, atomic overwrite, corrupt-save rejection, startup scenes.");
            }
            finally
            {
                // Delete only the files created above, never recursively remove a computed directory.
                foreach (string file in Directory.GetFiles(directory)) File.Delete(file);
                Directory.Delete(directory);
            }
        }
    }
}
