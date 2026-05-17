# ADR 0010: 自动化脚本可信执行治理

- Status: Accepted
- Date: 2026-05-17

## Context

Nerv-IIP 已经把验证脚本用于第一到第七阶段纵切验收，也会继续把安装、发布、迁移、seed、诊断、OpenAPI 导出和本地依赖编排纳入脚本入口。脚本已经不只是开发者便利工具，而是项目可信交付链路的一部分。

当前脚本可以完成关键验证，但仍存在治理缺口：部分脚本直接调用 `dotnet`、`docker`、`pnpm` 和子脚本；超时、日志、端口占用、后台进程、环境变量恢复和敏感信息脱敏没有统一约束；部分验证脚本还混入生成产物写入行为。随着后续进入 Gateway-wide auth、Console 登录、FileStorage、发布安装包和客户 PoC，这些缺口会放大为难诊断、难复现、甚至误删或误连真实环境的风险。

ADR 0008 冻结多部署目标和统一 AppHost/安装脚本方向，ADR 0009 冻结数据库迁移、seed 和发布边界。本 ADR 只定义“脚本如何被信任和治理”，不替代部署拓扑和数据库发布策略。

## Decision

1. 自动化脚本是一等工程资产，必须像业务代码一样拥有分类、边界、诊断和门禁。
2. `scripts` 下脚本按意图分为 `check`、`verify`、`generate`、`release-install` 四类。
3. 每个脚本必须声明分类、主要副作用、依赖服务、可写路径、清理策略和建议运行场景。
4. `check` 脚本只能做构建、测试、静态检查、格式检查和治理门禁；不得启动长期依赖、修改业务数据或写入生成代码。
5. `verify` 脚本可以启动本地依赖、容器、临时服务和一次性数据库，但必须使用可识别的验证库名或 run id，输出清理范围，并保证失败路径也会清理自有进程。
6. `generate` 脚本可以写入声明过的生成产物，例如 OpenAPI 快照和 api-client；它不得伪装成纯验证入口。
7. `release-install` 脚本是 PoC、私有化和生产安装发布入口，必须满足 ADR 0009 和数据库发布 runbook 的备份、迁移、seed、诊断、停止条件和敏感信息处理要求。
8. 高风险原生命令、长耗时命令和后台进程必须通过共享脚本 helper 执行；helper 负责超时、结构化日志、退出码、耗时、PID、进程树清理和敏感信息脱敏。
9. 脚本不得依赖 `Start-Job`/`Stop-Job` 作为唯一进程清理机制。需要启动后台服务时必须记录 root PID，并在超时、失败和正常退出路径清理自有进程树。
10. 固定端口、数据库名、容器名和输出目录必须有 preflight；端口占用时必须给出明确诊断，不得盲目继续。
11. 环境变量修改必须具备作用域和恢复语义；脚本结束后不得把连接串、token、密码或 profile 设置泄漏到调用 shell。
12. 脚本输出可以打印诊断路径、服务名、profile、数据库别名和 correlation id，但不得打印完整连接串、密码、token、client secret 或客户密钥。
13. 新增脚本必须通过脚本治理门禁。既有脚本可以按迁移清单逐步纳入，但迁移期间不得新增未登记的治理债务。
14. PowerShell 7 是当前 Windows 脚本基线；后续 Linux Bash 脚本必须满足同等治理契约，而不是另起一套较弱规则。
15. 自动化验证矩阵分为 fast、infra、full 三层：fast 覆盖解析、治理门禁和不依赖外部服务的测试；infra 覆盖 Docker/本地依赖和真实数据库验证；full 串起导出、生成、前后端和 connector host 的完整回归。

## Rationale

1. 脚本故障通常发生在业务测试之外：端口被占用、后台进程残留、连接串误用、子进程卡住或外部命令吞掉错误。统一 helper 能把这些问题变成可观察事件。
2. 把 `verify` 和 `generate` 分开，可以避免“验收脚本顺手改代码”的隐性副作用。
3. 把 `release-install` 与本地验证脚本分开，可以避免客户数据环境继承本地 disposable database 习惯。
4. AST/static gate 比人工约定可靠，能尽早阻止直接调用高风险命令、动态执行和绕过 helper 的后台进程。
5. 迁移清单允许已有脚本渐进治理，同时防止后续继续扩大不可控脚本面。

## Consequences

1. 后续新增或修改脚本时，需要同步声明分类、副作用和清理策略。
2. 现有脚本需要逐步迁移到共享 helper，优先迁移真实基础设施、数据库和 IAM 持久化相关脚本。
3. 验证耗时脚本会多输出结构化诊断，短期增加日志量，但能显著降低卡住和误连环境时的排障成本。
4. 发布安装脚本不能复用本地验证脚本的危险默认值，必须显式区分 disposable verification 和 release target。
5. CI 后续应先接入 fast gate，再把 infra/full gate 分层接入，避免所有 PR 都强制拉起完整依赖栈。

## Implementation Notes

1. 共享 helper 落点为 `scripts/lib/ScriptAutomation.ps1`。
2. 脚本治理门禁落点为 `scripts/check-script-governance.ps1`。
3. 具体分类矩阵、helper 契约、迁移清单和验证命令见 `docs/architecture/script-automation-governance.md`。
4. 数据库迁移、seed、备份和发布停止条件继续以 `docs/architecture/database-release-runbook.md` 为准。
5. 部署拓扑、AppHost、Compose、安装包和整合安装脚本边界继续以 `docs/adr/0008-multi-target-deployment-and-aspire-apphost.md` 与 `docs/architecture/deployment-baseline.md` 为准。

## Out of Scope

1. 本 ADR 不定义最终 CI/CD 平台或流水线文件格式。
2. 本 ADR 不要求一次性重写所有历史脚本。
3. 本 ADR 不替代 ADR 0009 的数据库发布、迁移、seed 和备份策略。
4. 本 ADR 不定义客户环境具体安装参数、端口规划、机器规格或密钥轮换流程。
