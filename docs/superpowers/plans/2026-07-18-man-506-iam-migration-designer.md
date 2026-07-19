# MAN-506 IAM Historical Migration Designer Repair Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restore EF Core recognition of IAM migration `20260519032000_AddUserCaseInsensitiveUniqueIndexes`, prove its intended PostgreSQL indexes on a fresh database, and prevent any compiled repository migration from silently losing its Designer metadata again.

**Architecture:** Restore the missing partial Designer from the exact IAM model snapshot committed at the historical migration point, so its target model contains the case-insensitive-index migration's contemporaneous schema but none of the following `AddRoleNormalizedName` delta. Add a repository-wide compiled-assembly governance test that inspects EF migration attributes and the declared `BuildTargetModel` override without traversing source paths, plus IAM-specific EF discovery and real-PostgreSQL behavior tests.

**Tech Stack:** .NET 10, EF Core 10, xUnit, Npgsql/PostgreSQL, PowerShell, GitHub CLI.

---

### Task 1: Lock the regression with compiled EF metadata tests

**Files:**
- Create: `backend/tests/Nerv.IIP.MigrationGovernance.Tests/Nerv.IIP.MigrationGovernance.Tests.csproj`
- Create: `backend/tests/Nerv.IIP.MigrationGovernance.Tests/MigrationDesignerGovernanceTests.cs`
- Modify: `backend/Nerv.IIP.sln`
- Modify: `backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/IamSchemaConventionTests.cs`

- [ ] **Step 1: Add an xUnit project whose `ProjectReference` glob includes every `*.Infrastructure.csproj` under `backend/services` and add it to `backend/Nerv.IIP.sln`.**

- [ ] **Step 2: Add `Every_compiled_migration_has_EF_discovery_metadata_and_target_model`.** Load `Nerv.IIP.*.Infrastructure.dll` files from the test output, find every non-abstract `Migration` subclass, and assert each type has `MigrationAttribute`, `DbContextAttribute`, a valid EF migration id, and a non-public instance `BuildTargetModel` method declared on the combined partial type.

- [ ] **Step 3: Add IAM assertion `User_case_insensitive_unique_index_migration_is_discoverable`.** Assert `Database.GetMigrations()` contains `20260519032000_AddUserCaseInsensitiveUniqueIndexes`.

- [ ] **Step 4: Run the two tests and verify RED.**

Run:
```powershell
dotnet test backend/tests/Nerv.IIP.MigrationGovernance.Tests/Nerv.IIP.MigrationGovernance.Tests.csproj --filter Every_compiled_migration_has_EF_discovery_metadata_and_target_model
dotnet test backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj --filter User_case_insensitive_unique_index_migration_is_discoverable
```

Expected: both fail specifically because `AddUserCaseInsensitiveUniqueIndexes` lacks EF Designer metadata and is absent from `GetMigrations()`.

### Task 2: Lock real PostgreSQL behavior before repairing the migration

**Files:**
- Modify: `backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/IamPostgresProfileTests.cs`

- [ ] **Step 1: Add gated test `Fresh_Postgres_has_case_insensitive_user_unique_indexes`.** With `NERV_IIP_TEST_POSTGRES`, delete the disposable database, run `MigrateAsync`, query `pg_indexes` for `IX_users_LoginName_Lower` and `IX_users_Email_Lower`, insert `Admin`, then assert `SaveChangesAsync` for `admin` throws `DbUpdateException`.

- [ ] **Step 2: Start a uniquely named disposable PostgreSQL container and run only this test to verify RED.**

Expected: the test fails because the fresh migration chain lacks both expression indexes and accepts the case-only duplicate.

### Task 3: Restore the exact historical Designer and documentation fact

**Files:**
- Create: `backend/services/Iam/src/Nerv.IIP.Iam.Infrastructure/Migrations/20260519032000_AddUserCaseInsensitiveUniqueIndexes.Designer.cs`
- Modify: `docs/architecture/database-schema-catalog.md`

- [ ] **Step 1: Create the Designer from `df1fb17:.../ApplicationDbContextModelSnapshot.cs`.** Change the snapshot class to partial migration class `AddUserCaseInsensitiveUniqueIndexes`, add `[DbContext(typeof(ApplicationDbContext))]` and `[Migration("20260519032000_AddUserCaseInsensitiveUniqueIndexes")]`, and retain the exact historical `BuildModel` body as `BuildTargetModel`.

- [ ] **Step 2: Prove the target model excludes the next migration.** Confirm it contains `RoleName` and its unique index, and contains neither `NormalizedRoleName` nor later IAM lifecycle/security-audit/data-scope deltas.

- [ ] **Step 3: Update the IAM `users` catalog row to state that login name and email case-insensitive uniqueness is enforced by the two lower-expression indexes.** No table/column/comment or new business schema delta is introduced.

- [ ] **Step 4: Re-run Task 1 tests and `dotnet ef migrations list` to verify GREEN and explicit discovery of `20260519032000`.**

### Task 4: Verify fresh PostgreSQL and affected gates

**Files:**
- No additional production files.

- [ ] **Step 1: Re-run the gated PostgreSQL test against the disposable container.** Verify both indexes exist and the `Admin`/`admin` duplicate is rejected.

- [ ] **Step 2: Run IAM tests, the migration-governance test project, schema convention tests, and `dotnet test backend/Nerv.IIP.sln`.**

- [ ] **Step 3: Inspect `git diff --check`, the branch diff against `origin/main`, and confirm no MAN-423 or endpoint/OpenAPI changes.**

### Task 5: Review, commit, push, and open the ready PR

**Files:**
- No additional source files unless review finds a MAN-506-scoped defect.

- [ ] **Step 1: Request an independent code review against `origin/main` and address all critical or important findings.**

- [ ] **Step 2: Re-run fresh verification after review, commit with MAN-506/#919 scope, and push `codex/man-506-919-iam-migration-designer`.**

- [ ] **Step 3: Create a ready PR with `gh`.** Title starts `MAN-506 #919`; body contains `Fix`, `Tests`, `Risk`, `OpenAPI or schema impact`, `产品文档影响`, and `Fixes #919`. State explicitly that this restores historical migration metadata and intended fresh-database indexes without adding a new business schema delta, and that product docs have no impact.

- [ ] **Step 4: Stop after reporting the ready PR URL; do not merge or begin another issue.**
