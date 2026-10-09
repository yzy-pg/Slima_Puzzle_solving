using System.Collections.Generic;
using UnityEngine;

namespace BlockMap.Gameplay
{
    public readonly struct MechanismLabel
    {
        public readonly Vector3Int Cell;
        public readonly int[] Numbers;

        public MechanismLabel(Vector3Int cell, int[] numbers)
        {
            Cell = cell;
            Numbers = numbers;
        }
    }

    /// <summary>由现有关联规则生成编号列表；不参与门的触发判定。</summary>
    public sealed class BlockMechanismLabels
    {
        readonly List<MechanismLabel> _labels = new List<MechanismLabel>();
        readonly Dictionary<string, SortedSet<int>> _sources = new Dictionary<string, SortedSet<int>>();
        BlockWorld _world;
        int _version = -1;

        public IReadOnlyList<MechanismLabel> Labels => _labels;

        public void Refresh(BlockWorld world)
        {
            if (world == null) return;
            if (world == _world && world.Version == _version) return;
            _world = world;
            _version = world.Version;
            world.EnsureMechanismNumbers();
            _labels.Clear();
            _sources.Clear();
            CollectSources();
            CollectDoors();
        }

        void CollectSources()
        {
            foreach (KeyValuePair<Vector3Int, int> block in _world.Blocks)
            {
                GameplayBlockProfile profile = ProfileFor(block.Value);
                if (profile == null || !BlockLinkRules.IsSignalSource(profile.Kind)) continue;
                int number = _world.GetMechanismNumber(block.Key);
                _labels.Add(new MechanismLabel(block.Key, new[] { number }));
                string channel = ChannelFor(block.Key, profile);
                if (channel.Length == 0) continue;
                if (!_sources.TryGetValue(channel, out SortedSet<int> numbers))
                    _sources[channel] = numbers = new SortedSet<int>();
                numbers.Add(number);
            }
        }

        void CollectDoors()
        {
            foreach (KeyValuePair<Vector3Int, int> block in _world.Blocks)
            {
                GameplayBlockProfile profile = ProfileFor(block.Value);
                if (profile == null || !BlockLinkRules.IsSignalTarget(profile.Kind)) continue;
                string channel = ChannelFor(block.Key, profile);
                if (!_sources.TryGetValue(channel, out SortedSet<int> numbers)) continue;
                var values = new int[numbers.Count];
                numbers.CopyTo(values);
                _labels.Add(new MechanismLabel(block.Key, values));
            }
        }

        string ChannelFor(Vector3Int cell, GameplayBlockProfile profile) =>
            BlockLinkRules.ResolveChannel(_world.GetLinkId(cell), profile, !_world.HasLinkIds);

        static GameplayBlockProfile ProfileFor(int index)
        {
            BlockDefinition definition = BlockLibrary.Get(index);
            return definition != null ? BlockGameplayCatalog.Get(definition.id) : null;
        }
    }
}
