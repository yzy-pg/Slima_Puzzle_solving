using UnityEngine;

namespace BlockMap
{
    /// <summary>
    /// 把「面板 + 边框」两张同尺寸贴图合成一张 8x8 = 64 格的图集，供自动包边使用。
    ///
    /// 索引规则与 Minecraft 的 Create 模组 AllCTTypes.OMNIDIRECTIONAL 一致：
    /// 每个面用 8 位上下文（4 个正交邻居 + 4 个斜角邻居）算出一个 0..63 的格子号。
    ///   · 某边邻居不同 → 画那条边
    ///   · 两正交边都相同但斜角不同（凹角）→ 补一个角点
    ///   · 四边都相同 → 纯面板（内部无缝）
    ///
    /// 边框贴图只需要"四条边条 + 四个角点"画对即可，中间画什么都不影响（不会被采样）。
    /// </summary>
    public static class AutoTileAtlas
    {
        public const int Sheet = 8;                  // 8x8 = 64 格
        public const int TileCount = Sheet * Sheet;
        public const int MaxIndex = TileCount - 1;

        /// <summary>面板贴图的边长就是单格像素数（16 → 128x128 图集；64 → 512x512 图集）。</summary>
        public static Texture2D Build(Texture2D panel, Texture2D frame)
        {
            int ts = Mathf.Min(panel.width, panel.height);
            int band = Mathf.Max(1, ts / 8);         // 16px 时 = 2px，与 Minecraft 习惯一致
            int size = ts * Sheet;

            var atlas = new Texture2D(size, size, TextureFormat.RGBA32, false, false)
            {
                name = "AutoTileAtlas",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                anisoLevel = 0
            };

            Color32[] panelPx = ReadPixels(panel, out var panelCopy);
            Color32[] framePx = ReadPixels(frame, out var frameCopy);
            int pw = panel.width, ph = panel.height;
            int fw = frame.width, fh = frame.height;
            var outPx = new Color32[size * size];

            ForEachTile((index, up, down, left, right, tl, tr, bl, br) =>
            {
                int tx = index % Sheet, ty = index / Sheet;
                int ox = tx * ts, oy = ty * ts;

                // 1) 先铺面板
                for (int y = 0; y < ts; y++)
                {
                    int sy = Mathf.Clamp(y * ph / ts, 0, ph - 1);
                    for (int x = 0; x < ts; x++)
                    {
                        int sx = Mathf.Clamp(x * pw / ts, 0, pw - 1);
                        outPx[(oy + y) * size + ox + x] = panelPx[sy * pw + sx];
                    }
                }

                // 2) 边带只画"中间段"：沿边方向两端各让出 band，源头也从 band 处开始取（= 往旁边偏移 8px）。
                //    这样只取到边框图每条边的"笔直段"，连续段里不会再重复冒出角块的弧形装饰。
                int inner = ts - band * 2;
                if (!up)
                {
                    CopyRegion(outPx, size, framePx, fw, fh, ox + band, oy + ts - band, band, fh - band, inner, band);
                    CopyRegion(outPx, size, framePx, fw, fh, ox, oy + ts - band, band, fh - band, band, band);
                    CopyRegion(outPx, size, framePx, fw, fh, ox + ts - band, oy + ts - band, band, fh - band, band, band);
                }
                if (!down)
                {
                    CopyRegion(outPx, size, framePx, fw, fh, ox + band, oy, band, 0, inner, band);
                    CopyRegion(outPx, size, framePx, fw, fh, ox, oy, band, 0, band, band);
                    CopyRegion(outPx, size, framePx, fw, fh, ox + ts - band, oy, band, 0, band, band);
                }
                if (!left)
                {
                    CopyRegion(outPx, size, framePx, fw, fh, ox, oy + band, 0, band, band, inner);
                    CopyRegion(outPx, size, framePx, fw, fh, ox, oy, 0, band, band, band);
                    CopyRegion(outPx, size, framePx, fw, fh, ox, oy + ts - band, 0, band, band, band);
                }
                if (!right)
                {
                    CopyRegion(outPx, size, framePx, fw, fh, ox + ts - band, oy + band, fw - band, band, band, inner);
                    CopyRegion(outPx, size, framePx, fw, fh, ox + ts - band, oy, fw - band, band, band, band);
                    CopyRegion(outPx, size, framePx, fw, fh, ox + ts - band, oy + ts - band, fw - band, band, band, band);
                }

                // 3) 真正的拐角（两条相邻边都缺）才补 8x8 角块，取边框图对应角落
                if (!up && !left)   CopyRegion(outPx, size, framePx, fw, fh, ox, oy + ts - band, 0, fh - band, band, band);
                if (!up && !right)  CopyRegion(outPx, size, framePx, fw, fh, ox + ts - band, oy + ts - band, fw - band, fh - band, band, band);
                if (!down && !left) CopyRegion(outPx, size, framePx, fw, fh, ox, oy, 0, 0, band, band);
                if (!down && !right)CopyRegion(outPx, size, framePx, fw, fh, ox + ts - band, oy, fw - band, 0, band, band);

                // 2) 该边不同就盖上边框条；两正交边相同但斜角不同就盖角点





                if (up && left && !tl) CopyRegion(outPx, size, framePx, fw, fh, ox, oy + ts - band, 0, fh - band, band, band);
                if (up && right && !tr) CopyRegion(outPx, size, framePx, fw, fh, ox + ts - band, oy + ts - band, fw - band, fh - band, band, band);
                if (down && left && !bl) CopyRegion(outPx, size, framePx, fw, fh, ox, oy, 0, 0, band, band);
                if (down && right && !br) CopyRegion(outPx, size, framePx, fw, fh, ox + ts - band, oy, fw - band, 0, band, band);
            });

            atlas.SetPixels32(outPx);
            atlas.Apply(false, false);
            DestroySafe(panelCopy);
            DestroySafe(frameCopy);
            return atlas;
        }

