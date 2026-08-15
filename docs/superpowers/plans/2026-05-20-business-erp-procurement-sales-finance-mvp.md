# 业务 ERP 采购、销售与财务 MVP 实施计划

> 仅作为历史输入。截至 2026-05-23，ERP 已拆分为 #137 采购、#138 销售和 #139 财务。请使用 `docs/superpowers/specs/2026-05-23-erp-procurement-sales-finance-mvp-design.md` 及三份 2026-05-23 ERP 计划，不要直接执行这份较早的合并计划。

> **供代理执行者使用：**必须使用子技能 superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans，逐项实施本计划。各步骤使用复选框（`- [ ]`）语法跟踪。

**目标：**构建覆盖采购/轻量级 SRM、销售/轻量级 CRM/轻量级 OMS 及财务能力的 ERP MVP。

**架构：**ERP 拥有商务单据和财务单据。采购域接受计划采购建议，并管理 RFQ、供应商报价、采购订单和采购收货。销售域管理商机、报价、销售订单和发货请求。财务域基于业务事件和库存事件创建 AR、AP、凭证及成本候选事实，同时强制保证凭证借贷平衡。

**技术栈：**.NET 10、FastEndpoints、MediatR、EF Core、Npgsql、netcorepal 集成事件、xUnit。

---

## 边界

1. 不包含完整的总账月结。
2. 本纵切不包含独立的 SRM、CRM、CPQ 或 OMS 服务。
3. ERP 不拥有仓储执行步骤或库存余额。
4. ERP 财务域不得创建借贷不平衡的凭证。

## 文件结构图

```text
backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/
  AggregatesModel/PurchaseRequisitionAggregate/PurchaseRequisition.cs
  AggregatesModel/RequestForQuotationAggregate/RequestForQuotation.cs
  AggregatesModel/SupplierQuotationAggregate/SupplierQuotation.cs
  AggregatesModel/PurchaseOrderAggregate/PurchaseOrder.cs
  AggregatesModel/PurchaseReceiptAggregate/PurchaseReceipt.cs
  AggregatesModel/OpportunityAggregate/Opportunity.cs
  AggregatesModel/QuotationAggregate/Quotation.cs
  AggregatesModel/SalesOrderAggregate/SalesOrder.cs
  AggregatesModel/DeliveryOrderAggregate/DeliveryOrder.cs
  AggregatesModel/JournalVoucherAggregate/JournalVoucher.cs
  AggregatesModel/AccountReceivableAggregate/AccountReceivable.cs
  AggregatesModel/AccountPayableAggregate/AccountPayable.cs
  AggregatesModel/CostCalculationAggregate/CostCalculation.cs

backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/
  Application/Commands/Procurement/*.cs
  Application/Commands/Sales/*.cs
  Application/Commands/Finance/*.cs
  Application/Queries/*.cs
  Application/IntegrationEvents/ErpIntegrationEvents.cs
  Endpoints/Erp/*.cs
```

## 任务 1：搭建 ERP 服务骨架

**文件：**

- 创建：`backend/services/Business/Erp/*`
- 修改：`backend/Nerv.IIP.sln`

- [ ] **步骤 1：创建项目和测试**

运行：

```powershell
dotnet new netcorepal-web -n Nerv.IIP.Business.Erp -o backend/services/Business/Erp --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
dotnet new xunit -n Nerv.IIP.Business.Erp.Domain.Tests -o backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests --framework net10.0
dotnet new xunit -n Nerv.IIP.Business.Erp.Web.Tests -o backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests --framework net10.0
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/Nerv.IIP.Business.Erp.Domain.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/Nerv.IIP.Business.Erp.Infrastructure.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Nerv.IIP.Business.Erp.Web.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests/Nerv.IIP.Business.Erp.Domain.Tests.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/Nerv.IIP.Business.Erp.Web.Tests.csproj
```

- [ ] **步骤 2：提交服务骨架**

运行：

```powershell
git add backend/Nerv.IIP.sln backend/services/Business/Erp
git commit -m "feat: scaffold erp service"
```

## 任务 2：实现采购与轻量级 SRM

**文件：**

- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/PurchaseRequisitionAggregate/PurchaseRequisition.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/RequestForQuotationAggregate/RequestForQuotation.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/SupplierQuotationAggregate/SupplierQuotation.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/PurchaseOrderAggregate/PurchaseOrder.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/PurchaseReceiptAggregate/PurchaseReceipt.cs`
- 创建：`backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests/ProcurementAggregateTests.cs`

- [ ] **步骤 1：编写预期失败的采购测试**

测试以下链路：

```csharp
var requisition = PurchaseRequisition.FromPlanningSuggestion("org-001", "env-dev", "suggestion-001", "SKU-RM-1000", 19m);
var rfq = RequestForQuotation.Create("org-001", "env-dev", requisition.Id.Value, new[] { "SUP-001", "SUP-002" }, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)));
var quotation = SupplierQuotation.Receive("org-001", "env-dev", rfq.Id.Value, "SUP-001", "SKU-RM-1000", 19m, 12.34m);
var po = PurchaseOrder.Create("org-001", "env-dev", "SUP-001", new[] { PurchaseOrderLine.Create("SKU-RM-1000", 19m, 12.34m) });
var receipt = PurchaseReceipt.Record("org-001", "env-dev", po.Id.Value, new[] { PurchaseReceiptLine.Create("SKU-RM-1000", 19m) });
```

断言供应商报价的数量和价格为正数，采购收货不得超过订购数量，并且收货会发出 `PurchaseReceiptRecordedDomainEvent`。

- [ ] **步骤 2：实现路由**

| 路由 | 权限 |
| --- | --- |
| `POST /api/business/v1/erp/purchase-requisitions/from-suggestion` | `business.erp.procurement.manage` |
| `POST /api/business/v1/erp/rfqs` | `business.erp.procurement.manage` |
| `POST /api/business/v1/erp/supplier-quotations` | `business.erp.procurement.manage` |
| `POST /api/business/v1/erp/purchase-orders` | `business.erp.procurement.manage` |
| `POST /api/business/v1/erp/purchase-receipts` | `business.erp.procurement.manage` |
| `GET /api/business/v1/erp/purchase-orders` | `business.erp.procurement.read` |

- [ ] **步骤 3：运行并提交**

运行：

```powershell
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests/Nerv.IIP.Business.Erp.Domain.Tests.csproj --no-restore --filter FullyQualifiedName~ProcurementAggregateTests
git add backend/services/Business/Erp
git commit -m "feat: add erp procurement flow"
```

预期：提交前测试通过。

## 任务 3：实现销售、轻量级 CRM 和轻量级 OMS

**文件：**

- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/OpportunityAggregate/Opportunity.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/QuotationAggregate/Quotation.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/SalesOrderAggregate/SalesOrder.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/DeliveryOrderAggregate/DeliveryOrder.cs`
- 创建：`backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests/SalesAggregateTests.cs`

- [ ] **步骤 1：编写预期失败的销售测试**

覆盖商机创建、报价审批、销售订单创建和发货下达：

```csharp
var opportunity = Opportunity.Open("org-001", "env-dev", "CUST-001", "Pump replacement");
var quotation = Quotation.Create("org-001", "env-dev", opportunity.Id.Value, "CUST-001", new[] { QuotationLine.Create("SKU-FG-1000", 2m, 1000m) });
quotation.Approve("approval-chain-002");
var order = SalesOrder.CreateFromQuotation("org-001", "env-dev", quotation.Id.Value);
var delivery = DeliveryOrder.Release("org-001", "env-dev", order.Id.Value, new[] { DeliveryOrderLine.Create("SKU-FG-1000", 2m) });
```

断言未审批的报价不能转为销售订单，并且发货数量不得超过订购数量。

- [ ] **步骤 2：添加销售路由**

| 路由 | 权限 |
| --- | --- |
| `POST /api/business/v1/erp/opportunities` | `business.erp.sales.manage` |
| `POST /api/business/v1/erp/quotations` | `business.erp.sales.manage` |
| `POST /api/business/v1/erp/sales-orders` | `business.erp.sales.manage` |
| `POST /api/business/v1/erp/delivery-orders` | `business.erp.sales.manage` |
| `GET /api/business/v1/erp/sales-orders` | `business.erp.sales.read` |

