# Order Urgency Snapshot Retention And Archive Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver fail-safe, tenant-isolated retention, versioned archival, authorized cleanup, restore, observability, migration guidance, and representative capacity evidence for Scheduling urgency snapshots.

**Architecture:** BusinessScheduling owns policy and lifecycle state; FileStorage owns the MinIO/S3-compatible versioned-object adapter. Database leases and stable archive batches provide multi-instance safety and resumable idempotency, while exact-version read-back evidence gates every source deletion.

**Tech Stack:** .NET 10, EF Core 10/PostgreSQL, FastEndpoints, Minio .NET SDK 7, Prometheus, xUnit, PowerShell verification scripts.

---

### Task 1: Freeze contracts and fail-safe policy validation

**Files:**
- Create: `backend/common/Contracts/Nerv.IIP.Contracts.FileStorage/VersionedArchiveContracts.cs`
- Create: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Urgency/OrderUrgencyRetentionOptions.cs`
- Test: `backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/OrderUrgencyRetentionPolicyTests.cs`

- [ ] Write tests proving disabled/missing scopes, non-positive durations, total retention not exceeding online retention, invalid batch sizes, active holds, and absent/expired deletion authorizations all reject deletion.
- [ ] Run the focused tests and confirm they fail because the policy types do not exist.
- [ ] Add the minimal options, per-scope policy, legal-hold, and explicit authorization types; keep default processing disabled and default hold inactive.
- [ ] Re-run the focused tests and confirm they pass.

### Task 2: Add the FileStorage versioned-object boundary

**Files:**
- Modify: `backend/Directory.Packages.props`
- Modify: `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Nerv.IIP.FileStorage.Web.csproj`
- Create: `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Application/Archives/VersionedArchiveStore.cs`
- Create: `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Endpoints/Archives/VersionedArchiveEndpoints.cs`
- Modify: `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Program.cs`
- Test: `backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/VersionedArchiveStoreTests.cs`

- [ ] Write endpoint/service tests for versioning-required put, exact-version read-back verification, SHA/size mismatch rejection, exact-version get, legal hold application, and exact-version delete.
- [ ] Run them red.
- [ ] Add Minio 7.0.0 and implement `IVersionedArchiveStore`; require a configured bucket and enabled versioning, and never auto-downgrade to local files.
- [ ] Add internal-service-authorized put/get/delete endpoints and DI wiring.
- [ ] Run focused FileStorage tests green.

### Task 3: Add durable Scheduling lifecycle state

**Files:**
- Modify: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Domain/AggregatesModel/OrderUrgencyAggregate/OrderUrgencyBusinessPriority.cs`
- Modify: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/ApplicationDbContext.cs`
- Modify: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/EntityConfigurations/OrderUrgencyEntityTypeConfigurations.cs`
- Test: `backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/OrderUrgencyRetentionPersistenceTests.cs`

- [ ] Write schema/model tests for archive batch uniqueness, scope lease uniqueness, immutable object evidence, authorization audit fields, restore audit, and required comments/indexes.
- [ ] Run them red.
- [ ] Add archive batch, lease, and restore audit entities/configurations with organization/environment scope on every row.
- [ ] Run focused persistence/schema tests green.

### Task 4: Implement archive, authorized cleanup, concurrency, and restore

**Files:**
- Create: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Urgency/OrderUrgencyArchiveClient.cs`
- Create: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Urgency/OrderUrgencyRetentionService.cs`
- Create: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Application/Urgency/OrderUrgencyRetentionWorker.cs`
- Create: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Endpoints/Scheduling/OrderUrgencyArchiveEndpoints.cs`
- Modify: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Program.cs`
- Modify: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/appsettings.json`
- Test: `backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/OrderUrgencyRetentionServiceTests.cs`

- [ ] Write tests for exact 180-day boundary, latest-snapshot protection, scope isolation, deterministic replay, concurrent lease exclusion, archive failure, incomplete evidence, partial failure resume, legal hold, expired authorization, 3-year archive expiry, and idempotent restore.
- [ ] Run each behavior red before implementation.
- [ ] Implement deterministic envelopes, candidate selection, leases, FileStorage client, evidence persistence, two-phase source deletion, archive expiry, metrics, worker scheduling, and internal restore endpoint.
- [ ] Run focused tests green after each behavior.

### Task 5: Generate migration and operational evidence

**Files:**
- Create: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/Migrations/<timestamp>_AddOrderUrgencyRetentionArchive.cs`
- Modify: `backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs`
- Create: `scripts/verify-business-scheduling-urgency-retention.ps1`
- Create: `scripts/tests/business-scheduling-urgency-retention-verify-script.Tests.ps1`
- Modify: `docs/architecture/database-schema-catalog.md`
- Create: `docs/architecture/business-scheduling-order-urgency-retention.md`
- Modify: `docs/architecture/implementation-readiness.md`
- Modify: `infra/aspire/Nerv.IIP.AppHost/Program.cs`

- [ ] Generate the EF migration with the explicit PostgreSQL profile and inspect it for comments, indexes, and service-schema history.
- [ ] Add a governed representative-capacity verification using the shared script automation helpers; capture 10k+ rows, two scopes, concurrent attempts, latest-row preservation, retry, and timings.
- [ ] Document policy/configuration, alerts, migration, rollback, legal hold, deletion authorization, exact-version restore, and the 24-hour objective.
- [ ] Wire FileStorage reference/configuration without enabling retention by default.

### Task 6: Verify and publish

- [ ] Run focused Scheduling and FileStorage tests.
- [ ] Run `dotnet test backend/Nerv.IIP.sln`.
- [ ] Run script governance and the representative capacity verifier when Docker is available; report an unavailable daemon as an environment limitation.
- [ ] Build the AppHost, run `git diff --check`, and inspect endpoint/facade coverage impact.
- [ ] Commit scoped changes, push the `codex/issue-1068-order-urgency-retention` branch, and create a ready PR containing `Fixes #1068` and `Linear: MAN-594`.
- [ ] Stop without merging.
