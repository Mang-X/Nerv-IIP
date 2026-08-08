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
11. MAN-519 领导演示环境必须使用 `.\nerv.ps1 demo start|reset|seed|health-check|stop`；密码仅从当前 PowerShell 进程的 `NERV_IIP_LEADER_DEMO_ADMIN_PASSWORD`（及可选的 PDA 演示工人口令 `NERV_IIP_LEADER_DEMO_WORKER_PASSWORD`）读取，运行 profile 强制断言 Redis，reset/stop 只清理本地 pointer 授权的精确 full-stack session。PDA 演示现场态数据在 reset 后用 `scripts/generate-pda-demo-fieldwork.ps1` 重灌：只经公开 BusinessGateway facade 写业务事实、幂等可重跑，密码同样仅从进程 env 读取。
12. MAN-601 设备遥测模拟使用 `pwsh scripts/verify-leader-demo-telemetry-simulator.ps1 -DurationMinutes 10 -HistoricalBackfill`；领导演示当天应先执行 `.\nerv.ps1 demo reset` 重建 session（旧 tag/rule 前置事实不原地升级），并按演示窗口显式使用 `-DurationMinutes 30` 等足够时长。脚本只解析当前 leader-demo pointer 指向的精确 Running session，只经公开 BusinessGateway `/telemetry/samples` 写入事实，并通过公开 history/alarm facade 验证。模拟器在前台有界运行，不创建后台进程；同一 `RunId + ScenarioStartUtc` 产生相同 source sequence/payload。`-ReplayExisting` 还会从同一 session 的已完成 real-time 证据读取并逐字段校验 duration、sample interval、三个 phase transition、历史开关/小时/间隔，任一不同即拒绝；校验通过后跳过样本间实时等待，并以默认每次公开 POST 300 ms 的受控节奏重放整条时间线。普通 24h 历史回填同样默认按每 POST 300 ms 节奏发布，分别可通过最小值为 250 ms 的 `-ReplayRequestIntervalMilliseconds` / `-HistoricalRequestIntervalMilliseconds` 调整，避免绕过 BusinessGateway 固定窗口限流。历史时间戳先实测，只有 HTTP 400/422 业务拒绝才降级；鉴权、限流、路由、5xx 或传输故障直接失败。fallback 起点为 `max(session.createdAtUtc, scenarioStart-5min)`，保证短窗事实属于当前 session；不得直写 historian。

## 分类矩阵

| 分类 | 允许行为 | 禁止行为 | 示例 |
| --- | --- | --- | --- |
| `check` | 解析脚本、静态门禁、build/test/typecheck、格式检查、无外部依赖的单元测试 | 启动长期服务、删除数据库、写生成代码、修改业务配置 | `check-script-governance.ps1` |
| `verify` | 启动本地依赖、容器、临时 Web 服务、disposable database，运行端到端验收 | 连接客户数据环境、写未声明产物、使用生产库名、失败后留下自有进程 | `verify-iam-persistent-auth-foundation.ps1` |
| `generate` | 导出 OpenAPI、生成 api-client、写入声明过的 generated/openapi 文件 | 伪装成只读验证、手改生成产物、绕过后端契约测试 | `export-gateway-openapi.ps1` |
| `release-install` | 环境检查、配置生成、备份证据、migration bundle、seed、服务注册、健康检查、诊断包 | 直接拼 SQL 写业务表、绕过 EF migrations history 建表、默认测试密码、删除未知数据库、打印密钥 | 后续 `scripts/install/**` |
| `library` | 被 dot-source 进调用方作用域的共享库；只提供函数，不作为程序被执行 | 作为入口脚本被直接运行、在 `scripts/lib/` 之外声明该分类 | `scripts/lib/TestEvidence.ps1` |

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

`Category` 可以是单一分类，也可以用逗号声明复合分类（例如 `verify, generate`、`library, generate`）；所有分类项都必须属于 `check`、`verify`、`generate`、`release-install`、`library`。`library` 只能由 `scripts/lib/` 下的文件声明，且该目录下的文件**必须**声明它（见下面的扫描边界）。`SideEffects` 必须说清楚是否会删除、重建或写入数据库。`Writes` 必须覆盖生成产物、日志目录和临时文件。`Cleanup` 必须说明脚本结束后会清理什么，以及哪些外部依赖会被保留。

## Helper 契约

`scripts/lib/ScriptAutomation.ps1` 负责把长耗时和高风险命令包装成可诊断动作：

1. `Invoke-NativeCommandWithTimeout`：启动原生命令，记录命令名、参数摘要、cwd、timeout、exit code、duration、stdout/stderr 日志和 root PID。
2. `Invoke-DotNet`、`Invoke-Pnpm`、`Invoke-DockerCompose`、`Invoke-PwshScript`、`Invoke-Aspire`：领域化包装常用命令，避免脚本直接调用 `dotnet`、`pnpm`、`docker`、`pwsh` 或 Aspire CLI。其中 `Invoke-Pnpm` 统一经 `Resolve-PnpmInvocation` 规约进程 cwd：参数中的 `-C`/`--dir` 会被对齐为进程工作目录（行为等价），未显式传 `WorkingDirectory` 时默认以 `<repoRoot>/frontend` 为 cwd——corepack 按“进程 cwd 就近 `package.json` 的 `packageManager` 字段”解析 pnpm 版本，仓库根目录没有 `package.json`，从根目录 cwd 调用会拉取最新 pnpm 并因与 `frontend/` 锁定版本不一致直接失败。
3. `Start-ManagedBackgroundProcess`：启动本地 Web 服务或长运行进程，返回 root PID、日志路径和 stop handle。
4. `Stop-ProcessTree`：基于 root PID 清理自有进程树；失败时输出残留 PID 和进程名。
5. `Use-ScopedEnvironmentVariable`：设置环境变量并在脚本结束时恢复原始状态，包括原本不存在、原本为空字符串和原本有值三种情况。
6. `Write-Diagnostic`：输出结构化诊断，默认脱敏 token、password、secret、connection string 和 authorization header。

