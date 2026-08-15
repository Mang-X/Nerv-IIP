---
description: 完整设置 worktree 环境（前端依赖 + 后端 .NET 还原）
---

从仓库根目录为此 worktree 运行完整环境设置（与 codex `[setup]` 对等），并报告每一步的结果：

1. 代理技能 + 前端依赖：`pwsh -NoProfile -File scripts/setup-worktree.ps1`，即 SessionStart 钩子运行的同一脚本。它从主 worktree 镜像 `.agents/skills` 以及 `.claude/skills` 链接层（仅当主 worktree 中不存在技能时，才通过 `npx skills experimental_install` 安装），随后运行 `pnpm -C frontend install --frozen-lockfile --config.confirmModulesPurge=false`。
2. 后端还原：`dotnet restore backend/Nerv.IIP.sln`
3. Connector Host 还原：`dotnet restore connector-hosts/Nerv.IIP.ConnectorHost.sln`

如果某一步的产物已经存在（非空的 `.agents/skills`、前端 `node_modules`、后端 `obj/project.assets.json`），则跳过该步骤。必须明确报告每一步是已运行还是已跳过，并展示所有失败及其输出。
