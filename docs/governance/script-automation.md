# 脚本与自动化治理

本文承接 ADR 0010，定义 `scripts/` 当前必须遵守的可信执行边界。它回答“脚本为什么可以被信任、什么行为不得越界”，不维护命令清单、逐脚本迁移状态、某次 CI/兼容验证结果或事故形成史。

当前实现事实以 `scripts/check-script-governance.ps1`、`scripts/lib/ScriptAutomation.ps1`、相关 library、测试与脚本自身 `Get-Help` 为准；本文与机器契约冲突时先停止并核实，不通过改文档让未验证行为变成合规。

## 适用范围与分类

`script-governance` 当前允许五类：

| 分类 | 允许行为 | 禁止行为 |
| --- | --- | --- |
| `check` | 解析、静态检查、build/test/typecheck、无外部副作用的契约验证 | 启动长期服务、删除数据库、写生成代码、改变业务配置 |
| `verify` | 使用明确归属的临时服务、容器、disposable database 做真实验证 | 连接客户/生产数据、写未声明产物、失败后留下自有进程或资源 |
| `generate` | 写入声明过的生成物 | 把写操作藏在纯 verify/check 中、手改生成物、绕过 producer |
| `release-install` | 环境检查、备份、migration、seed、服务注册、健康验证与诊断 | 复用 destructive verify 习惯处理客户数据、绕过 migration history、使用默认口令、打印秘密 |
| `library` | `scripts/lib/` 下被 dot-source 的共享函数库 | 作为独立程序入口执行，或由目录外脚本冒充 library 获取放宽 |

复合分类只允许由上述值组成。分类描述能力边界，不等于当前仓库“已经有多少脚本完成迁移”。

## Script-Governance 声明

受治理脚本必须在头部保留机器可读声明：

```powershell
# Script-Governance:
#   Category: verify
#   SideEffects:
#     - <side effect or None>
#   Writes:
#     - <declared path or None>
#   Cleanup:
#     - <cleanup contract or None>
#   Requires:
#     - <runtime/tool dependency>
```

规则：

1. `Category` 必须准确反映行为；不得用 `check` 掩盖真实环境副作用。
2. `SideEffects` 必须说明会启动、停止、删除、重建或修改什么。
3. `Writes` 覆盖生成物、artifact、日志和持久化临时文件；未声明写入视为契约缺口。
4. `Cleanup` 说明脚本拥有并回收什么，以及哪些共享依赖明确保留。
5. `Requires` 只记录运行所需工具/环境，不把某次实测版本写成永久支持证明。
6. `scripts/lib/` 下文件必须声明 `library`；目录外不得借该分类取得 library scope。

精确 parser、字段校验与错误码由 `scripts/check-script-governance.ps1` 生产。

## 原生命令、Helper 与进程所有权

1. 调用 `dotnet`、`docker`、`pnpm`、`pwsh` 或启动受管子进程时，使用 `scripts/lib/ScriptAutomation.ps1` 当前公开 helper；不要在 Governance 复制 helper 函数清单，实际 API 以源码为准。
2. 长耗时/高风险命令必须有 timeout、cwd、参数摘要、exit code、duration、stdout/stderr 和 root PID 等可诊断信息。
3. stdout/stderr 必须异步或文件化排空，不能因缓冲区阻塞子进程。
4. timeout/失败时先按当前 helper 的受管停止策略回收**本次调用拥有**的进程树；无法证明所有权时 fail closed，禁止 `kill all`、按进程名扫杀或扩大到其它 worktree/session。
5. 环境变量的临时修改必须在作用域结束时恢复“原不存在 / 空字符串 / 有值”三种原始状态。
6. 被测子进程因信号退出时，失败链必须保留当前 `NERV-SIGNAL-EXIT` 可继承语义和原始 exit code；具体分类/格式由 `ScriptAutomation.ps1` 与 `scripts/tests/script-automation-signal-exit.Tests.ps1` 定义。

## Script Governance 门禁

`scripts/check-script-governance.ps1` 使用 PowerShell parser/AST，而不是自然语言 grep。当前核心边界包括：

