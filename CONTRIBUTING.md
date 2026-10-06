# 贡献和开发规范

本文件是仓库的开发契约。新增模块、修复问题和修改公共接口都应遵循这里的规则。

## 开发环境

- 使用 Unity 2022.3.62f3 打开项目。
- 不提交 Unity 生成的 `Library`、`Temp`、`Obj`、`Logs`、`Build` 等目录。
- 不提交个人编辑器布局、机器相关配置、构建产物或本地密钥。
- 第三方素材必须记录来源、作者和许可证。

## 分支

`main` 只接受经过检查的合并提交。每个任务使用独立分支，格式如下：

```text
feature/<short-name>
fix/<short-name>
refactor/<short-name>
perf/<short-name>
test/<short-name>
docs/<short-name>
chore/<short-name>
```

分支名称使用小写英文和连字符，只描述一个主题，例如 `feature/inventory-stack`。

## 提交信息

提交信息使用 Conventional Commits：

```text
<type>(<scope>): <imperative summary>
```

允许的 `type`：

- `feat`：新增用户可见能力。
- `fix`：修复错误行为。
- `refactor`：不改变行为的代码重构。
- `perf`：性能改进。
- `test`：新增或修改测试。
- `docs`：文档修改。
- `chore`：工程配置或维护工作。

示例：

```text
feat(inventory): add stack capacity validation
fix(ui): refresh only changed inventory slot
docs(architecture): define module dependency direction
```

每次提交保持一个可解释的变更主题。提交前必须确认 Unity 能打开、脚本无编译错误，并运行与变更相关的测试。

## Pull Request

PR 应从任务分支提交到 `main`，标题遵循提交信息格式。描述中必须说明：

- 修改解决了什么问题。
- 影响了哪些模块和公共接口。
- 如何验证，包含测试场景、测试结果或 Profiler 数据。
- 如果是 UI 变更，附上运行截图或短视频。
- 如果是性能变更，提供相同条件下的优化前后数据。

PR 合并前需要满足：

- GitHub Actions 的 `CI / Repository policy` 检查通过。
- 没有把 `Library`、构建包或个人配置带入提交。
- 业务层没有新增跨模块的隐式依赖。
- 公共接口和数据字段变更已同步更新文档。
- 变更不会破坏 PDF 计划中已经验收的功能。

分支创建、CI 触发、审批和合并方式见 [CI 和分支合并流程](Docs/GitWorkflow.md)。

## 代码规则

- 类型、方法、属性和枚举成员使用 `PascalCase`。
- 私有字段使用 `_camelCase`，参数和局部变量使用 `camelCase`。
- 布尔值使用 `is`、`has`、`can` 或 `should` 开头。
- 使用完整名称，例如 `itemId`、`instanceId`、`slotIndex`、`quantity`，不使用 `id`、`num`、`data` 作为含义不清的公共字段。
- Inspector 字段使用 `[SerializeField] private`，不要为了方便把字段公开。
- 不公开可变集合；对外返回只读接口或副本。
- 业务层不调用 `Find`、`GetComponent`、`Resources.Load`、`PlayerPrefs` 或 Unity UI API。
- 正常业务失败使用 `Try...` 返回值或明确的结果对象，不使用异常控制流程。
- 不使用一个全局单例承载多个系统。

## 变更原则

先保持数据和规则正确，再做 UI 和性能优化。性能优化必须有基线、测试条件和复测结果；没有数据的“优化”不能写入项目结论。
