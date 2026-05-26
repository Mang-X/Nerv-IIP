# P2 Event Reliability Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make #170/#171 event reliability production-grade enough for P2 by persisting DLQ facts for Notification, AppHub and MES and adding an opt-in cross-service CAP hardening gate.

**Architecture:** Reuse the existing Maintenance persistent DLQ shape as the table contract, but keep the base `Nerv.IIP.Messaging.CAP` package limited to contracts, guard logic and in-memory DLQ. The reusable EF store lives in `Nerv.IIP.Messaging.CAP.EntityFrameworkCore` so PostgreSQL-backed services can opt in without copy/paste while non-persistent consumers avoid EF Core transitive dependencies. Each service owns its own `integration_event_dead_letters` table inside its schema. CAP `received` remains broker-level inbox; service-owned processed-event tables remain business inbox and will be extended incrementally.

**Implementation status (2026-05-26):** This first PR completes the persistent DLQ slice for the shared CAP store, Notification, AppHub and MES. The opt-in cross-service multi-process CAP gate remains the next event-reliability PR so it can carry its own Docker/PostgreSQL/RabbitMQ setup and teardown review.

**Tech Stack:** .NET 10, EF Core 10, PostgreSQL, CAP, RabbitMQ profile, xUnit, governed PowerShell scripts.

---

### Task 1: Reusable Persistent DLQ Store

**Files:**
- Modify: `backend/common/Messaging/Nerv.IIP.Messaging.CAP/Nerv.IIP.Messaging.CAP.csproj`
- Modify: `backend/common/Messaging/Nerv.IIP.Messaging.CAP/IntegrationEventReliability.cs`
- Create: `backend/common/Messaging/Nerv.IIP.Messaging.CAP.EntityFrameworkCore/**`
- Test: `backend/tests/Nerv.IIP.Messaging.CAP.Tests/IntegrationEventReliabilityTests.cs`

- [ ] **Step 1: Add a failing EF-backed DLQ store test**

Add a test that creates a relational EF test `DbContext` (SQLite in-memory is sufficient for CI), configures the shared dead-letter entity, verifies the relational mapping metadata such as table name, `event_json` column type and indexes, writes a rejected message through the persistent store, lists it, marks it replayed, and verifies the status change.

Run:

```powershell
dotnet test backend/tests/Nerv.IIP.Messaging.CAP.Tests/Nerv.IIP.Messaging.CAP.Tests.csproj --no-restore --filter FullyQualifiedName~Persistent_dead_letter_store
```

Expected before implementation: compile failure because `PersistentIntegrationEventDeadLetterStore<TDbContext>` does not exist.

- [ ] **Step 2: Add EF extension package**

Create `Nerv.IIP.Messaging.CAP.EntityFrameworkCore` and add `Microsoft.EntityFrameworkCore` and `Microsoft.EntityFrameworkCore.Relational` there. Do not add EF Core package references to the base `Nerv.IIP.Messaging.CAP.csproj`.

- [ ] **Step 3: Implement shared persistent DLQ entity and store**

In the EF extension package, add:

```csharp
public sealed class IntegrationEventDeadLetter
{
    private IntegrationEventDeadLetter() { }

    public IntegrationEventDeadLetter(IntegrationEventDeadLetterMessage message) { ... }

    public Guid Id { get; private set; }
    public string ConsumerName { get; private set; } = string.Empty;
    public string? EventId { get; private set; }
    public string? EventType { get; private set; }
    public int? EventVersion { get; private set; }
    public string? SourceService { get; private set; }
    public string? IdempotencyKey { get; private set; }
    public string EventClrType { get; private set; } = string.Empty;
    public string EventJson { get; private set; } = string.Empty;
    public string FailureCode { get; private set; } = string.Empty;
    public string FailureMessage { get; private set; } = string.Empty;
    public IntegrationEventDeadLetterStatus Status { get; private set; }
    public DateTimeOffset DeadLetteredAtUtc { get; private set; }
    public DateTimeOffset? ReplayedAtUtc { get; private set; }
}
```

Add `PersistentIntegrationEventDeadLetterStore<TDbContext>` with `AddAsync`, `ListAsync` and `MarkReplayedAsync`, using `dbContext.Set<IntegrationEventDeadLetter>()`. Keep only `IIntegrationEventDeadLetterStore`, `IntegrationEventDeadLetterMessage`, `IntegrationEventDeadLetterStatus`, `IntegrationEventConsumerGuard`, the envelope validator and the in-memory store in the base CAP package.

