# BusinessApproval MVP Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement #134 by creating BusinessApproval for business approval templates, approval chains, approval steps, decision records and approval result events.

**Architecture:** BusinessApproval is a CleanDDD business service under `backend/services/Business/Approval`. It references IAM users/contexts by public IDs and emits approval result events for business services. It does not replace Ops operation approvals or copy IAM role facts.

**Tech Stack:** .NET 10, NetCorePal CleanDDD template, FastEndpoints, EF Core PostgreSQL, xUnit, ADR 0011 integration event conversion, `Nerv.IIP.Testing` schema convention helpers.

---

## Specification

Use `docs/superpowers/specs/2026-05-23-business-approval-mvp-design.md` as the domain contract for this plan.

## Files

- Create: `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Domain/Nerv.IIP.Business.Approval.Domain.csproj`
- Create: `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Infrastructure/Nerv.IIP.Business.Approval.Infrastructure.csproj`
- Create: `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Web/Nerv.IIP.Business.Approval.Web.csproj`
- Create: `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Domain/AggregatesModel/ApprovalTemplateAggregate/ApprovalTemplate.cs`
- Create: `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Domain/AggregatesModel/ApprovalChainAggregate/ApprovalChain.cs`
- Create: `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Domain/AggregatesModel/ApprovalStepAggregate/ApprovalStep.cs`
- Create: `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Domain/AggregatesModel/ApprovalDecisionAggregate/ApprovalDecision.cs`
- Create: `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Domain/DomainEvents/ApprovalDomainEvents.cs`
- Create: `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Infrastructure/ApplicationDbContext.cs`
- Create: `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Infrastructure/EntityConfigurations/*.cs`
- Create: `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Web/Application/Auth/ApprovalPermissionCodes.cs`
- Create: `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Web/Application/Commands/*.cs`
- Create: `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Web/Application/Queries/*.cs`
- Create: `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Web/Application/IntegrationEvents/ApprovalIntegrationEvents.cs`
- Create: `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Web/Application/IntegrationEventConverters/ApprovalIntegrationEventConverters.cs`
- Create: `backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Web/Endpoints/Approvals/ApprovalEndpoints.cs`
- Create: `backend/services/Business/Approval/tests/Nerv.IIP.Business.Approval.Domain.Tests/ApprovalAggregateTests.cs`
- Create: `backend/services/Business/Approval/tests/Nerv.IIP.Business.Approval.Web.Tests/ApprovalEndpointContractTests.cs`
- Create: `backend/services/Business/Approval/tests/Nerv.IIP.Business.Approval.Web.Tests/ApprovalIntegrationEventTests.cs`
- Create: `backend/services/Business/Approval/tests/Nerv.IIP.Business.Approval.Web.Tests/ApprovalSchemaConventionTests.cs`

Shared files requested from WAVE2-INTEG:

- `backend/Nerv.IIP.sln`
- `infra/aspire/Nerv.IIP.AppHost/Program.cs`
- `docs/architecture/authorization-matrix.md`
- `docs/architecture/database-schema-catalog.md`
- `docs/architecture/implementation-readiness.md`
- `scripts/verify-business-approval-mvp.ps1`

## Task 1: Scaffold BusinessApproval Service Locally

- [ ] **Step 1: Create service projects**

Run:

```powershell
dotnet new netcorepal-web -n Nerv.IIP.Business.Approval -o backend/services/Business/Approval --Framework net10.0 --Database PostgreSQL --MessageQueue RabbitMQ --UseAspire false --IncludeCopilotInstructions false --UseAdmin false
dotnet new xunit -n Nerv.IIP.Business.Approval.Domain.Tests -o backend/services/Business/Approval/tests/Nerv.IIP.Business.Approval.Domain.Tests --framework net10.0
dotnet new xunit -n Nerv.IIP.Business.Approval.Web.Tests -o backend/services/Business/Approval/tests/Nerv.IIP.Business.Approval.Web.Tests --framework net10.0
```

- [ ] **Step 2: Remove template demo code**

Run:

```powershell
rg -n "OrderAggregate|DeliverRecord|LoginEndpoint|ChatHub|LockEndpoint" backend/services/Business/Approval
```

Expected: no matches.

## Task 2: Implement Domain Model

- [ ] **Step 1: Write failing aggregate tests**

Cover:

1. Active template starts an approval chain for a source document reference.
2. Ordered steps must resolve in sequence.
3. Same actor repeating the same decision is idempotent.
4. Same actor repeating a conflicting decision is rejected.
5. Rejected chains are terminal.
6. Approved chains emit approved domain event only after the last required step.

- [ ] **Step 2: Implement aggregate roots**

Implement template, chain, step and decision aggregates. Keep IAM facts as string references only.

- [ ] **Step 3: Run domain tests**

Run:

```powershell
dotnet test backend/services/Business/Approval/tests/Nerv.IIP.Business.Approval.Domain.Tests/Nerv.IIP.Business.Approval.Domain.Tests.csproj --no-restore
```

Expected: BusinessApproval domain tests pass.

## Task 3: Add Persistence And Events

- [ ] **Step 1: Configure DbContext**

Use schema `business_approval` and migrations history `business_approval.__EFMigrationsHistory`.

- [ ] **Step 2: Generate migration**

Run:

```powershell
$env:Persistence__Provider = "PostgreSQL"
dotnet tool restore
dotnet tool run dotnet-ef migrations add InitialBusinessApprovalSchema --project backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Infrastructure/Nerv.IIP.Business.Approval.Infrastructure.csproj --startup-project backend/services/Business/Approval/src/Nerv.IIP.Business.Approval.Web/Nerv.IIP.Business.Approval.Web.csproj --output-dir Migrations
```

- [ ] **Step 3: Add event tests**

Verify event names:

1. `businessApproval.ApprovalStarted`
2. `businessApproval.StepResolved`
3. `businessApproval.ApprovalApproved`
4. `businessApproval.ApprovalRejected`
5. `businessApproval.ApprovalReturned`

## Task 4: Add API Surface

- [ ] **Step 1: Add endpoint contract tests**

Cover routes, permission codes, validation, operation IDs and pending task query behavior.

- [ ] **Step 2: Implement commands, queries and FastEndpoints**

Implement endpoints from the spec under `Endpoints/Approvals`.

- [ ] **Step 3: Run Web tests**

Run:

```powershell
dotnet test backend/services/Business/Approval/tests/Nerv.IIP.Business.Approval.Web.Tests/Nerv.IIP.Business.Approval.Web.Tests.csproj --no-restore
```

Expected: BusinessApproval Web tests pass.

## Task 5: Handoff Shared Changes To WAVE2-INTEG

- [ ] **Step 1: Record shared changes**

In the PR/session summary, include:

```markdown
## Shared Changes Needed

- Add Approval projects/tests to `backend/Nerv.IIP.sln`.
- Register Approval in AppHost.
- Add BusinessApproval permissions to IAM seed and `authorization-matrix.md`.
- Add `business_approval` schema entries to `database-schema-catalog.md`.
- Add `scripts/verify-business-approval-mvp.ps1`.
```

- [ ] **Step 2: Run final focused verification**

Run:

```powershell
dotnet test backend/services/Business/Approval/tests/Nerv.IIP.Business.Approval.Domain.Tests/Nerv.IIP.Business.Approval.Domain.Tests.csproj --no-restore
dotnet test backend/services/Business/Approval/tests/Nerv.IIP.Business.Approval.Web.Tests/Nerv.IIP.Business.Approval.Web.Tests.csproj --no-restore
```

Expected: both commands pass.
