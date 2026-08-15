# WMS 执行 MVP 设计

## 目标

将 WMS 构建为入库、出库、上架、拣货、盘点执行和 WCS 适配器任务映射的仓库执行事实来源。

WMS 拥有仓库工作流状态。Inventory 仍是库存余额和库存移动的唯一事实来源。

## 当前状态

WMS 尚无服务目录。第 1 波现已提供 Inventory 库存移动/可用量和 MES 成品入库请求事实。第 2 波将加入 BarcodeLabel 以记录扫码，但在 BarcodeLabel 可用之前，WMS 可以将扫码引用保留为来源设备/值字符串。

## 所有权事实

WMS 拥有：

1. InboundOrder：收货/入库执行单头、来源单据引用和入库行。
2. PutawayTask：将入库货物移至库存位置的仓库任务。
3. OutboundOrder：发货/出库执行单头、来源单据引用和出库行。
4. PickingTask：拣取出库货物的仓库任务。
5. PackReview：出库复核和包装完成结果。
6. CountExecution：仓库盘点执行事实和差异输出。
7. WcsTask：适配器任务映射、外部任务 ID、载荷、状态、重试和诊断事实。
8. InventoryMovementRequest：WMS 所有的请求元数据，用于通过公开边界过账 Inventory 移动。

WMS 不拥有：

1. Inventory 库存余额、库存台账或库存移动事实。
2. ERP 采购、销售、发票或财务状态。
3. MES 工单或生产报工状态。
4. Quality 检验结果所有权。
5. 外部 WCS 排程内部状态。

## Inventory 边界

WMS 仅通过公开边界过账 Inventory 变更：

1. 入库完成时请求带幂等键的 Inventory 入库移动。
2. 出库完成时请求带幂等键的 Inventory 出库移动。
3. 出库拣货通过 Inventory 的公开预留 API 预留库存，并且只存储返回的公开预留 ID；出库完成时携带该 ID，使 Inventory 在过账过程中核销预留。
4. 盘点完成时可以请求 Inventory 盘点调整，或发出盘点差异事件供 Inventory/Approval 处理。
5. WMS 绝不读写 Inventory 数据表。
6. WMS 测试应当使用进程内 Inventory 客户端替身，并校验请求载荷形状。

## API 接口面

| API | 用途 | 权限 |
| --- | --- | --- |
| `POST /api/business/v1/wms/inbound-orders` | 根据采购收货、生产入库请求或人工来源创建入库单。 | `business.wms.receipts.manage` |
| `GET /api/business/v1/wms/inbound-orders` | 列出入库单。 | `business.wms.receipts.read` |
| `POST /api/business/v1/wms/inbound-orders/{inboundOrderId}/putaway-tasks` | 创建上架任务。 | `business.wms.receipts.manage` |
| `POST /api/business/v1/wms/inbound-orders/{inboundOrderId}/complete` | 完成入库并请求 Inventory 移动。 | `business.wms.receipts.manage` |
| `POST /api/business/v1/wms/outbound-orders` | 根据交货请求或人工来源创建出库单。 | `business.wms.shipments.manage` |
| `GET /api/business/v1/wms/outbound-orders` | 列出出库单。 | `business.wms.shipments.read` |
| `POST /api/business/v1/wms/outbound-orders/{outboundOrderId}/picking-tasks` | 创建拣货任务。 | `business.wms.shipments.manage` |
| `POST /api/business/v1/wms/warehouse-tasks/{warehouseTaskId}/progress` | 记录上架/拣货任务的已执行数量。 | `business.wms.receipts.manage` |
| `POST /api/business/v1/wms/warehouse-tasks/{warehouseTaskId}/complete` | 将已执行数量设为计划数量，以完成上架/拣货任务。 | `business.wms.receipts.manage` |
| `POST /api/business/v1/wms/outbound-orders/{outboundOrderId}/complete` | 完成包装复核并请求 Inventory 移动。 | `business.wms.shipments.manage` |
| `POST /api/business/v1/wms/count-executions` | 创建盘点执行。 | `business.wms.receipts.manage` |
| `POST /api/business/v1/wms/count-executions/{countExecutionId}/complete` | 完成盘点并产生差异输出。 | `business.wms.receipts.manage` |
| `POST /api/business/v1/wms/wcs-tasks/{warehouseTaskId}/dispatch` | 派发 WCS 适配器任务。 | `business.wms.automation.manage` |
| `POST /api/business/v1/wms/wcs-tasks/{externalTaskId}/complete` | 记录 WCS 完成回调。 | `business.wms.automation.manage` |
| `POST /api/business/v1/wms/wcs-tasks/{externalTaskId}/fail` | 记录 WCS 失败回调。 | `business.wms.automation.manage` |

## 规则

