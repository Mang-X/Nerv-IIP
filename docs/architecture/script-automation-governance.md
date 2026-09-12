# 脚本自动化治理已拆分

本路径仅为 M2 迁移兼容导航，不承载当前脚本规则、操作步骤、事故证据或逐脚本实施账本。

- 当前脚本 Governance：[`../governance/script-automation.md`](../governance/script-automation.md)
- 当前操作/排障 Runbook：[`../runbooks/script-automation.md`](../runbooks/script-automation.md)
- signal / memory 历史调查：[`../reports/investigations/script-automation-signal-memory-2026-08.md`](../reports/investigations/script-automation-signal-memory-2026-08.md)
- scanner / ordinal / 迁移演进审计：[`../reports/audits/script-automation-governance-evolution-2026-08.md`](../reports/audits/script-automation-governance-evolution-2026-08.md)
- 当前命令、参数与规则事实：`nerv.ps1 help`、目标脚本 `Get-Help`、`scripts/check-script-governance.ps1`、`scripts/lib/ScriptAutomation.ps1` 与对应测试

完整 M2-G 前正文可从 Git `26e88a62e2223ba7da2443c6471b34d971d4ad28:docs/architecture/script-automation-governance.md` 追溯。冻结 ADR、Superpowers spec/plan 和报告不因路径清理而改写其历史语义。

## 退出约定

TestEvidence 对本页路径锚、迁移表和收口声明的逐字读取已移除；这些冻结字面不再保留，不恢复成可维护 registry，也不平移到当前 Governance。脚本执行、脱敏、ordinal、进程所有权及负向行为回归继续由原有 producer 和测试负责。

后续迁移仍需核对 `scripts/check-script-governance.ps1`、`scripts/lib/ScriptVariableBinding.ps1`、`scripts/verify-backend-test-shards.ps1` 与 `scripts/tests/script-governance-scan-boundary.Tests.ps1` 的代码旁说明，以及 CI 和活跃文档的实际入链；当前规则指向 Governance，历史形成过程指向相应报告，不把两者混为一处事实源。

删除条件：当前脚本、CI 与活跃文档不再引用本路径，历史已有明确 Git/归档追溯，并实际通过既有文档结构、Script Governance、TestEvidence 与相关 backend-shards 检查。删除 PR 必须记录精确 head/run、执行结果和未执行项，不能仅凭链接或聚合绿色推断行为验证完成。

责任与最迟退出阶段：[NERV-1377](https://linear.app/mangax/issue/NERV-1377) 的 M4 消费者迁移与最终验收前。若仍保留，必须在该票列出精确文件、读取点和阻塞原因；不能以未具名的潜在消费者延长兼容窗口。本文不声明上述全部引用已迁出。
