# MAN-422 Scheduling Provider / Engine Boundary Implementation Plan

> **For implementation:** Follow this plan with strict RED/GREEN TDD. Do not
> run `dotnet test backend/Nerv.IIP.sln` until the coordinating session
> explicitly releases the exclusive full-suite slot.

**Goal:** Separate rule/profile and constraint sourcing from the deterministic
Scheduling engine, persist queryable execution provenance and an exact replay
input, and freeze the MES implementation boundary without introducing a solver
or new endpoint.

**Architecture:** `SchedulingPlanGenerator` composes
`ISchedulingRuleProvider -> ISchedulingConstraintProvider ->
ISchedulingEngine`. `FiniteCapacityScheduler` remains the only default engine.
Post-override base and effective problem snapshots have separate persistence
semantics.

**Tech stack:** .NET 10, FastEndpoints, EF Core, PostgreSQL 18, xUnit,
BusinessGateway OpenAPI, Hey API codegen.

**Design:** `docs/superpowers/specs/2026-07-26-man-422-scheduling-provider-engine-boundary-design.md`

---

## Guardrails

- Work only on `codex/man-422-scheduling-provider-engine`.
- Preserve `SchedulingProblemContract -> SchedulePlanContract`.
- Preserve `FiniteCapacityScheduler` output and `aps-lite-v1`.
- Do not add a solver, endpoint, Gateway route, or connector-host reference.
- Keep `AlgorithmVersion` as the sole engine-version field.
- Do not change the meaning of existing base `problem_json/fingerprint`.
- Do not remove or expand MES `RuleScheduler`; document it as a deprecated
  exception and add an implementation-assembly boundary test.
- Use `Guid.CreateVersion7()` for persisted identifiers.
- Use async EF calls with `CancellationToken`.
- New PostgreSQL verification must self-start `postgres:18` with a unique
  container name/random host port and remove the exact container in `finally`.
- Commit each focused phase independently.

## Task 1: Freeze the three boundaries and default composition

**Files:**

- Create:
  `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Scheduling/SchedulingExecutionAbstractions.cs`
- Create:
  `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Scheduling/DefaultSchedulingRuleProvider.cs`
- Create:
  `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Scheduling/DefaultSchedulingConstraintProvider.cs`
- Create:
  `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Scheduling/SchedulingPlanGenerator.cs`
- Modify:
  `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Scheduling/FiniteCapacityScheduler.cs`
- Modify:
  `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Program.cs`
- Create:
  `backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/SchedulingEngineProviderPipelineTests.cs`

### RED

Write tests that prove:

1. `FiniteCapacityScheduler` implements `ISchedulingEngine` with
   `finite-capacity / aps-lite-v1`.
2. default DI resolves one engine, one rule provider, one constraint provider,
   and `SchedulingPlanGenerator`;
3. a spy rule provider runs before a spy constraint provider, and the engine
   receives the transformed effective problem;
4. duplicate constraint source IDs fail deterministically;
5. no-data/degraded sources create explicit outcomes; and
6. the constraint result exposes the post-override base problem separately
   from the equipment/material effective problem; and
7. for a fixed fixture/plan ID/time, default pipeline business output matches a
   direct call to the current scheduler.

Run:

```powershell
dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/Nerv.IIP.Business.Scheduling.Web.Tests.csproj `
  --filter FullyQualifiedName~SchedulingEngineProviderPipelineTests
```

Confirm failure is caused by the missing abstractions/composition.

### GREEN

Implement the minimum abstractions, default providers, generator, and DI
registrations. Keep provider summaries deterministic and bounded. Do not move
finite-capacity ordering or allocation logic into the rule provider.

Re-run the focused filter, then the existing scheduler tests:

```powershell
dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/Nerv.IIP.Business.Scheduling.Web.Tests.csproj `
  --filter "FullyQualifiedName~SchedulingEngineProviderPipelineTests|FullyQualifiedName~FiniteCapacitySchedulerTests"
```

Commit:

```text
refactor(scheduling): separate provider and engine composition
```

## Task 2: Route Create and Preview through one generation pipeline

**Files:**