helper 必须异步或文件重定向 stdout/stderr，避免子进程缓冲区阻塞。超时后必须先尝试温和停止，再强制清理自有进程树，并把 killed PID 写入诊断。

## 门禁规则

`scripts/check-script-governance.ps1` 使用 PowerShell parser/AST 检查脚本，而不是简单 grep。首批门禁规则：

1. 入口脚本必须 dot-source `scripts/lib/ScriptAutomation.ps1`；helper 自身、门禁脚本自身与 `scripts/lib/` 下的库不适用（库的规则差异见下面「`scripts/lib` 的治理扫描边界」）。
2. 禁止直接调用 `dotnet`、`docker`、`pnpm`、`pwsh`、`powershell`、`Start-Job`、`Start-Process`、`Invoke-Expression`、`iex`。
3. 禁止使用 `[scriptblock]::Create`、`System.Diagnostics.Process.Start`、`cmd /c` 和未登记的动态 invocation。
4. 每个脚本必须包含 `Script-Governance` 声明块和有效 `Category`。
5. legacy exemption（`scripts/script-governance-baseline.json`）必须指向具体脚本和具体规则，不能使用通配符豁免整个目录。扫描边界（`$scanExclusions`）是另一回事：它决定「哪些文件进入扫描」，穷举为三条并被契约测试逐字锁定，见下节。

PSScriptAnalyzer 可以作为后续增强层，但不是当前唯一门禁；当前仓库必须能在没有额外全局模块安装的机器上运行 fast gate。

## `scripts/lib` 的治理扫描边界（#1509 裁决）

原先 `check-script-governance.ps1` 把 `scripts/lib/*` 整体排除出默认扫描，结果是**全仓库影响面最大的一批文件反而完全不受管**：`ForbiddenCommand`、`DynamicInvocation`、`ForbiddenProcessStart`，乃至 `ParseError` 都对库文件不生效。#1509 的裁决是**收窄排除范围，让库进入扫描**，而不是把「库不受管」写成文档。

### 排除清单（穷举，只有三条）

| 排除项 | 理由 |
| --- | --- |
| `scripts/check-script-governance.ps1` | 门禁脚本本身：它把每个被禁命令名当字面量列出，无法做自己的被检对象。 |
| `scripts/lib/ScriptAutomation.ps1` | 所有规则指向的那个 wrapper。`ForbiddenCommand`/`DynamicInvocation` 存在的目的就是把调用方赶进这个文件，对它本身施加即构造性循环。 |
| `scripts/tests/*` | 测试脚本要把被治理的程序当**真进程**跑，并刻意构造非法 fixture；这两件事都是测试的目的，不是治理发现。 |

清单是 `check-script-governance.ps1` 里一份具名数据（`$scanExclusions`），不是内联布尔链——`scripts/tests/script-governance-scan-boundary.Tests.ps1` 逐字断言这三条，因此**放宽边界必然改到一条被命名的契约**，而不是改一处没人看的 `Where-Object`。

**「穷举三条」说的是扫描边界，不是全部放宽杠杆**（#1509 二轮走查）。还有第四条独立的放宽通道：`scripts/script-governance-baseline.json` 的 `exemptions`——它不改变哪些文件被扫，而是逐「文件 × 规则」豁免已扫出的发现，且**不被上述三条守卫覆盖**（`script-governance-scan-boundary.Tests.ps1` 的镜像树里没有 baseline 文件，checker 缺文件即返回空豁免表，所以那些用例始终按「零 exemption」判定）。风险有限：豁免必须逐文件逐规则写明，不接受通配路径，改动落在被跟踪的 JSON 里可 review。但它确实存在，别把「三条」读成「全部」。

### 库作用域（library scope）

`scripts/lib/` 下的文件按 library scope 扫描。规则差异只有两条，其余（`ParseError`、`MissingGovernanceHeader`、`MissingCategory`/`InvalidCategory`、`ForbiddenCommand`、`ForbiddenDynamicScriptBlock`、`ForbiddenProcessStart`）与入口脚本完全一致：

