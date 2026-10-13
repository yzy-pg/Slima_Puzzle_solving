using NUnit.Framework;
using UnityEngine;

namespace BlockMap.Gameplay.Tests
{
    public sealed class MechanismColorTests
    {
        GameObject _root;
        BlockWorld _world;
        readonly Vector3Int _a = new Vector3Int(1,1,1);
        readonly Vector3Int _b = new Vector3Int(2,1,1);
        readonly Vector3Int _door = new Vector3Int(3,1,1);

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("MechanismColorTests"); _world = _root.AddComponent<BlockWorld>();
            _world.Set(_a,BlockLibrary.IndexOf("Lab_PressurePlate"));
            _world.Set(_b,BlockLibrary.IndexOf("Button_A"));
            _world.Set(_door,BlockLibrary.IndexOf("Door"));
            foreach(var cell in new[] { _a,_b,_door }) _world.SetLinkId(cell,"G001");
        }

        [TearDown] public void TearDown() => Object.DestroyImmediate(_root);

        [Test]
        public void ColorsSurviveJsonAndSceneSerializationAndMoving()
        {
            _world.SetMechanismColor(_a,new Color32(23,117,241,255));
            string json = _world.ToJson(); _world.FromJson(json);
            Assert.That(_world.GetMechanismColorHex(_a),Is.EqualTo("1775F1"));
            _world.OnBeforeSerialize();
            var clone = Object.Instantiate(_root);
            try
            {
                var saved=clone.GetComponent<BlockWorld>(); saved.RestoreIfNeeded();
                Assert.That(saved.GetMechanismColorHex(_a),Is.EqualTo("1775F1"));
                var to=new Vector3Int(4,1,1); Assert.That(saved.Move(_a,to),Is.True);
                Assert.That(saved.GetMechanismColorHex(to),Is.EqualTo("1775F1"));
                Assert.That(saved.GetMechanismColorHex(_a),Is.Empty);
            }
            finally { Object.DestroyImmediate(clone); }
        }

        [Test]
        public void DoorPickerColorsLinkedSourcesInOneUndoableEdit()
        {
            BlockLinkVisuals.SetEditorColor(_world,_door,Color.green);
            foreach(var cell in new[] { _a,_b,_door }) Assert.That(_world.GetMechanismColorHex(cell),Is.EqualTo("00FF00"));
            _world.Undo();
            foreach(var cell in new[] { _a,_b,_door }) Assert.That(_world.GetMechanismColorHex(cell),Is.Empty);
            _world.Redo();
            foreach(var cell in new[] { _a,_b,_door }) Assert.That(_world.GetMechanismColorHex(cell),Is.EqualTo("00FF00"));
            BlockLinkVisuals.SetEditorColor(_world,_a,Color.red);
            Assert.That(_world.GetMechanismColorHex(_b),Is.EqualTo("00FF00"));
            _world.Undo(); Assert.That(_world.GetMechanismColorHex(_a),Is.EqualTo("00FF00"));
        }

        [Test]
        public void SavedColorsReachViewsAndDoorStripesAfterReload()
        {
            BlockLinkVisuals.SetEditorColor(_world,_a,new Color32(41,180,72,255));
            BlockLinkVisuals.SetEditorColor(_world,_b,Color.red);
            _world.FromJson(_world.ToJson());
            var controller=_root.AddComponent<BlockGameplayController>(); controller.Initialize(_world);
            var block=new MaterialPropertyBlock();
            foreach(var view in _root.GetComponentsInChildren<GameplayBlockView>())
            {
                var art=view.GetComponentInChildren<GameplayControlArt>();
                var renderer=art!=null ? art.colorParts[0] : view.GetComponentInChildren<Renderer>();
                renderer.GetPropertyBlock(block);
                Assert.That(block.GetFloat("_LinkColorCount"),Is.EqualTo(view.Cell==_door ? 2 : 1));
                if(view.Cell==_a) Assert.That(ColorUtility.ToHtmlStringRGB(block.GetColor("_BaseColor")),Is.EqualTo("29B448"));
                if(view.Cell==_b) Assert.That(block.GetColor("_BaseColor"),Is.EqualTo(Color.red));
            }
        }

        [Test]
        public void UnboundDoorAndPlateAcceptCustomColorAndReset()
        {
            _world.SetLinkId(_a,""); _world.SetLinkId(_door,"");
            BlockLinkVisuals.SetEditorColor(_world,_a,Color.blue);
            BlockLinkVisuals.SetEditorColor(_world,_door,Color.red);
            Assert.That(_world.GetMechanismColorHex(_a),Is.EqualTo("0000FF"));
            Assert.That(_world.GetMechanismColorHex(_door),Is.EqualTo("FF0000"));
            BlockLinkVisuals.SetEditorColor(_world,_a,null);
            Assert.That(_world.GetMechanismColorHex(_a),Is.Empty);
            _world.Undo(); Assert.That(_world.GetMechanismColorHex(_a),Is.EqualTo("0000FF"));
        }

        [Test]
        public void RemovingOrReplacingColoredBlockDoesNotLeakColorAndUndoRestoresIt()
        {
            _world.SetMechanismColor(_a,Color.red);
            _world.BeginStroke(); _world.Remove(_a); _world.EndStroke();
            Assert.That(_world.GetMechanismColorHex(_a),Is.Empty);
            _world.Undo(); Assert.That(_world.GetMechanismColorHex(_a),Is.EqualTo("FF0000"));
            _world.BeginStroke(); _world.Set(_a,BlockLibrary.IndexOf("Lab_Lever")); _world.EndStroke();
            Assert.That(_world.GetMechanismColorHex(_a),Is.Empty);
            _world.Undo(); Assert.That(_world.GetMechanismColorHex(_a),Is.EqualTo("FF0000"));
            _world.Redo(); Assert.That(_world.GetMechanismColorHex(_a),Is.Empty);
        }
    }
}
