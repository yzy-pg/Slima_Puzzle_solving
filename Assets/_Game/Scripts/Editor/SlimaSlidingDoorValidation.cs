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
            foreach(string id in new[] {"Door","Door_B"}) for(int direction=0;direction<4;direction++)
            {
                var view=GameplayPrototypeFactory.Create(BlockLibrary.Get(id),BlockGameplayCatalog.Get(id),Vector3Int.zero,direction,group.transform);
                var door=view.GetComponentInChildren<BlockMapDoor>(); var renderer=view.GetComponentInChildren<Renderer>();
                Require(view.GetComponentInChildren<GameplayMechanismArt>()!=null,"Legacy door model took priority.");
                var texture=(Texture2D)renderer.sharedMaterial.mainTexture;
                Require(texture.width==64 && texture.height==64 && texture.filterMode==FilterMode.Point,"Door texture format is incorrect.");
                Vector3 closed=door.hinge.localPosition; Quaternion rotation=door.hinge.localRotation;
                Require(renderer.enabled && !door.IsOpen && closed==Vector3.zero,"Initial door is open or displaced.");
                view.SetLinkColors(new[] {Color.green,Color.red,Color.red});
                var properties=new MaterialPropertyBlock(); renderer.GetPropertyBlock(properties);
                Require(properties.GetFloat("_LinkColorCount")==3 && renderer.sharedMaterial.mainTexture==texture,"Door link colors lost the metal texture.");
                view.SetActivated(true); door.AdvanceAnimation(door.duration/2);
                Require(renderer.enabled && Vector3.Distance(door.hinge.localPosition,closed+Vector3.right*door.slideDistance*.5f)<.0001f,"Door did not slide through its midpoint.");
                Require(Quaternion.Angle(rotation,door.hinge.localRotation)<.001f,"Sliding door rotated.");
                Vector3 halfway=door.hinge.position;
                view.SetActivated(false);
                Require(Vector3.Distance(door.hinge.position,halfway)<.0001f,"Reversing the animation jumped the door.");
                door.AdvanceAnimation(door.duration); Require(renderer.enabled && door.hinge.localPosition==closed,"Closing did not restore the door.");
                view.SetActivated(true); door.AdvanceAnimation(door.duration);
                Require(door.IsOpen && !renderer.enabled && Vector3.Distance(door.hinge.localPosition,closed+Vector3.right*door.slideDistance)<.0001f,"Open door did not hide after sliding.");
                view.SetActivated(false); Require(renderer.enabled,"Closing did not reveal the panel."); door.AdvanceAnimation(door.duration);
                Object.DestroyImmediate(view.gameObject);
            }
            CheckSignals();
            RenderPreview(group.transform);
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/SlidingDoor-validation.txt","PASS: Door and Door_B, 8 orientations, closed pose, rightward translation, no rotation, hide after animation, interrupted reversal, closing restores visibility, lever/link signals, movement/beam blocking, reset, metal texture retained with 3 link colors, rendered animation.\n");
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
                    Require(gameplay.TryInteract(actor),"Lever interaction failed."); Tick(); Door().AdvanceAnimation(Door().duration);
                    Require(!gameplay.IsBeamBlocked(cell) && gameplay.CanEnter(actor,cell,0,false) && Door().IsOpen,"Open door is logically closed.");
                    Require(!Door().hinge.GetComponentInChildren<Renderer>().enabled,"Open linked door remained visible.");
                    gameplay.TryInteract(actor); Tick(); Door().AdvanceAnimation(Door().duration);
                    Require(gameplay.IsBeamBlocked(cell) && !Door().IsOpen && Door().hinge.GetComponentInChildren<Renderer>().enabled,"Linked closing reversed the model state.");
                    gameplay.ResetState(); Require(!Door().IsOpen && Door().hinge.GetComponentInChildren<Renderer>().enabled,"Reset did not restore a visible closed door.");
                }
                finally { Object.DestroyImmediate(root); }
            }
        }

        static void RenderPreview(Transform parent)
        {
            var view=GameplayPrototypeFactory.Create(BlockLibrary.Get("Door"),BlockGameplayCatalog.Get("Door"),Vector3Int.zero,0,parent);
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
                Require(!ShaderUtil.ShaderHasError(view.GetComponentInChildren<Renderer>().sharedMaterial.shader),"Door shader failed.");
            }
            finally { rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(image); Object.DestroyImmediate(floorMaterial); }
        }
        static void Require(bool condition,string message) { if(!condition) throw new Exception(message); }
    }
}
