using System;
using UnityEngine;

namespace BlockMap.Gameplay
{
    public enum GameplayBlockKind
    {
        None,
        PressurePlate,
        Lever,
        Door,
        Splitter,
        RedFloor,
        BlueFloor,
        DamageFloor,
        Destructible,
        BeamEmitter,
        BeamReceiver,
        Button
    }

    public enum SlimeElement { Neutral, Red, Blue }
    public enum SlimeForm { Solid, Gas }
    public enum SlimeSize { Large, Small }
    public enum BeamColor { White, Red, Blue, Purple }
    public enum BlueOpticsMode { PassThrough, ReflectLeft, ReflectRight }

    [Serializable]
    public sealed class SlimeState
    {
        public SlimeElement element = SlimeElement.Neutral;
        public SlimeForm form = SlimeForm.Solid;
        public SlimeSize size = SlimeSize.Large;
        public BlueOpticsMode opticsMode = BlueOpticsMode.PassThrough;

        public int Weight => form == SlimeForm.Gas ? 0 : size == SlimeSize.Large ? 2 : 1;

        public SlimeState Copy()
        {
            return new SlimeState
            {
                element = element,
                form = form,
                size = size,
                opticsMode = opticsMode
            };
        }
    }

    [Serializable]
    public sealed class GameplayBlockProfile
    {
        public string id;
        public string displayName;
        public string kind;
        public string channel;
        public string color;
        public string mode;
        public string tint = "#FFFFFF";
        public string shape = "Cube";
        public int value;
        public bool blocksMovement;
        public bool pushable;
        public bool interactive = true;

        public GameplayBlockKind Kind => Enum.TryParse(kind, true, out GameplayBlockKind result)
            ? result
            : GameplayBlockKind.None;

        public BeamColor BeamColor => Enum.TryParse(color, true, out BeamColor result)
            ? result
            : BeamColor.White;

        public Color Tint => ColorUtility.TryParseHtmlString(tint, out Color result)
            ? result
            : UnityEngine.Color.white;

        public bool IsLatched => string.Equals(mode, "Latch", StringComparison.OrdinalIgnoreCase);
    }

    [Serializable]
    internal sealed class GameplayBlockCatalogData
    {
        public string _comment = string.Empty;
        public GameplayBlockProfile[] blocks = Array.Empty<GameplayBlockProfile>();
    }

    public static class GridDirection
    {
        public static Vector3Int ToVector(int direction)
        {
            switch (direction & 3)
            {
                case 0: return Vector3Int.forward;
                case 1: return Vector3Int.right;
                case 2: return Vector3Int.back;
                default: return Vector3Int.left;
            }
        }

        public static int Left(int direction) => (direction + 3) & 3;
        public static int Right(int direction) => (direction + 1) & 3;
        public static int Back(int direction) => (direction + 2) & 3;
    }

    public static class SlimeRules
    {
        public static SlimeState Fuse(SlimeState first, SlimeState second)
        {
            var result = new SlimeState { size = SlimeSize.Large };
            bool oppositeColors =
                (first.element == SlimeElement.Red && second.element == SlimeElement.Blue) ||
                (first.element == SlimeElement.Blue && second.element == SlimeElement.Red);

            if (oppositeColors)
            {
                result.form = SlimeForm.Gas;
                result.element = SlimeElement.Neutral;
            }
            else
            {
                result.form = first.form == SlimeForm.Gas || second.form == SlimeForm.Gas
                    ? SlimeForm.Gas
                    : SlimeForm.Solid;
                result.element = first.element != SlimeElement.Neutral ? first.element : second.element;
            }

            return result;
        }

        public static BeamColor MixBeam(BeamColor beam, SlimeElement slime)
        {
            if (slime == SlimeElement.Neutral) return beam;
            if (beam == BeamColor.Purple) return BeamColor.Purple;
            if (beam == BeamColor.White) return slime == SlimeElement.Red ? BeamColor.Red : BeamColor.Blue;
            if (beam == BeamColor.Red && slime == SlimeElement.Blue) return BeamColor.Purple;
            if (beam == BeamColor.Blue && slime == SlimeElement.Red) return BeamColor.Purple;
            return beam;
        }

        public static bool ReceiverAccepts(BeamColor required, BeamColor incoming) => required == incoming;
    }
}
