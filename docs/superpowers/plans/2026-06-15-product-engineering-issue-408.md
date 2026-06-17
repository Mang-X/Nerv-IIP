# ProductEngineering Issue 408 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close ProductEngineering issue #408 backend business gaps around ECO propagation, BOM line semantics, Routing standard-operation snapshots and ProductionVersion validation.

**Architecture:** Keep all rules inside BusinessProductEngineering. Add narrowly scoped aggregate methods and repository lookups; command handlers orchestrate cross-aggregate validation without cross-service database coupling. Persist new owned-row snapshot fields through EF migrations and update schema docs.

**Tech Stack:** .NET 10, CleanDDD, FastEndpoints, EF Core, xUnit, PostgreSQL migrations.

---

### Task 1: ProductEngineering Reference Validation Tests

**Files:**
- Modify: `backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/ProductionVersionApiContractTests.cs`
- Modify: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Infrastructure/Repositories/ProductEngineeringReleaseRepositories.cs`
- Modify: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Commands/ProductionVersions/CreateProductionVersionCommand.cs`
- Modify: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Commands/ProductionVersions/UpdateProductionVersionCommand.cs`

- [ ] Add failing tests for missing, draft, SKU-mismatched and not-yet-effective MBOM/Routing references.
- [ ] Add repository methods to resolve `Code:Revision` MBOM/Routing references.
- [ ] Update create/update handlers to pass real statuses and reject invalid references with `KnownException`.
- [ ] Run ProductEngineering Web tests.

### Task 2: BOM And Routing Snapshot Tests

**Files:**
- Modify: `backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests/ProductEngineeringReleaseAggregateTests.cs`
- Modify: `backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/ProductEngineeringReleaseApiContractTests.cs`
- Modify: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/AggregatesModel/EngineeringBomAggregate/EngineeringBom.cs`
- Modify: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/AggregatesModel/ManufacturingBomAggregate/ManufacturingBom.cs`
- Modify: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/AggregatesModel/RoutingAggregate/Routing.cs`
- Modify: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Commands/ProductEngineeringReleaseCommands.cs`

- [ ] Add failing aggregate tests for BOM substitute/phantom/reference/yield/backflush fields and routing setup/run/teardown/control flags.
- [ ] Add failing command handler test proving routing release loads enabled StandardOperation defaults.
- [ ] Extend aggregate constructors and command records while preserving existing request compatibility.
- [ ] Update routing release handler to require enabled StandardOperation for each operation code.

### Task 3: ECO Propagation Tests

**Files:**
- Modify: `backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/ProductEngineeringReleaseApiContractTests.cs`
- Modify: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/AggregatesModel/EngineeringBomAggregate/EngineeringBom.cs`
- Modify: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/AggregatesModel/ManufacturingBomAggregate/ManufacturingBom.cs`
- Modify: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Domain/AggregatesModel/RoutingAggregate/Routing.cs`
- Modify: `backend/services/Business/ProductEngineering/src/Nerv.IIP.Business.ProductEngineering.Web/Application/Commands/ProductEngineeringReleaseCommands.cs`

- [ ] Add failing test that releases an ECO affecting EBOM, MBOM, Routing and ProductionVersion and then observes those versions archived.
- [ ] Add archive/supersede methods to released EBOM, MBOM and Routing aggregates.
- [ ] Add affected-version resolution in the ECO command handler.
- [ ] Keep the EngineeringChangeReleased event after validation succeeds.

### Task 4: Persistence, Migration And Docs

**Files:**
- Modify: ProductEngineering EF entity configurations.
- Create: ProductEngineering migration under `Infrastructure/Migrations`.
- Modify: `docs/architecture/database-schema-catalog.md`
- Modify: `docs/architecture/implementation-readiness.md`

- [ ] Map all new owned-row columns with max length, precision and comments.
- [ ] Generate a PostgreSQL migration.
- [ ] Update schema catalog/readiness notes for #408 closed-loop behavior.
- [ ] Run schema convention tests.

### Task 5: Verification And Delivery

**Files:**
- All touched ProductEngineering files.

- [ ] Run `dotnet test` for ProductEngineering Domain tests.
- [ ] Run `dotnet test` for ProductEngineering Web tests.
- [ ] Run `scripts/verify-business-product-engineering-mvp.ps1` if local prerequisites allow.
- [ ] Commit, push `codex/issue-408-product-engineering-gap-v2`, and create a PR with `Closes #408`.
