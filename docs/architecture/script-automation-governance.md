# 脚本自动化治理已拆分

本路径是 M2 迁移兼容入口，不再承载当前脚本规则、操作步骤、事故证据或逐脚本实施账本。

- 当前脚本 Governance：[`../governance/script-automation.md`](../governance/script-automation.md)
- 当前脚本操作/排障 Runbook：[`../runbooks/script-automation.md`](../runbooks/script-automation.md)
- signal / memory 历史调查：[`../reports/investigations/script-automation-signal-memory-2026-08.md`](../reports/investigations/script-automation-signal-memory-2026-08.md)
- scanner / ordinal / 迁移演进审计：[`../reports/audits/script-automation-governance-evolution-2026-08.md`](../reports/audits/script-automation-governance-evolution-2026-08.md)
- 当前命令/参数/规则事实：`nerv.ps1 help`、目标脚本 `Get-Help`、`scripts/check-script-governance.ps1`、`scripts/lib/ScriptAutomation.ps1` 与对应测试

完整 M2-G 前正文可从 Git `26e88a62e2223ba7da2443c6471b34d971d4ad28:docs/architecture/script-automation-governance.md` 追溯。历史 ADR、Superpowers spec/plan 和冻结报告中的旧链接可在 M2 迁移期继续通过本页导航；最终删除条件由 M2-M/M4 收口。

## M2-H 冻结兼容锚

> 下列内容**不是当前脚本 Governance，也不是可维护 registry**。`scripts/tests/test-evidence.Tests.ps1` 在 M2-H 尚未迁移前仍以这些旧自然语言/迁移表片段作为合同输入；M2-G 不越权削弱 TestEvidence 合同，因此只冻结保留它实际读取的最小字面量。任何脚本新增、分类变化或 ordinal 规则变化都不得修改本节；消费者移除与本节删除由 M2-H 原子完成。

兼容路径锚：

- `update-backend-test-shard-timings.ps1`
- `report-backend-test-shard-balance.ps1`
- `scripts/lib/BackendTestShardTimings.ps1`
- `collect-test-evidence.ps1`
- `generate-test-evidence-baseline.ps1`
- `scripts/lib/TestEvidence.ps1`
- `scripts/lib/TestEvidencePolicy.ps1`
- `scripts/lib/TestEvidencePrivacy.ps1`
- `scripts/lib/TestEvidenceParsing.ps1`
- `scripts/lib/TestEvidenceArtifacts.ps1`
- `scripts/lib/TestEvidenceProvenance.ps1`
- `scripts/lib/TestEvidenceBaseline.ps1`
- `scripts/tests/test-evidence.Tests.ps1`

冻结迁移行：

| 脚本 | 分类 | 冻结状态 |
| --- | --- | --- |
| `scripts/lib/TestEvidencePolicy.ps1` | `check` library | 已受治理 |
| `scripts/lib/TestEvidencePrivacy.ps1` | `check` library | 已受治理 |
| `scripts/lib/TestEvidenceParsing.ps1` | `check` library | 已受治理 |
| `scripts/lib/TestEvidenceArtifacts.ps1` | `check` library | 已受治理 |
| `scripts/lib/TestEvidenceBaseline.ps1` | `check` library | 已受治理 |
| `scripts/lib/TestEvidenceProvenance.ps1` | `check` library | 已受治理 |

### 八份收口声明

**八份声明的强度上界怎么读**：以下只冻结 M2-G 前 TestEvidence 合同仍读取的字面量，不重新声明其当前覆盖强度；当前机器结论以 `scripts/tests/test-evidence.Tests.ps1`、ordinal producer 和 Script Governance 实际执行为准。

| 文件 | M2-G 前冻结声明 | 当时证据入口 |
| --- | --- | --- |
| `scripts/lib/TestEvidence.ps1` | 全文件按上述扫描面**零发现**，**零豁免**。 | `scripts/tests/test-evidence.Tests.ps1` |
| `scripts/lib/TestEvidencePolicy.ps1` | 全文件按上述扫描面**零发现**，**零豁免**。 | `scripts/tests/test-evidence.Tests.ps1` |
| `scripts/lib/TestEvidencePrivacy.ps1` | 全文件按上述扫描面**零发现**，**零豁免**。 | `scripts/tests/test-evidence.Tests.ps1` |
| `scripts/lib/TestEvidenceParsing.ps1` | 全文件按上述扫描面**零发现**，**零豁免**。 | `scripts/tests/test-evidence.Tests.ps1` |
| `scripts/lib/TestEvidenceArtifacts.ps1` | 全文件按上述扫描面**零发现**，具名豁免 **1 条**：`New-NervTestEvidenceSummary` 里 `Group-Object { Get-NervRetainedSkipReason $_ }` | `scripts/tests/test-evidence.Tests.ps1` |
| `scripts/lib/TestEvidenceBaseline.ps1` | 全文件按上述扫描面**零发现**，**零豁免**。 | `scripts/tests/test-evidence.Tests.ps1` |
| `scripts/lib/TestEvidenceProvenance.ps1` | 全文件按上述扫描面**零发现**，**零豁免**。 | `scripts/tests/test-evidence.Tests.ps1` |