1. 已完成的入库单/出库单不可变。
2. 完成操作必须提供幂等键。
3. 已拣数量不得超过请求出库数量。
4. 上架数量不得超过已收货入库数量。
5. WCS 派发按仓库任务和适配器类型实现幂等。
6. WCS 失败存储诊断编码和消息，并保持可补偿。
7. 任何 WMS 数据表都不得包含现有量、可用量或库存余额列。
8. Inventory 过账失败必须通过 WMS 移动请求状态可见。
9. Inventory 业务过账拒绝由公开的 `inventory.StockMovementPostingFailed` 表示；WMS 消费该事件，并将匹配的移动请求标记为 `Failed`。
10. WMS 可以持久化 Inventory 公开预留 ID 以进行出库分配，但不得维护现有量、可用量或预留余额列。
11. WCS 完成/失败回调必须按组织、环境和外部任务 ID 匹配。

## 事件

WMS 发布符合 ADR 0011 信封格式的事件：

1. `wms.InboundOrderCompleted`
2. `wms.OutboundOrderCompleted`
3. `wms.CountExecutionCompleted`
4. `wms.WcsTaskDispatched`
5. `wms.WcsTaskFailed`

事件携带公开订单/任务引用、SKU/UOM/位置维度、数量和相关 ID。事件不得携带 Inventory 数据库 ID 或外部 WCS 密钥。

## 权限

初始权限编码：

1. `business.wms.receipts.read`
2. `business.wms.receipts.manage`
3. `business.wms.shipments.read`
4. `business.wms.shipments.manage`
5. `business.wms.automation.manage`

## 持久化

默认 schema：`wms`。

必需的数据表：

1. `inbound_orders`
2. `inbound_order_lines`
3. `outbound_orders`
4. `outbound_order_lines`
5. `warehouse_tasks`
6. `count_executions`
7. `wcs_tasks`
8. `inventory_movement_requests`

每张表和每个业务列都必须具有 schema 注释。PostgreSQL 迁移历史记录必须使用 `wms.__EFMigrationsHistory`。

## 测试

验收要求：

1. 覆盖入库完成、上架边界、出库拣货、包装复核、不可变性和幂等性的领域测试。
2. 覆盖 WCS 派发/完成/失败生命周期和诊断信息的领域测试。
3. 覆盖路由形状、授权、校验和 operation ID 的 Web 测试。
4. 校验移动请求载荷和幂等键形状的 Inventory 客户端替身测试。
5. 使用 `Nerv.IIP.Testing` 的 schema 约定测试。
6. 覆盖 WMS 事件的集成事件转换器/序列化测试。
7. 证明 WMS schema 未引入库存余额列的测试。

## 收货质量门产品流程

Business Console 收货页面以 WMS 质量门读模型作为操作员流程的事实来源。每张入库单展示
`收货 → 待检 → 合格上架/不合格隔离退供`（或 `收货 → 免检 → 上架`）等服务端事实路径：

1. 仅信任 WMS 质量门状态 `pending`、`passed`、`conditional-release`、
   `rejected` 和 `not-required`。未知值按失败关闭处理。`pending` 会阻止上架，
   并说明必须先完成检验才能执行该操作。
2. `not-required` 如实跳过检验步骤并放行该行上架；不得为免检行虚构检验任务。
3. `conditional-release` 仅以明显受限的操作形式保持上架可用，并说明这不是无条件接收。
4. `rejected` 会阻止上架，并在 WMS 已返回相关信息时显示真实隔离位置、处置原因和
   供应商退货编号。该操作还要求入库读模型的 `isReleasedForPutaway=true`；UI 绝不在本地推导该权限。
5. 只要仍有任一行处于 `pending`，检验任务链接便使用稳定的来源单据契约
   `/quality/inspection-tasks?sourceDocumentNo=<inboundOrderNo>`。已完成质量门链接至
   `/quality/inspections` 的前提是 WMS 返回真实的 `inspectionRecordId`；免检行显示不存在检验任务。
   UI 不根据 SKU 或行数据推断任务或记录。

收货变更后，页面从服务端刷新入库单、质量门和供应商退货。页面保持打开期间，相同读取会自动重新拉取，
使外部 Quality 结果无需人工重新加载即可收敛。Quality 和退货列表读取会遍历所有服务端分页，随后页面才按
真实入库单号筛选。页面绝不使用本地乐观状态声称质量门或上架已完成。启用的上架操作同时携带真实入库单号和
`inboundOrderId` 进入现有 `/wms/putaway` 流程；该流程打开创建表单，并预填服务端标识符。交接操作和创建表单
都要求 `business.wms.receipts.manage`，且创建操作要求满足 Gateway 请求校验器强制执行的正数数量。
检验任务和记录链接要求 `business.quality.inspection-records.read`；否则页面说明跨领域操作不可用，而不是暴露
失效或未授权链接。创建变更仍是完成状态的事实来源。
