# Aspire local development

`Nerv.IIP.AppHost` is the local full-platform topology for Gateway, AppHub, IAM, Ops, FileStorage, Connector Host, Business Gateway, Business Console and shared infrastructure.

The AppHost intentionally does not hardcode local secrets. `.\nerv.ps1 dev` checks the required AppHost user secrets before startup and fails fast when any are missing. This prevents Aspire from silently generating new random values that no longer match existing Docker volumes. Store repeatable local values in the AppHost user secrets store:

On a connected blank development machine, prefer the repository bootstrap entry first:

```powershell
.\nerv.ps1 bootstrap -InstallMissing
```

That command checks the required toolchain, installs missing Windows prerequisites
through `winget` when requested, initializes missing local Development user secrets,
trusts local HTTPS developer certificates, restores backend/frontend dependencies,
and builds the AppHost. Docker Desktop may still need to be started manually after
first installation. The manual commands below are for cases where you intentionally
want to set repeatable local values yourself. Bootstrap does not contain a fixed IAM
admin password; pass `-LocalAdminPassword` before the first database seed if you
need a known local login password, or inspect/reset the local user-secret value
yourself.

```powershell
dotnet user-secrets set "Parameters:iam-jwt-signing-key" "<at-least-32-byte-local-signing-key>" --project infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj
dotnet user-secrets set "Parameters:internal-service-bearer-token" "<local-internal-service-token>" --project infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj
dotnet user-secrets set "Parameters:postgres-password" "<local-postgres-password>" --project infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj
dotnet user-secrets set "Parameters:redis-password" "<local-redis-password>" --project infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj
dotnet user-secrets set "Parameters:minio-root-user" "<local-minio-user>" --project infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj
dotnet user-secrets set "Parameters:minio-root-password" "<local-minio-password>" --project infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj
dotnet user-secrets set "Parameters:iam-seed-admin-password" "<local-admin-password>" --project infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj
dotnet user-secrets set "Parameters:iam-seed-connector-host-secret" "<local-connector-secret>" --project infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj
```

Then start the platform from the repository root:

```powershell
.\nerv.ps1 dev
```

`.\nerv.ps1 dev` uses `aspire start --non-interactive` and automatically adds
`--isolated` when the repository is opened from a linked worktree. In isolated
mode, Aspire may assign dynamic host ports instead of the canonical local ports;
use `.\nerv.ps1 describe business-console` or the Aspire Dashboard resource page
to get the actual URL. Stop the platform with Aspire rather than killing `dotnet`,
AppHost, or DCP processes:

```powershell
.\nerv.ps1 stop
```

## Local observability

For normal local development, `.\nerv.ps1 dev` lets the Aspire AppHost inject the
OTLP endpoint for its own Dashboard. Do not override every project resource with a
custom `OTEL_EXPORTER_OTLP_ENDPOINT` in this path; doing so can leave the
Dashboard resource page healthy while the Structured logs, Traces, and Metrics
pages stay empty because telemetry was sent somewhere else.

The optional AppHost OpenTelemetry Collector path is only for Collector/Compose-like
testing:

```powershell
dotnet user-secrets set "Observability:UseCollector" "true" --project infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj
dotnet user-secrets set "Observability:AspireDashboardOtlpHttpEndpoint" "http://host.docker.internal:18890" --project infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj
```

When `Observability:UseCollector=true`, service telemetry is sent to the local
Collector resource by HTTP/protobuf. The Collector can then forward to a standalone
Aspire Dashboard OTLP/HTTP endpoint such as `http://host.docker.internal:18890`.
Leave this switch unset for the regular AppHost workflow.

After startup, telemetry can be checked without guessing from the UI:

```powershell
aspire otel logs
aspire otel traces
```

The standalone Aspire Dashboard is useful for development, PoC, and short-term
diagnostics. It stores telemetry in memory and must not be treated as the
production log retention or audit backend.

The same MinIO root user and password are passed to FileStorage as the local MinIO access key and secret key. If a future local profile provisions a separate MinIO service account, update both the AppHost parameter wiring and this document together.

