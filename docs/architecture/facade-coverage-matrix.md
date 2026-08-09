# Facade 覆盖矩阵

> 机器可读的唯一事实来源：[`facade-coverage-matrix.json`](./facade-coverage-matrix.json)。
> 本文档包含治理叙述与渲染汇总；逐 endpoint 登记表位于 JSON 中，并由
> `Nerv.IIP.FacadeCoverage.Tests` 强制执行。
> 最近一次完整重新推导：MAN-475 / #841，基于 #843 facade 回填时的 `main`。

本矩阵是有代码支撑的决策记录，说明**哪些业务服务 HTTP endpoint 可由前端经 Gateway facade
访问**，以及哪些经过刻意设计而不可访问。它用于补上 X1 / #784 暴露出的结构性缺口：一项能力只有以
**两跳**交付时才可端到端使用——服务 endpoint _以及_ Gateway facade（OpenAPI snapshot → api-client
codegen → stable barrel）。历史上 Issue 验收只关注第一跳，因此缺少 facade 不会使任何门禁变红，只能通过
完整审计发现（#784 找回了 11 个此类缺口，并由 #833–#838 回填）。

它是 [`integration-event-consumption-matrix.md`](./integration-event-consumption-matrix.md) 的 HTTP facade
对应物：将“只有被归类为缺陷时，无 consumer 的 producer 才是缺陷”这一模式，应用到“没有 facade 的服务
endpoint”。无 facade 的 endpoint 仅在本应为 `exposed` 时才是缺陷；`deferred` 与 `internal` 是合法且已
登记的状态。

## 分类

每个业务服务的外部 HTTP endpoint 必须且只能属于以下一种：

- **`exposed`** —— 可通过 Gateway facade 访问。该行记录 facade 的 `gatewayOperationIds`，并且每一项
  **均由门禁验证存在于 Gateway OpenAPI snapshot 中**；因此 `exposed` 始终带有机器可检查的 facade
  证据。该能力存在于 snapshot 中，重新生成到 `@nerv-iip/api-client`（`types.gen.ts`），并从 stable barrel
  （`business-console.ts` / `console.ts`）再导出。业务 endpoint 通过 **BusinessGateway** 暴露。
- **`deferred`** —— 服务 endpoint 已存在，但 Gateway facade 尚未交付。这是跟随前端菜单阶段或指定 Issue 的
  _已跟踪_ 缺口。`deferred` 必须带有 `followUp` 说明。它诚实、可见地表达“尚未完成”，因此绝不能再被误认为
  “忘记了”。
- **`internal`** —— 按设计永不通过 Gateway 暴露。这些是服务间契约、后台 scheduler 或 connector/WCS callback
  endpoint。`internal` 必须带有 `rationale`。典型先例为仅由 Maintenance PM 消费的 IIoT
  `GET /iiot/runtime-hours`（#688）。

## DoD 契约（强制声明）

依 AGENTS.md 的“Facade 覆盖治理”，**任何新增或变更业务服务 HTTP endpoint 的 Issue，必须为每个新增/变更
endpoint 声明一个使用面结果——`exposed`、`deferred` 或 `internal`——并在同一 PR 更新本矩阵。**不存在默认值。
PR 审核须将声明与实际交付物交叉核验（facade + codegen + barrel 为 `exposed`；矩阵的
`followUp` 用于 `deferred`；矩阵的 `rationale` 用于 `internal`）。

## 门禁如何强制执行

`backend/tests/Nerv.IIP.FacadeCoverage.Tests` 在常规
`dotnet test backend/Nerv.IIP.sln` 过程中运行（因而已接入 CI）：

1. **覆盖性** —— 反射每个业务服务实时的 `*EndpointContracts.All` registry，并断言每个
   `(service, method, route)` 都存在于 JSON。**新增却未登记的 endpoint 会使构建失败。**
2. **无陈旧行** —— 每一 JSON 行必须能映射回实时 endpoint，因此改名或删除的 endpoint 不会在登记表中腐化。
3. **分类有效性** —— 值 ∈ {`exposed`,`deferred`,`internal`}，并携带必需的配套字段：`exposed` → 非空
   `gateways` **且**非空 `gatewayOperationIds`；`deferred` → `followUp`；`internal` → `rationale`。
