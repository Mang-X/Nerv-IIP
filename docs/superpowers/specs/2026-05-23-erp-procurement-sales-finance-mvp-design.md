# ERP 采购、销售与财务 MVP 设计

## 背景

ERP 是第 1 波次、第 2 波次和设备可靠性完成后剩余的业务执行服务。`backend/services/Business/Erp` 尚不存在。GitHub #137、#138 和 #139 仍处于开放状态，并在 ERP epic #76 下进行跟踪。

本规格取代已过时的 2026-05-20 ERP 单一计划，作为下一个实施波次的领域契约。

## 目标

1. 创建包含 Domain、Infrastructure 和 Web 项目的 CleanDDD ERP 服务。
2. 实施从计划建议到采购收货的 Procurement/SRM-lite。
3. 实施从商机到发货请求的 Sales/CRM-lite/OMS-lite。
4. 实施包含 AP、AR、平衡凭证和成本候选项的 Finance MVP。
5. 通过 ADR 0011 集成事件 envelope 发布 ERP 事实。
6. 保持 ERP 独立于 WMS 执行、Inventory 余额和 MES 生产任务所有权。

## 非目标

1. 不创建独立的 SRM、CRM、CPQ 或 OMS 服务。
2. 不实施完整总账、月度结账、税务报告或银行结算。
3. 不直接写入 Inventory、WMS、MES、DemandPlanning 或 Quality 数据库。
4. 不创建跨 schema 外键。
5. 不在领域聚合中存储 FileStorage 对象 key 或 signed URL（签名 URL）。

## 服务边界

| 领域 | ERP 拥有 | ERP 不拥有 |
| --- | --- | --- |
| Procurement | 请购单、RFQ、供应商报价、采购订单、采购收货 | 供应商主数据、仓库收货执行、库存余额 |
| Sales | 商机、报价、销售订单、发货订单请求 | 客户主数据、拣货/打包执行、库存分配余额 |
| Finance | AP、AR、凭证、成本候选项、记账平衡不变量 | 完整总账结账、银行对账、税务引擎 |
| Integration | 单据生命周期事实和已接受的下游引用 | 其他服务的内部命令或表状态 |

## 聚合

| 聚合 | Issue | 关键不变量 |
| --- | --- | --- |
| PurchaseRequisition | #137 | 可根据 DemandPlanning 建议创建，也可手动创建；建议引用不可变；已接受的建议必须按下游单据 ID 保证幂等。 |
| RequestForQuotation | #137 | 必须引用一个或多个供应商以及至少一个请求物料；关闭/取消后不得接收报价。 |
| SupplierQuotation | #137 | 数量和价格必须为正；供应商和 RFQ 引用不可变。 |
| PurchaseOrder | #137 | 行不得为空；收货数量不得超过未收货的订购数量；已关闭订单不可变。 |
| PurchaseReceipt | #137 | 引用 PO 和已收货行；在业务规则接受之前，被拒绝或待质检状态不得创建 AP 候选项。 |
| Opportunity | #138 | 必须提供客户引用和主题；已关闭商机不得创建新报价。 |
| Quotation | #138 | 行不得为空；已批准报价可以创建销售订单；已过期/已拒绝报价不得创建。 |
| SalesOrder | #138 | 根据报价或手工订单创建；发货数量不得超过订购数量。 |
| DeliveryOrder | #138 | 请求 WMS 出库执行；根据 WMS/Inventory 事实确认完成，不得直接变更仓库状态。 |
| AccountPayable | #139 | 未结金额等于收货金额减去已付金额；已付金额不得超过未结金额。 |
| AccountReceivable | #139 | 未结金额等于发货/发票金额减去已收金额；已收金额不得超过未结金额。 |
| JournalVoucher | #139 | 记账要求借方合计等于贷方合计；已记账凭证不可变。 |
| CostCandidate | #139 | 引用 MES 报工、Inventory 移动或 WMS 完成事实；它仍是候选项，而不是最终成本结账。 |

## 生命周期流程

### 采购

```text
DemandPlanning.PlannedPurchaseSuggested
  -> ERP.PurchaseRequisition
  -> ERP.RequestForQuotation
  -> ERP.SupplierQuotation
  -> ERP.PurchaseOrder
  -> ERP.PurchaseReceipt
  -> Quality inspection / WMS inbound / Inventory movement
  -> ERP.AccountPayable
```

### 销售

```text
ERP.Opportunity
  -> ERP.Quotation
  -> ERP.SalesOrder
  -> ERP.DeliveryOrder
  -> WMS outbound / Inventory movement
  -> ERP.AccountReceivable
```

### 财务

```text
PurchaseReceipt / WMS inbound / Inventory movement
  -> AccountPayable candidate

DeliveryOrder / WMS outbound / Inventory movement
  -> AccountReceivable candidate

MES report / finished receipt / Inventory movement
  -> CostCandidate

AP / AR / CostCandidate
  -> JournalVoucher with balanced debit and credit lines
```

## API 契约

后端 MVP 中的所有 ERP API 均使用内部服务授权。未来的 BusinessGateway 或业务控制台 facade 可以暴露面向用户的路由。