1. **`MissingHelper` 不适用。** 库是被 dot-source 进调用方作用域的，调用方已经加载了 wrapper；而且 `BackendTestShardSelectors.ps1`、`CiWorkflowBudgets.ps1` 这类库根本不调用任何外部进程，强塞一个用不到的 import 买不到任何东西。这条规则真正要防的「库绕过 wrapper 直接 shell out」并没有失守：`ForbiddenCommand`、`ForbiddenProcessStart` 和下面收窄后的 `DynamicInvocation` 在库作用域全部生效，而确实要 shell out 的库（`BackendTestShardTimings.ps1`、`FullStackSessionRuntime.ps1`）为自己的需要本来就 dot-source 了 wrapper。
2. **`DynamicInvocation` 收窄为「注入式 action seam」。** 库可以 `& $Action`，条件是该变量在**本次调用所在的作用域链上可证明是 script block**：要么是 `[scriptblock]` 类型的参数，要么是被 `{ … }` 字面量（含 `.GetNewClosure()`）赋值的变量。作用域单位是一个 `ScriptBlockAst`（函数体或 `{ … }` 字面量），查找沿包围链向外走——与 PowerShell 自身的作用域一致：外层的 seam 对内层可见，**兄弟函数的同名变量永远不可见**，且**内层一旦自己绑定同名变量就遮蔽外层**（最内层绑定说了算，与运行期解析一致：函数体里写 `$x = 'dotnet'` 就是一个局部，文件级的 `$x = { … }` 替它作不了保）。这一条是刻意的：若按整文件收集，A 函数里的 `$action = { }` 就会替 B 函数里的 `$action = 'dotnet'; & $action` 放行，等于用一个流行参数名重新打开这条规则要堵的洞。这正是本仓库构造可测试库所依赖的注入 seam（见 `AGENTS.md` 后端测试确定性一节）。`& 'dotnet'`、`& "$exe"`、`& (Get-Command …)`、`& $stringVariable` 一律仍是违规——最后一种是 `ForbiddenCommand` 在结构上看不见的那个洞，所以这条规则必须留在库作用域，而不是整体豁免。

   **参数的两种拼写按同一个作用域判定。** PowerShell 的 `function Foo { param(...) }` 与 `function Foo(...)` 运行期语义完全相同，但 AST 不同：内联参数表挂在 `FunctionDefinitionAst` 上、位于函数体**之外**，因此「向外找最近的 `ScriptBlockAst`」会把它算到**文件级**、对所有兄弟函数可见。#1509 二轮实测到这个洞（内联拼写 exit 0，`param()` 拼写 exit 1）。现在两种拼写都归属函数体，`inline-parameter-cross-function-leak` 与 `cross-function-parameter-leak` 各钉一种。

library scope 是**声明出来的**，不只是从路径推断：`scripts/lib/` 下的文件必须在自己的 header 里写 `Category: library`（否则报 `MissingLibraryCategory`），而目录之外的文件不能通过贴这个标签来蹭上述两条放宽（否则报 `InvalidCategory`）。放宽写在被放宽者自己的头部，改动可见。

### 可执行守卫

裁决的每一条都有会红的对照，全部在 `scripts/tests/script-governance-scan-boundary.Tests.ps1`（从 `check-script-governance.Tests.ps1` 拆出来的独立文件，见下面「守卫落在哪里」）：

- 排除清单逐字断言 → 往里加 `scripts/lib/*` 直接红；
- 在 `scripts/lib/` 下临时种一个违规库文件，**默认扫描**（不带 `-Path`）必须报出它 → 边界被行为性放宽时红；
- seam 调用放行 / `& 'dotnet'` / `& $exe`（字符串变量）/ 直呼 `dotnet` / `Process::Start` / 未声明 `library` 分类 / 目录外冒充 `library` / 跨函数同名变量不放行逐条对照 → 任何一条规则被削弱或过度收紧都红；
- 参数两种拼写各一条（`cross-function-parameter-leak` 钉 `param()` 块、`inline-parameter-cross-function-leak` 钉内联参数表），外加 `inline-parameter-seam-allowed` 防止「靠丢掉内联参数」来假装收紧；
- 遮蔽语义两条：`file-level-seam-visible-to-inner-scope`（外层 seam 对不重绑的内层仍可见，防止「干脆不向外走」的假收紧）与 `file-level-seam-shadowed-by-local`（内层重绑同名变量后外层 seam 失效）。

**守卫落在哪里**：该文件是 CI `Script Governance` job 的一步（`.github/workflows/ci.yml`），同时进入本节末尾的 `compat-fast` 清单与 `scripts/check-script-compatibility.ps1`。它从 `check-script-governance.Tests.ps1` 拆出来，是因为后者还要驱动 ScriptAutomation 的流排空与游离进程夹具，比一条边界契约重得多。

**夹具不落在被跟踪目录**：所有 library scope 用例都在临时目录里搭一棵 `<temp>/scripts/` 镜像树（把 checker 原文件复制进去），在那棵树里种违规夹具并跑默认扫描。checker 的 repo root 是 `$PSScriptRoot/..`，因此镜像树里的 `scripts/lib/x.ps1` 走的正是同一套 `$scanExclusions` 与 library scope 判定；而进程被 kill 或 CI step 超时后，残留只会留在临时目录，不会让此后每次治理门禁变红，也不会污染并行 worktree 的工作树。

### 已知残余

**同一作用域内先后重新赋值**：`& $x` 与 `$x` 的真实内容仍受运行时决定，若有人在**同一个作用域**里写 `$x = { … }` 之后再改赋成字符串，AST 层面无法判否。该残余被接受，因为它需要一次刻意的、可见的重新赋值，而更常见的绕过路径（直呼命令、字面量调用、字符串变量调用、借用兄弟函数的同名变量、借用文件级同名变量）都已被上面的规则堵死。

这条残余的边界是**同一个作用域**，不是文件、也不是作用域链：跨函数借名（两种参数拼写都算）与「外层写 seam、内层改赋成字符串」都不在残余里，它们分别是被 `cross-function-name-collision` / `cross-function-parameter-leak` / `inline-parameter-cross-function-leak` / `file-level-seam-shadowed-by-local` 钉死的违规。措辞与实现在这一点上必须一致——文档承诺的强度就是实现的强度。

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

1. `compat-fast`：在 macOS 或 Linux 环境运行 `pwsh scripts/check-script-governance.ps1`、`pwsh scripts/tests/check-script-governance.Tests.ps1`、`pwsh scripts/tests/script-governance-scan-boundary.Tests.ps1`、`pwsh scripts/tests/test-evidence.Tests.ps1` 和 `git diff --check`。
2. `compat-core-verify`：在 macOS 或 Linux 环境安装 PowerShell 7、.NET 10 SDK、Docker Compose v2 后，运行已经迁移到 helper 的核心验证脚本；首批目标是 `pwsh scripts/verify-iam-persistent-auth-foundation.ps1`。
3. `compat-release-install`：Linux 私有化安装不直接复用本地 `verify` 脚本。后续 `scripts/install/linux/**` Bash/systemd 入口必须满足同一套分类、副作用、日志、超时、清理和敏感信息脱敏契约。

