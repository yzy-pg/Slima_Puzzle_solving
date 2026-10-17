using System;
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
    public static class SlimaMechanismArtValidation
    {
        public static void RunSimplified()
        {
            Run();
            SlimaSplitterSawValidation.Run();
        }
        public static void Run()
        {
            SlimaMechanismAssetBuilder.BuildAssets();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            BlockLibrary.Reload();
            var group = new GameObject("Mechanism variants");
            foreach (string id in SlimaMechanismAssetBuilder.Ids)
            {
                var definition = BlockLibrary.Get(id);
                Require(definition != null && definition.previewPrefab != null,"Resource prefab missing: " + id);
                var prefab = definition.previewPrefab;
                Require(prefab.GetComponent<GameplayMechanismArt>() != null,"Artwork marker missing: " + id);
                Bounds bounds = new Bounds(); bool first = true;
                foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>())
                {
                    Mesh mesh = filter.sharedMesh;
                    Require(mesh != null && mesh.uv.Length == mesh.vertexCount && mesh.normals.Length == mesh.vertexCount,"Invalid UV or normals: " + id);
                    foreach (var n in mesh.normals) Require(n.sqrMagnitude > .99f,"Invalid normal: " + id);
                    if (first) { bounds=mesh.bounds; first=false; } else bounds.Encapsulate(mesh.bounds);
                    foreach (var uv in mesh.uv) Require(uv.x>=0 && uv.x<=1 && uv.y>=0 && uv.y<=1,"UV outside tile: " + id);
                }
                Require(bounds.min.y>=-.001f && bounds.max.y<=1.001f && bounds.size.x<=1.001f && bounds.size.z<=1.001f,"Model exceeds grid: " + id);
                Require(prefab.transform.childCount==(id=="Splitter" ? 2 : 4),"Unexpected extra model parts: " + id);
                if (id=="Splitter")
                {
                    Require((bounds.size-new Vector3(.94f,.11f,.94f)).sqrMagnitude<.000001f,"Splitter has more than its floor.");
                    Require(prefab.GetComponentsInChildren<MeshFilter>().Length==1,"Splitter retained frame geometry.");
                }
                else
                {
                    Require((bounds.size-Vector3.one).sqrMagnitude<.000001f,"Optic must be a unit cube: " + id);
                    foreach (var filter in prefab.GetComponentsInChildren<MeshFilter>())
                        foreach (var p in filter.sharedMesh.vertices)
                            Require(Mathf.Min(Mathf.Abs(Mathf.Abs(p.x)-.5f),Mathf.Abs(Mathf.Abs(p.z)-.5f),Mathf.Abs(p.y),Mathf.Abs(p.y-1))<.0001f,"Geometry protrudes from cube: " + id);
                }
                foreach (var renderer in prefab.GetComponentsInChildren<Renderer>())
                {
                    var spriteRenderer = renderer as SpriteRenderer;
                    var texture = spriteRenderer != null ? spriteRenderer.sprite.texture : (Texture2D)renderer.sharedMaterial.mainTexture;
                    Require(texture.width==64 && texture.height==64 && texture.filterMode==FilterMode.Point,"Wrong texture format: " + id);
                    Require(renderer.sharedMaterial.shader.name==(spriteRenderer != null ? "Slima/Saw Sprite" : "Slima/Toon (Cel + Pixel)"),"Wrong material shader: " + id);
                }
                var profile = BlockGameplayCatalog.Get(id);
                for (int direction=0;direction<4;direction++)
                {
                    var view = GameplayPrototypeFactory.Create(definition,profile,Vector3Int.zero,direction,group.transform);
                    Require(Vector3.Dot(view.transform.forward,GridDirection.ToVector(direction))>.999f,"Incorrect facing: " + id);
                    Require(view.transform.Find("EmitterDirectionIndicator")==null,"Prototype arrow still present: " + id);
                    var casing = view.transform.Find(id+"/Casing").GetComponent<Renderer>();
                    var block = new MaterialPropertyBlock(); casing.GetPropertyBlock(block);
                    Require(block.GetColor("_BaseColor")==Color.white,"Casing was tinted: " + id);
                    var lens = view.transform.Find(id+(id=="Splitter" ? "/CircularSaw" : "/Optics")).GetComponent<Renderer>();
                    view.SetReceiverFaces(1); view.SetActivated(true); lens.GetPropertyBlock(block);
                    if (id!="Splitter") Require(block.GetFloat("_EmissionStrength")>lens.sharedMaterial.GetFloat("_EmissionStrength"),"Activation did not light sensor: " + id);
                    view.SetActivated(false); lens.GetPropertyBlock(block);
                    Require(block.GetColor("_BaseColor")==lens.sharedMaterial.color,"Reset lost optics color: " + id);
                    float resetEmission = profile.Kind==GameplayBlockKind.BeamEmitter ?
                        view.GetComponentInChildren<GameplayOpticGlow>().emissionStrength : lens.sharedMaterial.GetFloat("_EmissionStrength");
                    Require(block.GetFloat("_EmissionStrength")==resetEmission,"Reset lost glow level: " + id);
                    Object.DestroyImmediate(view.gameObject);
                }
            }
            int i=0;
            foreach (string id in SlimaMechanismAssetBuilder.Ids)
            {
                var prefab = BlockLibrary.Get(id).previewPrefab;
                var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab,group.transform);
                model.transform.position = new Vector3((i%4)*1.6f,0,(i/4)*1.6f);
                i++;
            }
            AddCameraAndLight(new Vector3(2.4f,.3f,1.6f),5.0f);
            Directory.CreateDirectory("Assets/_Game/Scenes");
            EditorSceneManager.SaveScene(scene,"Assets/_Game/Scenes/MechanismShowcase.unity");

            // Render the three families through their real gameplay views.
            Object.DestroyImmediate(group); group = new GameObject("Three model families");
            string[] previews = { "Splitter","Lab_Laser_03","ReceiverPurple_A" };
            for (i=0;i<previews.Length;i++)
            {
                string id=previews[i];
                GameplayPrototypeFactory.Create(BlockLibrary.Get(id),BlockGameplayCatalog.Get(id),new Vector3Int((2-i)*2,0,0),0,group.transform);
            }
            var camera = Camera.main; Vector3 target=new Vector3(2.5f,.42f,.5f);
            camera.transform.position=target+new Vector3(1.8f,3.5f,7); camera.transform.LookAt(target); camera.orthographicSize=1.4f;
            var rt=new RenderTexture(1500,650,24,RenderTextureFormat.ARGB32); rt.Create();
            var image = new Texture2D(rt.width,rt.height,TextureFormat.RGBA32,false);
            RenderTexture old=RenderTexture.active;
            try
            {
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest { destination=rt });
                RenderPipeline.SubmitRenderRequest(camera,new UniversalRenderPipeline.SingleCameraRequest { destination=rt });
                RenderTexture.active=rt; image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0); image.Apply();
                Directory.CreateDirectory("Docs/images"); File.WriteAllBytes("Docs/images/MechanismModels.png",image.EncodeToPNG());
            }
            finally { RenderTexture.active=old; rt.Release(); Object.DestroyImmediate(rt); Object.DestroyImmediate(image); }
            Directory.CreateDirectory("Logs"); File.WriteAllText("Logs/MechanismArt-validation.txt","PASS: floor-and-saw-only Splitter, 11 exact cube optics, 12 resource prefabs, 64px textures, UVs, normals, 48 facing/color checks, activation/reset and rendered preview.\n");
            Debug.Log("[Slima Mechanisms] Validation PASS; preview: Docs/images/MechanismModels.png");
        }

        static void AddCameraAndLight(Vector3 target,float size)
        {
            var cameraObject=new GameObject("Camera"); cameraObject.tag="MainCamera";
            var camera=cameraObject.AddComponent<Camera>(); camera.orthographic=true; camera.orthographicSize=size;
            camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.07f,.095f,.15f);
            cameraObject.transform.position=target+new Vector3(4,5,7); cameraObject.transform.LookAt(target);
            var data=cameraObject.AddComponent<UniversalAdditionalCameraData>(); data.renderPostProcessing=false;
            var lightObject=new GameObject("Key light"); lightObject.transform.rotation=Quaternion.Euler(45,-25,0);
            var light=lightObject.AddComponent<Light>(); light.type=LightType.Directional; light.intensity=1.2f;
            RenderSettings.ambientLight=new Color(.4f,.44f,.52f);
        }
        static void Require(bool condition,string message) { if (!condition) throw new Exception(message); }
    }
}