These values are for local development only. Do not commit real credentials to `appsettings*.json`, source files or documentation examples.

## Runtime image versions

The AppHost pins the persistent local infrastructure image tags instead of using
provider defaults or `latest`:

| Resource | Current tag | Reason |
| --- | --- | --- |
| PostgreSQL | `18` | Uses the current PostgreSQL 18 major line while avoiding the unbounded `latest` tag. PostgreSQL Docker images 18+ use a major-version-specific data directory under `/var/lib/postgresql`, so the AppHost uses a new local dev volume, `nerv-iip-postgres-18`, instead of reusing the old 17-era `nerv-iip-postgres` volume. |
| Redis | `8` | Uses the current Redis 8 major line while avoiding unbounded `latest` drift. Redis 8 can read older local cache data, and if the cache volume is ever incompatible it can be recreated because Redis is not a local source-of-truth business store. |

Do not change these tags to `latest`. Moving to the next PostgreSQL or Redis major
version should be a deliberate upgrade issue with a clean-volume test,
preserved-volume migration test where applicable, AppHost build, Compose publish
verification, and startup smoke test. For PostgreSQL, either run an explicit
`pg_upgrade`/dump-restore plan or introduce a new dev volume name; do not let the
default container image decide that migration.

## Certificate preflight

Aspire AppHost, Dashboard/DCP, and local HTTPS development endpoints require the
local developer certificate to be trusted. `.\nerv.ps1 bootstrap -InstallMissing`
and `.\nerv.ps1 dev` now check this before starting the platform. If the check
fails, run:

```powershell
aspire certs trust
dotnet dev-certs https --trust
```

If the Aspire AppHost log reports a certificate name mismatch after an Aspire
upgrade or certificate cache change, reset the local Aspire certificate cache:

```powershell
aspire certs clean
aspire certs trust
dotnet dev-certs https --trust
```

