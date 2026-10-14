using NUnit.Framework;
using UnityEngine;

namespace BlockMap.Gameplay.Tests
{
    public sealed class GameplayRulesTests
    {
        [TestCase(SlimeSize.Large, SlimeForm.Solid, 2)]
        [TestCase(SlimeSize.Small, SlimeForm.Solid, 1)]
        [TestCase(SlimeSize.Large, SlimeForm.Gas, 0)]
        [TestCase(SlimeSize.Small, SlimeForm.Gas, 0)]
        public void WeightMatchesFormAndSize(SlimeSize size, SlimeForm form, int expected)
        {
            var state = new SlimeState { size = size, form = form };
            Assert.That(state.Weight, Is.EqualTo(expected));
        }

        [Test]
        public void RedAndBlueSmallSlimesFuseIntoSolidNeutralSlime()
        {
            var red = new SlimeState { size = SlimeSize.Small, element = SlimeElement.Red };
            var blue = new SlimeState { size = SlimeSize.Small, element = SlimeElement.Blue };

            SlimeState result = SlimeRules.Fuse(red, blue);

            Assert.That(result.size, Is.EqualTo(SlimeSize.Large));
            Assert.That(result.form, Is.EqualTo(SlimeForm.Solid));
            Assert.That(result.element, Is.EqualTo(SlimeElement.Neutral));
        }

        [TestCase(BeamColor.White, SlimeElement.Neutral, BeamColor.White)]
        [TestCase(BeamColor.White, SlimeElement.Red, BeamColor.Red)]
        [TestCase(BeamColor.White, SlimeElement.Blue, BeamColor.Blue)]
        [TestCase(BeamColor.Red, SlimeElement.Blue, BeamColor.Purple)]
        [TestCase(BeamColor.Blue, SlimeElement.Red, BeamColor.Purple)]
        public void BeamMixingMatchesRules(BeamColor incoming, SlimeElement slime, BeamColor expected)
        {
            Assert.That(SlimeRules.MixBeam(incoming, slime), Is.EqualTo(expected));
        }

        [Test]
        public void LocalDirectionsUseLeftRightForwardBackOrder()
        {
            int facing = 1;
            Assert.That(GridDirection.ToVector(GridDirection.Left(facing)), Is.EqualTo(Vector3Int.forward));
            Assert.That(GridDirection.ToVector(GridDirection.Right(facing)), Is.EqualTo(Vector3Int.back));
            Assert.That(GridDirection.ToVector(facing), Is.EqualTo(Vector3Int.right));
            Assert.That(GridDirection.ToVector(GridDirection.Back(facing)), Is.EqualTo(Vector3Int.left));
        }

        [Test]
        public void GameplayCatalogLoadsConfiguredIds()
        {
            GameplayBlockProfile door = BlockGameplayCatalog.Get("Door");
            GameplayBlockProfile splitter = BlockGameplayCatalog.Get("Splitter");

            Assert.That(door, Is.Not.Null);
            Assert.That(door.Kind, Is.EqualTo(GameplayBlockKind.Door));
            Assert.That(door.channel, Is.EqualTo("A"));
            Assert.That(splitter, Is.Not.Null);
            Assert.That(splitter.Kind, Is.EqualTo(GameplayBlockKind.Splitter));
        }

        [Test]
        public void GlobalSettingsLoadFromJson()
        {
            BlockGameplaySettings.Reload();
            Assert.That(BlockGameplaySettings.Current.stepDuration, Is.GreaterThan(0f));
            Assert.That(BlockGameplaySettings.Current.beamRange, Is.GreaterThan(0));
        }

        [Test]
        public void InstanceLinkOverridesCatalogChannel()
        {
            var profile = new GameplayBlockProfile { channel = "A" };

            Assert.That(BlockLinkRules.ResolveChannel("G012", profile), Is.EqualTo("G012"));
            Assert.That(BlockLinkRules.ResolveChannel(string.Empty, profile), Is.EqualTo("A"));
            Assert.That(BlockLinkRules.ResolveChannel(string.Empty, profile, false), Is.Empty);
        }
    }
}
