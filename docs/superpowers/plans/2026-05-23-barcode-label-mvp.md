# BarcodeLabel MVP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement #133 by creating BarcodeLabel for barcode rules, label templates, print batches and scan records.

**Architecture:** BarcodeLabel is a CleanDDD business service under `backend/services/Business/BarcodeLabel`. It references MasterData and FileStorage by public IDs and records print/scan facts. It does not own inventory balances, WMS execution state or FileStorage object keys.

**Tech Stack:** .NET 10, NetCorePal CleanDDD template, FastEndpoints, EF Core PostgreSQL, xUnit, ADR 0011 integration event conversion, `Nerv.IIP.Testing` schema convention helpers.

---

## Specification

Use `docs/superpowers/specs/2026-05-23-barcode-label-mvp-design.md` as the domain contract for this plan.

## Files

- Create: `backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Domain/Nerv.IIP.Business.BarcodeLabel.Domain.csproj`
- Create: `backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Infrastructure/Nerv.IIP.Business.BarcodeLabel.Infrastructure.csproj`
- Create: `backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Nerv.IIP.Business.BarcodeLabel.Web.csproj`
- Create: `backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Domain/AggregatesModel/BarcodeRuleAggregate/BarcodeRule.cs`
- Create: `backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Domain/AggregatesModel/LabelTemplateAggregate/LabelTemplate.cs`
- Create: `backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Domain/AggregatesModel/LabelPrintBatchAggregate/LabelPrintBatch.cs`
- Create: `backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Domain/AggregatesModel/ScanRecordAggregate/ScanRecord.cs`
- Create: `backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Domain/DomainEvents/BarcodeLabelDomainEvents.cs`
- Create: `backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Infrastructure/ApplicationDbContext.cs`
- Create: `backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Infrastructure/EntityConfigurations/*.cs`
- Create: `backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Application/Auth/BarcodeLabelPermissionCodes.cs`
- Create: `backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Application/Commands/*.cs`
- Create: `backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Application/Queries/*.cs`
- Create: `backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Application/IntegrationEvents/BarcodeLabelIntegrationEvents.cs`
- Create: `backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Application/IntegrationEventConverters/BarcodeLabelIntegrationEventConverters.cs`
- Create: `backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Endpoints/BarcodeLabel/BarcodeLabelEndpoints.cs`
- Create: `backend/services/Business/BarcodeLabel/tests/Nerv.IIP.Business.BarcodeLabel.Domain.Tests/BarcodeLabelAggregateTests.cs`
- Create: `backend/services/Business/BarcodeLabel/tests/Nerv.IIP.Business.BarcodeLabel.Web.Tests/BarcodeLabelEndpointContractTests.cs`
- Create: `backend/services/Business/BarcodeLabel/tests/Nerv.IIP.Business.BarcodeLabel.Web.Tests/BarcodeLabelIntegrationEventTests.cs`
- Create: `backend/services/Business/BarcodeLabel/tests/Nerv.IIP.Business.BarcodeLabel.Web.Tests/BarcodeLabelSchemaConventionTests.cs`

Shared files requested from WAVE2-INTEG:

- `backend/Nerv.IIP.sln`
- `infra/aspire/Nerv.IIP.AppHost/Program.cs`
- `docs/architecture/authorization-matrix.md`
- `docs/architecture/database-schema-catalog.md`
- `docs/architecture/implementation-readiness.md`
- `scripts/verify-business-barcode-label-mvp.ps1`

## Task 1: Scaffold BarcodeLabel Service Locally

- [ ] **Step 1: Create service projects**

Run:

```powershell
dotnet new netcorepal-web -n Nerv.IIP.Business.BarcodeLabel -o backend/services/Business/BarcodeLabel --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
dotnet new xunit -n Nerv.IIP.Business.BarcodeLabel.Domain.Tests -o backend/services/Business/BarcodeLabel/tests/Nerv.IIP.Business.BarcodeLabel.Domain.Tests --framework net10.0
dotnet new xunit -n Nerv.IIP.Business.BarcodeLabel.Web.Tests -o backend/services/Business/BarcodeLabel/tests/Nerv.IIP.Business.BarcodeLabel.Web.Tests --framework net10.0
```