4. **`exposed` 真实性** —— 每个 `exposed` 行的 `gatewayOperationIds` 都必须确实存在于指定 Gateway 的
   OpenAPI snapshot 中。没有可验证 facade operationId，或 facade 不在 snapshot 中的 `exposed` 行，都会失败——
   这正是 #784 的失败模式（endpoint 声称 exposed，却未交付 facade）。
5. **`deferred`/`internal` 不得被静默暴露** —— `deferred` 或 `internal` endpoint **不得**以 1:1 facade
   route 出现在 BusinessGateway snapshot 中。交付 facade 却未切换分类会失败。
6. **汇总新鲜度** —— 下方逐服务汇总表会与 JSON 断言比对，因此文档不会偏离登记表。

## 维护矩阵

- **新增 endpoint** → 在 `facade-coverage-matrix.json` 加入其行并选择分类。若为 `exposed`，交付 facade，
  并记录 facade `gatewayOperationIds`（门禁会在 snapshot 中验证它们）。
- 在 facade 交付时将 **`deferred` → `exposed`**：变更 `classification`，添加 `gateways` +
  `gatewayOperationIds`，删除 `followUp`。
- **新增业务服务** → 将其 `.Web` project reference 与 assembly name 加入门禁项目
  （`Nerv.IIP.FacadeCoverage.Tests`），使其 endpoints 被覆盖。
- 此处按数量汇总 `exposed` 行；含逐 endpoint facade operation id 的完整 415 行登记表位于 JSON 中。

## 汇总

<!-- FACADE-COVERAGE-SUMMARY:START (generated from facade-coverage-matrix.json; the FacadeCoverage.Tests gate asserts these counts) -->

| 服务                |   总数 | exposed | deferred | internal |
| ------------------- | ------: | ------: | -------: | -------: |
| Approval            |      16 |      11 |        4 |        1 |
| BarcodeLabel        |      12 |       9 |        0 |        3 |
| DemandPlanning      |      16 |      16 |        0 |        0 |
| Erp                 |      55 |      43 |       11 |        1 |
| IndustrialTelemetry |      27 |      24 |        1 |        2 |
| Inventory           |      17 |      10 |        2 |        5 |
| Maintenance         |      26 |      20 |        4 |        2 |
| MasterData          |      49 |      41 |        4 |        4 |
| Mes                 |      55 |      52 |        3 |        0 |
| ProductEngineering  |      39 |      38 |        0 |        1 |
| Quality             |      41 |      29 |       12 |        0 |
| Scheduling          |      15 |      13 |        1 |        1 |
| Wms                 |      47 |      37 |        5 |        5 |
| **Total**           | **415** | **343** |   **47** |   **25** |

<!-- FACADE-COVERAGE-SUMMARY:END -->

`exposed` 行（343）带有已验证 facade `gatewayOperationIds`，列举于 JSON 登记表中。实际的治理决策，即
`deferred` 与 `internal` 行，完整列于下方。

对于 MAN-632 可搜索目录，`listBusinessConsoleSearchableDirectory` 为每种类型映射恰好一个权威 owner 和
permission。Inventory 的 `listInventoryDirectory` 提供库位/批次/序列号；Maintenance 的
`listMaintenanceDowntimeReasons` 提供 downtime-reason 及明确的 maintenance-reason alias。MasterData 与
Quality 复用既有 exposed 读取。该 facade 保留稳定 ID、可读标签、范围上下文、owner 身份、确定性分页和明确的
不可用排序元数据；它不复制跨服务事实，也不创建业务决策评分。

对于 MAN-641，Maintenance `listMaintenanceWorkOrders` 仍保持 `exposed`，并通过 BusinessGateway
`listBusinessConsoleMaintenanceWorkOrders` 暴露。该 facade 保留遗留的 `deviceAssetId` /
CSV `deviceAssetIds` filter，并额外将重复的 `deviceAssetReferences` 值作为精确设备 code（可含逗号）或规范
`DeviceAssetId` 值转发。公开 facade 最多 200 个 reference 是应用验证上限，并不保证任意 200 个、每个 150 字符
的值都能装入 Kestrel GET request target。受限范围授权通过一个 MasterData batch RPC 解析至多 200 个规范设备，
该 RPC 由两次有界且不增长的关系读取支撑，并可能将其展开为最多 400 个已验证的 `DeviceAssetId` + code alias。
BusinessGateway 通过已认证、body-based 的 Maintenance 内部查询发送这些 alias，而非通过过长的 GET target。在调用
Maintenance 前，缺失、畸形、重复、跨范围、陈旧、停用或冲突的 identity 均会拒绝整个 batch。

