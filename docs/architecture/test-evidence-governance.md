# 测试证据治理已拆分

本路径仅为 M2 迁移兼容导航，不承载当前规则、运行证据或测试必须匹配的文案。

- 当前 Governance：[`../governance/testing/evidence.md`](../governance/testing/evidence.md)
- 当前操作 Runbook：[`../runbooks/testing/evidence.md`](../runbooks/testing/evidence.md)
- Producer/manifest 导航：[`../reference/testing/producers.md`](../reference/testing/producers.md)
- 历史演进审计：[`../reports/audits/test-evidence-governance-evolution-2026-08.md`](../reports/audits/test-evidence-governance-evolution-2026-08.md)

完整 M2-H 前正文可从 Git `6e8747a8f93a6398c45c8eb2f2a33ad3a7b64019:docs/architecture/test-evidence-governance.md` 追溯。

## 退出约定

TestEvidence 对本页的自然语言、历史运行编号与标题字面读取已移除；旧字面块不再保留，也不迁入当前 Governance。skip、TRX、脱敏、来源证明、超时预算、畸形 baseline 与 ordinal 的行为回归仍由现有测试负责。

剩余迁移范围来自 M2-M 的消费者移交：`.github/workflows/ci.yml`、`scripts/lib/CiWorkflowBudgets.ps1`、`scripts/lib/TestEvidenceArtifacts.ps1`、`scripts/lib/BackendTestShardTimings.ps1`、`scripts/lib/BackendTestShardSelectors.ps1` 与 `scripts/tests/backend-test-shards.Tests.ps1` 的旧路径说明或选择条件。必须按实际用途迁往上述当前 Governance、Runbook、Reference 或历史审计；这份清单不是运行时 registry，最终删除以该提交的引用核查为准。

删除条件：当前代码旁说明、CI 选择条件和活跃文档不再消费本路径，冻结历史已有明确 Git/归档追溯，并实际通过既有文档结构、TestEvidence、backend-shards 与相关 Script Governance 检查。只移除文案耦合，不删除真实行为与负向断言，不把旧文案合同平移到 canonical 文档。

责任与最迟退出阶段：[NERV-1377](https://linear.app/mangax/issue/NERV-1377) 的 M4 消费者迁移与最终验收前。届时仍保留必须在该票列出实际文件、读取点和阻塞原因；不得以未具名的潜在消费者延长兼容窗口。本文不声明这些迁移或验证已经全部完成。
