# BlockMapEditor —— 方块地图编辑器

一格 = 1m×1m×1m，世界上限 **256 × 256 × 36 格**，正交俯视机位。
贴图由 `面板 + 边框` 在运行时合成图集，实现**自动包边**（机壳式：只有整片结构的外沿才出现边框，凹角自动补点）。

---

## 一、三步上手

1. 本工具已作为 **UPM 本地包**放在 `Tools/BlockMapEditor/`，由 `Packages/manifest.json` 的 `file:` 依赖引入（不用再往 `Assets/` 里拷）
2. 菜单 **`Tools / Slima / 地图编辑器 / ① 创建地图编辑场景`**
3. **Play** → 左侧面板开始刷地图

| 操作 | 按键 |
|---|---|
| 放置 / 擦除 | **左键** / **Shift+左键**（或勾选面板的「橡皮擦」后左键）|
| 换方块 | `[` `]`（或点面板方块库）|
| 放置朝向 | 面板「左转 / 右转」或按 `R`；发射器顶部箭头指向实际出光方向 |
| 旋转已放机关 | 鼠标指向机关后按 `Shift+R`，原地顺时针旋转90度 |
| 关联机关与门 | 按 `L` 或点右上角「机关关联」；左键加入当前组，`Shift+左键` 解除，`Esc` 退出 |
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

菜单 **`Tools / Slima / 地图编辑器 / ② 把地图烘成场景（持久化）`**：

- 在**干净场景**里 → 烘成真实 GameObject（每个 chunk 一个 MeshFilter+MeshRenderer），材质与图集落成 `Assets/BlockMapEditor/Built/` 下的工程资产 → **保存场景后永久存在**
- 在**带编辑器的场景**里 → 只把数据灌进 `BlockWorld`（可继续编辑），不烘静态几何
- **方块数据会一起存进场景**（`BlockWorld` 把数据按 id 镜像持久化）——重开工程后起点 / 终点、特殊模型、光源都还在。
  ⚠️ 用旧版本烘的场景没有这份数据，**必须重新跑一次 ②**
- 烘出来的几何**自带碰撞体**（每个 chunk 一个 MeshCollider）——角色要站在地形上，这一项必须有

---

**① 在地图里刷起点 / 终点**（开箱即用，不需要先跑任何生成菜单）：

- 方块列表里已经有 `Spawn`（青色 `S` 面板）和 `Exit`（橙色 `E` 面板）两种方块 —— 由 `Resources/Spawn|Exit/` 下的 panel + frame 贴图自动注册，**像门一样直接刷**
- 这两个 id 登记为「玩法标记」（`BlockLibrary.MarkerBlockIds`）：`interactive = true` → **烘焙静态几何时会跳过**，不会变成挡路的立方体
- 在编辑器里它们是**半透明**的（`BlockRenderMode.Transparent`，alpha 0.45）：看得见位置，又不会挡住地形；邻居也不会因为"这边有方块"而把面剔出空洞
- 刷在**地面之上那一层**（例如地板在 `y=0`，标记刷在 `y=1`），别把地板方块顶掉（真的顶掉了游戏根会在屏幕上提示）
- 按 `R` 转朝向 ＝ 角色出生朝向
- 想让标记 / 角色更好看：自己做个 Prefab 挂上去即可 —— 标记认 `Resources/BlockModels/<id>.prefab`，角色在 `BlockMapGameRoot.playerPrefab` 上指定（留空就用灰盒）

**② 烘场景**：菜单 **`Tools / Slima / 地图编辑器 / ② 把地图烘成场景（持久化）`**
- 得到真实几何 + **碰撞体**（每个 chunk 一个 MeshCollider）+ 方块数据（按 id 持久化进场景，重开工程不丢）

**③ 加游戏根**：菜单 **`Tools / Slima / 地图编辑器 / ③ 把当前场景变成可玩关卡`**
- 在 `BlockWorld` 所在物体上挂 `BlockMapGameRoot`：读起点 / 终点 → 在起点实例化角色 → 架好固定角度正交相机 → 走到终点格即通关
- 起点 / 终点在游戏里的可见标记由游戏根现场生成（若该方块已有 `previewPrefab` 则不重复生成）
- 会顺便检查「标记有没有」「碰撞体有没有」「数据有没有」，缺什么直接写在对话框里

