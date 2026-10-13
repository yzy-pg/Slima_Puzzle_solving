using System;
using System.IO;
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
    public static class SlimaOpticGlowValidation
    {
        public static void Run()
        {
            SlimaMechanismArtValidation.Run();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            BlockLibrary.Reload();
            var parent=new GameObject("Optical glow validation");
            Material shared=Resources.Load<Material>("BlockMapLampHalo");
            Require(shared!=null && !ShaderUtil.ShaderHasError(shared.shader),"Lamp halo material is unavailable.");
            foreach (string id in SlimaMechanismAssetBuilder.Ids)
            {
                var profile=BlockGameplayCatalog.Get(id);
                if (profile.Kind==GameplayBlockKind.Splitter) continue;
                for (int direction=0;direction<4;direction++)
                {
                    var view=GameplayPrototypeFactory.Create(BlockLibrary.Get(id),profile,new Vector3Int(3,2,1),direction,parent.transform);
                    var glow=view.GetComponentInChildren<GameplayOpticGlow>();
                    var halos=view.GetComponentsInChildren<BlockMapLampHalo>();
                    int expected=profile.Kind==GameplayBlockKind.BeamReceiver ? 4 : 1;
                    Require(halos.Length==expected,"Wrong optical face count: "+id);
                    glow.Initialize(); glow.Initialize();
                    Require(view.GetComponentsInChildren<BlockMapLampHalo>().Length==expected,"Duplicate halos: "+id);
                    Require(view.GetComponentsInChildren<Light>().Length==expected && view.GetComponentsInChildren<Collider>().Length==0,"Expected one outward light per optical face and no colliders.");
                    foreach(var normal in glow.opticalSurface.GetComponent<MeshFilter>().sharedMesh.normals)
                        Require(Mathf.Abs(normal.y)<.001f,"Top or bottom geometry is using the emitting material.");
                    for (int face=0;face<expected;face++)
                    {
                        Vector3 local=Quaternion.Euler(0,face*90,0)*new Vector3(0,.5f,.52f);
                        Require((halos[face].transform.position-glow.transform.TransformPoint(local)).sqrMagnitude<.000001f,"Face position does not follow map rotation.");
                        var renderer=halos[face].GetComponent<MeshRenderer>();
                        Require(renderer.sharedMaterial==shared && renderer.shadowCastingMode==ShadowCastingMode.Off,"Glow does not use the lamp effect.");
                    }
                    view.SetReceiverFaces(1 << direction);
                    view.SetActivated(true);
                    CheckSettings(halos,glow,glow.intensity);
                    if (glow.receiver)
                        for(int face=1;face<4;face++)
                        {
                            view.SetReceiverFaces(1 << ((face+direction)&3));
                            CheckSettings(halos,glow,glow.intensity,face);
                        }
                    view.SetActivated(false);
                    CheckSettings(halos,glow,glow.receiver ? 0f : glow.intensity);
                    Object.DestroyImmediate(view.gameObject);
                }
            }
            // Preserve the original definition-driven _Lamp path after adding direct optical settings.
            var definition=ScriptableObject.CreateInstance<BlockDefinition>();
            var lamp=new GameObject("Original lamp compatibility").AddComponent<BlockMapLampHalo>();
            definition.lightColor=Color.yellow; definition.lampHaloSize=1.8f; definition.lampHaloIntensity=.6f;
            lamp.Configure(Color.blue,.65f,.3f,.12f); lamp.Configure(definition);
            var block=new MaterialPropertyBlock(); lamp.GetComponent<Renderer>().GetPropertyBlock(block);
            Require(block.GetColor("_Color")==Color.yellow && block.GetFloat("_Size")==1.8f && block.GetFloat("_Intensity")==.6f,"Original lamp settings changed.");
            Object.DestroyImmediate(lamp.gameObject); Object.DestroyImmediate(definition);
            CheckBeamReception();
            CheckConcurrentReception();

            var emitter=GameplayPrototypeFactory.Create(BlockLibrary.Get("Lab_Laser_03"),BlockGameplayCatalog.Get("Lab_Laser_03"),Vector3Int.zero,0,parent.transform);
            var receiver=GameplayPrototypeFactory.Create(BlockLibrary.Get("ReceiverPurple_A"),BlockGameplayCatalog.Get("ReceiverPurple_A"),new Vector3Int(2,0,0),0,parent.transform);
            receiver.SetReceiverFaces(1);
            receiver.SetActivated(true);
            var camera=new GameObject("Glow camera").AddComponent<Camera>();
            camera.orthographic=true; camera.orthographicSize=1.05f; camera.aspect=2f; camera.allowHDR=true;
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.07f,.095f,.15f);
            camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
            Vector3 targetPoint=new Vector3(1.5f,.5f,.5f);
            camera.transform.position=targetPoint+new Vector3(1,2.3f,6); camera.transform.LookAt(targetPoint);
            var light=new GameObject("Key light").AddComponent<Light>(); light.type=LightType.Directional;
            light.intensity=1.2f; light.transform.rotation=Quaternion.Euler(45,-25,0);
            RenderSettings.ambientLight=new Color(.4f,.44f,.52f); RenderSettings.fog=false;
            var rt=new RenderTexture(1200,600,24,RenderTextureFormat.ARGBFloat,RenderTextureReadWrite.Linear); rt.Create();
            var pixels=new Texture2D(1200,600,TextureFormat.RGBAFloat,false,true);
            var haloRenderers=parent.GetComponentsInChildren<BlockMapLampHalo>();
            void ShowHalos(bool visible) { foreach (var halo in haloRenderers) halo.GetComponent<Renderer>().enabled=visible; }
            Color[] Capture(string path)
            {
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest {destination=rt});
                var old=RenderTexture.active; RenderTexture.active=rt;
                pixels.ReadPixels(new Rect(0,0,1200,600),0,0); pixels.Apply(); RenderTexture.active=old;
                Color[] data=pixels.GetPixels();
                if (path!=null)
                {
                    var image=new Texture2D(1200,600,TextureFormat.RGBA32,false);
                    var preview=new Color[data.Length]; for (int i=0;i<data.Length;i++) preview[i]=data[i].gamma;
                    image.SetPixels(preview); image.Apply(); File.WriteAllBytes(path,image.EncodeToPNG()); Object.DestroyImmediate(image);
                }
                return data;
            }
            Material wallMaterial=null, floorMaterial=null;
            try
            {
                Directory.CreateDirectory("Docs/images"); Directory.CreateDirectory("Logs/OpticGlow");
                ShowHalos(false); Capture(null); var off=Capture("Logs/OpticGlow/without-glow.png");
                ShowHalos(true); var on=Capture("Docs/images/OpticGlow.png");
                Require(Differences(off,on)>100,"Optical glow did not render.");
                var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.transform.position=camera.transform.position+camera.transform.forward*3;
                wall.transform.rotation=camera.transform.rotation;
                wall.transform.localScale=new Vector3(5,3,.05f);
                wallMaterial=new Material(Shader.Find("Universal Render Pipeline/Unlit")); wallMaterial.color=Color.black;
                wall.GetComponent<Renderer>().sharedMaterial=wallMaterial;
                var blocked=Capture(null); ShowHalos(false); var blockedOff=Capture(null);
                Require(Differences(blocked,blockedOff)==0,"Glow appeared through an opaque wall."); Object.DestroyImmediate(wall);
                receiver.gameObject.SetActive(false);
                camera.transform.position=new Vector3(.5f,.5f,-6); camera.transform.LookAt(new Vector3(.5f,.5f,.5f));
                ShowHalos(true); var rear=Capture(null); ShowHalos(false); var rearOff=Capture(null);
                Require(Differences(rear,rearOff)==0,"Emitter halo appeared behind its non-optical face.");
                receiver.gameObject.SetActive(true);
                camera.transform.position=targetPoint+new Vector3(1,2.3f,6); camera.transform.LookAt(targetPoint);
                var floor=GameObject.CreatePrimitive(PrimitiveType.Cube);
                floor.transform.position=new Vector3(1.5f,-.05f,1); floor.transform.localScale=new Vector3(5,.1f,4);
                floorMaterial=new Material(Shader.Find("Slima/Toon (Cel + Pixel)"));
                floorMaterial.SetColor("_BaseColor",new Color(.55f,.55f,.55f));
                floorMaterial.SetColor("_AmbientColor",Color.black); floorMaterial.SetFloat("_FallbackLight",0);
                floorMaterial.SetFloat("_OutlineWidth",0); floor.GetComponent<Renderer>().sharedMaterial=floorMaterial;
                var points=parent.GetComponentsInChildren<Light>();
                light.enabled=false; ShowHalos(false);
                foreach(var point in points) point.enabled=false;
                var floorOff=Capture(null);
                emitter.GetComponentInChildren<GameplayOpticGlow>().SetActivated(true);
                receiver.GetComponentInChildren<GameplayOpticGlow>().SetActivated(true);
                var floorOn=Capture("Logs/OpticGlow/front-floor.png"); bool illuminated=false;
                foreach(float x in new[] {.5f,1.5f,2.5f}) foreach(float z in new[] {1.1f,1.3f,1.5f})
                {
                    Vector3 sample=camera.WorldToViewportPoint(new Vector3(x,.001f,z));
                    int index=Mathf.Clamp(Mathf.RoundToInt(sample.y*600),0,599)*1200+Mathf.Clamp(Mathf.RoundToInt(sample.x*1200),0,1199);
                    if(floorOn[index].maxColorComponent>floorOff[index].maxColorComponent+.005f) illuminated=true;
                }
                Require(illuminated,"Optical spotlights did not illuminate the floor ahead.");
                foreach(float x in new[] {.5f,2.5f})
                {
                    Vector3 sample=camera.WorldToViewportPoint(new Vector3(x,1.001f,.5f));
                    int index=Mathf.Clamp(Mathf.RoundToInt(sample.y*600),0,599)*1200+Mathf.Clamp(Mathf.RoundToInt(sample.x*1200),0,1199);
                    Require((new Vector3(floorOn[index].r,floorOn[index].g,floorOn[index].b)-new Vector3(floorOff[index].r,floorOff[index].g,floorOff[index].b)).sqrMagnitude<.000004f,
                        "Optical light illuminated a top surface from below.");
                }
                // Isolate the emitter: its own light must never illuminate its casing or rear floor.
                receiver.gameObject.SetActive(false); light.enabled=true; light.intensity=.35f;
                var emitterLight=emitter.GetComponentInChildren<Light>();
                emitterLight.enabled=false; var directionalOff=Capture(null);
                emitterLight.enabled=true; var directionalOn=Capture(null);
                foreach(var position in new[] {new Vector3(.5f,1.001f,.5f),new Vector3(1.001f,.65f,.5f),new Vector3(.5f,.001f,-.5f)})
                {
                    Vector3 sample=camera.WorldToViewportPoint(position);
                    int index=Mathf.Clamp(Mathf.RoundToInt(sample.y*600),0,599)*1200+Mathf.Clamp(Mathf.RoundToInt(sample.x*1200),0,1199);
                    Require((new Vector3(directionalOn[index].r,directionalOn[index].g,directionalOn[index].b)-new Vector3(directionalOff[index].r,directionalOff[index].g,directionalOff[index].b)).sqrMagnitude<.000004f,
                        "Emitter light leaked onto its top, side or rear floor.");
                }
                receiver.gameObject.SetActive(true);
                light.enabled=true; light.intensity=.35f; ShowHalos(true);
                targetPoint=new Vector3(1.5f,.35f,1); camera.orthographicSize=1.6f;
                camera.transform.position=targetPoint+new Vector3(1,2.3f,6); camera.transform.LookAt(targetPoint);
                Capture("Docs/images/OpticLight.png");
                if (ShaderUtil.ShaderHasError(shared.shader)) throw new Exception("Rendered lamp shader failed.");
            }
            finally { rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(pixels); if(wallMaterial!=null) Object.DestroyImmediate(wallMaterial); if(floorMaterial!=null) Object.DestroyImmediate(floorMaterial); }
            File.WriteAllText("Logs/OpticGlow-validation.txt","PASS: 11 optics, 44 orientations, incoming-face-only emission/halo/light, concurrent matching/mismatched beams, face changes while active, rotated receivers, beam removal, permanent latch, saved receiving faces, reset, nearby front floor illumination, no top/side/rear illumination from emitter, no duplicates, Lamp compatibility and halo occlusion.\n");
            Debug.Log("[Optic Glow] Validation PASS.");
        }
        static void CheckSettings(BlockMapLampHalo[] halos,GameplayOpticGlow glow,float intensity,int litFace=0)
        {
            bool lit=intensity>0;
            var lights=glow.GetComponentsInChildren<Light>();
            for(int face=0;face<lights.Length;face++)
            {
                var light=lights[face]; var turn=Quaternion.Euler(0,face*90,0);
                Require(light.enabled==(lit && face==litFace) && light.type==LightType.Spot && light.color==glow.color,"Optical spotlight state or color is wrong.");
                Require(light.range==glow.lightRange && light.intensity==glow.lightIntensity && light.spotAngle==glow.lightAngle,"Optical spotlight settings are wrong.");
                Require((light.transform.position-glow.transform.TransformPoint(turn*glow.lightLocalPosition)).sqrMagnitude<.000001f &&
                    Vector3.Dot(light.transform.forward,glow.transform.TransformDirection(turn*(Quaternion.Euler(glow.lightTilt,0,0)*Vector3.forward)))>.999f,"Optical light does not point outward with map rotation.");
            }
            var emission=new MaterialPropertyBlock(); glow.opticalSurface.GetPropertyBlock(emission);
            Require(emission.GetFloat("_EmissionStrength")== (lit ? glow.emissionStrength : 0f),"Optical surface emission is wrong.");
            Vector4 expectedMask=Vector4.zero; if(lit) expectedMask[litFace]=1;
            Require(emission.GetVector("_OpticFaceMask")==expectedMask,"A non-receiving sensor face is emitting.");
            for(int face=0;face<halos.Length;face++)
            {
                var halo=halos[face];
                var block=new MaterialPropertyBlock(); halo.GetComponent<Renderer>().GetPropertyBlock(block);
                Require(block.GetColor("_Color")==glow.color && block.GetFloat("_Intensity")== (face==litFace ? intensity : 0f),"Glow color or state is wrong.");
                Require(block.GetFloat("_Size")==glow.diameter && block.GetFloat("_CameraOffset")==glow.cameraOffset,"Glow size or front offset is wrong.");
            }
        }
        static void CheckBeamReception()
        {
            foreach(string receiverId in new[] {"ReceiverBlue_A","Lab_Laser_05"})
            for(int rotation=0;rotation<4;rotation++)
            {
                var root=new GameObject("Beam-controlled light test");
                try
                {
                    var world=root.AddComponent<BlockWorld>();
                    int floor=BlockLibrary.IndexOf("Lab_WallBrick1");
                    for(int x=0;x<5;x++) for(int z=0;z<5;z++) world.Set(new Vector3Int(x,0,z),floor);
                    var source=new Vector3Int(2,1,1); var destination=new Vector3Int(2,1,3);
                    string matching=receiverId=="Lab_Laser_05" ? "Lab_Laser_01" : "Lab_Laser_03";
                    world.Set(source,BlockLibrary.IndexOf("Lab_Laser_02")); world.Set(destination,BlockLibrary.IndexOf(receiverId));
                    world.SetDir(destination,rotation);
                    var gameplay=root.AddComponent<BlockGameplayController>(); gameplay.Initialize(world);
                    void Tick() => typeof(BlockGameplayController).GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(gameplay,null);
                    Light ReceiverLight()
                    {
                        foreach(var view in root.GetComponentsInChildren<GameplayBlockView>())
                            if(view.Cell==destination) return view.GetComponentsInChildren<Light>()[(2-rotation+4)&3];
                        throw new Exception("Receiver view missing.");
                    }
                    Require(!ReceiverLight().enabled,"Receiver lit before receiving a beam.");
                    Tick(); Require(!ReceiverLight().enabled,"Mismatched beam lit receiver.");
                    world.Set(source,BlockLibrary.IndexOf(matching)); Tick();
                    Require(ReceiverLight().enabled,"Matching beam did not light receiver.");
                    foreach(var view in root.GetComponentsInChildren<GameplayBlockView>())
                        if(view.Cell==destination)
                        {
                            int enabled=0; foreach(var light in view.GetComponentsInChildren<Light>()) if(light.enabled) enabled++;
                            Require(enabled==1,"One incoming beam enabled more than one receiver light.");
                        }
                    world.SetDir(source,1); Tick();
                    Require(ReceiverLight().enabled==(receiverId=="Lab_Laser_05"),"Hold/latch lighting after beam removal is wrong.");
                    if(receiverId=="Lab_Laser_05")
                    {
                        var progress=gameplay.CaptureProgress(); gameplay.RestoreProgress(progress); Tick();
                        Require(ReceiverLight().enabled,"Saved permanent receiver lost its receiving face.");
                    }
                    gameplay.ResetState(); Tick(); Require(!ReceiverLight().enabled,"Reset did not switch off receiver.");
                }
                finally { Object.DestroyImmediate(root); }
            }
        }
        static void CheckConcurrentReception()
        {
            var root=new GameObject("Concurrent receiver faces");
            try
            {
                var world=root.AddComponent<BlockWorld>();
                for(int x=0;x<5;x++) for(int z=0;z<5;z++) world.Set(new Vector3Int(x,0,z),BlockLibrary.IndexOf("Lab_WallBrick1"));
                var receiverCell=new Vector3Int(2,1,2); var back=new Vector3Int(2,1,0);
                var right=new Vector3Int(4,1,2); var left=new Vector3Int(0,1,2);
                world.Set(receiverCell,BlockLibrary.IndexOf("ReceiverBlue_A"));
                world.Set(back,BlockLibrary.IndexOf("Lab_Laser_03")); world.SetDir(back,0);
                world.Set(right,BlockLibrary.IndexOf("Lab_Laser_03")); world.SetDir(right,3);
                world.Set(left,BlockLibrary.IndexOf("Lab_Laser_02")); world.SetDir(left,1);
                var gameplay=root.AddComponent<BlockGameplayController>(); gameplay.Initialize(world);
                void Tick()=>typeof(BlockGameplayController).GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(gameplay,null);
                void Check(int mask)
                {
                    foreach(var view in root.GetComponentsInChildren<GameplayBlockView>())
                        if(view.Cell==receiverCell)
                        {
                            var lights=view.GetComponentsInChildren<Light>();
                            for(int face=0;face<4;face++) Require(lights[face].enabled==((mask & (1<<face))!=0),"Concurrent or changing beam lit the wrong face.");
                        }
                }
                Tick(); Check(6);
                world.SetDir(back,1); Tick(); Check(2);
                world.SetDir(right,0); Tick(); Check(0);
            }
            finally { Object.DestroyImmediate(root); }
        }
        static int Differences(Color[] a,Color[] b)
        {
            int count=0; for(int i=0;i<a.Length;i++) if(Mathf.Abs(a[i].r-b[i].r)+Mathf.Abs(a[i].g-b[i].g)+Mathf.Abs(a[i].b-b[i].b)>.002f) count++;
            return count;
        }
        static void Require(bool condition,string message) { if(!condition) throw new Exception(message); }
    }
}
