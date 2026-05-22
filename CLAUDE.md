# CLAUDE.md — Nerv-IIP Platform

> Agent workflow instructions. For current project status, always read
> `docs/architecture/implementation-readiness.md` first.

## Before You Start

**Always read `docs/architecture/implementation-readiness.md`** before making changes.
It records the current phase, delivered services, database schemas, and environment prerequisites.
Do NOT assume a service, schema, or port is ready based on prior knowledge — verify there.

For architectural decisions, read `docs/adr/` in order.
For service boundaries and responsibilities, read `docs/architecture/context-map.md`.

## Repo Layout

See the canonical source: `docs/architecture/repo-layout.md`

```text
Nerv-IIP/
├── AGENTS.md / CLAUDE.md          # Agent instructions
├── nerv.ps1                       # CLI dev entry point
├── docs/                          # ADR, architecture docs, specs, plans
├── backend/
│   ├── services/                  # Platform HTTP services (CleanDDD per service)
│   │   ├── Iam/
│   │   ├── AppHub/
│   │   ├── Ops/
│   │   ├── FileStorage/
│   │   ├── Notification/
│   │   └── Business/MasterData/
│   ├── gateway/PlatformGateway/   # BFF for Console
│   ├── common/                    # Narrow shared libs: Contracts, Sdk, Caching, Observability, Testing, ServiceAuth
│   └── tests/                     # Cross-service integration test hosts
├── frontend/
│   ├── apps/console/              # Vue 3 Console app
│   └── packages/                  # ui, app-shell, api-client
├── connector-hosts/               # Separate .sln — NEVER merge into backend
├── infra/                         # Aspire AppHost, Docker Compose, OTel
├── scripts/                       # Governed automation + lib/ScriptAutomation.ps1
└── .codex/                        # Codex agent config
```

## Commands

### Local dev launch
```powershell
.\nerv.ps1 dev              # Full platform via Aspire AppHost
.\nerv.ps1 dev -InfraOnly   # Infra only (PostgreSQL, Redis, RabbitMQ, MinIO, OTel)
.\nerv.ps1 ports            # Canonical port matrix
```

### Backend (.NET 10)
```powershell
dotnet build backend/Nerv.IIP.sln
dotnet test  backend/Nerv.IIP.sln

# EF migrations — set PostgreSQL profile explicitly:
$env:Persistence__Provider = "PostgreSQL"
dotnet tool restore
dotnet tool run dotnet-ef migrations add <Name> `
  --project backend/services/<Svc>/src/Nerv.IIP.<Svc>.Infrastructure `
  --startup-project backend/services/<Svc>/src/Nerv.IIP.<Svc>.Web
```

### Frontend (Node.js >=22.18.0, pnpm 11.1.2)
```powershell
pnpm -C frontend check        # typecheck + lint + fmt
pnpm -C frontend test          # vitest
pnpm -C frontend build
pnpm -C frontend generate:api  # Hey API codegen from Gateway OpenAPI snapshot
```

### Scripts (governed)
```powershell
scripts/check-script-governance.ps1   # Gate for all scripts
scripts/verify-*.ps1                  # Verification scripts (see script-automation-governance.md)
```

## Core Principles

1. Platform-before-business. Control plane is built first; industry semantics go in domain extensions after platform stabilizes.
2. Logical boundaries freeze first. Physical deployment stays flexible.
3. Frontend: explicit Vue structure only. No pseudo-Nuxt runtime.
4. Backend: organized by service boundary. No regression to monolith.
5. App integration via Connector Host pattern. Connector Host lives in `connector-hosts/`, not in backend solution.
6. AI capabilities: governance → query → low-risk actions. No model hosting in the main platform.
7. Main platform does not embed industry models (factory, line, equipment). These belong to domain extensions.
8. Platform SDK is modular and client-only. External units must not reference main platform internals.
9. File Storage and Notification are generic. Business services express intent by ID only — they don't manage storage or delivery.
10. Major version alignment between platform, apps, Connector Hosts, and extensions.
11. Docs, contracts, and catalogs are the stability foundation. Always update docs before making structural changes.
12. Aspire is the single deployment model. Multiple delivery targets (Compose, installer, package) adapt from it.
13. Automation scripts are classified engineering assets (check/verify/generate/release-install) — governed and auditable.

## "Do NOT" Constraints

1. Do NOT create unbound SharedKernel/Common/Utils mega-directories.
2. Do NOT merge Connector Host into `backend/Nerv.IIP.sln`.
3. Do NOT reference `connector-hosts/` from main platform code (and vice-versa for implementation projects).
4. Do NOT write industry domain rules into PlatformGateway, IAM, AppHub, Ops, or main console.
5. Do NOT create cross-schema foreign keys between services.
6. Do NOT use `EnsureCreated()` in non-disposable environments (PoC, shared dev, production).
7. Do NOT call `dotnet`, `docker`, `pnpm`, `pwsh` directly in scripts. Use `scripts/lib/ScriptAutomation.ps1` helpers.
8. Do NOT write Minimal API route mappings in startup files. Use FastEndpoints exclusively.
9. Do NOT hand-edit OpenAPI snapshots or generated client code.
10. Do NOT deep-import shadcn components. Always use `@nerv-iip/ui` stable export boundary.
11. Do NOT reference provider-specific APIs or write raw SQL in Domain/Application/Endpoint/SDK layers.
12. Do NOT store credentials, secrets, or customer keys in `infra/` or repo.

