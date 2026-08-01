# MAN-632 Searchable Directories Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver a scope-aware, server-paged searchable-directory facade over existing authoritative MasterData, Inventory, Quality, and Maintenance facts.

**Architecture:** BusinessGateway exposes one directory endpoint and dispatches each directory type to exactly one owner. Inventory adds a narrow read endpoint for location/batch/serial facts; Maintenance's existing reason read gains keyword filtering. MasterData and Quality reuse their current authoritative reads.

**Tech Stack:** .NET 10, FastEndpoints, EF Core, PostgreSQL, xUnit, BusinessGateway OpenAPI, Hey API, pnpm 11.

## Global Constraints

- One issue and one ready PR; do not merge.
- No NLU, offline synchronization, cross-service tables, copied facts, or invented values.
- `recent` and `suggested` may only reorder from real explainable facts; otherwise return unavailable.
- Authorization and scope fail closed; keyword and paging run in the owning service before totals are calculated.
- Generated OpenAPI/client files are refreshed only by governed scripts.

---

### Task 1: Inventory authoritative directories

**Files:**

- Create: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Application/Queries/ListInventoryDirectoryQuery.cs`
- Create: `backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/InventoryDirectoryQueryTests.cs`
- Create: `backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/InventoryDirectoryPostgresTests.cs`
- Modify: `backend/services/Business/Inventory/src/Nerv.IIP.Business.Inventory.Web/Endpoints/Inventory/InventoryEndpoints.cs`
- Modify: Inventory entity configuration and migration files only if PostgreSQL evidence shows a missing supporting index.

**Interfaces:**

- Produces: `GET /api/inventory/v1/directory` with `directoryType=location|batch|serial`, organization/environment, optional site/SKU, keyword, skip/take; returns typed `{items,total,skip,take,sourceKind,asOfUtc}`.

- [x] Write unit and PostgreSQL tests that fail because the query/endpoint does not exist.
- [x] Verify RED using the Inventory Web test project.
- [x] Implement distinct stable identities, deterministic ordering, owner-side filters and pagination.
- [x] Verify GREEN and inspect PostgreSQL query plans/index use.

### Task 2: Maintenance reason search

**Files:**

- Modify: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Application/Queries/MaintenanceQueries.cs`
- Modify: `backend/services/Business/Maintenance/src/Nerv.IIP.Business.Maintenance.Web/Endpoints/Maintenance/MaintenanceEndpoints.cs`
- Test: a new focused Maintenance reason search test file.

**Interfaces:**

- Produces: existing `GET /api/business/v1/maintenance/downtime-reasons` plus optional `keyword`, applied before total and paging.

- [x] Write scope/keyword/paging tests and verify RED.
- [x] Add the request field and EF filter, then verify GREEN.

### Task 3: Unified BusinessGateway contract

**Files:**

- Create: focused directory models, service, endpoint, and tests under `backend/gateway/BusinessGateway`.
- Modify: `BusinessServiceClients.cs` interfaces/clients and `Program.cs` registrations only as required.

**Interfaces:**

- Produces: `GET /api/business-console/v1/directories/{directoryType}` and operation ID `listBusinessConsoleSearchableDirectory`.

- [x] Write endpoint and service tests for authority mapping, dynamic permission, scope suppression, stable item IDs/displays, configurable priority unavailability, deterministic explainable ordering, and malformed/`success:false` wire failures.
- [x] Verify RED.
- [x] Implement the minimum dispatcher, downstream calls, typed mapping, ranking/status metadata and validators.
- [x] Verify GREEN plus BusinessGateway authorization/OpenAPI suites.

### Task 4: Governance, generation, and handoff

**Files:**

- Modify: `docs/architecture/facade-coverage-matrix.json`, rendered summary/narrative, `api-contract-and-codegen.md`, and `implementation-readiness.md`.
- Generate: BusinessGateway OpenAPI and `frontend/packages/api-client/src/generated/business-console/**`.
- Modify: stable api-client barrel only if the generated operation is not already exported by wildcard.

- [x] Register Inventory new GET as exposed; append the unified Gateway operation to reused MasterData/Quality/Maintenance rows and flip Maintenance GET from deferred to exposed.
- [x] Export OpenAPI through `scripts/export-gateway-openapi.ps1` and run `pnpm -C frontend generate:api`.
- [x] Run service/Gateway/full backend gates, facade coverage, contract/migration/schema checks, OpenAPI drift, frontend typecheck/test/build and per-file formatting.
- [x] Run real PostgreSQL verification with relevant environment variables explicitly cleared; remove the exact task containers/volumes and prove zero remain.
- [x] Re-fetch and report open-PR overlaps, especially #1137, #1139, and #1150.
- [x] Commit, push, create a ready PR containing `Closes #1169`, docs impact, per-endpoint facade declarations, and latest-main regeneration risk; link the PR to Linear MAN-632 and stop without merging.
