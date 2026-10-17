using System.Reflection;
using BlockMap.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace BlockMap.Tests
{
    public sealed class EmitterPushTests
    {
        GameObject _root;
        BlockWorld _world;
        BlockGameplayController _gameplay;
        SlimePartyController _party;
        readonly Vector3Int _from = new Vector3Int(2, 1, 2);

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("Emitter push test");
            _world = _root.AddComponent<BlockWorld>();
            int floor = BlockLibrary.IndexOf("Lab_WallBrick1");
            for (int x = 0; x < 6; x++)
                for (int z = 0; z < 6; z++) _world.Set(new Vector3Int(x, 0, z), floor);
            _gameplay = _root.AddComponent<BlockGameplayController>();
            _gameplay.Initialize(_world);
            _party = _root.AddComponent<SlimePartyController>();
            _party.Initialize(_gameplay, null, new Vector3Int(1, 1, 2), 1, 1f, 0f, true);
            _gameplay.BindParty(_party);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        void Refresh() => typeof(BlockGameplayController).GetMethod("Update",
            BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_gameplay, null);

        GameplayBlockView Arrange(string id, int direction)
        {
            _world.Set(_from, BlockLibrary.IndexOf(id));
            _world.SetDir(_from, direction);
            Refresh();
            return _root.GetComponentInChildren<GameplayBlockView>();
        }

        [TestCase("Lab_Laser_01", 0)]
        [TestCase("Lab_Laser_01", 1)]
        [TestCase("Lab_Laser_01", 2)]
        [TestCase("Lab_Laser_01", 3)]
        [TestCase("Lab_Laser_02", 0)]
        [TestCase("Lab_Laser_02", 1)]
        [TestCase("Lab_Laser_02", 2)]
        [TestCase("Lab_Laser_02", 3)]
        [TestCase("Lab_Laser_03", 0)]
        [TestCase("Lab_Laser_03", 1)]
        [TestCase("Lab_Laser_03", 2)]
        [TestCase("Lab_Laser_03", 3)]
        public void PushKeepsLiveViewAndSlidesWithBottomParticles(string id, int direction)
        {
            var view = Arrange(id, (direction + 1) & 3);
            Vector3 start = view.transform.position;
            Quaternion rotation = view.transform.rotation;
            Vector3Int step = GridDirection.ToVector(direction);
            Vector3Int to = _from + step;
            _party.ActiveActor.Mover.Teleport(_from - step, direction);
            _party.ActiveActor.Mover.stepDuration = .3f;

            Assert.IsTrue(_gameplay.CanEnter(_party.ActiveActor, _from, direction, true));
            Refresh();
            Assert.AreEqual(BlockLibrary.IndexOf(id), _world.GetIndex(to));
            Assert.AreEqual(to, view.Cell);
            Assert.AreSame(view, _root.GetComponentInChildren<GameplayBlockView>(), "A rebuild replaced the moving model.");
            Assert.AreEqual(start, view.transform.position, "The model teleported before animating.");
            Assert.AreEqual(rotation, view.transform.rotation);

            var animation = view.GetComponent<GameplayPushAnimation>();
            var particles = animation.GetComponentInChildren<ParticleSystem>();
            Assert.IsTrue(animation.IsMoving);
            Assert.IsTrue(particles.isEmitting);
            Assert.Greater(particles.particleCount, 0);
            Assert.AreEqual(ParticleSystemSimulationSpace.World, particles.main.simulationSpace);
            Assert.That(particles.transform.position.y - start.y, Is.InRange(.02f, .08f));
            Assert.Less(Vector3.Dot(particles.transform.position - start, step), -.4f,
                "Particles should emerge from the trailing bottom edge, outside the casing.");
            Assert.NotNull(particles.GetComponent<ParticleSystemRenderer>().sharedMaterial);
            var velocity = particles.velocityOverLifetime;
            Vector3 drift = new Vector3((velocity.x.constantMin + velocity.x.constantMax) * .5f, 0,
                (velocity.z.constantMin + velocity.z.constantMax) * .5f);
            Assert.Less(Vector3.Dot(drift, step), -1f, "Dust should trail opposite the push, regardless of emitter facing.");

            animation.AdvanceAnimation(.15f);
            Assert.Less(Vector3.Distance(start + (Vector3)step * .5f, view.transform.position), .001f);
            Assert.IsTrue(particles.isEmitting);
            _world.Set(new Vector3Int(5, 1, 5), BlockLibrary.IndexOf("Button_A"));
            Refresh();
            Assert.IsTrue(animation != null && animation.IsMoving, "An unrelated world change cancelled the push.");
            animation.AdvanceAnimation(.15f);
            Assert.IsFalse(animation.IsMoving);
            Assert.IsFalse(particles.isEmitting);
            Assert.Less(Vector3.Distance(start + (Vector3)step, view.transform.position), .001f);
            particles.Simulate(1f, true, false, true);
            Assert.AreEqual(0, particles.particleCount, "The trail did not expire after stopping.");
        }

        [Test]
        public void RepeatedPushReusesEffectAndResetRemovesIt()
        {
            var view = Arrange("Lab_Laser_01", 0);
            Assert.IsTrue(_gameplay.CanEnter(_party.ActiveActor, _from, 1, true));
            Refresh();
            var animation = view.GetComponent<GameplayPushAnimation>();
            animation.AdvanceAnimation(.16f);
            var firstParticles = view.GetComponentInChildren<ParticleSystem>();
            Assert.IsTrue(_gameplay.CanEnter(_party.ActiveActor, _from + Vector3Int.right, 1, true));
            Refresh();
            Assert.AreSame(firstParticles, view.GetComponentInChildren<ParticleSystem>());
            Assert.AreEqual(1, view.GetComponentsInChildren<ParticleSystem>().Length);
            Assert.IsTrue(animation.IsMoving);
            _gameplay.ResetState();
            Assert.IsTrue(view == null);
            Assert.IsNull(_root.GetComponentInChildren<GameplayPushAnimation>());
            var reset = _root.GetComponentInChildren<GameplayBlockView>();
            Assert.AreEqual(_from + Vector3Int.right * 2, reset.Cell);
            Assert.AreEqual(new Vector3(4.5f, 1, 2.5f), reset.transform.position);
        }

        [TestCase("Lab_Laser_01", true)]
        [TestCase("EmitterWhiteWall", false)]
        public void BlockedOrFixedEmitterDoesNotStartAnimation(string id, bool blockTarget)
        {
            var view = Arrange(id, 0);
            if (blockTarget)
            {
                _world.Set(_from + Vector3Int.right, BlockLibrary.IndexOf("Breakable"));
                Refresh();
            }
            Assert.IsFalse(_gameplay.CanEnter(_party.ActiveActor, _from, 1, true));
            Assert.AreEqual(_from, view.Cell);
            Assert.IsNull(view.GetComponent<GameplayPushAnimation>());
        }
    }
}
