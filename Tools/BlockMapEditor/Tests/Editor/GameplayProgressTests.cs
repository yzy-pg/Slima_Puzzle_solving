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

        [Test]
        public void EachBeamReceiverActivationTogglesItsSharedDoor()
        {
            var receiver = new Vector3Int(3, 1, 2);
            var door = new Vector3Int(4, 1, 2);
            _world.Set(receiver, BlockLibrary.IndexOf("Lab_Laser_04"));
            _world.Set(door, BlockLibrary.IndexOf("Door"));
            _world.SetLinkId(receiver, "receiver-toggle");
            _world.SetLinkId(door, "receiver-toggle");
            RefreshGameplay();

            _gameplay.ActivateReceiver(receiver);
            RefreshGameplay();
            Assert.That(_gameplay.CanEnter(_party.ActiveActor, door, 1, false), Is.True);
            _gameplay.BeginReceiverEvaluation();
            RefreshGameplay();
            _gameplay.ActivateReceiver(receiver);
            RefreshGameplay();

            Assert.That(_gameplay.CanEnter(_party.ActiveActor, door, 1, false), Is.False);
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
