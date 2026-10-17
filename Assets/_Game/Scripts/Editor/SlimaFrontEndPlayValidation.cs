using System;
using System.IO;
using System.Reflection;
using BlockMap;
using BlockMap.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace SlimaPuzzle.EditorTools
{
    [InitializeOnLoad]
    public static class SlimaFrontEndPlayValidation
    {
        const string Key = "Slima.FrontEndValidation.";
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        static SlimaFrontEndPlayValidation()
        {
            if (SessionState.GetBool(Key + "Running", false)) EditorApplication.update += Tick;
        }

        public static void Run()
        {
            if (!Application.isBatchMode) throw new InvalidOperationException("Use batch mode for play validation.");
            SlimaFrontEndSetup.Setup();
            EditorSceneManager.OpenScene(SlimaFrontEnd.MenuScene);
            EditorWindow gameView = EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
            gameView.position = new Rect(0, 0, 1280, 720);
            gameView.Show();
            string directory = Path.GetFullPath("Logs/SlimaFrontEnd-test-saves-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            SessionState.SetString(Key + "Directory", directory);
            SessionState.SetBool(Key + "Running", true);
            SessionState.SetInt(Key + "Stage", 0);
            SessionState.SetFloat(Key + "Next", 0f);
            SessionState.SetFloat(Key + "Deadline", (float)EditorApplication.timeSinceStartup + 120f);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.isPlaying = true;
        }

        static object Read(SlimaFrontEnd menu, string name) => typeof(SlimaFrontEnd).GetField(name, Private).GetValue(menu);
        static void Write(SlimaFrontEnd menu, string name, object value) => typeof(SlimaFrontEnd).GetField(name, Private).SetValue(menu, value);
        static object Call(SlimaFrontEnd menu, string name, params object[] args) =>
            typeof(SlimaFrontEnd).GetMethod(name, Private).Invoke(menu, args);
        static void Check(bool condition, string message)
        { if (!condition) throw new Exception(message); }

        static void Advance(int stage, float wait = 0.6f)
        {
            SessionState.SetInt(Key + "Stage", stage);
            SessionState.SetFloat(Key + "Next", (float)EditorApplication.timeSinceStartup + wait);
        }

        static void Tick()
        {
            if (!SessionState.GetBool(Key + "Running", false)) return;
            try
            {
                if (EditorApplication.timeSinceStartup > SessionState.GetFloat(Key + "Deadline", 0f))
                    throw new Exception("Play validation timed out.");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling ||
                    EditorApplication.timeSinceStartup < SessionState.GetFloat(Key + "Next", 0f)) return;
                SlimaFrontEnd menu = UnityEngine.Object.FindObjectOfType<SlimaFrontEnd>();
                if (menu == null || (bool)Read(menu, "_loading")) return;
                string directory = SessionState.GetString(Key + "Directory", "");
                int stage = SessionState.GetInt(Key + "Stage", 0);
                BlockMapGameRoot root = UnityEngine.Object.FindObjectOfType<BlockMapGameRoot>();
                switch (stage)
                {
                    case 0:
                        Write(menu, "_saveDirectory", directory);
                        Call(menu, "RefreshSlots");
                        ScreenCapture.CaptureScreenshot(Path.GetFullPath("Logs/SlimaFrontEnd-home.png"));
                        Advance(1);
                        break;
                    case 1:
                        Call(menu, "OpenLevelSelection");
                        Advance(2);
                        break;
                    case 2:
                        ScreenCapture.CaptureScreenshot(Path.GetFullPath("Logs/SlimaFrontEnd-levels.png"));
                        Advance(3);
                        break;
                    case 3:
                        Call(menu, "StartSlot", 0, false);
                        Advance(4);
                        break;
                    case 4:
                        Check(root != null && root.Party != null, "First level did not initialize.");
                        Check(Read(menu, "_page").ToString() == "Playing", "Slot did not enter gameplay.");
                        SlimeActor actor = root.Party.ActiveActor;
                        actor.SetState(new SlimeState { element = SlimeElement.Blue, opticsMode = BlueOpticsMode.ReflectRight });
                        actor.Mover.Teleport(actor.Mover.Cell, 3);
                        Check((bool)Call(menu, "SaveProgress", true), "Manual save failed: " + Read(menu, "_message") +
                            "; slot=" + Read(menu, "_slot") + "; busy=" + root.Party.IsBusy);
                        var saved = SlimaSaveStore.Read(0, directory);
                        Check(saved.progress.actors[0].state.element == SlimeElement.Blue, "Snapshot lost actor state.");
                        SlimaSaveStore.Write(1, new SlimaSave { scenePath = SlimaFrontEnd.DefaultLevelScenes[2] }, directory);
                        Write(menu, "_pauseRequested", true);
                        Advance(5);
                        break;
                    case 5:
                        Check(Time.timeScale == 0f, "Pause did not freeze gameplay.");
                        Call(menu, "OpenSettings", Enum.Parse(Read(menu, "_page").GetType(), "Pause"));
                        Advance(6);
                        break;
                    case 6:
                        ScreenCapture.CaptureScreenshot(Path.GetFullPath("Logs/SlimaFrontEnd-settings.png"));
                        Check(Time.timeScale == 0f, "Settings resumed paused gameplay.");
                        Advance(12);
                        break;
                    case 12:
                        Call(menu, "ReturnToMenu", true);
                        Advance(7);
                        break;
                    case 7:
                        Check(Read(menu, "_page").ToString() == "Home", "Return did not show the home page.");
                        Check(Time.timeScale == 1f, "Return did not reset the time scale.");
                        Call(menu, "StartSlot", 0, false);
                        Advance(8);
                        break;
                    case 8:
                        Check(root != null && root.Party.ActiveActor.State.element == SlimeElement.Blue,
                            "Continue did not restore actor state.");
                        Check(root.Party.ActiveActor.Mover.Facing == 3, "Continue did not restore facing.");
                        Check(root.Party.ActiveActor.State.opticsMode == BlueOpticsMode.ReflectRight,
                            "Continue did not restore optical ability.");
                        Check(SlimaSaveStore.Read(1, directory).scenePath == SlimaFrontEnd.DefaultLevelScenes[2],
                            "Continuing slot 1 changed slot 2.");
                        typeof(BlockMapGameRoot).GetField("_won", Private).SetValue(root, true);
                        Call(menu, "NextLevel");
                        Advance(9);
                        break;
                    case 9:
                        Check(root != null && root.gameObject.scene.path == SlimaFrontEnd.DefaultLevelScenes[1],
                            "Next level did not load the second chapter level.");
                        Check(SlimaSaveStore.Read(0, directory).scenePath == SlimaFrontEnd.DefaultLevelScenes[1],
                            "Next level did not persist the scene.");
                        Call(menu, "ReturnToMenu", true);
                        Advance(10);
                        break;
                    case 10:
                        Call(menu, "StartSlot", 0, true);
                        Advance(11);
                        break;
                    case 11:
                        Check(root != null && root.gameObject.scene.path == SlimaFrontEnd.DefaultLevelScenes[0],
                            "Restart did not return to the first level.");
                        Check(root.Party.ActiveActor.State.element == SlimeElement.Neutral,
                            "Restart retained the previous actor state.");
                        Check(SlimaSaveStore.Read(1, directory).scenePath == SlimaFrontEnd.DefaultLevelScenes[2],
                            "Restart overwrote another slot.");
                        Check(SlimaSaveStore.Read(2, directory) == null, "Unused slot is no longer empty.");
                        Debug.Log("[Slima] PLAY VALIDATION PASSED: menu, new game, pause, settings, save, continue, next level, reset, slot isolation.");
                        Finish(0);
                        break;
                }
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                Finish(1);
            }
        }

        static void Finish(int code)
        {
            // Prevent OnApplicationQuit from creating another validation save after cleanup.
            SlimaFrontEnd menu = UnityEngine.Object.FindObjectOfType<SlimaFrontEnd>();
            if (menu != null) Call(menu, "UnbindLevel");
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
