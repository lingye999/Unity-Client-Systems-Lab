# 森林随机地图 · 当前 Unity 模块

现用版本为 V2.1 无水森林和 V2.2 可选河流。只保留 `Scenes/RiverWoodlandV2.unity` 一个展示场景及其实际引用的烘焙资源。

Unity 菜单：**Forgotten Memories → Pastoral Grid V2 → Open current forest**。场景默认 seed 202、林间观景道和可变宽度河流。运行时可切换四种构图、植被密度、河流开关与全图/近景。旧普通森林和观景道无需再保存为两个独立场景。

## 当前代码入口

| 代码 | 职责 |
| --- | --- |
| `Runtime/Data/` | 单次请求、纯配置、布局与河流数据 |
| `Runtime/Generation/MapGenerationPipeline.cs` | 编排布局、栏杆、最终导航与校验 |
| `Runtime/Generation/SeededFields.cs` | 稳定 hash 与 Perlin 初始化 |
| `Runtime/Rules/PlacementConstraints.cs` | 五组硬约束、追加规则与首个失败统计 |
| `Runtime/PastoralGridGenerator.cs` | 路网、双网格、区域、植被和完整布局校验 |
| `Runtime/PastoralRiverGenerator.cs` | 最终水岸、多边形、岸边距离与通行辅助 |
| `Runtime/PastoralGridRaster.cs`、`PastoralRiverRaster.cs` | 原生像素、完整石头和六帧水纹 |
| `Runtime/PastoralGridV2Runtime.cs` | 资源加载、Unity 对象、Tilemap、Physics2D 与生命周期 |
| `Runtime/Unity/ForestGenerationProfile.cs` | Inspector 配方入口 |
| `Runtime/Unity/ForestCameraCapture.cs` | 当前验收共用的独立截图函数 |
| `Editor/` | 当前场景入口、配方验证和实际验收工具 |

配方可使用 `Profiles/ForestDefault.asset`，赋给运行组件的 `generationProfile` 后重新生成；留空保留旧默认参数。已有烘焙不会因为配方修改自动重建。

[仓库 README](../../../README.md) 提供运行说明、当前功能边界与调用关系。

## 素材、物理与显示约定

48×36 个 32px 原生瓦片；导航步长 16px，角色半径 9.6px。草土与水岸使用共享角样本，缺失的对角组合在生成阶段整理。草地通行路线仍保留净空；树根、水体和栏杆决定碰撞，树冠不作为整棵树的碰撞框。

原始 PNG 保留在旁边的美术项目。Unity 只保留冻结图集，以及现用林下草、水纹、铺石、栏杆片段。纹理使用 Point、Clamp、无 mipmap；不对原画统一染色或改变尺度。

当前模块已自包含：地图与栏杆数据在 `Runtime/Data`，道路几何与导航在 `Runtime/Geometry`，短栏杆规划在 `Runtime/Generation`，根部排序和原生像素适配在 `Runtime/Unity`，FastNoiseLite 与许可证在 `Runtime/ThirdParty`。所有现用素材统一为 `Resources/ForestMap`。三个旧模块目录已移除，运行代码不再依赖它们。迁移保留原素材和 `ForestDepth` 的 GUID，现用场景与烘焙文件未改写。

## 验证和限制

菜单保留 **Run acceptance** 与 **Run river acceptance**。测试在 Play Mode 改临时实例，默认输出到工程旁的 `output/ForestCurrentAcceptance/Dry` 或 `Water`；batch 可用 `-forestAcceptanceOutput` 指定目录。验收不重烘焙或覆盖已保存场景。

2026-10-15 的源工程中已完成核心 108 张、河流 48 张及 12 组实际 Unity Play Mode 验收。本仓库保留 Unity 验收入口；历史输出、离线工具、备份和设计演进 Word 留在本地。

当前是主线程生成的有限单层地图。高度仍为待美术评价的 PNG 原型；海拔生成、怪物 AI、桥、瀑布、无限地图、区块流送和 Player 性能基准尚未实现。手工刷瓦片只改变画面，不会反向修改逻辑地形；重新生成替换 Generated 子树。
