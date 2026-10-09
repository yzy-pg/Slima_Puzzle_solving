using UnityEngine;

namespace BlockMap.Gameplay
{
    public enum SlimeInteractionKind { Split, Lever, Fuse }

    /// <summary>只描述可执行的 F 交互；查询不会执行交互。</summary>
    public readonly struct SlimeInteraction
    {
        public readonly SlimeInteractionKind Kind;
        public readonly Vector3Int Cell;

        public SlimeInteraction(SlimeInteractionKind kind, Vector3Int cell)
        {
            Kind = kind;
            Cell = cell;
        }
    }
}
