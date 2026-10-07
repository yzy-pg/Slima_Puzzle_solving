using NUnit.Framework;
using UnityEngine;

namespace BlockMap.Gameplay.Tests
{
    public sealed class BlockWorldLinkTests
    {
        GameObject _root;
        BlockWorld _world;

        [SetUp]
        public void SetUp()
        {
            Assert.That(BlockLibrary.Count, Is.GreaterThan(0));
            _root = new GameObject("BlockWorldLinkTests");
            _world = _root.AddComponent<BlockWorld>();
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
        }

        [Test]
        public void LinkIdSurvivesJsonRoundTrip()
        {
            var cell = new Vector3Int(2, 1, 3);
            _world.Set(cell, 0);
            _world.SetLinkId(cell, "G007");

            string json = _world.ToJson();
            _world.Clear();
            _world.FromJson(json);

            Assert.That(_world.GetLinkId(cell), Is.EqualTo("G007"));
        }

        [Test]
        public void MovingBlockPreservesItsLinkId()
        {
            var from = new Vector3Int(1, 1, 1);
            var to = new Vector3Int(2, 1, 1);
            _world.Set(from, 0);
            _world.SetLinkId(from, "G002");

            bool moved = _world.Move(from, to);

            Assert.That(moved, Is.True);
            Assert.That(_world.GetLinkId(from), Is.Empty);
            Assert.That(_world.GetLinkId(to), Is.EqualTo("G002"));
        }

        [Test]
        public void LinkAssignmentCanBeUndoneAndRedone()
        {
            var cell = new Vector3Int(4, 1, 4);
            _world.Set(cell, 0);

            _world.BeginStroke();
            _world.SetLinkId(cell, "G003");
            _world.EndStroke();
            _world.Undo();
            Assert.That(_world.GetLinkId(cell), Is.Empty);

            _world.Redo();
            Assert.That(_world.GetLinkId(cell), Is.EqualTo("G003"));
        }

        [Test]
        public void OldMapWithoutLinkIdStillLoads()
        {
            BlockDefinition definition = BlockLibrary.Get(0);
            string json = $"{{\"chunkSize\":16,\"blocks\":[{{\"x\":1,\"y\":0,\"z\":1,\"r\":0,\"id\":\"{definition.id}\"}}]}}";

            _world.FromJson(json);

            Assert.That(_world.GetIndex(new Vector3Int(1, 0, 1)), Is.EqualTo(0));
            Assert.That(_world.GetLinkId(new Vector3Int(1, 0, 1)), Is.Empty);
        }
    }
}
