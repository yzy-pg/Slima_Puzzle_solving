using System.IO;
using System.Collections.Generic;
using BlockMap;
using BlockMap.Gameplay;
using UnityEditor;
using UnityEngine;

namespace SlimaPuzzle.EditorTools
{
    public static class SlimaElementFloorAssetBuilder
    {
        const string ModelDir = "Assets/_Game/Art/Models/ElementFloors";
        const string MaterialDir = "Assets/_Game/Art/Materials/ElementFloors";
        const string PrefabDir = "Assets/BlockMapEditor/Resources/BlockModels/ElementFloors";

        [MenuItem("Tools/Slima/贴图/配置元素地板发光")]
        public static void Build()
        {
            Directory.CreateDirectory(ModelDir);
            Directory.CreateDirectory(MaterialDir);
            Directory.CreateDirectory(PrefabDir);
            AssetDatabase.Refresh();
            const string sidePath = "Assets/_Game/Art/Textures/ElementFloors/ElementFloor_Side.png";
            ImportPixels(sidePath);
            var casing = SaveMaterial(MaterialDir + "/Casing.mat", Shader.Find("Slima/Toon (Cel + Pixel)"));
            casing.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(sidePath);
            casing.SetFloat("_EmissionStrength", 0);
            casing.SetFloat("_OutlineWidth", 0);
            EditorUtility.SetDirty(casing);
            var pulse = SaveMaterial("Assets/BlockMapEditor/Resources/ElementFloorRise.mat", Shader.Find("BlockMap/ElementFloorRise"));
            pulse.SetFloat("_Intensity", 6);
            EditorUtility.SetDirty(pulse);
            var topMesh = SaveMesh("ElementFloor_Top", true);
            var sideMesh = SaveMesh("ElementFloor_Casing", false);
            foreach (string id in new[] { "Lab_Heater_01", "Lab_Heater_02" })
            {
                bool fire = id == "Lab_Heater_01";
                string folder = "Packages/com.gamejam.slima-blockmap-editor/Resources/" + id + "/";
                string texturePath = folder + id + "_Stripe.png";
                ImportPixels(texturePath);
                var source = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
                var pixels = source.GetPixels();
                // Bake shader data from saturated panel pixels; cool gray metal stays non-emissive.
                for (int i = 0; i < pixels.Length; i++)
                {
                    Color c = pixels[i];
                    bool lit = fire ? c.r > .65f && c.g < .8f && c.b < .35f
                        : c.b > .6f && c.g > .55f && c.b > c.r * 1.08f;
                    pixels[i] = lit ? new Color(c.r, c.g, c.b, 1) : Color.black;
                }
                var mask = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
                mask.SetPixels(pixels); mask.Apply();
                string maskPath = folder + id + "_Emission.png";
                File.WriteAllBytes("Tools/BlockMapEditor/Resources/" + id + "/" + id + "_Emission.png", mask.EncodeToPNG());
                Object.DestroyImmediate(mask);
                AssetDatabase.ImportAsset(maskPath);
                ImportPixels(maskPath);
                var def = AssetDatabase.LoadAssetAtPath<BlockDefinition>(folder + id + ".asset");
                def.simpleTexture = source;
                def.emissionMap = AssetDatabase.LoadAssetAtPath<Texture2D>(maskPath);
                def.emissionColor = Color.white;
                def.emissionStrength = 1.35f;
                def.emitLight = true;
                def.lightColor = fire ? new Color(1, .32f, .055f) : new Color(.16f, .7f, 1);
                def.lightIntensity = 1.6f;
                def.lightRange = 4;
                def.useLocalLightPosition = true;
                // The actual pad top is y+.12, rather than the full voxel top y+1.
                def.lightLocalPosition = new Vector3(0, -.34f, 0);
                def.lampHalo = false;
                var topMaterial = SaveMaterial(MaterialDir + "/" + id + "_Top.mat", Shader.Find("Slima/Toon (Cel + Pixel)"));
                topMaterial.mainTexture = source;
                topMaterial.SetTexture("_EmissionMap", def.emissionMap);
                topMaterial.SetColor("_EmissionColor", def.emissionColor);
                topMaterial.SetFloat("_EmissionStrength", def.emissionStrength);
                topMaterial.SetFloat("_OutlineWidth", 0);
                EditorUtility.SetDirty(topMaterial);
                var root = new GameObject(id);
                try
                {
                    root.AddComponent<GameplayMechanismArt>();
                    AddPart(root.transform, "Top", topMesh, topMaterial);
                    AddPart(root.transform, "Casing", sideMesh, casing);
                    var effect = root.AddComponent<ElementFloorRiseEffect>();
                    effect.color = def.lightColor;
                    effect.surfaceHeight = .12f;
                    def.previewPrefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabDir + "/" + id + ".prefab");
                }
                finally { Object.DestroyImmediate(root); }
                EditorUtility.SetDirty(def);
            }
            AssetDatabase.SaveAssets();
            BlockLibrary.Reload();
            Debug.Log("[ElementFloor] Built separate top/side UVs, surface lights and fading rising rectangles.");
        }

        static Material SaveMaterial(string path, Shader shader)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) { material = new Material(shader); AssetDatabase.CreateAsset(material, path); }
            material.color = Color.white;
            return material;
        }

        static void AddPart(Transform parent, string name, Mesh mesh, Material material)
        {
            var part = new GameObject(name);
            part.transform.SetParent(parent, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            part.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        static Mesh SaveMesh(string name, bool top)
        {
            const float h = .46f, y = .12f;
            var vertices = new List<Vector3>();
            var uv = new List<Vector2>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();
            void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
            {
                int start = vertices.Count;
                vertices.AddRange(new[] { a, b, c, d });
                uv.AddRange(new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right });
                normals.AddRange(new[] { normal, normal, normal, normal });
                bool flip = Vector3.Dot(Vector3.Cross(b-a, c-a), normal) < 0;
                triangles.AddRange(flip ? new[] { start, start+2, start+1, start, start+3, start+2 }
                    : new[] { start, start+1, start+2, start, start+2, start+3 });
            }
            if (top)
                Face(new Vector3(-h,y,-h), new Vector3(-h,y,h), new Vector3(h,y,h), new Vector3(h,y,-h), Vector3.up);
            else
            {
                Face(new Vector3(-h,0,-h), new Vector3(-h,y,-h), new Vector3(h,y,-h), new Vector3(h,0,-h), Vector3.back);
                Face(new Vector3(h,0,h), new Vector3(h,y,h), new Vector3(-h,y,h), new Vector3(-h,0,h), Vector3.forward);
                Face(new Vector3(-h,0,h), new Vector3(-h,y,h), new Vector3(-h,y,-h), new Vector3(-h,0,-h), Vector3.left);
                Face(new Vector3(h,0,-h), new Vector3(h,y,-h), new Vector3(h,y,h), new Vector3(h,0,h), Vector3.right);
                Face(new Vector3(-h,0,h), new Vector3(-h,0,-h), new Vector3(h,0,-h), new Vector3(h,0,h), Vector3.down);
            }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0); mesh.RecalculateBounds();
            string path = ModelDir + "/" + name + ".asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null) AssetDatabase.CreateAsset(mesh, path);
            else { EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); mesh = existing; }
            return mesh;
        }

        static void ImportPixels(string path)
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.isReadable = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }
    }
}
