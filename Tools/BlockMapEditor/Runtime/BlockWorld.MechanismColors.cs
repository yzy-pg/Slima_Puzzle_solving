using System.Collections.Generic;
using UnityEngine;

namespace BlockMap
{
    public partial class BlockWorld
    {
        readonly Dictionary<Vector3Int, string> _mechanismColors = new Dictionary<Vector3Int, string>();

        public string GetMechanismColorHex(Vector3Int cell) =>
            _mechanismColors.TryGetValue(cell, out var hex) ? hex : string.Empty;

        public bool TryGetMechanismColor(Vector3Int cell, out Color color) =>
            ColorUtility.TryParseHtmlString("#" + GetMechanismColorHex(cell), out color);

        public bool SetMechanismColor(Vector3Int cell, Color color) =>
            SetMechanismColorValue(cell, ColorUtility.ToHtmlStringRGB(color));

        public bool ClearMechanismColor(Vector3Int cell) => SetMechanismColorValue(cell, string.Empty);

        bool SetMechanismColorValue(Vector3Int cell, string hex)
        {
            if (!_blocks.ContainsKey(cell) || GetMechanismColorHex(cell) == hex) return false;
            int index = GetIndex(cell);
            RecordEdit(cell, index, index, GetLinkId(cell), GetLinkId(cell), afterColor: hex);
            SetMechanismColorRaw(cell, hex);
            NotifyChanged();
            return true;
        }

        void SetMechanismColorRaw(Vector3Int cell, string hex)
        {
            if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString("#" + hex, out var color))
                _mechanismColors[cell] = ColorUtility.ToHtmlStringRGB(color);
            else _mechanismColors.Remove(cell);
        }
    }
}
