using UnityEngine;

namespace BlockMap.Gameplay
{
    /// <summary>只读取交互条件并绘制气泡，不处理输入或改变玩法状态。</summary>
    public sealed class BlockMapInteractionHud : MonoBehaviour
    {
        BlockMapGameRoot _root;
        SlimePartyController _party;
        BlockGameplayController _gameplay;

        public void Initialize(BlockMapGameRoot root, SlimePartyController party, BlockGameplayController gameplay)
        {
            _root = root;
            _party = party;
            _gameplay = gameplay;
        }

        void OnGUI()
        {
            if (_root == null || _root.Won || _party == null || _gameplay == null || _party.IsBusy) return;
            SlimeActor actor = _party.ActiveActor;
            if (actor == null) return;
            if (actor.State.form == SlimeForm.Gas)
            {
                DrawAboveActor(actor, "E", _gameplay.CanSolidify(actor));
                return;
            }
            if (_gameplay.TryGetDestructible(actor, out Vector3Int package))
            {
                DrawAboveDevice(package, "E");
                return;
            }
            if (!_gameplay.TryGetInteraction(actor, out SlimeInteraction interaction)) return;
            if (interaction.Kind == SlimeInteractionKind.Fuse) DrawAboveActor(actor, "F", true);
            else DrawAboveDevice(interaction.Cell);
        }

        static void DrawAboveActor(SlimeActor actor, string key, bool available)
        {
            Vector3 anchor = actor.OverlayAnchor + Vector3.up * BlockGameplaySettings.Current.activeMarkerHeight;
            if (!GameplayOverlayDrawing.TryProject(anchor, out Vector2 screen)) return;
            float size = Mathf.Max(24f, BlockGameplaySettings.Current.interactionBubbleSize);
            // 横向避开操作标记；气态高度取自模型，不会把气泡画在脚下。
            screen += new Vector2(size * 0.8f, -size * 0.2f);
            GameplayOverlayDrawing.DrawKeyBubble(screen, key, available);
        }

        static void DrawAboveDevice(Vector3Int cell, string key = "F")
        {
            Vector3 anchor = cell + new Vector3(0.5f, BlockGameplaySettings.Current.interactionBubbleHeight, 0.5f);
            if (GameplayOverlayDrawing.TryProject(anchor, out Vector2 screen))
                GameplayOverlayDrawing.DrawKeyBubble(screen, key, true);
        }
    }
}