Add `ModelBuilder.ConfigureIntegrationEventDeadLetters()` extension that maps table `integration_event_dead_letters`, all comments, JSON column type, status string conversion, and indexes:

```csharp
builder.HasIndex(x => new { x.ConsumerName, x.Status, x.DeadLetteredAtUtc });
builder.HasIndex(x => new { x.ConsumerName, x.EventId });
```

- [ ] **Step 4: Run focused tests**

Run:

```powershell
dotnet test backend/tests/Nerv.IIP.Messaging.CAP.Tests/Nerv.IIP.Messaging.CAP.Tests.csproj --no-restore
```

Expected: all messaging CAP tests pass.

### Task 2: Notification Persistent DLQ

**Files:**
- Modify: `backend/services/Notification/src/Nerv.IIP.Notification.Infrastructure/ApplicationDbContext.cs`
- Modify: `backend/services/Notification/src/Nerv.IIP.Notification.Web/Program.cs`
- Create: `backend/services/Notification/src/Nerv.IIP.Notification.Infrastructure/Migrations/*_AddNotificationIntegrationEventDeadLetters.cs`
- Test: `backend/services/Notification/tests/Nerv.IIP.Notification.Web.Tests/OperationTaskFailedNotificationConsumerTests.cs`
- Docs: `docs/architecture/database-schema-catalog.md`

- [ ] **Step 1: Add failing Notification persistence test**

Add a test that boots Notification with PostgreSQL profile test services and asserts `IIntegrationEventDeadLetterStore` resolves to `PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>`.

Expected before implementation: the resolved store is `InMemoryIntegrationEventDeadLetterStore`.

- [ ] **Step 2: Register persistent store only for PostgreSQL profile**

In `Program.cs`, keep in-memory registration for non-PostgreSQL. In PostgreSQL branch register:

```csharp
builder.Services.AddScoped<IIntegrationEventDeadLetterStore, PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>>();
```

- [ ] **Step 3: Map the table and generate migration**

In `ApplicationDbContext.OnModelCreating`, call:

```csharp
modelBuilder.ConfigureIntegrationEventDeadLetters();
```

Generate migration:

```powershell
$env:Persistence__Provider = "PostgreSQL"
dotnet tool restore
dotnet tool run dotnet-ef migrations add AddNotificationIntegrationEventDeadLetters `
  --project backend/services/Notification/src/Nerv.IIP.Notification.Infrastructure `
  --startup-project backend/services/Notification/src/Nerv.IIP.Notification.Web
```

- [ ] **Step 4: Run focused Notification tests**

Run:

```powershell
dotnet test backend/services/Notification/tests/Nerv.IIP.Notification.Web.Tests/Nerv.IIP.Notification.Web.Tests.csproj --no-restore
```

Expected: Notification tests pass.

### Task 3: AppHub Persistent DLQ

**Files:**
- Modify: `backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/ApplicationDbContext.cs`
- Modify: `backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Program.cs`
- Create: `backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/Migrations/*_AddAppHubIntegrationEventDeadLetters.cs`
- Test: `backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/AppHubIntegrationEventTests.cs`
- Docs: `docs/architecture/database-schema-catalog.md`

- [ ] **Step 1: Add failing AppHub persistence test**

Add a test that boots AppHub with PostgreSQL profile test services and asserts `IIntegrationEventDeadLetterStore` resolves to `PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>`.
Because this test only verifies DI registration, the test factory must replace the EF Core database provider with an in-memory provider after service registration. It must not depend on a reachable PostgreSQL instance.

- [ ] **Step 2: Register persistent store for PostgreSQL profile**

Use scoped persistent store when `usePostgreSql` is true; keep singleton in-memory store otherwise.

- [ ] **Step 3: Map table and generate migration**

Call `modelBuilder.ConfigureIntegrationEventDeadLetters()` from AppHub `ApplicationDbContext`, then generate `AddAppHubIntegrationEventDeadLetters`.

- [ ] **Step 4: Run focused AppHub tests**

Run:

```powershell
dotnet test backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/Nerv.IIP.AppHub.Web.Tests.csproj --no-restore
```

