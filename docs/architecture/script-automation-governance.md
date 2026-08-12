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
2. **`DynamicInvocation` 收窄为「注入式 action seam」。** 库可以 `& $Action`，条件是该变量在**本次调用所在的作用域链上可证明是 script block**：要么是 `[scriptblock]` 类型的参数，要么是被 `{ … }` 字面量（含 `.GetNewClosure()`）赋值的变量。作用域单位是一个 `ScriptBlockAst`（函数体或 `{ … }` 字面量），查找沿包围链向外走——与 PowerShell 自身的作用域一致：外层的 seam 对内层可见，**兄弟函数的同名变量永远不可见**，且**内层一旦自己绑定同名变量就遮蔽外层**（最内层绑定说了算：函数体里写 `$x = 'dotnet'` 就是一个局部，文件级的 `$x = { … }` 替它作不了保）。**「绑定」的准确范围见下表**——它不是「与运行期解析一致」，运行期还有两种残余拼写这条规则看不见，而作用域限定符那一侧又比运行期更严。这一条是刻意的：若按整文件收集，A 函数里的 `$action = { }` 就会替 B 函数里的 `$action = 'dotnet'; & $action` 放行，等于用一个流行参数名重新打开这条规则要堵的洞。这正是本仓库构造可测试库所依赖的注入 seam。`& 'dotnet'`、`& "$exe"`、`& (Get-Command …)`、`& $stringVariable` 一律仍是违规——最后一种是 `ForbiddenCommand` 在结构上看不见的那个洞，所以这条规则必须留在库作用域，而不是整体豁免。

   **参数的两种拼写按同一个作用域判定。** PowerShell 的 `function Foo { param(...) }` 与 `function Foo(...)` 运行期语义完全相同，但 AST 不同：内联参数表挂在 `FunctionDefinitionAst` 上、位于函数体**之外**，因此「向外找最近的 `ScriptBlockAst`」会把它算到**文件级**、对所有兄弟函数可见。#1509 二轮实测到这个洞（内联拼写 exit 0，`param()` 拼写 exit 1）。现在两种拼写都归属函数体，`inline-parameter-cross-function-leak` 与 `cross-function-parameter-leak` 各钉一种。

   **「绑定一个名字」的拼写从 AST 类型穷举，不再逐轮追加**（#1509 四轮）。二轮只修了 `=`；三轮实测另外八种拼写照样 exit 0；四轮又实测出两种（`$a, $b = …` 多重赋值、`[string] $a = …` 类型化赋值）依然 exit 0，**并且被测进程真的执行了外部命令**。每一轮都只补了上一轮被点名的那几种，所以每一轮都落后一轮。现在 `AssignmentStatementAst.Left` 交给 `Get-SeamAssignmentTargets` **按类型层次递归展开**，下表的 Left 形状是把 `Left` 能取的类型写全的结果，而不是被报告过的拼写清单：

   | 拼写 | AST | 是否算绑定 | 能否证明 seam | 钉住它的用例 |
   | --- | --- | --- | --- | --- |
   | `$x = …` / `$x += …` | `VariableExpressionAst` | ✅ | ✅（未加限定符时） | `file-level-seam-shadowed-by-local`、`compound-assignment-shadows-seam` |
   | `$x, $y = …` | `ArrayLiteralAst`（逐元素递归） | ✅ | ❌ | `multiple-assignment-shadows-seam` |
   | `[string] $x = …` | `ConvertExpressionAst`（剥壳递归） | ✅ | ❌ | `type-constrained-assignment-shadows-seam`、`type-constrained-declaration-proves-no-seam` |
   | `[ValidateNotNullOrEmpty()] $x = …` | `AttributedExpressionAst`（`Convert` 的基类，同一分支） | ✅ | ❌ | `attributed-assignment-shadows-seam` |
   | `($x) = …` / `($x, $y) = …` | `ParenExpressionAst`（穿过 pipeline 递归） | ✅ | ❌ | `parenthesized-assignment-shadows-seam` |
   | `[string[]] ($x, $y) = …` | 三层嵌套 | ✅ | ❌ | `nested-left-shapes-shadow-seam`（只有递归展开才红） |
   | `$h['k'] = …` | `IndexExpressionAst` | ❌ **显式跳过** | — | `index-assignment-is-not-a-binding`（断言**当前放行**） |
   | `$o.P = …` | `MemberExpressionAst` | ❌ **显式跳过** | — | `member-assignment-is-not-a-binding`（断言**当前放行**） |
   | `param($x)` | `ParameterAst`（`param()` 块） | ✅ | 仅 `[scriptblock]` 参数 | `cross-function-parameter-leak` |
   | `function F($x)` | `ParameterAst`（内联表） | ✅ | 仅 `[scriptblock]` 参数 | `inline-parameter-cross-function-leak` |
   | `foreach ($x in …)` | `ForEachStatementAst.Variable` | ✅ | ❌ | `foreach-iteration-variable-shadows-seam` |
   | `$local:` / `$script:` / `$global:` / `$private:` / `$variable:` | 限定符归一化后比对 | ✅ | ❌ | `{local,script,global,private,variable}-qualifier-shadows-seam` 各一条 |
   | `data $x { … }` | `DataStatementAst.Variable` | ✅ | ❌ | `data-statement-shadows-seam` |
   | `Set-Variable` / `New-Variable`（名字实参**逐个字面量**展开；命名 `-Name`、`-Name:x` 冒号实参或位置参数；别名从 PowerShell 的别名表枚举，当前解出 `set`/`sv`/`nv`；命令名大小写不敏感；参数前缀缩写；模块限定名剥掉限定符后同样命中） | `CommandAst`，命令名按别名表 + 限定符归一化，元素配对按 cmdlet 参数元数据，名字实参按 AST 类型递归展开（`Get-SeamBinderLiteralNames`） | ✅ | ❌ | `set-variable-shadows-seam`、`new-variable-shadows-seam`、`set-variable-positional-name-after-{valued,value,switch}-parameter`、`new-variable-positional-name-after-valued-parameter`、`set-variable-alias-positional-name-after-valued-parameter`、`set-variable-abbreviated-{valued,name}-parameter`、`set-variable-shipped-alias-{,uppercase-}shadows-seam`、`{set,new}-variable-module-qualified-shadows-seam`、`set-variable-module-qualified-mixed-case-shadows-seam`、`set-variable-module-qualified-alias-shadows-seam`、`set-variable-colon-argument-{single,multiple}-name{,s}`、`binder-alias-removed-from-session` |
   | `Set-Variable a,b 'x'` / `-Name a,b`（多名字） | `ArrayLiteralAst`（逐元素递归）——`Set-Variable -Name` 的声明类型是 `[string[]]` | ✅ **每个字面量元素各算一个绑定** | ❌ | `set-variable-multiple-literal-names-{positional,named}`、`set-variable-colon-argument-multiple-names`、`set-variable-trailing-comma-name-list`、`set-variable-alias-multiple-literal-names`、`set-variable-module-qualified-multiple-literal-names` |
   | `Set-Variable ('a','b')` / `@('a','b')` / `$('a')` | `ParenExpressionAst` / `ArrayExpressionAst` / `SubExpressionAst`（分组语法，穿透递归） | ✅ | ❌ | `set-variable-parenthesized-name-list`、`set-variable-array-expression-name-list`、`set-variable-parenthesized-single-name`、`set-variable-subexpression-single-name` |
   | `Set-Variable -Name a,$computed` | 混合列表 | ✅ **只记字面量元素**（非字面量元素落入残余） | ❌ | `set-variable-mixed-literal-and-computed-names` |
   | `New-Variable a,b 'x'` | `New-Variable -Name` 是标量 `[string]`，运行期直接抛错、什么都没绑 | ✅ **过报**（fail-closed 裁决，见下） | ❌ | `new-variable-multiple-literal-names-over-reported` |
   | `Set-Variable -Name ([string] 'a')` / `-Name ('a' + 'b')` | 名字可静态算出但不是**字面量**；本 checker 不做常量折叠 | ❌ 残余 | — | `residual-set-variable-{non-literal,concatenated}-name-expression`（断言**当前放行**） |
   | `Set-Variable -Bogus x 'y'` / `Set-Variable -V x 'y'` | 参数名无法解析或前缀歧义 | ❌ **不构成绑定**（该调用运行期直接抛错，什么都没绑） | — | `set-variable-{unknown,ambiguous}-parameter-binds-nothing`（断言**当前放行**） |
   | `Set-Variable -Name $computed` | 名字只在运行期存在 | ❌ 残余 | — | `residual-set-variable-computed-name`（断言**当前放行**） |
   | `Set-Variable @splat` | 名字在 hashtable 里，AST 解不出 | ❌ 残余 | — | `residual-set-variable-splatted-parameters`（断言**当前放行**） |
   | `$ExecutionContext.SessionState.PSVariable.Set(…)` | 同上 | ❌ 残余 | — | `residual-psvariable-set`（断言**当前放行**） |
   | `[ref] $x` / `Get-Variable x` 后写 `.Value` | 运行期替换绑定；写法上是成员赋值 | ❌ 残余 | — | `residual-ref-rebinding`（断言**当前放行**） |
   | `-PipelineVariable x`（下游元素消费） | 绑定由管道处理器创建，文件里没有对应 AST 节点 | ❌ 残余 | — | `residual-pipeline-variable`（断言**当前放行**） |
   | `-OutVariable x` | 同上，且管道结束后仍然存在 | ❌ 残余 | — | `residual-out-variable`（断言**当前放行**） |
   | A 函数里 `$script:x = …`，B 函数里 `& $x` | 跨 `ScriptBlockAst` 作用域，读的一侧看不到这次绑定 | ❌ 残余 | — | `residual-cross-scope-script-assignment`（断言**当前放行**） |
   | 自动变量 `$_`（管道 / `switch` / `catch`） | 不是本规则要建模的绑定 | n/a：`& $_` 一律违规 | — | `foreach-object-automatic-variable`、`switch-automatic-variable`、`catch-automatic-variable` |

   **`Set-Variable` 的名字按 cmdlet 的参数绑定语义配对，不是「第一个非参数元素」**（#1509 六轮）。旧实现把第一个非 `CommandParameterAst` 的元素当位置化的 `-Name`，于是**任何带值的命名参数排在前面都能借名**：`Set-Variable -Scope Local action '/bin/echo'`、`Set-Variable -Value 'dotnet' action`、`sv -Scope Local action '/bin/echo'` 三种实测 exit 0，而运行期真的执行了外部命令。反向的粗暴修法（「遇到参数就跳过下一个元素」）会踩坏另一半：`-Force` 是 switch，它后面那个元素**就是**位置化的名字。所以配对读的是 cmdlet 自己的参数元数据（`Get-Command Set-Variable` 的 `ParameterType -eq [switch]`），带值参数吞掉后一个元素、switch 不吞；参数名按 PowerShell 自己的规则解析（精确名/别名，否则唯一前缀）。解析不出来或前缀有歧义时**不记任何绑定**，因为这种调用运行期直接抛错（实测 `NamedParameterNotFound` / `AmbiguousParameter`），压根没绑成——这不是放宽。表格里三类各有用例，`set-variable-positional-name-after-switch-parameter` 就是防粗暴修法的那条控制组。

   **命令名这一半也从元数据枚举**（#1509 七轮）。六轮只把**参数配对**改成读 cmdlet 元数据，**命令名识别**仍是手写的四条清单（`set-variable`/`sv`/`new-variable`/`nv`），于是同一个缺陷类换个拼写照样放行——实测 exit 0 且运行期真的执行了外部命令（以 `/bin/echo` 回显证实）：

   ```powershell
   set -Scope Local action '/bin/echo'                          # set 是 Set-Variable 的内置别名
   SET -Name action -Value '/bin/echo'                          # 命令名大小写不敏感
   Microsoft.PowerShell.Utility\Set-Variable -Scope Local action '/bin/echo'   # 模块限定名
   ```

   现在别名来自 `Get-Alias -Definition` 对 `$seamBinderCanonicalNames`（只声明 `Set-Variable`/`New-Variable` 两个 cmdlet）的枚举，模块限定名按最后一个 `\` 序数切分后取末段再比对。比较用 `OrdinalIgnoreCase`：**刻意的大小写不敏感**，因为 PowerShell 解析命令名本来就不分大小写（`SET`、`microsoft.powershell.utility\SET-VARIABLE` 实测都真的绑定），而 `OrdinalIgnoreCase` 只折叠大小写、不折叠任何可忽略字符，所以仍在本 PR 的序数口径内。

   `Get-Alias` 读的是**会话状态**，只会让 checker 变瞎（`set` 的 `Options` 是 `None`，profile 或先跑的任何东西都能删掉或改绑它，而被扫描的文件仍会在别处以完整别名表运行）。因此随附一份 `set`/`sv`/`nv` 的**下界**并入，它是「至少认这些」而不是识别清单——往里加名字只会把放行变成报告，绝不可能反过来。两半各有守卫：`binder-alias-removed-from-session` 在删掉 `Alias:set` 的会话里跑 checker、要求判定不变（钉下界），源码结构契约要求别名集合由**恰好一次** `Get-Alias -Definition $seamBinderCanonicalName` 派生（钉枚举本身——把清单手写全、行为用例全绿时它照样红）。

   **名字实参的「集合性」这一轴**（#1509 八轮）。六轮把**参数配对**改成读 cmdlet 元数据，七轮把**命令名识别**改成从别名表枚举，两轮都没问过第五个轴：**参数声明类型本身是不是集合**。`Get-Command Set-Variable` 说 `-Name` 是 `[string[]]`——一个实参可以拼出好几个绑定，而实现只认单个 `StringConstantExpressionAst`，于是整条绑定被丢掉。实测 exit 0 且运行期真的执行了外部命令（用 `/bin/echo` 回显证实：seam 应返回 `'seam'`，实际输出为空）：

   ```powershell
   Set-Variable action,zz '/bin/echo'            # 位置参数，ArrayLiteralAst
   Set-Variable -Name action,zz -Value '/bin/echo'
   Set-Variable -Name:action,zz -Value '/bin/echo'   # 冒号实参，挂在 CommandParameterAst 上
   Set-Variable ('action','zz') '/bin/echo'      # 分组语法
   Set-Variable @('action','zz') '/bin/echo'
   Set-Variable ('action') '/bin/echo'           # 连单个名字加了括号都看不见
   Set-Variable $('action') '/bin/echo'
   Set-Variable -Name action, -Value '/bin/echo' # 尾逗号：真的把 `-Value` 也绑成了变量名
   ```

   这**不是**残余表里的「名字只在运行期存在」：`action,zz` 是两个静态字面量，而文档那一行按字面读（「**字面量**名字，命名 `-Name` 或位置参数」）恰好覆盖这个拼写——又一次文档强度 > 实现强度。现在名字实参交给 `Get-SeamBinderLiteralNames` **按 AST 类型递归展开**，与 `Get-SeamAssignmentTargets` 对 `$a, $b = …` 的处理同构，两处用的是同一条判据而不是两套强度。

   **裁决三条**：

   1. **只记字面量元素，不是整条不记。** `-Name a,$computed` 运行期两个名字都绑（实测），把整条丢掉会在一个**就写在那里**的名字上 fail open；非字面量元素仍然落进既有的 computed-name 残余。`set-variable-mixed-literal-and-computed-names` 钉住这条——改成「整条不记」，只有它红。
   2. **字面量 ≠ 可常量折叠。** `(…)`、`@(…)`、`$(…)` 是分组/数组构造，不计算值，所以穿透；`([string] 'a')`、`('a' + 'b')` 是计算，本 checker 不做常量折叠，它们是**登记残余**（两条用例断言当前放行，实测运行期真的绑上并执行了外部命令）。加一条读穿 `AttributedExpressionAst` 的折叠分支，这两条就红——这条线是可执行的，不是注释里的声明。
   3. **`New-Variable` 的 `-Name` 是标量 `[string]`**，多名字实参运行期直接抛错（命名式 `CannotConvertArgument`、位置式「positional parameter cannot be found」，均实测），什么都没绑；checker 照样展开，属**过报**。一条规则而不是两条，方向 fail-closed——与模块限定别名同一笔交易。`new-variable-multiple-literal-names-over-reported` 把它登记成明写裁决：真要加 per-cmdlet 分支，得先改这一行。

   **这一轴被守成契约，不只是被修好**：`binder parameter collection types` 从 `Get-Command` 枚举两个 binder cmdlet 的**全部**参数，断言集合类型的恰好是 `Set-Variable` 的 `Name`/`Include`/`Exclude` 三个（单向：PowerShell 新增一个集合类型参数就红，逼人裁决；少一个不红），并分别断言 `Set-Variable -Name` 是 `[string[]]`、`New-Variable -Name` 是 `[string]`——上面两条裁决各自站在哪个测量结果上，是断言而不是记忆。`Get-SeamBinderLiteralNames dispatch set` 则按 `Left type set` 的同一形式断言源码里对 `$Argument` 的 `-is` 分派集合恰好是那五种类型，并用一份语料实测这五种拼写真的解析成那五种 AST——删掉任一分支即便行为用例没覆盖到也会红。

   **变异矩阵里查出的第三处盲区**：`-Name:x` 把实参挂在 `CommandParameterAst.Argument` 上，走的是与「下一个元素」「第一个未被消费的元素」都不同的**第三条**路径，而在八轮之前**一条用例都没有**——把那条 return 改坏，全绿。现在 `set-variable-colon-argument-{single,multiple}-name{,s}` 各钉一条。

   模块限定的**别名**（`Microsoft.PowerShell.Utility\sv`）是唯一的过报：PowerShell 只限定导出命令，内置别名不属于任何模块，该调用运行期直接抛 `CommandNotFoundException`（实测）。剥限定符是一条规则而不是两条，且过报只会多花一次权限、不会多给一次，属**fail-closed** 方向，由 `set-variable-module-qualified-alias-shadows-seam` 登记成一条明写的裁决。

   **索引与成员赋值为什么显式跳过**：`$h['k'] = …` 与 `$o.P = …` 在**语法层**不构成变量绑定——它们命名的是 `$h` / `$o` 所指对象的一个成员，文件里根本没有对名字 `h` / `o` 的绑定可记；算成绑定就会报一个背后什么都没有的违规。两条「断言当前放行」的用例把这个语法层判断钉住——把任一种改成绑定，用例就红。

   这条**不等于**「所以变量在运行期必然仍持有原来的 seam」。那句话是假的，四轮里一直挂在这里当理由，五轮实测推翻（pwsh 7.6.4）：

   ```powershell
   $a = { 'seam' }; $r = [ref] $a; $r.Value = '/bin/echo'; & $a   # 真的执行 /bin/echo
   ```

   `[ref] $a` 与 `Get-Variable a` 交出的都是活的 `PSVariable`，写它的 `.Value` 就替换了绑定——用的正是这一分支跳过的成员赋值拼写；`scripts/lib/` 里 `[ref]` 有 18 处在用（全是 `TryParse`/`ParseFile` 出参）。运行期经 `PSVariable` 句柄改绑属于上表的**登记残余**，不在本 checker 的静态覆盖面内，由 `residual-ref-rebinding` 钉住；跳过成员赋值的正当性来自语法层，不来自这条假前提。

   **`Left` 类型集合是被守住的，不只是被观察到的**：`Get-SeamAssignmentTargets` 对未识别的 Left 形状返回「什么都不绑」= fail open，未来 PowerShell 加一种表达式形状会静默失去遮蔽。`script-governance-scan-boundary.Tests.ps1` 里的 `Left type set` 契约把四件事变成断言：源码里对 `$Left` 的 `-is` 分派集合恰好是那四种、`ConvertExpressionAst` 确实继承 `AttributedExpressionAst`、一份 28 条拼写语料解析出的 Left 类型恰好是上表七种且每种要么被分派要么被显式判为非绑定、以及 AST 程序集里不存在第三类「没人裁决过」的具体表达式类型（只对**新增**报红，某个运行时缺类型不报红——缺类型不可能引入未处理的 Left 形状）。

   **残余表按「先 seam 后改赋」写，但方向是双向的**：`$x = { … }` 之后再 `$x = 'dotnet'` 与 `$x = 'dotnet'` 之后再 `$x ??= { … }` 在本 checker 眼里同形——同一作用域内的赋值不分先后，只要有一条右侧是 script block 字面量就进 Seam 集合。后一种实测 exit 0 而运行期 `$x` 仍是 `'dotnet'`（`??=` 不触发），`& $x` 真的执行外部命令。条件性赋值（`??=`）与复合赋值（`+=`）在这一点上同形。

   **`$using:` 不在限定符表里**：`$using:a = 1` 在 PowerShell 7 是**parse error**（四轮实测），列进去只是个没有源码能走到的死分支；文档此前把它和另外五个真限定符一起打 ✅，实现里也确实带着它——那正是「措辞强于实现」的同一类问题，一并清掉。`$variable:` 是真的（实测 `$variable:a = 'x'` 之后 `$a` 读回 `x`），补一条用例。

   **包壳的拼写只遮蔽、不证明**：只有**裸的、未加限定符的** `VariableExpressionAst` 能进 Seam 集合。`[string] $x = { … }` 绑定的是字符串而不是 script block，这是包壳里唯一答案可知而且为**否**的一种；其余包壳一律按同一方向处理，保证这次修改与三轮的限定符裁决一样**只减不增**权限。`type-constrained-declaration-proves-no-seam` 钉住这一半。

   限定符只归一化**绑定**这一侧，不归一化**调用**这一侧：`$script:x = { … }` 会遮蔽外层的 `x` 但不能证明 seam，`& $script:x` 什么也证明不了。两条对照分别钉住这两半（`qualified-declaration-proves-no-seam`、`qualified-invocation-proves-nothing`）；本仓库当前没有任何 scope-qualified 的 `& $…` 调用。

library scope 是**声明出来的**，不只是从路径推断：`scripts/lib/` 下的文件必须在自己的 header 里写 `Category: library`（否则报 `MissingLibraryCategory`），而目录之外的文件不能通过贴这个标签来蹭上述两条放宽（否则报 `InvalidCategory`）。放宽写在被放宽者自己的头部，改动可见。

### 可执行守卫

裁决的每一条都有会红的对照，全部在 `scripts/tests/script-governance-scan-boundary.Tests.ps1`（从 `check-script-governance.Tests.ps1` 拆出来的独立文件，见下面「守卫落在哪里」）：

- 排除清单逐字断言 → 往里加 `scripts/lib/*` 直接红；
- 在 `scripts/lib/` 下临时种一个违规库文件，**默认扫描**（不带 `-Path`）必须报出它 → 边界被行为性放宽时红；
- seam 调用放行 / `& 'dotnet'` / `& $exe`（字符串变量）/ 直呼 `dotnet` / `Process::Start` / 未声明 `library` 分类 / 目录外冒充 `library` / 跨函数同名变量不放行逐条对照 → 任何一条规则被削弱或过度收紧都红；
- 参数两种拼写各一条（`cross-function-parameter-leak` 钉 `param()` 块、`inline-parameter-cross-function-leak` 钉内联参数表），外加 `inline-parameter-seam-allowed` 防止「靠丢掉内联参数」来假装收紧；
- 遮蔽语义两条：`file-level-seam-visible-to-inner-scope`（外层 seam 对不重绑的内层仍可见，防止「干脆不向外走」的假收紧）与 `file-level-seam-shadowed-by-local`（内层重绑同名变量后外层 seam 失效）；
- 上表每一行各有对照：算绑定的拼写各一条遮蔽用例（含复合赋值这个控制组），两种显式跳过的 Left 形状各一条「断言当前放行」的用例，每一条残余拼写各一条「断言当前放行」的用例，三条自动变量 `$_` 各一条「断言当前拦截」的用例，限定符归一化的两半各一条，包壳不证明 seam 一条；名字实参的三条路径（下一个元素 / 位置参数 / `-Name:x` 冒号实参）与四种字面量容器形状（`a,b`、`(…)`、`@(…)`、`$(…)`）各有用例，外加 `binder parameter collection types` 与 `Get-SeamBinderLiteralNames dispatch set` 两条结构性契约（前者从 `Get-Command` 单向枚举集合类型参数，后者钉住 `-is` 分派集合，删分支即便行为用例没覆盖也会红）。共同的性质是：**行为一变就红**，无论变松还是变紧。

**守卫落在哪里**：该文件是 CI `Script Governance` job 的一步（`.github/workflows/ci.yml`），同时进入本节末尾的 `compat-fast` 清单与 `scripts/check-script-compatibility.ps1`。它从 `check-script-governance.Tests.ps1` 拆出来，是因为后者还要驱动 ScriptAutomation 的流排空与游离进程夹具，比一条边界契约重得多。

**夹具不落在被跟踪目录**：所有 library scope 用例都在临时目录里搭一棵 `<temp>/scripts/` 镜像树（把 checker 原文件复制进去），在那棵树里种违规夹具并跑默认扫描。checker 的 repo root 是 `$PSScriptRoot/..`，因此镜像树里的 `scripts/lib/x.ps1` 走的正是同一套 `$scanExclusions` 与 library scope 判定；而进程被 kill 或 CI step 超时后，残留只会留在临时目录，不会让此后每次治理门禁变红，也不会污染并行 worktree 的工作树。

### 已知残余

残余共九条，除第 1 条外每一条都有一条**断言当前放行**的用例；行为一变（不论收紧还是放松）用例就红，因此这份清单不会与实现悄悄脱节。这份清单与上表的残余行是同一份枚举，不是它的摘要——六轮之前这里写「残余共三条」而上表已经登记了七条，属于同一类「措辞强于实现」的失配，一并更正。

1. **同一作用域内先后重新赋值**：`& $x` 与 `$x` 的真实内容仍受运行时决定，若有人在**同一个作用域**里写 `$x = { … }` 之后再改赋成字符串，AST 层面无法判否；方向是双向的（`??=` 那一半见上表下方那段）。这一条没有独立用例，因为「当前放行」正是本 checker 对同作用域赋值不排序的直接后果。
2. **`Set-Variable` / `New-Variable` 的名字是算出来的**（`-Name $computed`）：绑定的名字只在运行期存在。用例 `residual-set-variable-computed-name`。
3. **`Set-Variable @splat`**：名字在 hashtable 里，与第 2 条同类。用例 `residual-set-variable-splatted-parameters`。
4. **`$ExecutionContext.SessionState.PSVariable.Set(…)`**：同上。用例 `residual-psvariable-set`。
5. **`[ref] $x` / `Get-Variable x` 之后写 `.Value`**：运行期经活的 `PSVariable` 句柄改绑。用例 `residual-ref-rebinding`。
6. **`-PipelineVariable x`**：绑定由管道处理器创建，文件里没有对应 AST 节点。用例 `residual-pipeline-variable`。
7. **`-OutVariable x`**：同上，且管道结束后仍然存在。用例 `residual-out-variable`。
8. **A 函数里 `$script:x = …`，B 函数里 `& $x`**：跨 `ScriptBlockAst` 作用域，读的一侧看不到这次绑定。用例 `residual-cross-scope-script-assignment`。
9. **名字实参不是字面量**（`-Name ([string] 'x')`、`-Name ('a' + 'b')`）：名字可以静态算出，但本 checker 不做常量折叠——分组语法穿透、计算不穿透。用例 `residual-set-variable-{non-literal,concatenated}-name-expression`。

这九条的共同边界是**同一个作用域**、**运行期才成立的名字/绑定**，或**名字没有被写成字面量**，不是文件、也不是作用域链：跨函数借名（两种参数拼写都算）、「外层写 seam、内层改赋成字符串」，以及 `foreach` / 作用域限定符 / `data` / 字面量 `Set-Variable`（含位置参数、冒号实参、多名字列表与分组语法、从别名表枚举出的别名 `set`/`sv`/`nv`、大小写变体与模块限定名）/ 多重赋值 / 类型化赋值 / 带 attribute 的赋值 / 带括号的赋值这些拼写，**都不在残余里**——它们各有一条钉死的违规用例（见上表）。

索引赋值 `$h['k'] = …` 与成员赋值 `$o.P = …` 也不是残余，它们是**判定为不构成变量绑定**（理由见上表下方那段），各有一条「断言当前放行」的用例；这与「看不见所以放过」是两件事。

措辞与实现在这一点上必须一致——文档承诺的强度就是实现的强度。这条规矩本 PR 自己已经违反过五次（二轮：参数拼写；三轮：八种绑定拼写；四轮：多重/类型化赋值、`$using:` 死条目；八轮：`-Name` 的多名字与分组语法——表格里「**字面量**名字」按字面读恰好覆盖了实现看不见的 `action,zz`），每一次都是文档先写了一个实现还没到的强度。八轮之后上表的每一行都对应实现里的一个分支或一次显式跳过，且每一行都有具名用例。

## 标识符比较的序数收口（#1509、#1512）

PowerShell 的默认字符串比较是 **culture-aware**，`-c` 前缀只关掉大小写不敏感、并不改成序数。实测（pwsh 7 / macOS，U+00AD SOFT HYPHEN）：

```
"Passed$([char]0x00AD)" -ne "Passed"      → False    # 未通过的结果被折进「通过」
switch ("failed$([char]0x00AD)")          → 命中 'failed' 分支（加 -CaseSensitive 也一样）
("$([char]0x00AD)^x").StartsWith('^')     → True     # 锚定守卫被绕过
@('apple','Banana') | Sort-Object         → apple, Banana（序数是 Banana, apple）
Sort-Object @{Expression='name'}          → 同样是文化排序，且作用在**留存产物**上
```

因此「本文件的标识符比较一律序数」这类收口声明**必须是被解析的契约，而不是一句话**——本票前三轮各写过一次这样的声明，三次都被查出不实。扫描面实现在 `scripts/lib/OrdinalComparisonContract.ps1`，被两个契约测试复用。

### 扫描面覆盖的构造（`Get-NervOrdinalContractCoveredAxes`）

`banned-c-operator`（`-ceq`/`-cne`/`-cge`/`-cgt`/`-clt`/`-cle`/`-ccontains`/`-cnotcontains`/`-cin`/`-cnotin`，**无豁免通道**）、`culture-operator-with-string-literal`（对应的默认拼写 `-eq`/`-ne`/`-ge`/`-gt`/`-lt`/`-le`/`-contains`/`-notcontains`/`-in`/`-notin`，且至少一侧是字符串字面量、`[string]` 转换，或元素全部可证明为字符串的 array literal/array expression）、`culture-operator-with-identity-variable`（两侧均为受管 identity 变量/成员的 `-eq`/`-ne`）、`sort-object`（含**不带** `-Unique` 的排序）、`group-object`、`compare-object`、`select-object-unique`、`where-object-comparison-switch`、`switch-statement-string-clause`、`string-method-without-ordinal-comparison`（`.StartsWith`/`.EndsWith`/`.IndexOf`/`.LastIndexOf` 未显式传序数 `[StringComparison]`）、`comparison-method-without-ordinal-comparison`（`[string]::Compare(…)` / `.CompareTo(…)` 未传序数 `[StringComparison]`、接收方也不是序数 `[StringComparer]`）、`parameterless-sort-method`（`.Sort()` 无参，用的是 culture-aware 的 `Comparer<string>.Default`）、`ambiguous-method-with-string-literal`（`.Contains`/`.Equals` 单个**字面量**参数）、`non-ordinal-stringcomparison`（写出来的 `[StringComparison]::CurrentCulture` 之类）、`non-ordinal-stringcomparer`（写出来的 `[StringComparer]::InvariantCulture` 之类）以及 `culture-created-stringcomparer`（`StringComparer.Create(CultureInfo, …)`）。

`culture-operator-with-identity-variable` 只识别 `Id`、`Identity`、`Sha`、`Name`、`Lane`、`Outcome`、`Status`、`Code`、`Key`、`Path`、`Uri`、`Namespace`、`Prefix` 等大小写敏感的窄后缀；`$name`、`$id`、`$path` 等全小写裸名不在该轴内，`ExitCode`、`StatusCode`、`ProcessId`、`Pid` 则不分大小写地明确排除。具名值类型局部声明、本地 `[StringComparison]` 变量与本地 ordinal `HashSet[string]` 只有在同一函数、调用前唯一赋值、不是参数且赋值与调用位于同一直接顺序执行块时才可通过，因此计数、日期和集合的普通比较不会被误报；comparer 的所有 `if/else` 分支还必须为 `Ordinal`/`OrdinalIgnoreCase`。culture、未知、重赋值、条件回退与非 ordinal 分支一律报告。此证明不使用变量名白名单或整文件豁免。

非字符串同形调用只按下列窄证明排除：char 必须来自 `[char]`、带 `[string]` 类型的参数索引，或同一作用域内唯一的该索引赋值/未重写的 `ToCharArray()` 迭代。局部 comparer、ordinal set、具名值类型 local 与 char 证明共用同一写入判定：除直接/typed 赋值外，还识别 canonical、module-qualified 与内建 alias 的 `Set/New/Clear/Remove-Variable`，显式 `variable:` provider 路径上的 `Set/New/Clear/Remove/Copy/Move/Rename-Item`（`New-Item -Path variable: -Name …` 的拆分拼写也在内），五个 common output-variable 参数 `OutVariable`/`ErrorVariable`/`WarningVariable`/`InformationVariable`/`PipelineVariable` 及其 `ov`/`ev`/`wv`/`iv`/`pv` alias，以及把受保护 local 暴露为 `[ref]` 的构造。动态 `*-Variable -Name`、显式 `variable:` 根下的动态 `New-Item -Name`、动态 common-parameter target 与 `local:`、`script:`、`global:`、`private:` 同名路径都按可能写入处理。动态 provider path 无法证明选择了 `variable:` provider，不作为该窄证明的写入事实。ordinal set 必须是接收方自身或调用前唯一赋值的 `HashSet[string]::new(..., [StringComparer]::Ordinal/OrdinalIgnoreCase)`；collection lookup 只接受静态 `[Array]::IndexOf`；日期排序只接受未被同一解析单元 function/filter/alias 遮蔽的 `Get-ChildItem` 紧邻输入、唯一属性为 `LastWriteTimeUtc` 且不带 `-Unique` 的 `Sort-Object`。canonical 或 `Microsoft.PowerShell.Utility` 限定的 `Set-Alias`/`New-Alias` 及其内建 `sal`/`nal` 只在 attached、separate、positional `Name` 为可确认字面量时解析；未知名字和非规范 scope 定义使来源证明失效，任意用户 alias 不会被静态当作内建命令。`String.IndexOf` 的 char overload 只接受显式或静态可证明的 `[char]` needle；其接收方由 `[string]` 参数、`[string]` 转换或全部为未遮蔽的 `Get-Content -Raw` 的同作用域赋值证明为字符串，二、三参数形态的 start/count 还必须由 `[int]` 或整数常量证明。任何字符串字面量（包括单字符）都不享 char 豁免，必须显式传 ordinal comparer。未知接收方、默认/culture set、混合重赋值、动态排序键都继续报告。

**比较算子是从 parser 的类型系统穷举的，不是被点过名的拼写清单**（#1509 六轮）。PowerShell 的比较算子在 `TokenKind` 里成对出现（`I…` / `C…`），契约测试把这个成对家族整体枚举出来，断言它恰好等于三份名单的并集：banned（`-c*` 的相等/序关系/成员系）、culture（它们的默认拼写）、pattern（`-like`/`-notlike`/`-match`/`-notmatch`/`-replace`/`-split` 及其 `c` 变体，按 #1507 口径刻意不动，登记在盲区 `like-and-match-operators`）。三份名单互不相交、每个算子各有一条探针（banned 必报、culture 对字面量必报、pattern 必须沉默）。此前只枚举了相等/成员系的一半，序关系算子 `-lt`/`-le`/`-gt`/`-ge` 两边都没有——它们走 culture collation，与 `Sort-Object` 同一个毛病。

**方法名与类型名这两类判据是名单，不是穷举**：`.StartsWith` 系、`.Compare`/`.CompareTo`、`.Sort()`、`.Contains`/`.Equals`、`[StringComparison]`/`[StringComparer]` 的判定都靠具名匹配，名单之外的拼写不在声明覆盖内（见下一节末尾对声明强度上界的表述）。`.CompareTo` 同时存在于数字与日期类型上，那里报出来是误报——这个方向是刻意的：误报靠把比较写清楚来消除，漏报是留存产物里的静默 locale 依赖。

每一条都有一份合成源码的正向用例，断言**必须被报出来**；这也是「契约存在」不等于「零发现」的那道保险。

### 扫描面的盲区（`Get-NervOrdinalContractBlindSpots`）

以下构造扫不出来。它们不是待办清单，而是**当前行为**：每一条各有一条合成源码用例断言扫描面**保持沉默**，哪天其中一条变得可检测，用例就红、这份清单必须与实现一起改。

这条「双向活」在 #1509 四轮之前只成立一半：用例原先是拿每条 finding 去比**已声明的覆盖轴**，于是「新增了检测能力、但没登记轴名」这个方向抓不到（实测：给 `[ValidateSet]` 加检测、轴名不登记 → 全绿；登记进 `CoveredAxes` 才红）。现在断言改成钉**允许出现的 finding 集合**（除 `sort-object-via-splatted-parameters` 允许 `sort-object` 一条外，其余一律零 finding），因此无论新轴是否登记，多出任何 finding 都红；反过来，那一条被允许的 finding 消失也红。

| 盲区 | 例子 | 为什么扫不出 |
| --- | --- | --- |
| `both-operands-non-literal-in` | `$candidate -in $known` | 同上 |
| `non-identity-variable-eq` | `$attempt -eq $runAttempt` | 两侧变量名都不携带受管 identity 后缀，避免把计数、日期和集合比较泛化为字符串门禁 |
| `ambiguous-method-with-variable-argument` | `$text.Contains($needle)` | 与 `HashSet[string]`/`Hashtable` 的成员查找同形，后者本身就是序数 |
| `variable-write-via-dynamic-provider-path` | `$target = 'variable:character'; Set-Item -Path $target …` | 动态 provider path 无法从命令 AST 证明选中了 `variable:` provider；显式 `variable:` 路径与动态 `*-Variable -Name` 仍会使局部类型证明失效 |
| `like-and-match-operators` | `-like` / `-match` | 正则与面向人的文案匹配，按 #1507 口径刻意不动 |
| `validateset-attribute` | `[ValidateSet('all','class')]` | 属性自身的比较是 culture-aware，无法从调用点收窄（`Get-BackendTestShardExcludedSelectors` 的做法是：属性放行的折叠拼法在函数体内**抛错**而不是静默选空） |
| `sort-object-via-splatted-parameters` | `Sort-Object @splat` | 仍会被报成 `sort-object`，但**比较的是哪些属性**看不见，因此 `Select-Object -Unique` 那一轴对 splat 是盲的 |

**不要把「契约存在」读成「语义被保护」**：上表是被写下来的口径差。

### 两份收口声明

| 文件 | 声明 | 由谁执行 |
| --- | --- | --- |
| `scripts/lib/TestEvidence.ps1` | 全文件按上述扫描面**零发现**，具名豁免 **1 条**：`New-NervTestEvidenceSummary` 里 `Group-Object { Get-NervRetainedSkipReason $_ }`（按人读文案**分组**；同一行的**排序**不在豁免内）。豁免按「函数名 + 表达式原文精确相等」匹配，不是子串，且必须恰好命中一处；「精确相等」是真序数（查表用 `[StringComparer]::Ordinal` 构造，裸 `@{}` 的默认比较器是 OrdinalIgnoreCase，#1509 四轮更正）。 | `scripts/tests/test-evidence.Tests.ps1` |
| `scripts/lib/BackendTestShardSelectors.ps1` | 全文件按上述扫描面**零发现**，**零豁免**——这个库处理的每一个字符串都是标识符。 | `scripts/tests/backend-test-shards.Tests.ps1` |

**两份声明的强度上界怎么读**（#1509 六轮更正）。此前这里写的是「盲区之外的构造，声明成立」，那句话把「盲区表」当成了扫描面的补集——实测不成立：`-lt`/`-le`/`-gt`/`-ge`、`[string]::Compare`/`.CompareTo`、`[StringComparer]::InvariantCulture`、`.Sort()` 无参四类都是 culture-aware，既不在覆盖轴也不在盲区表。四类现已全部补进覆盖轴。准确的上界是分两半的：

- **比较算子**这一半是穷举的：`TokenKind` 的成对家族被整体枚举并与三份名单对账，因此「不在盲区表里的比较算子一律被覆盖」成立，将来 PowerShell 新增一个也会红。
- **方法名 / 类型名**这一半是名单：`Get-NervOrdinalContract{StringMethods,ComparisonMethods,AmbiguousMethods}` 与 `[StringComparison]`/`[StringComparer]` 的后缀判据都靠具名匹配，名单之外的成员（例如某个第三方 comparer 类型、或另一个 culture-aware 的字符串方法）**既不在覆盖轴也不在盲区表**，声明对它们不作断言。

盲区表登记的是「本可以被这两半判据看到、但刻意或无力区分」的构造；名单之外的拼写不进盲区表，也不算在声明强度里。

`#1512` 的分层门禁覆盖 `scripts/verify-*`、顶层 `scripts/*.ps1`、递归 `scripts/tests/**`、独立 `scripts/tests/fixtures/**`、`scripts/{install,package,support}` 与 `scripts/lib/*`。契约实现自身进入递归 `lib` 层实扫；这是有意义且刻意的自指裁决：AST 只审查实际比较构造，注释或字符串中的 `-ceq` 等示例不自报，契约代码中的真实 culture comparison 仍会报告。每层都以临时仓库镜像中真实枚举路径的 culture comparison mutation 重跑完整门禁；tests/fixtures 重复、漏掉新增子目录或跳过一层都会失败。全量「零 finding」只能在该门禁实际通过后声明，不能以本段文字、局部扫描或 #1520 runtime 证据替代。

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
| `generate-test-evidence-baseline.ps1` | `generate` | 已受治理 | committed evidence snapshot 的唯一写入口；只接受 EvidenceRoot authority 或只读 GitHub Actions console provenance，禁止手改该文件。**#1507 起该 snapshot 是离线兜底而不是治理资产**：重生成属可选维护，任何拓扑/宿主变更都不再欠一次刷新，也没有任何门禁以它的覆盖面为判据（主耗时来源是 `update-backend-test-shard-timings.ps1` 的自动缓存）。runner 环境**按 lane 逐条**从各自 summary 读入并写进 `source.laneProvenance`（baseline schema 2），绝不取 `$first` 的值冒充整份 baseline —— 拆分理由见 `test-evidence-governance.md` 的“运行身份与逐作业环境”。 |
| `scripts/lib/TestEvidence.ps1` | `check` library | 已受治理 | 提供 TRX 解析、policy、摘要/脱敏 artifact、provenance 与 baseline 纯函数；quarantine metadata 的 policy/runtime 两调用点复用同一纯校验，runner normalization 读取显式 regex result，不依赖 PowerShell 自动 `$Matches`；provenance 分「run 身份」（8 字段跨 lane 严格等值）与「per-job 环境」（`runnerOs`/`runnerImage`/`dotnetSdk`，只逐条校形、按 lane 记录），`Assert-NervEvidenceSourceSummaries` 只返回收窄后的 run 身份对象，调用方拿不到任何 per-job 字段；`source.laneProvenance` 的 lane 集合必须与 baseline 记录的 assemblies lane 集合**双向精确相等**（缺 lane、多 lane、重复 lane 全拒），每行 `jobName` 按 `Get-NervTestEvidenceLaneJobs` 允许表逐 lane 复核（不得为空、臆造或借用兄弟 lane）；读取侧只接受 baseline `schemaVersion` 1 或 2，其余（含缺失）降级为 `unsupported-baseline-schema-version` 的 report-only 不可用而非照常比较；**耗时比较键自 #1507 起是「程序集」单键**（lane 仍作为 provenance 留在行上，只在同一程序集出现两行时用来消歧，两行都不属当前 lane 则报 `ambiguous-assembly-in-baseline` 而不是随便挑一行），因此换片、改宿主都不再失键；调用方必须先加载 `ScriptAutomation.ps1`。**#1509 起本文件的标识符/身份语义比较由一份被解析的契约兜底**（收口声明见下），走 `Test-NervOrdinalEquals`/`Get-NervOrdinalSet`/`Get-NervOrdinalSorted`/`Get-NervOrdinalGroups`/`Get-NervOrdinalSortedBy`/`Get-NervOrdinalRankedTop`/`Test-NervHasProperty` 七个原语；具名豁免现为 1 条——按人读文案**分组**的 `skipReasons`（prose 不是标识符）；同一行的**排序**不在豁免内，留存产物的字节顺序必须序数。 |
| `scripts/lib/OrdinalComparisonContract.ps1` | `check` library | 已受治理 | #1509 的序数比较扫描面：`Get-NervOrdinalComparisonFindings` 解析一个 PowerShell 文件并报出 culture-aware 的标识符比较；`Get-NervOrdinalContractCoveredAxes` / `Get-NervOrdinalContractBlindSpots` 把「能扫到什么」和「扫不到什么」都做成可枚举的数据，由调用方用合成源码逐条正反对照。具名豁免按「函数名 + 表达式原文精确相等」匹配（不是子串——子串豁免能被放宽成裸 cmdlet 名并顺带吞掉未来的调用点，#1509 三轮实证；“精确相等”由 `[StringComparer]::Ordinal` 构造的查表兑现，四轮更正了裸 `@{}` 实为 OrdinalIgnoreCase 这个口径差），且必须恰好命中一处，死豁免与过宽豁免都会红。它存在的理由是 `test-evidence.Tests.ps1` 与 `backend-test-shards.Tests.ps1` 对两个不同的库做同一句声明——两份手写扫描器等于两句不同的声明。纯函数，只解析不执行。 |
| `scripts/lib/CiWorkflowBudgets.ps1` | `check` library | 已受治理 | MAN-799 CI timeout 预算不变量的纯读取与校验函数；结构化读取 `.github/workflows/ci.yml`，step 数与原文交叉核对不上、或 job header 读不出时直接抛错（fail closed），绝不静默返回“零违规”；tier A/B 分档同样 fail closed——`if:` 只要可能在失败后仍运行（`always()`/`!cancelled()`/`failure()` 的任意合法写法，含 `${{ }}` 包裹、复合表达式、尾随注释），或无法判定，一律按 tier A 处理；`needs:`/`strategy.matrix` 等 job 级序列不计入 step 数；由 `scripts/tests/test-evidence.Tests.ps1` 调用，因此随 Script Governance job 一起在 CI 执行。 |
| `scripts/tests/test-evidence.Tests.ps1` | `check` | 已受治理/CI 接线 | fixture 证明三项硬门禁、双 SHA、baseline authority、selected-lane/shard 语义、脱敏与 normalized roundtrip，并锁定无 raw-path writer 参数、Ubuntu major normalization、quarantine 到期边界与两调用点错误契约；另以 AST 契约钉住 `scripts/lib/TestEvidence.ps1` 的序数比较边界（扫描面见「标识符比较的序数收口」；豁免表按「函数名 + 表达式原文精确相等」匹配，不按行号也不按子串，现为 1 条，且必须恰好命中一处），并用合成源码对扫描面本身做正反鉴别：每条覆盖轴必须报出、每条已登记盲区必须沉默、命名豁免不得吞掉同函数里另一处 `Group-Object`；由 Script Governance job 和 `compat-fast` 执行并保留真实退出码。 |
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
| `check-backend-test-determinism.ps1` | `check` | 已受治理 | MAN-662 后端测试确定性静态门禁；扫描 solution 内全部测试源，按 `backend/test-determinism-baseline.json`（schema 3）逐行核对已登记发现（`Task.Delay` / `StaticSetter` / `UnreachableAddress`），未登记发现即失败。每行必须声明 `classification`：`expiring-debt` 除 `ownerIssue`/`exitCondition`/`expiresOn` 外还必须带 `registeredByIssue`/`registeredOn`，登记票与 owner 票格式均为 `MAN-\d+` 或 `#\d+` 且不得相同；登记日不得晚于 UTC 今日，expiry 必须落在登记日到登记日后 45 天的**含边界**区间，并继续保持过期即失败。校验只读本地 JSON、使用 invariant `DateOnly`，不查询 GitHub/Linear，所以离线与 CI 结论一致。`permanent`（#1471 起）不带任何债务/登记元数据，仅限脚本参数 `-PermanentAllowlist` 默认值列出的 `路径=pattern=maxRows` 条目（受审计原语自身的实现与自测），必填 `rationale`；path/pattern 按 ordinal 精确匹配，`maxRows` 为正整数且同一 pair 只能声明一次。容量按通过行级校验的 permanent baseline 行数计，不按源 occurrence 或 `occurrenceCount` 计，实际行数可低于 cap；提高 cap 必须修改受治理 checker。当前容量为 `GlobalTestStateScopeTests.cs=StaticSetter=12`、`GlobalTestStateScope.cs=StaticSetter=9`、`BoundedObservationWindow.cs=Task.Delay=1`。该参数只是 checker 自测 harness 的接缝，CI 一律用默认值调用。扫描范围含 `backend/common/Testing/**`（共享测试基建），该目录在 solution 里找不到项目即失败。只读扫描，不启动服务、不写 `artifacts/`；CI 在 Script Governance job 中执行一次，不重复接入后端测试 job。 |
| `verify-backend-test-determinism.ps1` | `verify` | 已受治理 | 对四个目标测试程序集执行六轮 × 四项目：seed `man662-01`..`man662-06`、serial/parallel 交替、`MaxParallelThreads` 1/4、项目顺序逐轮旋转。以 `New-ExclusiveInvocationClaim` 原子取得 invocation 所有权，既有证据永不被 rerun 覆盖；除退出码外还跨轮比对每个项目的 total/passed/skipped/failed，静默跳过即判失败。证据写入 `artifacts/test-determinism/man-662/<invocation-id>/summary.json`（六个本地复现字段 + 逐项目结果），不产出 TRX、lane timing 或 flake trend——那些属 MAN-661。本机执行，不进 CI。 |
| `tests/check-backend-test-determinism.Tests.ps1` | `check` | 已受治理 | 用 `scripts/tests/fixtures/backend-test-determinism/**` 夹具回归扫描器本身：普通/逐字/raw/嵌套插值字符串中的可执行表达式都必须参与扫描，脱敏值不得尾部泄漏。另覆盖 schema 3 的分类规则——permanent 只在白名单内通过、白名单未覆盖的 pattern（同路径）必须失败而写明该 pattern 后必须通过、同 pair 两条 distinct permanent 行在 cap=1 时失败而 cap=2 时通过、旧两段语法/空字段/非正或非整数 cap/重复 pair/不支持 pattern 必须失败、用默认白名单校验同一行必须失败（白名单一旦被放宽即变红）、permanent 带债务/登记元数据或缺 `rationale` 失败、未知/缺失 `classification` 失败、debt 行带 `rationale` 失败；expiring 行还覆盖登记票自担保、`#1487`/`#01487` 同身份自担保、`registeredByIssue`/`registeredOn` 各自缺失与畸形、未来登记日、expiry 早于登记日、46 天超限及正好 45 天通过控制。正向 fixture 与 45/46 天控制从测试启动时的 UTC 今日动态物化，静态 JSON 只保存日期占位模板，不会自然过期。CI Script Governance job 执行。 |
| `tests/verify-backend-test-determinism.Tests.ps1` | `check` | 已受治理 | 用一次性 stubbed harness（复用真实 helper，只替换进程启动面）验证六轮契约、caller cwd 保护、invocation claim 的双进程原子性、已被占用 ID 时零项目执行，以及"退出码全零但测试结果漂移"必须失败。不调用真实 `dotnet test`。CI Script Governance job 执行。 |
| `verify-backend-test-shards.ps1` | `check` | 已受治理 | MAN-669 后端快速分片治理；校验 `scripts/backend-test-shards.json` 把每个后端测试项目恰好分类一次、solution filter 与清单逐项一致、排除选择器唯一归属 heavy lane、**每条排除都能在 MAN-661 `test-evidence-policy.json` 中找到 environment-gated 真实依赖 skip 登记**（排除清单不得成为私自绕过默认门禁的口子）、`excludedTestLanes` 与这些登记的 `requiredLane` 派生结果逐项相等、方法选择器不得是同源文件中其它成员的前缀，并用结构化 YAML 解析核对 `ci.yml`：四个 shard job 的名称/`evidenceLane`/原始 TRX 目录/TRX 前缀、MAN-661 证据采集参数（`-Lane`/`-SelectedLanes`/`-JobName`/`-CurrentTestOutcome`/`-RetentionDays 14`）、唯一且脱敏的 evidence artifact，以及 `Backend Tests` 聚合只包含四条独立成功断言。MAN-669 PR-B 追加两条构建配置硬规则：(1) **`backend/**` 下的每个 `csproj`（不只 `*.Tests.csproj`）都必须是 `backend/Nerv.IIP.sln` 的成员**——只经 `ProjectReference` 传递可达、不在解决方案 configuration map 里的项目会回落到自身默认配置，被 `--configuration Release` 的分片产出到 `bin/Debug`，且构建输出里没有任何东西会失败（`Nerv.IIP.Contracts.Mes` 真实踩过，见 `docs/architecture/backend-ci-build-strategy.md`）；(2) 拒绝把 shard 的 `solutionFilter` 直接写成 `backend/Nerv.IIP.sln`，否则会被下游 JSON 解析报成"filter 格式非法"，掩盖真正的问题——列出全部项目的 `.slnf` 则由既有的"filter 与清单逐项相等"挡住。规则 (2) 的两侧路径先经 `[System.IO.Path]::GetFullPath()` **相对仓库根规范化**再大小写不敏感比较（走查收尾；原先只剥一层 `^\./` 前缀，`backend//…`、`backend/./…`、`../` 回绕与绝对路径都会绕过该分支落回误报）。规则 (1) **有意不提供 allowlist / owner-issue 豁免通道**（与 `test-determinism-baseline.json` 的分类纪律不同）：一条登记的例外等于"明知配置错误仍然构建"，不是任何人能背的债；当前覆盖 163/163 无缺口，确有需要时的豁免路径是改本脚本（连同其契约测试）并走脚本治理。**失败时把每条发现写 stdout 再 `exit 1`（与 `check-script-governance.ps1`、`verify-solution-configuration-membership.ps1` 同形），不用 `throw`；因此调用方必须查退出码，本脚本与它的契约测试在 `ci.yml` 里各占一个独立 step，绝不可合并进同一个 `run:` 块**——两条规则的论证只写在 `docs/architecture/backend-ci-build-strategy.md` 的「走查收尾」第 3 条。只读，不执行测试。**#1507 起本脚本是纯政策门禁，完全不读耗时数据**：配平属于测量值，归 report-only 的 `report-backend-test-shard-balance.ps1`，该边界由契约测试以 **AST** 判定（不 dot-source 耗时库、不调用其任一导出函数、求值字符串里不出现耗时文件名），而不是扫原始源码文本——注释里提到耗时文件名不构成依赖；扫描范围含**门禁自己 dot-source 的每个 `scripts/lib` 库**（库集由门禁的 dot-source 语句自行推导，不硬编码），否则把耗时调用挪进 `BackendTestShardSelectors.ps1` 就能绕过。「排除选择器 → MAN-661 policy 身份」的推导抽到 `scripts/lib/BackendTestShardSelectors.ps1`：`Get-BackendTestShardPolicyIdentityMatches` 是门禁与契约测试共跑的同一份推导，`Get-BackendTestShardPolicyIdentityKey` 只把一条 match 压成可比字符串、目前只有契约测试调用（门禁要的是 match 本身，不是压平的键）；测试自带对照组（把 lane 拼回键会失键、把 `excludedTestLanes` 留在原片会被本脚本挡下），否则「零失键」等于测试在断言自己的算术。 |
| `update-backend-test-shard-timings.ps1` | `generate` | 已受治理 | #1507 分片耗时缓存刷新入口。把最近 **5** 次成功 `main` push CI run 的 retained TRX evidence artifact 聚合成 `artifacts/backend-test-shard-timings.json`（gitignored，**不入库、无哈希、不受刷新触发约束、不是任何门禁的输入**）。键是**程序集**，不含 lane/shard，因此换片不失键；同一 run 内同程序集的多行先求和再跨 run 取**中位数**（hosted runner 同 commit 抖动可达数十个百分点，一次 run 是样本不是测量）。**任何取不到数据的情况都只告警并 exit 0**——没有 gh、没有 token、离线、artifact 过期、某个 run 的证据包不可用，全部属于缓存的正常状态；把其中任何一种变成非零退出，等于把 #1507 删掉的那套人肉刷新仪式重建起来。口径与 N 值的论证写在 `scripts/lib/BackendTestShardTimings.ps1` 与 `test-evidence-governance.md` 的“耗时数据是缓存，不是受治理资产”。 |
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

后端测试确定性两个脚本共用 `artifacts/test-determinism/man-662/**`：`check` 侧只读、`verify` 侧只追加新的 invocation 目录。`backend/test-determinism-baseline.json` schema 3 的每一条 `expiring-debt` 行都以 `registeredByIssue` 记录登记变更，并用不同的 `ownerIssue` 指向一个**在本变更之外仍然存在**的责任 issue；两者均只接受 `MAN-\d+` 或 `#\d+`，按命名空间与去前导零后的数字比较身份，相同即按自担保拒绝。`registeredOn` 与 `expiresOn` 使用 `yyyy-MM-dd`，登记日不得在未来，expiry 不得早于登记日且最长 45 天（正好 45 天有效），同时保留早于 UTC 今日即过期硬失败。该上界完全由本地元数据计算，不访问 GitHub 或 Linear，避免网络与权限把政策门禁变成非确定性外部依赖。`permanent` 行是唯一没有到期日的分类，代价是它的 `路径=pattern=maxRows` 白名单与容量写在脚本里而不是 baseline 里：新增常设例外或提高容量要改脚本、过脚本治理与评审，baseline 无法自我豁免；白名单只对 ordinal 精确匹配的 pair 生效，容量统计合法 permanent baseline 行而不是 occurrence，一个文件拿到 `StaticSetter` 常设位不等于它以后的 `Thread.Sleep` 也能登记成 permanent。删除 permanent 行无需先降低 cap。

## 新脚本准入

新增脚本合入前至少满足：

1. 有 `Script-Governance` 声明块。
2. 分类准确，副作用写清楚。
3. 高风险命令通过 helper。
4. fast gate 通过。
5. 涉及数据库、容器、端口或生成产物时，同步更新对应架构文档或 runbook。