- Modify:
  `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Commands/CreateSchedulePlanCommand.cs`
- Modify:
  `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Commands/PreviewSchedulePlanCommand.cs`
- Modify:
  `backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/SchedulingEngineProviderPipelineTests.cs`
- Modify:
  `backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/SchedulingEndpointContractTests.cs`
- Modify related handler construction tests only where compilation requires it.

### RED

Add handler-level tests proving:

- Create and Preview invoke the same generator;
- each provider is invoked once per new generation;
- both paths return the same trace for the same fixed input/time;
- preview status remains `Preview`;
- create status remains `Generated`; and
- Create still checks the post-override, pre-runtime-facts base fingerprint
  before returning an idempotent existing plan.

### GREEN

Replace the duplicated handler orchestration with `SchedulingPlanGenerator`.
Keep the existing idempotency branch and current urgency-capture behavior. When
an existing plan is returned, do not overwrite its historical provenance with
current provider facts.

Run:

```powershell
dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/Nerv.IIP.Business.Scheduling.Web.Tests.csproj `
  --filter "FullyQualifiedName~SchedulingEngineProviderPipelineTests|FullyQualifiedName~SchedulingEndpointContractTests|FullyQualifiedName~SchedulingProviderDegradationTests"
```

Commit:

```text
refactor(scheduling): unify plan generation paths
```

## Task 3: Persist execution provenance and exact engine input

**Files:**

- Modify:
  `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Domain/AggregatesModel/SchedulePlanAggregate/SchedulePlan.cs`
- Modify:
  `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/EntityConfigurations/SchedulePlanEntityTypeConfiguration.cs`
- Modify:
  `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/EntityConfigurations/ScheduleProblemSnapshotEntityTypeConfiguration.cs`
- Modify:
  `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Commands/CreateSchedulePlanCommand.cs`
- Add EF migration `AddSchedulingEngineProviderTrace` and generated designer.
- Update:
  `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`
- Modify tests:
  `SchedulePlanAggregateTests.cs`,
  `SchedulingPersistenceTests.cs`,
  `SchedulingSchemaConventionTests.cs`
- Create:
  `backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/SchedulingEngineTracePostgresTests.cs`

### RED

Write unit/model tests proving:

- plan metadata requires stable engine/rule identifiers and versions;
- replacing a generated plan replaces trace and output atomically;
- base problem JSON/fingerprint and effective engine-input JSON/fingerprint
  round-trip independently;
- `algorithm_version` remains the engine version;
- all new columns have explicit names, lengths/types, nullability, and comments;
- historical provenance is explicitly `legacy-unavailable`.

Write the PostgreSQL 18 test fixture using `ProcessStartInfo.ArgumentList`:

- unique `nerv-man422-postgres-<guid>` container name;
- generate an ephemeral password for this test process and pass it only through
  `-e POSTGRES_PASSWORD=<ephemeral>`;
- `docker run -d --name <exact> -e POSTGRES_PASSWORD=<ephemeral> -p 127.0.0.1::5432 postgres:18`;
- discover the assigned port using `docker port`;
- construct a temporary Npgsql connection string;
- wait with a bounded retry;
- migrate and verify the live schema/jsonb round-trip;
- always `docker rm -f <exact>` in `finally`;
- never read `NERV_IIP_TEST_POSTGRES`.

The test must skip with an explicit Docker-unavailable reason when the Docker
daemon is unavailable; it must not turn missing Docker into a code failure.

### GREEN

Add the aggregate fields and dual input snapshot. Generate the migration through
`dotnet-ef`; do not hand-edit the model snapshot or designer.

Migration policy:

- known legacy engine ID may backfill to `finite-capacity`;
- existing `algorithm_version` remains untouched;
- legacy constraint source and replay fields state unavailable;
- legacy `engine_input_json/fingerprint` remain null;
- new rows always populate effective input and provenance.

Run:

```powershell
dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Domain.Tests/Nerv.IIP.Business.Scheduling.Domain.Tests.csproj
dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/Nerv.IIP.Business.Scheduling.Web.Tests.csproj `
  --filter "FullyQualifiedName~SchedulingPersistenceTests|FullyQualifiedName~SchedulingSchemaConventionTests|FullyQualifiedName~SchedulingEngineTracePostgresTests"
```

