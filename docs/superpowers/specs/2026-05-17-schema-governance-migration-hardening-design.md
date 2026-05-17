# Schema Governance & Migration Hardening Design

## Context

The fifth stage moved AppHub and Ops from PostgreSQL `EnsureCreated()` shortcuts to EF Core migrations and explicit migration runners. That was the right foundation, but the repository still has several known schema governance gaps recorded in `docs/architecture/database-schema-conventions.md` and `docs/architecture/database-schema-catalog.md`.

Those gaps should be closed before IAM, FileStorage, Notification, high-risk Ops workflows or new persistent services start adding more tables. Otherwise every future service will need a separate cleanup pass for table comments, JSON compatibility notes, migrations history placement, catalog alignment and convention tests.

The sixth stage therefore turns the schema rules from documentation reminders into enforceable code and focused tests. It also cleans up small planning handoff drift from the fifth-stage merge so future agents read the project state correctly.

## Recommended Approach

Use a narrow schema governance hardening slice centered on AppHub and Ops, the two services that already have real migrations. Add the missing EF metadata, lock migrations history into each service schema, create reusable schema convention assertions and update the architecture docs to match the enforced rules.

Alternatives considered:

1. Start IAM or FileStorage first and add schema rules while building them. This would create visible platform capability sooner, but it risks scattering governance fixes across new service work.
2. Build customer-release migration bundles and installation scripts first. This is valuable for a release-readiness phase, but the schema rules those scripts would package are not fully enforced yet.
3. Harden schema governance first. This is less product-visible, but it prevents repeated patching when IAM, FileStorage, Notification and Ops approval introduce long-lived data.

The third option is the selected design.

## Scope

In scope:

1. Normalize planning handoff documentation so README, technology references and fifth-stage plan state do not mislead the next worker.
2. Add table comments to AppHub and Ops business tables.
3. Strengthen AppHub and Ops JSON/text column comments so they state format, producer, consumer and compatibility expectations.
4. Configure AppHub and Ops PostgreSQL `__EFMigrationsHistory` tables inside the service schema.
5. Add reusable schema convention test helpers under `backend/common/Testing/Nerv.IIP.Testing`.
6. Add AppHub and Ops tests that enforce business table comments, business column comments, JSON/text compatibility comments, string strongly typed ID rules and migrations history schema configuration.
7. Regenerate or adjust EF migrations/model snapshots only where metadata changes require it.
8. Update schema catalog, schema conventions, implementation readiness and related docs so the code, tests and documents agree.

Out of scope:

1. IAM implementation, login, authorization guards or seed commands.
2. FileStorage upload/download, MinIO provider or download authorization.
3. Notification, Knowledge, AI Integration or Observability index tables.
4. Customer-release migration bundle, installation package, Windows/Linux installer or backup/restore rehearsal.
5. GaussDB, DMDB or other database profile validation.
6. Frontend pages, navigation, component styling or Design System decisions.

## Architecture

The hardening work stays close to the existing service boundaries.

```text
backend/common/Testing/Nerv.IIP.Testing/
  EntityFramework/
    SchemaConventionAssertions.cs

backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/
  AppHubPersistenceServiceCollectionExtensions.cs
  EntityConfigurations/*.cs
  Migrations/*

backend/services/AppHub/tests/Nerv.IIP.AppHub.Web.Tests/
  AppHubSchemaConventionTests.cs

backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/
  OpsPersistenceServiceCollectionExtensions.cs
  EntityConfigurations/*.cs
  Migrations/*

backend/services/Ops/tests/Nerv.IIP.Ops.Web.Tests/
  OpsSchemaConventionTests.cs
```

The shared testing helper should inspect EF Core metadata, not raw migration text. Service tests create the service `DbContextOptions` with the PostgreSQL provider against a dummy connection string so provider annotations and relational metadata are available without requiring a live database.

The helper should be generic enough for future services but not overbuilt. It only needs assertions that are already required by `database-schema-conventions.md`:

1. business tables must have table comments;
2. business properties must have column comments;
3. configured JSON/text properties must have comments that mention JSON, producer, consumer and compatibility;
4. string strongly typed ID keys must use `ValueGeneratedNever()` and a max length;
5. PostgreSQL options must place `__EFMigrationsHistory` in the service schema.

