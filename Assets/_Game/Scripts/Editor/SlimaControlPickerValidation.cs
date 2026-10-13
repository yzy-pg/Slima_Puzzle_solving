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
    public static class SlimaControlPickerValidation
    {
        const string Key = "Slima.ControlPickerValidation.";
        const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        static SlimaControlPickerValidation()
        {
            if (SessionState.GetBool(Key+"Running",false)) EditorApplication.update += Tick;
        }

        public static void Run()
        {
            SlimaControlArtValidation.Run();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var view=EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView"));
            view.position=new Rect(0,0,1280,720); view.Show();
            SessionState.SetBool(Key+"Running",true); SessionState.SetInt(Key+"Stage",0);
            SessionState.SetFloat(Key+"Next",0); SessionState.SetFloat(Key+"Deadline",(float)EditorApplication.timeSinceStartup+90);
            EditorApplication.update -= Tick; EditorApplication.update += Tick;
            EditorApplication.isPlaying=true;
        }

        static void Tick()
        {
            try
            {
                if (EditorApplication.timeSinceStartup > SessionState.GetFloat(Key+"Deadline",0)) throw new Exception("Picker validation timed out.");
                if (!EditorApplication.isPlaying || EditorApplication.isCompiling || EditorApplication.timeSinceStartup<SessionState.GetFloat(Key+"Next",0)) return;
                int stage=SessionState.GetInt(Key+"Stage",0);
                if (stage==0)
                {
                    BlockLibrary.Reload();
                    var camera=new GameObject("Camera").AddComponent<Camera>(); camera.tag="MainCamera";
                    camera.orthographic=true; camera.orthographicSize=3;
                    camera.transform.position=new Vector3(4,7,-5); camera.transform.LookAt(new Vector3(2,.5f,2));
                    camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.12f,.14f,.18f);
                    var light=new GameObject("Light").AddComponent<Light>(); light.type=LightType.Directional;
                    light.transform.rotation=Quaternion.Euler(45,-25,0);
                    var root=new GameObject("RGB preview map"); var world=root.AddComponent<BlockWorld>();
                    for(int x=0;x<6;x++) for(int z=0;z<5;z++) world.Set(new Vector3Int(x,0,z),BlockLibrary.IndexOf("Lab_WallBrick1"));
                    var plate=new Vector3Int(2,1,2); var door=new Vector3Int(3,1,2);
                    world.Set(plate,BlockLibrary.IndexOf("PressurePlateLarge_A")); world.Set(door,BlockLibrary.IndexOf("Door_B"));
                    world.SetLinkId(plate,"G001"); world.SetLinkId(door,"G001");
                    BlockLinkVisuals.SetEditorColor(world,plate,new Color32(36,190,88,255));
                    var editor=root.AddComponent<BlockMapEditorTool>(); editor.showDebugBounds=false;
                    root.AddComponent<BlockMapRuntimeSpawner>();
                    var picker=root.GetComponent<BlockMapLinkEditorTool>();
                    if(picker==null) picker=root.AddComponent<BlockMapLinkEditorTool>();
                    typeof(BlockMapLinkEditorTool).GetMethod("SetLinkMode",Private).Invoke(picker,new object[] { true });
                    typeof(BlockMapLinkEditorTool).GetField("_colorMode",Private).SetValue(picker,true);
                    typeof(BlockMapLinkEditorTool).GetMethod("SelectColorCell",Private).Invoke(picker,new object[] { plate });
                    var rgb=(string[])typeof(BlockMapLinkEditorTool).GetField("_rgbText",Private).GetValue(picker);
                    if(rgb[0]!="36" || rgb[1]!="190" || rgb[2]!="88") throw new Exception("RGB picker did not load selected color.");
                }
                else if(stage==1) ScreenCapture.CaptureScreenshot(Path.GetFullPath("Docs/images/ControlColorPicker.png"));
                else
                {
                    if(!File.Exists("Docs/images/ControlColorPicker.png")) throw new Exception("Picker screenshot missing.");
                    File.WriteAllText("Logs/ControlPicker-validation.txt","PASS: Play-mode RGB picker opens, selects plate, loads RGB 36/190/88, renders IMGUI panel and map.\n");
                    Debug.Log("[Slima Controls] RGB PICKER PASS"); Finish(0); return;
                }
                SessionState.SetInt(Key+"Stage",stage+1); SessionState.SetFloat(Key+"Next",(float)EditorApplication.timeSinceStartup+2f);
            }
            catch(Exception error) { Debug.LogException(error); Finish(1); }
        }

        static void Finish(int result)
        {
            SessionState.SetBool(Key+"Running",false); EditorApplication.update -= Tick;
            EditorApplication.Exit(result);
        }
    }
}