## Common Mistakes

These are errors that have occurred repeatedly. Read before writing any code.

1. **Endpoints default to `[AllowAnonymous]`.** Internal service APIs require
   `[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]`. Gateway Console
   endpoints use `GatewayPolicies.ConsoleAuthenticated`. Only health endpoints remain
   anonymous.

2. **Template/scaffold code not cleaned.** When scaffolding from NetCorePal or any
   template, delete all demo endpoints (LoginEndpoint, ChatHub, LockEndpoint),
   sample aggregates (OrderAggregate, DeliverRecord), and their tests before
   committing. Also verify `ServiceName` is correct and `UseAuthentication()` is
   called before `UseAuthorization()`.

3. **`Guid.NewGuid()` instead of `Guid.CreateVersion7()`.** EF's
   `UseGuidVersion7ValueGenerator()` only fires when the ID is default. If a
   constructor already assigned a v4 GUID, EF will not override it, breaking
   time-ordered index locality for child entities.

4. **Synchronous EF Core calls.** All repository, service, and query handler methods
   must be async with `CancellationToken`. Use `SaveChangesAsync`,
   `SingleOrDefaultAsync`, `AnyAsync` — never their synchronous counterparts.

5. **`Environment.SetEnvironmentVariable()` in tests without isolation.** xUnit runs
   test classes in parallel. Use `builder.UseSetting()` instead, or place the test
   in a `[CollectionDefinition("...", DisableParallelization = true)]` collection.

6. **`MigrationsHistoryTable` not configured to service schema.** Every service's
   PostgreSQL DbContext must include
   `MigrationsHistoryTable("__EFMigrationsHistory", "<service-schema>")`, otherwise
   EF puts the history table in the default `public` schema.

7. **`string.Equals` for boolean config instead of `GetValue<bool>()`.** Use
   `builder.Configuration.GetValue<bool>("Persistence:AutoMigrate")`. String
   comparison silently ignores `True`, ` true ` (with spaces), and other valid .NET
   boolean representations.

8. **Repository query filters not matching DB unique index columns.** If the unique
   index is on N columns, the deduplication/lookup query must filter on all N columns.
   Partial matches will return wrong records when different sources share the same
   partial key.

9. **Domain entities holding secrets or sensitive data.** Secret names, API keys, and
   credential references must not live in Domain aggregates. Keep them in
   Infrastructure or configuration; use reflection tests to enforce if needed.

10. **InMemory test stores generating fake tokens.** InMemory auth stores must produce
    real JWT tokens via the service's token issuer, not `Convert.ToBase64String(...)`.
    Otherwise Gateway JWT validation middleware will reject every request.

11. **Scripts calling native commands directly.** New scripts must dot-source
    `scripts/lib/ScriptAutomation.ps1` and use `Invoke-DotNet`, `Invoke-Pnpm`,
    `Invoke-DockerCompose` etc. Do NOT define functions named `Write-Error` — it
    shadows the built-in PowerShell cmdlet.

12. **Test assertions via source file path traversal.** Do NOT use
    `Path.Combine(AppContext.BaseDirectory, "..", ...)` to locate and read source
    files for assertions. Use DI, `DbContext` reflection, or
    `Nerv.IIP.Testing` schema convention helpers instead.

## "Done" Definition

Before claiming a task is complete:

1. ✅ Backend builds cleanly: `dotnet build backend/Nerv.IIP.sln` — no warnings (warnings as errors).
2. ✅ Backend tests pass: `dotnet test backend/Nerv.IIP.sln`.
3. ✅ Frontend passes: `pnpm -C frontend check` (typecheck + lint + fmt).
4. ✅ Frontend tests pass: `pnpm -C frontend test`.
5. ✅ API contract changes: Gateway OpenAPI snapshot regenerated → `pnpm -C frontend generate:api` → both committed.
6. ✅ Database schema changes: migration created. `docs/architecture/database-schema-catalog.md` updated. Schema convention tests pass. Table + column comments added.
7. ✅ New scripts: `Script-Governance` header added → `scripts/check-script-governance.ps1` passes.
8. ✅ Affected `verify-*.ps1` scripts still pass.
9. ✅ Relevant docs in `docs/architecture/` and `docs/adr/` updated.

## Finding Documentation

1. **Start here**: `docs/architecture/implementation-readiness.md` — current phase,
   delivered services, environment prerequisites.
2. **Service boundaries**: `docs/architecture/context-map.md`.
3. **Directory rules**: `docs/architecture/repo-layout.md`.
4. **ADRs**: `docs/adr/` — numbered sequentially, read in order for architectural
   decisions and their rationale.
5. **All other topics**: `docs/architecture/` files are named by topic
   (e.g., `database-schema-conventions.md`, `frontend-structure.md`,
   `script-automation-governance.md`). Search by keyword when needed.

## Sub-directory Overrides

- `backend/services/<Svc>/AGENTS.md` — Service-specific build/test notes, schema owner, EF migration hints.
- `frontend/apps/console/AGENTS.md` — Console-specific routing, composables, token conventions.
- `scripts/AGENTS.md` — Additional script governance rules if needed.
- Use `AGENTS.override.md` for temporary overrides; rename or remove to restore base guidance.

## GitHub Workflow

- Use `gh` CLI directly for PR creation. Do not use the GitHub connector.
- If a PR operation fails via `gh`, report the command and error clearly.
