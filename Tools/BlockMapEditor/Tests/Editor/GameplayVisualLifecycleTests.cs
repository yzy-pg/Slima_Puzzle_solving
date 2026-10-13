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
