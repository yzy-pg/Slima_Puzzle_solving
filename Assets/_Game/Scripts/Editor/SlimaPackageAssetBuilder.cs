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
    public static class SlimaPackageAssetBuilder
    {
        const string ModelDir = "Assets/_Game/Art/Models/Breakable";
        const string TexturePath = "Assets/_Game/Art/Textures/Breakable/cardboard.png";
        const string MaterialPath = "Assets/_Game/Art/Materials/Breakable/CardboardPackage.mat";
        public const string PrefabPath = "Assets/BlockMapEditor/Resources/BlockModels/Breakable.prefab";

        [MenuItem("Tools/Slima/机关/生成可破坏包裹模型")]
        public static void BuildAssets()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(MaterialPath));
            AssetDatabase.Refresh();
            var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();

            var source = JsonUtility.FromJson<PackageModel>(File.ReadAllText(ModelDir + "/cardboard_12x12.json"));
            Mesh mesh = BuildMesh(source.elements[0]);
            string meshPath = ModelDir + "/CardboardPackage.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (existing == null) AssetDatabase.CreateAsset(mesh, meshPath);
            else
            {
                EditorUtility.CopySerialized(mesh, existing);
                Object.DestroyImmediate(mesh);
                mesh = existing;
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Slima/Toon (Cel + Pixel)"));
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath));
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_OutlineWidth", .6f);
            material.SetFloat("_PixelSnap", 0);
            material.SetFloat("_EmissionStrength", 0);
            EditorUtility.SetDirty(material);

            var root = new GameObject("Breakable");
            try
            {
                // Preserve the source cardboard color instead of multiplying it by the prototype brown tint.
                root.AddComponent<GameplayMechanismArt>();
                root.AddComponent<MeshFilter>().sharedMesh = mesh;
                root.AddComponent<MeshRenderer>().sharedMaterial = material;
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { Object.DestroyImmediate(root); }
            AssetDatabase.SaveAssets();
            BlockLibrary.Reload();
            Debug.Log("[Slima Package] Create cardboard_12x12 is now the Breakable resource prefab.");
        }

        static Mesh BuildMesh(Element box)
        {
            float x0 = box.from[0], y0 = box.from[1], z0 = box.from[2];
            float x1 = box.to[0], y1 = box.to[1], z1 = box.to[2];
            var vertices = new List<Vector3>();
            var uv = new List<Vector2>();
            var triangles = new List<int>();
            // Minecraft face vertex order, including its mirrored south UV and rotated top/bottom.
            AddFace(vertices, uv, triangles, box.faces.north, new[] {
                new Vector3(x1,y1,z0), new Vector3(x1,y0,z0), new Vector3(x0,y0,z0), new Vector3(x0,y1,z0) });
            AddFace(vertices, uv, triangles, box.faces.east, new[] {
                new Vector3(x1,y1,z1), new Vector3(x1,y0,z1), new Vector3(x1,y0,z0), new Vector3(x1,y1,z0) });
            AddFace(vertices, uv, triangles, box.faces.south, new[] {
                new Vector3(x0,y1,z1), new Vector3(x0,y0,z1), new Vector3(x1,y0,z1), new Vector3(x1,y1,z1) });
            AddFace(vertices, uv, triangles, box.faces.west, new[] {
                new Vector3(x0,y1,z0), new Vector3(x0,y0,z0), new Vector3(x0,y0,z1), new Vector3(x0,y1,z1) });
            AddFace(vertices, uv, triangles, box.faces.up, new[] {
                new Vector3(x0,y1,z0), new Vector3(x0,y1,z1), new Vector3(x1,y1,z1), new Vector3(x1,y1,z0) });
            AddFace(vertices, uv, triangles, box.faces.down, new[] {
                new Vector3(x0,y0,z1), new Vector3(x0,y0,z0), new Vector3(x1,y0,z0), new Vector3(x1,y0,z1) });
            var mesh = new Mesh { name = "CardboardPackage" };
            mesh.SetVertices(vertices); mesh.SetUVs(0, uv); mesh.SetTriangles(triangles, 0);
            var colors = new Color[vertices.Count];
            for (int i = 0; i < colors.Length; i++) colors[i] = Color.white;
            mesh.colors = colors;
            mesh.RecalculateNormals(); mesh.RecalculateBounds();
            return mesh;
        }

        static void AddFace(List<Vector3> vertices, List<Vector2> uv, List<int> triangles, Face face, Vector3[] corners)
        {
            int start = vertices.Count;
            var sourceUV = new[] {
                new Vector2(face.uv[0], face.uv[1]), new Vector2(face.uv[0], face.uv[3]),
                new Vector2(face.uv[2], face.uv[3]), new Vector2(face.uv[2], face.uv[1]) };
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = corners[i];
                // 16 Minecraft pixels = one grid cell; floor origin; reflect Z into Unity coordinates.
                vertices.Add(new Vector3((p.x - 8) / 16, p.y / 16, (8 - p.z) / 16));
                Vector2 t = sourceUV[(i + face.rotation / 90) % 4];
                uv.Add(new Vector2(t.x / 16, 1 - t.y / 16));
            }
            triangles.AddRange(new[] { start, start + 2, start + 1, start, start + 3, start + 2 });
        }

        [Serializable] class PackageModel { public Element[] elements; }
        [Serializable] class Element { public float[] from; public float[] to; public Faces faces; }
        [Serializable] class Faces { public Face north, east, south, west, up, down; }
        [Serializable] class Face { public float[] uv; public int rotation; }
    }
}