对于 connector 的配置 tag 覆盖，声明是精确的：服务 operation
`reportBusinessIiotConnectorTagManifest` 因为是 Connector Host callback 而为 `internal`；服务 operation
`getBusinessIiotConnectorTagCoverage` 则为 `exposed`，并通过 Gateway operation
`getBusinessConsoleTelemetryConnectorTagCoverage` 暴露。覆盖从当前 manifest 而非 sample 开始，因此
facade 必须保留 `current` 与 `unavailable` 的区分以及 nullable sample timestamp。

对于 MAN-629 WMS 作业候选，exposed 声明有意将一个服务 operation 对应到三个 Gateway operation：
`listWmsOperationalCandidates` 映射到 `listBusinessConsoleWmsReceiptOperationalCandidates`、
`listBusinessConsoleWmsShipmentOperationalCandidates` 与
`listBusinessConsoleWmsCountOperationalCandidates`。每个 Gateway facade 均固定候选 domain，并在转发可信
principal、scope 和已授权 site 事实前检查各自 receipts、shipments 或 counts 的 read permission。响应保留
`sourceKind`、`asOfUtc`、nullable `freshnessUtc` 与 `truncated`；它是 WMS 作业事实的有界视图，而不是
MasterData/Inventory 库位目录或完整批次目录。本行是这三个公开 operation 的两跳证据；服务 endpoint 不是三份
独立的公开契约。

### 延后 endpoint（facade 已跟踪，尚未暴露）

