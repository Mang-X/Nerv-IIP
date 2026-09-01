# 脚本治理扫描边界与比较语义演进审计（冻结）

> 类型：历史 audit
>
> 冻结基线：`main@26e88a62e2223ba7da2443c6471b34d971d4ad28`
>
> 原始混合文档：`docs/architecture/script-automation-governance.md`

本报告冻结 M2-G 前脚本治理大文档中关于 scanner 形成过程、review 轮次、兼容实测与逐脚本迁移账本的摘要。当前规则以 `docs/governance/script-automation.md` 和机器 producer 为准。

## `scripts/lib` 从“整体排除”到受管 library scope

早期 checker 曾把 `scripts/lib/*` 整体排除，导致共享 library 中的 direct command、dynamic invocation、process start、parse error 等都不被治理。#1509 的核心裁决是把默认 scan boundary 收窄成少量构造性例外，并让 library 进入扫描。

冻结基线的 exclusion 为：

- `scripts/check-script-governance.ps1`；
- `scripts/lib/ScriptAutomation.ps1`；
- `scripts/tests/*`。

这三项在冻结时由 `scripts/tests/script-governance-scan-boundary.Tests.ps1` 以机器合同守卫。当前集合如有变化，应读 checker/test，不从本报告恢复旧值。

## Dynamic Invocation / variable binding 的多轮收口

library 需要保留一种可测试的注入 seam：静态可证明为 script block 的变量允许 `& $Action`；任意字符串变量或动态命令仍应被拒绝。

M2-G 前文档记录了多轮 review 对静态证明模型的扩展与纠错，包括：

- function/file scope 的可见性与 shadowing；
- inline parameter 与 `param()` 两种函数参数结构；
- assignment left AST 的不同形状；
- scope-qualified variable；
- `Set-Variable` / `New-Variable` 的多种参数绑定方式；
- script-block literal 与包壳/类型转换；
- 无法由静态 AST 完整证明的运行期 rebinding 残余。

历史上多次出现“文档先宣称覆盖更强，下一轮 review 才发现实现还没到”的情况。因此 M2-G 后不再把庞大的 binding 拼写/残余枚举复制进 Governance；**当前覆盖强度必须由 `check-script-governance.ps1`、`ScriptVariableBinding.ps1` 和 scan-boundary tests 共同定义。**

## Ordinal comparison 收口

#1509/#1512 的调查确认：PowerShell 默认字符串比较/排序可能是 culture-aware，大小写敏感操作符也不等于 ordinal。对路径、名称、SHA、lane、status、code 等身份语义，如果依赖 culture 行为，会出现肉眼近似但机器身份被错误折叠的问题。

后续把受管比较轴收敛到 `scripts/lib/OrdinalComparisonContract.ps1` 和对应 tests。M2-G 后 Governance 只保留“身份/治理字符串使用明确 ordinal 语义”这一现态原则，具体扫描语法集合从机器 producer 读取，不再在人工文档维护 scanner 内部枚举。

## 跨平台兼容证据是时点证据

M2-G 前文档保存过 2026-05-18 的一份 Ubuntu/WSL 兼容验证记录，包含当时 OS、PowerShell、.NET SDK、Docker Compose 和 compat-fast/core verify 结果，以及 linked worktree 下 WSL Git 的环境处理。

该记录只能证明当时 head/环境的结果。它不能成为“当前所有脚本已支持 Linux/macOS”的长期声明。当前跨平台能力必须按当前 compatibility producer 在目标 OS 重新取证。

## 逐脚本迁移清单为什么退出 Governance

旧文档包含很长的“脚本 → 分类 → 当前治理状态 → 迁移要求”表，并持续吸收 TestEvidence、FullChain、determinism、CI、OpenAPI 等后续工作。随着代码继续拆分，这张表同时承担了规则、实施状态、设计说明和历史变更日志四种职责，形成第二套事实源。

M2-G 的收口方式是：

- 当前脚本分类/副作用 → 脚本自己的 `Script-Governance` header；
- checker 实际规则 → checker/library/tests；
- 当前命令/参数 → `Get-Help` / `nerv.ps1 help`；
- 当前项目状态与清理 owner → GitHub/Linear；
- 历史形成过程 → 本报告、其它冻结 Reports 和 Git 历史。

因此不再迁移旧逐脚本状态表到新 Governance/Runbook。

## 与 #2157 的边界

本次 M2-G 只重新安置文档职责和必要的 canonical-path consumer。它不证明脚本数量合理、legacy exemption 已清零，也不承担脚本/CI 影子框架的删除优先清洗。

#2157 继续拥有：

- 临时/重复脚本删除；
- 不必要 checker/fixture/CI step 收缩；
- 更广泛的 CI impact routing 修复；
- 脚本治理结构的进一步简化。

不能把历史审计里的每个发现重新包装成永久 gate。

## 当前事实入口

- [`../../governance/script-automation.md`](../../governance/script-automation.md)
- [`../../runbooks/script-automation.md`](../../runbooks/script-automation.md)
- `scripts/check-script-governance.ps1`
- `scripts/lib/ScriptAutomation.ps1`
- `scripts/lib/ScriptVariableBinding.ps1`
- `scripts/lib/OrdinalComparisonContract.ps1`
- `scripts/tests/script-governance-scan-boundary.Tests.ps1`

完整 M2-G 前正文可从 Git `26e88a62e2223ba7da2443c6471b34d971d4ad28:docs/architecture/script-automation-governance.md` 追溯。本报告完成后冻结。