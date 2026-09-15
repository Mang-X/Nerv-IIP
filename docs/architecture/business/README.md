# Business 当前架构路由

本目录只承载业务平台的 Current Architecture。按任务只读取直接相关页面，不要把整个目录作为默认上下文；产品页面/角色/IA 去 `docs/product/`，规则去 `docs/governance/`，操作去 `docs/runbooks/`，矩阵/目录去 `docs/reference/`，历史证据去 `docs/reports/` 或 `docs/status/archive/`。

| 任务 | 当前架构 |
| --- | --- |
| 业务域划分、服务职责、依赖方向 | [`domain-architecture.md`](domain-architecture.md) |
| MasterData 字段/事实 owner | [`master-data-field-ownership.md`](master-data-field-ownership.md) |
| 流程制造 MasterData 边界 | [`master-data-process-manufacturing.md`](master-data-process-manufacturing.md) |
| 现场主体、Worker、角色与 scope | [`frontline-principal-and-scope.md`](frontline-principal-and-scope.md) |
| 订单紧急度快照保留/恢复边界 | [`scheduling-order-urgency-retention.md`](scheduling-order-urgency-retention.md) |
| 设备状态、Maintenance、MES/APS 事件流 | [`equipment-status-event-flow.md`](equipment-status-event-flow.md) |
| ERP 退货会计边界 | [`erp-return-accounting.md`](erp-return-accounting.md) |
| MES 线边收货来源分配 | [`mes-line-side-receipt-source-allocation.md`](mes-line-side-receipt-source-allocation.md) |
| 销售订单 → DemandPlanning | [`sales-order-to-demand-planning.md`](sales-order-to-demand-planning.md) |
| WMS → Inventory RPC / 幂等恢复 | [`wms-inventory-rpc-idempotency.md`](wms-inventory-rpc-idempotency.md) |

API/Gateway 契约链仍从 [`../integration/README.md`](../integration/README.md) 路由，平台级事实从 [`../overview/README.md`](../overview/README.md) / [`../platform/README.md`](../platform/README.md) 路由。
