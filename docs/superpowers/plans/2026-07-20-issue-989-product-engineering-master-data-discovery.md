# Issue #989 ProductEngineering MasterData Discovery Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ensure the Aspire AppHost supplies ProductEngineering with the ephemeral BusinessMasterData HTTP endpoint and startup dependency so EBOM active-reference validation never falls back to `localhost:5107` in managed full-stack sessions.

**Architecture:** Keep `MasterData:BaseUrl` as the existing service contract and make the platform AppHost its deployment-time source of truth. Add an architecture contract test that scopes assertions to the `businessProductEngineering` resource block, then add the minimal environment injection, resource reference, and readiness wait; do not change ProductEngineering validation or Gateway behavior.

**Tech Stack:** .NET 10, Aspire AppHost, xUnit architecture tests, governed PowerShell full-stack runner.

---

### Task 1: Lock the AppHost dependency contract with a failing test

**Files:**
- Modify: `backend/tests/Nerv.IIP.FastEndpoints.Architecture.Tests/FastEndpointsArchitectureTests.cs`
- Test: `backend/tests/Nerv.IIP.FastEndpoints.Architecture.Tests/FastEndpointsArchitectureTests.cs`

- [ ] **Step 1: Add a focused contract test**

Add a test that reads `infra/aspire/Nerv.IIP.AppHost/Program.cs`, extracts only the section from `var businessProductEngineering =` through the next resource declaration, and requires all three statements:

```csharp
Assert.Contains(
    ".WithEnvironment(\"MasterData__BaseUrl\", businessMasterData.GetEndpoint(\"http\"))",
    resourceBlock);
Assert.Contains(".WithReference(businessMasterData)", resourceBlock);
Assert.Contains(".WaitFor(businessMasterData)", resourceBlock);
```

Also assert that the scoped resource block does not contain `localhost:5107`, so fixed-port fallback cannot satisfy the AppHost contract.

- [ ] **Step 2: Run the focused test and verify RED**

Run:

```powershell
dotnet test backend/tests/Nerv.IIP.FastEndpoints.Architecture.Tests/Nerv.IIP.FastEndpoints.Architecture.Tests.csproj --filter "FullyQualifiedName~Aspire_apphost_product_engineering_uses_master_data_service_discovery"
```

Expected: FAIL because ProductEngineering currently lacks the `MasterData__BaseUrl` injection/reference/wait in its AppHost resource block.

- [ ] **Step 3: Commit only after the production change and GREEN verification in Task 2**

Keep the failing test uncommitted until the minimal implementation is present and verified.

### Task 2: Wire ProductEngineering to BusinessMasterData

**Files:**
- Modify: `infra/aspire/Nerv.IIP.AppHost/Program.cs`
- Test: `backend/tests/Nerv.IIP.FastEndpoints.Architecture.Tests/FastEndpointsArchitectureTests.cs`

- [ ] **Step 1: Add the minimal Aspire wiring**

In the `businessProductEngineering` resource chain add:

```csharp
.WithEnvironment("MasterData__BaseUrl", businessMasterData.GetEndpoint("http"))
.WithReference(businessMasterData)
.WaitFor(businessMasterData)
```

Retain PostgreSQL, Redis/RabbitMQ, internal bearer token, and ProductEngineering application behavior unchanged.

- [ ] **Step 2: Run the focused test and verify GREEN**

Run the same filtered `dotnet test` command from Task 1.

Expected: PASS with zero failed tests.

- [ ] **Step 3: Run the AppHost build**

Run:

```powershell
dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj
```

Expected: build succeeds with zero warnings and zero errors.

- [ ] **Step 4: Commit the red-green change**

Stage the architecture test and AppHost program, then commit with a focused `fix(apphost): wire product engineering to master data` message.

### Task 3: Verify governed checks and the real leader-demo session

**Files:**
- Verify: `scripts/check-script-governance.ps1`
- Verify: `backend/Nerv.IIP.sln`
- Runtime evidence: `artifacts/fullstack/<sessionId>/leader-demo-main-chain-evidence.json` (not committed)

- [ ] **Step 1: Run affected static and backend gates**

Run:

```powershell
scripts/check-script-governance.ps1
dotnet test backend/Nerv.IIP.sln
```

Expected: both exit successfully. If a failure is environmental or baseline-related, capture exact evidence and determine whether it is introduced by this change before proceeding.

- [ ] **Step 2: Run the required managed real-stack scenario**

Run:

```powershell
.\nerv.ps1 fullstack run -Scenario leader-demo-main-chain
```

Expected for #989: the evidence ledger advances past EBOM release and no ProductEngineering request targets `localhost:5107`. The overall scenario may still exit nonzero if a later #965 chain gap appears; record that later breakpoint without fixing it in this PR.

- [ ] **Step 3: Verify cleanup and evidence**

Inspect the produced manifest/evidence and managed-session status. Require the exact session to be `Stopped`, cleanup errors to be empty, and no session-owned containers, networks, or volumes to remain.

- [ ] **Step 4: Review scope and commit any necessary test-only evidence contract adjustments**

Confirm the diff contains only the #989 AppHost wiring, its focused contract test, and this execution plan. Do not commit runtime artifacts or changes for later #965 breakpoints.

### Task 4: Publish one issue-scoped PR

**Files:**
- Review: all committed files and commit history

- [ ] **Step 1: Run fresh completion verification**

Re-run the focused architecture test, AppHost build, script governance gate, backend solution tests, and required leader-demo scenario as needed so every PR claim is supported by fresh output.

- [ ] **Step 2: Push the issue branch**

Push the `codex/989-apphost-masterdata-discovery` branch to `origin`.

- [ ] **Step 3: Create the PR and stop**

Create one PR whose body includes `Fixes #989`, `Refs #965`, `Linear MAN-524`, the exact verification outcomes, later-breakpoint evidence if present, cleanup status, and `文档：无影响`. Do not merge; wait for review.
