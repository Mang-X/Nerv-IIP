# Business Wave 2 Registration Verify Readiness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Coordinate shared integration for Wave 2 services after #128, #133, #134 and #136 service branches are ready.

**Architecture:** This is a coordinator plan. It applies shared solution, AppHost, verify script and readiness changes after service sessions produce compiling projects and focused tests. It must not implement service domain behavior.

**Tech Stack:** .NET 10, Aspire AppHost, governed PowerShell scripts, `scripts/lib/ScriptAutomation.ps1`, Markdown architecture docs.

---

## Inputs

This plan consumes service-session outputs from:

1. #128 DemandPlanning MVP.
2. #133 BarcodeLabel MVP.
3. #134 BusinessApproval MVP.
4. #136 WMS execution MVP.

Each service PR/session should include `Shared Changes Needed`. If a service does not compile, skip registration and record it as blocked.

## Files

- Modify: `backend/Nerv.IIP.sln`
- Modify: `infra/aspire/Nerv.IIP.AppHost/Program.cs`
- Modify: `docs/architecture/authorization-matrix.md`
- Modify: `docs/architecture/database-schema-catalog.md`
- Modify: `docs/architecture/implementation-readiness.md`
- Create: `scripts/verify-business-demand-planning-mvp.ps1`
- Create: `scripts/verify-business-barcode-label-mvp.ps1`
- Create: `scripts/verify-business-approval-mvp.ps1`
- Create: `scripts/verify-business-wms-execution-mvp.ps1`
- Create: `scripts/verify-business-wave2-execution.ps1`

## Task 1: Collect Service Outputs

- [ ] **Step 1: Inspect service directories**

Run:

```powershell
rg --files backend/services/Business/DemandPlanning backend/services/Business/BarcodeLabel backend/services/Business/Approval backend/services/Business/Wms
```

Expected: only services that exist and compile are considered for registration.

- [ ] **Step 2: Inspect shared-change notes**

For each Wave 2 branch or session, copy its `Shared Changes Needed` section into the integration summary. If no section exists, inspect the service tests and project files before deciding shared changes.

## Task 2: Add Solution Entries

- [ ] **Step 1: Add ready projects to backend solution**

Run `dotnet sln backend/Nerv.IIP.sln add` for each ready Domain, Infrastructure, Web and test project.

Candidate service roots:

1. `backend/services/Business/DemandPlanning`
2. `backend/services/Business/BarcodeLabel`
3. `backend/services/Business/Approval`
4. `backend/services/Business/Wms`

- [ ] **Step 2: Verify solution build**

Run:

```powershell
dotnet build backend/Nerv.IIP.sln --no-restore
```

Expected: solution builds. If a Wave 2 service fails, remove only that service from this integration batch and document the blocker.

## Task 3: Register AppHost Services

- [ ] **Step 1: Add AppHost databases and service registrations**

Use the existing Wave 1 business service registration style. Candidate service names:

1. `business-demand-planning`
2. `business-barcode-label`
3. `business-approval`
4. `business-wms`

Proposed next local ports after Wave 1 are `5112-5115`, but preserve any existing port matrix if it has already been updated.

- [ ] **Step 2: Add cross-service base URLs only when needed**

If WMS calls Inventory over HTTP in this batch, pass `Inventory__BaseUrl` or the service's established configuration equivalent. If DemandPlanning calls ProductEngineering/Inventory over HTTP, pass `ProductEngineering__BaseUrl` and `Inventory__BaseUrl`.

- [ ] **Step 3: Verify AppHost build**

Run:

```powershell
dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore
```

Expected: AppHost builds.

## Task 4: Add Verify Scripts

- [ ] **Step 1: Create service verify scripts**

Each script must dot-source `scripts/lib/ScriptAutomation.ps1` and use helper functions such as `Invoke-DotNet`. Do not call `dotnet` directly inside scripts.

Create scripts for ready services:

1. `scripts/verify-business-demand-planning-mvp.ps1`
2. `scripts/verify-business-barcode-label-mvp.ps1`
3. `scripts/verify-business-approval-mvp.ps1`
4. `scripts/verify-business-wms-execution-mvp.ps1`

Each script runs only focused Domain and Web tests for its service.

- [ ] **Step 2: Create Wave 2 aggregate verify script**

Create `scripts/verify-business-wave2-execution.ps1`. It should run:

1. `scripts/verify-business-wave1-foundation.ps1`
2. Every ready Wave 2 service verify script.
3. `dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore`.

- [ ] **Step 3: Run script governance**

Run:

```powershell
scripts/check-script-governance.ps1
```

Expected: script governance passes.

## Task 5: Update Authorization, Schema Catalog And Readiness

- [ ] **Step 1: Update authorization matrix and IAM seed**

Add permissions from the service specs:

1. `business.planning.*`
2. `business.barcodes.*`
3. `business.approvals.*`
4. `business.wms.*`

- [ ] **Step 2: Update database schema catalog**

Add or refresh entries for:

1. `demand_planning`
2. `barcode`
3. `business_approval`
4. `wms`

Only document tables that exist in migrations in the current branch.

- [ ] **Step 3: Update implementation readiness**

Record:

1. Which Wave 2 services compile.
2. Which verify scripts exist and pass.
3. Which services are registered in AppHost.
4. Which downstream ERP issues are unblocked.
5. Which services are blocked or intentionally deferred.

## Task 6: Final Verification

- [ ] **Step 1: Run focused Wave 2 verification**

Run:

```powershell
scripts/verify-business-wave2-execution.ps1
```

Expected: script exits `0` for registered services.

- [ ] **Step 2: Run backend build**

Run:

```powershell
dotnet build backend/Nerv.IIP.sln --no-restore
```

Expected: build passes.

- [ ] **Step 3: Report integration state**

In the session summary, include a `Wave 2 Integration State` section. Each service line must use one of these exact state words: `registered`, `skipped`, or `blocked`, followed by the reason and verification command.