**地图文件（可选）**：`导出地图 json 到工程（Git）` / `从工程导入地图 json`
- Play 里「保存」写的是 `AppData/…/Maps`，不在工程里；这两个菜单把 json 存进 `Assets/BlockMapEditor/Levels/` 便于提交给队友

**操作**：
- **WASD / 方向键**：按固定四方向移动一格；蓝色固态史莱姆会持续滑行
- **E**：红色破坏面前一个可破坏物；蓝色切换光学模式；气态恢复固态
- **F**：使用所在格的分割机，或操作相邻拉杆
- **Tab**：两只史莱姆之间切换控制
- **R**：完整重置关卡
- 通行、机关和光线均按格子数据判定；碰撞体只负责画面和后续物理表现

---

## 五、玩法机关与配置

### 5.1 配置位置

| 文件 | 用途 |
|---|---|
| `Resources/BlockMap/GameplaySettings.json` | 移动时间、跳跃高度、气态高度、蓝色滑行上限、光程、光束宽度、门动画与发射器方向箭头 |
| `Resources/BlockMap/GameplayCatalog.json` | 方块 id、机关类型、旧地图默认频道、颜色、模式、重量阈值、灰盒颜色与形状 |

JSON 内的 `_comment` 为中文说明字段。修改后重新进入 Play 生效。

### 5.2 地编可用 id

| 功能 | id |
|---|---|
| 门 | `Door`、`Door_B` |
| 压力板 | `Lab_PressurePlate`（小）、`PressurePlateLarge_A`（大）、`PressurePlateSmall_B`（小）|
| 拉杆 | `Lab_Lever`、`Lever_B` |
| 分割机 | `Splitter` |
| 高温 / 低温 / 伤害地板 | `Lab_Heater_01` / `Lab_Heater_02` / `DamageFloor` |
| 可破坏物 | `Breakable` |
| 白 / 红 / 蓝光发射器（可推动） | `Lab_Laser_01` / `Lab_Laser_02` / `Lab_Laser_03` |
| 白 / 红 / 蓝光发射器（固定） | `EmitterWhiteWall` / `EmitterRedWall` / `EmitterBlueWall` |
| 白光持续 / 永久接收器 | `Lab_Laser_04` / `Lab_Laser_05` |
| 红 / 蓝 / 紫光接收器 | `ReceiverRed_A` / `ReceiverBlue_A` / `ReceiverPurple_A` |

### 5.3 机关关联流程

1. 放置压力板、拉杆或光接收器，再放置需要控制的门。
2. 按 `L` 或点击右上角「机关关联」。
3. 点击一个机关：自动建立 `G001`；继续点击任意数量的门或机关，将它们加入同组。
4. 需要另一套关联时点击「新建组」；需要修改旧组时在列表中选择该组。
5. `Shift+左键` 解除鼠标所指物件的关联。机关上方显示独立编号；门上方居中显示同组机关的所有编号，例如 `[1]  [3]`。
6. 保存地图。每个物件的 `linkId` 与机关的 `mechanismNumber` 随地图 JSON 和烘焙场景保存。已有编号不会因删除其他机关而变化；旧地图加载时自动补号，保存后固定。

同组多个机关采用“任意一个启动即开门”；同组可控制任意数量的门。地图一旦使用实例关联，未关联物件不会误触发其他门。旧地图没有 `linkId` 时继续读取 `GameplayCatalog.json` 的 `channel`。
发射器朝向取地编中的方块朝向。
原型发射器顶部使用高亮箭头显示方向：白光白箭头、红光红箭头、蓝光蓝箭头。美术模型能清楚表达方向后，可把 `showEmitterDirectionIndicator` 改为 `false`。

### 5.4 关卡制作检查

1. 地板放在 `y=0`；起点、终点、地板机关和角色活动格放在 `y=1`。
2. 每格最多放一个机关；机关下方保留地板。
3. 至少放一个 `Spawn` 和一个 `Exit`。
4. 按菜单 ② 烘焙，再按菜单 ③ 转为可玩关卡。
5. Play 测试移动、能力、分裂/融合、门和光路；R 检查能否完整还原。

### 5.5 接入美术

- 使用现有 id 制作 `Resources/BlockModels/<id>.prefab`，逻辑与关卡数据无需修改。
- Prefab 原点放在格心；朝向正面使用本地 `+Z`；占地保持一格。
- 新机关先在 `GameplayCatalog.json` 增加条目；需要新规则时新增独立脚本，不把规则写进地编输入脚本。