| 服务                | 方法   | 服务 route                                                                                      | 后续事项                                                                                                                                                                          |
| ------------------- | ------ | ----------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| Approval            | POST   | `/api/business/v1/approvals/chains/{chainId}/resubmit`                                          | BusinessGateway facade 待交付；跟随审批治理（撤回、重新提交、加签、转办）的 Business Console 菜单阶段（#488）。                                                                  |
| Approval            | POST   | `/api/business/v1/approvals/chains/{chainId}/steps/{stepNo}/add-signer`                         | BusinessGateway facade 待交付；跟随审批治理的 Business Console 菜单阶段（#488）。                                                                                                  |
| Approval            | POST   | `/api/business/v1/approvals/chains/{chainId}/steps/{stepNo}/transfer`                           | BusinessGateway facade 待交付；跟随审批治理的 Business Console 菜单阶段（#488）。                                                                                                  |
| Approval            | POST   | `/api/business/v1/approvals/chains/{chainId}/withdraw`                                          | BusinessGateway facade 待交付；跟随审批治理的 Business Console 菜单阶段（#488）。                                                                                                  |
| Erp                 | POST   | `/api/business/v1/erp/purchase-orders/{purchaseOrderNo}/changes`                                | BusinessGateway facade 待交付；采购订单变更审批跟随 ERP 订单管理的 Business Console 菜单阶段。                                                                                      |
| Erp                 | POST   | `/api/business/v1/erp/purchase-orders/{purchaseOrderNo}/lines/{lineNo}/final-delivery`          | BusinessGateway facade 待交付；最终交付关闭跟随 ERP 订单管理的 Business Console 菜单阶段。                                                                                          |
| Erp                 | POST   | `/api/business/v1/erp/purchase-orders/{purchaseOrderNo}/cancel`                                 | BusinessGateway facade 待交付；采购订单取消跟随 ERP 订单管理的 Business Console 菜单阶段。                                                                                          |
| Erp                 | POST   | `/api/business/v1/erp/sales-orders/{salesOrderNo}/lines/{lineNo}`                               | BusinessGateway facade 待交付；销售订单变更跟随 ERP 订单管理的 Business Console 菜单阶段。                                                                                          |
| Erp                 | POST   | `/api/business/v1/erp/sales-orders/{salesOrderNo}/cancel`                                       | BusinessGateway facade 待交付；销售订单取消跟随 ERP 订单管理的 Business Console 菜单阶段。                                                                                          |
| Erp                 | POST   | `/api/business/v1/erp/finance/payables/payment`                                                 | BusinessGateway facade 待交付；跟随 ERP 财务的 Business Console 菜单阶段（ERP 菜单按就绪度明确分阶段）。                                                                             |
| Erp                 | POST   | `/api/business/v1/erp/finance/receivables/collection`                                           | BusinessGateway facade 待交付；跟随 ERP 财务的 Business Console 菜单阶段。                                                                                                           |
| Erp                 | POST   | `/api/business/v1/erp/supplier-invoices`                                                        | BusinessGateway facade 待交付；供应商发票 UI 是已知的 ERP 前端缺口（就绪度）。                                                                                                        |
| Erp                 | POST   | `/api/business/v1/erp/supplier-invoices/{invoiceNo}/release-payment-hold`                       | BusinessGateway facade 待交付；供应商发票付款暂扣 UI 是已知的 ERP 前端缺口。                                                                                                          |
| Erp                 | POST   | `/api/business/v1/erp/supplier-invoices/{invoiceNo}/void-payment-hold`                          | BusinessGateway facade 待交付；供应商发票付款暂扣 UI 是已知的 ERP 前端缺口。                                                                                                          |
| Erp                 | POST   | `/api/business/v1/erp/sales-return-authorizations`                                              | BusinessGateway facade 待交付；客户退货授权跟随 ERP 退货的 Business Console 菜单阶段。                                                                                                |
| IndustrialTelemetry | POST   | `/api/business/v1/iiot/tags`                                                                    | BusinessGateway facade 待交付；遥测 tag 创建跟随设备/遥测配置菜单阶段（当前仅暴露 tag 列表 GET）。                                                                                   |
| Inventory           | POST   | `/api/inventory/v1/count-tasks/{countTaskId}/cancel`                                            | BusinessGateway facade 待交付；盘点任务创建/调整已暴露，取消跟随 Inventory 盘点的 Business Console 菜单阶段。                                                                        |
| Inventory           | POST   | `/api/inventory/v1/locations`                                                                   | BusinessGateway facade 待交付；Inventory 库位主数据设置 UI 属于后续菜单阶段。                                                                                                         |
| Maintenance         | POST   | `/api/business/v1/maintenance/downtime-reasons`                                                 | BusinessGateway facade 待交付；停机原因目录配置 UI 属于后续 Maintenance 菜单阶段。                                                                                                    |
| Maintenance         | DELETE | `/api/business/v1/maintenance/downtime-reasons/{reasonCode}`                                    | BusinessGateway facade 待交付；停机原因目录配置 UI 属于后续 Maintenance 菜单阶段。                                                                                                    |
| Maintenance         | PUT    | `/api/business/v1/maintenance/downtime-reasons/{reasonCode}`                                    | BusinessGateway facade 待交付；停机原因目录配置 UI 属于后续 Maintenance 菜单阶段。                                                                                                    |
| Maintenance         | POST   | `/api/business/v1/maintenance/work-orders/{workOrderId}/repair-started`                         | BusinessGateway facade 待交付；维修开始操作跟随 CMMS 执行的 Business Console 菜单阶段。                                                                                               |
| Mes                 | POST   | `/api/business/v1/mes/material-issue-requests/{requestId}/line-side-returns`                    | BusinessGateway facade 待交付；线边退料跟随 MES 物料工作台菜单阶段。                                                                                                                  |
| Mes                 | POST   | `/api/business/v1/mes/work-orders/{workOrderId}/close`                                          | BusinessGateway facade 待交付；MES 工单关闭跟随工作台关闭操作菜单阶段（暂挂/取消已通过 #833 暴露）。                                                                                   |
| Mes                 | POST   | `/api/business/v1/mes/work-orders/{workOrderId}/engineering-change-decisions`                   | BusinessGateway facade 待交付；工单工程变更决策跟随工单 ECO 菜单阶段。                                                                                                                |
| Quality             | POST   | `/api/business/v1/quality/capas`                                                                | BusinessGateway CAPA 管理 facade 由 #677 跟踪，并解锁前端 #804。                                                                                                                       |
| Quality             | POST   | `/api/business/v1/quality/capas/{correctiveActionId}/actions`                                   | BusinessGateway CAPA 管理 facade 由 #677 跟踪，并解锁前端 #804。                                                                                                                       |
| Quality             | POST   | `/api/business/v1/quality/capas/{correctiveActionId}/actions/{correctiveActionItemId}/complete` | BusinessGateway CAPA 管理 facade 由 #677 跟踪，并解锁前端 #804。                                                                                                                       |
| Quality             | POST   | `/api/business/v1/quality/capas/{correctiveActionId}/close`                                     | BusinessGateway CAPA 管理 facade 由 #677 跟踪，并解锁前端 #804。                                                                                                                       |
| Quality             | POST   | `/api/business/v1/quality/capas/{correctiveActionId}/effectiveness`                             | BusinessGateway CAPA 管理 facade 由 #677 跟踪，并解锁前端 #804。                                                                                                                       |
| Quality             | POST   | `/api/business/v1/quality/ncrs`                                                                 | BusinessGateway facade 待交付；通用 NCR 创建跟随 Quality NCR 菜单阶段（当前仅通过 openBusinessConsoleQualityNcrFromInspection 暴露检验来源 NCR）。                                    |
| Quality             | POST   | `/api/business/v1/quality/spc/control-chart/evaluate`                                           | BusinessGateway facade 待交付；SPC 控制图读取已暴露，评估（写入）跟随 SPC 分析菜单阶段（#725）。                                                                                        |
| Quality             | POST   | `/api/business/v1/quality/spc/control-chart/lock`                                               | BusinessGateway facade 待交付；SPC 控制限锁定（写入）跟随 SPC 分析菜单阶段（#725）。                                                                                                   |
| Quality             | POST   | `/api/business/v1/quality/measuring-devices`                                                    | BusinessGateway 测量设备管理 facade 跟随 Quality 校准工作台菜单阶段。                                                                                                                  |
| Quality             | POST   | `/api/business/v1/quality/measuring-devices/{measuringDeviceId}/calibrations`                   | BusinessGateway 校准记录 facade 跟随 Quality 校准工作台菜单阶段。                                                                                                                      |
| Quality             | POST   | `/api/business/v1/quality/measuring-devices/{measuringDeviceId}/status`                         | BusinessGateway 测量设备生命周期 facade 跟随 Quality 校准工作台菜单阶段。                                                                                                              |
| Quality             | GET    | `/api/business/v1/quality/measuring-devices/calibration-dashboard`                              | BusinessGateway 校准仪表板 facade 跟随 Quality 校准工作台菜单阶段。                                                                                                                    |
| Scheduling          | POST   | `/api/business/v1/scheduling/problems/assemble`                                                 | BusinessGateway facade 待交付；APS 问题装配跟随 Scheduling 工作台菜单阶段（预览、创建、Gantt、发布已暴露）。                                                                            |
| Wms                 | POST   | `/api/business/v1/wms/inbound-orders/{inboundOrderId}/inventory-posting/retry`                  | BusinessGateway facade 待交付；WMS 入库过账重试跟随 WMS 作业菜单阶段（MES 过账重试已通过 #833 暴露）。                                                                                  |
| Wms                 | GET    | `/api/business/v1/wms/backorder-orders`                                                         | BusinessGateway facade 待交付；跟随 #707 跟踪的余下 WMS 深化和 Business Console 作业批次。                                                                                              |
| Wms                 | GET    | `/api/business/v1/wms/replenishment-tasks`                                                      | BusinessGateway facade 待交付；跟随 #707 跟踪的余下 WMS 深化和 Business Console 作业批次。                                                                                              |
| Wms                 | POST   | `/api/business/v1/wms/backorder-orders/{backorderOrderId}/close`                                | BusinessGateway facade 待交付；跟随 #707 跟踪的余下 WMS 深化和 Business Console 作业批次。                                                                                              |
| Wms                 | POST   | `/api/business/v1/wms/outbound-orders/{outboundOrderId}/cancel`                                 | BusinessGateway facade 待交付；WMS 出库取消跟随 WMS 作业菜单阶段。                                                                                                                      |

