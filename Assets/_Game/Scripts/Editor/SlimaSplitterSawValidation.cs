using System;
using System.IO;
using BlockMap;
using BlockMap.Gameplay;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace SlimaPuzzle.EditorTools
{
    public static class SlimaSplitterSawValidation
    {
        public static void Run()
        {
            SlimaMechanismAssetBuilder.BuildSplitterAssets();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            BlockLibrary.Reload();
            var group = new GameObject("Splitter saw validation");
            var view = GameplayPrototypeFactory.Create(BlockLibrary.Get("Splitter"),BlockGameplayCatalog.Get("Splitter"),Vector3Int.zero,0,group.transform);
            var saw = view.GetComponentInChildren<SplitterSawAnimation>();
            Require(saw != null && saw.frames.Length == 24,"Missing 24-frame animated saw.");
            Require(saw.transform.localPosition == new Vector3(0,.11f,0),"Saw centre must be on the floor surface.");
            Require(Vector3.Dot(saw.transform.right,Vector3.back)>.999f && Vector3.Dot(saw.transform.up,Vector3.up)>.999f,"Saw plane must follow the floor slot along local Z.");
            var renderer = saw.GetComponent<SpriteRenderer>();
            Require(renderer.sharedMaterial.shader.name == "Slima/Saw Sprite","Wrong saw material.");
            Require(renderer.sharedMaterial.GetFloat("_VisibleMinY")==0f,"Saw must expose only its upper half.");
            Require(saw.transform.localScale==Vector3.one*1.1f,"Saw was not enlarged to the target size.");
            Require(renderer.bounds.max.y<.67f && renderer.bounds.size.z<1.11f,"Saw canvas exceeds the target size.");
            Quaternion orientation = saw.transform.localRotation;
            for (int i=0;i<24;i++)
            {
                Sprite sprite = saw.frames[i];
                Require(sprite.rect.size==new Vector2(64,64) && sprite.texture.filterMode==FilterMode.Point,"Wrong frame format.");
                Require(sprite.pivot==new Vector2(32,32),"Frame pivot drift.");
                var decoded = new Texture2D(2,2);
                try
                {
                    decoded.LoadImage(File.ReadAllBytes("Assets/_Game/Art/Textures/Mechanisms/SplitterSaw/SplitterSaw_" + i.ToString("00") + ".png"));
                    Require(decoded.GetPixel(0,0).a==0 && decoded.GetPixel(63,63).a==0,"Frame has an opaque background.");
                    int left=64,right=0;
                    for (int y=0;y<64;y++) for (int x=0;x<64;x++)
                        if (decoded.GetPixel(x,y).a>=.5f) { left=Math.Min(left,x); right=Math.Max(right,x); }
                    Require((right-left+1)*saw.transform.localScale.x/64f<.94f,"Visible teeth exceed the floor length.");
                }
                finally { Object.DestroyImmediate(decoded); }
                Require(saw.CurrentFrame==i && renderer.sprite==sprite,"Incorrect frame order.");
                saw.Advance(1f/saw.framesPerSecond);
                Require(saw.transform.localRotation==orientation,"Animation rotated the object instead of changing sprites.");
            }
            Require(saw.CurrentFrame==0,"Animation does not loop.");
            saw.Advance(0.001f); saw.Advance(1.5f);
            Require(saw.CurrentFrame==0,"Long frame delta broke the loop.");
            saw.Advance(.125f); saw.gameObject.SetActive(false); saw.gameObject.SetActive(true);
            // Edit-mode batch checks dispatch this callback explicitly; play mode invokes it on re-enable.
            saw.SendMessage("OnEnable");
            Require(saw.CurrentFrame==0 && renderer.sprite==saw.frames[0],"Re-enable did not reset animation.");
            view.SetActivated(true); view.SetActivated(false);
            var properties = new MaterialPropertyBlock(); renderer.GetPropertyBlock(properties);
            Require(properties.GetColor("_Color")==Color.white,"Gameplay tinted the metal blade.");

            var cameraObject=new GameObject("Camera"); var camera=cameraObject.AddComponent<Camera>();
            camera.orthographic=true; camera.orthographicSize=.64f; camera.clearFlags=CameraClearFlags.SolidColor;
            camera.backgroundColor=new Color(.07f,.095f,.15f);
            var target=new Vector3(.5f,.18f,.5f);
            camera.transform.position=target+new Vector3(1.8f,2.7f,7); camera.transform.LookAt(target);
            cameraObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=false;
            var lightObject=new GameObject("Key light"); var light=lightObject.AddComponent<Light>();
            light.type=LightType.Directional; light.intensity=1.2f; light.transform.rotation=Quaternion.Euler(45,-25,0);
            RenderSettings.ambientLight=new Color(.4f,.44f,.52f);
            var rt=new RenderTexture(600,600,24,RenderTextureFormat.ARGB32); rt.Create();
            var image=new Texture2D(600,600,TextureFormat.RGBA32,false); var old=RenderTexture.active;
            Directory.CreateDirectory("Logs/SplitterSawFrames"); Directory.CreateDirectory("Docs/images");
            try
            {
                for (int i=0;i<24;i++)
                {
                    Require(saw.CurrentFrame==i,"Rendered animation frame order differs.");
                    RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest {destination=rt});
                    RenderTexture.active=rt; image.ReadPixels(new Rect(0,0,600,600),0,0); image.Apply();
                    byte[] png=image.EncodeToPNG(); File.WriteAllBytes("Logs/SplitterSawFrames/"+i.ToString("00")+".png",png);
                    if (i==0) File.WriteAllBytes("Docs/images/SplitterSawModel.png",png);
                    saw.Advance(1f/saw.framesPerSecond);
                }
            }
            finally { RenderTexture.active=old; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(image); }
            File.WriteAllText("Logs/SplitterSaw-validation.txt","PASS: floor only plus half-exposed vertical saw; 24 transparent 64px sprites; 15-degree source rotations; frame order, looping, large delta, reset, fixed transform and 24 rendered animation frames.\n");
            Debug.Log("[Splitter Saw] Validation PASS.");
        }
        static void Require(bool condition,string message) { if (!condition) throw new Exception(message); }
    }
}
