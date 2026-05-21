# Ops Governance Readiness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prepare Ops for more than the second-slice `lifecycle.restart` workflow while keeping Notification and FileStorage MVP branches unblocked.

**Architecture:** Keep the first batch inside Ops and stable shared contracts. Add templates as the source of operation-code defaults, repair SDK/client compatibility, add read APIs for task/audit views, publish additive integration events, and harden migration/id generation behavior without changing Gateway, Console, Notification, or FileStorage wiring.

**Tech Stack:** .NET 10, FastEndpoints, EF Core, NetCorePal CleanDDD patterns, xUnit, `Nerv.IIP.Contracts.Ops`, `Nerv.IIP.Sdk.Ops`.

---

## Scope

In this phase:

1. Fix `Sdk.Ops` response envelope handling.
2. Add `OperationTemplate` aggregate, EF mapping, repository support, migration, contracts and Ops endpoints.
3. Make operation task creation validate operation codes through templates instead of hardcoding `lifecycle.restart`.
4. Add task list and audit record query endpoints.
5. Add additive requested/claimed/audit-recorded integration event contracts and converters without changing existing completed/failed events.
6. Add Development-only protection for Ops/AppHub `Persistence:AutoMigrate=true`.
7. Replace count-based Ops id generation with collision-resistant ids.

Out of scope:

1. Gateway/Console facades for templates, audit, or approvals.
2. ApprovalRequest runtime workflow and pending-approval state.
3. Notification/FileStorage direct integration.
4. Breaking changes to existing completed/failed integration event contracts.

## Worktree

Branch: `codex/ops-governance-readiness`

Path: `C:\WorkFile\Focus\项目\数字工厂\Nerv-IIP-worktrees\ops-governance-readiness`

Baseline already passed:

```powershell
dotnet test backend\services\Ops\tests\Nerv.IIP.Ops.Domain.Tests\Nerv.IIP.Ops.Domain.Tests.csproj --no-restore
dotnet test backend\tests\Nerv.IIP.Contracts.Ops.Tests\Nerv.IIP.Contracts.Ops.Tests.csproj --no-restore
dotnet test backend\services\Ops\tests\Nerv.IIP.Ops.Web.Tests\Nerv.IIP.Ops.Web.Tests.csproj --no-restore
```

## Task 1: SDK Envelope Compatibility

**Files:**

- Modify: `backend/common/Sdk/Nerv.IIP.Sdk.Ops/OpsClient.cs`
- Test: `backend/tests/Nerv.IIP.Contracts.Ops.Tests/OpsContractJsonTests.cs` or create focused SDK tests if an existing SDK test project is present.

- [ ] Add a failing test showing `HttpOpsClient` can read `{"data":{...}}` Ops responses.
- [ ] Add an internal response envelope record in `OpsClient.cs`.
- [ ] Update all response-reading methods to unwrap `ResponseData<T>` and keep useful errors for empty data.
- [ ] Run `dotnet test backend/tests/Nerv.IIP.Contracts.Ops.Tests/Nerv.IIP.Contracts.Ops.Tests.csproj --no-restore`.

## Task 2: Migration Safety And Id Generation

**Files:**

- Modify: `backend/services/Ops/src/Nerv.IIP.Ops.Web/Program.cs`
- Modify: `backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Program.cs`
- Modify: `backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/Repositories/OperationTaskRepository.cs`
- Test: `backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/OpsServiceReadinessTests.cs`

- [ ] Add failing tests for Production + PostgreSQL + `Persistence:AutoMigrate=true` rejecting startup.
- [ ] Add Development-only guards matching IAM behavior.
- [ ] Add a failing test or repository-level assertion proving task ids do not use count-based generation.
- [ ] Replace `NextTaskIdAsync` with deterministic prefix plus Guid v7/string id pattern that cannot collide under concurrent creates.
- [ ] Run Ops Web tests.

## Task 3: OperationTemplate Foundation

**Files:**

- Create: `backend/services/Ops/src/Nerv.IIP.Ops.Domain/AggregatesModel/OperationTemplateAggregate/OperationTemplate.cs`
- Modify: `backend/services/Ops/src/Nerv.IIP.Ops.Domain/DomainEvents/OperationTaskDomainEvents.cs`
- Modify: `backend/services/Ops/src/Nerv.IIP.Ops.Domain/AggregatesModel/OperationTaskAggregate/OperationTask.cs`
- Modify: `backend/services/Ops/src/Nerv.IIP.Ops.Domain/InMemoryOpsStateStore.cs`
- Modify: `backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/ApplicationDbContext.cs`
- Create: `backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/EntityConfigurations/OperationTemplateEntityTypeConfiguration.cs`
- Modify: `backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/Repositories/OperationTaskRepository.cs`
- Modify: `backend/common/Contracts/Nerv.IIP.Contracts.Ops/OpsContracts.cs`
- Create/modify: Ops migration files.
- Test: `backend/services/Ops/tests/Nerv.IIP.Ops.Domain.Tests/OperationTaskAggregateTests.cs`
- Test: `backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/OperationTaskEndpointTests.cs`

