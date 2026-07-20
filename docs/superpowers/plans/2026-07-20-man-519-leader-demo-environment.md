# MAN-519 Leader Demo Environment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver a repeatable, Redis-backed leader-demo environment with deterministic prerequisite seeds, exact reset/cleanup, health gates, and evidence for MAN-519/#960.

**Architecture:** Extend service-owned opt-in startup seeds for deterministic preconditions, then wrap the existing isolated full-stack lifecycle with a governed `nerv.ps1 demo` command family. Health and evidence use Aspire resource facts plus authenticated public Gateway queries; reset destroys only the exact demo session and recreates it.

**Tech Stack:** .NET 10, EF Core, Aspire CLI/AppHost, PostgreSQL 18, Redis 8, PowerShell 7, Pester-style repository script tests.

---

### Task 1: Deterministic service-owned demo prerequisites

**Files:**
- Modify: `backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Application/Seed/MasterDataSeedService.cs`
- Modify: `backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/MasterDataPostgresProfileTests.cs`
- Create: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Seed/LeaderDemoSeedService.cs`
- Modify: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Program.cs`
- Create: `backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/LeaderDemoSeedServiceTests.cs`
- Create: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Seed/LeaderDemoSeedService.cs`
- Modify: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Program.cs`
- Create: `backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/LeaderDemoSeedServiceTests.cs`
- Create: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Application/Seed/LeaderDemoSeedService.cs`
- Modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Program.cs`
- Create: `backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/LeaderDemoSeedServiceTests.cs`
- Modify: `backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Application/Seed/QualitySeedService.cs`
- Modify: `backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Web.Tests/QualityInspectionEndpointContractTests.cs`
- Create: `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Application/Seed/LeaderDemoSeedService.cs`
- Modify: `backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Program.cs`
- Create: `backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/LeaderDemoSeedServiceTests.cs`

- [ ] **Step 1: Write failing idempotency and boundary tests**

Add focused tests that call each seed twice and assert one reserved business object per key. Assert `WO-DEMO-Q01` is released with zero completed/scrap quantity, the Quality plan is active with no inspection record/NCR, the telemetry seed has no samples/alarm events, and Inventory contains raw stock but no finished-goods stock.

- [ ] **Step 2: Run targeted tests and verify RED**

Run each changed service test project with a filter for the new seed tests. Expected: failures because reserved prerequisite objects or seed services do not exist.

- [ ] **Step 3: Implement minimal service seeds**

Use existing domain factories and each service DbContext. Guard by organization/environment plus the complete reserved business key. Return without mutation on an exact existing fact; throw on an incompatible reserved fact. Use `SaveChangesAsync`/`SaveEntitiesAsync` following the owning service's existing seed pattern.

- [ ] **Step 4: Register opt-in startup seeds**

Add scoped registrations and run the seeds only when `LeaderDemo:Seed:Enabled=true` after Development-only migrations. Do not add endpoints or cross-service references.

- [ ] **Step 5: Run targeted tests and verify GREEN**

Run all changed service test projects. Expected: zero failures and no warnings.

- [ ] **Step 6: Commit**

Commit the prerequisite slice with message `feat(demo): seed deterministic leader prerequisites`.

### Task 2: Redis-backed demo profile and governed command lifecycle

**Files:**
- Modify: `infra/aspire/Nerv.IIP.AppHost/Program.cs`
- Modify: `scripts/lib/FullStackSessionRuntime.ps1`
- Modify: `scripts/lib/FullStackSessionState.ps1`
- Modify: `scripts/fullstack-session.ps1`
- Create: `scripts/leader-demo.ps1`
- Modify: `nerv.ps1`
- Modify: `scripts/tests/dev-entrypoint.Tests.ps1`
- Create: `scripts/tests/leader-demo.Tests.ps1`
- Modify: `scripts/tests/fullstack-session-runtime.Tests.ps1`

- [ ] **Step 1: Write failing command and lifecycle tests**

Test `nerv.ps1 demo start|reset|seed|health-check|stop` dispatch, demo state pointer validation, exact full-stack session stop/reset, automatic `Messaging__Provider=Redis`, and explicit AppHost seed flags. Inject lifecycle actions so fast tests never start Docker.

- [ ] **Step 2: Run script tests and verify RED**

Run `pwsh scripts/tests/dev-entrypoint.Tests.ps1`, `pwsh scripts/tests/fullstack-session-runtime.Tests.ps1`, and `pwsh scripts/tests/leader-demo.Tests.ps1`. Expected: failures for the missing demo command/profile.

