# Main Platform Development Entrypoint Design

## Purpose

This design turns the existing platform topology into a first-class development experience.

The repository already has a platform-level Aspire AppHost under `infra/aspire/Nerv.IIP.AppHost`, and that AppHost describes the real local topology: Gateway, IAM, AppHub, Ops, FileStorage, Connector Host, Console, PostgreSQL, Redis, RabbitMQ, MinIO and OpenTelemetry Collector. The missing piece is a unified command surface at the repository root.

The desired outcome is simple: a developer can clone the repository, install the documented runtimes, and start the main platform with one stable command instead of rediscovering service dependency order and scattered ports.

## Current Context

1. Aspire AppHost is already the topology source for local development and integration.
2. `infra/docker-compose.dev.yml` remains the stable dependency-only fallback used by verification scripts.
3. Existing verification scripts live under `scripts/` and are governed by `docs/architecture/script-automation-governance.md`.
4. `scripts/lib/ScriptAutomation.ps1` already provides safe wrappers for native commands, background processes, logging, timeout cleanup and scoped environment variables.
5. Frontend commands are currently exposed from `frontend/package.json` and `frontend/apps/console/package.json`.
6. Service launch ports are inconsistent: Gateway uses `5073`, Ops uses `5105`, AppHub uses `5204`, FileStorage uses `5261`, IAM uses `5283`, while older docs and fallback code still mention `5100`, `5103`, `5104` and `5105`.
7. Console development currently uses `127.0.0.1:5173`, which is Vite's common default port and should not become the platform's canonical Console port.
8. Local MinIO runtime references currently use `minio/minio` in both Aspire AppHost and `infra/docker-compose.dev.yml`; this should move to the maintained `pgsty/minio` image line for local development.

## Recommended Approach

Use a thin CLI wrapper over governed scripts.

The selected approach has two layers:

1. `nerv.ps1` at the repository root is the human-facing command surface.
2. `scripts/dev.ps1` contains the governed implementation for local startup and delegates process execution to `scripts/lib/ScriptAutomation.ps1`.

This keeps the project feeling like a complete platform without introducing a heavy CLI framework too early. The root CLI should remain intentionally thin; long-lived behavior belongs in governed scripts.

## Alternatives Considered

1. **Script only**: add `scripts/dev.ps1` and document it. This is stable, but it still makes the repository feel like a collection of subprojects rather than one platform.
2. **Full CLI tool**: build a .NET or Node CLI. This is attractive long term, but it creates a new packaging and versioning surface before the command set is large enough to justify it.
3. **Thin CLI plus governed scripts**: expose `.\nerv.ps1 dev` at the root, keep real work in `scripts/`, and evolve toward a full CLI only when the command surface grows. This is the selected approach.

## Scope

### In Scope

1. Add a root `nerv.ps1` command wrapper.
2. Add `scripts/dev.ps1` as the main local platform startup script.
3. Add a simple port matrix command exposed through `.\nerv.ps1 ports`.
4. Standardize local HTTP service ports into a contiguous platform range.
5. Update service `launchSettings.json` files and local fallback base URLs to match the matrix.
6. Update Console server-side default API base URL if needed.
7. Update README and architecture docs with the daily startup path.
8. Add script governance coverage for the new scripts.
9. Add focused verification for command routing, script governance and AppHost build.
10. Update local MinIO container image references from `minio/minio` to `pgsty/minio` with an explicit release tag.

### Out Of Scope

1. Building a packaged .NET global tool, npm package or standalone binary CLI.
2. Replacing Aspire AppHost as the topology source.
3. Generating production Docker Compose from Aspire.
4. Implementing Windows Service, systemd or offline installation workflows.
5. Making infrastructure service ports artificially contiguous when ecosystem defaults are more recognizable.
6. Changing production configuration, customer deployment ports or network policy.
7. Starting or stopping user-managed Docker resources outside the current local development topology.
8. Replacing the FileStorage object-storage abstraction or choosing a non-MinIO S3-compatible backend.

## Command Surface

The first version supports these commands:

```powershell
.\nerv.ps1 dev
.\nerv.ps1 dev -NoBuild
.\nerv.ps1 dev -InfraOnly
.\nerv.ps1 dev -OpenDashboard
.\nerv.ps1 ports
.\nerv.ps1 help
```

`.\nerv.ps1 dev` runs the Aspire AppHost:

```powershell
dotnet run --project infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj
```

`-NoBuild` forwards the expected `--no-build` behavior when supported by the underlying command.

`-InfraOnly` starts only the dependency services through `infra/docker-compose.dev.yml`. This is for backend-focused test and migration work, not for full platform use.

`-OpenDashboard` is reserved for opening or surfacing the Aspire dashboard URL when it can be discovered reliably. If it cannot be discovered in the first implementation, the script should print a clear message and leave the flag as a no-op rather than guessing.

`.\nerv.ps1 ports` prints the canonical local development port matrix.

## Port Matrix

Platform HTTP services use a contiguous `5100` range:

| Port | Service |
| --- | --- |
| `5100` | PlatformGateway |
| `5101` | AppHub |
| `5102` | IAM |
| `5103` | Ops |
| `5104` | FileStorage |
| `5105` | Console |

Infrastructure services keep familiar ecosystem ports:

| Port | Service |
| --- | --- |
| `15432` | PostgreSQL host mapping |
| `6379` | Redis |
| `5672` | RabbitMQ AMQP |
| `15672` | RabbitMQ Management |
| `9000` | MinIO API |
| `9001` | MinIO Console |
| `4317` | OTLP gRPC |
| `4318` | OTLP HTTP |

