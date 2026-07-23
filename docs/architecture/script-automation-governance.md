# 脚本自动化治理

本文档承接 ADR 0010，定义 Nerv-IIP 脚本的分类、声明、helper 契约、门禁和迁移顺序。它描述脚本如何被信任，不替代 ADR 0008 的部署拓扑，也不替代 ADR 0009 和 database release runbook 的数据库发布规则。

## 当前结论

1. `scripts` 下脚本必须按 `check`、`verify`、`generate`、`release-install` 分类。
2. 新增脚本必须声明副作用，并通过 `scripts/check-script-governance.ps1`。
3. 高风险原生命令必须通过 `scripts/lib/ScriptAutomation.ps1` 中的 helper 执行。
4. 既有脚本按迁移清单逐步治理；迁移期间允许登记 legacy exemption，但不得新增未登记债务。
5. `verify` 脚本可以使用 disposable database、容器和本地服务，但必须输出目标、清理策略和诊断日志。
6. `generate` 脚本可以写声明过的生成产物；生成行为不得藏在纯 verify 脚本里。
7. `release-install` 脚本必须走发布迁移、seed、备份和诊断契约；不得沿用本地验证脚本里的删除数据库、默认密码或隐式 AutoMigrate 习惯。
8. macOS/Linux 支持必须通过跨平台兼容门禁后才能声明；当前 IAM core verify 已在 Ubuntu 22.04.3 WSL 环境完成兼容门禁，后续脚本仍需按脚本粒度记录证据。
9. Agent-owned 真实全栈验证必须使用 `.\nerv.ps1 fullstack run -Scenario smoke`；MAN-440 运行小时 PM 触发验收使用 `.\nerv.ps1 fullstack run -Scenario man-440`，以 session-owned PostgreSQL seed 证明低于阈值不生成、追加运行状态跨阈值后由真实 scheduler 自动生成计划工单，并确认 Maintenance Redis CAP consumer group 已就绪；MAN-524 领导演示主链使用 `.\nerv.ps1 fullstack run -Scenario leader-demo-main-chain`。后者只允许公开 BusinessGateway HTTP 业务断言，session harness 必须在 AppHost 启动前显式选择 Redis messaging 与 PostgreSQL persistence、把画像记录到 manifest 并传给证据进程；只有逐跳 runtime-confirmed 和已登记的 #972 查询 gap 可以通过，任何 `not-verified` 或未登记 gap 都必须使命令失败。运行账本保存在 session artifact 中，不提交仓库。交互 `fullstack start` 只用于诊断，并必须在交接前停止。
10. Connector 现场断连验收使用受治理入口 `pwsh scripts/verify-connector-health-disconnect.ps1 -Runs 3`；固定 10 秒 deadline，不得因 CI 或现场抖动放宽。确定性门禁通过不等于 Docker/PostgreSQL 真实验收通过。
11. MAN-519 领导演示环境必须使用 `.\nerv.ps1 demo start|reset|seed|health-check|stop`；密码仅从当前 PowerShell 进程的 `NERV_IIP_LEADER_DEMO_ADMIN_PASSWORD` 读取，运行 profile 强制断言 Redis，reset/stop 只清理本地 pointer 授权的精确 full-stack session。
12. MAN-601 设备遥测模拟使用 `pwsh scripts/verify-leader-demo-telemetry-simulator.ps1 -DurationMinutes 10 -HistoricalBackfill`；脚本只解析当前 leader-demo pointer 指向的精确 Running session，只经公开 BusinessGateway `/telemetry/samples` 写入事实，并通过公开 history/alarm facade 验证。模拟器在前台运行，不创建后台进程；同一 `RunId + ScenarioStartUtc` 产生相同 source sequence/payload，`-ReplayExisting` 仅在同时显式提供这两个原始值时跳过等待并重放整条已存在时间线。历史时间戳先实测，拒绝时只降级为显式记录的 session 内五分钟短窗，不得直写 historian。

## 分类矩阵

| 分类 | 允许行为 | 禁止行为 | 示例 |
| --- | --- | --- | --- |
| `check` | 解析脚本、静态门禁、build/test/typecheck、格式检查、无外部依赖的单元测试 | 启动长期服务、删除数据库、写生成代码、修改业务配置 | `check-script-governance.ps1` |
| `verify` | 启动本地依赖、容器、临时 Web 服务、disposable database，运行端到端验收 | 连接客户数据环境、写未声明产物、使用生产库名、失败后留下自有进程 | `verify-iam-persistent-auth-foundation.ps1` |
| `generate` | 导出 OpenAPI、生成 api-client、写入声明过的 generated/openapi 文件 | 伪装成只读验证、手改生成产物、绕过后端契约测试 | `export-gateway-openapi.ps1` |
| `release-install` | 环境检查、配置生成、备份证据、migration bundle、seed、服务注册、健康检查、诊断包 | 直接拼 SQL 写业务表、绕过 EF migrations history 建表、默认测试密码、删除未知数据库、打印密钥 | 后续 `scripts/install/**` |

