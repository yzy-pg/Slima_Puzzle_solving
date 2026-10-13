using System;
using System.IO;
using BlockMap;
using BlockMap.Gameplay;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SlimaPuzzle.EditorTools
{
    public static class SlimaElementGlassValidation
    {
        [MenuItem("Tools/Slima/贴图/验证元素地块与玻璃")]
        public static void Validate()
        {
            BlockGameplayCatalog.Reload();
            BlockLibrary.Reload();
            foreach (string id in new[] { "Lab_Heater_01", "Lab_Heater_02" })
            {
                var def = BlockLibrary.Get(id);
                Require(def != null && def.simpleTexture != null, id + " texture missing");
                Require(def.simpleTexture.width == 64 && def.simpleTexture.height == 64, id + " size");
                Require(def.tint == Color.white && BlockGameplayCatalog.Get(id).Tint == Color.white, id + " tint");
                var kind = id == "Lab_Heater_01" ? GameplayBlockKind.RedFloor : GameplayBlockKind.BlueFloor;
                Require(BlockGameplayCatalog.Get(id).Kind == kind, id + " conversion kind");
            }
            Mesh wall = null;
            foreach (string id in new[] { "Glass_Wall", "Glass_Floor" })
            {
                var def = BlockLibrary.Get(id);
                Require(def != null && def.UseAutoTile && !def.interactive && def.previewPrefab == null, id + " definition");
                Require(def.renderMode == BlockRenderMode.Transparent, id + " transparent mode");
                Require(def.GetTexture().filterMode == FilterMode.Point, id + " pixel sampling");
                int visible = 0;
                foreach (var pixel in def.panelTexture.GetPixels32()) if (pixel.a > 0) visible++;
                Require(visible >= 20 && visible <= 80, id + " sparse highlights");
                var root = new GameObject("Glass validation");
                try
                {
                    var world = root.AddComponent<BlockWorld>();
                    var renderer = root.AddComponent<BlockChunkRenderer>();
                    renderer.generateColliders = true;
                    world.Set(Vector3Int.zero, BlockLibrary.IndexOf(id));
                    renderer.RebuildAll();
                    var collider = root.GetComponentInChildren<MeshCollider>();
                    Require(collider != null && collider.sharedMesh != null, id + " collider");
                    var mesh = collider.sharedMesh;
                    Require(mesh.bounds.size == Vector3.one && mesh.vertexCount == 24 && mesh.triangles.Length == 36,
                        id + " full-cell collision mesh");
                    Require(collider.Raycast(new Ray(new Vector3(.5f, 2, .5f), Vector3.down), out _, 3),
                        id + " transparent center must collide");
                    if (wall == null) wall = Object.Instantiate(mesh);
                    else
                    {
                        Require(mesh.bounds == wall.bounds, "Wall/Floor collision bounds differ");
                        var vertices = mesh.vertices;
                        var reference = wall.vertices;
                        for (int i = 0; i < vertices.Length; i++)
                            Require(vertices[i] == reference[i], "Wall/Floor collision vertices differ");
                        Object.DestroyImmediate(wall);
                    }
                }
                finally { Object.DestroyImmediate(root); }
            }
            Directory.CreateDirectory("Logs/ElementGlass");
            File.WriteAllText("Logs/ElementGlass/validation.txt",
                "PASS: conversion IDs/kinds/tints, 64x64 textures, sparse alpha, Point atlases, equal 1x1x1 collision meshes and center raycasts.");
            Debug.Log("[ElementGlass] PASS: textures and identical full-cell glass collisions.");
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