## Container Image Baseline

Local development should use `pgsty/minio` instead of `minio/minio`.

The implementation should update both local topology entrypoints:

1. `infra/aspire/Nerv.IIP.AppHost/Program.cs`
2. `infra/docker-compose.dev.yml`

Use an explicit release tag rather than `latest`; at design time the intended tag is:

```text
pgsty/minio:RELEASE.2026-04-17T00-00-00Z
```

If a newer `pgsty/minio` release exists at implementation time, prefer the latest stable release tag after checking the image metadata. This change is only about the local development container image. The platform still treats object storage through the FileStorage provider abstraction and remains compatible with MinIO or equivalent S3-compatible object storage.

The service-port updates should touch:

1. `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/Properties/launchSettings.json`
2. `backend/services/AppHub/src/Nerv.IIP.AppHub.Web/Properties/launchSettings.json`
3. `backend/services/Iam/src/Nerv.IIP.Iam.Web/Properties/launchSettings.json`
4. `backend/services/Ops/src/Nerv.IIP.Ops.Web/Properties/launchSettings.json`
5. `backend/services/FileStorage/src/Nerv.IIP.FileStorage.Web/Properties/launchSettings.json`
6. `backend/gateway/PlatformGateway/src/Nerv.IIP.PlatformGateway.Web/appsettings.Development.json`
7. `connector-hosts/src/Nerv.IIP.ConnectorHost.Host/appsettings.Development.json`
8. hardcoded fallback URLs in `Program.cs` files where they represent local development defaults.
9. `frontend/packages/api-client/src/transport/base-url.ts` if its server-side default still points at the old Gateway port.
10. `frontend/apps/console/package.json` and `frontend/apps/console/vite.config.ts` so the Console dev server no longer uses Vite's default `5173` port.

## Architecture

### Root CLI Wrapper

`nerv.ps1` should do only command dispatch:

1. Resolve the repository root from its own location.
2. Parse the first positional command.
3. Forward `dev` arguments to `scripts/dev.ps1`.
4. Print the static port matrix for `ports`.
5. Print concise usage text for `help` or unknown commands.

It should not directly run `dotnet`, `docker`, `pnpm` or other native tools. That keeps native execution inside governed scripts.

### Development Script

`scripts/dev.ps1` should:

1. Declare `Script-Governance` metadata with category `check`.
2. Dot-source `scripts/lib/ScriptAutomation.ps1`.
3. Validate required tools with `Get-Command` and clear error messages.
4. For full platform startup, call `Invoke-DotNet` against the Aspire AppHost.
5. For `-InfraOnly`, call `Invoke-DockerCompose` against `infra/docker-compose.dev.yml`.
6. Use existing logging and redaction behavior from `ScriptAutomation.ps1`.
7. Avoid printing secrets, full connection strings or tokens.

The full platform path should not manually start services in dependency order. Aspire owns that dependency graph.

### Aspire AppHost

The AppHost remains the local topology source. If fixed local service ports are required for direct browser/API access, the implementation should configure them through AppHost resource endpoint configuration or service launch settings in a way that does not fork topology truth.

### Documentation

README should gain a short "daily development" section near the current status or technical baseline:

```powershell
.\nerv.ps1 dev
```

It should explain that Aspire is the full-platform entrypoint, while `.\nerv.ps1 dev -InfraOnly` starts dependency services only.

Architecture docs should record that root CLI commands are wrappers over governed scripts, not a separate deployment model.

## Error Handling

1. Missing PowerShell 7 should be called out in documentation. The scripts should fail early if running under incompatible PowerShell.
2. Missing .NET SDK should fail before invoking AppHost.
3. Missing Docker should fail only for `-InfraOnly` or when Aspire/container resources require it.
4. Missing pnpm should be reported before full AppHost startup because Console is part of the AppHost topology.
5. Port conflicts should be documented as common local failures. First implementation can rely on underlying tool errors, but the docs should point users to `.\nerv.ps1 ports`.
6. Unknown CLI commands should return a non-zero exit code with usage text.

## Testing And Verification

Focused verification should include:

1. `pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/check-script-governance.ps1`
2. `dotnet build infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj --no-restore`
3. `pwsh -NoProfile -ExecutionPolicy Bypass -File .\nerv.ps1 ports`
4. `pwsh -NoProfile -ExecutionPolicy Bypass -File .\nerv.ps1 help`

If time and local dependencies allow, run a short startup smoke test of `.\nerv.ps1 dev` and stop it after AppHost reports that resources are starting. This smoke test should not become a brittle long-running gate in the first implementation.

## Migration Notes

1. Documentation that mentions old local ports should be updated when it is user-facing current guidance.
2. Historical Superpowers plans can keep old ports if they are clearly archival implementation notes.
3. Tests should prefer configured base URLs over hardcoded ports unless the test explicitly validates local defaults.
4. `infra/docker-compose.dev.yml` should keep PostgreSQL on `15432` because that decision intentionally avoids conflicts with local PostgreSQL on `5432`.

## Success Criteria

1. A developer can run `.\nerv.ps1 dev` from the repository root to start the full local platform topology.
2. A developer can run `.\nerv.ps1 ports` and see the canonical local port matrix.
3. Platform HTTP service ports are contiguous and documented.
4. Existing direct service fallback URLs do not contradict the documented port matrix.
5. New scripts pass script governance.
6. README makes the daily startup path obvious without requiring users to read dependency topology first.