| 方法 | 路由 | 权限 | 操作 ID |
| --- | --- | --- | --- |
| POST | `/api/business/v1/erp/purchase-requisitions/from-suggestion` | `business.erp.procurement.manage` | `createErpPurchaseRequisitionFromSuggestion` |
| POST | `/api/business/v1/erp/rfqs` | `business.erp.procurement.manage` | `createErpRequestForQuotation` |
| POST | `/api/business/v1/erp/supplier-quotations` | `business.erp.procurement.manage` | `receiveErpSupplierQuotation` |
| POST | `/api/business/v1/erp/purchase-orders` | `business.erp.procurement.manage` | `createErpPurchaseOrder` |
| POST | `/api/business/v1/erp/purchase-receipts` | `business.erp.procurement.manage` | `recordErpPurchaseReceipt` |
| GET | `/api/business/v1/erp/purchase-orders` | `business.erp.procurement.read` | `listErpPurchaseOrders` |
| POST | `/api/business/v1/erp/opportunities` | `business.erp.sales.manage` | `openErpOpportunity` |
| POST | `/api/business/v1/erp/quotations` | `business.erp.sales.manage` | `createErpQuotation` |
| POST | `/api/business/v1/erp/quotations/{quotationId}/approve` | `business.erp.sales.manage` | `approveErpQuotation` |
| POST | `/api/business/v1/erp/sales-orders` | `business.erp.sales.manage` | `createErpSalesOrder` |
| POST | `/api/business/v1/erp/delivery-orders` | `business.erp.sales.manage` | `releaseErpDeliveryOrder` |
| GET | `/api/business/v1/erp/sales-orders` | `business.erp.sales.read` | `listErpSalesOrders` |
| POST | `/api/business/v1/erp/finance/payables` | `business.erp.finance.manage` | `createErpAccountPayable` |
| POST | `/api/business/v1/erp/finance/receivables` | `business.erp.finance.manage` | `createErpAccountReceivable` |
| POST | `/api/business/v1/erp/finance/cost-candidates` | `business.erp.finance.manage` | `createErpCostCandidate` |
| POST | `/api/business/v1/erp/finance/vouchers` | `business.erp.finance.manage` | `postErpJournalVoucher` |
| GET | `/api/business/v1/erp/finance/summary` | `business.erp.finance.read` | `getErpFinanceSummary` |

## 集成事件

| 事件 | 发布者 | 消费意图 |
| --- | --- | --- |
| `erp.PurchaseRequisitionCreated` | ERP | 追踪 DemandPlanning 建议接受和采购启动。 |
| `erp.PurchaseOrderReleased` | ERP | 通知 WMS/Quality/Notification 可以准备入库工作。 |
| `erp.PurchaseReceiptRecorded` | ERP | 触发 Quality 收货检验、WMS 入库和 AP 候选项逻辑。 |
| `erp.DeliveryOrderReleased` | ERP | 触发 WMS 出库履约。 |
| `erp.AccountPayableCreated` | ERP | 通知财务汇总和工作流。 |
| `erp.AccountReceivableCreated` | ERP | 通知财务汇总和工作流。 |
| `erp.CostCandidateCreated` | ERP | 呈现生产或库存成本候选事实。 |
| `erp.JournalVoucherPosted` | ERP | 呈现平衡的财务记账事实。 |

事件不得携带凭据、对象存储 key、完整附件字节或外部系统 secret。

## 权限

| 权限 | 用途 |
| --- | --- |
| `business.erp.procurement.read` | 读取采购单据。 |
| `business.erp.procurement.manage` | 创建并推进请购、RFQ、报价、采购订单和收货。 |
| `business.erp.sales.read` | 读取销售单据。 |
| `business.erp.sales.manage` | 创建并推进商机、报价、销售订单和发货订单。 |
| `business.erp.finance.read` | 读取 AP、AR、凭证和财务汇总事实。 |
| `business.erp.finance.manage` | 创建财务候选项并记账平衡凭证。 |

## 持久化规则

1. 默认 schema 为 `erp`；EF migration 历史记录表为 `erp.__EFMigrationsHistory`。
2. ID 使用仓库既有的强类型 ID 和 Guid v7 约定。
3. 货币值使用具有显式精度的 decimal 列。
4. 需要快照时，JSON snapshot 列必须使用 schema 约定注释和带版本的 payload（载荷）。
5. ERP 表不得包含 `stock_balance`、`on_hand_quantity`、`warehouse_task_state` 或等价的所有权泄漏。
6. 跨服务引用仅使用公开 ID 或单据引用。

## 验收

1. ERP 服务包含 Domain、Infrastructure 和 Web 项目、migration、测试和验证脚本。
2. Procurement 可以根据计划建议创建请购单、发出 RFQ、接收供应商报价、创建采购订单并记录收货。
3. Sales 可以创建商机和报价、批准报价、创建销售订单并释放发货订单。
4. Finance 拒绝不平衡凭证，并防止 AP/AR 超额付款或超额收款。
5. ERP 发布本规格列出的集成事件。
6. IAM seed、授权矩阵、schema 目录、实施就绪状态和 AppHost 由集成计划更新。
7. 完整链路验收可以使用 ERP 公开 API/事件，无需直接读取 ERP 表。

## Issue 映射

| Issue | 计划 |
| --- | --- |
| #137 | `docs/superpowers/plans/2026-05-23-erp-procurement-mvp.md` |
| #138 | `docs/superpowers/plans/2026-05-23-erp-sales-mvp.md` |
| #139 | `docs/superpowers/plans/2026-05-23-erp-finance-mvp.md` |
| #76/#77 集成 | `docs/superpowers/plans/2026-05-23-business-wave-3-erp-registration-verify-readiness.md` |
