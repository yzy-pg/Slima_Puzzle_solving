using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BlockMap.Gameplay.Tests
{
    public sealed class MechanismNumberTests
    {
        GameObject _root;
        BlockWorld _world;

        [SetUp]
        public void SetUp()
        {
            _root = new GameObject("MechanismNumberTests");
            _world = _root.AddComponent<BlockWorld>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_root);

        void AddLever(Vector3Int cell, string group)
        {
            _world.Set(cell, BlockLibrary.IndexOf("Lever_B"));
            _world.SetLinkId(cell, group);
        }

        [Test]
        public void DoorDisplaysDistinctNumbersOfAllItsSources()
        {
            AddLever(new Vector3Int(1, 1, 1), "G001");
            AddLever(new Vector3Int(2, 1, 1), "G001");
            var door = new Vector3Int(3, 1, 1);
            _world.Set(door, BlockLibrary.IndexOf("Door"));
            _world.SetLinkId(door, "G001");
            var labels = new BlockMechanismLabels();
            labels.Refresh(_world);
            var label = labels.Labels.Single(item => item.Cell == door);
            Assert.That(label.Numbers, Is.EqualTo(new[] { 1, 2 }));
        }

        [Test]
        public void NumbersCompactAfterRemovalWhileMoveAndSavePreserveLinks()
        {
            var first = new Vector3Int(1, 1, 1);
            var second = new Vector3Int(2, 1, 1);
            var moved = new Vector3Int(4, 1, 1);
            AddLever(first, "G001");
            AddLever(second, "G001");
            Assert.That(_world.GetMechanismNumber(second), Is.EqualTo(2));
            _world.Remove(first);
            Assert.That(_world.Move(second, moved), Is.True);
            string json = _world.ToJson();
            _world.FromJson(json);
            Assert.That(_world.GetMechanismNumber(moved), Is.EqualTo(1));
            Assert.That(_world.GetLinkId(moved), Is.EqualTo("G001"));
            AddLever(new Vector3Int(5, 1, 1), "G001");
            _world.EnsureMechanismNumbers();
            Assert.That(_world.GetMechanismNumber(moved), Is.EqualTo(1));
            Assert.That(_world.GetMechanismNumber(new Vector3Int(5, 1, 1)), Is.EqualTo(2));
        }

        [Test]
        public void DeletingAndUndoingRestoresTheSameNumber()
        {
            var cell = new Vector3Int(1, 1, 1);
            AddLever(cell, "G001");
            int number = _world.GetMechanismNumber(cell);
            _world.BeginStroke();
            _world.Remove(cell);
            _world.EndStroke();
            _world.Undo();
            Assert.That(_world.GetMechanismNumber(cell), Is.EqualTo(number));
        }

        [Test]
        public void OldMapsReceiveNumbersWhenLoaded()
        {
            _world.FromJson("{\"blocks\":[{\"x\":1,\"y\":1,\"z\":1,\"id\":\"Lever_B\"}]}");
            Assert.That(_world.GetMechanismNumber(new Vector3Int(1, 1, 1)), Is.GreaterThan(0));
        }
    }
}
