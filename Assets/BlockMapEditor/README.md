# BlockMapEditor —— 方块地图编辑器

一格 = 1m×1m×1m，世界上限 **256 × 256 × 36 格**，正交俯视机位。
贴图由 `面板 + 边框` 在运行时合成图集，实现**自动包边**（机壳式：只有整片结构的外沿才出现边框，凹角自动补点）。

---

## 一、三步上手

1. 把整个 **`BlockMapEditor/`** 文件夹拷进 Unity 工程的 `Assets/` 下
2. 菜单 **`Tools / BlockMap / ① 创建地图编辑场景`**
3. **Play** → 左侧面板开始刷地图

| 操作 | 按键 |
|---|---|
| 放置 / 擦除 | **左键** / **Shift+左键**（或勾选面板的「橡皮擦」后左键）|
| 换方块 | `[` `]`（或点面板方块库）|
| 笔刷大小 | `-` `=`（1³ / 3³ / 5³）|
| 撤销 / 重做 | `Ctrl+Z` / `Ctrl+Y`（一次"按下-松开"= 一步）|
| 高度锁 | 面板勾选后只在指定层编辑（笔刷也压成单层）|
| 相机 | 滚轮缩放 / 中键拖拽平移 / 右键旋转（关掉「固定机位」时）/ `1~4` 视角预设 |
| 保存 / 读取 | `F5` / `F9`（写到 `%USERPROFILE%\AppData\LocalLow\<公司>\<产品>\Maps\<槽名>.json`）|
| 进出编辑模式 | `Tab` |

---

## 二、资产怎么放

贴图放在 **`Resources` 下的任意子目录**（推荐每个方块一个文件夹），文件名必须按约定：

```
Assets/<任意路径>/Resources/<id>/<id>_panel.png     ← 面板（内部）
Assets/<任意路径>/Resources/<id>/<id>_frame.png     ← 边框（中间必须透明）
或
Assets/<任意路径>/Resources/<id>/<id>_simple.png    ← 单图方块（不包边）
```

| 项 | 规则 |
|---|---|
| **id** | 取自**文件名**（不是文件夹名！Resources API 拿不到子目录路径）。例：`Lab_WallBrick1_panel.png` → id = `Lab_WallBrick1` |
| **边框图** | 只画四条边 + 四角，**中间透明**；边条宽度 = **尺寸 ÷ 8**（64×64 → 8px；16×16 → 2px）|
| **面板图** | **必须能无缝平铺**；硬边像素（不要抗锯齿/渐变）|
| **尺寸** | 两张一致；图集按面板尺寸生成（64 → 512×512）|
| **配对** | 自动包边要求 `_panel` 与 `_frame` **同 id 成对**；只有 `_panel` 会被跳过并打印原因 |
| 生效 | 放好后重新 Play，方块库自动出现（也可以 `Create → BlockMap → Block Definition` 建 `.asset` 精细控制）|

---

## 三、交互道具 vs 静态方块

`BlockDefinition` 上有 **`interactive`** 勾选（勾上 = 门/箱子/机关这类可交互物件）：

- 方块库面板会**分两栏**显示：`── 非交互 / 静态墙面 ──` 与 `── 交互道具（不烘进静态几何）──`
- 菜单 **②** 烘焙静态几何时会**自动跳过交互道具** → 不会被融并进静态资产
- 交互道具仍完整记录在 json / `BlockWorld` 里 → 游戏运行时读这些格子坐标，自行实例化你的交互物件

---

## 四、出成品（持久化）

菜单 **`Tools / BlockMap / ② 读取存档并在当前场景搭建（持久化）`**：

- 在**干净场景**里 → 烘成真实 GameObject（每个 chunk 一个 MeshFilter+MeshRenderer），材质与图集落成 `Assets/BlockMapEditor/Built/` 下的工程资产 → **保存场景后永久存在**
- 在**带编辑器的场景**里 → 只把数据灌进 `BlockWorld`（可继续编辑），不烘静态几何

---

## 五、发版保护

`BlockMapBootstrap` 上的 **`Editor Only In Development Build`**（默认 ☑）：

- Unity Editor / **Development Build**：全功能
- **正式包**：自动关闭面板、鼠标操作、网格与虚影 → 玩家看不到任何编辑器痕迹

