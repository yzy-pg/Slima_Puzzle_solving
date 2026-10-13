using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BlockMap;
using BlockMap.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace SlimaPuzzle.EditorTools
{
    public static class SlimaOpticLightingRegression
    {
        public static void RunCompetition() => CheckLightCompetition();
        public static void Run()
        {
            SlimaOpticGlowValidation.Run();
            CheckLightCompetition();
            CheckLevel();
            File.WriteAllText("Logs/OpticLighting-regression.txt",
                "PASS: Forward reproduces lost lamp illumination with >8 lights; Forward+ preserves it. test-level 2-1 lamps remain enabled and illuminate the level after real receiver activation.\n");
            Debug.Log("[Optic Lighting Regression] Validation PASS.");
        }

        static void CheckLightCompetition()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var oldGraphics=GraphicsSettings.renderPipelineAsset;
            var oldQuality=QualitySettings.renderPipeline;
            var material=new Material(Shader.Find("Slima/Toon (Cel + Pixel)"));
            var rt=new RenderTexture(240,240,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var pixels=new Texture2D(240,240,TextureFormat.RGBAFloat,false,true);
            var renderer=Object.Instantiate(AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/Settings/URP-Balanced-Renderer.asset"));
            renderer.rendererFeatures.Clear();
            var feature=ScriptableObject.CreateInstance<SlimaPuzzle.Rendering.SlimaToonLightingFeature>();
            renderer.rendererFeatures.Add(feature);
            UniversalRenderPipelineAsset pipeline=null;
            try
            {
                RenderSettings.ambientMode=AmbientMode.Flat; RenderSettings.ambientLight=Color.black;
                RenderSettings.ambientProbe=new SphericalHarmonicsL2(); RenderSettings.fog=false;
                var quad=GameObject.CreatePrimitive(PrimitiveType.Quad);
                quad.GetComponent<Renderer>().sharedMaterial=material;
                material.SetColor("_ShadowColor",Color.black); material.SetColor("_AmbientColor",Color.clear);
                material.SetFloat("_FallbackLight",0); material.SetFloat("_OutlineWidth",0); material.SetFloat("_LightingMode",1);
                var camera=new GameObject("Light competition camera").AddComponent<Camera>();
                camera.transform.position=new Vector3(0,0,-8); camera.orthographic=true; camera.orthographicSize=1;
                camera.allowHDR=true; camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
                var competing=new Light[16];
                for(int i=0;i<competing.Length;i++)
                {
                    var light=new GameObject("Competing optical light "+i).AddComponent<Light>();
                    light.type=LightType.Point; light.color=Color.blue; light.range=4; light.intensity=30;
                    light.transform.position=new Vector3((i-4)*.01f,0,-.5f); competing[i]=light;
                }
                var lamp=new GameObject("Light_Green_Lamp_regression").AddComponent<Light>();
                lamp.type=LightType.Point; lamp.color=Color.green; lamp.range=4; lamp.intensity=.5f;
                lamp.transform.position=new Vector3(0,0,-1.5f);
                float Green()
                {
                    RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest {destination=rt});
                    var old=RenderTexture.active; RenderTexture.active=rt;
                    pixels.ReadPixels(new Rect(0,0,240,240),0,0); pixels.Apply(); RenderTexture.active=old;
                    return pixels.GetPixel(120,120).g;
                }
                float forwardLoss=0, forwardPlusPreserved=0;
                foreach(int mode in new[] {0,2})
                {
                    renderer.renderingMode=(RenderingMode)mode;
                    pipeline=UniversalRenderPipelineAsset.Create(renderer); pipeline.msaaSampleCount=1; pipeline.maxAdditionalLightsCount=8;
                    GraphicsSettings.renderPipelineAsset=pipeline; QualitySettings.renderPipeline=pipeline;
                    // Batch render requests run without an editor frame between pipeline changes.
                    typeof(RenderPipelineManager).GetMethod("CleanupRenderPipeline",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
                    typeof(RenderPipelineManager).GetMethod("PrepareRenderPipeline",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[] {pipeline});
                    foreach(var light in competing) light.enabled=false;
                    Green(); float baseline=Green(); Require(baseline>.1f,"Lamp baseline did not illuminate the quad.");
                    foreach(var light in competing) light.enabled=true;
                    Green(); float combined=Green();
                    Debug.Log($"[Optic Lighting Regression] Mode {mode}: lamp green baseline {baseline:F4}, crowded {combined:F4}, Forward+ keyword {Shader.IsKeywordEnabled("_FORWARD_PLUS")}.");
                    if(mode==0) { forwardLoss=combined/baseline; Require(forwardLoss<.5f,"Forward lamp competition did not reproduce."); }
                    else { forwardPlusPreserved=combined/baseline; Require(forwardPlusPreserved>.95f,"Forward+ dropped the lamp contribution."); }
                    GraphicsSettings.renderPipelineAsset=oldGraphics; QualitySettings.renderPipeline=oldQuality;
                    typeof(RenderPipelineManager).GetMethod("CleanupRenderPipeline",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
                    Object.DestroyImmediate(pipeline); pipeline=null;
                }
                Debug.Log($"[Optic Lighting Regression] Lamp contribution: Forward {forwardLoss:F3}, Forward+ {forwardPlusPreserved:F3}.");
            }
            finally
            {
                GraphicsSettings.renderPipelineAsset=oldGraphics; QualitySettings.renderPipeline=oldQuality;
                typeof(RenderPipelineManager).GetMethod("PrepareRenderPipeline",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[] {oldQuality ?? oldGraphics});
                if(pipeline!=null) Object.DestroyImmediate(pipeline);
                Object.DestroyImmediate(renderer); Object.DestroyImmediate(feature); Object.DestroyImmediate(material);
                rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(pixels);
            }
        }

        static void CheckLevel()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/test-level 2-1.unity",OpenSceneMode.Single);
            BlockLibrary.Reload();
            var world=Object.FindObjectOfType<BlockWorld>(); world.RestoreIfNeeded();
            var gameplay=world.GetComponent<BlockGameplayController>() ?? world.gameObject.AddComponent<BlockGameplayController>();
            gameplay.Initialize(world); world.GetComponent<BlockMapRuntimeSpawner>()?.Rebuild();
            var lamps=Object.FindObjectsOfType<Light>().Where(l=>l.name.Contains("_Lamp_")).ToArray();
            Require(lamps.Length>0 && lamps.All(l=>l.enabled && l.gameObject.activeInHierarchy),"test2-1 lamps are not enabled initially.");
            var receiver=gameplay.Enumerate(GameplayBlockKind.BeamReceiver).First();
            var source=receiver.Cell+Vector3Int.back;
            string emitterId=receiver.Profile.BeamColor==BeamColor.Red ? "Lab_Laser_02" : receiver.Profile.BeamColor==BeamColor.Blue ? "Lab_Laser_03" : "Lab_Laser_01";
            world.Set(source,BlockLibrary.IndexOf(emitterId)); world.SetDir(source,0);
            typeof(BlockGameplayController).GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(gameplay,null);
            var receiverView=world.GetComponentsInChildren<GameplayBlockView>().First(v=>v.Cell==receiver.Cell);
            Require(receiverView.GetComponentsInChildren<Light>().Count(l=>l.enabled)==1,"test2-1 receiver did not illuminate only the incoming face.");
            Require(lamps.All(l=>l!=null && l.enabled && l.gameObject.activeInHierarchy),"Receiver activation disabled a test2-1 Lamp.");

            var camera=new GameObject("test2-1 lamp verification camera").AddComponent<Camera>();
            Vector3 center=lamps.Select(l=>l.transform.position).Aggregate(Vector3.zero,(a,b)=>a+b)/lamps.Length;
            camera.orthographic=true; camera.orthographicSize=13; camera.aspect=2;
            camera.transform.position=center+new Vector3(0,20,-16); camera.transform.LookAt(center);
            camera.allowHDR=true; camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
            var rt=new RenderTexture(800,400,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear);
            var pixels=new Texture2D(800,400,TextureFormat.RGBAFloat,false,true);
            Color[] Capture()
            {
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest {destination=rt});
                var old=RenderTexture.active; RenderTexture.active=rt; pixels.ReadPixels(new Rect(0,0,800,400),0,0);
                pixels.Apply(); RenderTexture.active=old; return pixels.GetPixels();
            }
            try
            {
                Capture(); var on=Capture();
                foreach(var lamp in lamps) lamp.enabled=false;
                var off=Capture();
                int differences=0;
                for(int i=0;i<on.Length;i++) if(Mathf.Abs(on[i].r-off[i].r)+Mathf.Abs(on[i].g-off[i].g)+Mathf.Abs(on[i].b-off[i].b)>.01f) differences++;
                Require(differences>100,"test2-1 lamps stopped contributing illumination after receiver activation.");
                foreach(var lamp in lamps) lamp.enabled=true;
                Debug.Log($"[Optic Lighting Regression] test2-1: {lamps.Length} lamps still illuminate {differences} pixels after receiver activation.");
            }
            finally { rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(pixels); }
        }
        static void Require(bool condition,string message) { if(!condition) throw new Exception(message); }
    }
}