### 内部 endpoint（按设计永不暴露）

| 服务                | 方法   | 服务 route                                                                   | 理由                                                                                                                               |
| ------------------- | ------ | ---------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------- |
| Approval            | POST   | `/api/business/v1/approvals/tasks/overdue/check`                             | Approval OverdueCheck 后台扫描器调用的内部服务器时钟逾期 scheduler endpoint（#488）；不是用户操作。 |
| Erp                 | GET    | `/api/business/v1/erp/purchase-receipts/{purchaseReceiptNo}/source-document` | 供 Quality 校验收货行 SKU/qty/UOM/lot 的服务间来源单据读取契约（#77）。                |
| IndustrialTelemetry | POST   | `/api/business/v1/iiot/alarms/escalations/run`                               | 内部告警升级 scheduler endpoint（IndustrialTelemetry:AlarmEscalation 选择性启用的扫描器，#686）；不是用户操作。         |
| IndustrialTelemetry | POST   | `/api/business/v1/iiot/connector-tag-manifests`                              | Connector Host callback 上报权威连接器配置和激活事实；绝不是直接 Console 操作。        |
| Inventory           | POST   | `/api/inventory/v1/reservations`                                             | WMS 拣货任务创建所消费的服务间预留 API（#412）。                                                       |
| Inventory           | POST   | `/api/inventory/v1/reservations/fefo`                                        | WMS 消费的服务间 FEFO 预留 API（#412）。                                                                     |
| Inventory           | POST   | `/api/inventory/v1/reservations/{reservationId}/release`                     | WMS 出库取消所消费的服务间预留释放 API（#412）。                                                  |
| Inventory           | POST   | `/api/inventory/v1/status-transfers`                                         | 由 Quality 检验结果事件驱动的内部受控状态转换；不是直接 Console 操作。                      |
| Maintenance         | POST   | `/api/business/internal/v1/maintenance/work-orders/query`                    | 面向最多 400 个已验证设备 alias 的 body-based BusinessGateway 批量查询；它避免 request-target 限制、复用列表查询 handler，且不是公开 facade。 |
| Maintenance         | POST   | `/api/business/internal/v1/maintenance/work-orders/{workOrderId}/assignment-replay-probe` | 在可变目标校验前读取精确持久化派工回执的只读 BusinessGateway 查询；它绝不创建回执或改变工单状态。 |
| MasterData          | GET    | `/api/business/v1/master-data/partners/{customerCode}/credit`                | 供 ERP 销售订单信用检查消费的服务间公开信用读取（#436）。                                              |
| MasterData          | POST   | `/api/business/v1/master-data/references/resolve`                            | 供其他业务服务消费的服务间批量参考数据和权威设备 identity 快照解析（最多 200 个 reference）。 |
| MasterData          | POST   | `/api/business/v1/master-data/references/validate`                           | 供其他业务服务消费的服务间批量参考数据校验。                                               |
| Scheduling          | POST   | `/api/business/internal/v1/scheduling/order-urgency-archives/restore`        | 面向精确版本合规归档的已认证操作员恢复；绝不是 Business Console 操作。                             |
| Wms                 | POST   | `/api/business/v1/wms/inbound-orders/cancel-by-source`                       | 服务间 ERP 采购订单取消关闭匹配的 open WMS 入库预期；不是直接 Console 操作。      |
| Wms                 | POST   | `/api/business/v1/wms/warehouse-tasks/{warehouseTaskId}/complete`            | 由 WCS adapter/callback 边界消费的内部仓库任务完成 endpoint（#413）。                                   |
| Wms                 | POST   | `/api/business/v1/wms/warehouse-tasks/{warehouseTaskId}/progress`            | 由 WCS adapter/callback 边界消费的内部仓库任务进度 endpoint（#413）。                                     |
| Wms                 | GET    | `/api/business/v1/wms/wcs-dispatch-circuits`                                 | 每个 adapter/device 的 WCS circuit 状态的内部作业可见性。                                                           |
| Wms                 | POST   | `/api/business/v1/wms/wcs-dispatch-circuits/reset`                           | 面向 open WCS circuit 的内部受保护手工恢复操作。                                                                    |

## 与 #842 的关系（设备控制读取面）

#842（并行）在服务侧增加 IIoT device-control **result / history GET** endpoint 及其 BusinessGateway
facade。本矩阵的框架与门禁不依赖 #842。#842 落地时，将其新增 IIoT endpoint 加为 `exposed` 行（否则门禁会将其
标记为未登记）。现有 IIoT `POST /iiot/device-control-commands` dispatch facade（#838）已经登记为 `exposed`。
