# DemandPlanning MRP Gap Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement issue #409 MRP net-requirement hardening for DemandPlanning.

**Architecture:** Keep MRP as a pure calculation unit fed by immutable snapshots. DemandPlanning stores calculation results and suggestion release dates, but all upstream business facts remain owned by ProductEngineering, Inventory, ERP, MES, and MasterData.

**Tech Stack:** .NET 10, FastEndpoints, EF Core PostgreSQL, xUnit, NetCorePal CleanDDD patterns.

---

## Files

- Modify: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Planning/MrpCalculator.cs`
- Modify: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Planning/PlanningInputAdapters.cs`
- Modify: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Commands/RunMrpCommand.cs`
- Modify: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Domain/AggregatesModel/PlanningSuggestionAggregate/PlanningSuggestion.cs`
- Modify: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Infrastructure/EntityConfigurations/PlanningSuggestionEntityTypeConfiguration.cs`
- Add: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Infrastructure/Migrations/*_AddPlanningSuggestionReleaseDate.cs`
- Modify: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`
- Modify: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/Queries/DemandPlanningQueries.cs`
- Modify: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/IntegrationEvents/DemandPlanningIntegrationEvents.cs`
- Modify: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Application/IntegrationEventConverters/DemandPlanningIntegrationEventConverters.cs`
- Modify: `backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Program.cs`
- Modify: `backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/MrpCalculatorTests.cs`
- Modify: `backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/PlanningInputAdapterTests.cs`
- Modify: `backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/DemandPlanningEndpointContractTests.cs`
- Modify: `backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Domain.Tests/DemandPlanningAggregateTests.cs`
- Modify: `docs/architecture/database-schema-catalog.md`
- Modify: `docs/architecture/implementation-readiness.md`

## Task 1: Red Tests

- [ ] Add calculator tests for scheduled receipts, multi-level BOM, release date lead time, daily bucket lot sizing, and safety stock.
- [ ] Add adapter tests proving ProductEngineering lot-size values and ERP purchase-order scheduled receipts enter snapshots.
- [ ] Run `dotnet test backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/Nerv.IIP.Business.DemandPlanning.Web.Tests.csproj --no-restore --filter FullyQualifiedName~MrpCalculatorTests` and confirm the new tests fail for missing behavior.

## Task 2: Calculator and Snapshot Implementation

- [ ] Extend `MrpCalculationInput`, `ProductionVersionSnapshot`, and snapshot result records.
- [ ] Implement bucketed netting with scheduled receipts and safety stock.
- [ ] Implement recursive BOM expansion with make/buy split based on production-version availability.
- [ ] Implement release-date offset and L4L/min/max/multiple lot sizing.
- [ ] Run the focused calculator tests and keep the original deterministic fixture passing.

## Task 3: Persistence/API Release Date

- [ ] Add `ReleaseDate` to `PlanningSuggestion`, factory creation, EF configuration, query response, and integration event payload.
- [ ] Generate or hand-maintain the EF migration and model snapshot for `planning_suggestions.release_date`.
- [ ] Update aggregate and endpoint contract tests for `ReleaseDate`.
- [ ] Run DemandPlanning Domain/Web focused tests.

## Task 4: Upstream Adapter Wiring

- [ ] Preserve ProductEngineering `LotSizeMin` and `LotSizeMax` in `ProductionVersionSnapshot`.
- [ ] Add an ERP purchase-order scheduled-receipt client using open purchase order line remaining quantities.
- [ ] Register the ERP client in `Program.cs` with `Erp:BaseUrl`.
- [ ] Keep MES scheduled receipts documented as pending because current MES work-order list lacks UOM.

## Task 5: Docs and Verification

- [ ] Update database schema catalog and readiness to record `release_date` and the remaining MES scheduled-receipt limitation.
- [ ] Run `dotnet test` for DemandPlanning Domain and Web projects.
- [ ] Run `scripts/verify-business-demand-planning-mrp-mvp.ps1` if available and not blocked by environment.
- [ ] Run `git diff --check`.
- [ ] Commit, push `codex/issue-409-demand-planning-mrp-gap`, and open a PR with `Closes #409`.
