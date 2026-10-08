# 三渲二 · 风格化 Shader 使用说明

三个需求对应的实现：

| 需求 | 怎么实现的 | 在哪调 |
|---|---|---|
| **① 像素化画面** | 物体：顶点在裁剪空间吸附到虚拟像素网格（几何轮廓变"方"）<br>整屏：URP 的 Render Scale + Upscaling Filter = Point（真正降分辨率，推荐） | 物体 shader 的 `画面像素化 / PixelSnap`；整屏在 URP Asset |
| **② 黑色描边** | 方块地形 / 整场景：**全屏后处理**，对深度做 Sobel<br>角色 / 单体物件：**反转壳**描边（物体 shader 自带的 Outline Pass） | Feature 面板的 `Outline Color / Thickness / Depth Threshold` |
| **③ 五段阶梯光影 + 像素化噪声过渡** | `NdotL` 先用**像素化噪声**抖动，再量化成 `_Bands`（默认 **5**）段硬边 | `Bands=5`、`Noise Strength`、`Pixel Size` |

## 一、文件

| 文件 | 作用 |
|---|---|
| `Assets/_Game/Art/Shaders/SlimaToon.shader` | 物体卡通着色：五段阶梯光影 + 像素噪声过渡 + 顶点像素吸附 + 可选反转壳黑描边 |
| `Assets/_Game/Art/Shaders/SlimaToonOutlinePost.shader` | 全屏：像素化 + 深度 Sobel 黑描边 |
| `Assets/_Game/Scripts/Runtime/SlimaToonOutlineFeature.cs` | 上面那个全屏 shader 的 URP Renderer Feature |

## 二、装「整屏像素化 + 描边」

1. 选中实际在用的 Renderer 资产（本项目是 `Assets/Settings/URP-Balanced-Renderer.asset`）
2. Inspector 底部 **Add Renderer Feature** → **Slima Toon Outline**
3. 参数：
   - `Pixel Size`：像素块大小（默认 4，越大越糊；1 = 不像素化）
   - `Pixelate Amount`：0 = 只描边不做像素化
   - `Outline Color` / `Outline Thickness`（以像素块为单位）/ `Depth Threshold`（越小线越多，远处满屏线就调大）
4. 想要**真的低分辨率颗粒**：URP Asset 里 `Render Scale` 调到 0.25~0.5，`Upscaling Filter` 选 **Point**（和 shader 里的像素化叠加会更强）

## 三、给物体上卡通材质

1. 新建材质 → Shader 选 `Slima/Toon (Cel + Pixel)`
2. 关键参数：
   - `Bands`：**5**（就是五段阶梯；2~8 可调）
   - `Noise Strength`：段与段之间噪声"糊"的程度（0 = 纯硬边五段）
   - `Pixel Size`：噪声颗粒大小（按屏幕像素算，和 Render Scale 配套）
   - `Pixel Snap`：顶点像素吸附强度（0 = 关，越大越方）
   - `Shadow Color` / `Light Tint` / `Ambient Color`：决定五段的配色
   - `Outline Width`：反转壳描边粗细（**只给角色/单体物件用**）
3. 贴图的 Filter Mode 记得设 **Point**（像素风的关键）

## 四、注意事项（踩过的坑）

- **方块地形不要用物体 shader 的 Outline Pass**：chunk 是"每个面顶点独立"的合并网格，反转壳沿法线外扩会在每个方块棱上裂开 → 地形一律用全屏后处理描边。
- 反转壳的粗细按屏幕像素算，所以**远处的角色描边不会变细**。
- 贴图要关 Mipmap、用 Point，否则像素化会被模糊掉。
- 全屏描边依赖深度（Feature 内部已 `ConfigureInput(Depth)`）；默认插在 `BeforeRenderingPostProcessing`，如果有别的后处理需要调整顺序，改 Feature 面板的 `Pass Event`。
- 这套只针对 **URP**；如果换回 Built-in 需要重写。
- ⚠️ **ShaderLab 的 `[Header(...)]` 属性里不能写中文**：属性参数是按 token 解析的，非 ASCII 会让解析器报
  `Parse error: syntax error, unexpected $undefined, expecting TVAL_ID or TVAL_VARREF`。
  要分组就把分组名写进**带引号的显示名**，例如 `_Bands ("光影｜阶梯段数（5 = 五段）", Range(2, 8)) = 5`
  —— 引号内的中文是安全的，Inspector 里的分组观感一样（注释里的中文也没问题）。

## 五、还没做的

- 五段阶梯目前只按**主光**分段；额外光（点光/聚光）要支持的话，得每盏光各做一次量化再加权。
- 描边只有深度项、没有法线项 → 同一平面内的颜色变化不会出线。对方块场景来说这正好（棱上出线、面上干净）；如果要做"颜色描边"，再接 `_CameraNormalsTexture` 加 Sobel 项即可。
