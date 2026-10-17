using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using BlockMap;
using BlockMap.Gameplay;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SlimaPuzzle.EditorTools
{
    public static class SlimaMechanismAssetBuilder
    {
        const string Art = "Assets/_Game/Art/";
        const string Models = Art + "Models/Mechanisms";
        const string Materials = Art + "Materials/Mechanisms";
        const string Prefabs = "Assets/BlockMapEditor/Resources/BlockModels/Mechanisms";
        static readonly Rect Steel = new Rect(.025f, .915f, .075f, .06f);
        static readonly Rect Full = new Rect(.008f, .008f, .984f, .984f);
        public static readonly string[] Ids = {
            "Splitter", "Lab_Laser_01", "Lab_Laser_02", "Lab_Laser_03",
            "EmitterWhiteWall", "EmitterRedWall", "EmitterBlueWall",
            "Lab_Laser_04", "Lab_Laser_05", "ReceiverRed_A", "ReceiverBlue_A", "ReceiverPurple_A"
        };

        [MenuItem("Tools/Slima/机关/生成专属模型与64像素材质")]
        public static void BuildAssets() => BuildAssets(null);

        [MenuItem("Tools/Slima/机关/更新切分机圆锯")]
        public static void BuildSplitterAssets() => BuildAssets("Splitter");

        static void BuildAssets(string familyFilter)
        {
            Directory.CreateDirectory(Models); Directory.CreateDirectory(Materials); Directory.CreateDirectory(Prefabs);
            AssetDatabase.Refresh();
            foreach (string family in new[] { "Splitter", "LightEmitter", "LightReceiver" })
            {
                if (familyFilter != null && family != familyFilter) continue;
                var importer = (TextureImporter)AssetImporter.GetAtPath(Art + "Textures/Mechanisms/" + family + "_64.png");
                importer.filterMode = FilterMode.Point; importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp; importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(importer.assetPath);
                if (texture.width != 64 || texture.height != 64) throw new Exception("Expected 64px texture: " + family);
                var casing = new Geometry(); var optics = new Geometry(); var shell = new Geometry(); var indicator = new Geometry();
                if (family == "Splitter") BuildSplitter(casing);
                else casing.OpticCube(optics,shell,indicator,family=="LightReceiver");
                Mesh casingMesh = SaveMesh(family + "_Casing", casing);
                Mesh opticsMesh = family == "Splitter" ? null : SaveMesh(family + "_Optics", optics);
                Mesh shellMesh = family == "Splitter" ? null : SaveMesh(family + "_Shell", shell);
                Mesh indicatorMesh = family == "Splitter" ? null : SaveMesh(family + "_TopIndicator", indicator);
                ExportObj(family, casingMesh, opticsMesh, shellMesh, indicatorMesh);
                Material metal = SaveMaterial(family + "_Steel", texture, Color.white, false);
                Material shellMaterial = null;
                if (shellMesh != null)
                {
                    string shellPath=Art+"Textures/Mechanisms/MechanismCasing_64.png";
                    var shellImporter=(TextureImporter)AssetImporter.GetAtPath(shellPath);
                    shellImporter.filterMode=FilterMode.Point; shellImporter.mipmapEnabled=false;
                    shellImporter.wrapMode=TextureWrapMode.Clamp; shellImporter.textureCompression=TextureImporterCompression.Uncompressed;
                    shellImporter.SaveAndReimport();
                    shellMaterial=SaveMaterial("MechanismCasing",AssetDatabase.LoadAssetAtPath<Texture2D>(shellPath),Color.white,false);
                }
                foreach (string id in Ids)
                {
                    var profile = BlockGameplayCatalog.Get(id);
                    bool belongs = family == "Splitter" ? profile.Kind == GameplayBlockKind.Splitter :
                        family == "LightEmitter" ? profile.Kind == GameplayBlockKind.BeamEmitter : profile.Kind == GameplayBlockKind.BeamReceiver;
                    if (!belongs) continue;
                    Color tint = profile.Kind == GameplayBlockKind.Splitter ? Color.white : BlockOpticsSystem.ToColor(profile.BeamColor);
                    Material lens = id == "Splitter" ? null : SaveMaterial(id + "_Optics", texture, tint, true);
                    if (lens != null) lens.SetFloat("_EmissionStrength",0f);
                    Material top = id == "Splitter" ? null : SaveMaterial(id + "_TopIndicator", texture, tint, true);
                    if (top != null) top.SetFloat("_EmissionStrength",0f);
                    var root = new GameObject(id);
                    try
                    {
                        root.AddComponent<GameplayMechanismArt>();
                        AddPart(root, "Casing", casingMesh, metal);
                        if (shellMesh != null) AddPart(root, "Shell", shellMesh, shellMaterial);
                        if (opticsMesh != null) AddPart(root, "Optics", opticsMesh, lens);
                        if (indicatorMesh != null) AddPart(root, "TopIndicator", indicatorMesh, top);
                        if (id == "Splitter") AddSaw(root);
                        else
                        {
                            var glow = root.AddComponent<GameplayOpticGlow>();
                            glow.color = tint;
                            glow.receiver = profile.Kind == GameplayBlockKind.BeamReceiver;
                            if (!glow.receiver)
                            {
                                glow.lightRange = 5f;
                                glow.lightAngle = 120f;
                            }
                            else
                            {
                                glow.lightRange = 1.8f;
                                glow.lightAngle = 120f;
                                glow.lightIntensity = 1.6f;
                            }
                            glow.opticalSurface = root.transform.Find("Optics").GetComponent<Renderer>();
                            glow.topIndicator = root.transform.Find("TopIndicator").GetComponent<Renderer>();
                            glow.lightLocalPosition = new Vector3(0,.5f,.55f);
                        }
                        PrefabUtility.SaveAsPrefabAsset(root, Prefabs + "/" + id + ".prefab");
                    }
                    finally { Object.DestroyImmediate(root); }
                }
            }
            AssetDatabase.SaveAssets(); AssetDatabase.Refresh(); BlockLibrary.Reload();
            Debug.Log("[Slima Mechanisms] " + (familyFilter ?? "All families") + " assets ready.");
        }

        static void BuildSplitter(Geometry body)
        {
            body.Box(new Vector3(0,.055f,0), new Vector3(.94f,.11f,.94f), true);
        }

        static void AddSaw(GameObject root)
        {
            var frames = new Sprite[24];
            for (int i=0;i<frames.Length;i++)
            {
                string path = Art + "Textures/Mechanisms/SplitterSaw/SplitterSaw_" + i.ToString("00") + ".png";
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 64;
                importer.spritePivot = new Vector2(.5f,.5f);
                importer.filterMode = FilterMode.Point; importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.alphaIsTransparency = true;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.SaveAndReimport();
                frames[i] = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (frames[i] == null || frames[i].rect.size != new Vector2(64,64)) throw new Exception("Invalid saw frame: " + path);
            }
            string materialPath = Materials + "/SplitterSaw.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null) { material = new Material(Shader.Find("Slima/Saw Sprite")); AssetDatabase.CreateAsset(material,materialPath); }
            material.SetColor("_Color",Color.white); material.SetColor("_BaseColor",Color.white);
            material.SetFloat("_VisibleMinY",0f);
            EditorUtility.SetDirty(material);
            var saw = new GameObject("CircularSaw"); saw.transform.SetParent(root.transform,false);
            saw.transform.localPosition = new Vector3(0,.11f,0);
            saw.transform.localRotation = Quaternion.Euler(0,90,0);
            saw.transform.localScale = Vector3.one * 1.1f;
            var renderer = saw.AddComponent<SpriteRenderer>();
            renderer.sprite = frames[0]; renderer.sharedMaterial = material;
            var animation = saw.AddComponent<SplitterSawAnimation>();
            animation.frames = frames; animation.framesPerSecond = 16f;
        }

        static void AddPart(GameObject root, string name, Mesh mesh, Material material)
        {
            var part = new GameObject(name); part.transform.SetParent(root.transform,false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            part.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        static Mesh SaveMesh(string name, Geometry geometry)
        {
            var mesh = geometry.Make(name); string path = Models + "/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) AssetDatabase.CreateAsset(mesh,path);
            else
            {
                existing.Clear();
                existing.vertices = mesh.vertices; existing.uv = mesh.uv;
                existing.colors = mesh.colors; existing.normals = mesh.normals;
                existing.triangles = mesh.triangles; existing.bounds = mesh.bounds;
                EditorUtility.SetDirty(existing);
                Object.DestroyImmediate(mesh); mesh = existing;
            }
            return mesh;
        }

        static Material SaveMaterial(string name, Texture texture, Color color, bool emission)
        {
            string path = Materials + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(Shader.Find("Slima/Toon (Cel + Pixel)")); AssetDatabase.CreateAsset(material,path); }
            material.SetTexture("_BaseMap",texture); material.SetColor("_BaseColor",color);
            material.SetFloat("_OutlineWidth",.8f); material.SetFloat("_PixelSnap",0);
            material.SetFloat("_EmissionStrength",emission ? .55f : 0);
            material.SetColor("_EmissionColor",color); material.SetTexture("_EmissionMap",texture);
            if (!emission && name!="Splitter_Steel")
            {
                material.SetFloat("_ExtraLightMode",0);
                material.SetFloat("_LightWrap",0);
            }
            EditorUtility.SetDirty(material); return material;
        }

        static void ExportObj(string family, params Mesh[] meshes)
        {
            var text = new StringBuilder("# Metres, Y up, grid floor origin, forward +Z; UVs use the 64px texture.\nmtllib " + family + ".mtl\n");
            int offset = 1;
            for (int m = 0; m < meshes.Length; m++)
            {
                Mesh mesh = meshes[m]; if (mesh == null) continue;
                text.Append("o ").Append(mesh.name).Append("\nusemtl ").Append(m == 0 ? "Casing" : m == 1 ? "Optics" : m == 2 ? "Shell" : "TopIndicator").Append('\n');
                foreach (var p in mesh.vertices) text.AppendFormat(CultureInfo.InvariantCulture,"v {0} {1} {2}\n",p.x,p.y,p.z);
                foreach (var uv in mesh.uv) text.AppendFormat(CultureInfo.InvariantCulture,"vt {0} {1}\n",uv.x,uv.y);
                foreach (var n in mesh.normals) text.AppendFormat(CultureInfo.InvariantCulture,"vn {0} {1} {2}\n",n.x,n.y,n.z);
                int[] faces = mesh.triangles;
                for (int i = 0; i < faces.Length; i += 3)
                {
                    text.Append('f'); for (int k = 0; k < 3; k++) { int v = faces[i+k]+offset; text.AppendFormat(" {0}/{0}/{0}",v); } text.Append('\n');
                }
                offset += mesh.vertexCount;
            }
            File.WriteAllText(Models + "/" + family + ".obj",text.ToString());
            string map = "../../Textures/Mechanisms/" + family + "_64.png";
            File.WriteAllText(Models + "/" + family + ".mtl","newmtl Casing\nKd 1 1 1\nmap_Kd " + map + "\n\nnewmtl Optics\nKd 1 1 1\nmap_Kd " + map + "\nnewmtl Shell\nKd 1 1 1\nmap_Kd ../../Textures/Mechanisms/MechanismCasing_64.png\n\nnewmtl TopIndicator\nKd 1 1 1\nmap_Kd " + map + "\n");
        }

        sealed class Geometry
        {
            readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<Vector2> uvs = new List<Vector2>();
            readonly List<int> triangles = new List<int>();
            void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Rect uv)
            {
                int i = vertices.Count; vertices.AddRange(new[] {a,b,c,d});
                uvs.AddRange(new[] {new Vector2(uv.xMin,uv.yMin),new Vector2(uv.xMax,uv.yMin),new Vector2(uv.xMax,uv.yMax),new Vector2(uv.xMin,uv.yMax)});
                triangles.AddRange(new[] {i,i+1,i+2,i,i+2,i+3});
            }
            public void Box(Vector3 p, Vector3 size, bool topTexture = false, Rect? rect = null) => BoxAt(p,size,Quaternion.identity,topTexture,rect);
            void BoxAt(Vector3 p, Vector3 size, Quaternion q, bool top, Rect? rect)
            {
                Vector3 s = size*.5f;
                Vector3[] v = {new Vector3(-s.x,-s.y,-s.z),new Vector3(s.x,-s.y,-s.z),new Vector3(s.x,s.y,-s.z),new Vector3(-s.x,s.y,-s.z),new Vector3(-s.x,-s.y,s.z),new Vector3(s.x,-s.y,s.z),new Vector3(s.x,s.y,s.z),new Vector3(-s.x,s.y,s.z)};
                for (int i=0;i<v.Length;i++) v[i]=q*(p+v[i]);
                Rect uv = rect ?? Steel;
                Face(v[4],v[5],v[6],v[7],uv); Face(v[1],v[0],v[3],v[2],uv);
                Face(v[0],v[4],v[7],v[3],uv); Face(v[5],v[1],v[2],v[6],uv);
                Face(v[7],v[6],v[2],v[3],top ? Full : uv); Face(v[0],v[1],v[5],v[4],uv);
            }
            public void OpticCube(Geometry optics, Geometry shell, Geometry indicator, bool receiver)
            {
                // Every surface is exactly on the boundary of a one-metre cube.
                // Split the painted face between steel and glass, with no overlapping polygons.
                for (int direction=0;direction<4;direction++)
                {
                    Quaternion turn = Quaternion.Euler(0,direction*90,0);
                    if (!receiver && direction!=0) { shell.SideRegion(-.5f,0,.5f,1,turn); continue; }
                    float half = receiver ? .14f : .15f;
                    SideRegion(-.5f,0,.5f,.5f-half,turn);
                    SideRegion(-.5f,.5f+half,.5f,1,turn);
                    SideRegion(-.5f,.5f-half,-half,.5f+half,turn);
                    SideRegion(half,.5f-half,.5f,.5f+half,turn);
                    optics.SideRegion(-half,.5f-half,half,.5f+half,turn);
                }
                shell.Face(new Vector3(-.5f,0,-.5f),new Vector3(.5f,0,-.5f),new Vector3(.5f,0,.5f),new Vector3(-.5f,0,.5f),Full);
                float topHalf = receiver ? .23f : .18f;
                shell.TopRegion(-.5f,-.5f,.5f,-topHalf);
                shell.TopRegion(-.5f,topHalf,.5f,.5f);
                shell.TopRegion(-.5f,-topHalf,-topHalf,topHalf);
                shell.TopRegion(topHalf,-topHalf,.5f,topHalf);
                float glassHalf = receiver ? .15f : topHalf;
                if (receiver)
                {
                    // A neutral metal bezel surrounds the larger colored glass; only the glass emits.
                    TopRegion(-topHalf,-topHalf,topHalf,-glassHalf,new Rect(.20f,.20f,.60f,.20f));
                    TopRegion(-topHalf,glassHalf,topHalf,topHalf,new Rect(.20f,.60f,.60f,.20f));
                    TopRegion(-topHalf,-glassHalf,-glassHalf,glassHalf,new Rect(.20f,.40f,.20f,.20f));
                    TopRegion(glassHalf,-glassHalf,topHalf,glassHalf,new Rect(.60f,.40f,.20f,.20f));
                }
                indicator.TopRegion(-glassHalf,-glassHalf,glassHalf,glassHalf,
                    receiver ? new Rect(.40f,.40f,.20f,.20f) : new Rect(.43f,.86f,.14f,.10f));
            }
            void SideRegion(float left,float bottom,float right,float top,Quaternion turn,Rect? rect=null)
            {
                Rect uv = rect ?? new Rect(Full.x+(left+.5f)*Full.width,Full.y+bottom*Full.height,(right-left)*Full.width,(top-bottom)*Full.height);
                Face(turn*new Vector3(left,bottom,.5f),turn*new Vector3(right,bottom,.5f),turn*new Vector3(right,top,.5f),turn*new Vector3(left,top,.5f),uv);
            }
            void TopRegion(float left,float back,float right,float front,Rect? rect=null)
            {
                Rect uv=rect ?? new Rect(Full.x+(left+.5f)*Full.width,Full.y+(back+.5f)*Full.height,(right-left)*Full.width,(front-back)*Full.height);
                Face(new Vector3(left,1,front),new Vector3(right,1,front),new Vector3(right,1,back),new Vector3(left,1,back),uv);
                // Texture-up corresponds to local +Z, so the painted arrow matches beam direction.
                for (int i=uvs.Count-4;i<uvs.Count;i++) uvs[i]=new Vector2(uvs[i].x,2*uv.center.y-uvs[i].y);
            }
            public Mesh Make(string name)
            {
                var mesh = new Mesh { name = name }; mesh.SetVertices(vertices); mesh.SetUVs(0,uvs); mesh.SetTriangles(triangles,0);
                var colors = new Color[vertices.Count]; for (int i=0;i<colors.Length;i++) colors[i]=Color.white;
                mesh.colors=colors; mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
            }
        }
    }
}
