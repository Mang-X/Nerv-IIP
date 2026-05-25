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
    "scripts/install/start-nerv-iip-apphost.ps1"
)

foreach ($file in $requiredFiles) {
    if (-not (Test-Path $file)) {
        throw "Missing deployment artifact: $file"
    }
}

if (-not $SkipDockerComposeConfig) {
    $environment = @{
        NERV_IIP_POSTGRES_PASSWORD = "postgres-password-32chars-test"
        NERV_IIP_MINIO_ROOT_USER = "minioadmin"
        NERV_IIP_MINIO_ROOT_PASSWORD = "minio-password-32chars-test"
        NERV_IIP_INTERNAL_SERVICE_BEARER_TOKEN = "internal-token-32chars-test-value"
        NERV_IIP_CONNECTOR_HOST_SECRET = "connector-secret-32chars-test-value"
        NERV_IIP_IAM_JWT_SIGNING_KEY = "iam-jwt-signing-key-32chars-test-value"
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
        )
    }
}

Write-Diagnostic "Production deployment artifacts verified."
