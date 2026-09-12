# 测试证据治理已迁移

本路径是 M2 兼容入口，不再承载当前测试证据规则、动态清单、操作步骤或历史运行账本。

- 当前 Governance：[`../governance/testing/evidence.md`](../governance/testing/evidence.md)
- 操作 Runbook：[`../runbooks/testing/evidence.md`](../runbooks/testing/evidence.md)
- Producer/manifest 导航：[`../reference/testing/producers.md`](../reference/testing/producers.md)
- 历史演进审计：[`../reports/audits/test-evidence-governance-evolution-2026-08.md`](../reports/audits/test-evidence-governance-evolution-2026-08.md)

完整 M2-H 前正文可从 Git `6e8747a8f93a6398c45c8eb2f2a33ad3a7b64019:docs/architecture/test-evidence-governance.md` 追溯。

## 退出约定

- 已核实的直接机器消费者：`scripts/tests/test-evidence.Tests.ps1` 中 `$governanceDocPath` / `$governanceDoc` 的读取和 `$requiredText` 循环。下面的命令、历史 run ID、标题措辞与时点数字只为该旧断言兼容，不是独立权威来源。
- 删除条件：移除针对本页自然语言与历史字面的断言，不将其复制到当前 Governance；保留 skip、zero-execution、TRX、脱敏、provenance、超时预算和畸形 baseline 等真实行为回归。代码旁说明、CI 路径选择和活跃文档的旧路径引用必须在该批次迁出；历史依据使用上方冻结审计与记录时点的 Git 树。
- 验证条件：通过既有 TestEvidence、backend-shards、Script Governance 与文档结构检查；记录实际 commit/run、执行范围及未执行项。不能仅凭文档链接绿色推断该消费者已迁出。
- 责任与最迟退出阶段：[NERV-1377](https://linear.app/mangax/issue/NERV-1377) 的 M4「TestEvidence / ScriptAutomation 消费者迁移」批次，最迟在 M4 最终验收前删除；仍有阻塞时必须在该票给出精确文件与读取点，不接受未具名的潜在消费者作为保留理由。

## 兼容字面锚（非 Governance）

> **仅供 `scripts/tests/test-evidence.Tests.ps1` 现有文档闭合断言读取。** #2400 的 Scope Gate 明确本票不修改测试代码，因此 M2-H 不借文档迁移重写这组历史合同测试。以下文本不是第二份规则、inventory 或当前状态；不得手工扩充。动态事实仍以 `docs/reference/testing/producers.md` 所列 machine producer 为准。

当前全部 75 个选择器。
当前 active core manifest 为 15 个成员、143 个冻结身份。
顺序执行 workflow 明确选择的 14 个 active core manifest member，共 135 个冻结身份。
Inventory 的 3 个；MasterData 的 5 个；Scheduling 的 6 个；AppHub 3 个；BarcodeLabel/FileStorage/Maintenance 各 1 个；IndustrialTelemetry 15 个；Quality 23 个；MES 47 个；WMS 9 个；ERP 15 个；DemandPlanning 3 个；跨业务 Acceptance 3 个。

兼容关键字：`optional`、`environment-gated`、`quarantined`、`unregistered-skip`、`illegal-quarantine`、`zero-execution`、`backend-shard-1`、`MAN-669`、`recovered-after-rerun`、`report-only`、`continue-on-error`、`Nerv-IIP Platform CI/Test Governance`、`MAN-663`、`selectedLaneResults`、`incompatible-granularity-or-duration-metric`、`single-lane collector`、`2000-01-01T00:00:00Z`、`Actions job log`、`raw TRX`。

历史命令/证据字面：

`pwsh scripts/generate-test-evidence-baseline.ps1 -EvidenceRoot artifacts/test-evidence -OutputPath scripts/test-evidence-baseline.json`

`30819675007` / `91706113150` / `9dafb512c992b240222c8d9b5ada43e4bfc8ac3d`

Timing data is a cache, not a governed asset.

兼容 violation / timing 字面：`assembly-not-in-baseline`、`ambiguous-assembly-in-baseline`、`no-compatible-assembly`、`timing-assembly-missing`、`timing-source-unavailable`、`scripts/update-backend-test-shard-timings.ps1`、`scripts/report-backend-test-shard-balance.ps1`、`There are no longer any mandatory refresh triggers`。
