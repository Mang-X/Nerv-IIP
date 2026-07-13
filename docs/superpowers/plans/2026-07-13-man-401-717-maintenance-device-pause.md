# MAN-401 #717 Maintenance Device Pause Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Consume `DeviceAssetChangedIntegrationEvent` in BusinessMaintenance so a disabled device durably pauses all matching maintenance plans and replay cannot generate duplicate effects.

**Architecture:** Keep the public MasterData contract shape unchanged and correct the producer's existing `Status` and idempotency-key values. BusinessMaintenance routes the validated event through its existing durable consumer inbox into an idempotent latest-device-state projection and pauses matching plan aggregates. Device-state updates, plan creation, and PM generation share an organization/environment lock so a disable cannot race scheduling; both the projection and aggregate pause gate PM generation.

**Tech Stack:** .NET 10, EF Core, MediatR/NetCorePal, CAP integration events, xUnit, PostgreSQL migrations.

---

### Task 1: Correct the producer facts without changing the contract

**Files:**
- Modify: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/DomainEvents/MasterDataDomainEvents.cs`
- Modify: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/DeviceAssetAggregate/DeviceAsset.cs`
- Modify: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/IntegrationEventConverters/MasterDataIntegrationEventConverters.cs`
- Test: `backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/MasterDataIntegrationEventTests.cs`

- [x] Add failing converter tests proving disabled status is emitted and two distinct changes have distinct idempotency keys.
- [x] Run the focused MasterData test and confirm the expected failure.
- [x] Add status to the internal device domain event and use the generated event ID in the existing envelope idempotency key.
- [x] Re-run the focused test and confirm it passes.

### Task 2: Add durable plan pause state and block generation

**Files:**
- Modify: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Domain/AggregatesModel/MaintenancePlanAggregate/MaintenancePlan.cs`
- Modify: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/Commands/MaintenanceCommands.cs`
- Test: `backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Domain.Tests/MaintenanceAggregateTests.cs`
- Test: `backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/MaintenanceEndpointContractTests.cs`

- [x] Add failing tests proving pause is idempotent and paused due plans do not generate work orders or advance due state.
- [x] Run focused tests and confirm the expected failures.
- [x] Add `Paused`/`Pause()` to the aggregate, an idempotent pause command scoped by organization/environment/device, and filter paused plans from generation.
- [x] Re-run focused tests and confirm they pass.

### Task 3: Consume the event through the durable inbox

**Files:**
- Create: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/IntegrationEventHandlers/PauseMaintenancePlansWhenDeviceDisabledHandler.cs`
- Modify: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Program.cs`
- Modify: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Nerv.IIP.Business.Maintenance.Web.csproj`
- Modify: `backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/Nerv.IIP.Business.Maintenance.Web.Tests.csproj`
- Test: `backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/MaintenanceIntegrationEventHandlerTests.cs`

- [x] Add failing handler tests for disabled-event behavior, active-event projection, replay idempotency, out-of-order rejection, and unsupported-version dead-lettering.
- [x] Run the focused handler tests and confirm the expected failures.
- [x] Implement the guarded CAP handler using `MaintenanceProcessedIntegrationEventInbox`, a durable latest-state projection, and a shared scheduling lock; never throw for absent plans or supported business statuses.
- [x] Re-run handler tests and confirm they pass.

### Task 4: Persist and document the schema and consumption

**Files:**
- Modify: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Infrastructure/EntityConfigurations/MaintenanceEntityTypeConfigurations.cs`
- Create: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Infrastructure/Migrations/*_PauseMaintenancePlansForDisabledDevices.cs`
- Modify: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`
- Modify: `docs/architecture/database-schema-catalog.md`
- Modify: `docs/architecture/integration-event-consumption-matrix.md`
- Test: `backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/MaintenanceSchemaConventionTests.cs`

- [x] Configure the paused column and device-state projection with database comments and generate the EF migration through `dotnet-ef`.
- [x] Update the schema catalog and the exact DeviceAssetChanged matrix row.
- [x] Run schema convention, migration-focused, and disposable PostgreSQL 18 acceptance tests.

### Task 5: Verify, review, and publish only this slice

- [x] Run MasterData and Maintenance focused test projects and schema gates; run the necessary backend solution gate before publishing.
- [x] Inspect `git diff` and confirm Scheduling, MES, generated OpenAPI, and public contract files are untouched.
- [ ] Invoke `verification-before-completion` and request an independent code review; resolve all critical/important findings.
- [ ] Commit, push, and create a PR titled with `MAN-401 #717`, using `Refs #717` and the required Fix/Tests/Risk/OpenAPI or schema impact sections.
