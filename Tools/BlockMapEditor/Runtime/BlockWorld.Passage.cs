using System.Collections.Generic;
using UnityEngine;

namespace BlockMap
{
    public partial class BlockWorld
    {
        // 运行时推动物可以占据开门所在格。保留被覆盖的门，离开时恢复其地图数据。
        readonly Dictionary<Vector3Int, SaveEntry> _coveredBlocks = new Dictionary<Vector3Int, SaveEntry>();
        [SerializeField, HideInInspector] List<SaveEntry> _serializedCoveredBlocks = new List<SaveEntry>();

        public IEnumerable<SaveEntry> CoveredBlocks => _coveredBlocks.Values;

        public bool TryGetCoveredBlock(Vector3Int cell, out SaveEntry block) =>
            _coveredBlocks.TryGetValue(cell, out block);

        public string GetLinkId(Vector3Int cell, string blockId) =>
            _coveredBlocks.TryGetValue(cell, out SaveEntry covered) && covered.id == blockId
                ? covered.linkId : GetLinkId(cell);

        public bool TryGetMechanismColor(Vector3Int cell, string blockId, out Color color)
        {
            if (_coveredBlocks.TryGetValue(cell, out SaveEntry covered) && covered.id == blockId)
                return ColorUtility.TryParseHtmlString("#" + covered.mechanismColor, out color);
            return TryGetMechanismColor(cell, out color);
        }

        bool CoveredBlocksHaveLinks()
        {
            foreach (SaveEntry block in _coveredBlocks.Values)
                if (!string.IsNullOrWhiteSpace(block.linkId)) return true;
            return false;
        }

        SaveEntry CaptureBlock(Vector3Int cell, int index)
        {
            return new SaveEntry
            {
                x = cell.x, y = cell.y, z = cell.z,
                id = BlockLibrary.Get(index).id,
                r = GetDir(cell),
                linkId = GetLinkId(cell),
                mechanismNumber = GetMechanismNumber(cell),
                mechanismColor = GetMechanismColorHex(cell)
            };
        }

        void RemoveBlockRaw(Vector3Int cell)
        {
            _blocks.Remove(cell);
            _dir.Remove(cell);
            _linkIds.Remove(cell);
            _mechanismNumbers.Remove(cell);
            _mechanismColors.Remove(cell);
        }

        void ApplyBlockRaw(Vector3Int cell, SaveEntry entry)
        {
            _blocks[cell] = BlockLibrary.IndexOf(entry.id);
            if ((entry.r & 3) != 0) _dir[cell] = (byte)(entry.r & 3);
            SetLinkIdRaw(cell, entry.linkId);
            SetMechanismNumberRaw(cell, entry.mechanismNumber);
            SetMechanismColorRaw(cell, entry.mechanismColor);
        }

        void RevealCoveredBlock(Vector3Int cell)
        {
            if (!_coveredBlocks.TryGetValue(cell, out SaveEntry entry)) return;
            ApplyBlockRaw(cell, entry);
            _coveredBlocks.Remove(cell);
        }

        void RestoreCoveredBlocks(IEnumerable<SaveEntry> entries)
        {
            _coveredBlocks.Clear();
            if (entries == null) return;
            foreach (SaveEntry entry in entries)
            {
                if (entry == null || BlockLibrary.IndexOf(entry.id) < 0) continue;
                var cell = new Vector3Int(entry.x, entry.y, entry.z);
                if (InBounds(cell) && _blocks.ContainsKey(cell)) _coveredBlocks[cell] = entry;
            }
        }
    }
}
