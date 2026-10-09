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

        /// <summary>旧地图按固定坐标顺序补号；已有编号不会随删除或重排变化。</summary>
        public void EnsureMechanismNumbers()
        {
            var cells = new List<Vector3Int>(_blocks.Keys);
            cells.Sort(CompareCells);
            foreach (Vector3Int cell in cells)
            {
                if (GetMechanismNumber(cell) > 0 || !IsMechanismSource(_blocks[cell])) continue;
                SetMechanismNumberRaw(cell, _nextMechanismNumber++);
            }
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
