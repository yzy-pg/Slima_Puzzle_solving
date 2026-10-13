using System;
using System.Collections.Generic;
using System.IO;
using BlockMap;
using BlockMap.Gameplay;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SlimaPuzzle.EditorTools
{
    public static class SlimaSlidingDoorAssetBuilder
    {
        public const string TexturePath = "Assets/_Game/Art/Textures/Doors/SlidingDoorMetal_64.png";
        public const string ModelPath = "Assets/_Game/Art/Models/Doors/SlidingDoorPanel.asset";
        public const string MaterialPath = "Assets/_Game/Art/Materials/Doors/SlidingDoorMetal.mat";
        public const string PrefabFolder = "Assets/BlockMapEditor/Resources/BlockModels/Doors";

        [MenuItem("Tools/Slima/机关/生成灰色金属推拉门")]
        public static void BuildAssets()
        {
            foreach (string path in new[] {Path.GetDirectoryName(ModelPath),Path.GetDirectoryName(MaterialPath),PrefabFolder})
                Directory.CreateDirectory(path);
            AssetDatabase.Refresh();
            var importer=(TextureImporter)AssetImporter.GetAtPath(TexturePath);
            if(importer==null) throw new Exception("Missing sliding door texture: "+TexturePath);
            importer.filterMode=FilterMode.Point; importer.mipmapEnabled=false;
            importer.wrapMode=TextureWrapMode.Clamp; importer.textureCompression=TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            var texture=AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            if(texture.width!=64 || texture.height!=64) throw new Exception("Door texture must be 64x64.");
            var mesh=MakePanel();
            var existing=AssetDatabase.LoadAssetAtPath<Mesh>(ModelPath);
            if(existing==null) AssetDatabase.CreateAsset(mesh,ModelPath);
            else
            {
                existing.Clear(); existing.vertices=mesh.vertices; existing.normals=mesh.normals;
                existing.uv=mesh.uv; existing.colors=mesh.colors; existing.triangles=mesh.triangles; existing.bounds=mesh.bounds;
                EditorUtility.SetDirty(existing); Object.DestroyImmediate(mesh); mesh=existing;
            }
            var material=AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if(material==null)
            {
                material=new Material(Shader.Find("Slima/Toon (Cel + Pixel)"));
                AssetDatabase.CreateAsset(material,MaterialPath);
            }
            material.mainTexture=texture; material.color=Color.white;
            material.SetFloat("_EmissionStrength",0); material.SetFloat("_OutlineWidth",.6f);
            material.SetFloat("_ExtraLightMode",0); material.SetFloat("_LightWrap",0);
            material.SetColor("_ShadowColor",new Color(.38f,.38f,.38f));
            material.SetColor("_AmbientColor",new Color(.16f,.16f,.16f,1));
            EditorUtility.SetDirty(material);
            foreach(string id in new[] {"Door","Door_B"})
            {
                var root=new GameObject(id);
                try
                {
                    root.AddComponent<GameplayMechanismArt>();
                    var panel=new GameObject("SlidingPanel"); panel.transform.SetParent(root.transform,false);
                    panel.AddComponent<MeshFilter>().sharedMesh=mesh;
                    panel.AddComponent<MeshRenderer>().sharedMaterial=material;
                    var door=root.AddComponent<BlockMapDoor>(); door.slideDistance=1.05f;
                    door.toggleKey=KeyCode.None; door.clickToToggle=false;
                    door.duration=BlockGameplaySettings.Current.doorAnimationDuration;
                    door.SetHinge(panel.transform);
                    PrefabUtility.SaveAsPrefabAsset(root,PrefabFolder+"/"+id+".prefab");
                }
                finally { Object.DestroyImmediate(root); }
            }
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh(); BlockLibrary.Reload();
        }

        static Mesh MakePanel()
        {
            var vertices=new List<Vector3>(); var normals=new List<Vector3>();
            var uv=new List<Vector2>(); var colors=new List<Color>(); var triangles=new List<int>();
            var half=new Vector3(.48f,.5f,.07f);
            foreach(Vector3 normal in new[] {Vector3.forward,Vector3.back,Vector3.right,Vector3.left,Vector3.up,Vector3.down})
            {
                Vector3 horizontal=Mathf.Abs(normal.y)>.5f ? Vector3.right : Vector3.Cross(Vector3.up,normal);
                Vector3 vertical=Vector3.Cross(normal,horizontal); int start=vertices.Count;
                foreach(Vector2 corner in new[] {new Vector2(-1,-1),new Vector2(1,-1),new Vector2(1,1),new Vector2(-1,1)})
                {
                    vertices.Add(Vector3.Scale(normal+horizontal*corner.x+vertical*corner.y,half)+Vector3.up*.5f);
                    normals.Add(normal); colors.Add(Color.white);
                    uv.Add(Mathf.Abs(normal.z)>.5f ? new Vector2((1-corner.x)/2,(corner.y+1)/2) : new Vector2(.08f,.08f)+corner*.015f);
                }
                triangles.AddRange(new[] {start,start+1,start+2,start,start+2,start+3});
            }
            var mesh=new Mesh {name="SlidingDoorPanel"}; mesh.SetVertices(vertices); mesh.SetNormals(normals);
            mesh.SetUVs(0,uv); mesh.SetColors(colors); mesh.SetTriangles(triangles,0); mesh.RecalculateBounds();
            return mesh;
        }
    }
}
