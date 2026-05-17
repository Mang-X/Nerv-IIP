# Release-Grade Persistence Foundation Design

## Context

The fourth vertical slice proved that AppHub and Ops can run on PostgreSQL, Redis and RabbitMQ through the real-infrastructure gate. The remaining weakness is that the PostgreSQL path still depends on `EnsureCreated()` for local verification and service startup. That is acceptable for a slice, but it is the wrong foundation for IAM, File Storage, Ops approval, Notification, package installs and customer-controlled deployments.

The next phase therefore hardens persistence before adding more user-facing capability. Frontend work is intentionally deferred: most backend SDK and migration verification does not need console changes, and the visual design system has not been selected. This phase may regenerate API clients and run frontend quality gates only when backend contracts change; it must not introduce new console pages, component skins or layout decisions.

## Recommended Approach

Use a release-grade persistence vertical slice centered on AppHub and Ops, the two services that already own real PostgreSQL models. Add EF Core migrations, explicit migration execution helpers, startup guardrails and verification scripts that prove the database can be created from migrations rather than `EnsureCreated()`. Keep the scope narrow enough to finish with confidence.

Alternatives considered:

1. Start IAM first. This would unlock authentication sooner, but it would build critical security state on top of an unproven migration path.
2. Start File Storage first. This would make the platform feel more complete, but file metadata, object state and authorization all depend on durable schema evolution.
3. Harden persistence first. This creates less visible product surface, but it reduces future rework across IAM, File Storage, Ops, Notification and deployment.

The third option is the selected design.

## Scope

In scope:

1. Add initial EF Core migrations for AppHub and Ops PostgreSQL models.
2. Add a small service-owned migration entrypoint for each service, callable from tests and scripts.
3. Replace PostgreSQL test setup that uses `EnsureCreated()` with migration-based setup.
4. Remove PostgreSQL service startup `EnsureCreated()` and replace it with an opt-in migration switch suitable for local/dev scripts.
5. Add a fifth-stage verification script that starts local infrastructure, resets verification databases, applies migrations and reruns backend contract/SDK tests.
6. Add a local `dotnet-ef` tool manifest if required to make migration generation repeatable. Current .NET 10 tooling creates `dotnet-tools.json` at the repository root.
7. Update documentation to make frontend deferral and Design System planning explicit.

Out of scope:

1. IAM schema migration.
2. File Storage metadata/object provider implementation.
3. CAP business outbox and consumer idempotency.
4. Production installers, Windows Service, systemd or packaging.
5. Frontend feature work or visual component redesign.
6. Selecting shadcn-vue, UnoCSS, token naming, themes or interaction design.

## Architecture

Each service keeps ownership of its database schema and migrations inside its Infrastructure project:

```text
backend/services/AppHub/src/Nerv.IIP.AppHub.Infrastructure/
  Migrations/
  AppHubDatabaseMigrationRunner.cs

backend/services/Ops/src/Nerv.IIP.Ops.Infrastructure/
  Migrations/
  OpsDatabaseMigrationRunner.cs
```

The migration runner is intentionally small: it receives the service `ApplicationDbContext`, calls `Database.MigrateAsync`, and exposes a single async method. Web startup wires it through dependency injection only when PostgreSQL is selected. The web host must not silently mutate production databases by default. Local scripts may opt in through `Persistence:AutoMigrate=true`.

Tests use migrations to create schema in empty databases. This proves that a clean database can be brought to the current model without relying on EF's `EnsureCreated()` shortcut.

## Data Flow

For PostgreSQL verification:

1. The script starts PostgreSQL, Redis and RabbitMQ through `infra/docker-compose.dev.yml`.
2. The script drops and recreates AppHub/Ops verification databases.
3. Tests or migration commands apply AppHub and Ops migrations.
4. AppHub commands record registration, heartbeat and state facts.
5. Ops endpoints create, dispatch and complete an operation task.
6. SDK and contract tests run without frontend UI participation.
7. The fourth-stage real-infrastructure gate remains available as a broader regression check.

## Error Handling

Migration failure must be loud. Scripts and tests fail immediately when:

1. `dotnet-ef` is unavailable or migration generation cannot be restored.
2. `Database.MigrateAsync` fails.
3. PostgreSQL connection strings are missing in PostgreSQL mode.
4. A service tries to use `EnsureCreated()` in production-like verification paths.

Migration generation must use the PostgreSQL profile explicitly. The current Web startup defaults to `Persistence:Provider=InMemory`, so `dotnet tool run dotnet-ef ...` commands need `Persistence__Provider=PostgreSQL` plus the service connection string unless a later design-time factory is introduced.

Runtime service startup only auto-migrates when `Persistence:AutoMigrate` is true. Without that flag, startup should register the database profile but leave schema changes to deploy/install scripts.

## Testing

The first implementation pass must be test-first:

1. Add tests that assert AppHub and Ops PostgreSQL setup can migrate an empty database and persist current facts.
2. Watch those tests fail while migration support is missing or still uses `EnsureCreated()`.
3. Add migration runners and migrations.
4. Rerun targeted PostgreSQL tests.
5. Run backend solution tests, connector-host tests and SDK/contract tests.
6. Only run frontend generation/build gates if Gateway OpenAPI or generated client inputs change. No frontend page/component work is part of this phase.

## Frontend Deferral

The console remains valuable as a verification surface, but it should not become the pacing item for this phase. Until the Design System is deliberately planned:

1. Do not create new console pages for migration status.
2. Do not add new UI primitives or restyle `packages/ui`.
3. Do not introduce a component library or token system.
4. Keep generated API client work mechanical and traceable to backend OpenAPI.
5. Treat frontend design-system planning as its own future spec before implementation.

## Completion Definition

The phase is ready to close when:

1. AppHub and Ops have committed initial migrations.
2. PostgreSQL tests create schema through migrations, not `EnsureCreated()`.
3. Web startup no longer calls `EnsureCreated()` in PostgreSQL mode.
4. Local/dev auto-migration is explicit and documented.
5. A fifth-stage verification script exits `0`.
6. Backend, connector-host and contract/SDK tests pass.
7. Documentation records that frontend feature work remains deferred pending Design System planning.