Reference: [Aspire certificate configuration](https://aspire.dev/app-host/certificate-configuration/).

## Startup troubleshooting

When the Business Console appears to be slow or stuck, check the Aspire Dashboard before restarting repeatedly:

```powershell
dotnet user-secrets list --project infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj
.\nerv.ps1 status
.\nerv.ps1 describe business-console
.\nerv.ps1 describe business-gateway
.\nerv.ps1 describe gateway
.\nerv.ps1 logs gateway -Tail 120
.\nerv.ps1 logs business-gateway -Tail 120
.\nerv.ps1 wait gateway -Status up -TimeoutSeconds 600
```

Common symptoms and fixes:

| Symptom | Likely cause | Action |
| --- | --- | --- |
| Dashboard shows `Unresolved parameters` and services stay `Waiting`. | Required AppHost parameters are missing. | Set all `Parameters:*` user secrets above, then restart `.\nerv.ps1 dev`. |
| `127.0.0.1:5119` or `127.0.0.1:5125` refuses connections while Aspire says the resources are up. | The repo is running from a linked worktree and `.\nerv.ps1 dev` added Aspire `--isolated`, so host ports are dynamic. | Run `.\nerv.ps1 describe business-gateway` and `.\nerv.ps1 describe business-console` and use the URLs shown there. |
| Frontend port `5125` responds, but login or API calls hang. | A stale AppHost/DCP proxy or failed backend resource is present. | Run `.\nerv.ps1 status`, `.\nerv.ps1 describe -IncludeHidden`, then `.\nerv.ps1 stop` before starting again. Do not kill AppHost/DCP manually unless Aspire CLI cannot see a legacy process. |
| `postgres` exits immediately after an Aspire/provider upgrade. | The AppHost pulled a PostgreSQL image whose data-directory layout is incompatible with the existing PostgreSQL volume. | PostgreSQL 18 uses `nerv-iip-postgres-18`. Do not point it back at the old `nerv-iip-postgres` volume unless you are running an explicit migration. Inspect `.\nerv.ps1 logs postgres -Tail 120` before deleting any database volume. |
| `postgres` is `Running (Unhealthy)` and dependent services remain `Waiting`. | The persistent `nerv-iip-postgres-18` Docker volume was initialized with a different `postgres` password than the current Aspire `Parameters:postgres-password`. | Prefer reusing the existing user secret. If the secret changed and the volume must be preserved, align the container's `postgres` user password to the current secret. |
| `redis` exits with an RDB/AOF format error. | The local `nerv-iip-redis` cache volume was written by an incompatible Redis major version. | Redis is a local cache/session dependency, not a source-of-truth business store. Stop the platform and remove only `nerv-iip-redis`, then restart: `.\nerv.ps1 stop`; `docker volume rm nerv-iip-redis`; `.\nerv.ps1 dev`. |
| `.\nerv.ps1 dev` or `.\nerv.ps1 stop` appears stuck. | Aspire CLI or DCP did not return promptly. | Scripts now use bounded commands and print phase diagnostics. Check the latest `artifacts/script-logs/dev-apphost/` or `artifacts/script-logs/aspire-stop/` directory and rerun `.\nerv.ps1 status`. |
| `http://127.0.0.1:5102/api/iam/v1/me` returns `401`. | IAM is running and the request is unauthenticated. | This is expected before login. Use it as a quick liveness check. |
| `http://127.0.0.1:5119/swagger/v1/swagger.json` returns `200`. | BusinessGateway is up. | Business Console API proxying can be tested from `5125` after login. |

## Docker Compose artifacts

The AppHost includes the Aspire Docker Compose deployment target. Generate Compose
artifacts from the AppHost instead of maintaining a second full-platform compose model:

```powershell
.\nerv.ps1 publish-compose
```

The default output path is `artifacts/aspire-output/compose`. To prepare
environment-specific values and build images, use:

```powershell
.\nerv.ps1 prepare-compose -EnvironmentName Production
```

To let Aspire generate and apply the Docker Compose deployment in one step:

```powershell
.\nerv.ps1 deploy-compose -EnvironmentName Production
```

Current limitation: the Platform Console is published as an Aspire static website
with `/api` proxied to PlatformGateway. Business Console is still a development
Vite resource in Compose output until the AppHost models its two production API
routes (`/api/console` to PlatformGateway and `/api/business-console` to
BusinessGateway), or BusinessGateway owns the required auth facade. Do not treat a
Compose publish as a complete Business Console deployment until that production
serving model is added and verified.

For the PostgreSQL password-mismatch case, avoid deleting `nerv-iip-postgres-18`
unless losing local data is acceptable. To preserve data, temporarily relax
`pg_hba.conf` inside the dev container, reset the `postgres` password, then restore
the file immediately. If the password reset command fails, manually run the restore
command before doing anything else; otherwise the container can keep accepting local
connections without a password.

```powershell
$container = "<postgres-container-name>"
$password = "<current-Parameters:postgres-password>"
$escaped = $password.Replace("'", "''")
$pgData = "/var/lib/postgresql/18/docker"

docker exec -u root $container sh -lc "cp $pgData/pg_hba.conf /tmp/pg_hba.conf.codex-bak && sed -i 's/scram-sha-256/trust/g' $pgData/pg_hba.conf && chown postgres:postgres $pgData/pg_hba.conf && chmod 600 $pgData/pg_hba.conf && kill -HUP 1"
docker exec $container psql -U postgres -h 127.0.0.1 -d postgres -c "ALTER USER postgres WITH PASSWORD '$escaped';"
docker exec -u root $container sh -lc "cp /tmp/pg_hba.conf.codex-bak $pgData/pg_hba.conf && chown postgres:postgres $pgData/pg_hba.conf && chmod 600 $pgData/pg_hba.conf && kill -HUP 1"
docker exec -e PGPASSWORD=$password $container psql -U postgres -h 127.0.0.1 -d postgres -c "select current_user;"
```

After the database is healthy, the expected Business Console chain is:

1. `http://127.0.0.1:5125/login?redirect=/mes` returns the login page.
2. The BusinessGateway URL from `.\nerv.ps1 describe business-gateway` returns
   `200` at `/swagger/v1/swagger.json`.
3. `http://127.0.0.1:5102/api/iam/v1/me` returns `401` before login.
4. Login redirects to the Business Console URL from
   `.\nerv.ps1 describe business-console`, typically `/mes` after authentication.