Expected: AppHub tests pass.

### Task 4: MES Persistent DLQ

**Files:**
- Modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/ApplicationDbContext.cs`
- Modify: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Web/Program.cs`
- Create: `backend/services/Business/Mes/src/Nerv.IIP.Business.Mes.Infrastructure/Migrations/*_AddMesIntegrationEventDeadLetters.cs`
- Test: `backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/MaintenanceEventHandlerTests.cs`
- Docs: `docs/architecture/database-schema-catalog.md`

- [ ] **Step 1: Add failing MES persistence test**

Add a test asserting MES PostgreSQL service registration resolves `IIntegrationEventDeadLetterStore` to `PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>`.

- [ ] **Step 2: Replace singleton in-memory registration**

MES is PostgreSQL-backed in current runtime, so register:

```csharp
builder.Services.AddScoped<IIntegrationEventDeadLetterStore, PersistentIntegrationEventDeadLetterStore<ApplicationDbContext>>();
```

- [ ] **Step 3: Map table and generate migration**

Call `modelBuilder.ConfigureIntegrationEventDeadLetters()` from MES `ApplicationDbContext`, then generate `AddMesIntegrationEventDeadLetters`.

- [ ] **Step 4: Run focused MES tests**

Run:

```powershell
dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj --no-restore
```

Expected: MES tests pass.

### Task 5: Cross-Service CAP Hardening Gate

**Files:**
- Create or modify: `backend/tests/Nerv.IIP.Infra.Cap.Tests/**`
- Create: `scripts/verify-infra-cross-service-cap.ps1`
- Docs: `docs/architecture/implementation-readiness.md`
- Docs: `docs/architecture/script-automation-governance.md`

- [ ] **Step 1: Add opt-in test category**

Create tests tagged `Category=cap-cross-service` that require `NERV_IIP_TEST_POSTGRES`. RabbitMQ is required only for `Profile=rabbitmq`.

- [ ] **Step 2: Cover Ops to Notification/AppHub**

Publish `OperationTaskFailedIntegrationEvent` through Ops/Notification CAP contract and verify Notification creates an intent and AppHub refresh handler records or safely ignores the event without DLQ when version is supported.

- [ ] **Step 3: Add governed script**

Create `scripts/verify-infra-cross-service-cap.ps1` with Script-Governance header, scoped environment variables, `Invoke-DotNet`, explicit PostgreSQL requirement, optional RabbitMQ requirement, and log output under `artifacts/script-logs/**`.

- [ ] **Step 4: Run script governance and focused gate**

Run:

```powershell
scripts/check-script-governance.ps1
dotnet test backend/tests/Nerv.IIP.Messaging.CAP.Tests/Nerv.IIP.Messaging.CAP.Tests.csproj --no-restore
```

If PostgreSQL is available, also run:

```powershell
pwsh scripts/verify-infra-cross-service-cap.ps1 -PostgresConnectionString $env:NERV_IIP_TEST_POSTGRES -Profile inmemory
```

### Task 6: Documentation and Readiness

**Files:**
- Modify: `docs/architecture/implementation-readiness.md`
- Modify: `docs/architecture/database-schema-catalog.md`
- Modify: `docs/architecture/project-status-dashboard.html`

- [ ] **Step 1: Update readiness**

Revise #170/#171 lines to say Notification/AppHub/MES have persistent DLQ under PostgreSQL profile, and cross-service CAP remains opt-in hardening.

- [ ] **Step 2: Update schema catalog**

Add `integration_event_dead_letters` entries for notification, apphub and mes schemas with indexes and ownership.

- [ ] **Step 3: Final verification**

Run:

```powershell
dotnet test backend/tests/Nerv.IIP.Messaging.CAP.Tests/Nerv.IIP.Messaging.CAP.Tests.csproj --no-restore
dotnet test backend/services/Notification/tests/Nerv.IIP.Notification.Web.Tests/Nerv.IIP.Notification.Web.Tests.csproj --no-restore
dotnet test backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/Nerv.IIP.AppHub.Web.Tests.csproj --no-restore
dotnet test backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj --no-restore
scripts/check-script-governance.ps1
git diff --check
```

Expected: all commands pass, except opt-in PostgreSQL/RabbitMQ hardening script is reported as skipped when required infrastructure is unavailable.