- [ ] **Step 2: Remove template demo code**

Run:

```powershell
rg -n "OrderAggregate|DeliverRecord|LoginEndpoint|ChatHub|LockEndpoint" backend/services/Business/BarcodeLabel
```

Expected: no matches.

## Task 2: Implement Domain Model

- [ ] **Step 1: Write failing aggregate tests**

Cover:

1. Barcode rule creation rejects blank prefixes or unsupported barcode types.
2. Label template creation stores only FileStorage file IDs, not object keys.
3. Print batch creation generates deterministic label items from rule and source document.
4. Print batch idempotency returns the existing batch for the same payload.
5. Conflicting print idempotency payload is rejected.
6. Scan record creation requires source device, scanned value and idempotency key.
7. Scan idempotency rejects conflicting payloads.

- [ ] **Step 2: Implement aggregate roots and domain events**

Implement the aggregate files and domain events from the spec. Use `Guid.CreateVersion7()` for IDs and keep generation deterministic for tests.

- [ ] **Step 3: Run domain tests**

Run:

```powershell
dotnet test backend/services/Business/BarcodeLabel/tests/Nerv.IIP.Business.BarcodeLabel.Domain.Tests/Nerv.IIP.Business.BarcodeLabel.Domain.Tests.csproj --no-restore
```

Expected: BarcodeLabel domain tests pass.

## Task 3: Add Persistence And Events

- [ ] **Step 1: Configure DbContext**

Use schema `barcode` and migrations history `barcode.__EFMigrationsHistory`.

- [ ] **Step 2: Generate migration**

Run:

```powershell
$env:Persistence__Provider = "PostgreSQL"
dotnet tool restore
dotnet tool run dotnet-ef migrations add InitialBarcodeLabelSchema --project backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Infrastructure/Nerv.IIP.Business.BarcodeLabel.Infrastructure.csproj --startup-project backend/services/Business/BarcodeLabel/src/Nerv.IIP.Business.BarcodeLabel.Web/Nerv.IIP.Business.BarcodeLabel.Web.csproj --output-dir Migrations
```

- [ ] **Step 3: Add event tests**

Verify event names:

1. `barcode.LabelPrintBatchCreated`
2. `barcode.LabelPrintBatchCompleted`
3. `barcode.LabelScanned`
4. `barcode.ScanRejected`

## Task 4: Add API Surface

- [ ] **Step 1: Add endpoint contract tests**

Cover route shape, permission codes, validation, operation IDs and no public `objectKey`/`object_key` leakage.

- [ ] **Step 2: Implement commands, queries and FastEndpoints**

Implement endpoints from the spec under `Endpoints/BarcodeLabel`.

- [ ] **Step 3: Run Web tests**

Run:

```powershell
dotnet test backend/services/Business/BarcodeLabel/tests/Nerv.IIP.Business.BarcodeLabel.Web.Tests/Nerv.IIP.Business.BarcodeLabel.Web.Tests.csproj --no-restore
```

Expected: BarcodeLabel Web tests pass.

## Task 5: Handoff Shared Changes To WAVE2-INTEG

- [ ] **Step 1: Record shared changes**

In the PR/session summary, include:

```markdown
## Shared Changes Needed

- Add BarcodeLabel projects/tests to `backend/Nerv.IIP.sln`.
- Register BarcodeLabel in AppHost.
- Add BarcodeLabel permissions to IAM seed and `authorization-matrix.md`.
- Add `barcode` schema entries to `database-schema-catalog.md`.
- Add `scripts/verify-business-barcode-label-mvp.ps1`.
```

- [ ] **Step 2: Run final focused verification**

Run:

```powershell
dotnet test backend/services/Business/BarcodeLabel/tests/Nerv.IIP.Business.BarcodeLabel.Domain.Tests/Nerv.IIP.Business.BarcodeLabel.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/BarcodeLabel/tests/Nerv.IIP.Business.BarcodeLabel.Web.Tests/Nerv.IIP.Business.BarcodeLabel.Web.Tests.csproj --no-restore
```

Expected: both commands pass.

