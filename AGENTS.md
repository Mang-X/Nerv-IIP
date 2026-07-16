# AGENTS.md — Nerv-IIP Platform

> Canonical agent instruction file (shared by all agents/models). For current
> project status, always read `docs/architecture/implementation-readiness.md` first.

## Before You Start

**Always read `docs/architecture/implementation-readiness.md`** before making changes.
It records the current phase, delivered services, database schemas, and environment
prerequisites. Do NOT assume a service, schema, or port is ready based on prior
knowledge — verify there.

Where to find the rest:

- **Architectural decisions**: `docs/adr/` — numbered sequentially.
- **Service boundaries**: `docs/architecture/context-map.md`.
- **Directory rules**: `docs/architecture/repo-layout.md` (canonical repo layout).
- **Local dev / Aspire troubleshooting**: `docs/architecture/local-dev-troubleshooting.md`
  — read it before debugging startup, infra containers, or deployment artifacts.
- **All other topics**: `docs/architecture/` files are named by topic
  (e.g., `database-schema-conventions.md`, `frontend-structure.md`). Search by keyword.

Non-obvious boundaries: `backend/` is one CleanDDD service per directory
(`services/`, business services under `services/Business/`) plus `gateway/`
(PlatformGateway for Console, BusinessGateway for Business Console/PDA facades)
and narrow shared libs in `common/`. `connector-hosts/` is a separate .sln —
never merged into or referenced from `backend/` or `frontend/` (or vice-versa).

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
.\nerv.ps1 fullstack run -Scenario smoke  # Agent-owned real full-stack verification
.\nerv.ps1 fullstack start               # Interactive diagnostics only
.\nerv.ps1 fullstack stop                # Stop the exact diagnostic session
```

Agent-owned real full-stack verification must use `fullstack run`. Interactive
`fullstack start` is diagnostic-only and must be stopped before handoff.

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

### Frontend (Node.js >=22.18.0, pnpm 11.13.1)
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
| Backend service / endpoint | implementation-readiness, api-contract-and-codegen, facade-coverage-matrix | `dotnet test backend/Nerv.IIP.sln` (includes the facade-coverage gate); declare each new/changed business endpoint `exposed`/`deferred`/`internal` in `facade-coverage-matrix.json`; if contract changed: export OpenAPI |
| Gateway route / contract | api-contract-and-codegen | backend tests; export OpenAPI; `pnpm -C frontend generate:api` |
| DB schema / migration | database-schema-conventions, database-schema-catalog | migration + schema convention tests; update catalog + comments |
| Frontend page / feature | frontend-structure | `pnpm -C frontend typecheck && pnpm -C frontend test && pnpm -C frontend build` |
| Scripts | script-automation-governance | `scripts/check-script-governance.ps1` |
| Connector Host | connector boundary docs | `dotnet test connector-hosts/Nerv.IIP.ConnectorHost.sln`; no backend service impl references |
| Infra / Aspire | deployment-baseline | `dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj` |

PDA changes additionally run `typecheck`/`test`/`build` with
`pnpm -C frontend --filter @nerv-iip/business-pda`; run `cap:sync` when native
Capacitor artifacts are affected.

## Known Baseline Caveats

- `pnpm -C frontend check` / `fmt` are blocked by pre-existing out-of-scope
  formatting issues. Every file you touch must still pass individually
  (`pnpm -C frontend exec vp fmt --check <file>`); report whether any failure is
  pre-existing or introduced.
- GitHub CI runs `typecheck` + `build` for frontend (not the full gate) — run the
  full gate locally per the Change Decision Table.
- Docker-dependent `verify-*.ps1` scripts require a running Docker daemon; if
  unavailable, report and skip — not a code failure.

## Core Principles

1. Platform-before-business. Industry semantics (factory, line, equipment models)
   live in domain extensions — never in the main platform, PlatformGateway, IAM,
   AppHub, Ops, or main console.
2. Logical boundaries freeze first; physical deployment stays flexible.
3. Frontend uses explicit Vue structure only (no pseudo-Nuxt runtime); backend is
   organized by service boundary (no regression to monolith).
4. App integration goes through the Connector Host pattern in `connector-hosts/`.
5. AI capabilities: governance → query → low-risk actions. No model hosting in
   the main platform.
6. Platform SDK is modular and client-only; external units must not reference
   main platform internals.
7. File Storage and Notification are generic; business services express intent by
   ID only.
8. Major version alignment between platform, apps, Connector Hosts, extensions.
9. Docs, contracts, and catalogs are the stability foundation — update docs
   before structural changes.
10. Aspire is the single deployment model; Compose/installer/package adapt from it.
11. Automation scripts are governed, auditable engineering assets.

## "Do NOT" Constraints

1. Do NOT create unbound SharedKernel/Common/Utils mega-directories.
2. Do NOT merge Connector Host into `backend/Nerv.IIP.sln` or cross-reference
   `connector-hosts/` with `backend/`/`frontend/`.
3. Do NOT write industry domain rules into PlatformGateway, IAM, AppHub, Ops, or
   main console.
4. Do NOT create cross-schema foreign keys between services.
5. Do NOT use `EnsureCreated()` in non-disposable environments.
6. Do NOT call `dotnet`, `docker`, `pnpm`, `pwsh` directly in scripts — dot-source
   `scripts/lib/ScriptAutomation.ps1` (`Invoke-DotNet`, `Invoke-Pnpm`, …); never
   define a function named `Write-Error` (shadows the built-in cmdlet).
7. Do NOT write Minimal API route mappings in startup files — FastEndpoints only.
8. Do NOT hand-edit OpenAPI snapshots or generated client code.
9. Do NOT use non-`Nv*` component names or deep-import `components/ui/` in app
   code (see "NvUI Component Library").
10. Do NOT reference provider-specific APIs or write raw SQL in
    Domain/Application/Endpoint/SDK layers.
11. Do NOT store credentials, secrets, or customer keys in `infra/` or the repo.
12. Do NOT start the AppHost with `dotnet run` — always Aspire CLI via
    `.\nerv.ps1 dev`/`stop`/`wait`/`logs` (see local-dev-troubleshooting).

## NvUI Component Library — naming & import boundary (frontend)

NvUI is the Nerv-IIP brand component layer inside `@nerv-iip/ui` /
`@nerv-iip/ui-mobile`. Authoritative spec: ADR 0020
(`docs/adr/0020-nvui-naming-token-namespaces-and-style-isolation.md`;
**Appendix A = the frozen per-component map**).

1. **App/business code uses `Nv*` brand components only** (`NvButton`,
   `NvDataTable`, `NvPageHeader`, `NvOeeHero`, `NvMobileBadge`, …). A name
   without the `Nv` prefix is a shadcn 原版 base primitive — referenced ONLY
   inside `@nerv-iip/ui` itself, never from an app.
2. **Import through the stable boundary only:** bare `@nerv-iip/ui` and
   `@nerv-iip/ui-mobile` (sole allowed sub-entry: `@nerv-iip/ui/file-preview`).
   No deep paths, no direct `reka-ui`, no direct `shadcn-vue`.
3. **Enforcement is contract tests, not ESLint** (ADR 0006):
   `nvui-imports.contract.test.ts` per app, `nvui-naming.contract.test.ts` per
   package.
4. **Package names never change** (ADR 0020 Decision 2): the brand is the `Nv*`
   prefix — never rename `@nerv-iip/ui` / `@nerv-iip/ui-mobile` in passing.
5. **shadcn 原版 in `components/ui/` stay byte-for-byte unchanged** (no `Nv`,
   no `--nv-`). Customizations are rebuilt copies in the brand layers.

**Four-surface map** (examples only — the frozen table is ADR 0020 Appendix A):

| Surface | Package · layer | Naming rule | Examples |
|---|---|---|---|
| PC (console / business-console) | `@nerv-iip/ui` · `pc/` `blocks/` `layout/` | 素名优先 → `Nv` + plain name | `NvButton` `NvDataTable` `NvPageHeader` |
| Mobile (business-pda) | `@nerv-iip/ui-mobile` | clash with 原版/PC → `NvMobile*`; mobile-native专名 → `Nv*` | `NvMobileBadge` `NvMobileDialog` · `NvScanBar` `NvCell` |
| Touch (工位看板 / 车间一体机) | `@nerv-iip/ui` · `touch/` | clash → `NvTouch*`, else `Nv*` | `NvTouchButton` `NvQtyStepper` |
| Screen (大屏 / 挂墙) | `@nerv-iip/ui` · `screen/` | generic word → `NvScreen*`; industrial专名 → `Nv*` | `NvScreenButton` · `NvOeeHero` `NvTaktGantt` |

A component that spans two surfaces is built twice (one per layer) — never "one
component, two modes". New names follow ADR 0020 §1.2 (R1–R5).

## Common Mistakes

Repo-specific pitfalls that caused real regressions. Operational/startup lessons
(Aspire, infra pinning, deployment artifacts) are in
`docs/architecture/local-dev-troubleshooting.md`.

1. **Endpoints default to `[AllowAnonymous]`.** Internal service APIs require
   `[Authorize(Policy = InternalServiceAuthorizationPolicy.Name)]`; Gateway
   Console endpoints use `GatewayPolicies.ConsoleAuthenticated`; only health
   endpoints stay anonymous.
2. **Scaffold residue.** When scaffolding from NetCorePal templates, delete all
   demo endpoints/aggregates/tests; verify `ServiceName`; `UseAuthentication()`
   before `UseAuthorization()`.
3. **`Guid.NewGuid()` instead of `Guid.CreateVersion7()`.** EF's v7 generator
   only fires on default IDs; constructor-assigned v4 GUIDs break time-ordered
   index locality.
4. **Synchronous EF Core calls.** Repository/service/query-handler methods are
   async with `CancellationToken`, always.
5. **`Environment.SetEnvironmentVariable()` in parallel tests.** Use
   `builder.UseSetting()` or a `DisableParallelization` collection.
6. **Missing `MigrationsHistoryTable("__EFMigrationsHistory", "<schema>")`** on a
   service's PostgreSQL DbContext (history lands in `public` otherwise).
7. **String comparison for boolean config.** Use
   `builder.Configuration.GetValue<bool>(...)`.
8. **Query filters not matching the DB unique index.** If the unique index has N
   columns, dedup/lookup queries must filter on all N.
9. **Secrets in Domain aggregates.** Secret names/API keys/credential references
   live in Infrastructure or configuration only.
10. **InMemory auth stores issuing fake tokens.** They must produce real JWTs via
    the service's token issuer or Gateway JWT validation rejects everything.
11. **Test assertions via source-file path traversal.** Use DI, `DbContext`
    reflection, or `Nerv.IIP.Testing` helpers — never
    `Path.Combine(AppContext.BaseDirectory, "..", ...)`.
12. **Context-free readiness endpoints reporting context-specific blockers.**
    Global readiness must not report SKU/work-center/device-scoped blockers when
    that scope was never supplied; the owning frontend shows the selection
    prompt or empty state.
13. **Frontend facade calls with empty business scope.** Composables normalize
    IDs and suppress queries whose required scope is empty — no request or a
    clear empty state, never repeated failing calls.
14. **Demo/default identifiers causing 500s.** Defaults like `WO-001` are UI
    conveniences, not seed guarantees; handlers/facades return a
    domain-appropriate empty/`Unknown` result for missing records.

## "Done" Definition

Verify against the Change Decision Table. At minimum:

1. ✅ Targeted tests pass for the area changed.
2. ✅ No new warnings (backend has warnings-as-errors).
3. ✅ Generated artifacts refreshed if contracts changed (OpenAPI → api-client).
4. ✅ DB migrations/catalog/comments updated if schema changed.
5. ✅ Scripts pass governance if scripts changed.
6. ✅ Affected `verify-*.ps1` scripts still pass.
7. ✅ Relevant docs in `docs/architecture/` and `docs/adr/` updated.
8. ✅ New/changed business-service HTTP endpoints declared in
   `facade-coverage-matrix.json` as `exposed`/`deferred`/`internal`; the
   facade-coverage gate passes (see below).

## Facade Coverage Governance — the two-hop DoD (business endpoints)

A business capability is only usable end-to-end when it ships as **two hops**:
the service HTTP endpoint **and** a Gateway facade (OpenAPI snapshot →
`pnpm -C frontend generate:api` → `types.gen.ts` → stable barrel). Any issue/PR
adding or changing a business-service HTTP endpoint MUST declare, per endpoint:

1. **`exposed`** — the same PR delivers facade + OpenAPI export + codegen +
   stable-barrel re-export.
2. **`deferred`** — explicitly postponed; register in
   `facade-coverage-matrix.json` with a `followUp`. A tracked gap, never silent.
3. **`internal`** — never exposed by design (service-to-service, background
   scheduler, connector/WCS callback); register with a `rationale`.

**Enforcement:** `backend/tests/Nerv.IIP.FacadeCoverage.Tests` (inside
`dotnet test backend/Nerv.IIP.sln`, in CI) reflects every service's
`*EndpointContracts.All` registry and fails on unregistered live endpoints,
`exposed` rows missing from the Gateway snapshot, or `deferred`/`internal` rows
silently given a facade. Registry + narrative:
`docs/architecture/facade-coverage-matrix.{json,md}`. New business services must
also register their `.Web` assembly in the gate project.

## GitHub Workflow

- Use `gh` CLI directly for PR creation (the GitHub connector has repeatedly
  returned 404 in this repo). If a `gh` operation fails, report the command and
  error clearly.
- PR descriptions answer a docs-impact checklist: does the change affect product
  docs (`frontend/apps/docs`)? New pages, changed business flows, or
  user-visible behavior count as "yes" — update docs in the same PR or reference
  a follow-up issue; otherwise write "文档：无影响". IA rules: ADR 0021.
- If the PR adds/changes a business-service HTTP endpoint, state the facade
  declaration for each (`exposed`/`deferred`/`internal`) and confirm
  `facade-coverage-matrix.json` was updated. Hard gate.

## Sub-directory Overrides

The nearest `AGENTS.md` in a subtree extends and overrides this file for that
subtree. Currently: `frontend/apps/business-console/AGENTS.md`. Use
`AGENTS.override.md` for temporary overrides; remove it to restore base guidance.
