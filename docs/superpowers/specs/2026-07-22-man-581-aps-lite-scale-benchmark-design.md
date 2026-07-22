# MAN-581 APS Lite Scale Benchmark Design

## Goal

Provide repeatable, source-controlled evidence that the existing APS Lite finite-capacity heuristic can process fixed 100, 500, and 1000 order profiles. The evidence must report phase timing, memory, schedule KPIs, and unscheduled-reason distribution without claiming global optimality.

## Scope and boundaries

- Reuse `FiniteCapacityScheduler` and the existing Scheduling persistence model.
- Add no solver, optimizer, simulation engine, or algorithm-version change.
- Run the three fixed profiles with three repetitions each.
- Persist the generated problem snapshot and plan through the real Scheduling EF Core model on PostgreSQL.
- Produce generated JSON and Markdown under `artifacts/script-logs/business-scheduling-scale-benchmark/<run-id>/`; runtime evidence is not committed.
- Update readiness documentation with the latest successful run summary and the exact evidence path.
- Add no HTTP endpoint, Gateway facade, OpenAPI snapshot, generated client, migration, or schema change.

## Fixed data model

`SchedulingScaleProblemFactory` creates deterministic shock-absorber work orders from a fixed UTC horizon and stable identifiers. Every order contains four ordered operations (tube welding, rod assembly, oil/seal, and damping test) and selects from a fixed 24-resource pool. The same order count always serializes to the same normalized input.

The profile intentionally includes a deterministic, documented mixture of schedulable work and explicit blockers so the report proves both capacity scheduling and explanation behavior. Blocked operations use existing reason contracts only; no benchmark-only reason code enters production contracts.

Profiles are frozen at:

| Profile | Orders | Operations per order | Resources | Repetitions |
| --- | ---: | ---: | ---: | ---: |
| demo | 100 | 4 | 24 | 3 |
| medium | 500 | 4 | 24 | 3 |
| stress | 1000 | 4 | 24 | 3 |

## Measurement model

Each repetition records monotonic elapsed milliseconds for:

1. input assembly: deterministic contract construction;
2. constraint check: production normalization and structural validation;
3. algorithm calculation: the existing deterministic finite-capacity heuristic over the normalized input;
4. persistence: adding the production problem snapshot and generated plan aggregate and calling PostgreSQL `SaveChangesAsync`;
5. total: the complete measured pipeline.

The test samples process working set and managed heap during the measured pipeline and reports the peak values plus the working-set increase over the run baseline. Timings are observational evidence for the named machine/profile, not release SLOs.

## Stability proof

Each repetition uses the identical input contract, fixed `generatedAtUtc`, and fixed public plan identifier. The benchmark cleans its own persisted scope after each repetition so the same input can be persisted again. It hashes canonical schedule output (metrics, assignments, resource loads, conflicts, and unscheduled operations, excluding measured durations) and fails unless all three repetitions produce the same hash.

## Evidence contract

The generated evidence contains:

- commit, OS, architecture, .NET runtime, processor count, profile, PostgreSQL provider, and capture time;
- order, operation, and resource counts;
- per-repetition phase timings and peak memory;
- min/median/max summaries for each phase and memory measurement;
- on-time rate, total tardiness minutes, average resource utilization, and scheduled/unscheduled operation counts;
- unscheduled reason distribution;
- one canonical output hash per profile and an explicit `stable=true` assertion;
- the disclaimer: “APS Lite deterministic finite-capacity heuristic; no global optimality claim.”

The Markdown report is leadership-readable, but always retains the machine/profile context and disclaimer next to the headline results.

## Error handling and cleanup

- The script fails when Docker/PostgreSQL is unavailable, a profile does not complete, persistence fails, output hashes differ, or evidence files are missing.
- Database migrations run before measurements and are excluded from phase timing.
- Benchmark rows are deleted outside the timed window after every repetition, including failure cleanup where possible.
- The governed PowerShell entrypoint uses `ScriptAutomation.ps1` helpers and leaves the shared development PostgreSQL service running, matching existing persistence verification scripts.

## Verification

- Unit tests prove fixed profile counts, deterministic input serialization, evidence serialization, and stability mismatch detection.
- The Scheduling focused tests prove the scheduler refactor preserves behavior.
- The governed scale script runs all three profiles against PostgreSQL and writes both evidence formats.
- Script governance and the full backend solution gate run before PR creation.
