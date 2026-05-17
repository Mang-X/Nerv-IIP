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

## 分类矩阵

| 分类 | 允许行为 | 禁止行为 | 示例 |
| --- | --- | --- | --- |
| `check` | 解析脚本、静态门禁、build/test/typecheck、格式检查、无外部依赖的单元测试 | 启动长期服务、删除数据库、写生成代码、修改业务配置 | `check-script-governance.ps1` |
| `verify` | 启动本地依赖、容器、临时 Web 服务、disposable database，运行端到端验收 | 连接客户数据环境、写未声明产物、使用生产库名、失败后留下自有进程 | `verify-iam-persistent-auth-foundation.ps1` |
| `generate` | 导出 OpenAPI、生成 api-client、写入声明过的 generated/openapi 文件 | 伪装成只读验证、手改生成产物、绕过后端契约测试 | `export-gateway-openapi.ps1` |
| `release-install` | 环境检查、配置生成、备份证据、migration bundle、seed、服务注册、健康检查、诊断包 | 直接拼 SQL 写业务表、使用 `EnsureCreated()`、默认测试密码、删除未知数据库、打印密钥 | 后续 `scripts/install/**` |

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

`SideEffects` 必须说清楚是否会删除、重建或写入数据库。`Writes` 必须覆盖生成产物、日志目录和临时文件。`Cleanup` 必须说明脚本结束后会清理什么，以及哪些外部依赖会被保留。

## Helper 契约

`scripts/lib/ScriptAutomation.ps1` 负责把长耗时和高风险命令包装成可诊断动作：

1. `Invoke-NativeCommandWithTimeout`：启动原生命令，记录命令名、参数摘要、cwd、timeout、exit code、duration、stdout/stderr 日志和 root PID。
2. `Invoke-DotNet`、`Invoke-Pnpm`、`Invoke-DockerCompose`、`Invoke-PwshScript`：领域化包装常用命令，避免脚本直接调用 `dotnet`、`pnpm`、`docker` 或 `pwsh`。
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

## 日志与诊断

1. 默认日志目录为 `artifacts/script-logs/<script-name>/<timestamp>/`。
2. 每个长耗时命令必须有独立 stdout/stderr 日志文件。
3. 脚本失败时输出最近失败命令、exit code、duration、log path、root PID 和 cleanup 结果。
4. release/install 脚本必须额外输出 release id、service、profile、target、migration from/to、seed step、correlation id 和诊断包位置。
5. 日志不得包含完整连接串、密码、token、client secret、authorization header 或客户密钥。

## 验证矩阵

| 层级 | 目的 | 典型命令 |
| --- | --- | --- |
| fast | 快速发现脚本解析、治理和无外部依赖测试问题 | `pwsh scripts/check-script-governance.ps1`、`git diff --check` |
| infra | 验证 Docker、本地依赖、真实 PostgreSQL profile 和 disposable database | `pwsh scripts/verify-fourth-slice-real-infra.ps1`、`pwsh scripts/verify-fifth-slice-persistence-foundation.ps1`、`pwsh scripts/verify-iam-persistent-auth-foundation.ps1` |
| full | 串联 OpenAPI 导出、api-client 生成、前端质量门禁、后端和 Connector Host 回归 | `pwsh scripts/verify-third-slice-console.ps1`、后续总验收脚本 |

## 跨平台兼容门禁

当前脚本基线是 PowerShell 7 `pwsh`，不是 Windows-only 的 Windows PowerShell。`pwsh` 可以在 Windows、macOS 和 Linux 上运行，但 Nerv-IIP 不得在没有实际证据时声明某个脚本已经完成 macOS/Linux 支持。

跨平台兼容门禁分三步推进：