## 脚本声明

脚本顶部必须保留机器可读声明块。字段可按脚本实际情况取空数组，但不能省略：

```powershell
# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Starts local Docker dependencies from infra/docker-compose.dev.yml
#     - Recreates disposable database nerv_iip_iam_verify
#   Writes:
#     - artifacts/script-logs/**
#   Cleanup:
#     - Stops managed child process trees
#     - Leaves shared Docker services running unless -Cleanup is specified
#   Requires:
#     - PowerShell 7
#     - .NET SDK 10
#     - Docker Desktop
```

`Category` 可以是单一分类，也可以用逗号声明复合分类（例如 `verify, generate`）；所有分类项都必须属于 `check`、`verify`、`generate`、`release-install`。`SideEffects` 必须说清楚是否会删除、重建或写入数据库。`Writes` 必须覆盖生成产物、日志目录和临时文件。`Cleanup` 必须说明脚本结束后会清理什么，以及哪些外部依赖会被保留。

## Helper 契约

`scripts/lib/ScriptAutomation.ps1` 负责把长耗时和高风险命令包装成可诊断动作：

1. `Invoke-NativeCommandWithTimeout`：启动原生命令，记录命令名、参数摘要、cwd、timeout、exit code、duration、stdout/stderr 日志和 root PID。
2. `Invoke-DotNet`、`Invoke-Pnpm`、`Invoke-DockerCompose`、`Invoke-PwshScript`、`Invoke-Aspire`：领域化包装常用命令，避免脚本直接调用 `dotnet`、`pnpm`、`docker`、`pwsh` 或 Aspire CLI。
3. `Start-ManagedBackgroundProcess`：启动本地 Web 服务或长运行进程，返回 root PID、日志路径和 stop handle。
4. `Stop-ProcessTree`：基于 root PID 清理自有进程树；失败时输出残留 PID 和进程名。
5. `Use-ScopedEnvironmentVariable`：设置环境变量并在脚本结束时恢复原始状态，包括原本不存在、原本为空字符串和原本有值三种情况。
6. `Write-Diagnostic`：输出结构化诊断，默认脱敏 token、password、secret、connection string 和 authorization header。

helper 必须异步或文件重定向 stdout/stderr，避免子进程缓冲区阻塞。超时后必须先尝试温和停止，再强制清理自有进程树，并把 killed PID 写入诊断。

## 门禁规则

`scripts/check-script-governance.ps1` 使用 PowerShell parser/AST 检查脚本，而不是简单 grep。首批门禁规则：

1. 除 helper 和门禁脚本自身外，脚本必须 dot-source `scripts/lib/ScriptAutomation.ps1`。
2. 禁止直接调用 `dotnet`、`docker`、`pnpm`、`pwsh`、`powershell`、`Start-Job`、`Start-Process`、`Invoke-Expression`、`iex`。
3. 禁止使用 `[scriptblock]::Create`、`System.Diagnostics.Process.Start`、`cmd /c` 和未登记的动态 invocation。
4. 每个脚本必须包含 `Script-Governance` 声明块和有效 `Category`。
5. legacy exemption 必须指向具体脚本和具体规则，不能使用通配符豁免整个目录。

PSScriptAnalyzer 可以作为后续增强层，但不是当前唯一门禁；当前仓库必须能在没有额外全局模块安装的机器上运行 fast gate。

## 端口、数据库与容器

