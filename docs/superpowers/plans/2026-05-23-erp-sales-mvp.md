# ERP Sales MVP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement #138 by adding Sales/CRM-lite/OMS-lite facts to the ERP service.

**Architecture:** Sales extends `backend/services/Business/Erp` after the ERP scaffold from #137 exists. It owns opportunity, quotation, sales order and delivery order request facts. WMS owns warehouse execution; Inventory owns balances and movements.

**Tech Stack:** .NET 10, FastEndpoints, EF Core PostgreSQL, xUnit, ADR 0011 integration event conversion.

---

## Specification

Use `docs/superpowers/specs/2026-05-23-erp-procurement-sales-finance-mvp-design.md`.

## Prerequisites

1. `backend/services/Business/Erp` exists.
2. ERP Domain, Infrastructure and Web projects compile.
3. Procurement plan has established ERP permission code, endpoint contract and schema convention patterns.

## Files

- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/OpportunityAggregate/Opportunity.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/QuotationAggregate/Quotation.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/SalesOrderAggregate/SalesOrder.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/DeliveryOrderAggregate/DeliveryOrder.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/DomainEvents/ErpSalesDomainEvents.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/ApplicationDbContext.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/EntityConfigurations/Sales*.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Auth/ErpPermissionCodes.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Commands/Sales/*.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Queries/Sales/*.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEvents/ErpIntegrationEvents.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEventConverters/ErpSalesIntegrationEventConverters.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Endpoints/Erp/ErpSalesEndpoints.cs`
- Create: `backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests/ErpSalesAggregateTests.cs`
- Create: `backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/ErpSalesEndpointContractTests.cs`
- Create: `backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/ErpSalesIntegrationEventTests.cs`
- Modify: `backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/ErpSchemaConventionTests.cs`

Shared files requested from ERP-INTEG:

- IAM seed and authorization matrix additions for sales permissions.
- Schema catalog additions for sales tables.
- `scripts/verify-business-erp-sales-mvp.ps1`.

## Task 1: Implement Sales Domain

- [ ] **Step 1: Write failing aggregate tests**

Cover:

1. Opportunity requires customer reference and topic.
2. Quotation requires lines and positive quantity/price.
3. Unapproved quotation cannot create a sales order.
4. Expired or rejected quotation cannot create a sales order.
5. Delivery order quantity cannot exceed remaining sales order quantity.
6. Delivery order emits `DeliveryOrderReleased` domain event.

- [ ] **Step 2: Implement sales aggregates**

Use public IDs and document references only. Do not store WMS task state, Inventory balance or customer master data fields beyond stable references/snapshots.

- [ ] **Step 3: Run domain tests**

Run:

```powershell
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests/Nerv.IIP.Business.Erp.Domain.Tests.csproj --no-restore --filter FullyQualifiedName~ErpSalesAggregateTests
```

Expected: sales domain tests pass.

## Task 2: Extend Persistence

- [ ] **Step 1: Add sales mappings**

Map opportunity, quotation, sales order and delivery order tables in schema `erp`.

- [ ] **Step 2: Add migration**

Run:

```powershell
$env:Persistence__Provider = "PostgreSQL"
dotnet tool restore
dotnet tool run dotnet-ef migrations add AddErpSalesSchema --project backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/Nerv.IIP.Business.Erp.Infrastructure.csproj --startup-project backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Nerv.IIP.Business.Erp.Web.csproj --output-dir Migrations
```

- [ ] **Step 3: Run schema tests**

Run:

```powershell
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/Nerv.IIP.Business.Erp.Web.Tests.csproj --no-restore --filter FullyQualifiedName~ErpSchemaConventionTests
```

Expected: schema tests pass.

## Task 3: Add Sales API And Events

- [ ] **Step 1: Add endpoint contract tests**

Verify:

1. `POST /api/business/v1/erp/opportunities`
2. `POST /api/business/v1/erp/quotations`
3. `POST /api/business/v1/erp/quotations/{quotationId}/approve`
4. `POST /api/business/v1/erp/sales-orders`
5. `POST /api/business/v1/erp/delivery-orders`
6. `GET /api/business/v1/erp/sales-orders`

- [ ] **Step 2: Implement commands, queries and endpoints**

Keep approval state explicit. If BusinessApproval is later connected, store only the approval chain reference on quotation.

- [ ] **Step 3: Add event converter tests**

Verify:

1. `erp.DeliveryOrderReleased`
2. optional `erp.SalesOrderCreated` if the implementation publishes it.

- [ ] **Step 4: Run ERP Web tests**

Run:

```powershell
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/Nerv.IIP.Business.Erp.Web.Tests.csproj --no-restore
```

Expected: ERP Web tests pass.

## Task 4: Handoff Shared Changes

- [ ] **Step 1: Record shared changes**

In the PR/session summary, include:

```markdown
## Shared Changes Needed

- Add sales permissions to IAM seed and `authorization-matrix.md`.
- Add sales tables to `database-schema-catalog.md`.
- Add/update `scripts/verify-business-erp-sales-mvp.ps1`.
- Confirm WMS outbound integration uses public delivery order references only.
```

- [ ] **Step 2: Run focused verification**

Run:

```powershell
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests/Nerv.IIP.Business.Erp.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/Nerv.IIP.Business.Erp.Web.Tests.csproj --no-restore
```

Expected: both commands pass.

## Self-Review Checklist

1. Sales release creates delivery request facts, not WMS execution facts.
2. Delivery quantity cannot exceed ordered quantity.
3. Quotation approval is explicit and test-covered.
4. Shared changes are clearly handed to ERP-INTEG.