### 5.6 原型交互与提示（2026-10-10）

1）倒三角跟随当前操作的史莱姆，切换、分裂和融合后仍然有效；地图错误提示不隐藏标记。

2）满足 F 交互条件时显示气泡。拉杆和分裂器：气泡在道具上方；融合：气泡在当前操作的史莱姆上方。

3）常态小史莱姆面朝相邻常态小史莱姆，按 F 移动过去融合；普通移动不自动融合。冰史莱姆滑行碰到另一只常态小史莱姆仍自动融合，并停止滑行。

4）气态可经过、停留在普通史莱姆上方；重叠时禁止恢复常态，显示暗色、带斜杠的 E 气泡。移动到合法空格后显示亮色 E。

5）`Resources/BlockMap/GameplaySettings.json`：`cameraSmoothTime` 调整相机阻尼；`interactionBubbleSize/Height` 调整气泡；`activeMarkerHeight` 调整倒三角；`showMechanismNumbers` 与 `mechanismLabelHeight/Gap` 调整编号。

6）单关相机设置：选中挂有 `BlockMapGameRoot` 的对象，勾选 `overrideCameraSmoothTime`，修改 `cameraSmoothTime`。首次进入和重开直接定位，切换与移动平滑跟随。

---

## 六、发版保护

`BlockMapBootstrap` 上的 **`Editor Only In Development Build`**（默认 ☑）：

- Unity Editor / **Development Build**：全功能
- **正式包**：自动关闭面板、鼠标操作、网格与虚影 → 玩家看不到任何编辑器痕迹

更彻底的做法：玩家场景里根本不放编辑器（用菜单 ② 烘出纯几何的场景），并且 Build Settings 里不勾 `BlockMapEditor.unity`。

---

## 七、兼容性

| 项 | 要求 |
|---|---|
| Unity | **2019.4 LTS ~ 2022.3 LTS**（编写环境 2022.3 / 团结引擎 1.10.x）；Unity 6 可用但需 `Active Input Handling = Both` |
| 渲染管线 | ⚠️ **Built-in RP**（两个 shader 是 CG 写的；URP/HDRP 下会变粉红）|
| 输入 | 必须启用**旧版 Input Manager**（`Player Settings → Active Input Handling` 设为 Old 或 Both）|
| 交换方式 | 只交换**脚本 + json**；场景/预制体带版本号，跨版本不可逆 |

---

## 八、特殊效果方块（半透明 / 动画 / 双面 / 发光 / 特殊模型）

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

## 九、排错表

| 现象 | 原因 / 处理 |
|---|---|
| 方块**粉红** Missing | 图集/材质没落成工程资产 → 用菜单 ② 重跑一次 |
| 编辑时**画面不更新** | 场景里 `Block Chunk Renderer` 组件被禁用了 → 重新勾上 |
| 导入后 **0 个方块** | 看 Console 的 `解析结果：N 个方块；方块库 M 种`；M=0 说明素材命名/位置不符合上面的约定 |
| Console 提示**跳过方块…缺边框贴图** | 有 `_panel` 没 `_frame` → 补上同 id 的边框，或改名叫 `_simple` |
| 相机**怎么改都没变** | Unity 里 Inspector 序列化值优先于代码默认值 → 重跑菜单 ①，或勾 `Force Top Down On Start` |
| 面板/UI 看不见 | 确认 Game 视图缩放为 1×；面板有「归位」按钮可复位 |

---

## 十、文件清单

```
BlockMapEditor/
├─ Runtime/   地图数据、渲染、编辑运行时
│  ├─ BlockMapLinkEditorTool.cs  机关实例关联面板与标记
│  └─ Gameplay/
│     ├─ Core/     类型、规则、全局配置
│     ├─ Player/   史莱姆状态、格子移动、队伍、分裂融合
│     ├─ World/    机关、实例关联规则、信号、灰盒表现
│     ├─ Optics/   光线追踪与接收器
│     └─ UI/       原型局内 HUD
├─ Editor/    BlockMapSceneSetup（① 建编辑场景）
│             BlockMapLevelBuilder（② 读存档烘场景）
├─ Tests/     核心玩法规则的 EditMode 测试
└─ Resources/ BlockMap/GameplaySettings.json + GameplayCatalog.json
```