1. 固定端口必须先 preflight；端口占用时输出占用端口和建议处理，不盲目继续。
2. `verify` 数据库默认使用 `_verify` 或 run id 后缀，不使用客户库名、共享开发库名或生产库名。
3. 删除或重建 disposable database 前必须打印目标数据库名和 profile；release 脚本不得删除未知库。
4. Docker Compose 脚本必须声明是否保留共享依赖容器，建议提供 `-Cleanup` 或 `-KeepContainers` 参数。
5. 后台 Web 服务必须由 helper 启动，并在 finally 中清理进程树。
6. 并行全栈 session 必须用 session ID 同时绑定 manifest、动态 endpoint、进程身份、容器标签、专属卷和 artifact；不得按通用名称前缀清理，也不得自动执行 `aspire stop --all` 或 Docker prune。
7. 一次性 full-stack session 默认最多三个活动实例，不设置最低可用内存门槛。端口从 manifest 发现，每个 session 使用自己的 Aspire/DCP 代理，不维护共享 Nginx 路由表。
8. 自动化 `fullstack run` 无论成功或失败都必须进入 finally 清理运行资源，并保留 `artifacts/fullstack/<sessionId>/`；`fullstack gc` 只回收可以证明陈旧且属于本系统的 session。
9. 领导演示 `demo reset` 先校验机器本地 current-session pointer 与权威 manifest 的 worktree/所有权，再停止该精确 session；无权威 manifest 或所有权不匹配时必须失败，不得扩大到名称前缀、`aspire stop --all`、Docker prune 或共享卷删除。重置后的 seed 只能创建可重复的前置事实，禁止预制生产报工/完工、成品库存、检验结论/NCR/隔离/审批、发货、应收、遥测样本/报警事件或维修完工等最终态。

## 日志与诊断

1. 默认日志目录为 `artifacts/script-logs/<script-name>/<timestamp>/`。
2. 每个长耗时命令必须有独立 stdout/stderr 日志文件。
3. 脚本失败时输出最近失败命令、exit code、duration、log path、root PID 和 cleanup 结果。
4. release/install 脚本必须额外输出 release id、service、profile、target、migration from/to、seed step、correlation id 和诊断包位置。
5. 日志不得包含完整连接串、密码、token、client secret、authorization header 或客户密钥。
6. 领导演示的每次 `seed` / `health-check` 在成功或失败时均必须写入 `artifacts/leader-demo/<UTC-run-id>/evidence.json`，包含 commit/session/worktree、非敏感 profile、Aspire 资源状态、公开 HTTP 事实与链接、`Messaging Provider=Redis`、full-stack 诊断目录与精确 cleanup 命令；不得写入密码或 token。
7. 设备遥测模拟证据写入 `artifacts/leader-demo/<sessionId>/telemetry-simulator-<runId>-<UTC>.{json,md}`，至少包含历史回填 accepted/rejected-fallback 结论、连续样本数量、场景阶段、振动范围、公开 history/alarm 结果、重放身份一致性和 `backgroundProcessesCreated=0`；运行产物不提交仓库。

## 验证矩阵

| 层级 | 目的 | 典型命令 |
| --- | --- | --- |
| fast | 快速发现脚本解析、治理和无外部依赖测试问题 | `pwsh scripts/check-script-governance.ps1`、`git diff --check` |
| infra | 验证 Docker、本地依赖、真实 PostgreSQL profile、disposable database、现场连接断连和 opt-in 发布演练 | `pwsh scripts/verify-fourth-slice-real-infra.ps1`、`pwsh scripts/verify-fifth-slice-persistence-foundation.ps1`、`pwsh scripts/verify-iam-persistent-auth-foundation.ps1`、`pwsh scripts/verify-connector-health-disconnect.ps1 -Runs 3`、`pwsh scripts/verify-production-release-rehearsal.ps1 -Profile dependencies` |
| full | 串联 OpenAPI 导出、api-client 生成、前端质量门禁、后端和 Connector Host 回归；真实浏览器全栈使用一次性 session | `.\nerv.ps1 fullstack run -Scenario smoke`、`.\nerv.ps1 fullstack run -Scenario leader-demo-main-chain`、`pwsh scripts/verify-parallel-fullstack-isolation.ps1 -Sessions 2`、`pwsh scripts/verify-third-slice-console.ps1` |
| leader-demo | 重建隔离 PostgreSQL/Redis 演示 session，验证固定前置事实、公开 HTTP 查询、连续遥测、证据与精确清理 | `.\nerv.ps1 demo reset`、`.\nerv.ps1 demo health-check`、`pwsh scripts/verify-leader-demo-telemetry-simulator.ps1 -DurationMinutes 10 -HistoricalBackfill`、`.\nerv.ps1 demo stop`；停止后对同一 ID 执行 `.\nerv.ps1 fullstack stop -SessionId <sessionId>` 确认 `state=Stopped remaining=0`，再用 `fullstack status` 确认 `state=Stopped containers=0` |

## 跨平台兼容门禁

