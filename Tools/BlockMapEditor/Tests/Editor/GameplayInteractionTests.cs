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
            _party.Initialize(_gameplay, null, new Vector3Int(2, 1, 2), 0, 1f, 0f);
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
        public void GasMayOverlapSolidButCannotSolidifyThere()
        {
            ArrangeSmallPair();
            SlimeActor gas = _party.ActiveActor;
            gas.SetState(new SlimeState { size = SlimeSize.Small, form = SlimeForm.Gas });
            var route = new List<Vector3Int>();
            Assert.That(_party.TryBuildRoute(gas.Mover, 1, route), Is.True);
            gas.Mover.Teleport(_party.Actors[1].Mover.Cell, 1);
            Assert.That(_gameplay.CanSolidify(gas), Is.False);
            typeof(SlimePartyController).GetMethod("UseAbility", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_party, null);
            Assert.That(gas.State.form, Is.EqualTo(SlimeForm.Gas));
            gas.Mover.Teleport(new Vector3Int(4, 1, 2), 1);
            Assert.That(_gameplay.CanSolidify(gas), Is.True);
        }

        [Test]
        public void LeverPromptQueryDoesNotToggleTheLever()
        {
            var lever = new Vector3Int(2, 1, 3);
            var door = new Vector3Int(4, 1, 4);
            _world.Set(lever, BlockLibrary.IndexOf("Lever_B"));
            _world.Set(door, BlockLibrary.IndexOf("Door"));
            _world.SetLinkId(lever, "query-test");
            _world.SetLinkId(door, "query-test");
            RefreshGameplay();
            Assert.That(_gameplay.TryGetInteraction(_party.ActiveActor, out SlimeInteraction interaction), Is.True);
            Assert.That(interaction.Kind, Is.EqualTo(SlimeInteractionKind.Lever));
            Assert.That(interaction.Cell, Is.EqualTo(lever));
            Assert.That(_gameplay.TryGetInteraction(_party.ActiveActor, out _), Is.True);
            RefreshGameplay();
            Assert.That(_gameplay.CanEnter(_party.ActiveActor, door, 0, false), Is.False);
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
            Assert.That(_gameplay.TryInteract(_party.ActiveActor), Is.True);
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
    }
}
