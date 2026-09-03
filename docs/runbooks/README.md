# Runbook 文档入口

本目录只承载**当前可执行操作**：启动、部署、迁移、恢复、排障以及操作者需要遵守的停止条件和证据要求。命令、参数、端口、版本与运行行为最终以当前脚本、CLI help、配置和代码为准；Runbook 不保存项目进度或阶段历史。

## 按操作路由

| 操作 | 当前 Runbook | 主要权威生产者 |
| --- | --- | --- |
| 本地开发、Aspire、worktree、真实依赖与排障 | [`local-development.md`](local-development.md) | `nerv.ps1 help`、`nerv.ps1 ports`、AppHost、配置与脚本 |
| 脚本执行、验证、兼容性与治理排障 | [`script-automation.md`](script-automation.md) | `nerv.ps1 help`、目标脚本 `Get-Help`、`scripts/check-script-governance.ps1`、共享脚本 library/tests |
| 测试 evidence、determinism、真实依赖、PDA smoke 与前端单测判读 | [`testing/README.md`](testing/README.md) | `scripts/tests/**`、test/lane manifests、runner/verifier、Vitest/Playwright/Android producer |
| 数据库发布、迁移、备份、恢复与 seed | [`database-release.md`](database-release.md) | `scripts/install/migrate-*.ps1`、migration manifest、EF migrations、ADR 0009 |
| FileStorage 停服离线迁移、切换与回滚 | [`file-storage-offline-migration.md`](file-storage-offline-migration.md) | ADR 0027、FileStorage/provider 实现、对应迁移实现 |
| PDA / Capacitor / APK 构建与部署 | [`mobile-pda-deployment.md`](mobile-pda-deployment.md) | `frontend/apps/business-pda/scripts/pda-apk-build.ps1`、Capacitor/Android 配置 |

## 操作纪律

1. **先核对前置条件，再执行有副作用命令。** 目标环境、身份、连接、备份、工具链或受治理入口不满足时停止，不靠临时脚本绕过。
2. **优先使用仓库公开入口。** 如果 Runbook 文本与 `nerv.ps1 help`、脚本参数、配置或当前实现冲突，以生产者为准并修正文档。
3. **失败要有停止条件。** 不确定写入是否完成时先用权威读面或工具状态核实，禁止盲目重放非幂等动作。
4. **恢复/回滚必须说明边界。** 能否回滚、需要前滚修复、是否依赖备份或新 run，都以各 Runbook 的明确契约为准。
5. **证据与秘密分开。** 记录 commit/release/run、目标、命令结果、日志或 fingerprint；口令、token、连接串等敏感输入不得进入仓库或公开日志。
6. **Runbook 不保存阶段历史。** 一次性调查、历史形成过程和完成记录进入 Reports/Archive；任务状态和验收证据留在 GitHub/Linear。

## M2 迁移兼容

`docs/architecture/` 下已迁移的旧 Runbook/Governance 文件名在 M2 期间只保留短导航，不能继续维护操作或规则正文；兼容入口及本目录内临时旧相对链接指针的删除条件由 M2-M/M4 统一收口。
