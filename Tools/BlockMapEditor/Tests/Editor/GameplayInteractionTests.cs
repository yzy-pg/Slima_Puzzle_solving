using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace BlockMap.Gameplay.Tests
{
    public sealed class GameplayInteractionTests
    {
        GameObject _root;
        BlockWorld _world;
        BlockGameplayController _gameplay;
        SlimePartyController _party;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("GameplayInteractionTests");
            _world = _root.AddComponent<BlockWorld>();
            int floor = BlockLibrary.IndexOf("Lab_WallBrick1");
            Assert.That(floor, Is.GreaterThanOrEqualTo(0));
            for (int x = 0; x < 6; x++)
                for (int z = 0; z < 6; z++)
                    _world.Set(new Vector3Int(x, 0, z), floor);
            _gameplay = _root.AddComponent<BlockGameplayController>();
            _gameplay.Initialize(_world);
            _party = _root.AddComponent<SlimePartyController>();
            _party.Initialize(_gameplay, null, new Vector3Int(2, 1, 2), 0, 1f, 0f, true);
            _gameplay.BindParty(_party);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        void ArrangeSmallPair()
        {
            var splitter = new Vector3Int(2, 1, 3);
            _world.Set(splitter, BlockLibrary.IndexOf("Splitter"));
            RefreshGameplay();
            Assert.That(_party.TrySplit(_party.ActiveActor), Is.True);
            _party.AdvanceSplitAnimation(_party.splitterCutDuration + _party.splitterJumpDuration + .01f);
            _world.Remove(splitter);
            RefreshGameplay();
            _party.Actors[0].Mover.Teleport(new Vector3Int(2, 1, 2), 1);
            _party.Actors[1].Mover.Teleport(new Vector3Int(3, 1, 2), 3);
        }

        void RefreshGameplay()
        {
            typeof(BlockGameplayController).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_gameplay, null);
        }

        static void FinishCurrentStep(SlimeGridMover mover)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(SlimeGridMover).GetField("_progress", flags).SetValue(mover, 1f);
            typeof(SlimeGridMover).GetMethod("AdvanceStep", flags).Invoke(mover, null);
        }

        [Test]
        public void PressingFusionMovesTheControlledSlimeAndKeepsItsControl()
        {
            ArrangeSmallPair();
            SlimeActor controlled = _party.ActiveActor;
            Vector3Int destination = _party.Actors[1].Mover.Cell;
            Assert.That(_gameplay.TryInteract(controlled), Is.True);
            Assert.That(controlled.Mover.IsMoving, Is.True);
            Assert.That(_party.Actors.Count, Is.EqualTo(2));

            FinishCurrentStep(controlled.Mover);

            Assert.That(_party.Actors.Count, Is.EqualTo(1));
            Assert.That(_party.ActiveActor, Is.SameAs(controlled));
            Assert.That(controlled.IsControlled, Is.True);
            Assert.That(controlled.Mover.Cell, Is.EqualTo(destination));
            Assert.That(controlled.State.size, Is.EqualTo(SlimeSize.Large));
        }

        [Test]
        public void BlueSlideArrivalFusesWithoutPressingF()
        {
            ArrangeSmallPair();
            SlimeActor controlled = _party.ActiveActor;
            controlled.SetState(new SlimeState { size = SlimeSize.Small, element = SlimeElement.Blue });
            Assert.That(controlled.Mover.TryMoveTo(_party.Actors[1].Mover.Cell), Is.True);
            FinishCurrentStep(controlled.Mover);
            Assert.That(_party.Actors.Count, Is.EqualTo(1));
            Assert.That(controlled.State.size, Is.EqualTo(SlimeSize.Large));
            Assert.That(controlled.Mover.HasPendingMovement, Is.False);
        }

        [Test]
        public void OrdinaryMovementCannotEnterAnotherSmallSlime()
        {
            ArrangeSmallPair();
            var route = new List<Vector3Int>();
            Assert.That(_party.TryBuildRoute(_party.ActiveActor.Mover, 1, route), Is.False);
            Assert.That(_party.CanFuseFacing(_party.ActiveActor), Is.True);
        }

        [Test]
        public void FusionRequiresFacingTheOtherSlime()
        {
            ArrangeSmallPair();
            _party.ActiveActor.Mover.Teleport(new Vector3Int(2, 1, 2), 0);
            Assert.That(_party.CanFuseFacing(_party.ActiveActor), Is.False);
        }

        [Test]
        public void BlueSlideEndsAtFusionTarget()
        {
            ArrangeSmallPair();
            _party.ActiveActor.SetState(new SlimeState { size = SlimeSize.Small, element = SlimeElement.Blue });
            var route = new List<Vector3Int>();
            Assert.That(_party.TryBuildRoute(_party.ActiveActor.Mover, 1, route), Is.True);
            Assert.That(route, Is.EqualTo(new[] { new Vector3Int(3, 1, 2) }));
        }

        [Test]
        public void ColoredFloorsAlwaysConvertSlimeToTheirSolidColor()
        {
            Vector3Int redFloor = new Vector3Int(3, 1, 2);
            Vector3Int blueFloor = new Vector3Int(4, 1, 2);
            _world.Set(redFloor, BlockLibrary.IndexOf("Lab_Heater_01"));
            _world.Set(blueFloor, BlockLibrary.IndexOf("Lab_Heater_02"));
            RefreshGameplay();
            SlimeActor slime = _party.ActiveActor;
            slime.SetState(new SlimeState { form = SlimeForm.Gas });
            slime.Mover.Teleport(redFloor, 1);

            Assert.That(_gameplay.ApplyPassive(slime), Is.False);
            Assert.That(slime.State.form, Is.EqualTo(SlimeForm.Solid));
            Assert.That(slime.State.element, Is.EqualTo(SlimeElement.Red));
            slime.Mover.Teleport(blueFloor, 1);
            Assert.That(_gameplay.ApplyPassive(slime), Is.False);
            Assert.That(slime.State.form, Is.EqualTo(SlimeForm.Solid));
            Assert.That(slime.State.element, Is.EqualTo(SlimeElement.Blue));
        }

        [Test]
        public void RedBeamMixesWithBlueSlimeBeforeSplittingToPurpleReceiver()
        {
            var emitter = new Vector3Int(1, 1, 2);
            var blueSlimeCell = new Vector3Int(2, 1, 2);
            var receiver = new Vector3Int(2, 1, 3);
            var door = new Vector3Int(4, 1, 4);
            _world.Set(emitter, BlockLibrary.IndexOf("Lab_Laser_02"));
            _world.SetDir(emitter, 1);
            _world.Set(receiver, BlockLibrary.IndexOf("ReceiverPurple_A"));
            _world.Set(door, BlockLibrary.IndexOf("Door"));
            _world.SetLinkId(receiver, "purple-beam");
            _world.SetLinkId(door, "purple-beam");
            SlimeActor blue = _party.ActiveActor;
            blue.SetState(new SlimeState { element = SlimeElement.Blue, size = SlimeSize.Small });
            blue.Mover.Teleport(blueSlimeCell, 0);

            RefreshGameplay();

            Assert.That(_gameplay.CanEnter(blue, door, 0, false), Is.True);
        }

        [Test]
        public void LeverIsNotFInteractableAndTogglesDoorWhenEntered()
        {
            var lever = new Vector3Int(2, 1, 3);
            var door = new Vector3Int(4, 1, 4);
            _world.Set(lever, BlockLibrary.IndexOf("Lever_B"));
            _world.Set(door, BlockLibrary.IndexOf("Door"));
            _world.SetLinkId(lever, "query-test");
            _world.SetLinkId(door, "query-test");
            RefreshGameplay();
            Assert.That(_gameplay.TryGetInteraction(_party.ActiveActor, out _), Is.False);
            Assert.That(_gameplay.TryInteract(_party.ActiveActor), Is.False);

            _party.ActiveActor.Mover.Teleport(new Vector3Int(2, 1, 2), 0);
            Assert.That(_party.ActiveActor.Mover.TryMoveTo(lever), Is.True);
            FinishCurrentStep(_party.ActiveActor.Mover);
            RefreshGameplay();
            Assert.That(_gameplay.CanEnter(_party.ActiveActor, door, 0, false), Is.True);
        }

        [Test]
        public void EveryLinkedLeverEntryTogglesTheSharedDoor()
        {
            var leverA = new Vector3Int(2, 1, 3);
            var leverB = new Vector3Int(4, 1, 2);
            var door = new Vector3Int(4, 1, 4);
            _world.Set(leverA, BlockLibrary.IndexOf("Lab_Lever"));
            _world.Set(leverB, BlockLibrary.IndexOf("Lab_Lever"));
            _world.Set(door, BlockLibrary.IndexOf("Door"));
            _world.SetLinkId(leverA, "shared-door");
            _world.SetLinkId(leverB, "shared-door");
            _world.SetLinkId(door, "shared-door");
            RefreshGameplay();

            EnterLever(leverA, new Vector3Int(2, 1, 2), 0);
            Assert.That(_gameplay.CanEnter(_party.ActiveActor, door, 0, false), Is.True);
            EnterLever(leverB, new Vector3Int(4, 1, 1), 0);
            Assert.That(_gameplay.CanEnter(_party.ActiveActor, door, 0, false), Is.False);
            EnterLever(leverA, new Vector3Int(2, 1, 2), 0);
            Assert.That(_gameplay.CanEnter(_party.ActiveActor, door, 0, false), Is.True);
        }

        [Test]
        public void PressurePlateActivationTogglesDoorOnlyOnEachEntryEdge()
        {
            var plate = new Vector3Int(2, 1, 2);
            var door = new Vector3Int(4, 1, 4);
            _world.Set(plate, BlockLibrary.IndexOf("Lab_PressurePlate"));
            _world.Set(door, BlockLibrary.IndexOf("Door"));
            _world.SetLinkId(plate, "plate-toggle");
            _world.SetLinkId(door, "plate-toggle");
            RefreshGameplay();
            Assert.That(_gameplay.CanEnter(_party.ActiveActor, door, 0, false), Is.True);

            RefreshGameplay();
            Assert.That(_gameplay.CanEnter(_party.ActiveActor, door, 0, false), Is.True);
            _party.ActiveActor.Mover.Teleport(new Vector3Int(1, 1, 1), 0);
            RefreshGameplay();
            _party.ActiveActor.Mover.Teleport(plate, 0);
            RefreshGameplay();

            Assert.That(_gameplay.CanEnter(_party.ActiveActor, door, 0, false), Is.False);
        }

        [Test]
        public void RedSlimeCanActivelyFuseWithBlueSlime()
        {
            ArrangeSmallPair();
            SlimeActor red = _party.Actors[0];
            SlimeActor blue = _party.Actors[1];
            red.SetState(new SlimeState { size = SlimeSize.Small, element = SlimeElement.Red });
            blue.SetState(new SlimeState { size = SlimeSize.Small, element = SlimeElement.Blue });
            red.Mover.Teleport(new Vector3Int(2, 1, 2), 1);
            blue.Mover.Teleport(new Vector3Int(3, 1, 2), 3);

            Assert.That(_party.CanFuseFacing(red), Is.True);
            Assert.That(_party.CanFuseFacing(blue), Is.True);
            Assert.That(_party.TryFuseFacing(red), Is.True);
            FinishCurrentStep(red.Mover);

            Assert.That(_party.Actors.Count, Is.EqualTo(1));
            Assert.That(red.State.form, Is.EqualTo(SlimeForm.Solid));
            Assert.That(red.State.element, Is.EqualTo(SlimeElement.Neutral));
        }

        [Test]
        public void SynchronousBlueSlimeMovesUntilBlockedWhenControlledSlimeIsBlocked()
        {
            ArrangeSmallPair();
            SlimeActor controlled = _party.Actors[0];
            SlimeActor blue = _party.Actors[1];
            controlled.Mover.Teleport(new Vector3Int(2, 1, 2), 0);
            blue.SetState(new SlimeState { size = SlimeSize.Small, element = SlimeElement.Blue });
            blue.Mover.Teleport(new Vector3Int(3, 1, 2), 0);
            _world.Set(new Vector3Int(2, 1, 3), BlockLibrary.IndexOf("Door"));
            _world.Set(new Vector3Int(3, 1, 5), BlockLibrary.IndexOf("Door"));
            RefreshGameplay();

            var controlledRoute = new List<Vector3Int>();
            Assert.That(_party.TryBuildRoute(controlled.Mover, 0, controlledRoute), Is.False);
            Assert.That(controlled.Mover.HasPendingMovement, Is.False);
            Assert.That(blue.Mover.HasPendingMovement, Is.True);
            Assert.That(blue.Mover.HasQueuedRoute, Is.True);
        }

        [Test]
        public void BlueSlideTriggersLeverWhenPassingThroughItsCell()
        {
            var lever = new Vector3Int(2, 1, 2);
            var door = new Vector3Int(4, 1, 4);
            _world.Set(lever, BlockLibrary.IndexOf("Lab_Lever"));
            _world.Set(door, BlockLibrary.IndexOf("Door"));
            _world.SetLinkId(lever, "slide-trigger");
            _world.SetLinkId(door, "slide-trigger");
            RefreshGameplay();
            SlimeActor blue = _party.ActiveActor;
            blue.SetState(new SlimeState { size = SlimeSize.Small, element = SlimeElement.Blue });
            blue.Mover.Teleport(new Vector3Int(1, 1, 2), 1);

            var route = new List<Vector3Int>();
            Assert.That(_party.TryBuildRoute(blue.Mover, 1, route), Is.True);
            Assert.That(route, Does.Contain(lever));
            Assert.That(blue.Mover.QueueRoute(route), Is.True);
            FinishCurrentStep(blue.Mover);
            RefreshGameplay();

            Assert.That(_gameplay.CanEnter(blue, door, 0, false), Is.True);
            Assert.That(blue.Mover.HasQueuedRoute, Is.True);
        }

        [Test]
        public void RemovingAnObstaclePreservesTheOpenDoorObjectAndAngle()
        {
            var doorCell = new Vector3Int(4, 1, 4);
            var leverCell = new Vector3Int(2, 1, 3);
            var obstacle = new Vector3Int(3, 1, 3);
            _world.Set(doorCell, BlockLibrary.IndexOf("Door"));
            _world.Set(leverCell, BlockLibrary.IndexOf("Lever_B"));
            _world.Set(obstacle, BlockLibrary.IndexOf("Breakable"));
            _world.SetLinkId(doorCell, "test");
            _world.SetLinkId(leverCell, "test");
            RefreshGameplay();
            EnterLever(leverCell, new Vector3Int(2, 1, 2), 0);
            RefreshGameplay();
            BlockMapDoor door = _root.GetComponentInChildren<BlockMapDoor>();
            Assert.That(door, Is.Not.Null);
            door.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            int identity = door.GetInstanceID();
            Quaternion angle = door.transform.localRotation;

            _world.Remove(obstacle);
            RefreshGameplay();

            BlockMapDoor remaining = _root.GetComponentInChildren<BlockMapDoor>();
            Assert.That(remaining.GetInstanceID(), Is.EqualTo(identity));
            Assert.That(remaining.transform.localRotation, Is.EqualTo(angle));
            Assert.That(_gameplay.CanEnter(_party.ActiveActor, doorCell, 0, false), Is.True);
        }

        void EnterLever(Vector3Int lever, Vector3Int approach, int facing)
        {
            SlimeActor actor = _party.ActiveActor;
            actor.Mover.Teleport(approach, facing);
            Assert.That(actor.Mover.TryMoveTo(lever), Is.True);
            FinishCurrentStep(actor.Mover);
            RefreshGameplay();
        }
    }
}
