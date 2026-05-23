# Business ERP Procurement Sales Finance MVP Implementation Plan

> Historical input only. As of 2026-05-23, ERP is split into #137 Procurement, #138 Sales and #139 Finance. Use `docs/superpowers/specs/2026-05-23-erp-procurement-sales-finance-mvp-design.md` plus the three 2026-05-23 ERP plans instead of executing this older combined plan directly.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build ERP MVP covering Procurement/SRM-lite, Sales/CRM-lite/OMS-lite and Finance MVP.

**Architecture:** ERP owns commercial and financial documents. Procurement accepts planning purchase suggestions, manages RFQ, supplier quotation, purchase order and receipt. Sales manages opportunity, quotation, sales order and delivery request. Finance creates AR/AP/voucher/cost candidate facts from business and inventory events while enforcing balanced vouchers.

**Tech Stack:** .NET 10, FastEndpoints, MediatR, EF Core, Npgsql, netcorepal integration events, xUnit.

---

## Boundaries

1. No complete general ledger month-end close.
2. No standalone SRM, CRM, CPQ or OMS service in this slice.
3. ERP does not own warehouse execution steps or stock balance.
4. ERP Finance must not create unbalanced vouchers.

## File Structure Map

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

## Task 1: Scaffold ERP Service

**Files:**

- Create: `backend/services/Business/Erp/*`
- Modify: `backend/Nerv.IIP.sln`

- [ ] **Step 1: Create projects and tests**

Run:

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

- [ ] **Step 2: Commit scaffold**

Run:

```powershell
git add backend/Nerv.IIP.sln backend/services/Business/Erp
git commit -m "feat: scaffold erp service"
```

## Task 2: Implement Procurement and SRM-lite

**Files:**

- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/PurchaseRequisitionAggregate/PurchaseRequisition.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/RequestForQuotationAggregate/RequestForQuotation.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/SupplierQuotationAggregate/SupplierQuotation.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/PurchaseOrderAggregate/PurchaseOrder.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/PurchaseReceiptAggregate/PurchaseReceipt.cs`
- Create: `backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests/ProcurementAggregateTests.cs`

- [ ] **Step 1: Write failing procurement tests**

Test this chain:

```csharp
var requisition = PurchaseRequisition.FromPlanningSuggestion("org-001", "env-dev", "suggestion-001", "SKU-RM-1000", 19m);
var rfq = RequestForQuotation.Create("org-001", "env-dev", requisition.Id.Value, new[] { "SUP-001", "SUP-002" }, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7)));
var quotation = SupplierQuotation.Receive("org-001", "env-dev", rfq.Id.Value, "SUP-001", "SKU-RM-1000", 19m, 12.34m);
var po = PurchaseOrder.Create("org-001", "env-dev", "SUP-001", new[] { PurchaseOrderLine.Create("SKU-RM-1000", 19m, 12.34m) });
var receipt = PurchaseReceipt.Record("org-001", "env-dev", po.Id.Value, new[] { PurchaseReceiptLine.Create("SKU-RM-1000", 19m) });
```

Assert supplier quotation quantity and price are positive, purchase order cannot be received beyond ordered quantity, and receipt emits `PurchaseReceiptRecordedDomainEvent`.

- [ ] **Step 2: Implement routes**

| Route | Permission |
| --- | --- |
| `POST /api/business/v1/erp/purchase-requisitions/from-suggestion` | `business.erp.procurement.manage` |
| `POST /api/business/v1/erp/rfqs` | `business.erp.procurement.manage` |
| `POST /api/business/v1/erp/supplier-quotations` | `business.erp.procurement.manage` |
| `POST /api/business/v1/erp/purchase-orders` | `business.erp.procurement.manage` |
| `POST /api/business/v1/erp/purchase-receipts` | `business.erp.procurement.manage` |
| `GET /api/business/v1/erp/purchase-orders` | `business.erp.procurement.read` |

- [ ] **Step 3: Run and commit**

Run:

```powershell
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests/Nerv.IIP.Business.Erp.Domain.Tests.csproj --no-restore --filter FullyQualifiedName~ProcurementAggregateTests
git add backend/services/Business/Erp
git commit -m "feat: add erp procurement flow"
```

Expected: tests pass before commit.

## Task 3: Implement Sales, CRM-lite and OMS-lite

**Files:**

- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/OpportunityAggregate/Opportunity.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/QuotationAggregate/Quotation.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/SalesOrderAggregate/SalesOrder.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/DeliveryOrderAggregate/DeliveryOrder.cs`
- Create: `backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests/SalesAggregateTests.cs`

- [ ] **Step 1: Write failing sales tests**

Cover opportunity creation, quotation approval, sales order creation and delivery release:

```csharp
var opportunity = Opportunity.Open("org-001", "env-dev", "CUST-001", "Pump replacement");
var quotation = Quotation.Create("org-001", "env-dev", opportunity.Id.Value, "CUST-001", new[] { QuotationLine.Create("SKU-FG-1000", 2m, 1000m) });
quotation.Approve("approval-chain-002");
var order = SalesOrder.CreateFromQuotation("org-001", "env-dev", quotation.Id.Value);
var delivery = DeliveryOrder.Release("org-001", "env-dev", order.Id.Value, new[] { DeliveryOrderLine.Create("SKU-FG-1000", 2m) });
```

