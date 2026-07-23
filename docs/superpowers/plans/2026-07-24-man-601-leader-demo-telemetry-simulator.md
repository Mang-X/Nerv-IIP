# MAN-601 Leader-demo Telemetry Simulator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver a governed, deterministic PowerShell telemetry simulator for the current isolated leader-demo session that continuously publishes vibration, temperature, and device-state facts through BusinessGateway, proves historical backfill behavior, drives the seeded alarm lifecycle, and leaves auditable evidence without background processes.

**Architecture:** Keep simulation orchestration in a focused PowerShell library with injectable time, delay, HTTP, and evidence seams, then expose it through one governed foreground `verify` entrypoint. Reuse the current leader-demo session pointer, manifest, login flow, and public BusinessGateway facade; update only the opt-in demo prerequisite seed so `DEV-CNC-DEMO` owns both two-second vibration/temperature tags and `ALARM-DEMO-001` evaluates vibration. Persist no result facts in seed or scripts directly.

**Tech Stack:** PowerShell 7, `ScriptAutomation.ps1`, existing full-stack session state/runtime libraries, BusinessGateway public HTTP, .NET 10/xUnit, PostgreSQL + Redis Aspire full-stack.

---

## File Map

- Create `scripts/lib/LeaderDemoTelemetrySimulator.ps1`: deterministic scenario/profile generation, stable source-sequence construction, public HTTP publishing, backfill capability probe/fallback, replay probe, bounded history/alarm verification, and evidence shaping.
- Create `scripts/verify-leader-demo-telemetry-simulator.ps1`: governed foreground CLI that resolves the exact current demo session, logs in using the environment-only password, invokes the simulator, writes redacted JSON/Markdown evidence, and exits with the simulator result.
- Create `scripts/tests/leader-demo-telemetry-simulator.Tests.ps1`: dependency-injected fast tests for profile transitions, deterministic payloads, replay identity, historical acceptance/fallback, public-path-only requests, and clean cancellation.
- Modify `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Seed/LeaderDemoSeedService.cs`: seed the vibration and temperature demo tag definitions plus the vibration alarm rule, without seeding samples, snapshots, or alarms.
- Modify `backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/LeaderDemoSeedServiceTests.cs`: lock the two-tag seed, vibration rule, collision behavior, idempotence, and no-result-facts invariant.
- Modify `scripts/tests/leader-demo.Tests.ps1`: contract-check the governed simulator entrypoint and public telemetry/history/alarm paths.
- Modify `docs/architecture/implementation-readiness.md`: record MAN-601 delivery and the live backfill/10-minute acceptance evidence.
- Modify `docs/architecture/script-automation-governance.md`: register the simulator command, side effects, evidence, and foreground cleanup contract.

### Task 1: Freeze the demo prerequisite contract

**Files:**
- Modify: `backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/LeaderDemoSeedServiceTests.cs`
- Modify: `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Seed/LeaderDemoSeedService.cs`

- [ ] **Step 1: Write the failing seed contract**

Change the happy-path test to require:

```csharp
var tags = await db.TelemetryTags.OrderBy(x => x.TagKey).ToArrayAsync();
Assert.Collection(
    tags,
    vibration =>
    {
        Assert.Equal(LeaderDemoSeedService.TemperatureTagKey, vibration.TagKey);
        Assert.Equal("bucket-2s", vibration.SamplingPolicy);
    },
    temperature =>
    {
        Assert.Equal(LeaderDemoSeedService.VibrationTagKey, temperature.TagKey);
        Assert.Equal("mm/s", temperature.UnitCode);
        Assert.Equal("bucket-2s", temperature.SamplingPolicy);
    });
Assert.Equal(LeaderDemoSeedService.VibrationTagKey, rule.TagKey);
Assert.Equal(8m, rule.ThresholdValue);
```

Keep assertions that two calls are idempotent and `TelemetryRawSamples`, `TelemetrySummaries`, `DeviceStateSnapshots`, and `AlarmEvents` remain empty. Add a collision test for an incompatible reserved vibration tag.

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```powershell
dotnet test backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests.csproj --filter FullyQualifiedName~LeaderDemoSeedServiceTests
```

