# 素材目录规范

| 目录 | 放什么 |
|---|---|
| `Art/Models` | 三维模型（.fbx / .obj） |
| `Art/Textures` | 贴图 |
| `Art/Materials` | 材质 |
| `Art/Animations` | 动画片段 / 动画控制器 |
| `UI/Prefabs` | 界面预制体 |
| `UI/Sprites` | 图标 / 九宫格图 |
| `UI/Fonts` | 字体 |
| `Audio/BGM` | 背景音乐 |
| `Audio/SFX` | 音效 |
| `VFX/Prefabs` | 特效预制体（粒子 / 拖尾…） |
| `VFX/Textures` | 特效贴图（烟 / 火 / 光斑…） |
| `Scripts/Runtime` | 运行时脚本 |
| `Scripts/Editor` | 只在编辑器里跑的脚本（Unity 特殊目录名） |
| `Prefabs` | 通用预制体（角色 / 道具…） |
| `Scenes` | 自己的场景（主菜单 / 测试场…） |
| `Data` | 配置资产（ScriptableObject / json） |
| `Settings` | 项目内设置资产（渲染 / 质量…） |

三条约定：

1. **关卡场景不在这里** —— 地图编辑器烘出来的关卡统一放在 `Assets/Scenes/<地图名>.unity`。
2. `Scripts` 目前落在默认程序集 `Assembly-CSharp`（没有 asmdef），可以直接用 BlockMap 包的运行时类型（`BlockWorld` / `BlockMapGameRoot` / `BlockMapGridWalker` …）。以后要拆 asmdef 再说。
3. 工具产出放在 `Assets/BlockMapEditor/`（`Built/` 材质图集、`Resources/` 标记贴图与 Prefab），别手动改，重跑菜单会覆盖。