# MAN-581 APS Lite Scale Benchmark Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a repeatable PostgreSQL-backed APS Lite benchmark for fixed 100, 500, and 1000 order profiles with phased timing, memory, KPI, unscheduled-reason, and stability evidence.

**Architecture:** Extend the existing `Nerv.IIP.Business.Performance.Tests` project with a deterministic Scheduling data factory, a benchmark runner, and JSON/Markdown evidence writer. Split production scheduling into normalize/validate and normalized-execution seams without changing the public `Schedule` behavior, then drive the benchmark through a governed PowerShell script against the shared development PostgreSQL profile.

**Tech Stack:** .NET 10, xUnit, EF Core 10, Npgsql/PostgreSQL, PowerShell 7, existing `ScriptAutomation.ps1` helpers.

## Global Constraints

- Profiles are exactly 100, 500, and 1000 orders, with four operations per order, 24 resources, and three repetitions.
- Reuse `FiniteCapacityScheduler.AlgorithmVersion == "aps-lite-v1"`; add no solver or optimizer.
- Persistence evidence uses PostgreSQL, never EF Core InMemory.
- Evidence must say “APS Lite deterministic finite-capacity heuristic; no global optimality claim.”
- Runtime evidence is generated under `artifacts/` and is not committed.
- Add no endpoint, OpenAPI contract, generated client, migration, schema, or facade-matrix row.

---

### Task 1: Add the normalized scheduler execution seam

**Files:**
- Modify: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Scheduling/FiniteCapacityScheduler.cs`
- Modify: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Nerv.IIP.Business.Scheduling.Web.csproj`
- Test: `backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/FiniteCapacitySchedulerTests.cs`

**Interfaces:**
- Consumes: existing `SchedulingProblemNormalizer.Normalize(SchedulingProblemContract)`.
- Produces: internal `FiniteCapacityScheduler.ScheduleNormalized(SchedulingProblemContract normalizedProblem, string planId, DateTimeOffset generatedAtUtc)` for the performance test friend assembly.

- [ ] **Step 1: Write the failing normalized-execution parity test**

Add a focused test that normalizes `ShockAbsorberSchedulingFixture.CreateProblem()`, calls `Schedule` and the desired `ScheduleNormalized`, and asserts equivalent metrics, assignments, loads, conflicts, and unscheduled operations.

```csharp
[Fact]
public void ScheduleNormalized_matches_public_schedule_for_normalized_input()
{
    var scheduler = new FiniteCapacityScheduler();
    var problem = ShockAbsorberSchedulingFixture.CreateProblem();
    var normalized = SchedulingProblemNormalizer.Normalize(problem);

    var expected = scheduler.Schedule(problem, "plan-parity", FixedGeneratedAtUtc);
    var actual = scheduler.ScheduleNormalized(normalized, "plan-parity", FixedGeneratedAtUtc);

    Assert.Equal(expected.Metrics, actual.Metrics);
    Assert.Equal(expected.Assignments, actual.Assignments);
    Assert.Equal(expected.ResourceLoads, actual.ResourceLoads);
    Assert.Equal(expected.Conflicts, actual.Conflicts);
    Assert.Equal(expected.UnscheduledOperations, actual.UnscheduledOperations);
}
```

- [ ] **Step 2: Run the test and verify RED**

Run:

```powershell
dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/Nerv.IIP.Business.Scheduling.Web.Tests.csproj --filter "FullyQualifiedName~ScheduleNormalized_matches_public_schedule"
```

Expected: compile failure because `ScheduleNormalized` does not exist.

- [ ] **Step 3: Add the minimal execution seam**

Keep the public API unchanged and move only the already-normalized execution:

```csharp
public SchedulePlanContract Schedule(SchedulingProblemContract problem, string planId, DateTimeOffset generatedAtUtc)
{
    ArgumentNullException.ThrowIfNull(problem);
    return ScheduleNormalized(SchedulingProblemNormalizer.Normalize(problem), planId, generatedAtUtc);
}

internal SchedulePlanContract ScheduleNormalized(
    SchedulingProblemContract normalizedProblem,
    string planId,
    DateTimeOffset generatedAtUtc)
{
    var state = SchedulerState.From(normalizedProblem, planId, generatedAtUtc);
    state.ReserveLockedAssignments();
    state.ScheduleOpenOperations();
    return state.ToPlan();
}
```