1. `compat-fast`：在 macOS 或 Linux 环境运行 `pwsh scripts/check-script-governance.ps1`、`pwsh scripts/tests/check-script-governance.Tests.ps1` 和 `git diff --check`。
2. `compat-core-verify`：在 macOS 或 Linux 环境安装 PowerShell 7、.NET 10 SDK、Docker Compose v2 后，运行已经迁移到 helper 的核心验证脚本；首批目标是 `pwsh scripts/verify-iam-persistent-auth-foundation.ps1`。
3. `compat-release-install`：Linux 私有化安装不直接复用本地 `verify` 脚本。后续 `scripts/install/linux/**` Bash/systemd 入口必须满足同一套分类、副作用、日志、超时、清理和敏感信息脱敏契约。

仓库提供 `scripts/check-script-compatibility.ps1` 作为本地兼容门禁入口。默认必须在 macOS 或 Linux 上运行；`-AllowWindows -FastOnly` 只用于 Windows 本地 smoke，不可作为兼容性声明依据。脚本会将 OS、PowerShell、.NET SDK、Docker Compose、执行命令、退出码和日志位置写入 `artifacts/script-logs/script-compatibility/**/evidence.json`。

跨平台验证记录必须包含操作系统、PowerShell 版本、.NET SDK 版本、Docker Compose 版本、执行命令、退出码和诊断日志位置。未跑过 `compat-fast` 和对应核心验证脚本前，只能说“脚本按 `pwsh` 跨平台口径编写”，不能说“已支持 macOS/Linux”。

2026-05-17 的首轮兼容证据记录在 `artifacts/script-logs/script-compatibility/20260517-233939-907/evidence.json`：Ubuntu 22.04.3 LTS、PowerShell 7.6.1、.NET SDK 10.0.300、Docker Compose 5.1.3、`isLinux: true`、`fastOnly: false`，并通过 `compat-fast` 和 `pwsh scripts/verify-iam-persistent-auth-foundation.ps1`。在 Codex 生成的 Windows linked worktree 中，WSL Git 需要临时设置 `GIT_DIR`、`GIT_COMMON_DIR`、`GIT_WORK_TREE`，并用 `core.autocrlf=true` 与 `core.filemode=false` 对齐 Windows 工作树，避免兼容门禁把行尾或文件模式差异误报为源码变更。

## 迁移清单

| 脚本 | 分类 | 当前治理状态 | 迁移要求 |
| --- | --- | --- | --- |
| `verify-iam-persistent-auth-foundation.ps1` | `verify` | 已迁移 | 使用 helper 执行 dotnet/docker/pwsh，输出超时日志和 scoped env 诊断；Ubuntu 22.04.3 `compat-core-verify` 已通过，证据路径为 `artifacts/script-logs/script-compatibility/20260517-233939-907/evidence.json`。 |
| `verify-fifth-slice-persistence-foundation.ps1` | `verify` | 已迁移 | 使用 helper 执行 Docker Compose、dotnet、solution tests 和 scoped PostgreSQL test environment；baseline exemption 已移除。 |
| `verify-fourth-slice-real-infra.ps1` | `verify` | 已迁移 | 使用 helper 执行 Docker Compose、PostgreSQL reset、AppHub/Ops profile tests 和嵌套第三阶段脚本；baseline exemption 已移除。 |
| `verify-third-slice-console.ps1` | `verify` + `generate` | 需拆分或显式声明混合副作用 | OpenAPI 导出和 api-client 生成应由 `generate` 脚本承载，verify 只调用声明过的 generate step。 |
| `export-gateway-openapi.ps1` | `generate` | 待迁移 | 声明写入 OpenAPI 快照和服务启动副作用。 |
| `verify-first-slice.ps1` | `verify` | 待迁移 | 管理本地服务进程和端口 preflight。 |
| `verify-second-slice-ops.ps1` | `verify` | 待迁移 | 管理 Gateway/Ops/Connector Host 进程树和日志。 |

## 新脚本准入

新增脚本合入前至少满足：

1. 有 `Script-Governance` 声明块。
2. 分类准确，副作用写清楚。
3. 高风险命令通过 helper。
4. fast gate 通过。
5. 涉及数据库、容器、端口或生成产物时，同步更新对应架构文档或 runbook。
