# CI 和分支合并流程

## 目标

CI 负责自动验证每个分支和 Pull Request 的仓库规则。合并由 GitHub 的分支保护和 Pull Request 流程控制，避免未经检查的代码直接进入 `main`。

## 标准流程

```text
feature/*
    -> push
    -> CI
    -> Pull Request -> main
    -> review / checklist
    -> required CI green
    -> squash merge
    -> delete branch
```

## CI 触发时机

`.github/workflows/ci.yml` 在以下情况运行：

- 任意分支 push，用来尽早发现问题。
- 目标为 `main` 的 Pull Request，用来作为合并门禁。
- 手动 `workflow_dispatch`，用于重新验证当前分支。

当前 CI 检查仓库文档、必需文件和 Unity 生成目录。第一个 Unity 工程提交后，再加入 EditMode、PlayMode 和构建验证 job。Unity 测试 job 必须固定使用 Unity 2022.3.62f3，并缓存只读的依赖，不把 `Library` 缓存提交到 Git。

## main 分支规则

GitHub 仓库设置中应为 `main` 配置以下保护规则：

- 禁止直接 push。
- 要求 Pull Request 合并。
- 要求 `CI / Repository policy` 通过。
- Unity 测试加入后，将对应测试 job 设为必需检查。
- 采用 squash merge，保持主分支提交历史按功能组织。
- 合并后删除任务分支。

这些保护规则属于 GitHub 仓库设置，不能仅靠仓库文件自动生效，需要在仓库 Settings 中配置一次。

## 自动合并

通过 GitHub Pull Request 的 `Enable auto-merge` 启用自动合并。启用后，GitHub 会等待必需检查和审批满足条件，再按照仓库设置执行 squash merge。

仓库不使用一个拥有写入权限的 CI 机器人直接执行 `git push` 或 `gh pr merge`。这样可以保留分支保护、审批和检查作为真正的合并门槛；如果 CI 失败，PR 会保持打开并等待修复。

## 本地检查

提交前至少执行：

```text
git status
git diff --check
```

Unity 工程存在后，再打开 Unity 2022.3.62f3，确认 Console 无编译错误，并运行与变更相关的 EditMode / PlayMode 测试。
