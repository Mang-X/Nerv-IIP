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