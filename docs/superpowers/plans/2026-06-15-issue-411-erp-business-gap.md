# Issue 411 ERP 业务缺口实施计划

> **供代理执行者使用：**必需子技能：使用 superpowers:executing-plans，逐项实施本计划。各步骤使用复选框（`- [ ]`）语法跟踪。

**目标：**以最小但真实的采购到付款和订单到收款闭环，解决 GitHub issue #411 指出的后端 ERP 业务逻辑缺口。

**架构：**ERP 继续作为采购、销售和财务单据的事实来源；Inventory 继续作为唯一库存台账；WMS 继续作为出库执行权威。ERP 将发布公开的 Inventory/WMS 契约事件，并在自身边界内创建已匹配的 AP/AR 和明细分类账记账凭证。

**技术栈：**.NET 10、CleanDDD/NetCorePal、FastEndpoints、EF Core PostgreSQL migration、CAP 集成事件、xUnit。

---

## 范围

本计划有意闭合 #411 指出的 P0/P1 缺口，并将 P2 税务/多币种/退货/ATP 留作独立的后续 issue：

1. 创建 AP 前完成供应商发票三单匹配。
2. 采购收货发布带 SKU/UOM/site/location/quantity 的 `InventoryMovementRequested`。
3. 交货单发布带 SKU/UOM/site/location/quantity 的 WMS 出库请求契约。
4. AP/AR 到期日、账龄桶和核销 endpoint。
5. 销售订单信用检查以客户额度减去未结 AR 和有效已下达订单敞口计算。
6. 创建 AP/AR/成本候选项时自动过账明细分类账记账凭证。

## 文件

- 修改： `backend/common/Contracts/Nerv.IIP.Contracts.Inventory/InventoryIntegrationEvents.cs`
- 修改： `backend/common/Contracts/Nerv.IIP.Contracts.Wms/WmsIntegrationEvents.cs`
- 修改： `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Nerv.IIP.Business.Erp.Web.csproj`
- 修改： `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/PurchaseReceiptAggregate/PurchaseReceipt.cs`
- 修改： `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/DeliveryOrderAggregate/DeliveryOrder.cs`
- 修改： `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/SalesOrderAggregate/SalesOrder.cs`
- 修改： `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/AccountPayableAggregate/AccountPayable.cs`
- 修改： `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/AccountReceivableAggregate/AccountReceivable.cs`
- 创建： `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/SupplierInvoiceAggregate/SupplierInvoice.cs`
- 修改： `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/DomainEvents/ErpProcurementDomainEvents.cs`
- 修改： `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/DomainEvents/ErpSalesFinanceDomainEvents.cs`
- 修改： `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/ApplicationDbContext.cs`
- 修改： `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/EntityConfigurations/ErpProcurementEntityTypeConfigurations.cs`
- 修改： `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/EntityConfigurations/ErpSalesFinanceEntityTypeConfigurations.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/Migrations/` 下的 EF migration
- 修改： `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Commands/Procurement/ErpProcurementCommands.cs`
- 修改： `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Commands/Sales/ErpSalesCommands.cs`
- 修改： `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Commands/Finance/ErpFinanceCommands.cs`
- 修改： `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Queries/SalesFinance/ErpSalesFinanceQueries.cs`
- 修改： `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEvents/ErpIntegrationEvents.cs`
- 修改： `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEventConverters/ErpProcurementIntegrationEventConverters.cs`
- 修改： `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEventConverters/ErpSalesFinanceIntegrationEventConverters.cs`
- 修改： `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Endpoints/Erp/ErpProcurementEndpoints.cs`
- 修改： `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Endpoints/Erp/ErpSalesFinanceEndpoints.cs`
- 修改 `backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests/` 和 `backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/` 下的测试
- 修改： `docs/architecture/business-platform-domain-architecture.md`
- 修改： `docs/architecture/implementation-readiness.md`

## 任务

- [ ] 为供应商发票匹配、收货/交货行维度、到期日、账龄、核销和信用检查编写失败的 ERP 领域测试。
- [ ] 为 Inventory/WMS 契约事件、财务核销 endpoint、账龄查询和自动凭证编写失败的 ERP Web 测试。
- [ ] 实现领域变更和新的 SupplierInvoice 聚合。
- [ ] 仅使用公开契约实现 ERP 命令、查询、endpoint 和事件转换器。
- [ ] 为供应商发票以及 AP/AR 到期日字段添加 EF 映射和 migration。
- [ ] 更新聚焦的架构/就绪文档，说明 #411 的闭环和剩余 P2 非目标。
- [ ] 在可行情况下运行 ERP 领域/Web 测试、受 Inventory/WMS 契约变更影响的契约测试、ERP 验证脚本和 AppHost 构建。
- [ ] 提交并推送 `codex/issue-411-erp-business-gap`，然后创建包含 `Closes #411` 的 draft PR。

## 验收检查

1. 只有当 PO、收货和发票的数量/价格处于容差范围内时，才能根据已匹配的供应商发票创建 AP。
2. 采购收货同时发出 ERP 收货事实和 Inventory 移动请求事实，并携带行级库存维度。
3. 交货单同时发出 ERP 交货事实和 WMS 出库请求事实，并携带行级履约维度。
4. 可通过 endpoint 执行 AP/AR 核销；它会阻止过度核销、更新未结金额并过账平衡的核销凭证。
5. AP/AR 列表响应公开到期日和账龄桶。
6. 如果未结 AR 加有效已下达订单敞口超过客户信用额度，创建销售订单时必须拒绝该客户。
7. 创建 AP/AR/成本候选项时过账平衡的明细分类账凭证，不直接跨服务写入。

## 审核后续范围

审核后的修正将两个 #411 P1 项保留在本 PR 中，而不是仅将其记录为风险：

1. 采购订单必须从受审批约束的单据开始，而不是直接处于 `Released`。ERP 创建待处理 PO，通过公开服务契约请求 BusinessApproval，在下达前拒绝收货，并消费 Approval 完成事件以释放或取消 PO。
2. 处于 `PaymentHeld` 的供应商发票必须有最小可达路径。ERP 支持审核后释放被挂起发票以创建 AP/凭证，也支持作废被挂起发票，使其数量不再占用累计已开票数量。
