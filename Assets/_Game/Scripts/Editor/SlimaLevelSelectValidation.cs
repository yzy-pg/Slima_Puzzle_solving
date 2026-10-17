using System;
using System.IO;
using System.Reflection;
using BlockMap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SlimaPuzzle.EditorTools
{
    [InitializeOnLoad]
    public static class SlimaLevelSelectValidation
    {
        const string Key = "Slima.LevelSelectValidation.";
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        static SlimaLevelSelectValidation()
        {
            if (SessionState.GetBool(Key + "Running", false)) EditorApplication.update += Tick;
        }
        public static void Run()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated batch project.");
            SlimaFrontEndSetup.Validate();
            EditorSceneManager.OpenScene(SlimaFrontEnd.MenuScene);
            var view = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
            view.position = new Rect(0, 0, 1280, 720);
            view.Show();
            string directory = Path.GetFullPath("Logs/LevelSelect-saves-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            SessionState.SetString(Key + "Directory", directory);
            SessionState.SetBool(Key + "Running", true);
            SessionState.SetInt(Key + "Stage", 0);
            SessionState.SetFloat(Key + "Next", 0f);
            SessionState.SetFloat(Key + "Deadline", (float)EditorApplication.timeSinceStartup + 180f);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.isPlaying = true;
        }
        static object Read(SlimaFrontEnd menu, string name) => typeof(SlimaFrontEnd).GetField(name, Private).GetValue(menu);
        static void Write(SlimaFrontEnd menu, string name, object value) => typeof(SlimaFrontEnd).GetField(name, Private).SetValue(menu, value);
        static object Call(SlimaFrontEnd menu, string name, params object[] args) => typeof(SlimaFrontEnd).GetMethod(name, Private).Invoke(menu, args);
        static void Page(SlimaFrontEnd menu, string name) => Write(menu, "_page", Enum.Parse(Read(menu, "_page").GetType(), name));
        static void Check(bool value, string message) { if (!value) throw new Exception(message); }
        static void Next(int stage)
        {
            Debug.Log("[LevelSelect] Stage " + stage);
            SessionState.SetInt(Key + "Stage", stage);
            SessionState.SetFloat(Key + "Next", (float)EditorApplication.timeSinceStartup + 0.6f);
        }
        static void Capture(string name) => ScreenCapture.CaptureScreenshot(Path.GetFullPath("Logs/LevelSelect-" + name + ".png"));
        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > SessionState.GetFloat(Key + "Deadline", 0f)) throw new Exception("Level selection validation timed out at stage " + SessionState.GetInt(Key + "Stage", 0));
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.timeSinceStartup < SessionState.GetFloat(Key + "Next", 0f)) return;
                var menu = UnityEngine.Object.FindObjectOfType<SlimaFrontEnd>();
                if (menu == null || (bool)Read(menu, "_loading")) return;
                var root = UnityEngine.Object.FindObjectOfType<BlockMapGameRoot>();
                string directory = SessionState.GetString(Key + "Directory", "");
                int stage = SessionState.GetInt(Key + "Stage", 0);
                switch (stage)
                {
                    case 0:
                        Write(menu, "_saveDirectory", directory);
                        SlimaSaveStore.Write(1, new SlimaSave { scenePath = menu.levelScenes[2] }, directory);
                        SessionState.SetString(Key + "OtherSlot", File.ReadAllText(SlimaSaveStore.SlotPath(1, directory)));
                        Call(menu, "RefreshSlots");
                        Check(SlimaFrontEnd.LevelSelectorCount == 20 && SlimaFrontEnd.LevelPageCount == 5 && menu.levelScenes.Length == 100, "Expected five pages of 20 level positions.");
                        for (int i = 0; i < 100; i++) Check((bool)Call(menu, "LevelAvailable", i) == (i < 16), "Incorrect availability at " + i);
                        Check(!(bool)Call(menu, "LevelAvailable", -1) && !(bool)Call(menu, "LevelAvailable", 100), "Out-of-range level is available.");
                        Call(menu, "OpenLevelSelection");
                        Check(Read(menu, "_page").ToString() == "Levels" && (int)Read(menu, "_selectionSlot") == 0, "Start game did not directly open level selection with automatic progress.");
                        Next(10); break;
                    case 10:
                        Capture("entry"); Next(1); break;
                    case 1:
                        Call(menu, "OpenLevelSelection");
                        Check(Read(menu, "_page").ToString() == "Levels" && (int)Read(menu, "_selectedLevel") == 0, "Empty slot did not open the selector.");
                        Check(!File.Exists(SlimaSaveStore.SlotPath(0, directory)), "Browsing created a save.");
                        Next(2); break;
                    case 2:
                        Capture("levels"); Next(22); break;
                    case 22:
                        Call(menu, "SetLevelPage", 1);
                        Check((int)Read(menu, "_levelPage") == 1 && (int)Read(menu, "_selectedLevel") == -1, "Empty second page selected an unavailable level.");
                        Call(menu, "StartSelectedLevel", false);
                        Check(!(bool)Read(menu, "_loading") && !File.Exists(SlimaSaveStore.SlotPath(0, directory)), "An empty page wrote a save.");
                        Next(23); break;
                    case 23:
                        Capture("page2"); Next(24); break;
                    case 24:
                        Call(menu, "SetLevelPage", 4);
                        Call(menu, "SetLevelPage", 5);
                        Call(menu, "SetLevelPage", -1);
                        Check((int)Read(menu, "_levelPage") == 4, "Page bounds were not respected.");
                        menu.levelScenes[20] = menu.levelScenes[0];
                        Call(menu, "SetLevelPage", 0);
                        Call(menu, "SetLevelPage", 1);
                        Check((int)Read(menu, "_selectedLevel") == 20 && (bool)Call(menu, "LevelAvailable", 20), "A configured later page could not be selected.");
                        menu.levelScenes[20] = "";
                        Call(menu, "SetLevelPage", 0);
                        Check(!File.Exists(SlimaSaveStore.SlotPath(0, directory)), "Paging wrote a save.");
                        Next(21); break;
                    case 21:
                        Write(menu, "_selectedLevel", 19);
                        Call(menu, "StartSelectedLevel", false);
                        Check(!(bool)Read(menu, "_loading") && !File.Exists(SlimaSaveStore.SlotPath(0, directory)), "Reserved level started or changed a save.");
                        Write(menu, "_selectedLevel", 0);
                        Call(menu, "StartSelectedLevel", false);
                        Next(3); break;
                    case 3:
                        if (root == null || root.Party.IsBusy) return;
                        Check(root.gameObject.scene.path == menu.levelScenes[0], "Selected level did not load.");
                        root.Party.ActiveActor.Mover.Teleport(root.Party.ActiveActor.Mover.Cell, 3);
                        Check((bool)Call(menu, "SaveProgress", true), "Selected level save failed.");
                        Write(menu, "_pauseRequested", true);
                        Next(31); break;
                    case 31:
                        Check(Time.timeScale == 0f, "Selected level did not pause.");
                        Call(menu, "ReturnToMenu", true); Next(4); break;
                    case 4:
                        Check(Read(menu, "_page").ToString() == "Home", "Selected level did not return to menu.");
                        Call(menu, "OpenLevelSelection");
                        Check((int)Read(menu, "_selectedLevel") == 0, "Existing slot did not preselect its current level.");
                        Call(menu, "StartSelectedLevel", false); Next(5); break;
                    case 5:
                        if (root == null || root.Party.IsBusy) return;
                        Check(root.gameObject.scene.path == menu.levelScenes[0] && root.Party.ActiveActor.Mover.Facing == 3, "Selecting the current level lost saved progress.");
                        Call(menu, "ReturnToMenu", true); Next(6); break;
                    case 6:
                        Call(menu, "OpenLevelSelection");
                        SessionState.SetString(Key + "BeforeConfirm", File.ReadAllText(SlimaSaveStore.SlotPath(0, directory)));
                        Write(menu, "_selectedLevel", 1);
                        Call(menu, "StartSelectedLevel", false);
                        Check(Read(menu, "_page").ToString() == "ConfirmLevel", "Changing level did not request confirmation.");
                        Check(File.ReadAllText(SlimaSaveStore.SlotPath(0, directory)) == SessionState.GetString(Key + "BeforeConfirm", ""), "Confirmation replaced a save too early.");
                        Next(7); break;
                    case 7:
                        Capture("confirm"); Next(71); break;
                    case 71:
                        Page(menu, "Levels");
                        Check(File.ReadAllText(SlimaSaveStore.SlotPath(0, directory)) == SessionState.GetString(Key + "BeforeConfirm", ""), "Cancel changed the save.");
                        Call(menu, "StartSelectedLevel", false);
                        Call(menu, "StartSelectedLevel", true); Next(8); break;
                    case 8:
                        Check(root != null && root.gameObject.scene.path == menu.levelScenes[1] && SlimaSaveStore.Read(0, directory).scenePath == menu.levelScenes[1], "Confirmed level did not replace the selected slot.");
                        Check(File.ReadAllText(SlimaSaveStore.SlotPath(1, directory)) == SessionState.GetString(Key + "OtherSlot", "") && SlimaSaveStore.Read(2, directory) == null, "Level selection changed another slot.");
                        Write(menu, "_save", new SlimaSave { scenePath = menu.levelScenes[15] });
                        Check((int)Call(menu, "NextLevelIndex") == -1, "The final configured level entered a reserved position.");
                        var scenes = (string[])menu.levelScenes.Clone();
                        menu.levelScenes[2] = "";
                        Write(menu, "_save", new SlimaSave { scenePath = menu.levelScenes[1] });
                        Check((int)Call(menu, "NextLevelIndex") == 3, "Next level did not skip an empty position.");
                        menu.levelScenes = scenes;
                        Call(menu, "ReturnToMenu", false); Next(9); break;
                    case 9:
                        File.WriteAllText(SlimaSaveStore.SlotPath(0, directory), "{broken");
                        Call(menu, "OpenLevelSelection");
                        Call(menu, "StartSelectedLevel", false);
                        Check(Read(menu, "_page").ToString() == "ConfirmLevel" && File.ReadAllText(SlimaSaveStore.SlotPath(0, directory)) == "{broken", "A damaged automatic save was overwritten without confirmation.");
                        Page(menu, "Levels");
                        Check(File.ReadAllText(SlimaSaveStore.SlotPath(0, directory)) == "{broken", "Cancel changed a damaged automatic save.");
                        Call(menu, "StartSelectedLevel", true); Next(11); break;
                    case 11:
                        Check(SlimaSaveStore.Read(0, directory).scenePath == menu.levelScenes[0], "Confirmed recovery did not create valid progress.");
                        Check(File.ReadAllText(SlimaSaveStore.SlotPath(1, directory)) == SessionState.GetString(Key + "OtherSlot", ""), "Automatic progress changed an old secondary save.");
                        Debug.Log("[Slima] DIRECT LEVEL ENTRY VALIDATION PASSED: start game opens selection directly, automatic save, five pages of 20 numeric rectangles, paging/browse/cancel without writes, continue with progress, confirmed level change, corrupt-save confirmation/recovery, old secondary save isolation, skip reserved levels.");
                        Finish(0); break;
                }
            }
            catch (Exception error) { Debug.LogException(error); Finish(1); }
        }
        static void Finish(int code)
        {
            var menu = UnityEngine.Object.FindObjectOfType<SlimaFrontEnd>();
            if (menu != null) Call(menu, "UnbindLevel");
            Time.timeScale = 1f;
            SessionState.SetBool(Key + "Running", false);
            EditorApplication.update -= Tick;
            string directory = SessionState.GetString(Key + "Directory", "");
            if (Directory.Exists(directory))
            {
                foreach (string file in Directory.GetFiles(directory)) File.Delete(file);
                Directory.Delete(directory);
            }
            EditorApplication.Exit(code);
        }
    }
}
