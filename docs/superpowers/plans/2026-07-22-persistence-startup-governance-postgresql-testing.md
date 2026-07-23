# Persistence Startup Governance and PostgreSQL Testing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver issue #1075 by centralizing persistence startup validation and reusable isolated PostgreSQL test-database lifecycle management.

**Architecture:** A runtime-only `Nerv.IIP.Persistence` project resolves a normalized `InMemory` or `PostgreSQL` startup decision from configuration, environment, and service-owned connection-string aliases. A separate `Nerv.IIP.Testing.PostgreSql` project owns Npgsql-backed disposable database creation and cleanup so the provider dependency does not leak into the generic testing package. Services retain their own DbContext, schema, migration runner, and remediation text.

**Tech Stack:** .NET 10, ASP.NET Core configuration/hosting abstractions, xUnit, Npgsql 10, EF Core 10, PostgreSQL.

## Global Constraints

- Do not change database schemas, business HTTP APIs, or FileStorage behavior delivered by PR #1071.
- Do not introduce provider-specific APIs into Domain or Application projects.
- Do not let `connector-hosts/` reference backend persistence or testing implementations.
- Non-Development environments require PostgreSQL and reject Web-host AutoMigrate.
- Diagnostics include service, environment, normalized provider state, connection presence, and remediation without connection-string values or credentials.

---

### Task 1: Shared persistence startup contract

**Files:**
- Create: `backend/common/Persistence/Nerv.IIP.Persistence/Nerv.IIP.Persistence.csproj`
- Create: `backend/common/Persistence/Nerv.IIP.Persistence/PersistenceStartupGovernance.cs`
- Create: `backend/tests/Nerv.IIP.Persistence.Tests/Nerv.IIP.Persistence.Tests.csproj`
- Create: `backend/tests/Nerv.IIP.Persistence.Tests/PersistenceStartupGovernanceTests.cs`
- Modify: `backend/Nerv.IIP.sln`

**Interfaces:**
- Produces: `PersistenceStartupRequirements`, `PersistenceStartupDecision`, and `PersistenceStartupGovernance.Resolve(IConfiguration, IHostEnvironment, PersistenceStartupRequirements)`.
- Produces: `PersistenceStartupDecision.UsePostgreSql` and `PersistenceStartupDecision.AutoMigrate` for service startup wiring.

- [ ] **Step 1: Write failing contract tests**

Cover trimmed/case-insensitive providers, explicit Development InMemory, PostgreSQL connection aliases, missing/unknown providers, non-Development InMemory, and AutoMigrate outside Development. Every failing diagnostic must reject a sentinel password.

- [ ] **Step 2: Run tests to verify RED**

Run: `dotnet test backend/tests/Nerv.IIP.Persistence.Tests/Nerv.IIP.Persistence.Tests.csproj`

Expected: compilation fails because the shared governance types do not exist.

- [ ] **Step 3: Implement the minimal resolver**

Use this decision shape:

```csharp
public sealed record PersistenceStartupDecision(bool UsePostgreSql, bool AutoMigrate);
```

Normalize `Persistence:Provider` with `Trim()`, accept only `InMemory` and `PostgreSQL`, require at least one configured service-owned connection-string alias for PostgreSQL, and build status-only error text.

- [ ] **Step 4: Run tests to verify GREEN**

Run: `dotnet test backend/tests/Nerv.IIP.Persistence.Tests/Nerv.IIP.Persistence.Tests.csproj`

Expected: all governance tests pass with zero warnings.

### Task 2: Migrate representative services

**Files:**
- Modify: `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Program.cs`
- Delete: `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/FileStoragePersistenceStartup.cs`
- Modify: `backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Program.cs`
- Modify: `backend/services/AppHub/src/Nerv.IIP.AppHub.Web/appsettings.Development.json`
- Modify: `backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/AppHubPersistenceServiceCollectionExtensions.cs`
- Modify: `backend/services/Ops/src/Nerv.IIP.Ops.Web/Program.cs`
- Modify: `backend/services/Ops/src/Nerv.IIP.Ops.Web/appsettings.Development.json`
- Modify: `backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/OpsPersistenceServiceCollectionExtensions.cs`
- Modify: `backend/services/Notification/src/Nerv.IIP.Notification.Web/Program.cs`
- Modify: `backend/services/Notification/src/Nerv.IIP.Notification.Web/appsettings.Development.json`
- Modify: `backend/services/Notification/src/Nerv.IIP.Notification.Web/Application/NotificationPersistenceServiceCollectionExtensions.cs`
- Modify: the four Web project files to reference `Nerv.IIP.Persistence`.
- Test: FileStorage/AppHub/Ops/Notification startup test projects.

**Interfaces:**
- Consumes: the Task 1 resolver.
- Produces: one startup decision per service; infrastructure extensions receive `UsePostgreSql` instead of reparsing provider defaults.

- [ ] **Step 1: Add failing service startup tests**

Assert all four services reject Production InMemory and Production missing provider, reject Production AutoMigrate, accept case/whitespace-normalized PostgreSQL with a configured alias, and never print the connection string secret.

- [ ] **Step 2: Run representative tests to verify RED**

Run: `dotnet test backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/Nerv.IIP.FileStorage.Web.Tests.csproj --filter Startup`

Run equivalent startup/readiness filters for AppHub, Ops, and Notification.

