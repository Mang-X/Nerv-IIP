# 脚本自动化治理已拆分

本路径是 M2 迁移兼容入口，不再承载当前脚本规则、操作步骤、事故证据或逐脚本实施账本。

- 当前脚本 Governance：[`../governance/script-automation.md`](../governance/script-automation.md)
- 当前脚本操作/排障 Runbook：[`../runbooks/script-automation.md`](../runbooks/script-automation.md)
- signal / memory 历史调查：[`../reports/investigations/script-automation-signal-memory-2026-08.md`](../reports/investigations/script-automation-signal-memory-2026-08.md)
- scanner / ordinal / 迁移演进审计：[`../reports/audits/script-automation-governance-evolution-2026-08.md`](../reports/audits/script-automation-governance-evolution-2026-08.md)
- 当前命令/参数/规则事实：`nerv.ps1 help`、目标脚本 `Get-Help`、`scripts/check-script-governance.ps1`、`scripts/lib/ScriptAutomation.ps1` 与对应测试

完整 M2-G 前正文可从 Git `26e88a62e2223ba7da2443c6471b34d971d4ad28:docs/architecture/script-automation-governance.md` 追溯。历史 ADR、Superpowers spec/plan 和冻结报告中的旧链接可在迁移期继续通过本页导航；不得据此建立新的当前事实源。

## 退出约定

- 已核实的直接机器消费者：`scripts/tests/test-evidence.Tests.ps1` 中的 `$registeredScriptPath`、`$scriptGovernanceDoc` 和 `$registeredPath` 文档读取及逐字断言。它们读取下面的路径锚、迁移行与收口声明，不是脚本执行、脱敏、ordinal 或进程所有权的行为证明。
- 删除条件：在同一消费者迁移批次移除上述文案耦合，保留实际行为与负向测试；迁出代码旁说明、CI 路径选择和活跃文档的旧路径引用；冻结历史通过上方审计与记录时点的 Git 树追溯。不得把旧字面断言平移到 canonical Governance。
- 验证条件：实际通过既有 Script Governance、TestEvidence、backend-shards 及文档结构检查，并在 PR 登记精确 commit/run 和未执行项；本页本身不证明引用已清零或测试已通过。
- 责任与最迟退出阶段：[NERV-1377](https://linear.app/mangax/issue/NERV-1377) 的 M4「TestEvidence / ScriptAutomation 消费者迁移」批次，最迟在 M4 最终验收前删除。若届时仍阻塞，必须在该票列出具体文件与读取点，不能用“可能还有消费者”延长兼容窗口。

## TestEvidence 冻结兼容锚

> 下列内容**不是当前脚本 Governance，也不是可维护 registry**。`scripts/tests/test-evidence.Tests.ps1` 仍读取这些 pre-M2-G 自描述字面；#2400 的 Scope Gate 明确 M2-H 不修改测试代码，因此本节只保留既有测试真正读取的最小冻结锚，不把它们迁回 canonical Governance。任何脚本新增、分类变化或 ordinal 规则变化都不得修改本节；退出按上方约定执行。

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