Add `<InternalsVisibleTo Include="Nerv.IIP.Business.Performance.Tests" />` beside the existing project item groups.

- [ ] **Step 4: Run focused Scheduling tests and verify GREEN**

Run the parity test, then all `FiniteCapacitySchedulerTests`. Expected: all pass with no new warnings.

- [ ] **Step 5: Commit**

```powershell
git add backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Scheduling/FiniteCapacityScheduler.cs backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Nerv.IIP.Business.Scheduling.Web.csproj backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/FiniteCapacitySchedulerTests.cs
git commit -m "refactor(scheduling): expose normalized benchmark seam"
```

### Task 2: Add deterministic scale input and evidence contracts

**Files:**
- Modify: `backend/tests/Nerv.IIP.Business.Performance.Tests/Nerv.IIP.Business.Performance.Tests.csproj`
- Create: `backend/tests/Nerv.IIP.Business.Performance.Tests/SchedulingScaleProblemFactory.cs`
- Create: `backend/tests/Nerv.IIP.Business.Performance.Tests/SchedulingScaleEvidence.cs`
- Create: `backend/tests/Nerv.IIP.Business.Performance.Tests/SchedulingScaleEvidenceTests.cs`

**Interfaces:**
- Produces: `SchedulingScaleProfile`, `SchedulingScaleProblemFactory.Create(profile)`, `SchedulingScaleRunEvidence`, `SchedulingScaleProfileEvidence`, and `SchedulingScaleEvidenceDocument.Write(directory)`.
- Consumes: production Scheduling contracts and `SchedulingJson.Options`.

- [ ] **Step 1: Write failing factory-count and determinism tests**

```csharp
[Theory]
[InlineData("demo", 100)]
[InlineData("medium", 500)]
[InlineData("stress", 1000)]
public void Factory_creates_fixed_deterministic_profiles(string name, int orderCount)
{
    var profile = SchedulingScaleProfile.All.Single(x => x.Name == name);
    var first = SchedulingScaleProblemFactory.Create(profile);
    var second = SchedulingScaleProblemFactory.Create(profile);

    Assert.Equal(orderCount, first.Orders.Count);
    Assert.Equal(orderCount * 4, first.Orders.Sum(x => x.Operations.Count));
    Assert.Equal(24, first.Resources.Count);
    Assert.Equal(
        JsonSerializer.Serialize(first, SchedulingJson.Options),
        JsonSerializer.Serialize(second, SchedulingJson.Options));
}
```

- [ ] **Step 2: Write failing evidence and stability tests**

Assert the document contains every required phase/KPI/memory field, the exact disclaimer, and rejects profile runs whose canonical output hashes differ.

- [ ] **Step 3: Run tests and verify RED**

Run:

```powershell
dotnet test backend/tests/Nerv.IIP.Business.Performance.Tests/Nerv.IIP.Business.Performance.Tests.csproj --filter "FullyQualifiedName~SchedulingScale"
```

Expected: compile failure because the Scheduling scale types do not exist.

- [ ] **Step 4: Implement the fixed factory and evidence records**

Add the Scheduling Web project reference. Freeze `SchedulingScaleProfile.All` to demo/medium/stress. Build stable order/resource/calendar identifiers from ordinal indexes, use no random value or current clock, and use existing Material/Quality/Tooling/Capacity reason paths for the fixed blocker mixture.

The evidence document must expose:

```csharp
public const string CapabilityDisclaimer =
    "APS Lite deterministic finite-capacity heuristic; no global optimality claim.";

public void EnsureStable()
{
    foreach (var profile in Profiles)
    {
        if (profile.Runs.Select(x => x.OutputHash).Distinct(StringComparer.Ordinal).Count() != 1)
        {
            throw new InvalidOperationException($"Profile '{profile.Profile}' produced unstable schedule output.");
        }
    }
}
```

