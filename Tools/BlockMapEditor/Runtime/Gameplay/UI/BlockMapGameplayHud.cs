using UnityEngine;

namespace BlockMap.Gameplay
{
    /// <summary>原型阶段局内 HUD；只显示状态，不参与玩法判定。</summary>
    public sealed class BlockMapGameplayHud : MonoBehaviour
    {
        BlockMapGameRoot _root;
        SlimePartyController _party;

        public void Initialize(BlockMapGameRoot root, SlimePartyController party)
        {
            _root = root;
            _party = party;
        }

        void OnGUI()
        {
            if (_root == null) return;
            DrawActiveMarker();
            if (!string.IsNullOrEmpty(_root.Hint))
            {
                GUI.Box(new Rect(Screen.width * 0.5f - 260f, 20f, 520f, 48f), GUIContent.none);
                GUI.Label(new Rect(Screen.width * 0.5f - 245f, 34f, 500f, 24f), _root.Hint);
                return;
            }

            GUI.Box(new Rect(16f, 16f, 330f, 118f), GUIContent.none);
            GUI.Label(new Rect(30f, 24f, 300f, 22f), "WASD / 方向键：移动　R：重开");
            GUI.Label(new Rect(30f, 47f, 300f, 22f), "E：能力 / 形态　F：交互　Tab：切换");
            GUI.Label(new Rect(30f, 70f, 300f, 22f), ActiveStateText());
            GUI.Label(new Rect(30f, 93f, 300f, 22f), $"史莱姆数量：{(_party != null ? _party.Actors.Count : 0)} / 2");

            if (!_root.Won) return;

            var style = new GUIStyle(GUI.skin.box) { fontSize = 28, alignment = TextAnchor.MiddleCenter };
            GUI.Box(new Rect(Screen.width * 0.5f - 220f, Screen.height * 0.5f - 64f, 440f, 128f),
                "到达终点，关卡完成\n按 R 重新开始", style);
        }

        string ActiveStateText()
        {
            SlimeActor actor = _party != null ? _party.ActiveActor : null;
            if (actor == null) return "当前：无";
            SlimeState state = actor.State;
            string optics = state.element == SlimeElement.Blue ? $" / 光学：{state.opticsMode}" : string.Empty;
            return $"当前：{state.size} / {state.element} / {state.form}{optics}";
        }

        void DrawActiveMarker()
        {
            SlimeActor actor = _party != null ? _party.ActiveActor : null;
            if (actor == null) return;
            Vector3 anchor = actor.OverlayAnchor + Vector3.up * BlockGameplaySettings.Current.activeMarkerHeight;
            if (!GameplayOverlayDrawing.TryProject(anchor, out Vector2 screen)) return;
            GameplayOverlayDrawing.DrawActiveTriangle(screen);
        }
    }
}