仓库提供 `scripts/check-script-compatibility.ps1` 作为本地兼容门禁入口。默认必须在 macOS 或 Linux 上运行；`-AllowWindows -FastOnly` 只用于 Windows 本地 smoke，不可作为兼容性声明依据。脚本会将 OS、PowerShell、.NET SDK、执行命令、退出码和日志位置写入 `artifacts/script-logs/script-compatibility/**/evidence.json`；full 模式还会记录 Docker Compose 版本并运行核心 verify 脚本。

跨平台验证记录必须包含操作系统、PowerShell 版本、.NET SDK 版本、执行命令、退出码和诊断日志位置；`compat-core-verify` 还必须包含 Docker Compose 版本。未跑过 `compat-fast` 和对应核心验证脚本前，只能说“脚本按 `pwsh` 跨平台口径编写”，不能说“已支持 macOS/Linux”。

2026-05-18 的复核兼容证据记录在 `artifacts/script-logs/script-compatibility/20260518-000559-198/evidence.json`：Ubuntu 22.04.3 LTS、PowerShell 7.6.1、.NET SDK 10.0.300、Docker Compose 5.1.3、`isLinux: true`、`fastOnly: false`，并通过 `compat-fast` 和 `pwsh scripts/verify-iam-persistent-auth-foundation.ps1`。在 Codex 生成的 Windows linked worktree 中，WSL Git 需要临时设置 `GIT_DIR`、`GIT_COMMON_DIR`、`GIT_WORK_TREE`，并用 `core.autocrlf=true` 与 `core.filemode=false` 对齐 Windows 工作树，避免兼容门禁把行尾或文件模式差异误报为源码变更。

## 迁移清单