- [ ] **Step 3: Implement the demo profile**

Force Redis in ephemeral full-stack environment, record the non-secret messaging provider in the session manifest, enable the explicit service seed flags in AppHost only when `NERV_IIP_LEADER_DEMO=true`, and preserve persistent Development behavior.

- [ ] **Step 4: Implement exact lifecycle commands**

`leader-demo.ps1` must dot-source the governed helpers, require the admin password from `NERV_IIP_LEADER_DEMO_ADMIN_PASSWORD`, store the current session pointer outside git, and invoke the existing full-stack script through `Invoke-PwshScript`. Reset and stop operate only on the validated recorded session ID.

- [ ] **Step 5: Run script tests and verify GREEN**

Run the three script test files again. Expected: zero failures.

- [ ] **Step 6: Commit**

Commit with message `feat(demo): add governed leader environment lifecycle`.

### Task 3: Health gate, public seed verification, and evidence manifest

**Files:**
- Modify: `scripts/leader-demo.ps1`
- Modify: `scripts/lib/FullStackSessionRuntime.ps1`
- Modify: `scripts/tests/leader-demo.Tests.ps1`
- Modify: `scripts/tests/fullstack-session-runtime.Tests.ps1`

- [ ] **Step 1: Write failing health and evidence tests**

Inject Aspire describe, HTTP, authentication, and public-fact query actions. Assert every required resource is checked; a missing/unhealthy resource returns non-zero with its name; a non-Redis manifest fails; fixed business keys must be observed; and evidence contains SHA, UTC time, URLs, roles, result links, diagnostics, and no secret.

- [ ] **Step 2: Run tests and verify RED**

Run the two affected script test files. Expected: failures because health/evidence functions are missing.

- [ ] **Step 3: Implement bounded health and evidence**

Use Aspire `describe`/`wait` through existing helpers for infrastructure and service states. Use the Platform Gateway login and BusinessGateway public read facades for `SO-DEMO-001`, `WO-DEMO-Q01`, `DEV-CNC-DEMO`, and the `MWO-DEMO-001` alarm-rule/source prefix. Write evidence on success and failure before returning the original non-zero result.

- [ ] **Step 4: Verify secret redaction and failure hints**

Include sensitive test values and ensure none appear in JSON or console diagnostics. Each failed resource/fact must include a bounded remediation hint and artifact/log path.

- [ ] **Step 5: Run tests and verify GREEN**

Run the affected script tests plus `scripts/check-script-governance.ps1`. Expected: zero failures.

- [ ] **Step 6: Commit**

Commit with message `feat(demo): verify health and emit evidence manifest`.

### Task 4: Documentation, full verification, and ready PR

**Files:**
- Modify: `docs/architecture/implementation-readiness.md`
- Modify: `docs/architecture/script-automation-governance.md`
- Modify: `infra/aspire/README.md`

- [ ] **Step 1: Update operational documentation**

Document exact commands, controlled local credential injection, Redis assertion, evidence path, reset semantics, and cleanup. State explicitly that seed creates prerequisites only and list prohibited final states.

- [ ] **Step 2: Run deterministic repository gates**

Run targeted backend service tests, `dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj`, all affected script tests, `pwsh scripts/check-script-governance.ps1`, and `git diff --check`.

- [ ] **Step 3: Run real-stack acceptance**

Set `NERV_IIP_LEADER_DEMO_ADMIN_PASSWORD` only in process scope. Run `demo reset` and `demo health-check`, repeat reset three times, confirm identical business-key counts and `Messaging Provider=Redis`, then `demo stop`. Verify the final session reports `state=Stopped remaining=0`. Preserve the evidence paths and report any environment blocker factually.

- [ ] **Step 4: Review scope and requirements**

Compare the diff to MAN-519/#960 line by line. Confirm no final business status is seeded, no endpoint/OpenAPI/client change occurred, and product docs are unaffected.

- [ ] **Step 5: Commit documentation and verification notes**

Commit with message `docs(demo): document repeatable leader environment`.

- [ ] **Step 6: Push and create ready PR**

Push `codex/man-519-960-demo-environment` and create a non-draft PR targeting `main`. The body includes summary, tests, real-stack evidence, risks, schema/OpenAPI impact, product-doc impact, per-endpoint facade declaration (`none`), and `Fixes #960`. Do not merge.

