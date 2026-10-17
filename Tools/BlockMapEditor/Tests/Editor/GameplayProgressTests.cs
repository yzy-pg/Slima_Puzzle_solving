using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace BlockMap.Gameplay.Tests
{
    public sealed class GameplayProgressTests
    {
        GameObject _root;
        BlockWorld _world;
        BlockGameplayController _gameplay;
        SlimePartyController _party;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("GameplayProgressTests");
            _world = _root.AddComponent<BlockWorld>();
            int floor = BlockLibrary.IndexOf("Lab_WallBrick1");
            Assert.That(floor, Is.GreaterThanOrEqualTo(0));
            for (int x = 0; x < 6; x++)
                for (int z = 0; z < 6; z++) _world.Set(new Vector3Int(x, 0, z), floor);
            _gameplay = _root.AddComponent<BlockGameplayController>();
            _gameplay.Initialize(_world);
            _party = _root.AddComponent<SlimePartyController>();
            _party.Initialize(_gameplay, null, new Vector3Int(2, 1, 2), 0, 1f, 0f);
            _gameplay.BindParty(_party);
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            Object.DestroyImmediate(_root);
        }

        [Test]
        public void JsonRoundTripRestoresSplitActorsControlAndWorldChanges()
        {
            _world.Set(new Vector3Int(2, 1, 3), BlockLibrary.IndexOf("Splitter"));
            RefreshGameplay();
            Assert.That(_party.TrySplit(_party.ActiveActor), Is.True);
            _party.AdvanceSplitAnimation(_party.splitterCutDuration + _party.splitterJumpDuration + .01f);
            _party.Actors[0].SetState(new SlimeState
            { size = SlimeSize.Small, element = SlimeElement.Blue, opticsMode = BlueOpticsMode.ReflectRight });
            _party.Actors[1].SetState(new SlimeState { size = SlimeSize.Small, form = SlimeForm.Gas });
            _party.Actors[1].Mover.Teleport(new Vector3Int(4, 1, 4), 3);
            typeof(SlimePartyController).GetMethod("SelectNext", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(_party, null);
            _world.Set(new Vector3Int(0, 0, 0), -1);
            SlimeProgress[] actors = _party.CaptureProgress(out int active);
            var captured = new GameplayProgress
            { worldJson = _world.ToJson(), actors = actors, activeActor = active };
            var saved = JsonUtility.FromJson<GameplayProgress>(JsonUtility.ToJson(captured));
            _world.Set(new Vector3Int(0, 0, 0), BlockLibrary.IndexOf("Lab_WallBrick1"));
            _world.FromJson(saved.worldJson);
            _party.RestoreProgress(saved.actors, saved.activeActor);
            Assert.That(_party.Actors.Count, Is.EqualTo(2));
            Assert.That(_party.ActiveActor, Is.SameAs(_party.Actors[1]));
            Assert.That(_party.Actors[0].IsControlled, Is.False);
            Assert.That(_party.Actors[1].IsControlled, Is.True);
            Assert.That(_party.Actors[0].State.opticsMode, Is.EqualTo(BlueOpticsMode.ReflectRight));
            Assert.That(_party.Actors[1].State.form, Is.EqualTo(SlimeForm.Gas));
            Assert.That(_party.Actors[1].Mover.Cell, Is.EqualTo(new Vector3Int(4, 1, 4)));
            Assert.That(_party.Actors[1].Mover.Facing, Is.EqualTo(3));
            Assert.That(_world.IsSolid(new Vector3Int(0, 0, 0)), Is.False);
        }

        [Test]
        public void RestoreRebuildsLeverSignalsAndOpenDoor()
        {
            var lever = new Vector3Int(3, 1, 2);
            var door = new Vector3Int(4, 1, 2);
            _world.Set(lever, BlockLibrary.IndexOf("Lever_B"));
            _world.Set(door, BlockLibrary.IndexOf("Door"));
            _world.SetLinkId(lever, "saved-door");
            _world.SetLinkId(door, "saved-door");
            RefreshGameplay();
            _party.ActiveActor.Mover.Teleport(new Vector3Int(2, 1, 2), 1);
            Assert.That(_party.ActiveActor.Mover.TryMoveTo(lever), Is.True);
            FinishCurrentStep(_party.ActiveActor.Mover);
            RefreshGameplay();
            MechanismProgress[] captured = _gameplay.CaptureProgress();
            Assert.That(captured.Length, Is.EqualTo(2));
            Assert.That(System.Array.Exists(captured, progress => progress.cell == lever && progress.toggled), Is.True);
            Assert.That(System.Array.Exists(captured, progress => progress.cell == door && progress.toggled), Is.True);
            _gameplay.ResetState();
            RefreshGameplay();
            Assert.That(_gameplay.CanEnter(_party.ActiveActor, door, 1, false), Is.False);
            _gameplay.RestoreProgress(JsonUtility.FromJson<GameplayProgress>(
                JsonUtility.ToJson(new GameplayProgress { mechanisms = captured })).mechanisms);
            Assert.That(_gameplay.CanEnter(_party.ActiveActor, door, 1, false), Is.True);
        }

        [Test]
        public void RestoreRetainsLatchedReceiversAfterTheBeamIsGone()
        {
            var receiver = new Vector3Int(3, 1, 2);
            var door = new Vector3Int(4, 1, 2);
            _world.Set(receiver, BlockLibrary.IndexOf("Lab_Laser_05"));
            _world.Set(door, BlockLibrary.IndexOf("Door"));
            _world.SetLinkId(receiver, "latched-save");
            _world.SetLinkId(door, "latched-save");
            RefreshGameplay();
            bool changed = false;
            _gameplay.Changed += () => changed = true;
            _gameplay.ActivateReceiver(receiver);
            Assert.That(changed, Is.True, "Latching must notify the autosave session.");
            RefreshGameplay();
            MechanismProgress[] captured = _gameplay.CaptureProgress();
            Assert.That(System.Array.Exists(captured, progress => progress.cell == receiver && progress.latched), Is.True);
            Assert.That(System.Array.Exists(captured, progress => progress.cell == door && progress.toggled), Is.True);
            _gameplay.ResetState();
            _gameplay.RestoreProgress(captured);
            _gameplay.BeginReceiverEvaluation();
            RefreshGameplay();
            Assert.That(_gameplay.CanEnter(_party.ActiveActor, door, 1, false), Is.True);
        }

        [TestCase("Door", false)]
        [TestCase("Door_Inverted", true)]
        public void ContinuousReceiverTogglesOnBothEdgesButNotWhileHeld(string doorId, bool initiallyOpen)
        {
            var receiver = new Vector3Int(0, 1, 4);
            var emitter = new Vector3Int(0, 1, 0);
            var door = new Vector3Int(4, 1, 2);
            _world.Set(receiver, BlockLibrary.IndexOf("Lab_Laser_04"));
            _world.Set(door, BlockLibrary.IndexOf(doorId));
            _world.SetLinkId(receiver, "receiver-toggle");
            _world.SetLinkId(door, "receiver-toggle");
            RefreshGameplay();
            Assert.That(_gameplay.CanEnter(_party.ActiveActor, door, 1, false), Is.EqualTo(initiallyOpen));

            _world.Set(emitter, BlockLibrary.IndexOf("Lab_Laser_01"));
            RefreshGameplay();
            Assert.That(_gameplay.CanEnter(_party.ActiveActor, door, 1, false), Is.EqualTo(!initiallyOpen));
            RefreshGameplay();
            Assert.That(_gameplay.CanEnter(_party.ActiveActor, door, 1, false), Is.EqualTo(!initiallyOpen));

            _world.SetDir(emitter, 1);
            RefreshGameplay();
            Assert.That(_gameplay.CanEnter(_party.ActiveActor, door, 1, false), Is.EqualTo(initiallyOpen));
            RefreshGameplay();
            Assert.That(_gameplay.CanEnter(_party.ActiveActor, door, 1, false), Is.EqualTo(initiallyOpen));
            _world.SetDir(emitter, 0);
            RefreshGameplay();

            Assert.That(_gameplay.CanEnter(_party.ActiveActor, door, 1, false), Is.EqualTo(!initiallyOpen));
        }

        [Test]
        public void RestoringLitReceiverDoesNotToggleAgainAndStillHandlesBeamLoss()
        {
            var receiver = new Vector3Int(0, 1, 4);
            var emitter = new Vector3Int(0, 1, 0);
            var door = new Vector3Int(4, 1, 2);
            _world.Set(receiver, BlockLibrary.IndexOf("Lab_Laser_04"));
            _world.Set(emitter, BlockLibrary.IndexOf("Lab_Laser_01"));
            _world.Set(door, BlockLibrary.IndexOf("Door"));
            _world.SetLinkId(receiver, "receiver-save");
            _world.SetLinkId(door, "receiver-save");
            RefreshGameplay();
            MechanismProgress[] saved = _gameplay.CaptureProgress();

            _gameplay.RestoreProgress(saved);
            RefreshGameplay();
            Assert.That(_gameplay.CanEnter(_party.ActiveActor, door, 1, false), Is.True);

            _world.SetDir(emitter, 1);
            RefreshGameplay();
            Assert.That(_gameplay.CanEnter(_party.ActiveActor, door, 1, false), Is.False);
        }

        [Test]
        public void HeldReceiverDoesNotOverrideLeverUntilItsSignalChanges()
        {
            var receiver = new Vector3Int(0, 1, 4);
            var emitter = new Vector3Int(0, 1, 0);
            var door = new Vector3Int(4, 1, 2);
            var lever = new Vector3Int(3, 1, 2);
            _world.Set(receiver, BlockLibrary.IndexOf("Lab_Laser_04"));
            _world.Set(emitter, BlockLibrary.IndexOf("Lab_Laser_01"));
            _world.Set(door, BlockLibrary.IndexOf("Door"));
            _world.Set(lever, BlockLibrary.IndexOf("Lever_B"));
            foreach (Vector3Int cell in new[] { receiver, door, lever }) _world.SetLinkId(cell, "shared-door");
            RefreshGameplay();
            Assert.That(_gameplay.CanEnter(_party.ActiveActor, door, 1, false), Is.True);

            _party.ActiveActor.Mover.TryMoveTo(lever);
            FinishCurrentStep(_party.ActiveActor.Mover);
            RefreshGameplay();
            RefreshGameplay();
            Assert.That(_gameplay.CanEnter(_party.ActiveActor, door, 1, false), Is.False);

            _world.SetDir(emitter, 1);
            RefreshGameplay();
            Assert.That(_gameplay.CanEnter(_party.ActiveActor, door, 1, false), Is.True);
        }

        [Test]
        public void RestoringPressedPlateSeedsItsSignalWithoutAnExtraToggle()
        {
            var plate = new Vector3Int(2, 1, 2);
            var door = new Vector3Int(4, 1, 4);
            _world.Set(plate, BlockLibrary.IndexOf("Lab_PressurePlate"));
            _world.Set(door, BlockLibrary.IndexOf("Door"));
            _world.SetLinkId(plate, "plate-save");
            _world.SetLinkId(door, "plate-save");
            RefreshGameplay();
            SlimeProgress[] actors = _party.CaptureProgress(out int active);
            MechanismProgress[] mechanisms = _gameplay.CaptureProgress();

            _gameplay.RestoreProgress(mechanisms);
            RefreshGameplay();
            RefreshGameplay();
            Assert.That(_gameplay.CanEnter(_party.ActiveActor, door, 0, false), Is.True);
            _party.ActiveActor.Mover.Teleport(new Vector3Int(1, 1, 1), 0);
            RefreshGameplay();
            Assert.That(_gameplay.CanEnter(_party.ActiveActor, door, 0, false), Is.False);

            _party.RestoreProgress(actors, active);
            _gameplay.RestoreProgress(mechanisms);
            RefreshGameplay();
            Assert.That(_gameplay.CanEnter(_party.ActiveActor, door, 0, false), Is.True);
            _party.ActiveActor.Mover.Teleport(new Vector3Int(1, 1, 1), 0);
            RefreshGameplay();
            Assert.That(_gameplay.CanEnter(_party.ActiveActor, door, 0, false), Is.False);
        }

        [Test]
        public void SaveWhileEmitterOccupiesDoorPreservesBothObjectsAndDoorMetadata()
        {
            var lever = new Vector3Int(2, 1, 3);
            var emitter = new Vector3Int(3, 1, 2);
            var door = new Vector3Int(4, 1, 2);
            _world.Set(lever, BlockLibrary.IndexOf("Lever_B"));
            _world.Set(emitter, BlockLibrary.IndexOf("Lab_Laser_01"));
            _world.Set(door, BlockLibrary.IndexOf("Door"));
            _world.SetLinkId(lever, "occupied-door-save");
            _world.SetLinkId(door, "occupied-door-save");
            _world.SetDir(emitter, 2);
            _world.SetDir(door, 3);
            _world.SetMechanismColor(door, Color.yellow);
            RefreshGameplay();
            _party.ActiveActor.Mover.Teleport(lever, 0);
            _gameplay.ProcessCellEntry(_party.ActiveActor, lever);
            Assert.That(_world.Move(emitter, door, allowCover: true), Is.True);
            RefreshGameplay();
            string savedWorld = _world.ToJson();
            MechanismProgress[] savedMechanisms = _gameplay.CaptureProgress();

            _world.FromJson(savedWorld);
            _gameplay.RestoreProgress(savedMechanisms);
            _party.ActiveActor.Mover.Teleport(emitter, 1);
            RefreshGameplay();
            Assert.That(_world.GetIndex(door), Is.EqualTo(BlockLibrary.IndexOf("Lab_Laser_01")));
            Assert.That(_world.GetLinkId(door, "Door"), Is.EqualTo("occupied-door-save"));
            Assert.That(_world.GetDir(door), Is.EqualTo(2));
            Assert.That(_gameplay.CanEnter(_party.ActiveActor, door, 1, true), Is.True);
            RefreshGameplay();
            Assert.That(_world.GetIndex(door), Is.EqualTo(BlockLibrary.IndexOf("Door")));
            Assert.That(_world.GetDir(door), Is.EqualTo(3));
            Assert.That(_world.GetLinkId(door), Is.EqualTo("occupied-door-save"));
            Assert.That(_world.TryGetMechanismColor(door, out Color restoredColor), Is.True);
            Assert.That(restoredColor, Is.EqualTo(Color.yellow));
            Assert.That(_gameplay.CanEnter(_party.ActiveActor, door, 1, false), Is.True);
        }

        [Test]
        public void PauseStopsMovementEvenWhenARouteIsQueued()
        {
            SlimeGridMover mover = _party.ActiveActor.Mover;
            Vector3Int start = mover.Cell;
            Assert.That(mover.TryMoveTo(start + Vector3Int.right), Is.True);
            Vector3 position = mover.transform.position;
            Time.timeScale = 0f;
            typeof(SlimeGridMover).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(mover, null);
            Assert.That(mover.Cell, Is.EqualTo(start));
            Assert.That(mover.transform.position, Is.EqualTo(position));
        }

        void RefreshGameplay() => typeof(BlockGameplayController)
            .GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_gameplay, null);

        static void FinishCurrentStep(SlimeGridMover mover)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(SlimeGridMover).GetField("_progress", flags).SetValue(mover, 1f);
            typeof(SlimeGridMover).GetMethod("AdvanceStep", flags).Invoke(mover, null);
        }
    }
}
