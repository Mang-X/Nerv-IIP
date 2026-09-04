# Facade 覆盖治理

机器可读的唯一事实来源是 [`../../reference/api/facade-coverage-matrix.json`](../../reference/api/facade-coverage-matrix.json)。本文只规定分类、完成定义、门禁与由 JSON 渲染的汇总，不维护逐 endpoint 第二份登记表。

## 分类

每个业务服务外部 HTTP endpoint 必须且只能属于一种：

- `exposed`：已通过 BusinessGateway facade 暴露；必须登记非空 `gateways` 和 `gatewayOperationIds`，且 operationId 能在对应 Gateway OpenAPI snapshot 中验证。
- `deferred`：服务 endpoint 已存在但 facade 尚未交付；必须有明确 `followUp`。
- `internal`：按设计不通过 Gateway 暴露；必须有明确 `rationale`。

不存在默认分类。新增或变更业务服务 HTTP endpoint 的 Issue/PR 必须在同一变更中登记使用面结果；只有服务 endpoint、没有 facade 的能力不能被描述为已公开交付。

## 门禁

`Nerv.IIP.FacadeCoverage.Tests` 必须验证：业务 endpoint 对 JSON 的全覆盖、无陈旧行、分类字段有效、`exposed` 的 Gateway operationId 真实存在，以及 `deferred`/`internal` 不被静默以 1:1 facade 暴露。JSON 路径迁移后，测试/脚本只能以 reference 路径作为机器事实输入。

## 维护

- 新 endpoint：在 JSON 中新增一行并选择分类。
- `deferred` 交付 facade：切换为 `exposed`，补 `gateways`/`gatewayOperationIds`，移除 `followUp`。
- 新业务服务：把其 endpoint registry 纳入既有 FacadeCoverage 门禁。
- 修改 JSON 时同步下方由机器事实渲染的汇总；逐 endpoint 明细只留在 JSON。

## 当前汇总

<!-- FACADE-COVERAGE-SUMMARY:START (generated from ../../reference/api/facade-coverage-matrix.json; FacadeCoverage gate asserts these counts) -->

| 服务 | 总数 | exposed | deferred | internal |
| --- | ---: | ---: | ---: | ---: |
| Approval | 16 | 11 | 4 | 1 |
| BarcodeLabel | 16 | 13 | 0 | 3 |
| DemandPlanning | 16 | 16 | 0 | 0 |
| Erp | 59 | 43 | 15 | 1 |
| IndustrialTelemetry | 28 | 25 | 1 | 2 |
| Inventory | 19 | 13 | 1 | 5 |
| Maintenance | 27 | 20 | 5 | 2 |
| MasterData | 50 | 45 | 1 | 4 |
| Mes | 65 | 64 | 0 | 1 |
| ProductEngineering | 39 | 38 | 0 | 1 |
| Quality | 43 | 30 | 12 | 1 |
| Scheduling | 15 | 13 | 1 | 1 |
| Wms | 49 | 37 | 7 | 5 |
| **Total** | **442** | **368** | **47** | **27** |

<!-- FACADE-COVERAGE-SUMMARY:END -->

历史逐项说明和曾经的渲染表保存在 [`../../reports/audits/facade-coverage-matrix.md`](../../reports/audits/facade-coverage-matrix.md)，不再作为当前机器或治理事实。