- [ ] **步骤 3：运行并提交**

运行：

```powershell
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests/Nerv.IIP.Business.Erp.Domain.Tests.csproj --no-restore --filter FullyQualifiedName~SalesAggregateTests
git add backend/services/Business/Erp
git commit -m "feat: add erp sales flow"
```

预期：提交前测试通过。

## 任务 4：实现财务 MVP

**文件：**

- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/JournalVoucherAggregate/JournalVoucher.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/AccountReceivableAggregate/AccountReceivable.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/AccountPayableAggregate/AccountPayable.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/CostCalculationAggregate/CostCalculation.cs`
- 创建：`backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests/FinanceAggregateTests.cs`

- [ ] **步骤 1：编写预期失败的财务测试**

测试断言：

```csharp
JournalVoucher.Create("org-001", "env-dev", "AP accrual")
    .AddDebit("inventory", 234.46m)
    .AddCredit("accounts-payable", 234.46m)
    .Post();
```

借方和贷方合计不一致时，`Post()` 必须失败。AR 的收款金额和 AP 的付款金额不得超过未结金额。

- [ ] **步骤 2：添加财务路由**

| 路由 | 权限 |
| --- | --- |
| `POST /api/business/v1/erp/finance/vouchers` | `business.erp.finance.manage` |
| `GET /api/business/v1/erp/finance/summary` | `business.erp.finance.read` |
| `GET /api/business/v1/erp/finance/receivables` | `business.erp.finance.read` |
| `GET /api/business/v1/erp/finance/payables` | `business.erp.finance.read` |

- [ ] **步骤 3：运行并提交**

运行：

```powershell
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests/Nerv.IIP.Business.Erp.Domain.Tests.csproj --no-restore --filter FullyQualifiedName~FinanceAggregateTests
git add backend/services/Business/Erp
git commit -m "feat: add erp finance mvp"
```

预期：提交前测试通过。

## 任务 5：添加持久化、事件、权限和验证

**文件：**

- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/ApplicationDbContext.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/EntityConfigurations/*.cs`
- 创建：`backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEvents/ErpIntegrationEvents.cs`
- 创建：`backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/ErpSchemaConventionTests.cs`
- 创建：`backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/ErpEndpointTests.cs`
- 修改：`backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs`
- 修改：`docs/architecture/database-schema-catalog.md`
- 创建：`scripts/verify-business-erp-procurement-sales-finance-mvp.ps1`

- [ ] **步骤 1：配置 schema 和事件**

使用 schema `erp`。添加集成事件 `PurchaseReceiptRecordedIntegrationEvent`、`DeliveryOrderReleasedIntegrationEvent`、`AccountPayableCreatedIntegrationEvent`、`AccountReceivableCreatedIntegrationEvent` 和 `CostCalculatedIntegrationEvent`。

- [ ] **步骤 2：写入 ERP 初始权限数据**

写入初始权限数据 `business.erp.procurement.read`、`business.erp.procurement.manage`、`business.erp.sales.read`、`business.erp.sales.manage`、`business.erp.finance.read`、`business.erp.finance.manage`。

- [ ] **步骤 3：运行完整 ERP 测试**

运行：

```powershell
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests/Nerv.IIP.Business.Erp.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/Nerv.IIP.Business.Erp.Web.Tests.csproj --no-restore
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --no-restore --filter FullyQualifiedName~IamFoundationTests
```

预期：通过。

- [ ] **步骤 4：添加验证并提交**

运行：

```powershell
scripts/verify-business-erp-procurement-sales-finance-mvp.ps1
git diff --check
git add backend/services/Business/Erp backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs docs/architecture/database-schema-catalog.md scripts/verify-business-erp-procurement-sales-finance-mvp.ps1 docs/architecture/implementation-readiness.md README.md
git commit -m "feat: complete erp procurement sales finance mvp"
```

预期：提交前验证通过。

## 自审清单

1. ERP 采购域覆盖从 MRP 建议到采购收货的链路。
2. ERP 销售域覆盖从商机到发货单的链路。
3. 财务域拒绝借贷不平衡的凭证。
4. ERP 不存储库存余额，也不存储 WMS 执行状态。
