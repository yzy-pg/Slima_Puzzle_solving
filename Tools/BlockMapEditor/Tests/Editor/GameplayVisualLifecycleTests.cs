using BlockMap.Gameplay;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace BlockMap.Tests
{
    public sealed class GameplayVisualLifecycleTests
    {
        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // Scene cleanup can unload the catalog's transient placeholder definitions.
            BlockGameplayCatalog.Reload();
            BlockLibrary.Reload();
        }

        [Test]
        public void DoorsSlideRightThenHideAndReturnWithoutRotating()
        {
            var parent = new GameObject("Door test");
            try
            {
                foreach (string id in new[] { "Door", "Door_B" })
                    for (int direction = 0; direction < 4; direction++)
                    {
                        var view = GameplayPrototypeFactory.Create(BlockLibrary.Get(id), BlockGameplayCatalog.Get(id),
                            Vector3Int.zero, direction, parent.transform);
                        var door = view.GetComponentInChildren<BlockMapDoor>();
                        Assert.NotNull(door.hinge);
                        Assert.Less(Quaternion.Angle(view.transform.rotation, Quaternion.Euler(0, direction * 90, 0)), .01f);
                        Assert.IsFalse(door.IsOpen);
                        Assert.Less(Quaternion.Angle(door.hinge.localRotation, Quaternion.identity), .01f);
                        var renderer = view.GetComponentInChildren<Renderer>();
                        Vector3 center = renderer.bounds.center;
                        Vector3 panelPosition = door.hinge.localPosition;
                        Quaternion panelRotation = door.hinge.localRotation;
                        Vector3 rootPosition = view.transform.position;
                        Assert.IsTrue(renderer.enabled);
                        view.SetActivated(true);
                        Assert.IsTrue(door.IsOpen);
                        door.AdvanceAnimation(door.duration / 2f);
                        Assert.Less(Vector3.Distance(door.hinge.localPosition,panelPosition+Vector3.right*door.slideDistance*.5f),.0001f);
                        Assert.IsTrue(renderer.enabled);
                        door.AdvanceAnimation(door.duration / 2f);
                        Assert.Less(Quaternion.Angle(door.hinge.localRotation,panelRotation),.01f);
                        Assert.Less(Vector3.Distance(door.hinge.localPosition,panelPosition+Vector3.right*door.slideDistance),.0001f);
                        Assert.IsFalse(renderer.enabled);
                        Assert.IsFalse(door.hinge.gameObject.activeInHierarchy);
                        Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off,renderer.shadowCastingMode);
                        Assert.Less(Vector3.Distance(renderer.bounds.center-center,view.transform.right*door.slideDistance),.0001f);
                        Assert.AreEqual(rootPosition, view.transform.position);
                        view.SetActivated(false); Assert.IsTrue(renderer.enabled); door.AdvanceAnimation(door.duration);
                        Assert.IsFalse(door.IsOpen);
                        Assert.Less(Quaternion.Angle(door.hinge.localRotation,panelRotation),.01f);
                        Assert.Less(Vector3.Distance(door.hinge.localPosition,panelPosition),.0001f);
                        Assert.Less(Vector3.Distance(center, renderer.bounds.center), .0001f);
                        Object.DestroyImmediate(view.gameObject);
                    }
            }
            finally { Object.DestroyImmediate(parent); }
        }

        [Test]
        public void FractionalFrameStepsReachTheHiddenEndpointAndRestoreRendering()
        {
            var parent=new GameObject("Fractional door animation");
            try
            {
                var view=GameplayPrototypeFactory.Create(BlockLibrary.Get("Door"),BlockGameplayCatalog.Get("Door"),Vector3Int.zero,0,parent.transform);
                var door=view.GetComponentInChildren<BlockMapDoor>();
                var leaf=door.hinge.GetComponentInChildren<Renderer>(true);
                view.SetActivated(true);
                for(int frame=0;frame<120;frame++) door.AdvanceAnimation(door.duration/12);
                Assert.IsFalse(leaf.enabled);
                Assert.IsFalse(door.hinge.gameObject.activeInHierarchy);
                Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.Off,leaf.shadowCastingMode);
                view.SetActivated(false);
                for(int frame=0;frame<12;frame++) door.AdvanceAnimation(door.duration/12);
                Assert.IsTrue(leaf.enabled);
                Assert.IsTrue(door.hinge.gameObject.activeInHierarchy);
                Assert.AreEqual(UnityEngine.Rendering.ShadowCastingMode.On,leaf.shadowCastingMode);
            }
            finally { Object.DestroyImmediate(parent); }
        }

        [TestCase("Door", 1)]
        [TestCase("Door", 2)]
        [TestCase("Door", 3)]
        [TestCase("Door_B", 1)]
        [TestCase("Door_B", 2)]
        [TestCase("Door_B", 3)]
        public void ExportCapturesDoorRotationAndRebuildRestoresIt(string id, int direction)
        {
            var root = new GameObject("Door export test");
            try
            {
                var cell = new Vector3Int(2, 1, 3);
                var world = root.AddComponent<BlockWorld>();
                world.Set(cell, BlockLibrary.IndexOf(id));
                var spawner = root.AddComponent<BlockMapRuntimeSpawner>();
                spawner.Rebuild();
                var view = root.GetComponentInChildren<GameplayBlockView>();
                view.transform.rotation = Quaternion.Euler(0, direction * 90, 0);

                string json = world.ToJson();
                var saved = JsonUtility.FromJson<BlockWorld.SaveData>(json);
                Assert.AreEqual(direction, saved.blocks[0].r, "Export lost the scene-authored door rotation.");
                world.FromJson(json);
                spawner.Rebuild();

                Assert.AreEqual(direction, world.GetDir(cell));
                Assert.Less(Quaternion.Angle(root.GetComponentInChildren<GameplayBlockView>().transform.rotation,
                    Quaternion.Euler(0, direction * 90, 0)), .01f);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void ExportBeforePreviewRebuildKeepsNewDataDirection()
        {
            var root = new GameObject("Door pending rotation test");
            try
            {
                var cell = new Vector3Int(2, 1, 3);
                var world = root.AddComponent<BlockWorld>();
                world.Set(cell, BlockLibrary.IndexOf("Door"));
                root.AddComponent<BlockMapRuntimeSpawner>().Rebuild();
                world.SetDir(cell, 2);

                var saved = JsonUtility.FromJson<BlockWorld.SaveData>(world.ToJson());
                Assert.AreEqual(2, saved.blocks[0].r, "An unchanged preview overwrote the new data direction.");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void Rotate90IsIncludedInDoorExport()
        {
            var root = new GameObject("Door Rotate90 export test");
            try
            {
                var world = root.AddComponent<BlockWorld>();
                world.Set(Vector3Int.zero, BlockLibrary.IndexOf("Door"));
                root.AddComponent<BlockMapRuntimeSpawner>().Rebuild();
                root.GetComponentInChildren<BlockMapDoor>().Rotate90();

                var saved = JsonUtility.FromJson<BlockWorld.SaveData>(world.ToJson());
                Assert.AreEqual(1, saved.blocks[0].r);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void RepeatedExportsCaptureRotationBackToZero()
        {
            var root = new GameObject("Door repeated export test");
            try
            {
                var world = root.AddComponent<BlockWorld>();
                world.Set(Vector3Int.zero, BlockLibrary.IndexOf("Door"));
                root.AddComponent<BlockMapRuntimeSpawner>().Rebuild();
                var view = root.GetComponentInChildren<GameplayBlockView>();
                view.transform.rotation = Quaternion.Euler(0, 90, 0);
                world.ToJson();
                view.transform.rotation = Quaternion.identity;

                var saved = JsonUtility.FromJson<BlockWorld.SaveData>(world.ToJson());
                Assert.AreEqual(0, saved.blocks[0].r);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void ExportRestoresSceneDataAndCapturesEditedDoorAfterReload()
        {
            var root = new GameObject("Door serialized export test");
            GameObject restored = null;
            try
            {
                var world = root.AddComponent<BlockWorld>();
                world.Set(Vector3Int.zero, BlockLibrary.IndexOf("Door"));
                root.AddComponent<BlockMapRuntimeSpawner>().Rebuild();
                world.OnBeforeSerialize();
                restored = Object.Instantiate(root);
                var restoredWorld = restored.GetComponent<BlockWorld>();
                restored.GetComponentInChildren<GameplayBlockView>().transform.rotation = Quaternion.Euler(0, 270, 0);

                var saved = JsonUtility.FromJson<BlockWorld.SaveData>(restoredWorld.ToJson());
                Assert.AreEqual(1, saved.blocks.Count);
                Assert.AreEqual(3, saved.blocks[0].r);
            }
            finally
            {
                Object.DestroyImmediate(restored);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void OpeningDoorDoesNotChangeExportedDirection()
        {
            var root = new GameObject("Open door export test");
            try
            {
                var world = root.AddComponent<BlockWorld>();
                world.Set(Vector3Int.zero, BlockLibrary.IndexOf("Door"));
                world.SetDir(Vector3Int.zero, 3);
                root.AddComponent<BlockMapRuntimeSpawner>().Rebuild();
                var door = root.GetComponentInChildren<BlockMapDoor>();
                door.Open();
                door.AdvanceAnimation(door.duration);

                var saved = JsonUtility.FromJson<BlockWorld.SaveData>(world.ToJson());
                Assert.AreEqual(3, saved.blocks[0].r);
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void BakedPreviewIsReplacedAndItsDoorOrientationSurvivesReset()
        {
            var root = new GameObject("Baked preview test");
            try
            {
                var world = root.AddComponent<BlockWorld>();
                world.Set(Vector3Int.zero, BlockLibrary.IndexOf("Door"));
                world.SetDir(Vector3Int.zero, 1);
                var baked = GameplayPrototypeFactory.Create(BlockLibrary.Get("Door"), BlockGameplayCatalog.Get("Door"),
                    Vector3Int.zero, 0, root.transform);
                var controller = root.AddComponent<BlockGameplayController>();
                controller.Initialize(world);
                Assert.IsTrue(baked == null, "Baked model was retained beneath the animated instance.");
                Assert.AreEqual(0, world.GetDir(Vector3Int.zero), "Entering gameplay turned the authored door by 90 degrees.");
                Assert.AreEqual(1, root.GetComponentsInChildren<GameplayBlockView>().Length);
                var door = root.GetComponentInChildren<BlockMapDoor>();
                door.Open(); door.AdvanceAnimation(door.duration);
                Assert.AreEqual(1, root.GetComponentsInChildren<GameplayBlockView>().Length);
                controller.ResetState();
                Assert.AreEqual(1, root.GetComponentsInChildren<GameplayBlockView>().Length);
                door = root.GetComponentInChildren<BlockMapDoor>();
                Assert.IsFalse(door.IsOpen);
                Assert.Less(Quaternion.Angle(door.hinge.localRotation, Quaternion.identity), .01f);
                Assert.AreEqual(Vector3.zero,door.hinge.localPosition);
                Assert.IsTrue(root.GetComponentInChildren<Renderer>().enabled);
                Assert.Less(Quaternion.Angle(root.GetComponentInChildren<GameplayBlockView>().transform.rotation,
                    Quaternion.identity), .01f);
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
