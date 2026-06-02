# AGENTS.md — Nerv-IIP Platform

> Canonical agent instruction file. For current project status, always read
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
.\nerv.ps1 bootstrap        # Connected blank-machine preflight, restore, local secrets
.\nerv.ps1 bootstrap -InstallMissing -Start
.\nerv.ps1 dev              # Full platform via Aspire CLI/AppHost
.\nerv.ps1 stop             # Stop current AppHost via Aspire CLI
.\nerv.ps1 status           # Show running Aspire AppHosts/resources
.\nerv.ps1 logs apphub      # Tail Aspire resource logs
.\nerv.ps1 wait gateway -Status up -TimeoutSeconds 600
.\nerv.ps1 dev -InfraOnly   # Infra only (PostgreSQL, Redis, RabbitMQ, MinIO, OTel)
.\nerv.ps1 publish-compose  # Generate Aspire Docker Compose artifacts
.\nerv.ps1 ports            # Canonical port matrix
```

### Backend (.NET 10)
```powershell
dotnet build backend/Nerv.IIP.sln
dotnet test  backend/Nerv.IIP.sln
dotnet test  connector-hosts/Nerv.IIP.ConnectorHost.sln

# EF migrations — set PostgreSQL profile explicitly:
$env:Persistence__Provider = "PostgreSQL"
dotnet tool restore
dotnet tool run dotnet-ef migrations add <Name> `
  --project backend/services/<Svc>/src/Nerv.IIP.<Svc>.Infrastructure `
  --startup-project backend/services/<Svc>/src/Nerv.IIP.<Svc>.Web