The fixture must retain its exact generated container name and, after its
`finally` cleanup completes, assert that `docker inspect <exact>` no longer
finds that container. It must not use a broad prefix cleanup that could delete
another concurrent test's container.

Commit:

```text
feat(scheduling): persist provider engine trace and replay input
```

## Task 4: Publish provenance and implement exact replay verification

**Files:**

- Modify:
  `backend/common/Contracts/Nerv.IIP.Contracts.Scheduling/SchedulingContracts.cs`
- Modify:
  `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Queries/SchedulePlanContractMapper.cs`
- Create:
  `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Scheduling/SchedulePlanReplayService.cs`
- Create:
  `backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/SchedulingReplayTests.cs`
- Modify:
  `backend/tests/Nerv.IIP.Contracts.Scheduling.Tests/SchedulingContractSerializationTests.cs`
- Modify:
  `backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/SchedulingEndpointContractTests.cs`
- Modify affected BusinessGateway proxy/OpenAPI tests.

### RED

Add tests proving:

- provenance JSON uses camelCase and round-trips;
- JSON contains `algorithmVersion` but not `engineVersion`;
- create, preview, workbench create, detail, and revision candidate expose
  provenance;
- endpoint count and routes are unchanged;
- exact replay with stored effective input/plan ID/time produces the same
  canonical digest;
- unknown engine ID/version, missing effective input, legacy trace, and
  unsupported trace schema return explicit unavailable results;
- replay never falls back to the default/current engine.

### GREEN

Add an optional trailing provenance contract and populate it for all new and
persisted plans. Implement a registry/resolver only as much as required to map
the exact default engine tuple. Do not add a replay endpoint.

Run:

```powershell
dotnet test backend/tests/Nerv.IIP.Contracts.Scheduling.Tests/Nerv.IIP.Contracts.Scheduling.Tests.csproj
dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/Nerv.IIP.Business.Scheduling.Web.Tests.csproj `
  --filter "FullyQualifiedName~SchedulingReplayTests|FullyQualifiedName~SchedulingEndpointContractTests|FullyQualifiedName~SchedulingPersistenceTests"
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj `
  --filter "FullyQualifiedName~BusinessGatewayOpenApiTests|FullyQualifiedName~BusinessGatewayProxyTests"
```

Commit:

```text
feat(scheduling): expose provenance and exact replay evidence
```

## Task 5: Freeze the MES boundary and update architecture/schema docs

**Files:**

- Modify:
  `backend/tests/Nerv.IIP.ContractBoundary.Tests/ContractBoundaryTests.cs`
- Modify:
  `backend/tests/Nerv.IIP.ContractBoundary.Tests/Nerv.IIP.ContractBoundary.Tests.csproj`
  only if an assembly reference is required.
- Modify:
  `docs/adr/0022-aps-rule-provider-and-engine-separation.md`
- Modify:
  `docs/architecture/business-platform-domain-architecture.md`
- Modify:
  `docs/architecture/mes-module-product-design.md`
- Modify:
  `docs/architecture/implementation-readiness.md`
- Modify:
  `docs/architecture/database-schema-catalog.md`
- Modify:
  `docs/architecture/api-contract-and-codegen.md`
- Update `docs/architecture/facade-coverage-matrix.md` narrative only if needed
  to record that existing exposed response schemas changed; do not add a JSON
  row without a new/changed route declaration.

### RED

Add an assembly-reference test proving MES Web:

- may reference `Nerv.IIP.Contracts.Scheduling`;
- does not reference `Nerv.IIP.Business.Scheduling.Web`;
- does not reference `Nerv.IIP.Business.Scheduling.Domain`;
- does not reference `Nerv.IIP.Business.Scheduling.Infrastructure`.

Use assembly metadata/reflection, not source path traversal.

### GREEN

Update docs to record:

