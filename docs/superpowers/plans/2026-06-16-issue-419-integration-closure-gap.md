# Issue 419 Integration Closure Gap Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rebase #419 on the latest `origin/main`, update the cross-service event wiring panorama to reflect merged adjacent issues, and keep the reusable ADR 0011 public event envelope gate current.

**Architecture:** Keep the meta issue docs-first and code-light. Public event contract governance stays in `backend/common/Contracts` plus `backend/tests/Nerv.IIP.Contracts.IntegrationEvents.Tests`; concrete business-chain repairs remain in their owning service issues and must not be centralized in Gateway or a new shared service.

**Tech Stack:** .NET 10, xUnit, public Contracts projects, governed PowerShell verification commands.

---

## Files

- Modify: `docs/superpowers/specs/2026-06-16-issue-419-integration-closure-gap.md`
- Modify: `backend/tests/Nerv.IIP.Contracts.IntegrationEvents.Tests/IntegrationEventEnvelopeContractTests.cs`
- Modify: `backend/tests/Nerv.IIP.Contracts.IntegrationEvents.Tests/Nerv.IIP.Contracts.IntegrationEvents.Tests.csproj`
- Modify: `backend/common/Contracts/Nerv.IIP.Contracts.MasterData/MasterDataIntegrationEvents.cs`
- Modify: `backend/common/Contracts/Nerv.IIP.Contracts.MasterData/Nerv.IIP.Contracts.MasterData.csproj`
- Modify: `backend/common/Contracts/Nerv.IIP.Contracts.ProductEngineering/ProductEngineeringContracts.cs`
- Modify: `backend/common/Contracts/Nerv.IIP.Contracts.ProductEngineering/Nerv.IIP.Contracts.ProductEngineering.csproj`
- Modify: `backend/common/Contracts/Nerv.IIP.Contracts.Quality/QualityIntegrationEvents.cs`
- Modify: `backend/tests/Nerv.IIP.Messaging.CAP.Tests/Nerv.IIP.Messaging.CAP.Tests.csproj`
- Modify: `docs/architecture/implementation-readiness.md`

### Task 1: Rebase And Recheck Facts

- [x] **Step 1: Fetch and rebase**

Run:

```powershell
git fetch origin --prune
git rebase origin/main
```

Expected: branch is based on latest main. Resolve conflicts against current main facts.

- [x] **Step 2: Read issue and readiness docs**

Run:

```powershell
gh issue view 419 --json number,title,body,labels,state,url,updatedAt
Get-Content docs/architecture/implementation-readiness.md -Raw
```

Expected: #419 remains the meta issue; readiness reflects recently merged business gap closures.

- [x] **Step 3: Capture current event wiring facts**

Run targeted searches for public contracts, converters and consumers:

```powershell
rg -n "IIntegrationEventConverter|ICapSubscribe|IIntegrationEventHandler|IntegrationEventConsumerGuard" backend -g "*.cs"
rg -n "InventoryMovementRequestedIntegrationEvent|SchedulePlanReleasedIntegrationEvent|WmsOutboundOrderRequestedIntegrationEvent|InspectionResultIntegrationEvent|NcrDispositionDecidedIntegrationEvent" backend -g "*.cs"
```

Expected: identify newly connected paths from MES, Scheduling, ERP, WMS, Quality and Approval before updating the #419 spec.

### Task 2: Keep Public Envelope Gate Current

- [x] **Step 1: Preserve discovery-based test**

Keep `backend/tests/Nerv.IIP.Contracts.IntegrationEvents.Tests/IntegrationEventEnvelopeContractTests.cs` scanning referenced public contract assemblies for exported non-generic classes ending in `IntegrationEvent`.

- [x] **Step 2: Add newly public event assemblies**

Add BarcodeLabel and Scheduling references/usings so current public contracts introduced by adjacent merged issues are included in the same ADR 0011 gate.

- [x] **Step 3: Verify green**

Run:

```powershell
dotnet test backend/tests/Nerv.IIP.Contracts.IntegrationEvents.Tests/Nerv.IIP.Contracts.IntegrationEvents.Tests.csproj --no-restore --verbosity minimal
```

Expected after implementation: PASS.

### Task 3: Update Documentation

- [x] **Step 1: Rewrite spec for latest main**

Update `docs/superpowers/specs/2026-06-16-issue-419-integration-closure-gap.md` so it distinguishes:

1. Newly connected paths from latest main.
2. Remaining published-but-unconsumed or service-local events.
3. Five chain statuses where some links are now closed but residual gaps remain.
4. Saga/process-manager absence.
5. Envelope/DLQ governance status.

- [x] **Step 2: Update readiness**

Update `docs/architecture/implementation-readiness.md` so the `Nerv.IIP.Contracts.IntegrationEvents.Tests` gate is documented as discovering public integration event contracts, not just checking a fixed subset, and so #419 reflects current public assembly coverage.

- [x] **Step 3: Final verification**

Run:

```powershell
dotnet test backend/tests/Nerv.IIP.Contracts.IntegrationEvents.Tests/Nerv.IIP.Contracts.IntegrationEvents.Tests.csproj --no-restore
dotnet test backend/tests/Nerv.IIP.Messaging.CAP.Tests/Nerv.IIP.Messaging.CAP.Tests.csproj --no-restore
git diff --check
```

Expected: all commands pass. `scripts/check-script-governance.ps1` is not required unless this plan adds or modifies PowerShell scripts.
