# 测试证据治理已迁移

本路径是 M2 兼容入口，不再承载当前测试证据规则、动态清单、操作步骤或历史运行账本。

- 当前 Governance：[`../governance/testing/evidence.md`](../governance/testing/evidence.md)
- 操作 Runbook：[`../runbooks/testing/evidence.md`](../runbooks/testing/evidence.md)
- Producer/manifest 导航：[`../reference/testing/producers.md`](../reference/testing/producers.md)
- 历史演进审计：[`../reports/audits/test-evidence-governance-evolution-2026-08.md`](../reports/audits/test-evidence-governance-evolution-2026-08.md)

完整 M2-H 前正文可从 Git `6e8747a8f93a6398c45c8eb2f2a33ad3a7b64019:docs/architecture/test-evidence-governance.md` 追溯。最终删除条件由 M2-M/M4 收口。

## 兼容字面锚（非 Governance）

> **仅供 `scripts/tests/test-evidence.Tests.ps1` 现有文档闭合断言读取。** #2400 的 Scope Gate 明确本票不修改测试代码，因此 M2-H 不借文档迁移重写这组历史合同测试。以下文本不是第二份规则、inventory 或当前状态；不得手工扩充。动态事实仍以 `docs/reference/testing/producers.md` 所列 machine producer 为准。

当前全部 75 个选择器。
当前 active core manifest 为 15 个成员、144 个冻结身份。
顺序执行 workflow 明确选择的 14 个 active core manifest member，共 136 个冻结身份。
Inventory 的 3 个；MasterData 的 5 个；Scheduling 的 6 个；AppHub 3 个；BarcodeLabel/FileStorage/Maintenance 各 1 个；IndustrialTelemetry 15 个；Quality 23 个；MES 48 个；WMS 9 个；ERP 15 个；DemandPlanning 3 个；跨业务 Acceptance 3 个。

兼容关键字：`optional`、`environment-gated`、`quarantined`、`unregistered-skip`、`illegal-quarantine`、`zero-execution`、`backend-shard-1`、`MAN-669`、`recovered-after-rerun`、`report-only`、`continue-on-error`、`Nerv-IIP Platform CI/Test Governance`、`MAN-663`、`selectedLaneResults`、`incompatible-granularity-or-duration-metric`、`single-lane collector`、`2000-01-01T00:00:00Z`、`Actions job log`、`raw TRX`。

历史命令/证据字面：

`pwsh scripts/generate-test-evidence-baseline.ps1 -EvidenceRoot artifacts/test-evidence -OutputPath scripts/test-evidence-baseline.json`

`30819675007` / `91706113150` / `9dafb512c992b240222c8d9b5ada43e4bfc8ac3d`

Timing data is a cache, not a governed asset.

兼容 violation / timing 字面：`assembly-not-in-baseline`、`ambiguous-assembly-in-baseline`、`no-compatible-assembly`、`timing-assembly-missing`、`timing-source-unavailable`、`scripts/update-backend-test-shard-timings.ps1`、`scripts/report-backend-test-shard-balance.ps1`、`There are no longer any mandatory refresh triggers`。
