# Aspire local development

`Nerv.IIP.AppHost` is the local full-platform topology for Gateway, AppHub, IAM, Ops, FileStorage, Connector Host, Console and shared infrastructure.

The AppHost intentionally does not hardcode local secrets. On first startup, Aspire can prompt for missing secret parameters. For repeatable local development, store them in the AppHost user secrets store:

```powershell
dotnet user-secrets set "Parameters:iam-jwt-signing-key" "<at-least-32-byte-local-signing-key>" --project infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj
dotnet user-secrets set "Parameters:minio-root-user" "<local-minio-user>" --project infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj
dotnet user-secrets set "Parameters:minio-root-password" "<local-minio-password>" --project infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj
dotnet user-secrets set "Parameters:iam-seed-admin-password" "<local-admin-password>" --project infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj
dotnet user-secrets set "Parameters:iam-seed-connector-host-secret" "<local-connector-secret>" --project infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj
```

Then start the platform from the repository root:

```powershell
.\nerv.ps1 dev
```

The same MinIO root user and password are passed to FileStorage as the local MinIO access key and secret key. If a future local profile provisions a separate MinIO service account, update both the AppHost parameter wiring and this document together.

These values are for local development only. Do not commit real credentials to `appsettings*.json`, source files or documentation examples.