```

### Frontend (Node.js >=22.18.0, pnpm 11.1.2)
```powershell
pnpm -C frontend typecheck     # fastest single check
pnpm -C frontend test           # vitest
pnpm -C frontend build          # production build
pnpm -C frontend generate:api   # Hey API codegen from Gateway OpenAPI snapshot
```

### Scripts (governed)
```powershell
scripts/check-script-governance.ps1   # Gate for all scripts
scripts/verify-*.ps1                  # Verification scripts
```

## Change Decision Table

| Change area | Required docs | Required checks |
|---|---|---|
| Backend service / endpoint | implementation-readiness, api-contract-and-codegen | `dotnet test backend/Nerv.IIP.sln`; if contract changed: export OpenAPI |
| Gateway route / contract | api-contract-and-codegen | backend tests; export OpenAPI; `pnpm -C frontend generate:api` |
| DB schema / migration | database-schema-conventions, database-schema-catalog | migration + schema convention tests; update catalog + comments |
| Frontend page / feature | frontend-structure | `pnpm -C frontend typecheck && pnpm -C frontend test && pnpm -C frontend build` |
| Scripts | script-automation-governance | `scripts/check-script-governance.ps1` |
| Connector Host | connector boundary docs | `dotnet test connector-hosts/Nerv.IIP.ConnectorHost.sln`; no backend service impl references |
| Infra / Aspire | deployment-baseline | `dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj` |

## Known Baseline Caveats

- `pnpm -C frontend check` and `pnpm -C frontend fmt` are currently blocked by
  pre-existing out-of-scope formatting issues. If you encounter failures from these
  commands, check `docs/architecture/implementation-readiness.md` to confirm they
  are known. Report clearly whether failures are pre-existing or introduced by your
  change. Touched files should pass individually.
- GitHub CI runs `typecheck` + `build` for frontend (not full quality gate). The
  `.codex` actions include the full gate for completeness, but known pre-existing
  issues are not regressions.
- Docker-dependent verify scripts (`verify-second-slice-ops.ps1`,
  `verify-iam-persistent-auth-foundation.ps1`) require a running Docker daemon.
  If Docker is unavailable, report clearly and skip — do not treat as a code failure.

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
3. Do NOT reference `connector-hosts/` from `backend/` or `frontend/` (and vice-versa).
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

13. **Treating Aspire `Finished` as a dashboard problem.** A project resource shown
    as `Finished` usually means the process exited during startup. Inspect the latest
    DCP stderr log under `%TEMP%\aspire-dcp*` before changing code or restarting
    blindly. The real error is usually in the resource process log, not Aspire
    itself.

14. **Forgetting local Development environment in AppHost project resources.**
    Platform AppHost is the canonical dev launcher. New project resources must run
    with `ASPNETCORE_ENVIRONMENT=Development` and `DOTNET_ENVIRONMENT=Development`
    unless there is an explicit test/deployment reason not to. Otherwise services may
    select production-like persistence or messaging branches and fail differently
    from local expectations.

15. **PostgreSQL services added to AppHost without local migration enablement.**
    If a local Development service relies on PostgreSQL migrations, verify whether
    AppHost must pass `Persistence__AutoMigrate=true` for that resource. Missing
    migration enablement can surface as broad Console request failures, downstream
    500s, or gateway circuit breakers; the root cause may be a missing table such as
    `relation "...table..." does not exist`. Observed local failures include AppHub
    `apphub.registration_idempotency`, MES execution tables, Maintenance readiness
    tables, and Notification `notification_messages` / `notification_tasks`.

16. **CAP PostgreSQL profile without integration event publisher registration.**
    Services with domain-event-to-integration-event converters must register the
    NetCorePal integration event publisher in the active CAP profile, including
    PostgreSQL. If startup fails with unresolved
    `NetCorePal.Extensions.DistributedTransactions.IIntegrationEventPublisher`,
    compare the service's CAP registration with a known working service before
    changing handlers.

17. **Redis-backed services aborting startup on first connect attempt.** Local
    Aspire startup can race Redis readiness. When a service constructs a
    `ConnectionMultiplexer`, parse options with `AbortOnConnectFail=false` so the
    service can start and reconnect instead of turning one transient Redis race into
    a failed resource.

18. **Context-free readiness checks reported as execution blockers.** Diagnostic
    endpoints such as MES `foundation-readiness` may be called without SKU,
    production version, work center, or device scope. Global readiness should not
    report context-specific quality/equipment execution blockers unless the required
    execution context was actually supplied. In that case, the frontend/workbench
    that owns the missing scope should present the selection prompt or empty state.

19. **Frontend facade calls with empty business scope.** Business Console composables
    must normalize IDs and suppress queries that require a device, work center, SKU,
    production version, or work order when that scope is empty. Empty scope should be
    represented as no request or a clear empty state, not as repeated failing backend
    calls.

20. **Demo/default identifiers causing backend 500s.** Console defaults such as
    `WO-001` are UI conveniences, not durable seed guarantees. Query handlers and
    facades must tolerate missing demo/default records with a domain-appropriate
    empty or `Unknown` result instead of throwing 500s.

21. **Starting AppHost with `dotnet run`.** The platform AppHost must be managed by
    Aspire CLI: use `.\nerv.ps1 dev` / `aspire start`, `.\nerv.ps1 stop` /
    `aspire stop`, `.\nerv.ps1 wait <resource>` / `aspire wait`, and
    `.\nerv.ps1 logs <resource>` / `aspire logs`. In linked worktrees, startup must
    use Aspire isolated mode; `scripts/dev.ps1` handles this. Direct `dotnet run`
    leaves stale DCP/backchannel state and makes later `aspire add`, deploy, and
    diagnostics unreliable.

22. **Maintaining a second full-platform Compose topology.** Aspire AppHost is the
    topology source. For container deployment, add/maintain Aspire deployment
    targets and generate Docker Compose artifacts with `.\nerv.ps1 publish-compose`
    or deploy with `.\nerv.ps1 deploy-compose`. Existing hand-written Compose files
    may remain for dependencies, smoke tests, or legacy overlay validation, but
    must not become a competing service graph.

23. **Assuming Vite dev proxy becomes production routing.** `AddViteApp` works for
    local dev, but publish/deploy needs an explicit JavaScript production serving
    model. Console can use `PublishAsStaticWebsite("/api", gateway)`. Business
    Console needs two production API routes (`/api/console` to PlatformGateway and
    `/api/business-console` to BusinessGateway) or an equivalent BusinessGateway
    auth facade before Compose output can be called a complete Business Console
    deployment.

24. **Skipping connected-machine bootstrap on a blank machine.** For a fresh online
    Windows machine, use `.\nerv.ps1 bootstrap -InstallMissing` first, then
    `.\nerv.ps1 dev`. The bootstrap entry owns prerequisite checks, optional tool
    installation, local AppHost user-secrets initialization, package restore and
    AppHost build. Do not debug broad request failures until this path has passed
    and Docker Desktop is actually running.

25. **Treating offline deployment as the current startup path.** Offline packaging is
    a deployment architecture track, not the first local-development fix. Keep the
    immediate startup path focused on connected machines and Aspire CLI/AppHost.
    Future offline scripts should consume Aspire-generated artifacts instead of
    inventing a parallel topology.

26. **Hardcoding bootstrap seed passwords.** Connected-machine bootstrap may create
    local Development user-secrets, but it must not keep a fixed IAM admin password
    in source. Generate a random local value by default, or require the operator to
    pass a value explicitly through a non-logged path. Secret-setting commands must
    mark sensitive arguments for script log redaction.

27. **Letting Aspire infrastructure image tags drift.** Persistent local resources
    must be explicitly pinned in AppHost. PostgreSQL is currently `18` and Redis
    is currently `8`; do not use `latest` or unpinned Aspire provider defaults.
    PostgreSQL 18+ uses a different major-version data directory than the old
    pre-18 `/var/lib/postgresql/data` layout, so local dev uses
    `nerv-iip-postgres-18` and must not point PostgreSQL 18 back at the old
    `nerv-iip-postgres` volume without an explicit `pg_upgrade` or dump/restore.
    Do not switch major versions without a tracked upgrade plan, clean-volume test,
    preserved-volume migration test where applicable, AppHost build, Compose
    publish verification, and smoke startup. If Redis reports an RDB/AOF format
    error, stop Aspire and remove only the local `nerv-iip-redis` cache volume.

28. **Startup/stop scripts with no bounded feedback.** `.\nerv.ps1 dev` and
    `.\nerv.ps1 stop` must show phase diagnostics and use bounded helper calls.
    A failed certificate check, exited container, Aspire/DCP hang, or successful
    startup must not all look like "still waiting". Stop must run fallback cleanup
    for current-repo AppHost processes and Aspire usvc-dev containers when Aspire
    CLI stop times out.

29. **Skipping local HTTPS certificate validation.** Aspire Dashboard/DCP and local
    HTTPS endpoints require a trusted developer certificate. On blank machines or
    after Aspire certificate cache changes, run `.\nerv.ps1 bootstrap -InstallMissing`
    or verify with `dotnet dev-certs https --check --trust`. If AppHost logs show
    certificate name mismatch, reset with `aspire certs clean`, `aspire certs trust`,
    and `dotnet dev-certs https --trust`.

## "Done" Definition

Before claiming a task is complete, verify against the Change Decision Table above.
At minimum:

1. ✅ Targeted tests pass for the area you changed.
2. ✅ No new warnings introduced (backend has warnings-as-errors).
3. ✅ Generated artifacts refreshed if contracts changed (OpenAPI → api-client).
4. ✅ DB migrations/catalog/comments updated if schema changed.
5. ✅ Scripts pass governance if scripts changed.
6. ✅ Affected `verify-*.ps1` scripts still pass.
7. ✅ Relevant docs in `docs/architecture/` and `docs/adr/` updated.

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

- Use `gh` CLI directly for PR creation. Do not use the GitHub connector — it has
  repeatedly returned 404 in this repo while `gh` is already authenticated and works.
- If a PR operation fails via `gh`, report the command and error clearly.
