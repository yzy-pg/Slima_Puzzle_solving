using System.Collections;
using System.Reflection;
using BlockMap.Gameplay;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace BlockMap.Gameplay.Tests
{
    public sealed class DestructibleBurnTests
    {
        GameObject _root;
        BlockWorld _world;
        BlockGameplayController _gameplay;
        SlimePartyController _party;
        readonly Vector3Int _cell = new Vector3Int(2, 1, 2);

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            if (!Application.isPlaying)
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                yield return new EnterPlayMode();
            }
            BlockGameplayCatalog.Reload(); BlockLibrary.Reload();
            _root = new GameObject("Package interaction test");
            _world = _root.AddComponent<BlockWorld>();
            for (int x = 0; x < 6; x++) for (int z = 0; z < 6; z++)
                _world.Set(new Vector3Int(x, 0, z), BlockLibrary.IndexOf("Lab_WallBrick1"));
            _gameplay = _root.AddComponent<BlockGameplayController>(); _gameplay.Initialize(_world);
            _party = _root.AddComponent<SlimePartyController>();
            _party.Initialize(_gameplay, Resources.Load<GameObject>("Player/BlockMapPlayer"), _cell, 0, 1, 0);
            _gameplay.BindParty(_party);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Time.timeScale = 1;
            Object.Destroy(_root);
            yield return null;
            if (Application.isPlaying) yield return new ExitPlayMode();
            BlockGameplayCatalog.Reload(); BlockLibrary.Reload();
        }

        [UnityTest]
        public IEnumerator RedAbilityBurnsTheRealPrefabInAllFourDirections()
        {
            for (int direction = 0; direction < 4; direction++)
            {
                Vector3Int target = _cell + GridDirection.ToVector(direction);
                _party.ActiveActor.Mover.Teleport(_cell, direction);
                _party.ActiveActor.SetState(new SlimeState { element = SlimeElement.Red });
                _world.Set(target, BlockLibrary.IndexOf("Breakable"));
                yield return null;
                Assert.IsTrue(_gameplay.TryGetDestructible(_party.ActiveActor, out var selected));
                Assert.AreEqual(target, selected);
                Assert.IsFalse(_gameplay.CanEnter(_party.ActiveActor, target, direction, false));
                _party.RequestAbility(); // Same path as the E key, with the real player and LateUpdate.
                yield return null; yield return null;
                Assert.AreEqual(-1, _world.GetIndex(target));
                Assert.IsTrue(_gameplay.CanEnter(_party.ActiveActor, target, direction, false));
                Assert.IsTrue(_party.ActiveActor.Mover.InputEnabled);
                var effect = _root.GetComponentInChildren<DestructibleBurnEffect>();
                Assert.IsNotNull(effect);
                var renderer = effect.GetComponentInChildren<MeshRenderer>();
                var properties = new MaterialPropertyBlock(); renderer.GetPropertyBlock(properties);
                Assert.AreEqual(1, properties.GetFloat("_BurnEnabled"));
                Assert.IsNotNull(properties.GetTexture("_BurnNoiseTex"));
                Assert.AreEqual(0, _root.GetComponentsInChildren<GameplayBlockView>().Length);
                effect.AdvanceAnimation(DestructibleBurnEffect.Duration * .5f);
                renderer.GetPropertyBlock(properties);
                Assert.Greater(properties.GetFloat("_BurnProgress"), .5f);
                // Shared prefab material must remain intact after an instance burns.
                Assert.AreEqual(0, renderer.sharedMaterial.GetFloat("_BurnEnabled"));
                effect.AdvanceAnimation(DestructibleBurnEffect.Duration + DestructibleBurnEffect.ParticleTail);
                yield return null;
                Assert.IsTrue(effect == null);
            }
        }

        [UnityTest]
        public IEnumerator BurningParticlesFollowTheEdgeFadeAndFinishTheirTail()
        {
            _party.ActiveActor.SetState(new SlimeState { element = SlimeElement.Red });
            _world.Set(_cell + Vector3Int.forward, BlockLibrary.IndexOf("Breakable"));
            yield return null;
            _party.RequestAbility(); yield return null; yield return null;
            var effect = _root.GetComponentInChildren<DestructibleBurnEffect>();
            var systems = effect.GetComponentsInChildren<ParticleSystem>();
            Assert.AreEqual(3, systems.Length);
            float initialY = systems[0].transform.position.y;
            foreach (var particles in systems)
            {
                Assert.IsTrue(particles.isPlaying);
                Assert.IsNotNull(particles.GetComponent<ParticleSystemRenderer>().sharedMaterial);
                Assert.IsTrue(particles.colorOverLifetime.enabled);
                Assert.AreEqual(ParticleSystemSimulationSpace.World, particles.main.simulationSpace);
                particles.Simulate(.2f, false, false, true);
                Assert.Greater(particles.particleCount, 0, "Particle emitter is invisible: " + particles.name);
            }
            effect.AdvanceAnimation(DestructibleBurnEffect.Duration * .5f);
            Assert.Less(systems[0].transform.position.y, initialY - .25f);
            Assert.AreEqual(systems[0].transform.position.y, systems[1].transform.position.y, .001f);
            effect.AdvanceAnimation(DestructibleBurnEffect.Duration * .5f);
            Assert.IsNotNull(effect, "Particle tail was cut off when the package disappeared.");
            Assert.IsFalse(effect.GetComponentInChildren<MeshRenderer>().enabled);
            foreach (var particles in systems) Assert.IsFalse(particles.isEmitting);
            effect.AdvanceAnimation(DestructibleBurnEffect.ParticleTail + .01f);
            yield return null;
            Assert.IsTrue(effect == null, "Finished particles were not cleaned up.");
        }

        [UnityTest]
        public IEnumerator EPressedDuringTheLastHopExecutesAfterLanding()
        {
            _party.ActiveActor.Mover.Teleport(_cell - Vector3Int.forward, 0);
            _party.ActiveActor.SetState(new SlimeState { element = SlimeElement.Red });
            Vector3Int target = _cell + Vector3Int.forward;
            _world.Set(target, BlockLibrary.IndexOf("Breakable")); yield return null;
            Assert.IsTrue(_party.ActiveActor.Mover.TryMoveTo(_cell));
            _party.RequestAbility();
            Assert.AreNotEqual(-1, _world.GetIndex(target), "Must wait for the landing cell.");
            // Finish the real mover's current step deterministically; the batch runner can advance
            // hundreds of test ticks before a single 0.16-second hop has elapsed.
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(SlimeGridMover).GetField("_progress", flags).SetValue(_party.ActiveActor.Mover, 1f);
            typeof(SlimeGridMover).GetMethod("AdvanceStep", flags).Invoke(_party.ActiveActor.Mover, null);
            yield return null; yield return null;
            Assert.AreEqual(_cell, _party.ActiveActor.Mover.Cell);
            Assert.AreEqual(-1, _world.GetIndex(target), "E was discarded during the hop.");
            Assert.IsTrue(_party.ActiveActor.Mover.InputEnabled);
            Assert.AreEqual(1, _root.GetComponentsInChildren<DestructibleBurnEffect>().Length);
            _party.RequestAbility(); yield return null; yield return null;
            Assert.AreEqual(1, _root.GetComponentsInChildren<DestructibleBurnEffect>().Length, "Repeated E duplicated the effect.");
        }

        [UnityTest]
        public IEnumerator OtherElementsCannotBurnAndResetClearsEffects()
        {
            Vector3Int target = _cell + Vector3Int.forward;
            _world.Set(target, BlockLibrary.IndexOf("Breakable")); yield return null;
            foreach (SlimeElement element in new[] { SlimeElement.Neutral, SlimeElement.Blue })
            {
                _party.ActiveActor.SetState(new SlimeState { element = element });
                _party.RequestAbility(); yield return null; yield return null;
                Assert.AreNotEqual(-1, _world.GetIndex(target));
                Assert.IsNull(_root.GetComponentInChildren<DestructibleBurnEffect>());
            }
            _party.ActiveActor.SetState(new SlimeState { element = SlimeElement.Red, form = SlimeForm.Gas });
            Assert.IsFalse(_gameplay.TryDestroyFacing(_party.ActiveActor));
            _party.ActiveActor.SetState(new SlimeState { element = SlimeElement.Red });
            _party.RequestAbility(); yield return null; yield return null;
            _world.Set(target, BlockLibrary.IndexOf("Breakable")); _gameplay.ResetState(); yield return null;
            Assert.IsNull(_root.GetComponentInChildren<DestructibleBurnEffect>());
            Assert.AreEqual(1, _root.GetComponentsInChildren<GameplayBlockView>().Length);
            Assert.IsFalse(_gameplay.CanEnter(_party.ActiveActor, target, 0, false));
        }
    }
}