- implemented provider/engine boundary and default engine;
- execution trace and replay semantics;
- schema columns and migration;
- no solver and no connector-host dependency;
- MES canonical consumption of plan/events;
- the active MES-local `RuleScheduler` as a deprecated exception with no new
  dependencies;
- a separate follow-up is required for its API/UI/caller/table migration.

Run:

```powershell
dotnet test backend/tests/Nerv.IIP.ContractBoundary.Tests/Nerv.IIP.ContractBoundary.Tests.csproj
dotnet test backend/services/Business/MES/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj `
  --filter "FullyQualifiedName~SchedulingPlanReleased|FullyQualifiedName~SchedulingPlanRevoked"
```

Commit:

```text
docs(scheduling): record engine trace and MES legacy boundary
```

## Task 6: Refresh the existing exposed facade contract

**Files generated by governed commands:**

- `frontend/packages/api-client/openapi/business-gateway-console.v1.json`
- affected files under
  `frontend/packages/api-client/src/generated/business-console/`
- stable barrel only if the generator introduces a newly referenced type.

Do not hand-edit generated artifacts.

Run:

```powershell
pwsh scripts/export-gateway-openapi.ps1
pnpm -C frontend generate:api
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj `
  --filter "FullyQualifiedName~BusinessGatewayOpenApiTests|FullyQualifiedName~BusinessGatewayProxyTests"
pwsh scripts/verify-openapi-client-drift.ps1
```

Confirm:

- only existing Scheduling response schemas changed;
- no new route or operation ID exists;
- current Scheduling rows in `facade-coverage-matrix.json` remain valid;
- no unrelated generated drift is committed.

Commit:

```text
chore(api): refresh scheduling provenance contract
```

## Task 7: Focused verification and independent review

Run fresh, unfiltered focused verification:

```powershell
dotnet test backend/tests/Nerv.IIP.Contracts.Scheduling.Tests/Nerv.IIP.Contracts.Scheduling.Tests.csproj
dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Domain.Tests/Nerv.IIP.Business.Scheduling.Domain.Tests.csproj
dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/Nerv.IIP.Business.Scheduling.Web.Tests.csproj
dotnet test backend/tests/Nerv.IIP.ContractBoundary.Tests/Nerv.IIP.ContractBoundary.Tests.csproj
dotnet test backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj
pwsh scripts/verify-business-scheduling-aps-lite.ps1
```

Also run:

```powershell
dotnet build backend/Nerv.IIP.sln
git diff --check
git status --short
```

Do not substitute build for tests. Capture exact pass/fail/skip counts.

Request an independent Critical/Important/Minor review from an agent that did
not implement the change. Send Critical/Important findings back to the original
implementer, re-run affected tests, and repeat independent review until both
categories are zero. Minor findings may remain only with an explicit rationale.

## Task 8: Full backend gate after coordinating-session release

Stop and request/await explicit release from the coordinating session. Once the
exclusive slot is released, run exactly:

```powershell
dotnet test backend/Nerv.IIP.sln
```

Do not run this command earlier. Record current output and do not treat older
main-branch evidence as a substitute.

## Task 9: Ready PR and Linear evidence

Verify clean scope:

```powershell
git status --short
git diff origin/main...HEAD --stat
git log --oneline origin/main..HEAD
```

Push the branch and create a non-draft PR with `gh`:

```powershell
git push -u origin codex/man-422-scheduling-provider-engine
gh pr create --base main --head codex/man-422-scheduling-provider-engine
```

The PR body must include:

- `Fixes #763`;
- summary of provider/engine/trace/replay changes;
- exact test commands and results;
- PostgreSQL 18 self-start/random-port/exact-cleanup evidence;
- architecture/database/API documentation impact;
- endpoint/facade declaration:
  “No new endpoint; existing exposed Scheduling plan response schemas changed,
  BusinessGateway OpenAPI/api-client refreshed; no facade coverage row added”;
- MES legacy `RuleScheduler` disposition and follow-up;
- no solver and no connector-host changes.

Create the PR ready for review, not draft. Do not merge it.

Update MAN-422 with the PR URL, commit/test evidence, review result
(`Critical=0`, `Important=0`), and the full backend gate result.