Expected: FAIL because `VibrationTagKey` and the two-second vibration seed do not exist and the rule still targets temperature.

- [ ] **Step 3: Implement the minimal seed change**

Define:

```csharp
public const string TemperatureTagKey = "temperature";
public const string VibrationTagKey = "vibration";
```

Use one private `EnsureTagAsync` helper to enforce:

```csharp
TemperatureTagKey, "decimal", "degC", "bucket-2s"
VibrationTagKey, "decimal", "mm/s", "bucket-2s"
```

Configure `ALARM-DEMO-001` as an enabled `VIBRATION-HIGH` critical rule on `vibration`, `>= 8m`, `mm/s`, with `0.3m` deadband and 4-second on/off/minimum-duration windows. Preserve collision failure and do not create any runtime facts.

- [ ] **Step 4: Run the focused test and verify GREEN**

Run the Step 2 command.

Expected: PASS with zero failures.

### Task 2: Build the deterministic simulator core with TDD

**Files:**
- Create: `scripts/tests/leader-demo-telemetry-simulator.Tests.ps1`
- Create: `scripts/lib/LeaderDemoTelemetrySimulator.ps1`

- [ ] **Step 1: Write failing profile and payload tests**

The test must dot-source the simulator library and assert:

```powershell
$timeline = New-NervLeaderDemoTelemetryTimeline `
    -RunId 'rehearsal-001' `
    -ScenarioStartUtc ([DateTimeOffset]::Parse('2026-07-24T00:00:00Z')) `
    -DurationSeconds 24 `
    -SampleIntervalSeconds 2 `
    -DegradingAtSeconds 6 `
    -AlarmAtSeconds 12 `
    -RecoveredAtSeconds 18

Assert-Equal @('normal', 'degrading', 'alarm', 'recovered') `
    @($timeline | Select-Object -ExpandProperty Profile -Unique)
Assert-True (($timeline | Where-Object Profile -eq 'degrading')[-1].Vibration -lt 8m)
Assert-True (($timeline | Where-Object Profile -eq 'alarm')[0].Vibration -gt 8m)
Assert-True (($timeline | Where-Object Profile -eq 'recovered')[0].Vibration -lt 7.7m)
```

Assert that identical inputs produce byte-equivalent sample bodies and stable sequences, every body uses `sourceSystem=leader-demo-simulator`, `sourceConnector=business-gateway`, only vibration bodies carry device state, and each bucket is exactly two seconds.

- [ ] **Step 2: Run the fast test and verify RED**

Run:

```powershell
pwsh scripts/tests/leader-demo-telemetry-simulator.Tests.ps1
```

Expected: FAIL because the simulator library and functions do not exist.

- [ ] **Step 3: Implement deterministic generation**

Add focused functions:

```powershell
New-NervLeaderDemoTelemetryTimeline
New-NervLeaderDemoTelemetrySampleBody
Get-NervLeaderDemoTelemetryProfile
Get-NervLeaderDemoTelemetryValue
```

Use index-based deterministic sine/ramp values, invariant decimal rounding, and source sequences shaped as:

```text
leader-demo:<runId>:<phase>:<zero-padded-index>:<tag>
```

Map states as `running` for normal/degrading, `unavailable` for alarm, and `available` for recovered.

- [ ] **Step 4: Run the fast test and verify GREEN**

Run the Step 2 command.

Expected: deterministic profile and payload tests pass.

- [ ] **Step 5: Write failing orchestration tests**

Inject `HttpAction` and `DelayAction` and assert:

- all writes are `POST /api/business-console/v1/telemetry/samples`;
- accepted historical probe sends oldest-to-newest 24-hour-shaped samples and records `historicalBackfill=accepted`;
- a rejected historical probe switches to a declared session-short-window backfill and records `historicalBackfill=rejected-fallback`;
- replaying an identical sample returns the same summary/state IDs and does not change the stable payload;
- history verification calls `GET /api/business-console/v1/telemetry/devices/DEV-CNC-DEMO/history`;
- alarm verification calls `GET /api/business-console/v1/telemetry/alarms`;
- injected cancellation ends before the next delay and returns a stopped evidence result.