System-owned CAP tables and EF migrations history are not required to have full business column comments, but they must remain represented in the catalog as system-owned infrastructure tables.

## Data And Metadata Flow

For AppHub:

1. EF entity configurations define table names, comments, indexes, conversions and JSON metadata.
2. Npgsql options define the AppHub migrations history table in the `apphub` schema.
3. EF migrations/model snapshot preserve the metadata that PostgreSQL will apply.
4. AppHub schema convention tests read `ApplicationDbContext.Model` and Npgsql relational options.
5. `database-schema-catalog.md` describes the same tables, status value sources and remaining service boundaries.

For Ops:

1. EF entity configurations define operation task, attempt and audit table comments plus JSON/text compatibility metadata.
2. Npgsql options define the Ops migrations history table in the `ops` schema.
3. EF migrations/model snapshot preserve the metadata.
4. Ops schema convention tests read `ApplicationDbContext.Model` and Npgsql relational options.
5. Catalog and readiness docs reflect the now-enforced governance baseline.

## Error Handling

The new tests should fail loudly when a future persistent service or table breaks the rules.

Failures should report:

1. service name;
2. entity/table name;
3. property/column name where relevant;
4. missing or insufficient convention;
5. expected schema for migrations history.

The tests should avoid requiring Docker or PostgreSQL. Live PostgreSQL verification remains covered by `scripts/verify-fifth-slice-persistence-foundation.ps1`; schema metadata rules should run as ordinary unit tests inside the backend solution.

## Testing

The implementation should be test-first:

1. Add failing AppHub and Ops schema convention tests that expose the current gaps.
2. Add the reusable assertion helper in `Nerv.IIP.Testing`.
3. Update AppHub/Ops test projects to reference `Nerv.IIP.Testing`.
4. Add table comments, JSON/text comments and migrations history schema configuration.
5. Regenerate migrations or model snapshots if EF detects metadata changes.
6. Run targeted AppHub/Ops tests.
7. Run `dotnet test backend/Nerv.IIP.sln`.
8. Run `pwsh scripts/verify-fifth-slice-persistence-foundation.ps1` if migrations or PostgreSQL configuration changed.
9. Run `git diff --check`.

Frontend gates are not required because this stage does not touch OpenAPI contracts or frontend files.

## Documentation

Documentation updates should be surgical:

1. `README.md` should describe the current baseline as fifth-stage complete and sixth-stage planning/implementation as schema governance hardening.
2. `docs/architecture/technology-stack-references.md` should stop saying the current baseline is only the fourth stage.
3. `docs/superpowers/plans/2026-05-17-release-grade-persistence-foundation.md` should avoid confusing future agents with unchecked tasks after a completed stage. A completion record is already present; the task list can be explicitly marked as historical or checked where completed.
4. `docs/architecture/database-schema-conventions.md` should distinguish rules now enforced by tests from rules that apply when additional persistent services are introduced.
5. `docs/architecture/database-schema-catalog.md` should remove AppHub/Ops known gaps that are closed by this stage and keep only real remaining gaps.
6. `docs/architecture/implementation-readiness.md` should identify this stage as the guardrail before IAM/FileStorage persistent tables.

No ADR is required for this stage. ADR 0009 already records the durable migration, release and seed strategy. This stage implements and enforces part of that accepted decision.

## Completion Definition

The stage is ready to close when:

1. AppHub and Ops business tables have table comments.
2. AppHub and Ops business columns have comments.
3. AppHub `Metadata` and `Capabilities` comments explain JSON format, producer, consumer and compatibility.
4. Ops `ParametersJson` and `FailureJson` comments explain JSON format, producer, consumer and compatibility.
5. AppHub and Ops configure `__EFMigrationsHistory` in `apphub` and `ops` schemas respectively.
6. AppHub and Ops schema convention tests pass without a live database.
7. Backend solution tests pass.
8. Fifth-stage persistence verification still passes if migrations or PostgreSQL provider configuration changed.
9. Documentation no longer lists closed AppHub/Ops schema governance gaps as open.
10. No frontend feature or Design System work is introduced.