更彻底的做法：玩家场景里根本不放编辑器（用菜单 ② 烘出纯几何的场景），并且 Build Settings 里不勾 `BlockMapEditor.unity`。

---

## 六、兼容性

| 项 | 要求 |
|---|---|
| Unity | **2019.4 LTS ~ 2022.3 LTS**（编写环境 2022.3 / 团结引擎 1.10.x）；Unity 6 可用但需 `Active Input Handling = Both` |
| 渲染管线 | ⚠️ **Built-in RP**（两个 shader 是 CG 写的；URP/HDRP 下会变粉红）|
| 输入 | 必须启用**旧版 Input Manager**（`Player Settings → Active Input Handling` 设为 Old 或 Both）|
| 交换方式 | 只交换**脚本 + json**；场景/预制体带版本号，跨版本不可逆 |

---

## 七、特殊效果方块（半透明 / 动画 / 双面 / 发光 / 特殊模型）

⚠️ 约定命名的自动方块（`<id>_panel/_frame/_simple`）**只能是普通不透明**。
要用下面这些效果，请用 `Create → BlockMap → Block Definition` 建 **`.asset` 定义**，在 Inspector 里配置。
方块库里会显示前缀标记：`[模型]` `[透]` `[动]` `[镂空]` `[双]`。

| 想要的效果 | 设置 | 素材要求 |
|---|---|---|
| 镂空（树叶 / 栅栏）| `renderMode = Cutout`，`cutoff` 调阈值 | 贴图不需要的地方 **alpha = 0** |
| 双面薄片（纸 / 藤蔓）| `doubleSided = true` | — |
| 玻璃 / 水 | `renderMode = Transparent` | 贴图带**半透明 alpha** |
| 岩浆 / 流水动画 | `renderMode = Animated` + `animFrames` / `animSpeed` | **一条横向 1×N 序列帧图**，且用 `_simple` 或 `atlasOverride`（非图集）|
| 门 / 箱子 / 家具 / 树 | `previewPrefab` 指向你的 Prefab（建议同时勾 `interactive`）| — |
| 照亮周围 | `emitLight` + `lightColor` / `lightRange` / `lightIntensity` | — |

### 特殊模型是怎么跑的

- 带 `previewPrefab`（或 `interactive`）的方块，在**菜单 ② 烘焙静态几何时会被自动跳过** → 不会出现"立方体 + Prefab"两份
- `BlockMapRuntimeSpawner`（自动挂载）按格位实例化 Prefab 与点光源；**编辑器里也生效**，所以编辑阶段就能看到真实外观
- 点光源受 `maxLights` 限制（默认 32），避免大量发光方块拖垮帧率
- 生成物带 `BlockMapSpawned` 标记，重建前会先清掉旧的，**不会重复生成**（包括已保存进场景的）

---

## 八、排错表

| 现象 | 原因 / 处理 |
|---|---|
| 方块**粉红** Missing | 图集/材质没落成工程资产 → 用菜单 ② 重跑一次 |
| 编辑时**画面不更新** | 场景里 `Block Chunk Renderer` 组件被禁用了 → 重新勾上 |
| 导入后 **0 个方块** | 看 Console 的 `解析结果：N 个方块；方块库 M 种`；M=0 说明素材命名/位置不符合上面的约定 |
| Console 提示**跳过方块…缺边框贴图** | 有 `_panel` 没 `_frame` → 补上同 id 的边框，或改名叫 `_simple` |
| 相机**怎么改都没变** | Unity 里 Inspector 序列化值优先于代码默认值 → 重跑菜单 ①，或勾 `Force Top Down On Start` |
| 面板/UI 看不见 | 确认 Game 视图缩放为 1×；面板有「归位」按钮可复位 |

---

## 九、文件清单

```
BlockMapEditor/
├─ Runtime/   BlockWorld / BlockChunkRenderer / BlockDefinition / BlockLibrary
│             AutoTileAtlas / OrthoOrbitCamera / BlockMapGuides
│             BlockMapEditorTool / BlockMapBootstrap
├─ Editor/    BlockMapSceneSetup（① 建编辑场景）
│             BlockMapLevelBuilder（② 读存档烘场景）
└─ Resources/ BlockMap/BlockUnlit.shader + GhostUnlit.shader
              Lab_WallBrick1/  ← 示例方块素材（可删，换成你自己的）
```