当前脚本基线是 PowerShell 7 `pwsh`，不是 Windows-only 的 Windows PowerShell。`pwsh` 可以在 Windows、macOS 和 Linux 上运行，但 Nerv-IIP 不得在没有实际证据时声明某个脚本已经完成 macOS/Linux 支持。

跨平台兼容门禁分三步推进：

1. `compat-fast`：在 macOS 或 Linux 环境运行 `pwsh scripts/check-script-governance.ps1`、`pwsh scripts/tests/check-script-governance.Tests.ps1` 和 `git diff --check`。
2. `compat-core-verify`：在 macOS 或 Linux 环境安装 PowerShell 7、.NET 10 SDK、Docker Compose v2 后，运行已经迁移到 helper 的核心验证脚本；首批目标是 `pwsh scripts/verify-iam-persistent-auth-foundation.ps1`。
3. `compat-release-install`：Linux 私有化安装不直接复用本地 `verify` 脚本。后续 `scripts/install/linux/**` Bash/systemd 入口必须满足同一套分类、副作用、日志、超时、清理和敏感信息脱敏契约。

仓库提供 `scripts/check-script-compatibility.ps1` 作为本地兼容门禁入口。默认必须在 macOS 或 Linux 上运行；`-AllowWindows -FastOnly` 只用于 Windows 本地 smoke，不可作为兼容性声明依据。脚本会将 OS、PowerShell、.NET SDK、执行命令、退出码和日志位置写入 `artifacts/script-logs/script-compatibility/**/evidence.json`；full 模式还会记录 Docker Compose 版本并运行核心 verify 脚本。

跨平台验证记录必须包含操作系统、PowerShell 版本、.NET SDK 版本、执行命令、退出码和诊断日志位置；`compat-core-verify` 还必须包含 Docker Compose 版本。未跑过 `compat-fast` 和对应核心验证脚本前，只能说“脚本按 `pwsh` 跨平台口径编写”，不能说“已支持 macOS/Linux”。

2026-05-18 的复核兼容证据记录在 `artifacts/script-logs/script-compatibility/20260518-000559-198/evidence.json`：Ubuntu 22.04.3 LTS、PowerShell 7.6.1、.NET SDK 10.0.300、Docker Compose 5.1.3、`isLinux: true`、`fastOnly: false`，并通过 `compat-fast` 和 `pwsh scripts/verify-iam-persistent-auth-foundation.ps1`。在 Codex 生成的 Windows linked worktree 中，WSL Git 需要临时设置 `GIT_DIR`、`GIT_COMMON_DIR`、`GIT_WORK_TREE`，并用 `core.autocrlf=true` 与 `core.filemode=false` 对齐 Windows 工作树，避免兼容门禁把行尾或文件模式差异误报为源码变更。

## 迁移清单

