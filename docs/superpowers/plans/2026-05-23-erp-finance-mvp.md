# ERP Finance MVP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement #139 by adding Finance MVP facts to ERP: AP, AR, balanced vouchers and cost candidates.

**Architecture:** Finance extends the ERP service after procurement receipt and sales delivery event shapes are stable. Finance creates candidate and posting facts from ERP, WMS, Inventory and MES public facts. It does not implement full GL close.

**Tech Stack:** .NET 10, FastEndpoints, EF Core PostgreSQL, xUnit, ADR 0011 integration event conversion.

---

## Specification

Use `docs/superpowers/specs/2026-05-23-erp-procurement-sales-finance-mvp-design.md`.

## Prerequisites

1. ERP procurement receipt facts exist.
2. ERP sales delivery order facts exist.
3. WMS, Inventory and MES public facts are available from closed Wave 1/2 services.

## Files

- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/AccountPayableAggregate/AccountPayable.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/AccountReceivableAggregate/AccountReceivable.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/JournalVoucherAggregate/JournalVoucher.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/AggregatesModel/CostCandidateAggregate/CostCandidate.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Domain/DomainEvents/ErpFinanceDomainEvents.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/ApplicationDbContext.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/EntityConfigurations/Finance*.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Auth/ErpPermissionCodes.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Commands/Finance/*.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Queries/Finance/*.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEventHandlers/Finance*.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEvents/ErpIntegrationEvents.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEventConverters/ErpFinanceIntegrationEventConverters.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Endpoints/Erp/ErpFinanceEndpoints.cs`
- Create: `backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests/ErpFinanceAggregateTests.cs`
- Create: `backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/ErpFinanceEndpointContractTests.cs`
- Create: `backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/ErpFinanceIntegrationEventTests.cs`
- Modify: `backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/ErpSchemaConventionTests.cs`

Shared files requested from ERP-INTEG:

- IAM seed and authorization matrix additions for finance permissions.
- Schema catalog additions for finance tables.
- `scripts/verify-business-erp-finance-mvp.ps1`.
- Final `scripts/verify-business-erp-procurement-sales-finance-mvp.ps1`.

## Task 1: Implement Finance Domain

- [ ] **Step 1: Write failing aggregate tests**

Cover:

1. Account payable amount must be positive.
2. AP paid amount cannot exceed open amount.
3. Account receivable amount must be positive.
4. AR collected amount cannot exceed open amount.
5. Journal voucher cannot post unless total debits equal total credits.
6. Posted voucher is immutable.
7. Cost candidate references at least one source fact: MES report, Inventory movement or WMS completion.

- [ ] **Step 2: Implement finance aggregates**

Use decimal precision explicitly and model currency codes as required stable fields.

- [ ] **Step 3: Run domain tests**

Run:

```powershell
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests/Nerv.IIP.Business.Erp.Domain.Tests.csproj --no-restore --filter FullyQualifiedName~ErpFinanceAggregateTests
```

Expected: finance domain tests pass.

## Task 2: Extend Persistence

- [ ] **Step 1: Add finance mappings**

Map AP, AR, journal voucher, voucher lines and cost candidate tables in schema `erp`.

- [ ] **Step 2: Add migration**

Run:

```powershell
$env:Persistence__Provider = "PostgreSQL"
dotnet tool restore
dotnet tool run dotnet-ef migrations add AddErpFinanceSchema --project backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/Nerv.IIP.Business.Erp.Infrastructure.csproj --startup-project backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Nerv.IIP.Business.Erp.Web.csproj --output-dir Migrations
```

- [ ] **Step 3: Run schema tests**

Run:

```powershell
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/Nerv.IIP.Business.Erp.Web.Tests.csproj --no-restore --filter FullyQualifiedName~ErpSchemaConventionTests
```

Expected: schema tests pass.

## Task 3: Add Finance API And Events

- [ ] **Step 1: Add endpoint contract tests**

Verify:

1. `POST /api/business/v1/erp/finance/payables`
2. `POST /api/business/v1/erp/finance/receivables`
3. `POST /api/business/v1/erp/finance/cost-candidates`
4. `POST /api/business/v1/erp/finance/vouchers`
5. `GET /api/business/v1/erp/finance/summary`

- [ ] **Step 2: Implement commands, queries and endpoints**

Keep finance commands idempotent by source document reference where downstream facts may be delivered more than once.

- [ ] **Step 3: Add event converter tests**

Verify:

1. `erp.AccountPayableCreated`
2. `erp.AccountReceivableCreated`
3. `erp.CostCandidateCreated`
4. `erp.JournalVoucherPosted`

- [ ] **Step 4: Add event handler tests**

Cover candidate creation from ERP receipt, ERP delivery, WMS completion, Inventory movement or MES report using public event contracts/stubs. Handler tests must not reference another service's Domain or Infrastructure project.

- [ ] **Step 5: Run ERP Web tests**

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

- Add finance permissions to IAM seed and `authorization-matrix.md`.
- Add finance tables to `database-schema-catalog.md`.
- Add/update `scripts/verify-business-erp-finance-mvp.ps1`.
- Add final `scripts/verify-business-erp-procurement-sales-finance-mvp.ps1`.
- Update readiness to state ERP is implemented only after procurement, sales and finance focused verifies pass.
```

- [ ] **Step 2: Run focused verification**

Run:

```powershell
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests/Nerv.IIP.Business.Erp.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/Nerv.IIP.Business.Erp.Web.Tests.csproj --no-restore
```

Expected: both commands pass.

## Self-Review Checklist

1. Unbalanced vouchers cannot post.
2. AP/AR cannot be overpaid or over-collected.
3. Finance stores candidate facts, not full ledger close facts.
4. Event handlers use public contracts only.
