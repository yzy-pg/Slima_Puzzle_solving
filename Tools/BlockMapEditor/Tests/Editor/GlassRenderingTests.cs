using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BlockMap.Tests
{
    public sealed class GlassRenderingTests
    {
        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BlockLibrary.Reload();
        }

        [Test]
        public void GlassKeepsFloorAndWallFacesIncludingAcrossChunkBoundaries()
        {
            foreach (string opaque in new[] { "Lab_Floor_01", "Lab_Wall_01" })
            foreach (string glass in new[] { "Glass_Wall", "Glass_Floor" })
            foreach (Vector3Int direction in new[] { Vector3Int.up, Vector3Int.right, Vector3Int.forward })
            {
                var root = new GameObject("Glass adjacency");
                try
                {
                    var world = root.AddComponent<BlockWorld>();
                    var renderer = root.AddComponent<BlockChunkRenderer>();
                    renderer.generateColliders = true;
                    Vector3Int cell = new Vector3Int(15, 0, 15);
                    world.Set(cell, BlockLibrary.IndexOf(opaque));
                    world.Set(cell + direction, BlockLibrary.IndexOf(glass));
                    renderer.RebuildAll();
                    int opaqueIndices = 0;
                    foreach (var item in root.GetComponentsInChildren<MeshRenderer>())
                    {
                        Mesh mesh = item.GetComponent<MeshFilter>().sharedMesh;
                        for (int sub = 0; sub < item.sharedMaterials.Length; sub++)
                            if (item.sharedMaterials[sub] == renderer.GetMaterial(BlockLibrary.Get(opaque)))
                                opaqueIndices += mesh.GetTriangles(sub).Length;
                    }
                    Assert.AreEqual(36, opaqueIndices, opaque + " must retain all six textured faces beside " + glass);
                    Assert.IsTrue(world.IsSolid(cell + direction), "Rendering transparency must not change solidity.");
                    Assert.IsNotEmpty(root.GetComponentsInChildren<MeshCollider>());
                }
                finally { Object.DestroyImmediate(root); }
            }
        }

        [TestCase("Glass_Wall")]
        [TestCase("Glass_Floor")]
        [TestCase("Lab_Wall_01")]
        public void IdenticalAdjacentBlocksStillCullTheirInternalFaces(string id)
        {
            var root = new GameObject("Internal faces");
            try
            {
                var world = root.AddComponent<BlockWorld>();
                var renderer = root.AddComponent<BlockChunkRenderer>();
                world.Set(Vector3Int.zero, BlockLibrary.IndexOf(id));
                world.Set(Vector3Int.right, BlockLibrary.IndexOf(id));
                renderer.RebuildAll();
                Assert.AreEqual(60, root.GetComponentInChildren<MeshFilter>().sharedMesh.triangles.Length);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void StandaloneGlassReceivesSceneAtmosphereAndKeepsFullCellCollision()
        {
            var root = new GameObject("Glass atmosphere");
            try
            {
                var world = root.AddComponent<BlockWorld>();
                var renderer = root.AddComponent<BlockChunkRenderer>();
                renderer.generateColliders = true;
                world.Set(Vector3Int.zero, BlockLibrary.IndexOf("Glass_Wall"));
                renderer.RebuildAll();
                var item = root.GetComponentInChildren<MeshRenderer>();
                var game = root.AddComponent<BlockMapGameRoot>();
                game.atmosphereAmbient = new Color(.08f, .12f, .2f);
                game.ApplyAtmosphereTo(item);
                var properties = new MaterialPropertyBlock();
                item.GetPropertyBlock(properties);
                Assert.Less(Vector4.Distance(game.atmosphereAmbient, properties.GetColor("_AmbientColor")), .00001f);
                Assert.AreEqual(0, properties.GetFloat("_FallbackLight"));
                var collider = root.GetComponentInChildren<MeshCollider>();
                Assert.AreEqual(Vector3.one, collider.sharedMesh.bounds.size);
                Assert.IsTrue(collider.Raycast(new Ray(new Vector3(.5f, 2, .5f), Vector3.down), out _, 3));
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
