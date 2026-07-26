# MAN-422 Scheduling Provider / Engine Boundary Design

**Issue:** Linear MAN-422 / GitHub #763
**Date:** 2026-07-26
**Status:** Approved for implementation by ADR 0022 and the MAN-422 delegation
**Decision sources:** ADR 0014, ADR 0022, BusinessScheduling APS lite design

## 1. Outcome

BusinessScheduling separates rule/profile resolution, runtime constraint
projection, and finite-capacity plan generation behind three explicit
application boundaries:

- `ISchedulingRuleProvider`
- `ISchedulingConstraintProvider`
- `ISchedulingEngine`

The default path keeps the current deterministic
`FiniteCapacityScheduler` and the stable
`SchedulingProblemContract -> SchedulePlanContract` contract. This change does
not introduce a solver, an engine-selection endpoint, a new Scheduling route,
or a connector-host dependency.

Every newly generated plan records enough immutable execution provenance to:

1. identify the exact engine implementation and version;
2. identify the rule provider/profile and profile version;
3. explain which constraint sources were applied and their outcomes; and
4. replay the exact effective engine input when that engine version remains
   available.

Historical plans whose effective engine input was never captured remain
queryable but explicitly report replay as unavailable. The migration must not
invent historical provider facts.

## 2. Current State and Scope Reduction

ADR 0022 already made the architecture decision required by MAN-422. The code
also already provides:

- a pure `FiniteCapacityScheduler` with version `aps-lite-v1`;
- a stable scheduling problem/plan contract;
- an operation-override overlay;
- equipment-availability and material-readiness providers; and
- Scheduling plan persistence plus released/revoked integration events.

MAN-422 therefore implements the missing boundaries and traceability rather
than revisiting the solver decision or rebuilding existing providers.

The current persistence path stores the base problem before equipment and
material facts are applied. That base snapshot is necessary for idempotency,
overrides, and revision generation, but it is not the exact engine input.
Replacing its meaning would break existing behavior. The design preserves the
base snapshot and adds a second, effective engine-input snapshot.

## 3. Boundaries

### 3.1 Scheduling engine

```csharp
public interface ISchedulingEngine
{
    string EngineId { get; }
    string Version { get; }

    SchedulePlanContract Schedule(
        SchedulingProblemContract problem,
        string planId,
        DateTimeOffset generatedAtUtc);
}
```

`FiniteCapacityScheduler` implements this interface:

- `EngineId = "finite-capacity"`
- `Version = "aps-lite-v1"`
- no database, HTTP, provider, service-locator, or clock access
- no behavior change to its deterministic finite-capacity heuristic

The existing `SchedulePlanContract.AlgorithmVersion` and database
`algorithm_version` remain the single wire/storage field for
`ISchedulingEngine.Version`. No duplicate `EngineVersion` field is added.

### 3.2 Rule provider

```csharp
public interface ISchedulingRuleProvider
{
    SchedulingRuleProviderResult Apply(SchedulingProblemContract problem);
}
```

The result contains:

- the transformed problem;
- a stable provider ID;
- a stable profile ID; and
- a profile version.

The default provider represents the current ADR 0014 policy. It normalizes the
existing rule-bearing input without moving the scheduling heuristic out of
`FiniteCapacityScheduler`. It must preserve the current golden plan output.

The provider is intentionally synchronous in the first version because the
default rule profile is local and deterministic. A future external rule store
can introduce an asynchronous boundary in a separate compatible change.

### 3.3 Constraint provider

```csharp
public interface ISchedulingConstraintProvider
{
    Task<SchedulingConstraintProviderResult> ApplyAsync(
        SchedulingProblemContract problem,
        CancellationToken cancellationToken);
}
```

The default constraint provider composes existing capabilities in a frozen
order:

1. operation override overlay;
2. equipment availability;
3. material readiness.

Each source returns a deterministic summary containing:

- `sourceId`
- `sourceVersion`
- `outcome`
- `factCount`
- `factsFingerprint`

The summary never stores credentials, raw provider errors, customer keys, or
unbounded payloads. No-data and degraded/fail-closed outcomes are explicit so
operators can distinguish “not invoked” from “invoked with no usable facts.”
Duplicate source IDs are rejected.

Its result exposes two different problem values:

- `BaseProblem`: the problem after the durable operation-override overlay,
  retaining the current idempotency and revision-baseline semantics;
- `EffectiveProblem`: the base problem after equipment and material runtime
  facts, which is the only value passed to the engine.

The default rule provider is behavior-preserving in this first version, so
applying it before the constraint provider does not change the existing
post-override base fingerprint. A future rule profile that changes the base
problem must do so deterministically and becomes part of that profile's
versioned semantics.

### 3.4 Generation pipeline

`SchedulingPlanGenerator` becomes the only application path that composes the
three boundaries:

```text
caller SchedulingProblem
  -> rule provider
  -> constraint provider
       -> durable override overlay -> BaseProblem
       -> equipment and material facts -> EffectiveProblem
  -> normalize and fingerprint effective input
  -> scheduling engine
  -> SchedulePlan + effective input + execution trace
```

`CreateSchedulePlanCommandHandler` and
`PreviewSchedulePlanCommandHandler` both depend on this generator. They no
longer depend directly on `FiniteCapacityScheduler`, the equipment provider,
the material provider, or the override overlay.

The generator accepts a caller-provided plan ID and timestamp so generation and
replay use exactly the same identity-sensitive inputs.

## 4. Execution Trace and Public Contract