- 入口脚本必须使用受治理 helper，适用例外由 checker 自身定义；
- 禁止绕过 wrapper 直接执行被治理的原生命令、任意动态命令或非受控进程启动；
- 每个扫描脚本必须有合法 Governance header/category；
- parse error 本身就是治理失败；
- `scripts/script-governance-baseline.json` 的 legacy exemption 只能逐“文件 × 规则”登记，不接受目录通配式豁免；当前有哪些 exemption 直接读该 producer，不在本文维护状态表。

不得为本文再造第二份 checker、规则 registry 或自然语言同步器。

## `scripts/lib` 扫描边界

默认扫描只排除以下三类，精确集合由 checker 与 `scripts/tests/script-governance-scan-boundary.Tests.ps1` 双向守住：

1. `scripts/check-script-governance.ps1`：checker 不能按其自身禁止命令字面量自检；
2. `scripts/lib/ScriptAutomation.ps1`：它是被治理调用重定向到的 wrapper；
3. `scripts/tests/*`：测试必须能执行真实进程并制造故意违规夹具。

除此之外 `scripts/lib/` 进入 library scope。library scope 的窄差异是：

- 不要求 library 自己满足入口脚本的 `MissingHelper` 规则；直接 shell-out/进程启动等危险行为仍受管；
- `& $Action` 只有在当前作用域链能够静态证明该变量为 script block seam 时才允许；字符串变量、动态表达式或无法证明的绑定必须 fail closed。

PowerShell variable binding 的完整 AST 判定、已知静态残余和 mutation matrix 由 `scripts/lib/ScriptVariableBinding.ps1`、checker 与 `scripts/tests/script-governance-scan-boundary.Tests.ps1` 生产。Governance 不复制逐轮审计出来的 binding 拼写清单；改变机器覆盖面时必须同步机器契约与对应测试。

## `scripts/tests` 的 CI 选取闭合

`scripts/tests/*.Tests.ps1` 被哪个 job 执行，是**算出来的补集**，不是手写名单：

```
发现式 runner 选中 = glob(scripts/tests/*.Tests.ps1) − 工作流 run: 体点名 − 显式出界登记
```

规则：

1. 新增契约测试的默认归宿是「被发现式 runner 执行」。忘记登记的后果是**被跑**，不是静默不跑；因此不存在「写在那里、绿着、从未执行过」的默认状态。
2. 「被工作流点名」由 `.github/workflows/**` 每个 step 的 `run` 体推导，口径必须覆盖多行 `run: |` 块；只出现在注释里不算选中。
3. 要让某个文件不被 runner 执行，必须写进出界登记。种类是**闭集**，三者互不可替代，字段要求各不相同，因此填错种类一定撞上另一种的必填/禁填：

   | 种类 | 含义 | 必填 | 禁填 |
   | --- | --- | --- | --- |
   | `nested` | 由另一个**已被 CI 选中**的测试嵌套执行 | `Parent`（其源码须真的引用该子测试） | `Tracking`、`Requirement` |
   | `excluded` | 该执行面**永久**跑不起来（真实外部依赖或必填参数） | `Requirement`（须出现在目标文件自己的 `Requires:` header 里） | `Tracking`、`Parent` |
   | `quarantine` | 应当跑、当前红、修它不属于本票，**有期限** | `Tracking`（`#<issue>`） | `Requirement`、`Parent` |

   出界不等于无需覆盖。`quarantine` 的解除方式是**删掉那一行登记**，该文件立即回到 runner 选中集合，不需要改 runner 源码。
4. 登记面自身必须可证伪：目标文件不存在、理由为空、种类不在闭集内、重复登记、嵌套条目的父测试不在 CI 上或其源码并未引用该子测试、登记项同时又被工作流点名、quarantine 无票号或票号形态不对、excluded 未给 Requirement 或 Requirement 不在目标文件声明的 `Requires:` 里——任一情形都 fail closed。
5. 扫描面塌掉（工作流目录缺失、零个工作流文件、零个测试文件、推导出的点名集合为空）必须报错，不得被读成「没有遗漏」。

### ⚠️ 覆盖边界：声明多少就只断言多少

门禁校验的是**种类闭集、字段必填/禁填矩阵、目标存在性、父子引用真实性、票号形态、Requirement 的出处**。以下各项**明确不在覆盖面内**，不要读成已被机器守住：

