using System;
using System.Collections.Generic;
using System.IO;
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
    public static class SlimaControlArtValidation
    {
        public static void Run()
        {
            SlimaControlAssetBuilder.BuildAssets();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            BlockLibrary.Reload();
            var display = new GameObject("Controls");
            for (int i=0;i<SlimaControlAssetBuilder.Ids.Length;i++)
            {
                string id = SlimaControlAssetBuilder.Ids[i];
                var def = BlockLibrary.Get(id); var profile = BlockGameplayCatalog.Get(id);
                Require(def != null && def.previewPrefab != null,"Missing prefab: " + id);
                for (int direction=0;direction<4;direction++)
                {
                    var view = GameplayPrototypeFactory.Create(def,profile,Vector3Int.zero,direction,display.transform);
                    var art = view.GetComponentInChildren<GameplayControlArt>();
                    Require(art != null && art.movingPart != null && art.colorParts.Length>0,"Missing animation/tint parts: " + id);
                    if (art.isLever)
                    {
                        var handle = art.colorParts[0].GetComponent<MeshFilter>().sharedMesh.bounds.size;
                        Require(Mathf.Abs(handle.x-.10f)<.0001f && Mathf.Abs(handle.y-.24f)<.0001f && Mathf.Abs(handle.z-.10f)<.0001f,"Incorrect narrow/long lever handle: " + id);
                    }
                    Bounds bounds = BoundsOf(view);
                    if (profile.Kind == GameplayBlockKind.PressurePlate)
                        Require(Mathf.Abs(bounds.size.x - (id.Contains("Large") ? 62f : 59f)/64f)<.0001f,"Incorrect plate width: " + id);
                    Require(bounds.min.y >= -.0001f && bounds.max.y < 1 && bounds.size.x<=1 && bounds.size.z<=1,"Outside grid: " + id);
                    Vector3 origin = art.movingPart.localPosition;
                    Quaternion closed = art.movingPart.localRotation;
                    view.SetActivated(true); art.AdvanceAnimation(art.duration/2);
                    if (art.isLever) Require(Quaternion.Angle(closed,art.movingPart.localRotation)>10,"Lever did not animate");
                    else Require(art.movingPart.localScale.y<1 && art.movingPart.localScale.y>art.pressedHeight,"Press did not interpolate");
                    art.AdvanceAnimation(art.duration);
                    Require(art.movingPart.localPosition==origin,"Bottom pivot moved: " + id);
                    view.SetActivated(false); art.AdvanceAnimation(art.duration);
                    Require(art.isLever ? Quaternion.Angle(closed,art.movingPart.localRotation)<.001f : art.movingPart.localScale==Vector3.one,"Reset failed: " + id);
                    var block = new MaterialPropertyBlock();
                    view.SetLinkColors(new[] { Color.red }); art.colorParts[0].GetPropertyBlock(block);
                    Require(block.GetColor("_BaseColor")==Color.red,"Program color failed: " + id);
                    foreach (var r in view.GetComponentsInChildren<Renderer>())
                    {
                        var t=(Texture2D)r.sharedMaterial.mainTexture;
                        Require(t.width==64 && t.height==64 && t.filterMode==FilterMode.Point,"Invalid pixel texture: " + id);
                        var importer=(TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t));
                        Require(!importer.mipmapEnabled && importer.textureCompression==TextureImporterCompression.Uncompressed,"Invalid texture sampling: " + id);
                    }
                    Object.DestroyImmediate(view.gameObject);
                }
            }
            ValidateLinks();

            // 前排为中性模型，后排为按下/拉动状态；右侧展示单色与多色门。
            string[] previewIds={ "PressurePlateLarge_A","Lab_PressurePlate","Lab_Lever","Button_A" };
            for (int row=0;row<2;row++) for (int col=0;col<4;col++)
            {
                string id=previewIds[col];
                var view=GameplayPrototypeFactory.Create(BlockLibrary.Get(id),BlockGameplayCatalog.Get(id),new Vector3Int(col*2,0,row*2),0,display.transform);
                if (row==1)
                {
                    view.SetLinkColors(new[] { col%2==0 ? Color.green : Color.red });
                    view.SetActivated(true); view.GetComponentInChildren<GameplayControlArt>().AdvanceAnimation(1);
                }
            }
            var door=GameplayPrototypeFactory.Create(BlockLibrary.Get("Door"),BlockGameplayCatalog.Get("Door"),new Vector3Int(8,0,0),0,display.transform);
            // Imported demo OBJ has a centre origin; place its preview above the floor.
            door.transform.position+=Vector3.up*.5f;
            door.SetLinkColors(new[] { Color.green,Color.red });
            var doorSingle=GameplayPrototypeFactory.Create(BlockLibrary.Get("Door_B"),BlockGameplayCatalog.Get("Door_B"),new Vector3Int(8,0,2),0,display.transform);
            doorSingle.SetLinkColors(new[] { Color.green });
            var cameraObject=new GameObject("Camera"); cameraObject.tag="MainCamera";
            var camera=cameraObject.AddComponent<Camera>(); camera.orthographic=true; camera.orthographicSize=3.25f;
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.10f,.12f,.16f);
            camera.transform.position=new Vector3(4.5f,7,-9); camera.transform.LookAt(new Vector3(4.5f,.1f,1.2f));
            cameraObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=false;
            var light=new GameObject("Key").AddComponent<Light>(); light.type=LightType.Directional; light.intensity=1.15f;
            light.transform.rotation=Quaternion.Euler(45,-25,0); RenderSettings.ambientLight=new Color(.4f,.4f,.4f);
            var floor=GameObject.CreatePrimitive(PrimitiveType.Cube); floor.name="PreviewFloor";
            floor.transform.position=new Vector3(4.5f,-.12f,1.5f); floor.transform.localScale=new Vector3(10.5f,.2f,5f);
            var floorMaterial=new Material(Shader.Find("Universal Render Pipeline/Lit")) { color=new Color(.22f,.24f,.27f) };
            floor.GetComponent<Renderer>().sharedMaterial=SavePreviewMaterial("Floor",floorMaterial);
            SavePreviewColors(display);
            EditorSceneManager.SaveScene(scene,"Assets/_Game/Scenes/ControlShowcase.unity");
            Object.DestroyImmediate(Render(camera,"Docs/images/ControlModels.png",1800,900));
            ValidateDoorPixels(door);
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/ControlArt-validation.txt","PASS: 7 prefabs, 28 facing/size/animation/reset checks, 64px Point textures, single/multiple binding colors, program tint, unbinding, rebinding, door GPU stripe pixels.\n");
            Debug.Log("[Slima Controls] Validation PASS");
        }

        static void ValidateLinks()
        {
            var root=new GameObject("Link validation"); var world=root.AddComponent<BlockWorld>();
            var a=new Vector3Int(1,1,1); var b=new Vector3Int(2,1,1); var d=new Vector3Int(3,1,1);
            world.Set(a,BlockLibrary.IndexOf("Button_A")); world.Set(b,BlockLibrary.IndexOf("Lab_PressurePlate")); world.Set(d,BlockLibrary.IndexOf("Door"));
            foreach (var cell in new[] { a,b,d }) world.SetLinkId(cell,"G001");
            var gameplay=root.AddComponent<BlockGameplayController>(); gameplay.Initialize(world);
            gameplay.SetMechanismColor(a,Color.green); gameplay.SetMechanismColor(b,Color.red);
            var views=root.GetComponentsInChildren<GameplayBlockView>(); var mpb=new MaterialPropertyBlock();
            foreach (var view in views)
            {
                var r=view.Profile.Kind==GameplayBlockKind.Door ? view.GetComponentInChildren<Renderer>() : view.GetComponentInChildren<GameplayControlArt>().colorParts[0];
                r.GetPropertyBlock(mpb);
                Require(mpb.GetFloat("_LinkColorCount")== (view.Cell==d ? 2 : 1),"Wrong source count");
                if (view.Cell!=d) Require(mpb.GetColor("_BaseColor")== (view.Cell==a ? Color.green : Color.red),"Wrong source tint");
            }
            world.SetLinkId(b,""); BlockLinkVisuals.Apply(world,views);
            foreach (var view in views) if (view.Cell==b)
            { view.GetComponentInChildren<GameplayControlArt>().colorParts[0].GetPropertyBlock(mpb); Require(mpb.GetFloat("_LinkColorCount")==0,"Unbound source still colored"); }
            world.SetLinkId(b,"G001"); BlockLinkVisuals.Apply(world,views);
            foreach (var view in views) if (view.Cell==d)
            { view.GetComponentInChildren<Renderer>().GetPropertyBlock(mpb); Require(mpb.GetFloat("_LinkColorCount")==2,"Rebinding failed"); }
            Object.DestroyImmediate(root);
        }

        static void ValidateDoorPixels(GameplayBlockView door)
        {
            var camera=new GameObject("Door GPU camera").AddComponent<Camera>();
            camera.gameObject.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing=false;
            camera.orthographic=true; camera.orthographicSize=.7f;
            var layers=new Dictionary<GameObject,int>();
            foreach(var item in door.GetComponentsInChildren<Transform>())
            { layers[item.gameObject]=item.gameObject.layer; item.gameObject.layer=30; }
            camera.cullingMask=1<<30;
            Quaternion original=door.transform.rotation;
            for (int direction=0;direction<4;direction++)
            {
                door.transform.rotation=Quaternion.Euler(0,direction*90,0);
                camera.transform.position=door.transform.position+door.transform.rotation*Vector3.back*3;
                camera.transform.LookAt(door.transform.position);
                var image=Render(camera,"Logs/ControlDoorStripes_"+direction+".png",512,512);
                var left=image.GetPixel(180,256); var right=image.GetPixel(330,256);
                Require(left.g>left.r*1.4f && right.r>right.g*1.4f,"GPU door stripes/facing failed: " + direction);
                Object.DestroyImmediate(image);
            }
            door.transform.rotation=original;
            door.SetLinkColors(new[] { Color.green,Color.red,Color.red });
            camera.transform.position=door.transform.position+Vector3.back*3; camera.transform.LookAt(door.transform.position);
            var weighted=Render(camera,"Logs/ControlDoorProportions.png",512,512);
            Require(weighted.GetPixel(140,256).g>weighted.GetPixel(140,256).r*1.4f &&
                weighted.GetPixel(240,256).r>weighted.GetPixel(240,256).g*1.4f,"Door did not show 1/3 green and 2/3 red");
            Object.DestroyImmediate(weighted); Object.DestroyImmediate(camera.gameObject);
            foreach(var item in layers) item.Key.layer=item.Value;
        }

        static Material SavePreviewMaterial(string name,Material material)
        {
            string path="Assets/_Game/Art/Materials/Controls/Showcase_"+name+".mat";
            var existing=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(existing==null) { AssetDatabase.CreateAsset(material,path); return material; }
            EditorUtility.CopySerialized(material,existing); Object.DestroyImmediate(material); return existing;
        }

        static void SavePreviewColors(GameObject display)
        {
            var palette=new Texture2D(2,1,TextureFormat.RGBA32,false)
            { filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp };
            palette.SetPixels(new[] { Color.green,Color.red }); palette.Apply();
            string path="Assets/_Game/Art/Textures/Controls/ShowcasePalette.asset";
            var existing=AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if(existing==null) AssetDatabase.CreateAsset(palette,path);
            else { EditorUtility.CopySerialized(palette,existing); Object.DestroyImmediate(palette); palette=existing; }
            int index=0; var block=new MaterialPropertyBlock();
            foreach(var view in display.GetComponentsInChildren<GameplayBlockView>())
            foreach(var renderer in view.GetComponentsInChildren<Renderer>())
            {
                renderer.GetPropertyBlock(block);
                float count=block.GetFloat("_LinkColorCount");
                if(count==0 && view.Profile.Kind!=GameplayBlockKind.Door) continue;
                var material=new Material(renderer.sharedMaterial);
                material.SetColor("_BaseColor",block.GetColor("_BaseColor"));
                if(count>1)
                {
                    material.SetTexture("_LinkPalette",palette); material.SetFloat("_LinkColorCount",count);
                    material.SetVector("_LinkAxis",block.GetVector("_LinkAxis")); material.SetVector("_LinkRange",block.GetVector("_LinkRange"));
                }
                var saved=SavePreviewMaterial((index++).ToString(),material);
                var slots=renderer.sharedMaterials; for(int i=0;i<slots.Length;i++) slots[i]=saved;
                renderer.sharedMaterials=slots;
            }
            AssetDatabase.SaveAssets();
        }

        static Texture2D Render(Camera camera,string path,int width,int height)
        {
            var rt=new RenderTexture(width,height,24); rt.Create();
            var image=new Texture2D(width,height,TextureFormat.RGBA32,false);
            var old=RenderTexture.active;
            try
            {
                for(int i=0;i<2;i++) RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest { destination=rt });
                RenderTexture.active=rt; image.ReadPixels(new Rect(0,0,width,height),0,0); image.Apply();
                Directory.CreateDirectory(Path.GetDirectoryName(path)); File.WriteAllBytes(path,image.EncodeToPNG());
            }
            finally { RenderTexture.active=old; rt.Release(); Object.DestroyImmediate(rt); }
            return image;
        }

        static Bounds BoundsOf(GameplayBlockView view)
        {
            var renderers=view.GetComponentsInChildren<Renderer>(); Bounds b=renderers[0].bounds;
            foreach(var r in renderers) b.Encapsulate(r.bounds); return b;
        }
        static void Require(bool condition,string message) { if(!condition) throw new Exception(message); }
    }
}