Assert unapproved quotation cannot become a sales order and delivery quantity cannot exceed ordered quantity.

- [ ] **Step 2: Add sales routes**

| Route | Permission |
| --- | --- |
| `POST /api/business/v1/erp/opportunities` | `business.erp.sales.manage` |
| `POST /api/business/v1/erp/quotations` | `business.erp.sales.manage` |
| `POST /api/business/v1/erp/sales-orders` | `business.erp.sales.manage` |
| `POST /api/business/v1/erp/delivery-orders` | `business.erp.sales.manage` |
| `GET /api/business/v1/erp/sales-orders` | `business.erp.sales.read` |

- [ ] **Step 3: Run and commit**

Run:

```powershell
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests/Nerv.IIP.Business.Erp.Domain.Tests.csproj --no-restore --filter FullyQualifiedName~SalesAggregateTests
git add backend/services/Business/Erp
git commit -m "feat: add erp sales flow"
```

Expected: tests pass before commit.

## Task 4: Implement Finance MVP

**Files:**

- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/JournalVoucherAggregate/JournalVoucher.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/AccountReceivableAggregate/AccountReceivable.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/AccountPayableAggregate/AccountPayable.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/CostCalculationAggregate/CostCalculation.cs`
- Create: `backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests/FinanceAggregateTests.cs`

- [ ] **Step 1: Write failing finance tests**

Tests assert:

```csharp
JournalVoucher.Create("org-001", "env-dev", "AP accrual")
    .AddDebit("inventory", 234.46m)
    .AddCredit("accounts-payable", 234.46m)
    .Post();
```

`Post()` fails when debit and credit totals differ. AR and AP received/paid amount cannot exceed open amount.

- [ ] **Step 2: Add finance routes**

| Route | Permission |
| --- | --- |
| `POST /api/business/v1/erp/finance/vouchers` | `business.erp.finance.manage` |
| `GET /api/business/v1/erp/finance/summary` | `business.erp.finance.read` |
| `GET /api/business/v1/erp/finance/receivables` | `business.erp.finance.read` |
| `GET /api/business/v1/erp/finance/payables` | `business.erp.finance.read` |

- [ ] **Step 3: Run and commit**

Run:

```powershell
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests/Nerv.IIP.Business.Erp.Domain.Tests.csproj --no-restore --filter FullyQualifiedName~FinanceAggregateTests
git add backend/services/Business/Erp
git commit -m "feat: add erp finance mvp"
```

Expected: tests pass before commit.

## Task 5: Add Persistence, Events, Permissions and Verification

**Files:**

- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/ApplicationDbContext.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/EntityConfigurations/*.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEvents/ErpIntegrationEvents.cs`
- Create: `backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/ErpSchemaConventionTests.cs`
- Create: `backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/ErpEndpointTests.cs`
- Modify: `backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs`
- Modify: `docs/architecture/database-schema-catalog.md`
- Create: `scripts/verify-business-erp-procurement-sales-finance-mvp.ps1`

- [ ] **Step 1: Configure schema and events**

Use schema `erp`. Add integration events `PurchaseReceiptRecordedIntegrationEvent`, `DeliveryOrderReleasedIntegrationEvent`, `AccountPayableCreatedIntegrationEvent`, `AccountReceivableCreatedIntegrationEvent` and `CostCalculatedIntegrationEvent`.

- [ ] **Step 2: Seed ERP permissions**

Seed `business.erp.procurement.read`, `business.erp.procurement.manage`, `business.erp.sales.read`, `business.erp.sales.manage`, `business.erp.finance.read`, `business.erp.finance.manage`.

- [ ] **Step 3: Run full ERP tests**

Run:

```powershell
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests/Nerv.IIP.Business.Erp.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/Nerv.IIP.Business.Erp.Web.Tests.csproj --no-restore
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --no-restore --filter FullyQualifiedName~IamFoundationTests
```

Expected: PASS.

- [ ] **Step 4: Add verification and commit**

Run:

```powershell
scripts/verify-business-erp-procurement-sales-finance-mvp.ps1
git diff --check
git add backend/services/Business/Erp backend/services/Iam/src/Nerv.IIP.Iam.Web/Application/Seed/IamSeedService.cs docs/architecture/database-schema-catalog.md scripts/verify-business-erp-procurement-sales-finance-mvp.ps1 docs/architecture/implementation-readiness.md README.md
git commit -m "feat: complete erp procurement sales finance mvp"
```

Expected: verification passes before commit.

## Self-Review Checklist

1. ERP Procurement covers MRP suggestion to purchase receipt.
2. ERP Sales covers opportunity to delivery order.
3. Finance rejects unbalanced vouchers.
4. ERP stores no stock balance and no WMS execution state.