| 缺口 | 为什么不关 |
| --- | --- |
| 一条**干净的无票 `excluded`** 可以静默摘掉任意测试 | `Requirement` 是子串匹配，且 61 个测试里 59 个在自己的 `Requires:` 里声明了 `PowerShell 7`，`Requires:` 段内容本身也无门禁。该校验只把豁免理由从自由散文压成一条**具名、可被逐字反驳**的引用，不是一道拦阻门。「该依赖是否真的不满足」属于人工复审 |
| **issue 是否存在 / 是否已关闭** | 需联网查 GitHub，该 job 无 token，也不应为一条注释性字段引入网络依赖与非确定性。解除由跟踪票自身的验收条目驱动 |
| **嵌套**块注释 `<# 外 <# 内 #> 名字 #>` | 非贪婪匹配停在第一个 `#>`，`名字 #>` 作为正文残留而被记成选中（实测确认）。正确处理嵌套要一个带嵌套计数的扫描器，还得同时处理 here-string 与引号内的 `<#` —— #3176 / PR #3214 判定永不收敛的那条路。可抵赖性也低：嵌套块注释在 diff 里不像无辜写法 |
| **不可判定的非执行提及**：`if: false` 的 step 点名；在 PR 上从不触发的工作流（如 nightly，`on:` 只有 schedule + dispatch）里点名；`: ./x`、引号字符串里的名字、here-doc 体内的名字、`false && ./x` | 要判它们就得同时实现 GitHub 表达式求值、事件触发模型和一个 shell 语义分析器，同属永不收敛那条路。与已关掉的注释形态的关键区别是**在 diff 里一眼就是错的、不可抵赖** |
| 改 runner 自己的 CI step、或在 runner 里插 `exit 0` | 自指缴械面：任何护栏都能被改护栏本身缴械 |
| 伪造父测试并在其源码加一行引用 | 要挡住就得证明父测试真的执行了子测试，成本远超收益 |

**已关的是 run 体内 `#` 系注释的四种形态**：整行 `#`、**行尾 `#`**、`<# … #>` 单行、`<# … #>` 多行（嵌套除外，见上表）。读 `run` 时先去块注释、再逐行截掉 `#` 之后的部分。⚠️ 措辞要准：**不是**「run 体内的注释都关掉了」。

关这四种而不关上表其余项的判据是**可抵赖性**：整行注释在 diff 里是新增一行，行尾注释只是**修改一行**，`<# … #>` 在 `shell: pwsh` 的 step 里本就是合法写法 —— 三者都能伪装成无辜注释；上表其余写法在 diff 里一眼就是错的。过滤对当前仓库**行为中性**（加过滤前后成员清单逐字相同），且失败方向安全：截断只会**减少**命中 ⇒ 文件落回 runner ⇒ 被跑。

`quarantine` 与 [`testing/evidence.md`](testing/evidence.md) 的 `illegal-quarantine` 是**两套不同的隔离**：那一页管的是测试运行时的隔离元数据（含「已到期」必须 fail-closed），本页管的是**文件是否被选进执行面**。本页的 `quarantine` **没有期限字段、结构上不会到期**，靠的是跟踪票自身的验收条目销账；要把「到期」也机器化，得先决定期限从哪来（票状态需联网、硬编码日期会腐烂），那是另一张票的事。

精确实现与失败诊断由 `scripts/lib/ScriptTestSelection.ps1`、`scripts/run-script-contract-tests.ps1` 与 `scripts/tests/script-test-selection.Tests.ps1` 生产；本页不维护逐文件名单、数量或出界条目表。

## 标识符比较

脚本中表示身份或治理契约的名称、路径、SHA、lane、status、code、key、namespace 等字符串必须使用明确的 ordinal 语义，不能依赖 PowerShell/.NET 默认 culture-aware 比较或排序。

哪些语法轴被自动扫描，以 `scripts/lib/OrdinalComparisonContract.ps1` 及其契约测试为准；本文不维护扫描器逐轮扩展历史，也不承诺超出机器 producer 的覆盖强度。

## 副作用、真实依赖与 Session

