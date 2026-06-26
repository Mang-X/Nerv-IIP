# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Validates production deployment Compose artifacts without starting containers
#   Writes:
#     - artifacts/script-logs/**
#   Cleanup:
#     - Restores scoped environment variables after compose config validation
#   Requires:
#     - PowerShell 7
#     - Docker CLI with compose plugin

[CmdletBinding()]
param(
    [switch] $SkipDockerComposeConfig
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root
. (Join-Path $root "scripts/lib/ScriptAutomation.ps1")

$requiredFiles = @(
    "infra/docker/dotnet-service.Dockerfile",
    "infra/docker/vite-spa.Dockerfile",
    "infra/docker/nginx-spa.conf",
    "infra/compose/nerv-iip.dependencies.yml",
    "infra/compose/nerv-iip.platform.yml",
    "infra/postgres/init-nerv-iip-databases.sql",
    "scripts/verify-production-release-rehearsal.ps1",
    "scripts/install/start-nerv-iip-apphost.ps1"
)

foreach ($file in $requiredFiles) {
    if (-not (Test-Path $file)) {
        throw "Missing deployment artifact: $file"
    }
}

$nonRootDockerfiles = @(
    "backend/services/Business/DemandPlanning/src/Nerv.IIP.Business.DemandPlanning.Web/Dockerfile",
    "backend/services/Business/IndustrialTelemetry/src/Nerv.IIP.Business.IndustrialTelemetry.Web/Dockerfile",
    "backend/services/Business/MasterData/src/Nerv.IIP.Business.MasterData.Web/Dockerfile",
    "backend/services/Business/Scheduling/src/Nerv.IIP.Business.Scheduling.Web/Dockerfile",
    "backend/services/Business/Wms/src/Nerv.IIP.Business.Wms.Web/Dockerfile",
    "infra/docker/dotnet-service.Dockerfile",
    "infra/docker/vite-spa.Dockerfile"
)

foreach ($dockerfile in $nonRootDockerfiles) {
    $dockerfileContent = Get-Content -Raw $dockerfile
    if ($dockerfileContent -notmatch "(?m)^\s*USER\s+appuser\s*(?:#.*)?$") {
        throw "Dockerfile must run as the dedicated non-root appuser: $dockerfile"
    }
}

$viteSpaDockerfile = Get-Content -Raw "infra/docker/vite-spa.Dockerfile"
if ($viteSpaDockerfile -notmatch "pid\s+/tmp/nginx\.pid") {
    throw "Vite SPA Dockerfile must move the nginx PID file to /tmp for non-root runtime."
}

if ($viteSpaDockerfile -match "/var/run") {
    throw "Vite SPA Dockerfile must not chown /var/run for non-root nginx runtime."
}

$ciWorkflow = Get-Content -Raw ".github/workflows/ci.yml"
if ($ciWorkflow -match "(?m)^\s*uses:\s+pnpm/action-setup@(?![0-9a-f]{40}(?:\s+#.*)?\s*$).+") {
    throw "GitHub Actions workflow must pin pnpm/action-setup to a full commit SHA."
}

if (-not $SkipDockerComposeConfig) {
    $environment = @{
        NERV_IIP_POSTGRES_PASSWORD = "postgres-password-32chars-test"
        NERV_IIP_MINIO_ROOT_USER = "minioadmin"
        NERV_IIP_MINIO_ROOT_PASSWORD = "minio-password-32chars-test"
        NERV_IIP_INTERNAL_SERVICE_BEARER_TOKEN = "internal-token-32chars-test-value"
        NERV_IIP_CONNECTOR_HOST_ID = "connector-host-001"
        NERV_IIP_CONNECTOR_HOST_ORGANIZATION_ID = "org-001"
        NERV_IIP_CONNECTOR_HOST_ENVIRONMENT_ID = "env-dev"
        NERV_IIP_CONNECTOR_HOST_SECRET = "connector-secret-32chars-test-value"
        NERV_IIP_CONNECTOR_INGESTION_TOKEN_SIGNING_KEY = "ingestion-signing-key-32chars-test-value"
        NERV_IIP_IAM_JWT_SIGNING_KEY_ID = "deployment-test-rsa-key"
        NERV_IIP_IAM_JWT_PRIVATE_KEY_PEM = "deployment-test-private-key-pem"
        NERV_IIP_IAM_JWT_JWKS_JSON = '{"keys":[{"kty":"RSA","use":"sig","kid":"deployment-test-rsa-key","alg":"RS256","n":"deployment-test-modulus","e":"AQAB"}]}'
        NERV_IIP_CORS_ALLOWED_ORIGINS = "https://console.example.test,https://business.example.test"
    }

    Invoke-WithScopedEnvironment -Variables $environment -ScriptBlock {
        Invoke-DockerCompose -Name "production-deployment-compose-config" -WorkingDirectory $root -Arguments @(
            "-f",
            "infra/compose/nerv-iip.dependencies.yml",
            "-f",
            "infra/compose/nerv-iip.platform.yml",
            "config",
            "--quiet"
        ) | Out-Null
    }
}

Write-Diagnostic "Production deployment artifacts verified."