`SchedulePlanContract` gains one optional trailing provenance value to preserve
source compatibility for existing call sites:

```csharp
SchedulingPlanProvenanceContract? Provenance = null
```

New plans always populate it. The provenance contains:

- `EngineId`
- `RuleProviderId`
- `RuleProfileId`
- `RuleProfileVersion`
- `EngineInputFingerprint`
- `TraceSchemaVersion`
- `ReplayStatus`
- a deterministic collection of constraint-source summaries

`AlgorithmVersion` remains the only engine-version field. Contract tests must
prove JSON does not contain both `algorithmVersion` and `engineVersion`.

The existing detail and generation responses make the trace queryable. No new
replay or engine-selection endpoint is added. The following existing exposed
responses change transitively:

- plan preview;
- plan create;
- workbench plan create;
- plan detail; and
- plan revision candidate.

There is no new facade coverage row because there is no new route. The existing
exposed facade remains authoritative, while the BusinessGateway OpenAPI
snapshot and generated api-client must be refreshed from the backend contract.

## 5. Persistence and Replay

### 5.1 Base problem snapshot

The existing `schedule_problems` fields retain their current meanings:

- `problem_json`: normalized base input used by idempotency, overrides, and
  revision generation;
- `problem_fingerprint`: deterministic fingerprint of that base input.

### 5.2 Effective engine input

`schedule_problems` gains:

- `engine_input_json jsonb null`
- `engine_input_fingerprint varchar(128) null`

New writes populate both from the normalized problem passed to
`ISchedulingEngine`. Historical rows remain null because their provider facts
cannot be reconstructed reliably.

### 5.3 Plan provenance

`schedule_plans` gains:

- `engine_id`
- `rule_provider_id`
- `rule_profile_id`
- `rule_profile_version`
- `constraint_sources_json`
- `trace_schema_version`
- `replay_status`

The existing `algorithm_version` column is retained and its comment is updated
to define it as the engine implementation version.

Historical rows may safely backfill the known finite-capacity engine ID and
existing algorithm version. Their constraint summary and replay status must
explicitly say `legacy-unavailable`; the migration must not claim that current
providers were invoked for an old plan.

### 5.4 Replay

`SchedulePlanReplayService` is an internal verification service. It:

1. loads the stored plan, trace, and effective engine input;
2. resolves the exact `(engineId, algorithmVersion)` engine;
3. re-runs it with the stored plan ID and generated timestamp;
4. compares a canonical digest of the generated and persisted plan content.

The resolver never falls back to the current engine. Missing input, an unknown
engine version, a legacy trace, or an unsupported trace schema returns an
explicit unavailable result.

Replay comparison covers deterministic business output: assignments, resource
loads, conflicts, unscheduled operations, metrics, and the effective problem
fingerprint. It does not compare persistence-only lifecycle state.

## 6. MES Boundary

The normative integration remains:

```text
BusinessScheduling providers/engine
  -> persisted SchedulePlan
  -> shared released/revoked events
  -> MES execution projection
```

MES may reference `Nerv.IIP.Contracts.Scheduling` but may not reference
BusinessScheduling Web, Domain, or Infrastructure. An assembly-reference
contract test freezes this rule.

MES-local `RuleScheduler` is still active in the manual schedule endpoint,
rush-order, plan conversion, maintenance reschedule, and planning-suggestion
paths. It is a documented deprecated exception, not a second APS authority.
MAN-422:

- adds no new dependency on it;
- does not make it a provider or solver adapter;
- does not delete its endpoint, UI, or persistence table; and
- records a follow-up requirement to migrate its callers before removal.

This bounded treatment avoids claiming a migration that the codebase has not
performed and avoids expanding MAN-422 into a multi-surface MES product change.

## 7. Database Verification

The new migration/profile test owns its PostgreSQL lifecycle:

1. create a unique Docker container name;
2. start `postgres:18` with Docker selecting a random localhost port;
3. build a temporary connection string from the mapped port;
4. apply migrations and verify columns, comments, jsonb round-trip, provenance,
   and replay input;
5. in `finally`, remove that exact container by name.

The test does not require or inspect `NERV_IIP_TEST_POSTGRES`. Existing
environment-gated database tests are outside this ticket and remain unchanged.

## 8. Non-goals

- No optimization solver or solver package.
- No multi-engine selection API.
- No provider/plugin marketplace.
- No connector-host changes or cross-boundary references.
- No new Scheduling or BusinessGateway route.
- No Business Console UI for provenance.
- No deletion of MES `RuleScheduler`, its route, UI, or table.
- No change to the deterministic `aps-lite-v1` scheduling behavior.

## 9. Acceptance Evidence

The implementation is complete when tests prove:

- default DI composition resolves the three boundaries;
- rule and constraint application order is deterministic;
- default pipeline output matches direct `FiniteCapacityScheduler` output;
- Create and Preview use the same pipeline;
- base and effective problem snapshots retain distinct semantics;
- plan provenance round-trips through EF and the public detail response;
- new plans replay exactly and legacy plans report replay unavailable;
- JSON contains one engine-version concept;
- PostgreSQL 18 migration and comments are correct with exact container cleanup;
- MES references shared Scheduling contracts/events but no provider, engine,
  solver, or BusinessScheduling implementation assembly;
- OpenAPI/api-client artifacts match the changed existing response schemas; and
- focused Scheduling, contract, Gateway, MES boundary, and governed verification
  tests pass before the explicitly released backend solution test.
