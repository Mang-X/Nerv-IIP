# MAN-401 Scheduling MasterData Invalidation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Consume MasterData work-calendar and resource change events in Scheduling and precisely invalidate affected generated schedule plans.

**Architecture:** Add guarded CAP consumers beside the existing Scheduling input-change handlers and reuse the processed-event inbox plus append-only plan invalidation projection. Extend the internal invalidation command with generated-only work-center and calendar scopes; calendar matching reads the persisted normalized scheduling problem snapshot, while resource changes match only `WorkCenter` assignments. Unsupported resource relationships safely produce no match because the v1 event does not carry a plan-traceable hierarchy.

**Tech Stack:** .NET 10, EF Core, MediatR/NetCorePal unit of work, CAP, xUnit.

---

### Task 1: Prove precise generated-plan behavior

**Files:**
- Modify: `backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/SchedulingInputChangeEventHandlerTests.cs`
- Modify: `backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/Nerv.IIP.Business.Scheduling.Web.Tests.csproj`

- [x] Add failing behavior tests for calendar matching through persisted problem snapshots, resource/work-center matching through assignments, released and unrelated plan exclusion, duplicate delivery idempotency, no-match success, CAP subscription metadata, and list-query `IsInvalidated` state.
- [x] Run the focused test class and confirm it fails because the MasterData handlers and generated-only scopes do not exist.

### Task 2: Implement minimal guarded consumers and generated-only scopes

**Files:**
- Modify: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Nerv.IIP.Business.Scheduling.Web.csproj`
- Modify: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/IntegrationEventHandlers/SchedulingInputChangeIntegrationEventHandlers.cs`
- Modify: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Commands/RecordSchedulePlanInvalidationsCommand.cs`
- Modify: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Domain/AggregatesModel/SchedulePlanAggregate/SchedulePlan.cs`

- [x] Reference the existing MasterData contract project without changing the contract.
- [x] Add reason codes and guarded, inbox-backed handlers for `WorkCalendarChangedIntegrationEvent` and `ResourceChangedIntegrationEvent`.
- [x] Add generated-only calendar/work-center command scopes; deserialize normalized problem snapshots with `SchedulingJson.Options` for exact `CalendarId` matching and leave untraceable resource hierarchy changes as successful no-match events.
- [x] Run the focused test class and confirm all tests pass.

### Task 3: Document and verify

**Files:**
- Modify: `docs/architecture/integration-event-consumption-matrix.md`

- [x] Change the two MasterData matrix rows to name the Scheduling consumers, exact matching behavior, generated-only invalidation, and v1 hierarchy limitation.
- [x] Run Scheduling Web tests, domain tests, the integration-event contract gate, and the Scheduling APS-lite verification script as applicable.
- [x] Review the diff for forbidden MES/common-contract changes, endpoint/schema impact, and touched-file scope.

### Task 4: Review and publish

- [x] Invoke `verification-before-completion` and rerun current evidence commands.
- [x] Invoke `requesting-code-review`, perform code-fact review, and address any findings.
- [ ] Invoke `finishing-a-development-branch`, commit, push `codex/man-401-717-scheduling-consumers`, and create a PR titled with `MAN-401 #717` whose body uses only `Refs #717` and includes Fix / Tests / Risk / OpenAPI or schema impact / product-doc impact.
- [ ] Stop after PR creation without merging or starting MES/#701 work.