Write indented JSON and a compact Markdown table with machine context and disclaimer.

- [ ] **Step 5: Run tests and verify GREEN**

Run the new focused tests. Expected: profile counts and deterministic serialization pass; mismatch detection fails only in the asserted exception case.

- [ ] **Step 6: Commit**

```powershell
git add backend/tests/Nerv.IIP.Business.Performance.Tests
git commit -m "test(scheduling): add fixed APS scale profiles"
```

### Task 3: Add PostgreSQL benchmark execution

**Files:**
- Modify: `backend/tests/Nerv.IIP.Business.Performance.Tests/BusinessPerformanceServiceProvider.cs`
- Create: `backend/tests/Nerv.IIP.Business.Performance.Tests/SchedulingScaleBenchmarkTests.cs`
- Create: `backend/tests/Nerv.IIP.Business.Performance.Tests/ProcessMemorySampler.cs`
- Test: `backend/tests/Nerv.IIP.Business.Performance.Tests/SchedulingScaleBenchmarkTests.cs`

**Interfaces:**
- Consumes: `NERV_IIP_PERF_POSTGRES`, `NERV_IIP_APS_SCALE_RUNS`, `NERV_IIP_APS_SCALE_EVIDENCE_DIRECTORY`, `NERV_IIP_APS_SCALE_COMMIT`, fixed profiles, the normalized scheduler seam, Scheduling EF context and domain aggregate mapper.
- Produces: one JSON and one Markdown evidence file and a failing test result if any profile is unstable.

- [ ] **Step 1: Write the gated benchmark test skeleton**

Add `[PerformanceBaselineFact("scheduling")]`, loop exactly three profiles and the configured repetitions (default 3, clamped to 3 for the governed script), and call not-yet-implemented helpers for phase measurement, persistence, cleanup, hash, and evidence writing.

- [ ] **Step 2: Run with the scheduling scenario and verify RED**

Start PostgreSQL through the existing development compose profile, set `NERV_IIP_PERF_POSTGRES`, and run only the new test. Expected: compile failure for the missing benchmark helpers.

- [ ] **Step 3: Implement Scheduling PostgreSQL provider and measurement helpers**

Add `CreateSchedulingProvider`/`MigrateSchedulingAsync`. For each repetition:

```csharp
var input = Measure(() => SchedulingScaleProblemFactory.Create(profile));
var normalized = Measure(() => SchedulingProblemNormalizer.Normalize(input.Value));
var plan = Measure(() => scheduler.ScheduleNormalized(normalized.Value, profile.PlanId, FixedGeneratedAtUtc));
var persistenceMs = await PersistAsync(db, normalized.Value, plan.Value, cancellationToken);
```

Measure total time around all four phases. Sample `Process.WorkingSet64` and `GC.GetTotalMemory(false)` during the run. Persist `ScheduleProblemSnapshot` plus `SchedulePlan.FromGeneratedPlan(...)` and `SaveChangesAsync`; migrations and cleanup are outside measured timing.

- [ ] **Step 4: Canonicalize output and enforce three-run stability**

Hash production metrics, ordered assignments, loads, conflicts, and unscheduled operations with SHA-256. Exclude elapsed time, memory, capture timestamp, and machine context. Call `EnsureStable()` before writing evidence.

- [ ] **Step 5: Run the PostgreSQL benchmark and verify GREEN**

Expected: 9 successful repetitions, all three profiles `stable=true`, and JSON/Markdown files present with non-negative phase timings, positive memory, KPI values, and reason distributions.

- [ ] **Step 6: Commit**

```powershell
git add backend/tests/Nerv.IIP.Business.Performance.Tests
git commit -m "test(scheduling): benchmark APS Lite scale on PostgreSQL"
```

### Task 4: Add the governed fixed-data script and docs

**Files:**
- Create: `scripts/verify-business-scheduling-scale-benchmark.ps1`
- Modify: `docs/architecture/script-automation-governance.md`
- Modify: `docs/architecture/implementation-readiness.md`

