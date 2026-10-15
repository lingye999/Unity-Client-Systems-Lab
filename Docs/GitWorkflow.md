# CI 和分支合并流程

## 目标

CI 负责自动验证每个分支和 Pull Request 的仓库规则。合并由 GitHub 的分支保护和 Pull Request 流程控制，避免未经检查的代码直接进入 `main`。

## 标准流程

```text
main
    -> create task branch
feature/*
    -> push
    -> CI
    -> Pull Request -> main
    -> review / checklist
    -> required CI green
    -> squash merge
    -> automatic remote branch deletion
    -> delete local branch
```

每次发布任务都走完整流程：创建短期分支、提交 PR、检查通过、自动 squash 合并、删除远程任务分支、同步本地 main 并清理本地分支。同一 PR 合并前的修正继续推送到该任务分支。不能把创建 PR 当作发布已完成。

`main` 是唯一长期保留的分支。每项功能、修复或仓库维护工作都从最新的 `main` 创建独立任务分支；任务合并后不继续保留该任务分支，也不创建长期存在的 `develop` 或个人分支。

## CI 触发时机

`.github/workflows/ci.yml` 在以下情况运行：

- 任意分支 push，用来尽早发现问题。
- 目标为 `main` 的 Pull Request，用来作为合并门禁。
- 手动 `workflow_dispatch`，用于重新验证当前分支。

当前 CI 检查必需文档、根与模块 README 的章节职责、本地 Markdown 链接和 Unity 生成目录。文档规则入口为 `.github/scripts/check_repository_docs.py`。EditMode、PlayMode 和构建验证 job 尚未接入；Unity 本机验收与 GitHub CI 分别记录。后续 Unity 测试 job 必须固定使用 Unity 2022.3.62f3，并缓存只读的依赖，不把 `Library` 缓存提交到 Git。

## main 分支规则

GitHub 仓库设置中应为 `main` 配置以下保护规则：

- 禁止直接 push。
- 要求 Pull Request 合并。
- 要求 `CI / Repository policy` 通过。
- Unity 测试加入后，将对应测试 job 设为必需检查。
- 采用 squash merge，保持主分支提交历史按功能组织。
- 合并后删除任务分支。

这些保护规则属于 GitHub 仓库设置，不能仅靠仓库文件自动生效。仓库须同时启用 `Allow auto-merge` 和 `Automatically delete head branches`，并为 main 配置必需 PR 与 `Repository policy` 检查；管理员也遵循该门禁。调整发布流程时须读取实际设置核对，不能只依据本文判断已经启用。

## 自动合并

通过 GitHub Pull Request 的 `Enable auto-merge` 启用自动合并。仓库的 `Allow auto-merge` 只是允许使用该能力；每个新 PR 仍须单独启用 `Enable auto-merge`，并选择 squash。GitHub 会等待必需检查和审批满足条件后合并；当前不要求额外人工批准次数，必需 CI 检查必须通过。

仓库不使用一个拥有写入权限的 CI 机器人直接执行 `git push` 或 `gh pr merge`。这样可以保留分支保护、审批和检查作为真正的合并门槛；如果 CI 失败，PR 会保持打开并等待修复。

## 合并后清理分支

仓库启用 GitHub 的 `Automatically delete head branches` 设置。Pull Request 合并后，GitHub 会自动删除源任务分支，例如 `feature/*`、`fix/*` 或 `chore/*`。`main` 不会被删除；如果后续需要追溯，合并提交和 PR 中仍保留完整历史。发布完成前须核对 PR 已合并、main 包含合并提交，以及远程任务分支确实已删除。开启设置不会补删历史遗留分支；仅在核实 PR 已合并且分支未新增提交后清理它们。

远程分支删除后，在本地完成清理：

```text
git switch main
git pull --ff-only origin main
git branch -d <task-branch>
git fetch --prune origin
```

如果任务分支尚未合并，使用 `git branch -d` 会拒绝删除，以避免误删未合并的工作。只有明确放弃该任务时，才允许使用 `git branch -D`。

## 本地检查

提交前至少执行：

```text
git status
git diff --check
```

Unity 工程存在后，再打开 Unity 2022.3.62f3，确认 Console 无编译错误，并运行与变更相关的 EditMode / PlayMode 测试。
