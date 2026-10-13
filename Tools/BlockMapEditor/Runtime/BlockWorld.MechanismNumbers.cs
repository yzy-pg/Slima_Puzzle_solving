using System.Collections.Generic;
using BlockMap.Gameplay;
using UnityEngine;

namespace BlockMap
{
    public partial class BlockWorld
    {
        readonly Dictionary<Vector3Int, int> _mechanismNumbers = new Dictionary<Vector3Int, int>();
        [SerializeField, HideInInspector] int _nextMechanismNumber = 1;

        public int GetMechanismNumber(Vector3Int cell) =>
            _mechanismNumbers.TryGetValue(cell, out int number) ? number : 0;

        /// <summary>为所有当前机关源分配连续编号；旧号仅用于稳定排序，删除后自动压紧编号。</summary>
        public void EnsureMechanismNumbers()
        {
            var cells = new List<Vector3Int>();
            foreach (Vector3Int cell in _blocks.Keys)
                if (IsMechanismSource(_blocks[cell])) cells.Add(cell);
            cells.Sort((first, second) =>
            {
                int byNumber = GetMechanismNumber(first).CompareTo(GetMechanismNumber(second));
                return byNumber != 0 ? byNumber : CompareCells(first, second);
            });
            _mechanismNumbers.Clear();
            for (int i = 0; i < cells.Count; i++) _mechanismNumbers[cells[i]] = i + 1;
            _nextMechanismNumber = cells.Count + 1;
        }

        int CreateMechanismNumber(int blockIndex) =>
            IsMechanismSource(blockIndex) ? _nextMechanismNumber++ : 0;

        static bool IsMechanismSource(int blockIndex)
        {
            BlockDefinition definition = BlockLibrary.Get(blockIndex);
            GameplayBlockProfile profile = definition != null ? BlockGameplayCatalog.Get(definition.id) : null;
            return profile != null && BlockLinkRules.IsSignalSource(profile.Kind);
        }

        void SetMechanismNumberRaw(Vector3Int cell, int number)
        {
            if (number <= 0) _mechanismNumbers.Remove(cell);
            else
            {
                _mechanismNumbers[cell] = number;
                _nextMechanismNumber = Mathf.Max(_nextMechanismNumber, number + 1);
            }
        }

        static int CompareCells(Vector3Int first, Vector3Int second)
        {
            int result = first.y.CompareTo(second.y);
            if (result != 0) return result;
            result = first.z.CompareTo(second.z);
            return result != 0 ? result : first.x.CompareTo(second.x);
        }
    }
}