**Interfaces:**
- Consumes: Docker Desktop, `infra/docker-compose.dev.yml`, .NET 10, the performance test project, and `ScriptAutomation.ps1` helpers.
- Produces: `artifacts/script-logs/business-scheduling-scale-benchmark/<UTC-run-id>/aps-lite-scale-benchmark.{json,md}`.

- [ ] **Step 1: Add the governed script with fixed profiles**

Declare `Category: verify`, startup/writes/cleanup/requires metadata, optional `-SkipRestore`, and no profile-size parameter. Use `Invoke-DockerCompose`, `Invoke-DotNet`, `Invoke-NativeCommandOutput`, and `Invoke-WithScopedEnvironment`; do not invoke `dotnet` or `docker` directly.

- [ ] **Step 2: Verify the script fails closed on missing evidence**

After the test command, assert both evidence files exist and parse the JSON. Assert profiles are exactly `demo`, `medium`, and `stress`, every profile is stable, and every profile has exactly three runs.

- [ ] **Step 3: Run script governance**

Run:

```powershell
pwsh scripts/check-script-governance.ps1
```

Expected: pass with the new script classified and no raw governed command violation.

- [ ] **Step 4: Run the complete benchmark and capture evidence**

Run:

```powershell
pwsh scripts/verify-business-scheduling-scale-benchmark.ps1
```

Expected: PostgreSQL starts, the performance project passes the Scheduling scenario, and the script prints the exact JSON/Markdown paths.

- [ ] **Step 5: Update documentation from the fresh evidence**

Add the script to the governance inventory. Add a MAN-581 readiness section with the actual machine/runtime profile, profile counts, median phase timings, peak memory, KPI/reason summary, stability result, evidence path, and explicit heuristic/no-global-optimum boundary.

- [ ] **Step 6: Commit**

```powershell
git add scripts/verify-business-scheduling-scale-benchmark.ps1 docs/architecture/script-automation-governance.md docs/architecture/implementation-readiness.md
git commit -m "docs(scheduling): publish APS Lite scale evidence"
```

### Task 5: Full verification and ready PR

**Files:**
- Verify all changed files and generated runtime evidence; do not stage `artifacts/`.

- [ ] **Step 1: Run focused and governed checks fresh**

```powershell
dotnet test backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/Nerv.IIP.Business.Scheduling.Web.Tests.csproj --filter "FullyQualifiedName~FiniteCapacitySchedulerTests"
dotnet test backend/tests/Nerv.IIP.Business.Performance.Tests/Nerv.IIP.Business.Performance.Tests.csproj --filter "FullyQualifiedName~SchedulingScale" --no-restore
pwsh scripts/check-script-governance.ps1
pwsh scripts/verify-business-scheduling-scale-benchmark.ps1 -SkipRestore
dotnet test backend/Nerv.IIP.sln --no-restore
```

Expected: all commands pass. The benchmark run produces fresh stable 3×3 evidence.

- [ ] **Step 2: Inspect scope and diff**

Run `git status --short`, `git diff --check`, `git diff origin/main...HEAD --stat`, and inspect every changed file. Confirm no generated `artifacts/`, unrelated issue work, endpoint, contract, client, migration, or solver dependency is tracked.

- [ ] **Step 3: Push and create the ready PR**

Push `codex/man-581-aps-lite-scale-benchmark` and create a non-draft PR targeting `main`. The body must include `Fixes #1050`, summarize profile/evidence results, list verification commands, say `文档：有影响` for the readiness/governance evidence update, state “业务 HTTP endpoint：无新增/修改，facade matrix 无需更新”, and repeat the no-global-optimum boundary.

- [ ] **Step 4: Verify live PR state and stop**

Use `gh pr view --json number,url,isDraft,state,mergeable,headRefName,baseRefName,statusCheckRollup` and confirm `isDraft=false`, `state=OPEN`, `headRefName` is the feature branch, and `baseRefName=main`. Report the PR URL and wait for review; do not merge.