- [ ] Add failing domain tests for unsupported operation code rejection through missing template.
- [ ] Add failing domain tests for creating a task from an enabled template.
- [ ] Add `OperationTemplate` with operation code, display name, JSON parameter schema, risk level, default max attempts, default lease duration seconds, requires approval, enabled flag, timestamps.
- [ ] Add EF configuration and DbSet using `ops.operation_templates`.
- [ ] Add repository methods to get templates by operation code and add/update templates.
- [ ] Change task creation to accept template defaults and remove the hardcoded `lifecycle.restart` check.
- [ ] Seed or lazily provide a built-in `lifecycle.restart` template so existing tests and second-slice behavior continue to pass.
- [ ] Run Ops Domain and Ops Web tests.

## Task 4: Template, Task List, And Audit Endpoints

**Files:**

- Create: `backend/services/Ops/src/Nerv.IIP.Ops.Web/Endpoints/OperationTemplates/OperationTemplateEndpoints.cs`
- Create: `backend/services/Ops/src/Nerv.IIP.Ops.Web/Application/Queries/ListOperationTasksQuery.cs`
- Create: `backend/services/Ops/src/Nerv.IIP.Ops.Web/Application/Queries/ListAuditRecordsQuery.cs`
- Modify: `backend/services/Ops/src/Nerv.IIP.Ops.Web/Endpoints/OperationTasks/OperationTaskEndpoints.cs`
- Create: `backend/services/Ops/src/Nerv.IIP.Ops.Web/Endpoints/AuditRecords/AuditRecordEndpoints.cs`
- Modify: `backend/common/Contracts/Nerv.IIP.Contracts.Ops/OpsContracts.cs`
- Modify: `backend/common/Sdk/Nerv.IIP.Sdk.Ops/OpsClient.cs`
- Test: `backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/OperationTaskEndpointTests.cs`

- [ ] Add failing endpoint tests for template create/list/get.
- [ ] Add failing endpoint tests for `GET /api/ops/v1/operation-tasks` paged list.
- [ ] Add failing endpoint tests for `GET /api/ops/v1/audit-records`.
- [ ] Implement query handlers using `ApplicationDbContext` projection.
- [ ] Add SDK methods for list tasks and template reads only if the contract is stable in this phase.
- [ ] Run Ops Web tests.

## Task 5: Additive Integration Events

**Files:**

- Modify: `backend/common/Contracts/Nerv.IIP.Contracts.Ops/OpsIntegrationEvents.cs`
- Create: `backend/services/Ops/src/Nerv.IIP.Ops.Web/Application/IntegrationEventConverters/OperationTaskRequestedIntegrationEventConverter.cs`
- Create: `backend/services/Ops/src/Nerv.IIP.Ops.Web/Application/IntegrationEventConverters/OperationTaskClaimedIntegrationEventConverter.cs`
- Create: `backend/services/Ops/src/Nerv.IIP.Ops.Web/Application/IntegrationEventConverters/AuditRecordedIntegrationEventConverter.cs`
- Test: `backend/tests/Nerv.IIP.Contracts.Ops.Tests/OpsContractJsonTests.cs`
- Test: `backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/OperationTaskIntegrationEventConverterTests.cs`

- [ ] Add failing JSON round-trip tests for requested/claimed/audit-recorded events.
- [ ] Add additive event records and payload records without modifying completed/failed records.
- [ ] Add converter tests.
- [ ] Implement converters from existing domain events or new narrow domain events if needed.
- [ ] Run contract and Ops Web converter tests.

## Verification

Run before completion:

```powershell
dotnet test backend\services\Ops\tests\Nerv.IIP.Ops.Domain.Tests\Nerv.IIP.Ops.Domain.Tests.csproj --no-restore
dotnet test backend\tests\Nerv.IIP.Contracts.Ops.Tests\Nerv.IIP.Contracts.Ops.Tests.csproj --no-restore
dotnet test backend\services\Ops\tests\Nerv.IIP.Ops.Web.Tests\Nerv.IIP.Ops.Web.Tests.csproj --no-restore
dotnet build backend\Nerv.IIP.sln --no-restore
```
