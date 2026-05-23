# Business Service Registration Verify Readiness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement #140 by coordinating Wave 1 shared integration changes: solution entries, AppHost registration, verify script pattern, authorization/schema documentation and readiness tracking.

**Architecture:** This is a coordinator plan, not a domain feature plan. It applies shared changes after service sessions produce compiling projects and focused tests. It keeps service-owned domain implementation out of AppHost, IAM, scripts and readiness docs.

**Tech Stack:** .NET 10, Aspire AppHost, PowerShell governed scripts, `scripts/lib/ScriptAutomation.ps1`, GitHub issue/PR handoff notes, Markdown architecture docs.

---

## Inputs

This plan consumes the service-session outputs from:

1. #127 ProductEngineering gap completion.
2. #131 Inventory MVP.
3. #132 Quality inspection MVP.
4. #135 MES CleanDDD persistence.

Each service PR must include `Shared Changes Needed` in its PR body. If a service is not merged or does not compile, skip its registration and record it as blocked in readiness.

## Files

- Modify: `backend/Nerv.IIP.sln`
- Modify: `infra/aspire/Nerv.IIP.AppHost/Program.cs`
- Modify: `docs/architecture/authorization-matrix.md`
- Modify: `docs/architecture/database-schema-catalog.md`
- Modify: `docs/architecture/implementation-readiness.md`
- Create: `scripts/verify-business-product-engineering-mvp.ps1`
- Create: `scripts/verify-business-inventory-mvp.ps1`
- Create: `scripts/verify-business-quality-inspection-mvp.ps1`
- Create: `scripts/verify-business-mes-execution-mvp.ps1`
- Create: `scripts/verify-business-wave1-foundation.ps1`

## Task 1: Collect Service Outputs

- [ ] **Step 1: Inspect service directories**

Run:

```powershell
rg --files backend/services/Business/ProductEngineering backend/services/Business/Inventory backend/services/Business/Quality backend/services/Business/Mes
```

Expected: only services that exist and compile are considered for registration.

- [ ] **Step 2: Inspect shared-change notes**

For each Wave 1 service branch or PR, copy its `Shared Changes Needed` section into the #140 PR description. If no section exists, inspect the service tests and project files before deciding shared changes.

## Task 2: Add Solution Entries

- [ ] **Step 1: Add missing projects to backend solution**

Run `dotnet sln backend/Nerv.IIP.sln add` for each compiling project that is not already in the solution. Use exact paths from `rg --files` output.

Required Wave 1 candidates:

```powershell
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/Nerv.IIP.Business.ProductEngineering.Domain.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Infrastructure/Nerv.IIP.Business.ProductEngineering.Infrastructure.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Nerv.IIP.Business.ProductEngineering.Web.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/Nerv.IIP.Business.ProductEngineering.Web.Tests.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Domain/Nerv.IIP.Business.Quality.Domain.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Infrastructure/Nerv.IIP.Business.Quality.Infrastructure.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Quality/src/Nerv.IIP.Business.Quality.Web/Nerv.IIP.Business.Quality.Web.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Domain.Tests/Nerv.IIP.Business.Quality.Domain.Tests.csproj
dotnet sln backend/Nerv.IIP.sln add backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Web.Tests/Nerv.IIP.Business.Quality.Web.Tests.csproj
```

Add Inventory and MES projects only after those directories exist.

- [ ] **Step 2: Verify solution build**

Run:

```powershell
dotnet build backend/Nerv.IIP.sln --no-restore
```

Expected: solution builds. If a Wave 1 service fails, remove only that service from this #140 integration batch and document the blocker.

## Task 3: Register AppHost Services

- [ ] **Step 1: Add AppHost registrations**

Modify `infra/aspire/Nerv.IIP.AppHost/Program.cs` using the existing service registration style. Register only Web projects that compile and have stable local ports assigned by the port matrix.

Candidate service names:

1. `business-product-engineering`
2. `business-inventory`
3. `business-quality`
4. `business-mes`

- [ ] **Step 2: Verify AppHost build**

Run:

```powershell
dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore
```

Expected: AppHost builds.

## Task 4: Add Verify Scripts

- [ ] **Step 1: Create service verify scripts**

Each script must dot-source `scripts/lib/ScriptAutomation.ps1` and use helper functions such as `Invoke-DotNet`. Do not call `dotnet` directly inside scripts.

Create scripts for services that exist:

1. `scripts/verify-business-product-engineering-mvp.ps1`
2. `scripts/verify-business-inventory-mvp.ps1`
3. `scripts/verify-business-quality-inspection-mvp.ps1`
4. `scripts/verify-business-mes-execution-mvp.ps1`

Each script runs only focused Domain and Web tests for its service.

- [ ] **Step 2: Create Wave 1 aggregate verify script**

Create `scripts/verify-business-wave1-foundation.ps1`. It should run:

1. `scripts/verify-business-master-data-realignment.ps1`
2. Every Wave 1 service verify script that exists.
3. `dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore`.

- [ ] **Step 3: Run script governance**

Run:

```powershell
scripts/check-script-governance.ps1
```

Expected: script governance passes.

## Task 5: Update Authorization, Schema Catalog And Readiness

- [ ] **Step 1: Update authorization matrix**

Add Wave 1 permissions from the service specs and plans. Minimum entries:

1. `business.engineering.boms.manage`
2. `business.engineering.routings.manage`
3. `business.engineering.changes.manage`
4. `business.inventory.locations.manage`
5. `business.inventory.movements.create`
6. `business.inventory.ledger.read`
7. `business.inventory.counts.manage`
8. `business.quality.inspection-plans.manage`
9. `business.quality.inspection-records.create`
10. `business.quality.inspection-records.read`
11. `business.mes.work-orders.manage`

- [ ] **Step 2: Update database schema catalog**

Add or refresh schema entries for:

1. `product_engineering`
2. `inventory`
3. `quality`
4. `mes`

Only document tables that exist in migrations in the current branch.

- [ ] **Step 3: Update implementation readiness**

Update `docs/architecture/implementation-readiness.md` with:

1. Which Wave 1 services compile.
2. Which verify scripts exist and pass.
3. Which services are registered in AppHost.
4. Which downstream issues are unblocked.
5. Any blockers such as Docker availability or unmerged service branches.

## Task 6: Final Verification

- [ ] **Step 1: Run focused Wave 1 verification**

Run:

```powershell
scripts/verify-business-wave1-foundation.ps1
```

Expected: script exits `0` for registered services. If Docker-dependent checks are unavailable, report them as environmental blockers only when the script clearly requires Docker.

- [ ] **Step 2: Run backend build**

Run:

```powershell
dotnet build backend/Nerv.IIP.sln --no-restore
```

Expected: build passes.

- [ ] **Step 3: Report integration state**

In the #140 PR body, include a `Wave 1 Integration State` section. Each service line must use one of these exact state words: `registered`, `skipped`, or `blocked`, followed by the reason and verification command. Include separate lines for AppHost, verify scripts, downstream unblocked issues and blockers.