| 脚本 | 分类 | 当前治理状态 | 迁移要求 |
| --- | --- | --- | --- |
| `verify-iam-persistent-auth-foundation.ps1` | `verify` | 已迁移 | 使用 helper 执行 dotnet/docker/pwsh，输出超时日志和 scoped env 诊断；Ubuntu 22.04.3 `compat-core-verify` 已通过，证据路径为 `artifacts/script-logs/script-compatibility/20260518-000559-198/evidence.json`。 |
| `verify-fifth-slice-persistence-foundation.ps1` | `verify` | 已迁移 | 使用 helper 执行 Docker Compose、dotnet、solution tests 和 scoped PostgreSQL test environment；baseline exemption 已移除。 |
| `verify-fourth-slice-real-infra.ps1` | `verify` | 已迁移 | 使用 helper 执行 Docker Compose、PostgreSQL reset、AppHub/Ops profile tests 和嵌套第三阶段脚本；baseline exemption 已移除。 |
| `verify-third-slice-console.ps1` | `verify` + `generate` | 已受治理 | 允许调用已声明的 OpenAPI export/api-client generate step；继续把写入 OpenAPI 快照和 api-client 的副作用归到 generate 分类说明中。 |
| `verify-openapi-client-drift.ps1` | `verify` + `generate` | 已受治理 | CI 契约漂移门禁；使用 helper 调用 OpenAPI 导出、frontend install/api-client generation 和 git diff/status 检查，失败时输出 OpenAPI 快照与 generated api-client 差异。 |
| `verify-first-slice.ps1` | `verify` | 已迁移 | 管理本地服务进程和端口 preflight；baseline exemption 已移除。 |
| `verify-production-release-rehearsal.ps1` | `verify` | 已迁移 | 使用 helper 执行 Docker Compose disposable project、依赖 smoke、平台 health smoke 和默认清理；`platform-smoke` profile 明确使用 Development-only auto-migration 作为发布演练 smoke，不替代生产 migration bundle。 |
| `verify-business-performance-baseline.ps1` | `verify` | 已迁移 | 使用 helper 执行 .NET performance tests，写 machine-readable metrics JSONL/summary JSON，并支持全局或分场景阈值失败门禁。 |
| `verify-business-scheduling-scale-benchmark.ps1` | `verify` | 已受治理/真实 PostgreSQL | 固定生成 100/500/1000 张 APS Lite 订单并各运行三次，记录输入组装、约束检查、算法、PostgreSQL 持久化、总耗时、峰值内存、KPI、未排原因和稳定输出哈希；证据写入 `artifacts/script-logs/business-scheduling-scale-benchmark/<timestamp>/aps-lite-scale-benchmark.{json,md}`，仅声明确定性有限产能启发式能力，不声明全局最优。 |
| `verify-coding-rule-engine.ps1` | `verify` | 已迁移 | 使用 helper 执行 Coding engine focused tests、后端 solution build 和 frontend typecheck；不导出 OpenAPI 或写 generated api-client。 |
| `verify-connector-health-disconnect.ps1` | `verify` | 已受治理/真实环境 3/3 | 通过 fullstack session lifecycle 与受控 loopback Modbus simulator 验证 Host 仍有新 heartbeat 时的现场 `lost`、`disconnectedSinceUtc`、同端口恢复和 current manifest 的 never-sampled binding；逐轮 evidence 写入 `artifacts/script-logs/connector-health-disconnect/<timestamp>/evidence.json`，固定 10 秒 deadline。当前代码头的最近成功证据 `20260718T062424954Z/evidence.json` 为 3/3（端到端 3181/1213/1267 ms；现场检测 401/82/767 ms；检测后 Gateway 可见 2783/1132/501 ms；最大 3181 ms）；AppHost/DCP 启动前失败的尝试仅保留 diagnostics，不计入拔线轮次。 |
| `verify-leader-demo-telemetry-simulator.ps1` | `verify` | 已受治理/前台真实栈 | 对当前精确 leader-demo session 以默认 2 秒周期发布 `normal -> degrading -> alarm -> recovered` 的振动、温度和设备状态，只使用公开 BusinessGateway；可选 24 小时形状历史回填先做迟到事实实测，重复 run 以稳定 source sequence 验证幂等。证据写入 `artifacts/leader-demo/<sessionId>/`，不创建后台进程。 |
| `bootstrap-online.ps1` | `release-install` | 已迁移 | 有网空白机器入口；使用 helper 执行 winget、Aspire install script、dotnet restore、pnpm install、AppHost build 和可选 dev 启动；只初始化本地 Development user-secrets，不承担离线包制作或客户现场服务注册。 |
| `install/migrate-file-storage.ps1` | `release-install` | 已受治理 | 只从当前进程的 `NERV_IIP_FILE_STORAGE_DB` 读取目标连接，默认校验目标库精确匹配 `nerv_iip_filestorage`（受控自定义名称必须显式传 `-ExpectedDatabase`），输出脱敏 release/service/profile/target/migration/correlation/log 状态并应用 FileStorage EF migrations；不负责备份、删库或 seed，PoC/production 调用前必须完成 database release runbook preflight。 |
| `export-gateway-openapi.ps1` | `generate` | legacy exemption | 仍在 `scripts/script-governance-baseline.json` 中豁免 `MissingHelper`、`ForbiddenCommand`、`DynamicInvocation` 和 `ForbiddenProcessStart`；迁移时需声明写入 OpenAPI 快照和服务启动副作用。 |
| `verify-second-slice-ops.ps1` | `verify` | legacy exemption | 仍在 `scripts/script-governance-baseline.json` 中豁免直接命令/进程调用；迁移时需收敛 Gateway/Ops/Connector Host 进程树、日志和端口清理。 |

当前脚本治理 baseline 只保留 `scripts/export-gateway-openapi.ps1` 与 `scripts/verify-second-slice-ops.ps1` 两个 legacy exemption；新增脚本不得复用该例外口径。

## 新脚本准入

新增脚本合入前至少满足：

1. 有 `Script-Governance` 声明块。
2. 分类准确，副作用写清楚。
3. 高风险命令通过 helper。
4. fast gate 通过。
5. 涉及数据库、容器、端口或生成产物时，同步更新对应架构文档或 runbook。
