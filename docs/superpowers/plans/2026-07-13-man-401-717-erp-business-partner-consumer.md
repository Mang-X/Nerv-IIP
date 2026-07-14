# MAN-401 #717 ERP Business Partner Consumer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Consume MasterData business-partner changes durably in ERP and block new PO/SO creation-submission after the referenced partner is disabled.

**Architecture:** MasterData emits the real status through its unchanged public payload. ERP persists a latest-state projection with inbox idempotency and timestamp ordering, then checks the projection at the two code-fact order submission boundaries.

**Tech Stack:** .NET 10, EF Core, PostgreSQL, CAP integration events, xUnit, NetCorePal/CleanDDD.

---

### Task 1: Make the existing event carry the real partner state

**Files:**
- Modify: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/DomainEvents/MasterDataDomainEvents.cs`
- Modify: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Domain/AggregatesModel/BusinessPartnerAggregate/BusinessPartner.cs`
- Modify: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/IntegrationEventConverters/MasterDataIntegrationEventConverters.cs`
- Test: `backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/MasterDataIntegrationEventTests.cs`

- [ ] Write tests that convert partner changed events for `disabled` and `active` and assert the payload status.
- [ ] Run the focused tests and observe failure because the event lacks status and the converter hardcodes `active`.
- [ ] Add `Status` to the internal domain event, publish `active`/`disabled` from aggregate transitions, and copy it into the existing public payload.
- [ ] Run the focused tests and observe them pass.

### Task 2: Add ERP durable projection and guarded consumer

**Files:**
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/MasterData/BusinessPartnerAvailability.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/EntityConfigurations/BusinessPartnerAvailabilityEntityTypeConfiguration.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/ApplicationDbContext.cs`
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/IntegrationEventHandlers/BusinessPartnerChangedIntegrationEventHandlerForProjectBusinessPartnerAvailability.cs`
- Test: `backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/BusinessPartnerChangedConsumerTests.cs`

- [ ] Write focused tests for disabled projection, duplicate replay, stale event ordering, invalid payload dead-lettering, and re-enable.
- [ ] Run them and observe compile/test failure because the projection and consumer do not exist.
- [ ] Implement the smallest projection, EF mapping, DbSet, guard, dead-letter branches, inbox recording, and ordered upsert required by the tests.
- [ ] Run the focused tests and observe them pass.

### Task 3: Gate the real PO/SO submission boundaries

**Files:**
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Commands/Procurement/ErpProcurementCommands.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Web/Application/Commands/Sales/ErpSalesCommands.cs`
- Test: `backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/BusinessPartnerChangedConsumerTests.cs`

- [ ] Extend the consumer tests so event consumption followed by PO/SO creation throws `KnownException`, while active state and idempotent existing-order replay remain allowed.
- [ ] Run and observe failure because the command handlers do not query the projection.
- [ ] Add async, cancellation-aware projection checks after existing idempotent replay returns and before new external/domain side effects.
- [ ] Run the focused tests and observe them pass.

### Task 4: Migration and governance documentation

**Files:**
- Create: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/Migrations/*_AddBusinessPartnerAvailabilityProjection.cs`
- Modify: `backend/services/Business/Erp/src/Nerv.IIP.Business.Erp.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`
- Modify: `docs/architecture/database-schema-catalog.md`
- Modify: `docs/architecture/integration-event-consumption-matrix.md`

- [ ] Generate the EF migration with `Persistence__Provider=PostgreSQL`; do not hand-edit generated artifacts.
- [ ] Document the projection table, uniqueness, lifecycle, and the active ERP consumer row.
- [ ] Run ERP schema convention tests.

### Task 5: Real-provider acceptance and final gates

**Files:**
- Create: `backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/BusinessPartnerChangedPostgresAcceptanceTests.cs`

- [ ] Add a gated PostgreSQL acceptance test that runs migrations, consumes disable, persists inbox/projection, and proves both PO and SO behavior change.
- [ ] Run the test when `NERV_IIP_TEST_POSTGRES` is available; otherwise capture the skipped result and exact reproduction command.
- [ ] Run MasterData Web tests, ERP Domain/Web tests, ERP schema tests, and the necessary backend solution gate.
- [ ] Review `git diff` for strict ERP/MasterData/docs scope and no Scheduling/MES/common-contract changes.
