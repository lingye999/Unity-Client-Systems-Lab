# 模块化架构规范

## 目标

架构需要让背包、红点、随机地图、存档和资源更新可以独立演进。模块之间通过明确的接口和事件通信，禁止通过场景查找、静态可变状态或隐式单例互相调用。

## 目录和职责

```text
Assets/_Project/
├── Runtime/
│   ├── Core/                 # 通用结果类型、时间、日志和基础接口
│   ├── Inventory/
│   │   ├── Domain/           # 物品、格子、背包规则；纯 C# 优先
│   │   ├── Application/      # 用例、命令、查询和业务事件
│   │   └── Infrastructure/   # ScriptableObject、存档、网络和资源适配器
│   ├── RedDot/               # 红点节点、聚合和通知适配
│   ├── ProceduralMap/        # 随机地图生成和地图数据
│   ├── UI/                   # uGUI 视图、Presenter 和输入适配
│   └── Bootstrap/            # 场景启动和依赖组装
├── Editor/                   # 编辑器工具，不进入运行时程序集
├── Tests/                    # EditMode 和 PlayMode 测试
├── Prefabs/
├── Scenes/
└── Settings/
```

## 依赖方向

```text
Bootstrap -> UI -> Application -> Domain
Bootstrap -> Infrastructure -> Application
RedDot  -> Application events
UI      -> read models and application commands
```

`Domain` 不依赖 Unity 场景、uGUI、存档、网络或资源加载。`Application` 负责业务用例和变更事件，但不负责渲染。`Infrastructure` 实现外部系统接口。`UI` 只负责显示和输入转发。

## 数据所有权

- `ItemDefinition` 是静态物品配置的唯一来源。
- `ItemInstance` 保存玩家拥有的运行时状态。
- `Inventory` 拥有格子集合，并负责维护容量和位置不变量。
- UI 只持有只读展示数据，不直接写入背包字段。
- 存档只保存可恢复的业务状态，不保存 GameObject 引用。

## 扩展规则

新增功能优先选择以下扩展点：

1. 新增一个业务用例或策略接口，而不是修改多个 UI 类。
2. 通过接口替换存档、网络或资源来源，而不是把外部 API 写入 Domain。
3. 通过领域事件通知变化，再由 UI 或红点模块订阅。
4. 新增物品属性时评估它属于 `ItemDefinition`、`ItemInstance` 还是独立组件。
5. 新增系统必须有自己的根目录、公共入口和测试，不把代码塞进 `GameManager`。

## 禁止的结构

- 一个类同时管理数据、业务、UI、存档和网络。
- UI 通过 `Find` 或字符串路径寻找业务对象。
- 静态列表作为跨场景的唯一业务状态。
- 事件订阅没有对应的解绑逻辑。
- 为了复用而把所有模块放进一个万能 `Utils` 或 `Manager`。
- 在没有基线和复测数据时声称完成性能优化。

## 生命周期

`Bootstrap` 创建配置仓库、业务服务和 UI 入口，完成依赖注入后才允许打开页面。模块关闭或销毁时必须解除事件订阅。测试可以使用内存实现替代存档、网络和资源加载器。
