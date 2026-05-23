# ERP Procurement MVP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement #137 by creating the ERP service scaffold and Procurement/SRM-lite flow from planning suggestion to purchase receipt.

**Architecture:** ERP is a CleanDDD business service under `backend/services/Business/Erp`. This plan creates the service base and procurement facts only. Sales, Finance, AppHost registration and final ERP aggregation have separate plans.

**Tech Stack:** .NET 10, NetCorePal CleanDDD template, FastEndpoints, EF Core PostgreSQL, xUnit, ADR 0011 integration event conversion, `Nerv.IIP.Testing` schema convention helpers.

---

## Specification

Use `docs/superpowers/specs/2026-05-23-erp-procurement-sales-finance-mvp-design.md`.

## Files

- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/Nerv.IIP.Business.Erp.Domain.csproj`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/Nerv.IIP.Business.Erp.Infrastructure.csproj`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Nerv.IIP.Business.Erp.Web.csproj`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/PurchaseRequisitionAggregate/PurchaseRequisition.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/RequestForQuotationAggregate/RequestForQuotation.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/SupplierQuotationAggregate/SupplierQuotation.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/PurchaseOrderAggregate/PurchaseOrder.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/PurchaseReceiptAggregate/PurchaseReceipt.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/DomainEvents/ErpProcurementDomainEvents.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/ApplicationDbContext.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/EntityConfigurations/Procurement*.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Auth/ErpPermissionCodes.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Commands/Procurement/*.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Queries/Procurement/*.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEvents/ErpIntegrationEvents.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEventConverters/ErpProcurementIntegrationEventConverters.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Endpoints/Erp/ErpProcurementEndpoints.cs`
- Create: `backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests/ErpProcurementAggregateTests.cs`
- Create: `backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/ErpProcurementEndpointContractTests.cs`
- Create: `backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/ErpProcurementIntegrationEventTests.cs`
- Create: `backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/ErpSchemaConventionTests.cs`

Shared files requested from ERP-INTEG:

- `backend/Nerv.IIP.sln`
- `infra/aspire/Nerv.IIP.AppHost/Program.cs`
- `docs/architecture/authorization-matrix.md`
- `docs/architecture/database-schema-catalog.md`
- `docs/architecture/implementation-readiness.md`
- `scripts/verify-business-erp-procurement-mvp.ps1`

## Task 1: Scaffold ERP Service Locally

- [ ] **Step 1: Create service projects**

Run:

```powershell
dotnet new netcorepal-web -n Nerv.IIP.Business.Erp -o backend/services/Business/Erp --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
dotnet new xunit -n Nerv.IIP.Business.Erp.Domain.Tests -o backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests --framework net10.0
dotnet new xunit -n Nerv.IIP.Business.Erp.Web.Tests -o backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests --framework net10.0
```

- [ ] **Step 2: Remove template demo code**

Run:

```powershell
rg -n "OrderAggregate|DeliverRecord|LoginEndpoint|ChatHub|LockEndpoint" backend/services/Business/Erp
```

Expected: no matches.

## Task 2: Implement Procurement Domain

- [ ] **Step 1: Write failing aggregate tests**

Cover:

1. Purchase requisition can be created from a DemandPlanning suggestion reference.
2. RFQ must include at least one supplier and one requested item.
3. Supplier quotation rejects non-positive quantity or price.
4. Purchase order rejects empty lines.
5. Purchase receipt cannot exceed open ordered quantity.
6. Purchase receipt emits a domain event and is immutable after recording.

- [ ] **Step 2: Implement procurement aggregates and value objects**

Use public codes or IDs for cross-service references: `suggestionId`, `supplierCode`, `skuCode`, `siteCode`, `purchaseOrderId`.

- [ ] **Step 3: Run domain tests**

Run:

```powershell
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests/Nerv.IIP.Business.Erp.Domain.Tests.csproj --no-restore --filter FullyQualifiedName~ErpProcurementAggregateTests
```

Expected: procurement domain tests pass.

## Task 3: Add Persistence And Schema Guardrails

- [ ] **Step 1: Configure DbContext**

Use schema `erp` and migrations history `erp.__EFMigrationsHistory`. Add DbSet mappings for procurement aggregates only. Add schema tests that reject stock-balance or warehouse-execution ownership leakage.

- [ ] **Step 2: Generate initial migration**

Run:

```powershell
$env:Persistence__Provider = "PostgreSQL"
dotnet tool restore
dotnet tool run dotnet-ef migrations add InitialErpProcurementSchema --project backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/Nerv.IIP.Business.Erp.Infrastructure.csproj --startup-project backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Nerv.IIP.Business.Erp.Web.csproj --output-dir Migrations
```

- [ ] **Step 3: Run schema tests**

Run:

```powershell
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/Nerv.IIP.Business.Erp.Web.Tests.csproj --no-restore --filter FullyQualifiedName~ErpSchemaConventionTests
```

Expected: schema convention tests pass.

## Task 4: Add Procurement API And Events

- [ ] **Step 1: Add endpoint contract tests**

Verify routes, operation IDs, permission codes and `InternalServiceAuthorizationPolicy.Name`:

1. `POST /api/business/v1/erp/purchase-requisitions/from-suggestion`
2. `POST /api/business/v1/erp/rfqs`
3. `POST /api/business/v1/erp/supplier-quotations`
4. `POST /api/business/v1/erp/purchase-orders`
5. `POST /api/business/v1/erp/purchase-receipts`
6. `GET /api/business/v1/erp/purchase-orders`

- [ ] **Step 2: Implement commands, queries and FastEndpoints**

Keep business logic in command handlers and domain aggregates. Startup must not map Minimal API routes.

- [ ] **Step 3: Add event converter tests**

Verify:

1. `erp.PurchaseRequisitionCreated`
2. `erp.PurchaseOrderReleased`
3. `erp.PurchaseReceiptRecorded`

- [ ] **Step 4: Run Web tests**

Run:

```powershell
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/Nerv.IIP.Business.Erp.Web.Tests.csproj --no-restore
```

Expected: ERP Web tests pass.

## Task 5: Handoff Shared Changes

- [ ] **Step 1: Record shared changes**

In the PR/session summary, include:

```markdown
## Shared Changes Needed

- Add ERP projects/tests to `backend/Nerv.IIP.sln`.
- Register ERP in AppHost after at least procurement compiles.
- Add ERP procurement permissions to IAM seed and `authorization-matrix.md`.
- Add `erp` schema entries to `database-schema-catalog.md`.
- Add `scripts/verify-business-erp-procurement-mvp.ps1`.
- Reserve local port 5118 for `business-erp` unless the port matrix changes.
```

- [ ] **Step 2: Run focused verification**

Run:

```powershell
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests/Nerv.IIP.Business.Erp.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/Nerv.IIP.Business.Erp.Web.Tests.csproj --no-restore
```

Expected: both commands pass.

## Self-Review Checklist

1. Procurement accepts planning suggestion references without requiring DemandPlanning internals.
2. Receipt quantity cannot exceed ordered quantity.
3. ERP stores no Inventory balance, WMS task state or MES operation state.
4. Shared changes are clearly handed to ERP-INTEG.
