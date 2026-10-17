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
    public static class SlimaSlidingDoorValidation
    {
        public static void Run()
        {
            SlimaSlidingDoorAssetBuilder.BuildAssets();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single); BlockLibrary.Reload();
            var group=new GameObject("Sliding doors");
            foreach(string id in SlimaSlidingDoorAssetBuilder.DoorIds) for(int direction=0;direction<4;direction++)
            {
                var view=GameplayPrototypeFactory.Create(BlockLibrary.Get(id),BlockGameplayCatalog.Get(id),Vector3Int.zero,direction,group.transform);
                var door=view.GetComponentInChildren<BlockMapDoor>(); var renderer=door.hinge.GetComponentInChildren<Renderer>(true);
                Require(view.GetComponentsInChildren<MeshRenderer>(true).Length==1,"Door contains an extra frame model.");
                Require(renderer.sharedMaterial.GetColor("_OutlineColor")==Color.black,"Editor door has gameplay highlighting.");
                EnableGameHighlight(view);
                Require(renderer.sharedMaterial.GetColor("_OutlineColor")==new Color(1,.72f,.06f,1) && renderer.sharedMaterial.GetFloat("_OutlineWidth")==2,"Game door has no yellow highlight.");
                Require(view.GetComponentInChildren<GameplayMechanismArt>()!=null,"Legacy door model took priority.");
                var texture=(Texture2D)renderer.sharedMaterial.mainTexture;
                Require(texture.width==64 && texture.height==64 && texture.filterMode==FilterMode.Point,"Door texture format is incorrect.");
                Vector3 closed=door.hinge.localPosition; Quaternion rotation=door.hinge.localRotation;
                Require(renderer.enabled && !door.IsOpen && closed==Vector3.zero,"Initial door is open or displaced.");
                view.SetLinkColors(new[] {Color.green,Color.red,Color.red});
                var properties=new MaterialPropertyBlock(); renderer.GetPropertyBlock(properties);
                Require(properties.GetFloat("_LinkColorCount")==3 && renderer.sharedMaterial.mainTexture==texture,"Door link colors lost the metal texture.");
                Require(renderer.sharedMaterial.GetColor("_OutlineColor")==new Color(1,.72f,.06f,1),"Link colors changed the yellow highlight.");
                view.SetActivated(true); door.AdvanceAnimation(door.duration/2);
                Require(renderer.enabled && Vector3.Distance(door.hinge.localPosition,closed+Vector3.right*door.slideDistance*.5f)<.0001f,"Door did not slide through its midpoint.");
                Require(Quaternion.Angle(rotation,door.hinge.localRotation)<.001f,"Sliding door rotated.");
                Vector3 halfway=door.hinge.position;
                view.SetActivated(false);
                Require(Vector3.Distance(door.hinge.position,halfway)<.0001f,"Reversing the animation jumped the door.");
                door.AdvanceAnimation(door.duration); Require(renderer.enabled && door.hinge.localPosition==closed,"Closing did not restore the door.");
                view.SetActivated(true); door.AdvanceAnimation(door.duration);
                Require(door.IsOpen && !renderer.enabled && Vector3.Distance(door.hinge.localPosition,closed+Vector3.right*door.slideDistance)<.0001f,"Open door did not hide after sliding.");
                Require(!door.hinge.gameObject.activeInHierarchy && renderer.shadowCastingMode==ShadowCastingMode.Off,"Hidden door still participates in rendering.");
                view.SetActivated(false); Require(renderer.enabled,"Closing did not reveal the panel."); door.AdvanceAnimation(door.duration);
                Object.DestroyImmediate(view.gameObject);
            }
            CheckSignals();
            CheckLaserActivation();
            RenderPreview(group.transform);
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/SlidingDoor-validation.txt","PASS: all catalog doors including inverted signal, game-only yellow shader outline with no extra mesh unaffected by link colors, 4 orientations each, sliding/reversal/reset, inactive hidden leaf with shadows off, walk-on lever/link signals, 120-frame laser activation stability, GPU hidden/disabled/force-hidden/camera-excluded leaf matches removed leaf under spotlight and custom outline masks, rendered animation.\n");
            Debug.Log("[Sliding Door] Validation PASS.");
        }

        static void CheckSignals()
        {
            foreach(string id in new[] {"Door","Door_B"})
            {
                var root=new GameObject("Door signals");
                try
                {
                    var world=root.AddComponent<BlockWorld>();
                    for(int x=0;x<5;x++) for(int z=0;z<5;z++) world.Set(new Vector3Int(x,0,z),BlockLibrary.IndexOf("Lab_Floor_01"));
                    var lever=new Vector3Int(1,1,2); var cell=new Vector3Int(3,1,2);
                    world.Set(lever,BlockLibrary.IndexOf("Lab_Lever")); world.Set(cell,BlockLibrary.IndexOf(id));
                    world.SetLinkId(lever,"SlidingDoor"); world.SetLinkId(cell,"SlidingDoor");
                    var gameplay=root.AddComponent<BlockGameplayController>(); gameplay.Initialize(world);
                    var actor=new GameObject("Interaction actor").AddComponent<SlimeActor>(); actor.transform.SetParent(root.transform);
                    actor.Initialize(new SlimeState(),null,1,0); actor.Mover.Teleport(new Vector3Int(1,1,1),0);
                    BlockMapDoor Door()=>root.GetComponentInChildren<BlockMapDoor>();
                    void Tick()=>typeof(BlockGameplayController).GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(gameplay,null);
                    Tick(); Require(gameplay.IsBeamBlocked(cell) && !gameplay.CanEnter(actor,cell,0,false) && !Door().IsOpen,"Closed door is logically open.");
                    actor.Mover.Teleport(lever,0); gameplay.ProcessCellEntry(actor,lever); Tick(); Door().AdvanceAnimation(Door().duration);
                    Require(!gameplay.IsBeamBlocked(cell) && gameplay.CanEnter(actor,cell,0,false) && Door().IsOpen,"Open door is logically closed.");
                    Require(!Door().hinge.GetComponentInChildren<Renderer>(true).enabled,"Open linked door remained visible.");
                    actor.Mover.Teleport(new Vector3Int(1,1,1),0); actor.Mover.Teleport(lever,0); gameplay.ProcessCellEntry(actor,lever);
                    Tick(); Door().AdvanceAnimation(Door().duration);
                    Require(gameplay.IsBeamBlocked(cell) && !Door().IsOpen && Door().hinge.GetComponentInChildren<Renderer>().enabled,"Linked closing reversed the model state.");
                    gameplay.ResetState(); Require(!Door().IsOpen && Door().hinge.GetComponentInChildren<Renderer>().enabled,"Reset did not restore a visible closed door.");
                }
                finally { Object.DestroyImmediate(root); }
            }
        }

        static void CheckLaserActivation()
        {
            foreach(string id in SlimaSlidingDoorAssetBuilder.DoorIds)
            {
            var root=new GameObject("Laser door stability");
            try
            {
                var world=root.AddComponent<BlockWorld>();
                for(int x=0;x<5;x++) for(int z=0;z<5;z++) world.Set(new Vector3Int(x,0,z),BlockLibrary.IndexOf("Lab_Floor_01"));
                var source=new Vector3Int(0,1,2); var receiver=new Vector3Int(3,1,2); var cell=new Vector3Int(2,1,3);
                world.Set(source,BlockLibrary.IndexOf("Lab_Laser_03")); world.SetDir(source,1);
                world.Set(receiver,BlockLibrary.IndexOf("ReceiverBlue_A")); world.Set(cell,BlockLibrary.IndexOf(id));
                world.SetLinkId(receiver,"LaserDoor"); world.SetLinkId(cell,"LaserDoor");
                var gameplay=root.AddComponent<BlockGameplayController>(); gameplay.Initialize(world);
                var door=root.GetComponentInChildren<BlockMapDoor>();
                var panel=door.hinge.GetComponentInChildren<Renderer>(true);
                bool inverted=BlockGameplayCatalog.Get(id).invertSignal;
                for(int frame=0;frame<120;frame++)
                {
                    root.GetComponent<BlockOpticsSystem>().MarkDirty();
                    typeof(BlockGameplayController).GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(gameplay,null);
                    door.AdvanceAnimation(door.duration/12);
                    Require(door.IsOpen==!inverted,"Steady matching laser toggled the door repeatedly: "+id);
                    if(frame>=12) Require(panel.enabled==inverted && door.hinge.gameObject.activeInHierarchy==inverted &&
                        panel.shadowCastingMode==(inverted ? ShadowCastingMode.On : ShadowCastingMode.Off),"Laser changed settled door visibility: "+id);
                }
                world.SetDir(source,0);
                typeof(BlockGameplayController).GetMethod("Update",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(gameplay,null);
                door.AdvanceAnimation(door.duration);
                Require(door.IsOpen==inverted && panel.enabled==!inverted && door.hinge.gameObject.activeInHierarchy==!inverted &&
                    panel.shadowCastingMode==(inverted ? ShadowCastingMode.Off : ShadowCastingMode.On),"Beam removal did not restore the expected door state: "+id);
            }
            finally { Object.DestroyImmediate(root); }
            }
        }

        static void CheckHiddenPasses(GameplayBlockView view,Camera camera,Func<Color[]> capture)
        {
            var pipeline=(UniversalRenderPipelineAsset)(QualitySettings.renderPipeline ?? GraphicsSettings.renderPipelineAsset);
            var serialized=new SerializedObject(pipeline);
            int index=serialized.FindProperty("m_DefaultRendererIndex").intValue;
            var data=(UniversalRendererData)serialized.FindProperty("m_RendererDataList").GetArrayElementAtIndex(index).objectReferenceValue;
            var feature=(SlimaPuzzle.Rendering.SlimaToonOutlineFeature)data.rendererFeatures.Find(f=>f is SlimaPuzzle.Rendering.SlimaToonOutlineFeature);
            Require(feature!=null,"Production outline feature is missing.");
            var original=feature.settings; string originalName=view.name;
            var door=view.GetComponentInChildren<BlockMapDoor>();
            var panel=door.hinge.GetComponentInChildren<MeshRenderer>(true);
            var lamp=new GameObject("Door regression spotlight").AddComponent<Light>();
            lamp.type=LightType.Spot; lamp.color=Color.blue; lamp.intensity=5; lamp.range=6;
            lamp.spotAngle=100; lamp.shadows=LightShadows.Hard;
            lamp.transform.position=new Vector3(1,2,-1); lamp.transform.LookAt(new Vector3(1.4f,0,.5f));
            try
            {
                view.name="GhostDoor";
                feature.settings=new SlimaPuzzle.Rendering.SlimaToonOutlineFeature.Settings
                {
                    crispNameFilter="GhostDoor",protectNameFilter="GhostDoor",protectMaterialFilter="",
                    maskGroups=new System.Collections.Generic.List<SlimaPuzzle.Rendering.SlimaToonOutlineFeature.MaskGroup>
                    {
                        new SlimaPuzzle.Rendering.SlimaToonOutlineFeature.MaskGroup {name="Door ghost test",nameContains="GhostDoor",materialContains="",thickness=12}
                    }
                };
                typeof(SlimaPuzzle.Rendering.SlimaToonOutlineFeature).GetField("_rescan",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(feature,0);
                view.SetActivated(false); door.AdvanceAnimation(door.duration); capture();
                view.SetActivated(true); door.AdvanceAnimation(door.duration);
                var hidden=capture();
                // Exercise cached disabled, force-hidden and camera-excluded renderers too.
                door.hinge.gameObject.SetActive(true); panel.enabled=false; var disabled=capture();
                panel.enabled=true; panel.forceRenderingOff=true; var forced=capture();
                panel.forceRenderingOff=false; panel.gameObject.layer=30; camera.cullingMask &= ~(1<<30); var excluded=capture();
                Object.DestroyImmediate(panel); var absent=capture();
                foreach(var pixels in new[] {hidden,disabled,forced,excluded})
                {
                    int differences=0;
                    for(int i=0;i<pixels.Length;i++)
                        if(Mathf.Abs(pixels[i].r-absent[i].r)+Mathf.Abs(pixels[i].g-absent[i].g)+Mathf.Abs(pixels[i].b-absent[i].b)>.015f) differences++;
                    Require(differences==0,"A hidden door still affected the spotlight, shadows or custom outline masks: "+differences+" pixels.");
                }
            }
            finally
            {
                feature.settings=original; view.name=originalName;
                typeof(SlimaPuzzle.Rendering.SlimaToonOutlineFeature).GetField("_rescan",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(feature,0);
                Object.DestroyImmediate(lamp.gameObject);
            }
        }

        static void RenderPreview(Transform parent)
        {
            var view=GameplayPrototypeFactory.Create(BlockLibrary.Get("Door"),BlockGameplayCatalog.Get("Door"),Vector3Int.zero,0,parent);
            EnableGameHighlight(view);
            var door=view.GetComponentInChildren<BlockMapDoor>();
            var camera=new GameObject("Door preview camera").AddComponent<Camera>();
            camera.orthographic=true; camera.orthographicSize=.8f; camera.aspect=1.6f;
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.055f,.07f,.10f);
            camera.transform.position=new Vector3(1,1.7f,-3); camera.transform.LookAt(new Vector3(.9f,.45f,.5f));
            camera.GetUniversalAdditionalCameraData().renderPostProcessing=false;
            var light=new GameObject("Door key light").AddComponent<Light>(); light.type=LightType.Directional;
            light.intensity=1.1f; light.transform.rotation=Quaternion.Euler(40,-25,0);
            RenderSettings.ambientLight=new Color(.2f,.2f,.2f); RenderSettings.fog=false;
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube); floor.transform.position=new Vector3(1,-.04f,.5f);
            floor.transform.localScale=new Vector3(2.6f,.08f,1.5f);
            var floorMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit")); floorMaterial.color=new Color(.12f,.13f,.15f);
            floor.GetComponent<Renderer>().sharedMaterial=floorMaterial;
            var rt=new RenderTexture(800,500,24); rt.Create(); var image=new Texture2D(800,500,TextureFormat.RGBA32,false);
            Directory.CreateDirectory("Logs/SlidingDoorFrames"); Directory.CreateDirectory("Docs/images");
            void Capture(string path)
            {
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest {destination=rt});
                var old=RenderTexture.active; RenderTexture.active=rt; image.ReadPixels(new Rect(0,0,800,500),0,0); image.Apply();
                RenderTexture.active=old; File.WriteAllBytes(path,image.EncodeToPNG());
            }
            try
            {
                Capture("Docs/images/SlidingDoorClosed.png");
                var quadrants=new int[4]; var pixels=image.GetPixels();
                for(int y=0;y<image.height;y++) for(int x=0;x<image.width;x++)
                {
                    var pixel=pixels[y*image.width+x];
                    if(pixel.r>.8f && pixel.g>.5f && pixel.b<.2f)
                        quadrants[(x>280 ? 1 : 0)+(y>250 ? 2 : 0)]++;
                }
                foreach(int count in quadrants) Require(count>10,"Yellow highlight is missing from part of the door contour.");
                for(int i=0;i<40;i++)
                {
                    if(i==1) view.SetActivated(true);
                    if(i==23) view.SetActivated(false);
                    door.AdvanceAnimation(i==0 ? 0 : door.duration/12);
                    Capture($"Logs/SlidingDoorFrames/{i:D2}.png");
                    if(i==6) Capture("Docs/images/SlidingDoorHalf.png");
                    if(i==18) Capture("Docs/images/SlidingDoorOpen.png");
                }
                // Program link colors still multiply the new grayscale texture.
                view.SetLinkColors(new[] {Color.green,Color.red,Color.red});
                Capture("Docs/images/SlidingDoorLinked.png");
                Require(!ShaderUtil.ShaderHasError(door.hinge.GetComponentInChildren<Renderer>(true).sharedMaterial.shader),"Door shader failed.");
                Color[] CapturePixels()
                {
                    RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest {destination=rt});
                    var old=RenderTexture.active; RenderTexture.active=rt;
                    image.ReadPixels(new Rect(0,0,800,500),0,0); image.Apply(); RenderTexture.active=old;
                    return image.GetPixels();
                }
                CheckWallOutline(view,camera,CapturePixels);
                CheckHiddenPasses(view,camera,CapturePixels);
            }
            finally { rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(image); Object.DestroyImmediate(floorMaterial); }
        }
        static void CheckWallOutline(GameplayBlockView view,Camera camera,Func<Color[]> capture)
        {
            var pipeline=(UniversalRenderPipelineAsset)(QualitySettings.renderPipeline ?? GraphicsSettings.renderPipelineAsset);
            var serialized=new SerializedObject(pipeline);
            var data=(UniversalRendererData)serialized.FindProperty("m_RendererDataList").GetArrayElementAtIndex(serialized.FindProperty("m_DefaultRendererIndex").intValue).objectReferenceValue;
            var feature=(SlimaPuzzle.Rendering.SlimaToonOutlineFeature)data.rendererFeatures.Find(f=>f is SlimaPuzzle.Rendering.SlimaToonOutlineFeature);
            var original=feature.settings;
            var door=view.GetComponentInChildren<BlockMapDoor>();
            var panel=door.hinge.GetComponentInChildren<MeshRenderer>(true);
            var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name="Door occlusion wall"; wall.transform.position=new Vector3(-.49f,.5f,.5f);
            var wallMaterial=new Material(Shader.Find("Universal Render Pipeline/Unlit")) {name="Regression_Wall",color=Color.gray};
            var marker=new Material(Shader.Find("Universal Render Pipeline/Unlit")) {color=Color.green};
            wall.GetComponent<Renderer>().sharedMaterial=wallMaterial;
            var originalMaterial=panel.sharedMaterial; var parent=panel.transform.parent;
            var originalProperties=new MaterialPropertyBlock(); panel.GetPropertyBlock(originalProperties);
            var wallGroup=new SlimaPuzzle.Rendering.SlimaToonOutlineFeature.MaskGroup {name="墙面",materialContains="_Wall",thickness=0};
            void Rescan()=>typeof(SlimaPuzzle.Rendering.SlimaToonOutlineFeature).GetField("_rescan",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(feature,0);
            bool Green(Color c)=>c.g>.8f && c.r<.1f && c.b<.1f;
            int Differences(Color[] baseline,Color[] outlined)
            {
                int count=0;
                for(int i=0;i<baseline.Length;i++) if(Green(baseline[i]) && !Green(outlined[i])) count++;
                return count;
            }
            try
            {
                feature.settings=new SlimaPuzzle.Rendering.SlimaToonOutlineFeature.Settings
                {pixelateAmount=0,crispSpecials=false,protectNameFilter="",protectMaterialFilter="",outlineColor=Color.magenta};
                foreach(var group in feature.settings.maskGroups) group.thickness=0;
                feature.settings.maskGroups.RemoveAll(group=>group.name=="墙面"); feature.settings.maskGroups.Add(wallGroup);
                view.SetActivated(false); door.AdvanceAnimation(door.duration); panel.sharedMaterial=marker; panel.SetPropertyBlock(null);
                // A detached panel proves the wall really overlaps the door in this scene.
                panel.transform.SetParent(null,true); Rescan(); capture();
                var baseline=capture(); wallGroup.thickness=12; capture(); var unprotected=capture();
                Require(Differences(baseline,unprotected)>20,"Wall/door overlap regression scene does not reproduce the issue.");
                panel.transform.SetParent(parent,true); Rescan(); capture(); var protectedPixels=capture();
                Require(Differences(baseline,protectedPixels)==0,"Wall outline was drawn over the door panel.");
                panel.sharedMaterial=originalMaterial; panel.SetPropertyBlock(originalProperties); wallGroup.thickness=0; capture(); var yellowBaseline=capture();
                wallGroup.thickness=12; capture(); var yellowProtected=capture();
                int overwritten=0;
                for(int i=0;i<yellowBaseline.Length;i++)
                    if(yellowBaseline[i].r>.8f && yellowBaseline[i].g>.5f && yellowBaseline[i].b<.2f &&
                        yellowProtected[i].b>.6f && yellowProtected[i].g<.3f) overwritten++;
                Require(overwritten==0,"Wall outline covered the door's yellow highlight: "+overwritten+" pixels.");
                // Hidden panels must stop shielding wall lines too, even when cached.
                view.SetActivated(true); door.AdvanceAnimation(door.duration); var hidden=capture();
                panel.transform.SetParent(null,true); Rescan(); capture(); var absent=capture();
                for(int i=0;i<hidden.Length;i++) Require(Vector3.Distance((Vector4)hidden[i],(Vector4)absent[i])<.015f,"Hidden door left an outline protection mask.");
                Debug.Log("[Sliding Door] Wall outline overlap and hidden protection PASS.");
            }
            finally
            {
                panel.transform.SetParent(parent,true); panel.sharedMaterial=originalMaterial; panel.SetPropertyBlock(originalProperties);
                view.SetActivated(false); door.AdvanceAnimation(door.duration);
                feature.settings=original; Rescan();
                Object.DestroyImmediate(wall); Object.DestroyImmediate(wallMaterial); Object.DestroyImmediate(marker);
            }
        }
        static void EnableGameHighlight(GameplayBlockView view)
        {
            typeof(GameplayBlockView).GetMethod("InitializeDoorMaterials",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(view,new object[] {true});
        }
        static void Require(bool condition,string message) { if(!condition) throw new Exception(message); }
    }
}
