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
    public static class SlimaControlAssetBuilder
    {
        public const string Art = "Assets/_Game/Art/";
        public const string Prefabs = "Assets/BlockMapEditor/Resources/BlockModels/Controls";
        const string Models = Art + "Models/Controls";
        const string Materials = Art + "Materials/Controls";
        const string Textures = Art + "Textures/Controls";
        public static readonly string[] Ids = { "Lab_PressurePlate", "PressurePlateLarge_A", "PressurePlateSmall_B", "Lab_Lever", "Lever_B", "Button_A", "Button_B" };

        [MenuItem("Tools/Slima/机关/生成压力板按钮拉杆模型")]
        public static void BuildAssets()
        {
            foreach (string path in new[] { Models, Materials, Textures, Prefabs }) Directory.CreateDirectory(path);
            AssetDatabase.Refresh();
            var steel = Material("Steel", Texture("Steel", false, false));
            var plastic = Material("Plastic", Texture("Plastic", true, false));
            var hazard = Material("Hazard", Texture("Hazard", false, true));
            var dark = Material("Dark", Texture("Dark", true, false));
            dark.color = new Color(.35f, .35f, .35f);
            foreach (string id in Ids)
            {
                var root = new GameObject(id);
                try
                {
                    root.AddComponent<GameplayMechanismArt>();
                    var art = root.AddComponent<GameplayControlArt>();
                    bool lever = id.Contains("Lever");
                    bool button = id.StartsWith("Button", StringComparison.Ordinal);
                    bool large = id.Contains("Large");
                    var tintParts = new List<Renderer>();
                    if (lever)
                    {
                        Part(root.transform, "SquareBase", new Vector3(0,.055f,0), new Vector3(.52f,.11f,.52f), steel);
                        Part(root.transform, "Socket", new Vector3(0,.13f,0), new Vector3(.22f,.10f,.22f), dark);
                        var pivot = new GameObject("LeverPivot"); pivot.transform.SetParent(root.transform,false);
                        pivot.transform.localPosition = new Vector3(0,.17f,0);
                        Part(pivot.transform, "MetalRod", new Vector3(0,.22f,0), new Vector3(.07f,.44f,.07f), steel);
                        tintParts.Add(Part(pivot.transform, "GrayHandle", new Vector3(0,.48f,0), new Vector3(.10f,.24f,.10f), plastic));
                        art.isLever = true; art.movingPart = pivot.transform; art.duration = .22f;
                        Bolts(root.transform,.52f,.12f,steel);
                    }
                    else
                    {
                        float width = button ? .48f : (large ? 62f : 59f) / 64f;
                        float baseHeight = large ? .075f : .04f;
                        Part(root.transform,"SquareBase",new Vector3(0,baseHeight/2,0),new Vector3(width,baseHeight,width),dark);
                        if (large)
                        {
                            float edge = 4f / 64f;
                            foreach (float side in new[] { -1f, 1f })
                            {
                                Part(root.transform,"HazardEdgeX" + side,new Vector3(side*(width-edge)/2,.088f,0),new Vector3(edge,.026f,width),hazard);
                                Part(root.transform,"HazardEdgeZ" + side,new Vector3(0,.088f,side*(width-edge)/2),new Vector3(width-2*edge,.026f,edge),hazard);
                            }
                        }
                        var pivot = new GameObject("PressPivot"); pivot.transform.SetParent(root.transform,false);
                        pivot.transform.localPosition = new Vector3(0,baseHeight,0);
                        float capWidth = width - (large ? 10f : 4f) / 64f;
                        float capHeight = button ? .19f : large ? .10f : .055f;
                        tintParts.Add(Part(pivot.transform,"GrayCap",new Vector3(0,capHeight/2,0),new Vector3(capWidth,capHeight,capWidth),large ? steel : plastic));
                        art.movingPart = pivot.transform; art.pressedHeight = button ? .25f : .35f;
                        if (large) Bolts(root.transform,width,.11f,steel);
                    }
                    art.colorParts = tintParts.ToArray(); art.AdvanceAnimation(0f);
                    PrefabUtility.SaveAsPrefabAsset(root, Prefabs + "/" + id + ".prefab");
                }
                finally { Object.DestroyImmediate(root); }
            }
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh(); BlockLibrary.Reload();
        }

        static void Bolts(Transform parent, float width, float y, Material material)
        {
            foreach (float x in new[] { -1f, 1f }) foreach (float z in new[] { -1f, 1f })
                Part(parent,"Bolt" + x + z,new Vector3(x*(width/2-.045f),y,z*(width/2-.045f)),new Vector3(.035f,.012f,.035f),material);
        }

        static Renderer Part(Transform parent, string name, Vector3 center, Vector3 size, Material material)
        {
            var part = new GameObject(name); part.transform.SetParent(parent,false);
            // Mesh 原点保持在移动部件底部，避免按压时穿入地板。
            string meshName = name + "_" + size.x.ToString("F4",System.Globalization.CultureInfo.InvariantCulture) + "_" + size.y.ToString("F4",System.Globalization.CultureInfo.InvariantCulture) + "_" + size.z.ToString("F4",System.Globalization.CultureInfo.InvariantCulture);
            var mesh = Box(meshName, size);
            string path = Models + "/" + meshName + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) AssetDatabase.CreateAsset(mesh,path);
            else { Object.DestroyImmediate(mesh); mesh = existing; }
            part.transform.localPosition = center;
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = part.AddComponent<MeshRenderer>(); renderer.sharedMaterial = material;
            return renderer;
        }

        static Mesh Box(string name, Vector3 size)
        {
            var vertices = new List<Vector3>(); var normals = new List<Vector3>();
            var uv = new List<Vector2>(); var triangles = new List<int>(); var colors = new List<Color>();
            Vector3 h = size / 2;
            Vector3[] ns = { Vector3.up, Vector3.down, Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
            foreach (var n in ns)
            {
                Vector3 u = Mathf.Abs(n.y) > .5f ? Vector3.right : Vector3.Cross(Vector3.up,n);
                Vector3 v = Vector3.Cross(n,u);
                int start = vertices.Count;
                Vector2[] corners = { new Vector2(-1,-1), new Vector2(1,-1), new Vector2(1,1), new Vector2(-1,1) };
                foreach (var corner in corners)
                {
                    vertices.Add(Vector3.Scale(n + u * corner.x + v * corner.y, h)); normals.Add(n); colors.Add(Color.white);
                    // 顶面完整像素图案，侧面采样中性灰色块。
                    uv.Add(n.y > .5f ? (corner + Vector2.one) / 2 : new Vector2(.48f,.48f) + corner * .025f);
                }
                triangles.AddRange(new[] { start,start+1,start+2,start,start+2,start+3 });
            }
            var mesh = new Mesh { name = name }; mesh.SetVertices(vertices); mesh.SetNormals(normals);
            mesh.SetUVs(0,uv); mesh.SetColors(colors); mesh.SetTriangles(triangles,0); mesh.RecalculateBounds();
            return mesh;
        }

        static Material Material(string name, Texture2D texture)
        {
            string path = Materials + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(Shader.Find("Slima/Toon (Cel + Pixel)")); AssetDatabase.CreateAsset(material,path); }
            material.SetTexture("_BaseMap",texture); material.SetColor("_BaseColor",Color.white);
            material.SetFloat("_OutlineWidth",.6f); material.SetFloat("_PixelSnap",0);
            EditorUtility.SetDirty(material); return material;
        }

        static Texture2D Texture(string name, bool plastic, bool hazard)
        {
            string path = Textures + "/" + name + "_64.png";
            var texture = new Texture2D(64,64,TextureFormat.RGBA32,false);
            var pixels = new Color[4096];
            for (int y=0;y<64;y++) for (int x=0;x<64;x++)
            {
                int edge = Mathf.Min(Mathf.Min(x,63-x),Mathf.Min(y,63-y));
                float gray = plastic ? .66f : .59f;
                if (!plastic) gray += ((x/4+y/2)%5==0 ? .06f : 0);
                else gray += ((x/3+y/3)%11==0 ? -.025f : 0);
                if (edge < 2) gray = .20f;
                else if (edge < 4) gray = x+y > 63 ? .48f : .85f;
                else if (edge < 6) gray = .37f;
                pixels[y*64+x] = hazard && edge>=2 ? ((x+y)/6%2==0 ? new Color(.92f,.71f,.13f) : new Color(.10f,.10f,.10f)) : new Color(gray,gray,gray);
            }
            texture.SetPixels(pixels); texture.Apply(); File.WriteAllBytes(path,texture.EncodeToPNG()); Object.DestroyImmediate(texture);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.filterMode=FilterMode.Point; importer.mipmapEnabled=false; importer.wrapMode=TextureWrapMode.Clamp;
            importer.textureCompression=TextureImporterCompression.Uncompressed; importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