1. `verify`/fullstack/兼容/发布类脚本只能操作明确属于本次 invocation/session 的临时或受控目标。
2. database、container、volume、process、port、artifact、seed 和 credential 的 ownership 必须可追；不能用名称前缀推测所有权后批量删除。
3. disposable 资源的清理进入 `finally` 或等价强制收口；失败也必须 best-effort 精确清理并保留诊断。
4. artifact/evidence 的收集是观察面，不得改变被测 lane 的成功/失败结论；采集失败应按当前 producer 的 best-effort 契约记录 unavailable/原因。
5. 交互式启动用于诊断时，交接前必须停止当前会话拥有的资源；长期服务由正式部署/运行入口拥有，不由 verify 脚本偷偷常驻。
6. 当前 fullstack/demo/compat 场景、参数和状态命令必须从 `nerv.ps1 help`、目标脚本 `Get-Help` 与源码读取，禁止在本页维护易漂移场景清单。

具体操作步骤见 [`../runbooks/script-automation.md`](../runbooks/script-automation.md)。

## 日志、证据与秘密

1. 长耗时动作必须留下可定位的 stdout/stderr、exit code、duration、目标摘要与 cleanup 结果。
2. 失败信息优先保留最内层可验证原因，不得把 signal/timeout/transport failure 泛化成“断言失败”。
3. token、password、client secret、完整 connection string、authorization header、客户密钥和其它敏感输入不得进入 retained log、manifest、artifact、报告或 committed 文件。
4. 证据必须明确 commit/run/session/target/profile 等非敏感身份；只能证明实际执行的 lane 和范围。
5. 运行 artifact 默认不提交仓库；需长期保留的审计结论进入 `docs/reports/`，并与秘密分离。

## 跨平台声明

1. 仓库脚本以 PowerShell 7 `pwsh` 口径编写，不因此自动获得“已支持 macOS/Linux/Windows”的声明。
2. 跨平台支持必须在目标 OS 上通过当前 `scripts/check-script-compatibility.ps1` / 相关 producer 所定义的验证层次，并保留实际版本、命令、退出码和日志证据。
3. `-AllowWindows`、fast-only 或等价 smoke 若 producer 明确为本地诊断，只能报告对应证明范围。
4. 历史某次 Ubuntu/WSL/macOS 成功记录不能替代当前代码和当前运行的兼容证据。

## Release / Install

客户数据上的 migration、backup、restore、seed 与发布操作同时受本页和 [`../runbooks/database-release.md`](../runbooks/database-release.md) 约束。`verify` 入口、Development AutoMigrate、临时数据库重建或测试 seed 不能升级成 release-install 行为。

## 新脚本与规则变更准入

新增/修改脚本至少满足：

1. header、分类、副作用、Writes、Cleanup、Requires 与实际行为一致；
2. 原生命令/进程行为走现有 helper，不能为单票再造近似 wrapper；
3. ownership 与 cleanup 可证明，秘密不会进入 retained evidence；
4. 运行 `pwsh scripts/check-script-governance.ps1`，再运行与改动直接相关的现有契约测试；
5. 若改变 checker、scan boundary、binding/ordinal contract 或 CI routing producer，必须运行对应已有合同测试并按 CI impact plan 验证；
6. 只报告实际执行成功的 lane，policy skip/未运行项明确写出。

出现以下情况应停止而不是加临时豁免：目标/所有权不明确、cleanup 无法限定、命令需要绕过 wrapper、秘密会写入 artifact、脚本类别与真实行为不符、需要连接生产/客户数据却没有 release-install 边界、或需要修改当前 checker/CI 语义但任务并不拥有该规则。

## 历史与其它 Owner

- pre-M2-G 的 signal / memory 事故证据见 [`../reports/investigations/script-automation-signal-memory-2026-08.md`](../reports/investigations/script-automation-signal-memory-2026-08.md)。
- scan boundary、dynamic invocation 与 ordinal scanner 的形成过程见 [`../reports/audits/script-automation-governance-evolution-2026-08.md`](../reports/audits/script-automation-governance-evolution-2026-08.md)。
- 完整迁移前正文可从 Git `26e88a62e2223ba7da2443c6471b34d971d4ad28:docs/architecture/script-automation-governance.md` 追溯。
- #2157 拥有脚本/CI 影子框架的删除优先清洗与更广 CI routing 修复；M2-G 不据此新增脚本、checker、fixture 或 CI step。
- 测试有效性、测试证据和真实依赖测试 lane 的文档职责由 M2-H owner 独立收口，本页不抢占。