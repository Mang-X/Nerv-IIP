# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Runs .NET restore and focused IndustrialTelemetry MVP test commands only
#     - When -PostgresConnectionString or NERV_IIP_TEST_POSTGRES is set, runs real PostgreSQL tests against disposable databases created by the test harness
#   Writes:
#     - bin/ and obj/ build outputs under tested .NET projects
#     - artifacts/script-logs/**
#   Cleanup:
#     - PostgreSQL test databases are dropped by the test harness
#   Requires:
#     - PowerShell 7
#     - .NET SDK 10

[CmdletBinding()]
param(
    [switch] $SkipRestore,
    [string] $PostgresConnectionString
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root
. (Join-Path $root "scripts/lib/ScriptAutomation.ps1")

if (-not $SkipRestore) {
    Invoke-DotNet -Name "business-industrial-telemetry-restore" -WorkingDirectory $root -Arguments @(
        "restore",
        "backend/Nerv.IIP.sln"
    ) | Out-Null
}

Invoke-DotNet -Name "business-industrial-telemetry-domain-tests" -WorkingDirectory $root -Arguments @(
    "test",
    "backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Domain.Tests/Nerv.IIP.Business.IndustrialTelemetry.Domain.Tests.csproj",
    "--no-restore"
) | Out-Null

Invoke-DotNet -Name "business-industrial-telemetry-web-tests" -WorkingDirectory $root -Arguments @(
    "test",
    "backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests.csproj",
    "--no-restore"
) | Out-Null

$effectivePostgresConnectionString = $PostgresConnectionString
if ([string]::IsNullOrWhiteSpace($effectivePostgresConnectionString)) {
    $effectivePostgresConnectionString = [Environment]::GetEnvironmentVariable("NERV_IIP_TEST_POSTGRES", "Process")
}

if (-not [string]::IsNullOrWhiteSpace($effectivePostgresConnectionString)) {
    Use-ScopedEnvironmentVariable -Name "NERV_IIP_TEST_POSTGRES" -Value $effectivePostgresConnectionString -ScriptBlock {
        Invoke-DotNet -Name "business-industrial-telemetry-postgres-tests" -WorkingDirectory $root -Arguments @(
            "test",
            "backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests.csproj",
            "--no-restore",
            "--filter",
            "FullyQualifiedName~Postgres_"
        ) | Out-Null
    }

    Write-Host "Business IndustrialTelemetry PostgreSQL regressions verified."
}

Write-Host "Business IndustrialTelemetry MVP verified."
