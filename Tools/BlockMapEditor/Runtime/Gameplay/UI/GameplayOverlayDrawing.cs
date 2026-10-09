using System.Collections.Generic;
using UnityEngine;

namespace BlockMap.Gameplay
{
    /// <summary>原型 IMGUI 图形绘制，与角色能力和机关判定无关。</summary>
    public static class GameplayOverlayDrawing
    {
        static Texture2D _triangle;
        static Texture2D _bubble;
        static GUIStyle _keyStyle;
        static GUIStyle _numberStyle;

        public static bool TryProject(Vector3 world, out Vector2 screen)
        {
            screen = default;
            Camera camera = Camera.main;
            if (camera == null) return false;
            Vector3 projected = camera.WorldToScreenPoint(world);
            if (projected.z <= 0f) return false;
            screen = new Vector2(projected.x, Screen.height - projected.y);
            return true;
        }

        public static void DrawActiveTriangle(Vector2 center)
        {
            EnsureTextures();
            DrawTinted(new Rect(center.x - 13f, center.y - 22f, 26f, 22f), _triangle, Color.black);
            DrawTinted(new Rect(center.x - 10f, center.y - 21f, 20f, 17f), _triangle, new Color(1f, 0.9f, 0.3f));
        }

        public static void DrawKeyBubble(Vector2 center, string key, bool available)
        {
            EnsureTextures();
            float size = Mathf.Max(24f, BlockGameplaySettings.Current.interactionBubbleSize);
            var rect = new Rect(center.x - size * 0.5f, center.y - size, size, size);
            Color background = available ? new Color(1f, 0.97f, 0.85f) : new Color(0.28f, 0.30f, 0.34f);
            Color foreground = available ? new Color(0.10f, 0.12f, 0.16f) : new Color(0.58f, 0.60f, 0.64f);
            DrawTinted(new Rect(rect.x - 2f, rect.y - 2f, size + 4f, size + 4f), _bubble, Color.black);
            DrawTinted(rect, _bubble, background);
            if (_keyStyle == null) _keyStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            _keyStyle.fontSize = Mathf.RoundToInt(size * 0.56f);
            _keyStyle.normal.textColor = foreground;
            GUI.Label(new Rect(rect.x, rect.y, size, size * 0.83f), key, _keyStyle);
            if (!available) DrawSlash(rect, foreground);
        }

        public static void DrawMechanismLabels(IReadOnlyList<MechanismLabel> labels)
        {
            if (_numberStyle == null)
                _numberStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 15 };
            _numberStyle.normal.textColor = Color.white;
            foreach (MechanismLabel label in labels)
            {
                Vector3 anchor = label.Cell + new Vector3(0.5f, BlockGameplaySettings.Current.mechanismLabelHeight, 0.5f);
                if (!TryProject(anchor, out Vector2 center)) continue;
                DrawNumberRow(center, label.Numbers);
            }
        }

        static void DrawNumberRow(Vector2 center, int[] numbers)
        {
            if (numbers.Length == 0) return;
            float width = 28f;
            foreach (int number in numbers) width = Mathf.Max(width, _numberStyle.CalcSize(new GUIContent(number.ToString())).x + 12f);
            float gap = Mathf.Max(0f, BlockGameplaySettings.Current.mechanismLabelGap);
            float total = numbers.Length * width + (numbers.Length - 1) * gap;
            for (int i = 0; i < numbers.Length; i++)
            {
                var rect = new Rect(center.x - total * 0.5f + i * (width + gap), center.y - 24f, width, 24f);
                DrawTinted(rect, Texture2D.whiteTexture, new Color(0.07f, 0.09f, 0.12f, 0.92f));
                GUI.Label(rect, numbers[i].ToString(), _numberStyle);
            }
        }

        static void DrawSlash(Rect rect, Color color)
        {
            Matrix4x4 previous = GUI.matrix;
            Vector2 center = new Vector2(rect.center.x, rect.y + rect.height * 0.42f);
            GUIUtility.RotateAroundPivot(-45f, center);
            DrawTinted(new Rect(center.x - rect.width * 0.39f, center.y - 1.5f, rect.width * 0.78f, 3f), Texture2D.whiteTexture, color);
            GUI.matrix = previous;
        }

        static void DrawTinted(Rect rect, Texture texture, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, texture);
            GUI.color = previous;
        }

        static void EnsureTextures()
        {
            if (_triangle == null) _triangle = CreateMask(false);
            if (_bubble == null) _bubble = CreateMask(true);
        }

        static Texture2D CreateMask(bool bubble)
        {
            const int size = 32;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = bubble ? "InteractionBubble" : "ActiveSlimeTriangle",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Abs(x - 15.5f);
                    bool filled = bubble
                        ? IsInsideBubble(dx, y)
                        : dx <= y * 0.5f;
                    texture.SetPixel(x, y, filled ? Color.white : Color.clear);
                }
            texture.Apply(false, true);
            return texture;
        }

        static bool IsInsideBubble(float dx, int y)
        {
            if (y < 6) return dx <= y * 0.65f;
            float cornerX = Mathf.Max(0f, dx - 9.5f);
            float cornerY = Mathf.Max(0f, Mathf.Abs(y - 18.5f) - 6.5f);
            return cornerX * cornerX + cornerY * cornerY <= 36f;
        }
    }
}
