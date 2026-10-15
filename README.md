# Unity Client Systems Lab

一个面向 Unity 客户端实习项目的系统实践仓库。

项目以 Unity 2022.3.62f3 为固定基线，按模块逐步实现背包、红点通知、随机地图、UI 交互、存档、网络通信、资源更新和性能分析。每个模块都以可运行、可测试、可解释为目标，并记录实现取舍和性能证据。

## 项目目标

- 练习可扩展的 Unity 客户端架构和 C# 代码组织。
- 将背包、红点和其他系统拆成职责清晰的模块。
- 按阶段加入测试、Profiler 数据和可复现的验收步骤。
- 形成可以用于实习展示的 Unity 项目作品集。

## 当前状态

当前已提供可运行的森林随机地图 Demo，详见模块 README。背包、红点、存档、网络与资源更新仍属于后续计划；各模块的实现与验收状态由对应模块文档说明。

## 文档导航

- [森林随机地图 Demo：已实现功能、运行方法与验收](Assets/ForgottenMemories/PastoralGridV2/README.md)
- [贡献、分支和提交规范](CONTRIBUTING.md)
- [CI 和分支合并流程](Docs/GitWorkflow.md)
- [后续系统架构规范](Docs/Architecture.md)
- [后续数据模型和变量定义](Docs/DataModel.md)
- [功能边界和阶段计划](Docs/FeatureScope.md)

## 基线环境

- Unity 2022.3.62f3。
- C#。
- 后续 UI 模块以 Unity uGUI 和 TextMeshPro 为基线。

## 许可

本仓库用于学习、作品集和面试展示。第三方素材和依赖遵循各自的许可证，使用新素材时必须在文档中记录来源和许可证。