Expected: AppHub/Ops/Notification non-Development fallback cases fail because their current startup validation is incomplete.

- [ ] **Step 3: Wire the shared resolver**

Each `Program.cs` calls `PersistenceStartupGovernance.Resolve(...)` before service registration. Add explicit `Persistence.Provider=InMemory` to each Development settings file that previously relied on an implicit default. Pass the decision into persistence registration and migration conditions.

- [ ] **Step 4: Run representative tests to verify GREEN**

Run the four service test projects. Expected: all pass, with live PostgreSQL facts skipped only when `NERV_IIP_TEST_POSTGRES` is absent.

### Task 3: Shared PostgreSQL testing package

**Files:**
- Create: `backend/common/Testing/Nerv.IIP.Testing.PostgreSql/Nerv.IIP.Testing.PostgreSql.csproj`
- Create: `backend/common/Testing/Nerv.IIP.Testing.PostgreSql/PostgreSqlTestDatabase.cs`
- Create: `backend/tests/Nerv.IIP.Testing.PostgreSql.Tests/Nerv.IIP.Testing.PostgreSql.Tests.csproj`
- Create: `backend/tests/Nerv.IIP.Testing.PostgreSql.Tests/PostgreSqlTestDatabaseTests.cs`
- Modify: `backend/Nerv.IIP.sln`

**Interfaces:**
- Produces: `PostgreSqlTestDatabase.CreateAsync(string, string, Func<string, CancellationToken, Task>?, CancellationToken)`.
- Produces: unique `DatabaseName`, test `ConnectionString`, cancellable `DropAsync`, and `IAsyncDisposable` cleanup.

- [ ] **Step 1: Write failing lifecycle and diagnostic tests**

Unit-test prefix normalization, PostgreSQL identifier length, unique names under parallel generation, and password redaction. Add opt-in real PostgreSQL tests for parallel database isolation, initializer failure cleanup, migration callback execution, and cancellation.

- [ ] **Step 2: Run tests to verify RED**

Run: `dotnet test backend/tests/Nerv.IIP.Testing.PostgreSql.Tests/Nerv.IIP.Testing.PostgreSql.Tests.csproj`

Expected: compilation fails because `PostgreSqlTestDatabase` does not exist.

- [ ] **Step 3: Implement creation and reliable cleanup**

Generate a lowercase identifier from a bounded prefix plus `Guid.CreateVersion7()`. Create through the `postgres` admin database, run the optional initializer, and force-drop on initializer failure or disposal. Wrap failures with sanitized host/port/database/operation metadata and no raw connection string.

- [ ] **Step 4: Run tests to verify GREEN**

Run the package tests. Expected: deterministic tests pass; real PostgreSQL tests pass when configured and otherwise report explicit skips.

### Task 4: Adopt the shared test database in two services

**Files:**
- Modify: `backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/FileStorageRestartPersistenceTests.cs`
- Modify: `backend/services/FileStorage/tests/Nerv.IIP.FileStorage.Web.Tests/Nerv.IIP.FileStorage.Web.Tests.csproj`
- Delete: `backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/SchedulingTemporaryDatabase.cs`
- Modify: Scheduling PostgreSQL profile test call sites and test project reference.

**Interfaces:**
- Consumes: `PostgreSqlTestDatabase` from Task 3.

- [ ] **Step 1: Replace FileStorage local lifecycle helper**

Use `PostgreSqlTestDatabase.CreateAsync(baseConnectionString, "nerv_filestorage_restart", cancellationToken: cancellationToken)` and retain service-owned Web-host migration behavior.

- [ ] **Step 2: Replace Scheduling local lifecycle helper**

Replace each `SchedulingTemporaryDatabase.CreateAsync(...)` call with the shared helper using prefix `nerv_scheduling_test`; delete the local class.

- [ ] **Step 3: Run both service suites**

Run the FileStorage and BusinessScheduling test projects. Expected: all default tests pass and PostgreSQL-gated tests retain their existing skip behavior when the environment variable is absent.

### Task 5: Documentation and final verification

**Files:**
- Modify: `docs/architecture/implementation-readiness.md`
- Create: `docs/architecture/persistence-startup-governance.md`

**Interfaces:**
- Documents: service matrix, runtime API, test package rationale, usage example, cancellation/parallelism/cleanup behavior, and credential-redaction rule.

- [ ] **Step 1: Document the delivered matrix and package boundary**

Record FileStorage/AppHub/Ops/Notification connection aliases, Development/Production provider rules, AutoMigrate behavior, and the separate Npgsql testing package decision.

- [ ] **Step 2: Run targeted and full verification**

Run: `dotnet test backend/Nerv.IIP.sln`

Expected: exit 0, no new warnings. The pre-existing `SQLitePCLRaw.lib.e_sqlite3` restore advisory may appear and must be reported as baseline, not introduced by this change.

- [ ] **Step 3: Review diff and commit**

Run: `git diff --check`, `git status --short`, and `git diff --stat`.

Commit message: `feat(persistence): unify startup governance and postgres tests`

- [ ] **Step 4: Push and open a PR**

Push `codex/issue-1075-persistence-governance` and open a non-draft PR that links `Closes #1075`, states `文档：有影响`, confirms no business endpoint/facade change, lists verification, and does not merge.
