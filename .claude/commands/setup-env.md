---
description: 完整设置 worktree 环境（前端依赖 + 后端 .NET 还原）
---

从仓库根目录为此 worktree 运行完整环境设置（与 codex `[setup]` 对等），并报告每一步的结果：

1. 代理技能 + 前端依赖：`pwsh -NoProfile -File scripts/setup-worktree.ps1`，即 SessionStart 钩子运行的同一脚本。它从主 worktree 镜像 `.agents/skills` 以及 `.claude/skills` 链接层（主 worktree 自己也缺这部分 payload 时，才先通过 `npx skills experimental_install` 安装），随后运行 `pnpm -C frontend install --frozen-lockfile --config.confirmModulesPurge=false`。
2. 后端还原：`dotnet restore backend/Nerv.IIP.sln`
3. Connector Host 还原：`dotnet restore connector-hosts/Nerv.IIP.ConnectorHost.sln`

如果某一步的产物已经存在则跳过该步骤：前端看 `node_modules`，后端看 `obj/project.assets.json`；**技能这一步的判据以 `scripts/lib/WorktreeSkills.ps1` 的 `Test-NervSkillsPayloadPresent` 为准，此处不复述**（它只数「这棵 worktree 自己的 `skills/` 提供不了、只能靠安装或镜像拿到」的那部分。仓库自带的技能每次会话都会重新发布进 `.agents/skills`，所以**该目录非空并不意味着这一步会被跳过**）。必须明确报告每一步是已运行还是已跳过，并展示所有失败及其输出。
