# `.claude/` — Claude Code 项目配置

这是 `.codex/environments/environment.toml` 对应的 Claude Code 配置。该配置已提交到仓库，因此新建的 Git worktree 会自动获取它。

## 环境设置（与 codex `[setup]` 对等）

每次会话启动时，[`settings.json`](settings.json) 中的 `SessionStart` 钩子（hook）都会运行 [`scripts/setup-worktree.ps1`](../scripts/setup-worktree.ps1)。该脚本具有**幂等性**：耗时步骤以其输出产物为执行条件，因此只会在新 worktree 中真正执行：

- **前端依赖**：缺少 `frontend/node_modules` 时运行 `pnpm -C frontend install --frozen-lockfile`。（始终启用；typecheck/test/build/preview 均需要它。）
- **后端 .NET 还原**：**按需启用**（速度较慢；前端工作不需要）。设置 `$env:NERV_SETUP_BACKEND = '1'` 可启用完整对等设置，也可按需运行 `/setup-env`。

## 斜杠命令（与 codex `[[actions]]` 对等）

| 命令 | 操作 |
|---|---|
| `/setup-env` | 完整环境设置（前端依赖 + 后端 `dotnet restore`）。 |
| `/frontend-gate` | 前端质量门禁：`check` + `typecheck` + `test` + `build`。 |

可在 [`commands/`](commands/) 下添加更多命令；每个 `*.md` 都是命令调用时运行的提示词。

## 与 codex 的映射

| codex `environment.toml` | Claude Code |
|---|---|
| `[setup].script` | `settings.json` → `hooks.SessionStart` → `scripts/setup-worktree.ps1` |
| `[[actions]]` 命名命令 | `commands/*.md` 斜杠命令 |
| （启动应用以供预览） | `.claude/launch.json` 由预览工具读取，而非核心 CLI；按需创建。 |

## 注意事项

- `settings.local.json` 是每位开发者各自的本地状态（权限等），**不会**提交；不得在其中放置共享配置。
- SessionStart 钩子运行的 `scripts/setup-worktree.ps1` 位于**受治理的** `scripts/` 目录树中：它包含 `Script-Governance` 标头，以 dot-source 方式加载 `scripts/lib/ScriptAutomation.ps1`，并通过 `Invoke-Pnpm` / `Invoke-DotNet` 封装 `pnpm`/`dotnet`（超时、日志、脱敏和进程清理）。与其他所有脚本一样，它由 `scripts/check-script-governance.ps1` 验证，不享有治理豁免。
