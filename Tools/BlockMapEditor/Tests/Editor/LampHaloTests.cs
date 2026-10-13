using System.Collections;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace BlockMap.Tests
{
    public sealed class LampHaloTests
    {
        [UnityTest]
        public IEnumerator ElementFloorsUseSeparateSideUvsAndSurfaceRiseEffects()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            foreach (bool gameplay in new[] { false, true })
            {
                var root = new GameObject("Element floor lights");
                root.SetActive(false);
                var world = root.AddComponent<BlockWorld>();
                string[] ids = { "Lab_Heater_01", "Lab_Heater_02" };
                for (int i = 0; i < ids.Length; i++)
                    world.Set(new Vector3Int(i * 3, 1, 0), BlockLibrary.IndexOf(ids[i]));
                if (gameplay) root.AddComponent<BlockMap.Gameplay.BlockGameplayController>().Initialize(world);
                var spawner = root.AddComponent<BlockMapRuntimeSpawner>();
                spawner.maxLights = 2;
                root.SetActive(true);
                spawner.Rebuild();
                yield return null;
                Assert.AreEqual(2, root.GetComponentsInChildren<Light>().Length);
                Assert.AreEqual(0, root.GetComponentsInChildren<BlockMapLampHalo>().Length, "No detached round halo.");
                Assert.AreEqual(2, root.GetComponentsInChildren<BlockMap.Gameplay.ElementFloorRiseEffect>().Length);
                Assert.AreEqual(2, root.GetComponentsInChildren<BlockMap.Gameplay.GameplayBlockView>().Length);
                foreach (Light light in root.GetComponentsInChildren<Light>())
                {
                    Assert.That(light.transform.position.y, Is.InRange(1.12f, 1.3f), "Light must follow the actual pad surface.");
                    Assert.AreEqual(LightType.Point, light.type);
                    Assert.Greater(light.intensity, 0);
                }
                foreach (var effect in root.GetComponentsInChildren<BlockMap.Gameplay.ElementFloorRiseEffect>())
                {
                    effect.Initialize();
                    Assert.AreEqual(3, effect.transform.GetComponentsInChildren<Renderer>().Length - 2);
                    var first = effect.transform.Find("RisingRectangle_0");
                    effect.SetPhase(0);
                    float start = first.localPosition.y;
                    var block = new MaterialPropertyBlock();
                    first.GetComponent<Renderer>().GetPropertyBlock(block);
                    float alpha = block.GetColor("_Color").a;
                    Assert.That(start, Is.InRange(.12f, .14f));
                    effect.SetPhase(.5f);
                    first.GetComponent<Renderer>().GetPropertyBlock(block);
                    Assert.Greater(first.localPosition.y, start);
                    Assert.Less(block.GetColor("_Color").a, alpha);
                    Assert.AreSame(Resources.Load<Material>("ElementFloorRise"), first.GetComponent<Renderer>().sharedMaterial);
                }
                foreach (var view in root.GetComponentsInChildren<BlockMap.Gameplay.GameplayBlockView>())
                {
                    Material material = view.GetComponentInChildren<Renderer>().sharedMaterial;
                    Assert.AreEqual("Slima/Toon (Cel + Pixel)", material.shader.name);
                    Assert.Greater(material.GetFloat("_EmissionStrength"), 0);
                    Assert.NotNull(material.GetTexture("_EmissionMap"));
                    var casing = view.transform.Find(view.Profile.id + "/Casing").GetComponent<Renderer>();
                    Assert.AreNotSame(material.mainTexture, casing.sharedMaterial.mainTexture);
                    Assert.AreEqual(0, casing.sharedMaterial.GetFloat("_EmissionStrength"));
                    Assert.AreEqual(4, view.transform.Find(view.Profile.id + "/Top").GetComponent<MeshFilter>().sharedMesh.vertexCount);
                    Assert.AreEqual(20, casing.GetComponent<MeshFilter>().sharedMesh.vertexCount);
                }
                spawner.Rebuild();
                yield return null;
                Assert.AreEqual(2, root.GetComponentsInChildren<Light>().Length, "Rebuild must not lose lights.");
                Assert.AreEqual(2, root.GetComponentsInChildren<BlockMap.Gameplay.ElementFloorRiseEffect>().Length);
                spawner.maxLights = 0;
                spawner.Rebuild();
                yield return null;
                Assert.AreEqual(0, root.GetComponentsInChildren<Light>().Length);
                Assert.AreEqual(2, root.GetComponentsInChildren<BlockMap.Gameplay.ElementFloorRiseEffect>().Length, "Surface effects do not consume light slots.");
                world.Clear(); spawner.Rebuild();
                yield return null;
                Assert.AreEqual(0, root.GetComponentsInChildren<BlockMapLampHalo>().Length);
                Assert.AreEqual(0, root.GetComponentsInChildren<BlockMap.Gameplay.ElementFloorRiseEffect>().Length);
                Object.Destroy(root);
                yield return null;
            }
            yield return new ExitPlayMode();
        }

        [UnityTest]
        public IEnumerator LampsCreateHalosOnlyInPlayModeAndRebuildWithoutDuplicates()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            var root = new GameObject("Lamp halo test world");
            var world = root.AddComponent<BlockWorld>();
            string[] ids = { "FluorescentScreen_Lamp", "FloorLight_Lamp", "YellowLight_Lamp", "RedLight_Lamp", "StandingLight_Lamp" };
            for (int i = 0; i < ids.Length; i++) world.Set(new Vector3Int(i * 2, 0, 0), BlockLibrary.IndexOf(ids[i]));
            var spawner = root.AddComponent<BlockMapRuntimeSpawner>();
            spawner.maxLights = 0; // Visual halos do not consume or depend on Unity Light slots.
            spawner.Rebuild();
            yield return null;
            Assert.AreEqual(5, root.GetComponentsInChildren<BlockMapLampHalo>().Length);
            spawner.Rebuild();
            yield return null;
            Assert.AreEqual(5, root.GetComponentsInChildren<BlockMapLampHalo>().Length);
            Assert.AreEqual(0, root.GetComponentsInChildren<Collider>().Length);
            world.Clear(); spawner.Rebuild();
            yield return null;
            Assert.AreEqual(0, root.GetComponentsInChildren<BlockMapLampHalo>().Length);
            Object.Destroy(root);
            yield return null;
            yield return new ExitPlayMode();

            var editorRoot = new GameObject("Lamp editor test world");
            var editorWorld = editorRoot.AddComponent<BlockWorld>();
            editorWorld.Set(Vector3Int.zero, BlockLibrary.IndexOf(ids[0]));
            editorRoot.AddComponent<BlockMapRuntimeSpawner>().Rebuild();
            Assert.AreEqual(0, editorRoot.GetComponentsInChildren<BlockMapLampHalo>().Length);
            Object.DestroyImmediate(editorRoot);
        }
    }
}
