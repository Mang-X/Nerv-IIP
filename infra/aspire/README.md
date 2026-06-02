# Aspire local development

`Nerv.IIP.AppHost` is the local full-platform topology for Gateway, AppHub, IAM, Ops, FileStorage, Connector Host, Business Gateway, Business Console and shared infrastructure.

The AppHost intentionally does not hardcode local secrets. `.\nerv.ps1 dev` checks the required AppHost user secrets before startup and fails fast when any are missing. This prevents Aspire from silently generating new random values that no longer match existing Docker volumes. Store repeatable local values in the AppHost user secrets store:

On a connected blank development machine, prefer the repository bootstrap entry first:

```powershell
.\nerv.ps1 bootstrap -InstallMissing
```

That command checks the required toolchain, installs missing Windows prerequisites
through `winget` when requested, initializes missing local Development user secrets,
restores backend/frontend dependencies, and builds the AppHost. Docker Desktop may
still need to be started manually after first installation. The manual commands
below are for cases where you intentionally want to set repeatable local values
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
`--isolated` when the repository is opened from a linked worktree. Stop the platform
with Aspire rather than killing `dotnet`, AppHost, or DCP processes:

```powershell
.\nerv.ps1 stop
```

The same MinIO root user and password are passed to FileStorage as the local MinIO access key and secret key. If a future local profile provisions a separate MinIO service account, update both the AppHost parameter wiring and this document together.

These values are for local development only. Do not commit real credentials to `appsettings*.json`, source files or documentation examples.

## Startup troubleshooting

When the Business Console appears to be slow or stuck, check the Aspire Dashboard before restarting repeatedly:

```powershell
dotnet user-secrets list --project infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj
.\nerv.ps1 status
.\nerv.ps1 describe gateway
.\nerv.ps1 logs gateway -Tail 120
.\nerv.ps1 logs business-gateway -Tail 120
.\nerv.ps1 wait gateway -Status up -TimeoutSeconds 600
```

Common symptoms and fixes:

| Symptom | Likely cause | Action |
| --- | --- | --- |
| Dashboard shows `Unresolved parameters` and services stay `Waiting`. | Required AppHost parameters are missing. | Set all `Parameters:*` user secrets above, then restart `.\nerv.ps1 dev`. |
| Frontend port `5125` responds, but login or API calls hang. | A stale AppHost/DCP proxy or failed backend resource is present. | Run `.\nerv.ps1 status`, `.\nerv.ps1 describe -IncludeHidden`, then `.\nerv.ps1 stop` before starting again. Do not kill AppHost/DCP manually unless Aspire CLI cannot see a legacy process. |
| `postgres` is `Running (Unhealthy)` and dependent services remain `Waiting`. | The persistent `nerv-iip-postgres` Docker volume was initialized with a different `postgres` password than the current Aspire `Parameters:postgres-password`. | Prefer reusing the existing user secret. If the secret changed and the volume must be preserved, align the container's `postgres` user password to the current secret. |
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

For the PostgreSQL password-mismatch case, avoid deleting `nerv-iip-postgres` unless losing local data is acceptable. To preserve data, temporarily relax `pg_hba.conf` inside the dev container, reset the `postgres` password, then restore the file immediately. If the password reset command fails, manually run the restore command before doing anything else; otherwise the container can keep accepting local connections without a password.

```powershell
$container = "<postgres-container-name>"
$password = "<current-Parameters:postgres-password>"
$escaped = $password.Replace("'", "''")

docker exec -u root $container sh -lc "cp /var/lib/postgresql/data/pg_hba.conf /tmp/pg_hba.conf.codex-bak && sed -i 's/scram-sha-256/trust/g' /var/lib/postgresql/data/pg_hba.conf && chown postgres:postgres /var/lib/postgresql/data/pg_hba.conf && chmod 600 /var/lib/postgresql/data/pg_hba.conf && kill -HUP 1"
docker exec $container psql -U postgres -h 127.0.0.1 -d postgres -c "ALTER USER postgres WITH PASSWORD '$escaped';"
docker exec -u root $container sh -lc "cp /tmp/pg_hba.conf.codex-bak /var/lib/postgresql/data/pg_hba.conf && chown postgres:postgres /var/lib/postgresql/data/pg_hba.conf && chmod 600 /var/lib/postgresql/data/pg_hba.conf && kill -HUP 1"
docker exec -e PGPASSWORD=$password $container psql -U postgres -h 127.0.0.1 -d postgres -c "select current_user;"
```

After the database is healthy, the expected Business Console chain is:

1. `http://127.0.0.1:5125/login?redirect=/mes` returns the login page.
2. `http://127.0.0.1:5119/swagger/v1/swagger.json` returns `200`.
3. `http://127.0.0.1:5102/api/iam/v1/me` returns `401` before login.
4. Login redirects to `http://127.0.0.1:5125/mes`.
