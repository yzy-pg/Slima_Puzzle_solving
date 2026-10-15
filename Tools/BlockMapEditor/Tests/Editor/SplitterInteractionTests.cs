using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace BlockMap.Gameplay.Tests
{
    public sealed class SplitterInteractionTests
    {
        GameObject _root;
        BlockWorld _world;
        BlockGameplayController _gameplay;
        SlimePartyController _party;
        readonly Vector3Int _center = new Vector3Int(2, 1, 2);

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("SplitterInteractionTests");
            _world = _root.AddComponent<BlockWorld>();
            for (int x = 0; x < 6; x++)
                for (int z = 0; z < 6; z++)
                    _world.Set(new Vector3Int(x, 0, z), BlockLibrary.IndexOf("Lab_WallBrick1"));
            _world.Set(_center, BlockLibrary.IndexOf("Splitter"));
            _gameplay = _root.AddComponent<BlockGameplayController>();
            _gameplay.Initialize(_world);
            _party = _root.AddComponent<SlimePartyController>();
            _party.Initialize(_gameplay, null, _center + Vector3Int.back, 0, 1f, 0f);
            _gameplay.BindParty(_party);
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
            Object.DestroyImmediate(_root);
        }

        void Refresh() => typeof(BlockGameplayController)
            .GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_gameplay, null);

        void Finish() => _party.AdvanceSplitAnimation(_party.splitterCutDuration + _party.splitterJumpDuration + .01f);

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void OnlyRearEntryIsAllowedAndBothSlimesLandOnFrontDiagonals(int facing)
        {
            _world.SetDir(_center, facing);
            Refresh();
            Vector3Int front = GridDirection.ToVector(facing);
            Vector3Int right = GridDirection.ToVector(GridDirection.Right(facing));
            SlimeActor source = _party.ActiveActor;
            source.Mover.Teleport(_center - front, facing);
            Assert.That(_gameplay.CanEnter(source, _center, facing, false), Is.True);
            foreach (int direction in new[] { GridDirection.Back(facing), GridDirection.Left(facing), GridDirection.Right(facing) })
            {
                source.Mover.Teleport(_center - GridDirection.ToVector(direction), direction);
                Assert.That(_gameplay.CanEnter(source, _center, direction, false), Is.False);
                Assert.That(_party.CanSplit(source), Is.False);
            }
            source.Mover.Teleport(_center, GridDirection.Right(facing));
            Assert.That(_gameplay.CanEnter(source, _center + right, GridDirection.Right(facing), false), Is.False);
            source.Mover.Teleport(_center - front, facing);
            Assert.That(_party.CanSplit(source), Is.True);
            Assert.That(_gameplay.TryGetInteraction(source, out _), Is.False, "切割机不再提供 F 气泡。");
            Assert.That(_gameplay.TryInteract(source), Is.False, "F 不得启动切割。");
            Assert.That(_party.IsBusy, Is.False);
            Assert.That(_party.TryBuildRoute(source.Mover, facing, new List<Vector3Int>()), Is.False);
            Assert.That(_party.IsBusy, Is.True, "沿正确方向移动仍应启动切割。");
            Finish();
            Assert.That(source.Mover.Cell, Is.EqualTo(_center + front - right));
            Assert.That(_party.Actors[1].Mover.Cell, Is.EqualTo(_center + front + right));
            Assert.That(_party.ActiveActor, Is.SameAs(source));
            Assert.That(source.Mover.InputEnabled, Is.True);
            Assert.That(_party.Actors[1].Mover.InputEnabled, Is.False);
            Assert.That(_party.IsBusy, Is.False);
        }

        [Test]
        public void MovementStartsSlowCutParticlesAtQuarterAndSplitAtThreeQuartersThenHop()
        {
            SlimeActor source = _party.ActiveActor;
            Vector3 start = source.transform.position;
            Assert.That(_party.TryBuildRoute(source.Mover, 0, new List<Vector3Int>()), Is.False);
            Assert.That(_party.IsBusy, Is.True);
            Assert.That(source.Mover.TryMoveTo(_center + Vector3Int.left), Is.False);
            Assert.That(_gameplay.TryInteract(source), Is.False);
            _party.RequestAbility();
            _party.AdvanceSplitAnimation(_party.splitterCutDuration * .24f);
            ParticleSystem particles = _root.transform.Find("Splitter Slime Droplets").GetComponent<ParticleSystem>();
            Assert.That(particles.isPlaying, Is.False);
            Assert.That(source.transform.position.z, Is.GreaterThan(start.z));
            _party.AdvanceSplitAnimation(_party.splitterCutDuration * .02f);
            Assert.That(particles.isPlaying, Is.True);
            Assert.That(particles.particleCount, Is.GreaterThanOrEqualTo(28));
            _party.AdvanceSplitAnimation(_party.splitterCutDuration * .48f);
            Assert.That(_party.Actors.Count, Is.EqualTo(1));
            _party.AdvanceSplitAnimation(_party.splitterCutDuration * .02f);
            Assert.That(_party.Actors.Count, Is.EqualTo(2));
            Assert.That(source.State.size, Is.EqualTo(SlimeSize.Small));
            Assert.That(_party.Actors[1].Mover.InputEnabled, Is.False);
            _party.AdvanceSplitAnimation(_party.splitterCutDuration * .24f + _party.splitterJumpDuration * .5f);
            Assert.That(source.transform.position.y, Is.GreaterThan(_center.y + .3f));
            Assert.That(_party.IsBusy, Is.True);
            Finish();
            Assert.That(source.transform.position.y, Is.EqualTo(_center.y));
            Assert.That(_root.transform.Find("Splitter Slime Droplets"), Is.Null);
        }

        [TestCase(SlimeElement.Neutral)]
        [TestCase(SlimeElement.Red)]
        [TestCase(SlimeElement.Blue)]
        public void DropletsMatchBodyAndSplitPreservesElementAndOptics(SlimeElement element)
        {
            var source = _party.ActiveActor;
            source.SetState(new SlimeState { element = element, opticsMode = BlueOpticsMode.ReflectRight });
            Assert.That(_party.TrySplit(source), Is.True);
            var particles = _root.transform.Find("Splitter Slime Droplets").GetComponent<ParticleSystem>();
            Assert.That(particles.main.startColor.color, Is.EqualTo(source.BodyColor));
            Assert.That(particles.GetComponent<ParticleSystemRenderer>().sharedMaterial, Is.Not.Null);
            Finish();
            foreach (SlimeActor actor in _party.Actors)
            {
                Assert.That(actor.State.element, Is.EqualTo(element));
                Assert.That(actor.State.opticsMode, Is.EqualTo(BlueOpticsMode.ReflectRight));
            }
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void SplitStartsAtCenterAndAcceleratesTowardsBothEdges(int facing)
        {
            _world.SetDir(_center, facing);
            Refresh();
            _party.splitterCutDuration = 4f;
            SlimeActor source = _party.ActiveActor;
            Vector3Int front = GridDirection.ToVector(facing);
            Vector3 right = GridDirection.ToVector(GridDirection.Right(facing));
            Vector3 center = new Vector3(_center.x + .5f, _center.y, _center.z + .5f);
            source.Mover.Teleport(_center - front, facing);
            Assert.That(_party.TrySplit(source), Is.True);
            _party.AdvanceSplitAnimation(3f);
            SlimeActor second = _party.Actors[1];
            Assert.That(Vector3.Distance(source.transform.position, center), Is.LessThan(.001f));
            Assert.That(Vector3.Distance(second.transform.position, center), Is.LessThan(.001f));
            _party.AdvanceSplitAnimation(.25f);
            float firstDistance = Vector3.Distance(source.transform.position, center);
            Assert.That(firstDistance, Is.GreaterThan(0f));
            Assert.That(firstDistance, Is.LessThan(.03f));
            _party.AdvanceSplitAnimation(.25f);
            float secondDistance = Vector3.Distance(source.transform.position, center);
            Assert.That(secondDistance - firstDistance, Is.GreaterThan(firstDistance));
            Assert.That(Vector3.Distance(second.transform.position, center), Is.EqualTo(secondDistance).Within(.001f));
            _party.AdvanceSplitAnimation(.5f);
            Assert.That(Vector3.Distance(source.transform.position, center - right * .3f), Is.LessThan(.001f));
            Assert.That(Vector3.Distance(second.transform.position, center + right * .3f), Is.LessThan(.001f));
            Finish();
        }

        [Test]
        public void LargerLongerLivedDropletsSurviveLandingAndAreClearedOnRestart()
        {
            Assert.That(_party.TrySplit(_party.ActiveActor), Is.True);
            ParticleSystem particles = _root.transform.Find("Splitter Slime Droplets").GetComponent<ParticleSystem>();
            Assert.That(particles.main.startSize.constantMin, Is.GreaterThanOrEqualTo(.13f));
            Assert.That(particles.main.startSize.constantMax, Is.GreaterThanOrEqualTo(.24f));
            Assert.That(particles.main.startLifetime.constantMin, Is.GreaterThanOrEqualTo(1f));
            _party.AdvanceSplitAnimation(_party.splitterCutDuration * .8f);
            Finish();
            Assert.That(_party.IsBusy, Is.False);
            Assert.That(particles.gameObject.activeSelf, Is.True);
            Assert.That(particles.IsAlive(), Is.True);
            Assert.That(particles.isEmitting, Is.False);
            _party.RestartParty();
            Assert.That(_root.transform.Find("Splitter Slime Droplet Tail"), Is.Null);
        }

        [Test]
        public void BlockedDiagonalsFallBackFromFrontInClockwiseOrderWithoutPushing()
        {
            _world.Set(_center + Vector3Int.forward + Vector3Int.left, BlockLibrary.IndexOf("Breakable"));
            _world.Set(_center + Vector3Int.forward + Vector3Int.right, BlockLibrary.IndexOf("Lab_Laser_01"));
            Refresh();
            Assert.That(_party.TrySplit(_party.ActiveActor), Is.True);
            Finish();
            Assert.That(_party.Actors[0].Mover.Cell, Is.EqualTo(_center + Vector3Int.forward));
            Assert.That(_party.Actors[1].Mover.Cell, Is.EqualTo(_center + Vector3Int.right));
            Assert.That(_gameplay.GetProfileAt(_center + Vector3Int.forward + Vector3Int.right).Kind,
                Is.EqualTo(GameplayBlockKind.BeamEmitter));
        }

        void BlockRingExcept(Vector3Int? free)
        {
            for (int x = -1; x <= 1; x++)
                for (int z = -1; z <= 1; z++)
                {
                    Vector3Int cell = _center + new Vector3Int(x, 0, z);
                    if (cell == _center || cell == free) continue;
                    _world.Set(cell, BlockLibrary.IndexOf("Breakable"));
                }
            Refresh();
        }

        [Test]
        public void OneLandingLeavesSecondSlimeAtMachineAndNoLandingRefusesToStart()
        {
            BlockRingExcept(_center + Vector3Int.forward + Vector3Int.left);
            Assert.That(_party.TrySplit(_party.ActiveActor), Is.True);
            Finish();
            Assert.That(_party.Actors[1].Mover.Cell, Is.EqualTo(_center));
            _party.RestartParty();
            BlockRingExcept(null);
            Assert.That(_party.TrySplit(_party.ActiveActor), Is.False);
            Assert.That(_party.Actors.Count, Is.EqualTo(1));
            Assert.That(_party.ActiveActor.State.size, Is.EqualTo(SlimeSize.Large));
            Assert.That(_party.IsBusy, Is.False);
        }

        [Test]
        public void LostLandingsAbortBeforeChangingSize()
        {
            SlimeActor source = _party.ActiveActor;
            Vector3Int original = source.Mover.Cell;
            Assert.That(_party.TrySplit(source), Is.True);
            BlockRingExcept(null);
            _party.AdvanceSplitAnimation(_party.splitterCutDuration * .8f);
            Assert.That(_party.IsBusy, Is.False);
            Assert.That(source.Mover.Cell, Is.EqualTo(original));
            Assert.That(source.State.size, Is.EqualTo(SlimeSize.Large));
            Assert.That(source.Mover.InputEnabled, Is.True);
        }

        [Test]
        public void MissingSupportIsSkippedAndLandingAppliesElementFloor()
        {
            _world.Remove(_center + Vector3Int.forward + Vector3Int.left + Vector3Int.down);
            _world.Set(_center + Vector3Int.forward + Vector3Int.right, BlockLibrary.IndexOf("Lab_Heater_02"));
            Refresh();
            Assert.That(_party.TrySplit(_party.ActiveActor), Is.True);
            Finish();
            Assert.That(_party.Actors[0].Mover.Cell, Is.EqualTo(_center + Vector3Int.forward + Vector3Int.right));
            Assert.That(_party.Actors[0].State.element, Is.EqualTo(SlimeElement.Blue));
            Assert.That(_party.Actors[1].Mover.Cell, Is.EqualTo(_center + Vector3Int.forward));
        }

        [Test]
        public void BlueSlideStopsBehindSawAndNextMovementStartsTheCut()
        {
            SlimeActor source = _party.ActiveActor;
            source.SetState(new SlimeState { element = SlimeElement.Blue });
            source.Mover.Teleport(_center + Vector3Int.back * 2, 0);
            var route = new List<Vector3Int>();
            Assert.That(_party.TryBuildRoute(source.Mover, 0, route), Is.True);
            Assert.That(route, Is.EqualTo(new[] { _center + Vector3Int.back }));
            Assert.That(source.Mover.TryMoveTo(route[0]), Is.True);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(SlimeGridMover).GetField("_progress", flags).SetValue(source.Mover, 1f);
            typeof(SlimeGridMover).GetMethod("AdvanceStep", flags).Invoke(source.Mover, null);
            Assert.That(_party.TryBuildRoute(source.Mover, 0, route), Is.False);
            Assert.That(_party.IsBusy, Is.True);
            Finish();
            Assert.That(_party.Actors.Count, Is.EqualTo(2));
        }

        [Test]
        public void RestoreDuringCutClearsTransientSlimeAndParticles()
        {
            SlimeProgress[] saved = _party.CaptureProgress(out int active);
            Assert.That(_party.TrySplit(_party.ActiveActor), Is.True);
            _party.AdvanceSplitAnimation(_party.splitterCutDuration * .8f);
            _party.RestoreProgress(saved, active);
            Finish();
            Assert.That(_party.Actors.Count, Is.EqualTo(1));
            Assert.That(_party.ActiveActor.State.size, Is.EqualTo(SlimeSize.Large));
            Assert.That(_party.ActiveActor.Mover.Cell, Is.EqualTo(saved[0].cell));
            Assert.That(_party.IsBusy, Is.False);
            Assert.That(_root.transform.Find("Splitter Slime Droplets"), Is.Null);
        }

        [Test]
        public void PauseAndRestartDoNotLeaveAnimationOrExtraSlimes()
        {
            Assert.That(_party.TrySplit(_party.ActiveActor), Is.True);
            Vector3 start = _party.ActiveActor.transform.position;
            Time.timeScale = 0f;
            Finish();
            Assert.That(_party.ActiveActor.transform.position, Is.EqualTo(start));
            Assert.That(_party.Actors.Count, Is.EqualTo(1));
            Time.timeScale = 1f;
            _party.AdvanceSplitAnimation(_party.splitterCutDuration * .8f);
            _party.RestartParty();
            Assert.That(_party.Actors.Count, Is.EqualTo(1));
            Assert.That(_party.IsBusy, Is.False);
            Assert.That(_party.ActiveActor.Mover.InputEnabled, Is.True);
            Assert.That(_root.transform.Find("Splitter Slime Droplets"), Is.Null);
        }
    }
}