- [ ] **Step 6: Run orchestration tests and verify RED**

Run the Step 2 command.

Expected: FAIL because orchestration and verification functions are missing.

- [ ] **Step 7: Implement minimal orchestration**

Add:

```powershell
Invoke-NervLeaderDemoTelemetrySimulator
Invoke-NervLeaderDemoTelemetryRequest
Invoke-NervLeaderDemoHistoricalBackfill
Test-NervLeaderDemoHistoricalSampleAcceptance
Test-NervLeaderDemoTelemetryReplay
Get-NervLeaderDemoTelemetryHistoryEvidence
Get-NervLeaderDemoTelemetryAlarmEvidence
```

Require bounded positive durations, a strictly ordered `normal -> degrading -> alarm -> recovered` timeline, and exact organization/environment/device scope. Treat HTTP/non-contract failures as fail-closed; only the historical capability probe may select the explicit fallback. Never invoke database, Docker, or service-internal endpoints.

- [ ] **Step 8: Run orchestration tests and verify GREEN**

Run the Step 2 command.

Expected: all simulator fast tests pass with no live services.

### Task 3: Add the governed foreground entrypoint and documentation

**Files:**
- Create: `scripts/verify-leader-demo-telemetry-simulator.ps1`
- Modify: `scripts/tests/leader-demo.Tests.ps1`
- Modify: `docs/architecture/script-automation-governance.md`

- [ ] **Step 1: Write the failing entrypoint contract**

Extend `leader-demo.Tests.ps1` to require that the new script:

```powershell
. scripts/lib/ScriptAutomation.ps1
. scripts/lib/FullStackSessionRuntime.ps1
. scripts/lib/LeaderDemoTelemetrySimulator.ps1
```

and exposes bounded `DurationMinutes`, default `SampleIntervalSeconds=2`, opt-in historical backfill, and an `artifacts/leader-demo` evidence path. Assert the core contains only BusinessGateway telemetry sample/history/alarm paths and no SQL/provider command.

- [ ] **Step 2: Run the script contract and verify RED**

Run:

```powershell
pwsh scripts/tests/leader-demo.Tests.ps1
```

Expected: FAIL because the governed entrypoint does not exist.

- [ ] **Step 3: Implement the governed CLI**

Add a `verify` governance header documenting:

- foreground network writes to the current demo session only;
- JSON/Markdown evidence under `artifacts/leader-demo/<sessionId>/`;
- no background process;
- Ctrl+C/failure/normal completion all finalize evidence;
- PowerShell 7 and a current healthy leader-demo session are required.

Resolve the exact owned session with `Resolve-NervLeaderDemoOwnedSession`, read `NERV_IIP_LEADER_DEMO_ADMIN_PASSWORD`, log in through the manifest Gateway URL, call only the manifest BusinessGateway URL, and pass the bearer token only in request headers. Never print or persist the password/token.

- [ ] **Step 4: Document the command**

Register:

```powershell
$env:NERV_IIP_LEADER_DEMO_ADMIN_PASSWORD = '<local-only>'
pwsh scripts/verify-leader-demo-telemetry-simulator.ps1 `
  -DurationMinutes 20 `
  -HistoricalBackfill
```

Document default phase timing, stable `RunId`/`ScenarioStartUtc` replay semantics, evidence location, public-path guarantee, historical fallback declaration, and foreground stop behavior.

- [ ] **Step 5: Run fast script gates and verify GREEN**

Run:

```powershell
pwsh scripts/tests/leader-demo-telemetry-simulator.Tests.ps1
pwsh scripts/tests/leader-demo.Tests.ps1
pwsh scripts/check-script-governance.ps1
```

Expected: all three exit 0.

### Task 4: Focused backend and static verification