        /// <summary>
        /// 安全取像素：贴图在导入设置里没勾 "Read/Write Enabled" 时 GetPixels32 会抛异常，
        /// 这里用 RenderTexture 在 GPU 上拷一份可读的再读，所以用户丢进来的图不用改导入设置也能用。
        /// </summary>
        static Color32[] ReadPixels(Texture2D tex, out Texture2D copy)
        {
            copy = null;
            try { return tex.GetPixels32(); }
            catch { /* 不可读，走下面的 GPU 拷贝 */ }

            int w = tex.width, h = tex.height;
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            RenderTexture prev = RenderTexture.active;
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            copy = new Texture2D(w, h, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            copy.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return copy.GetPixels32();
        }

        static void DestroySafe(UnityEngine.Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(o);
            else UnityEngine.Object.DestroyImmediate(o);
        }

        static void CopyRegion(Color32[] dst, int dstSize, Color32[] src, int srcW, int srcH,
                               int dx, int dy, int sx, int sy, int w, int h)
        {
            for (int y = 0; y < h; y++)
            {
                int ty = sy + y;
                if (ty < 0 || ty >= srcH) continue;
                int dy2 = dy + y;
                if (dy2 < 0 || dy2 >= dstSize) continue;
                for (int x = 0; x < w; x++)
                {
                    int tx = sx + x;
                    if (tx < 0 || tx >= srcW) continue;
                    int dx2 = dx + x;
                    if (dx2 < 0 || dx2 >= dstSize) continue;
                    dst[dy2 * dstSize + dx2] = src[ty * srcW + tx];
                }
            }
        }

        /// <summary>枚举全部 47 个可达上下文（正交 16 种 × 该组合下有效的斜角组合）。</summary>
        public static void ForEachTile(System.Action<int, bool, bool, bool, bool, bool, bool, bool, bool> action)
        {
            for (int m = 0; m < 16; m++)
            {
                bool up = (m & 1) != 0, down = (m & 2) != 0, left = (m & 4) != 0, right = (m & 8) != 0;
                bool canTL = up && left, canTR = up && right, canBL = down && left, canBR = down && right;
                int count = (canTL ? 1 : 0) + (canTR ? 1 : 0) + (canBL ? 1 : 0) + (canBR ? 1 : 0);
                for (int c = 0; c < (1 << count); c++)
                {
                    int k = 0;
                    bool tl = false, tr = false, bl = false, br = false;
                    if (canTL) { tl = ((c >> k) & 1) == 1; k++; }
                    if (canTR) { tr = ((c >> k) & 1) == 1; k++; }
                    if (canBL) { bl = ((c >> k) & 1) == 1; k++; }
                    if (canBR) { br = ((c >> k) & 1) == 1; k++; }
                    action(Index(up, down, left, right, tl, tr, bl, br), up, down, left, right, tl, tr, bl, br);
                }
            }
        }

        /// <summary>
        /// 8 位上下文 → 图集格号。逐行照抄 Create 的 AllCTTypes.OMNIDIRECTIONAL，保证与 Build() 生成的图集一一对应。
        /// up/down/left/right = 该方向邻居是否相连；topLeft 等 = 斜角是否相连（只在两正交边都相连时有意义）。
        /// </summary>
        public static int Index(bool up, bool down, bool left, bool right,
                                bool topLeft, bool topRight, bool bottomLeft, bool bottomRight)
        {
            int tileX = 0, tileY = 0;
            int borders = (!up ? 1 : 0) + (!down ? 1 : 0) + (!left ? 1 : 0) + (!right ? 1 : 0);

            if (up) tileX++;
            if (down) tileX += 2;
            if (left) tileY++;
            if (right) tileY += 2;

            if (borders == 0)
            {
                if (topRight) tileX++;
                if (topLeft) tileX += 2;
                if (bottomRight) tileY += 2;
                if (bottomLeft) tileY++;
            }

            if (borders == 1)
            {
                if (!right && (topLeft || bottomLeft)) { tileY = 4; tileX = -1 + (bottomLeft ? 1 : 0) + (topLeft ? 1 : 0) * 2; }
                if (!left && (topRight || bottomRight)) { tileY = 5; tileX = -1 + (bottomRight ? 1 : 0) + (topRight ? 1 : 0) * 2; }
                if (!down && (topLeft || topRight)) { tileY = 6; tileX = -1 + (topLeft ? 1 : 0) + (topRight ? 1 : 0) * 2; }
                if (!up && (bottomLeft || bottomRight)) { tileY = 7; tileX = -1 + (bottomLeft ? 1 : 0) + (bottomRight ? 1 : 0) * 2; }
            }

            if (borders == 2 && ((up && left && topLeft) || (down && left && bottomLeft)
                              || (up && right && topRight) || (down && right && bottomRight)))
            {
                tileX += 3;
            }

            return Mathf.Clamp(tileX + Sheet * tileY, 0, MaxIndex);
        }

        /// <summary>某格在图集里的 UV 区域（已内缩半像素，防止相邻格渗色）。</summary>
        public static Rect TileRect(int index, int tileSize)
        {
            int tx = index % Sheet, ty = index / Sheet;
            float texel = 1f / (Sheet * tileSize);
            float u0 = (float)tx / Sheet + texel * 0.5f;
            float v0 = (float)ty / Sheet + texel * 0.5f;
            float u1 = (float)(tx + 1) / Sheet - texel * 0.5f;
            float v1 = (float)(ty + 1) / Sheet - texel * 0.5f;
            return Rect.MinMaxRect(u0, v0, u1, v1);
        }
    }
}