| 脚本 | 分类 | 当前治理状态 | 迁移要求 |
| --- | --- | --- | --- |
| `collect-test-evidence.ps1` | `check` + `generate` | 已受治理 | 读取 job-local raw TRX，执行 skip/zero-execution 门禁，并只写声明过的脱敏 evidence tree、Step Summary 与确定性 failure sibling；artifact writer 只消费已解析 records/summary，不接收或复制 raw TRX path；CI Script Governance 直接执行其语义契约测试。 |
| `generate-test-evidence-baseline.ps1` | `generate` | 已受治理 | committed evidence snapshot 的唯一写入口；只接受 EvidenceRoot authority 或只读 GitHub Actions console provenance，禁止手改该文件。**#1507 起该 snapshot 是离线兜底而不是治理资产**：重生成属可选维护，任何拓扑/宿主变更都不再欠一次刷新，也没有任何门禁以它的覆盖面为判据（主耗时来源是 `update-backend-test-shard-timings.ps1` 的自动缓存）。runner 环境**按 lane 逐条**从各自 summary 读入并写进 `source.laneProvenance`（baseline schema 2），绝不取 `$first` 的值冒充整份 baseline —— 拆分理由见 `test-evidence-governance.md` 的「Run identity versus per-job environment」。 |
| `scripts/lib/TestEvidence.ps1` | `check` library | 已受治理 | 提供 TRX 解析、policy、摘要/脱敏 artifact、provenance 与 baseline 纯函数；quarantine metadata 的 policy/runtime 两调用点复用同一纯校验，runner normalization 读取显式 regex result，不依赖 PowerShell 自动 `$Matches`；provenance 分「run 身份」（8 字段跨 lane 严格等值）与「per-job 环境」（`runnerOs`/`runnerImage`/`dotnetSdk`，只逐条校形、按 lane 记录），`Assert-NervEvidenceSourceSummaries` 只返回收窄后的 run 身份对象，调用方拿不到任何 per-job 字段；`source.laneProvenance` 的 lane 集合必须与 baseline 记录的 assemblies lane 集合**双向精确相等**（缺 lane、多 lane、重复 lane 全拒），每行 `jobName` 按 `Get-NervTestEvidenceLaneJobs` 允许表逐 lane 复核（不得为空、臆造或借用兄弟 lane）；读取侧只接受 baseline `schemaVersion` 1 或 2，其余（含缺失）降级为 `unsupported-baseline-schema-version` 的 report-only 不可用而非照常比较；**耗时比较键自 #1507 起是「程序集」单键**（lane 仍作为 provenance 留在行上，只在同一程序集出现两行时用来消歧，两行都不属当前 lane 则报 `ambiguous-assembly-in-baseline` 而不是随便挑一行），因此换片、改宿主都不再失键；调用方必须先加载 `ScriptAutomation.ps1`。 |
| `scripts/lib/CiWorkflowBudgets.ps1` | `check` library | 已受治理 | MAN-799 CI timeout 预算不变量的纯读取与校验函数；结构化读取 `.github/workflows/ci.yml`，step 数与原文交叉核对不上、或 job header 读不出时直接抛错（fail closed），绝不静默返回“零违规”；tier A/B 分档同样 fail closed——`if:` 只要可能在失败后仍运行（`always()`/`!cancelled()`/`failure()` 的任意合法写法，含 `${{ }}` 包裹、复合表达式、尾随注释），或无法判定，一律按 tier A 处理；`needs:`/`strategy.matrix` 等 job 级序列不计入 step 数；由 `scripts/tests/test-evidence.Tests.ps1` 调用，因此随 Script Governance job 一起在 CI 执行。 |
| `scripts/tests/test-evidence.Tests.ps1` | `check` | 已受治理/CI 接线 | fixture 证明三项硬门禁、双 SHA、baseline authority、selected-lane/shard 语义、脱敏与 normalized roundtrip，并锁定无 raw-path writer 参数、Ubuntu major normalization、quarantine 到期边界与两调用点错误契约；由 Script Governance job 和 `compat-fast` 执行并保留真实退出码。 |
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
| `check-backend-test-determinism.ps1` | `check` | 已受治理 | MAN-662 后端测试确定性静态门禁；扫描 solution 内全部测试源，按 `backend/test-determinism-baseline.json`（schema 2）逐行核对已登记发现（`Task.Delay` / `StaticSetter` / `UnreachableAddress`），未登记发现即失败。每行必须声明 `classification`：`expiring-debt` 到期即失败；`permanent`（#1471 起）不带到期日，仅限脚本参数 `-PermanentAllowlist` 默认值列出的 `路径=pattern` 条目（受审计原语自身的实现与自测），必填 `rationale` 且禁带债务元数据；白名单锁到 pattern 一级，同一文件里的其它 pattern 不被顺带放行。该参数只是 checker 自测 harness 的接缝，CI 一律用默认值调用。扫描范围含 `backend/common/Testing/**`（共享测试基建），该目录在 solution 里找不到项目即失败。只读扫描，不启动服务、不写 `artifacts/`；CI 在 Script Governance job 中执行一次，不重复接入后端测试 job。 |
| `verify-backend-test-determinism.ps1` | `verify` | 已受治理 | 对四个目标测试程序集执行六轮 × 四项目：seed `man662-01`..`man662-06`、serial/parallel 交替、`MaxParallelThreads` 1/4、项目顺序逐轮旋转。以 `New-ExclusiveInvocationClaim` 原子取得 invocation 所有权，既有证据永不被 rerun 覆盖；除退出码外还跨轮比对每个项目的 total/passed/skipped/failed，静默跳过即判失败。证据写入 `artifacts/test-determinism/man-662/<invocation-id>/summary.json`（六个本地复现字段 + 逐项目结果），不产出 TRX、lane timing 或 flake trend——那些属 MAN-661。本机执行，不进 CI。 |
| `tests/check-backend-test-determinism.Tests.ps1` | `check` | 已受治理 | 用 `scripts/tests/fixtures/backend-test-determinism/**` 夹具回归扫描器本身：普通/逐字/raw/嵌套插值字符串中的可执行表达式都必须参与扫描，脱敏值不得尾部泄漏。另覆盖 schema 2 的分类规则——permanent 只在白名单内通过、白名单未覆盖的 pattern（同路径）必须失败而写明该 pattern 后必须通过、白名单条目格式错误或 pattern 不受支持必须失败、用默认白名单校验同一行必须失败（白名单一旦被放宽即变红）、permanent 带债务元数据或缺 `rationale` 失败、未知/缺失 `classification` 失败、debt 行带 `rationale` 失败。CI Script Governance job 执行。 |
| `tests/verify-backend-test-determinism.Tests.ps1` | `check` | 已受治理 | 用一次性 stubbed harness（复用真实 helper，只替换进程启动面）验证六轮契约、caller cwd 保护、invocation claim 的双进程原子性、已被占用 ID 时零项目执行，以及"退出码全零但测试结果漂移"必须失败。不调用真实 `dotnet test`。CI Script Governance job 执行。 |
| `verify-backend-test-shards.ps1` | `check` | 已受治理 | MAN-669 后端快速分片治理；校验 `scripts/backend-test-shards.json` 把每个后端测试项目恰好分类一次、solution filter 与清单逐项一致、排除选择器唯一归属 heavy lane、**每条排除都能在 MAN-661 `test-evidence-policy.json` 中找到 environment-gated 真实依赖 skip 登记**（排除清单不得成为私自绕过默认门禁的口子）、`excludedTestLanes` 与这些登记的 `requiredLane` 派生结果逐项相等、方法选择器不得是同源文件中其它成员的前缀，并用结构化 YAML 解析核对 `ci.yml`：四个 shard job 的名称/`evidenceLane`/原始 TRX 目录/TRX 前缀、MAN-661 证据采集参数（`-Lane`/`-SelectedLanes`/`-JobName`/`-CurrentTestOutcome`/`-RetentionDays 14`）、唯一且脱敏的 evidence artifact，以及 `Backend Tests` 聚合只包含四条独立成功断言。MAN-669 PR-B 追加两条构建配置硬规则：(1) **`backend/**` 下的每个 `csproj`（不只 `*.Tests.csproj`）都必须是 `backend/Nerv.IIP.sln` 的成员**——只经 `ProjectReference` 传递可达、不在解决方案 configuration map 里的项目会回落到自身默认配置，被 `--configuration Release` 的分片产出到 `bin/Debug`，且构建输出里没有任何东西会失败（`Nerv.IIP.Contracts.Mes` 真实踩过，见 `docs/architecture/backend-ci-build-strategy.md`）；(2) 拒绝把 shard 的 `solutionFilter` 直接写成 `backend/Nerv.IIP.sln`，否则会被下游 JSON 解析报成"filter 格式非法"，掩盖真正的问题——列出全部项目的 `.slnf` 则由既有的"filter 与清单逐项相等"挡住。规则 (2) 的两侧路径先经 `[System.IO.Path]::GetFullPath()` **相对仓库根规范化**再大小写不敏感比较（走查收尾；原先只剥一层 `^\./` 前缀，`backend//…`、`backend/./…`、`../` 回绕与绝对路径都会绕过该分支落回误报）。规则 (1) **有意不提供 allowlist / owner-issue 豁免通道**（与 `test-determinism-baseline.json` 的分类纪律不同）：一条登记的例外等于"明知配置错误仍然构建"，不是任何人能背的债；当前覆盖 163/163 无缺口，确有需要时的豁免路径是改本脚本（连同其契约测试）并走脚本治理。**失败时把每条发现写 stdout 再 `exit 1`（与 `check-script-governance.ps1`、`verify-solution-configuration-membership.ps1` 同形），不用 `throw`；因此调用方必须查退出码，本脚本与它的契约测试在 `ci.yml` 里各占一个独立 step，绝不可合并进同一个 `run:` 块**——两条规则的论证只写在 `docs/architecture/backend-ci-build-strategy.md` 的「走查收尾」第 3 条。只读，不执行测试。**#1507 起本脚本是纯政策门禁，完全不读耗时数据**：配平属于测量值，归 report-only 的 `report-backend-test-shard-balance.ps1`，该边界由契约测试以 **AST** 判定（不 dot-source 耗时库、不调用其任一导出函数、求值字符串里不出现耗时文件名），而不是扫原始源码文本——注释里提到耗时文件名不构成依赖；扫描范围含**门禁自己 dot-source 的每个 `scripts/lib` 库**（库集由门禁的 dot-source 语句自行推导，不硬编码），否则把耗时调用挪进 `BackendTestShardSelectors.ps1` 就能绕过。「排除选择器 → MAN-661 policy 身份」的推导抽到 `scripts/lib/BackendTestShardSelectors.ps1`：`Get-BackendTestShardPolicyIdentityMatches` 是门禁与契约测试共跑的同一份推导，`Get-BackendTestShardPolicyIdentityKey` 只把一条 match 压成可比字符串、目前只有契约测试调用（门禁要的是 match 本身，不是压平的键）；测试自带对照组（把 lane 拼回键会失键、把 `excludedTestLanes` 留在原片会被本脚本挡下），否则「零失键」等于测试在断言自己的算术。 |
| `update-backend-test-shard-timings.ps1` | `generate` | 已受治理 | #1507 分片耗时缓存刷新入口。把最近 **5** 次成功 `main` push CI run 的 retained TRX evidence artifact 聚合成 `artifacts/backend-test-shard-timings.json`（gitignored，**不入库、无哈希、不受刷新触发约束、不是任何门禁的输入**）。键是**程序集**，不含 lane/shard，因此换片不失键；同一 run 内同程序集的多行先求和再跨 run 取**中位数**（hosted runner 同 commit 抖动可达数十个百分点，一次 run 是样本不是测量）。**任何取不到数据的情况都只告警并 exit 0**——没有 gh、没有 token、离线、artifact 过期、某个 run 的证据包不可用，全部属于缓存的正常状态；把其中任何一种变成非零退出，等于把 #1507 删掉的那套人肉刷新仪式重建起来。口径与 N 值的论证写在 `scripts/lib/BackendTestShardTimings.ps1` 与 `test-evidence-governance.md` 的「Timing data is a cache, not a governed asset」。 |
| `report-backend-test-shard-balance.ps1` | `check` + `generate` | 已受治理 | #1507 四条后端 fast shard 的 **report-only** 配平报告。读缓存（超过 24 小时自行刷新，因此没有人工刷新步骤），缓存不可用时回落到 committed evidence snapshot，再不可用则全部走估值。**耗时数据不存在任何失败模式**：缺观测的程序集用估值参与配平（优先取同片已测程序集的中位数，其次全局中位数，最后固定兜底值），并输出 `timing-assembly-missing` 警告；完全无数据输出 `timing-source-unavailable`；两者都 exit 0。唯一的非零退出是清单结构不可用——那是受治理文件的缺陷，不是测量值的缺陷。与 `verify-backend-test-shards.ps1` 是**两个程序**：后者是政策硬门禁且完全不读耗时，该边界由 `tests/backend-test-shards.Tests.ps1` 断言。刻意**不接 CI 门禁**：它的正常路径包含降级，接成 required check 会把「artifact 拉不到」变成红。分类是 `check` + `generate`（与 `collect-test-evidence.ps1` 同形）而不是纯 `check`：默认路径会刷新并写入声明过的 gitignored 缓存，本文分类矩阵里 `check` 禁止写生成物；`-NoRefresh` 才是纯读路径。 |
| `scripts/lib/BackendTestShardTimings.ps1` | `check`/`generate` library | 已受治理 | 分片耗时缓存的纯函数与刷新实现：程序集键规范化（`ToLowerInvariant` 折一次，VSTest 的 `storage` 是小写而磁盘上是 Pascal）、中位数、evidence summary → 观测行、跨 run 聚合、缓存读写、配平报告与文本渲染（数值一律 `InvariantCulture` 格式化，避免同一份报告在不同机器上读出不同文本）。**本文件的文件头是「耗时=缓存 / 政策=治理资产」这条边界的代码侧唯一权威**，叙事侧是 `test-evidence-governance.md` 的同名小节。 |
| `run-backend-test-shard.ps1` | `verify` | 已受治理 | 执行单个已分类的后端快速 shard（`--logger trx;LogFilePrefix=<jobId>` 写入 job 本地原始结果目录），按清单 `excludedTestClasses`/`excludedTests` 精确排除真实 PostgreSQL 选择器（类选择器带尾点锚定，避免 `!~` 子串匹配连带排除共享前缀的兄弟类）；跑完后从自己的 TRX（与采集器同一 `UnitTest/@storage` 规则）核对每个已分类项目都至少执行了一个用例、且没有执行未分类的程序集，零执行即失败关闭——不扫 dotnet 控制台文本，那段文本是本地化的，短语匹配在非英文 runner 上会静默放行。失败/超时时把缓冲的 stdout/stderr **脱敏后写入调用方日志流**，不落盘、不上传原始产物——保留面由 MAN-661 采集器负责。 |
| `verify-backend-real-postgres-tests.ps1` | `verify` | 已受治理/真实 PostgreSQL | opt-in 的 `real-postgres` heavy lane owner；对清单登记的每个排除选择器逐条 discovery（必须精确匹配）并要求 TRX 中全部 Passed，不允许把 skip 当成通过。非默认 hosted lane。 |
| `tests/backend-test-shards.Tests.ps1` | `check` | 已受治理 | 用临时未分类项目与一组 `ci.yml` 变异（缺失聚合依赖、no-op/`\|\| true` 断言、`continue-on-error`、命令替换参数、上传原始目录、冒用兄弟 lane、管道包裹 runner、把采集降级成 `success()`）回归分片治理本身，并验证脱敏后的缓冲诊断不落盘。MAN-669 PR-B 追加两组夹具：临时种一个**非测试**项目 `backend/common/Nerv.IIP.TemporarySolutionMembership`（它对旧的 `*.Tests.csproj`-only 规则不可见，因此只有新的全量解决方案成员性检查能拦住它——把该检查削弱掉此用例立刻红），并断言失败文本里不含 `Unclassified backend test`、以及 `Nerv.IIP.Contracts.Mes` 必须留在 `.sln` 里；另用临时 manifest 把某片的 `solutionFilter` 改成**八种拼法**的整个解决方案（原样／`./`／反斜杠／全小写／双斜杠／`/./`／`../` 回绕／绝对路径），逐一断言「被整解分支拒绝」**且**「不得被报成 invalid JSON」——只断言失败的话，整条分支删掉也是全绿。所有断言统一走 `Invoke-ShardValidator`（判退出码 + 完整 stdout），不再从命令日志目录捞 `stdout.log`/`stderr.log` 补全文本，断言也从短片段升级成整句（缘由见 `backend-ci-build-strategy.md` 的「走查收尾」第 3 条）。#1507 追加一组「耗时是缓存、政策才是资产」的回归：删掉某程序集的耗时数据后配平必须 report-only 告警并 exit 0、完全无耗时来源也必须 exit 0、模拟一次换片（改 manifest 分组）后政策键与耗时键各自零丢失——并附一条**对照**断言证明同一次换片在旧的 (lane, assembly) 键下确实会失键，否则前一条断言在查找逻辑被削成 no-op 时也会绿。另断言 policy 规则的 lane 字段不带 `-shard-N`、determinism 债务行不带任何 lane/shard 维度。CI 在 Backend Test Shard Governance job 中以真实退出码执行，与被测脚本各占一个独立 step。 |
| `verify-solution-configuration-membership.ps1` | `check` | 已受治理 | MAN-669 PR-C 解决方案配置映射门禁。决定一个项目用哪个 Configuration 的是解决方案的**配置映射**（`GlobalSection(ProjectConfigurationPlatforms)` → `CurrentSolutionConfigurationContents`），**不是** `Project(...)` 声明行；映射覆盖不到的项目回落到自身默认配置，于是 `--configuration Release` 的构建把它产出到 `bin/Debug`，而构建输出里没有任何东西会失败。项目有**两种**方式逃出这张表，本脚本两种都查：**形态 1「根本不是成员」**（只经传递 `ProjectReference` 到达，既无声明行也无映射条目；本仓库真实踩过两次）与**形态 2「是成员但映射缺失或反向」**（有 `Project(...)` 行因而骗过一切声明行规则，但缺某个解决方案配置的 `.ActiveCfg`，或把 `Release\|*` 指向 `Debug\|*`；本仓库尚未真实发生，但离发生只有一次手改的距离）。**两种形态的完整论证、踩过的项目、前后对照的 run id 与「漏一行即复现」的推导只写在 `docs/architecture/backend-ci-build-strategy.md`（唯一权威），本表与脚本注释都只留结论 + 指针**——同一段论证复述四处，改一处就会与其余三处打架。与上面 `verify-backend-test-shards.ps1` 那条**目录规则**互补而非替代——目录规则按构造只认识一个解决方案，也只读 `Project(...)` 行，两种形态都看不见；反过来目录规则能抓到「没人引用的孤儿项目」，本脚本抓不到。**解决方案是扫描发现的而非硬编码列出**，否则第三个 `.sln` 会静默不受检——那正是本脚本批评目录规则的那种「按构造看不见」。发现范围**跳过 git 忽略的路径**（走查收尾，issue #1496）：agent 工具会在仓库内建整仓工作副本（`/worktrees/`、`/.claude/worktrees/` 等已在 `.gitignore` 里），不过滤的话别人 worktree 里的半成品会让你本地变红。判据取 `.gitignore` 单一来源（一次批量 `git check-ignore`）而非枚举目录名黑名单——黑名单要逐个登记将来的新工具目录；根不是 git work tree 时（契约测试的临时夹具）退回「全部发现」而不是「什么都不发现」。**空扫描仍然失败**（收紧范围不得把门禁变成 no-op），失败信息与其它发现同形。实测数据与修法权衡见 `backend-ci-build-strategy.md` 的「走查收尾」第 4 条。展开 MSBuild glob（`**` 跨目录，`*`/`?` 不跨），因为 `Nerv.IIP.MigrationGovernance.Tests` 用了 `..\services\**\*.Infrastructure.csproj`——按字面路径处理会报一个不存在的项目并把真实发现全埋在后面。**失败时把每条发现写 stdout 再 `exit 1`（与 `check-script-governance.ps1` 同形），不用 `throw`；因此调用方必须查退出码，不可与别的脚本共用一个 `run:` 块**——两条规则的论证只写在 `backend-ci-build-strategy.md` 的「走查收尾」第 3 条（与上面 `verify-backend-test-shards.ps1` 那行同源，不在本表复述）。**无 allowlist、无 owner-issue 豁免通道**，理由同上一条：一条登记的例外等于「明知配置错误仍然构建」。只读；接入 required check `Script Governance`。 |
| `tests/solution-configuration-membership.Tests.ps1` | `check` | 已受治理 | 上一条的契约测试，10 步**全部是行为断言**（跑 verifier、看它的真实输出），**没有一条源码文本断言**。真仓库必须通过，且必须从 verifier **自己的输出**里看到两个解决方案都被报告——不是「文件里提到过这两个路径」，后者在默认范围被收窄、路径残留在注释里时依然是绿的（PR-C 走查实测坐实过这个假绿）。夹具覆盖：传递非成员必红且同时点名非成员与拉它进来的成员、登记后必绿、**声明成员缺 `Release` 映射必红**、**`Release` 映射到 `Debug` 必红**、映射完整必绿、glob 必须展开（`**` 跨目录、叶子模式不误伤同目录其它项目、字面 glob 文本不得被当作项目路径）、**未登记的 `.sln` 丢进树里也必须被发现并检查**。走查收尾追加两步：**gitignore 排除**用一棵临时 `git init` 夹具树做**成对断言**——同一棵树，带 `/worktrees/` 忽略规则时那份故意坏掉的工作副本不可见、整体变绿，去掉规则后同一份副本被发现、整体变红（该过滤削弱即红，既非恒真断言也不读源码文本）；**空扫描**把 `*.sln` 全部忽略掉，必须失败并说明「不得 vacuously pass」。五条原有变异逐条实跑确认变红：关闭形态 2 检查、关闭反向映射检查、默认范围收窄成 backend-only 且路径留在注释里、glob 展开返回空集、发现跳过某个子树。断言前把捕获文本的空白折叠，使断行位置不进入契约（缘由见 `backend-ci-build-strategy.md` 的「走查收尾」第 3 条）。夹具全部建在 OS 临时目录并在 `finally` 清理，不改动仓库。CI 在 Script Governance job 中以真实退出码执行。 |
| `bootstrap-online.ps1` | `release-install` | 已迁移 | 有网空白机器入口；使用 helper 执行 winget、Aspire install script、dotnet restore、pnpm install、AppHost build 和可选 dev 启动；只初始化本地 Development user-secrets，不承担离线包制作或客户现场服务注册。 |
| `install/migrate-file-storage.ps1` | `release-install` | 已受治理 | 只从当前进程的 `NERV_IIP_FILE_STORAGE_DB` 读取目标连接，默认校验目标库精确匹配 `nerv_iip_filestorage`（受控自定义名称必须显式传 `-ExpectedDatabase`），输出脱敏 release/service/profile/target/migration/correlation/log 状态并应用 FileStorage EF migrations；不负责备份、删库或 seed，PoC/production 调用前必须完成 database release runbook preflight。 |
| `export-gateway-openapi.ps1` | `generate` | legacy exemption | 仍在 `scripts/script-governance-baseline.json` 中豁免 `MissingHelper`、`ForbiddenCommand`、`DynamicInvocation` 和 `ForbiddenProcessStart`；迁移时需声明写入 OpenAPI 快照和服务启动副作用。 |
| `verify-second-slice-ops.ps1` | `verify` | legacy exemption | 仍在 `scripts/script-governance-baseline.json` 中豁免直接命令/进程调用；迁移时需收敛 Gateway/Ops/Connector Host 进程树、日志和端口清理。 |