**Files:**
- Modify only files already listed.

- [ ] **Step 1: Run IndustrialTelemetry tests**

Run:

```powershell
dotnet test backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests.csproj
```

Expected: all tests pass, zero warnings/errors.

- [ ] **Step 2: Run whitespace/static checks**

Run:

```powershell
git diff --check
pwsh scripts/check-script-governance.ps1
```

Expected: both exit 0.

### Task 5: Real-stack acceptance and issue evidence

**Files:**
- Modify: `docs/architecture/implementation-readiness.md`

- [ ] **Step 1: Start/reset the isolated demo**

Run with the environment-only local password:

```powershell
.\nerv.ps1 demo reset -NoBuild
```

Expected: one exact current Redis + PostgreSQL leader-demo session and successful seed/health evidence.

- [ ] **Step 2: Run the historical capability probe**

Run a short deterministic profile with `-HistoricalBackfill` and fixed `RunId`/`ScenarioStartUtc`.

Expected: evidence states either `accepted` or `rejected-fallback`; history query proves the selected samples are visible. Record the factual result in GitHub #1086 and Linear MAN-601.

- [ ] **Step 3: Run the required 10-minute scenario**

Run:

```powershell
pwsh scripts/verify-leader-demo-telemetry-simulator.ps1 `
  -DurationMinutes 10 `
  -SampleIntervalSeconds 2 `
  -DegradingAtMinutes 2 `
  -AlarmAtMinutes 5 `
  -RecoveredAtMinutes 8 `
  -HistoricalBackfill
```

Expected evidence:

- current state freshness advances throughout the run;
- vibration/temperature history contains continuously increasing run-scoped points;
- degrading vibration remains below but approaches 8 mm/s;
- alarm phase raises `ALARM-DEMO-001`;
- recovered phase falls below deadband and clears the alarm;
- replay probe returns identical fact identities;
- evidence contains no secrets.

- [ ] **Step 4: Prove replay idempotence**

Rerun with the exact same `RunId` and `ScenarioStartUtc`, then query the same public history window.

Expected: identical returned identities and unchanged distinct run-scoped fact count.

- [ ] **Step 5: Stop and prove cleanup**

Run:

```powershell
.\nerv.ps1 demo stop
```

Expected: authoritative session state is `Stopped`, cleanup has no remaining owned containers/networks/volumes, and no simulator background process exists.

- [ ] **Step 6: Record readiness evidence**

Add a MAN-601 section to `implementation-readiness.md` containing the exact commit, session/run IDs, accepted/rejected historical result, 10-minute counts and phase results, replay result, evidence path, and cleanup result. Do not commit generated evidence.

### Task 6: Final verification, commit, and ready PR

**Files:**
- All listed files.

- [ ] **Step 1: Run final fresh verification**

Run:

```powershell
pwsh scripts/tests/leader-demo-telemetry-simulator.Tests.ps1
pwsh scripts/tests/leader-demo.Tests.ps1
pwsh scripts/check-script-governance.ps1
dotnet test backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests.csproj
git diff --check
```

Expected: every command exits 0.

- [ ] **Step 2: Review issue scope and diff**

Run:

```powershell
git status --short
git diff --stat origin/main...HEAD
git diff origin/main...HEAD
```

Expected: only MAN-601 simulator, demo seed contract, tests, plan, and required architecture docs changed; no generated artifact or secret is tracked.

- [ ] **Step 3: Commit and push**

Commit with a scoped message, push `codex/issue-1086-telemetry-simulator`, and do not merge.

- [ ] **Step 4: Create one ready PR**

Use `gh pr create` with:

- title referencing MAN-601;
- `Fixes #1086`;
- delivery summary and exact verification evidence;
- historical backfill result;
- script governance result;
- real-stack cleanup result;
- `文档：有影响` with the updated architecture docs;
- explicit statement that no business HTTP endpoint changed, so facade coverage/OpenAPI/codegen are unaffected.

Stop after confirming the PR is open, non-draft, and linked to #1086.
