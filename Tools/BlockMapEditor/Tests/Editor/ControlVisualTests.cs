using NUnit.Framework;
using UnityEngine;

namespace BlockMap.Gameplay.Tests
{
    public sealed class ControlVisualTests
    {
        GameObject _root;
        BlockWorld _world;
        BlockGameplayController _gameplay;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("ControlVisualTests");
            _world = _root.AddComponent<BlockWorld>();
            _gameplay = _root.AddComponent<BlockGameplayController>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        GameplayBlockView ViewAt(Vector3Int cell)
        {
            foreach (var view in _root.GetComponentsInChildren<GameplayBlockView>())
                if (view.Cell == cell) return view;
            Assert.Fail("Missing view at " + cell);
            return null;
        }

        MaterialPropertyBlock Properties(GameplayBlockView view)
        {
            var art = view.GetComponentInChildren<GameplayControlArt>();
            var renderer = art != null ? art.colorParts[0] : view.GetComponentInChildren<Renderer>();
            var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block); return block;
        }

        [Test]
        public void SourceColorsRemainDistinctAndDoorIncludesEverySource()
        {
            var a = new Vector3Int(1,1,1); var b = new Vector3Int(2,1,1); var door = new Vector3Int(3,1,1);
            _world.Set(a, BlockLibrary.IndexOf("Button_A"));
            _world.Set(b, BlockLibrary.IndexOf("Lab_PressurePlate"));
            _world.Set(door, BlockLibrary.IndexOf("Door"));
            foreach (var cell in new[] { a,b,door }) _world.SetLinkId(cell,"G001");
            _gameplay.Initialize(_world);
            _gameplay.SetMechanismColor(a,Color.green); _gameplay.SetMechanismColor(b,Color.red);
            Assert.That(Properties(ViewAt(a)).GetColor("_BaseColor"), Is.EqualTo(Color.green));
            Assert.That(Properties(ViewAt(b)).GetColor("_BaseColor"), Is.EqualTo(Color.red));
            Assert.That(Properties(ViewAt(door)).GetFloat("_LinkColorCount"), Is.EqualTo(2));
            ViewAt(a).SetActivated(true);
            Assert.That(Properties(ViewAt(a)).GetColor("_BaseColor"), Is.EqualTo(Color.green));
            _gameplay.ResetState();
            Assert.That(Properties(ViewAt(a)).GetColor("_BaseColor"), Is.EqualTo(Color.green));
        }

        [Test]
        public void ExplicitUnbindingRestoresNeutralColorAndRemovesDoorStripe()
        {
            var source = new Vector3Int(1,1,1); var door = new Vector3Int(2,1,1);
            _world.Set(source,BlockLibrary.IndexOf("Lab_Lever")); _world.Set(door,BlockLibrary.IndexOf("Door"));
            _world.SetLinkId(source,"G001"); _world.SetLinkId(door,"G001"); _gameplay.Initialize(_world);
            Assert.That(Properties(ViewAt(door)).GetFloat("_LinkColorCount"),Is.EqualTo(1));
            _world.SetLinkId(source,"");
            BlockLinkVisuals.Apply(_world,_root.GetComponentsInChildren<GameplayBlockView>());
            Assert.That(Properties(ViewAt(source)).GetFloat("_LinkColorCount"),Is.Zero);
            Assert.That(Properties(ViewAt(door)).GetFloat("_LinkColorCount"),Is.Zero);
        }

        [Test]
        public void ButtonQueryDoesNotPressAndInteractionPressesThenResetReleases()
        {
            var cell = new Vector3Int(2,1,1);
            _world.Set(new Vector3Int(1,0,1),BlockLibrary.IndexOf("Lab_Floor_01"));
            _world.Set(cell,BlockLibrary.IndexOf("Button_A"));
            _gameplay.Initialize(_world);
            var actor = new GameObject("Actor"); actor.transform.SetParent(_root.transform);
            var slime = actor.AddComponent<SlimeActor>();
            slime.Initialize(new SlimeState(),null,1f,0f);
            slime.Mover.Teleport(new Vector3Int(1,1,1),1);
            Assert.That(_gameplay.TryGetInteraction(slime,out var interaction),Is.True);
            Assert.That(interaction.Kind,Is.EqualTo(SlimeInteractionKind.Button));
            var art = ViewAt(cell).GetComponentInChildren<GameplayControlArt>();
            art.AdvanceAnimation(1f); Assert.That(art.movingPart.localScale.y,Is.EqualTo(1f));
            Assert.That(_gameplay.TryInteract(slime),Is.True);
            art.AdvanceAnimation(1f); Assert.That(art.movingPart.localScale.y,Is.EqualTo(art.pressedHeight));
            _gameplay.ResetState();
            art = ViewAt(cell).GetComponentInChildren<GameplayControlArt>(); art.AdvanceAnimation(1f);
            Assert.That(art.movingPart.localScale.y,Is.EqualTo(1f));
        }
    }
}