当前脚本治理 baseline 只保留 `scripts/export-gateway-openapi.ps1` 与 `scripts/verify-second-slice-ops.ps1` 两个 legacy exemption；新增脚本不得复用该例外口径。`scripts/tests/**` 目前不在目录扫描范围内（这些 harness 故意以动态调用和直接进程验证治理规则本身）；把它们纳入治理需要 baseline 先支持 owner issue 与到期日，与 `backend/test-determinism-baseline.json` 同等纪律，属独立跟进项，不在 MAN-669 范围内。

后端测试确定性两个脚本共用 `artifacts/test-determinism/man-662/**`：`check` 侧只读、`verify` 侧只追加新的 invocation 目录。`backend/test-determinism-baseline.json` 的每一条 `expiring-debt` 行必须指向一个**在本变更之外仍然存在**的责任 issue（`MAN-\d+` 或 `#\d+`），并带独立到期日；用当前 PR 自己的票做 owner 等于合并当天就没有责任人，属于门禁失效。`permanent` 行是唯一没有到期日的分类，代价是它的 `路径=pattern` 白名单写在脚本里而不是 baseline 里：新增一个常设例外要改脚本、过脚本治理与评审，baseline 无法自我豁免；白名单只对写明的 pattern 生效，一个文件拿到 `StaticSetter` 常设位不等于它以后的 `Thread.Sleep` 也能登记成 permanent。

## 新脚本准入

新增脚本合入前至少满足：

1. 有 `Script-Governance` 声明块。
2. 分类准确，副作用写清楚。
3. 高风险命令通过 helper。
4. fast gate 通过。
5. 涉及数据库、容器、端口或生成产物时，同步更新对应架构文档或 runbook。
