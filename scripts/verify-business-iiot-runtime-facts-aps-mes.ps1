# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Runs .NET restore and focused #207 Business IIoT runtime facts / APS / MES test commands unless -SkipRestore is set
#     - Regenerates frontend api-client from committed Gateway OpenAPI snapshots unless -SkipFrontend is set
#     - Runs Business Console typecheck, unit tests, and production build unless -SkipFrontend is set
#   Writes:
#     - bin/ and obj/ build outputs under tested .NET projects
#     - frontend/packages/api-client/src/generated/**
#     - frontend package build outputs
#     - artifacts/script-logs/**
#   Cleanup:
#     - None required
#   Requires:
#     - PowerShell 7
#     - .NET SDK 10
#     - Node.js >=22.18.0
#     - pnpm 11.1.2

[CmdletBinding()]
param(
    [switch] $SkipRestore,
    [switch] $SkipFrontend
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root
. (Join-Path $root "scripts/lib/ScriptAutomation.ps1")

if (-not $SkipRestore) {
    Invoke-DotNet -Name "business-iiot-runtime-backend-restore" -WorkingDirectory $root -Arguments @(
        "restore",
        "backend/Nerv.IIP.sln"
    ) | Out-Null
}

$backendTestProjects = @(
    @{
        Name = "business-iiot-runtime-contract-tests"
        Project = "backend/tests/Nerv.IIP.Contracts.EquipmentRuntime.Tests/Nerv.IIP.Contracts.EquipmentRuntime.Tests.csproj"
    },
    @{
        Name = "business-iiot-runtime-industrial-telemetry-tests"
        Project = "backend/services/Business/IndustrialTelemetry/tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests/Nerv.IIP.Business.IndustrialTelemetry.Web.Tests.csproj"
    },
    @{
        Name = "business-iiot-runtime-maintenance-tests"
        Project = "backend/services/Business/Maintenance/tests/Nerv.IIP.Business.Maintenance.Web.Tests/Nerv.IIP.Business.Maintenance.Web.Tests.csproj"
    },
    @{
        Name = "business-iiot-runtime-scheduling-tests"
        Project = "backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/Nerv.IIP.Business.Scheduling.Web.Tests.csproj"
    },
    @{
        Name = "business-iiot-runtime-mes-tests"
        Project = "backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj"
    },
    @{
        Name = "business-iiot-runtime-business-gateway-tests"
        Project = "backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj"
    }
)

foreach ($testProject in $backendTestProjects) {
    Invoke-DotNet -Name $testProject.Name -WorkingDirectory $root -Arguments @(
        "test",
        $testProject.Project,
        "--no-restore"
    ) | Out-Null
}

if (-not $SkipFrontend) {
    Invoke-Pnpm -Name "business-iiot-runtime-generate-api" -WorkingDirectory $root -Arguments @(
        "-C",
        "frontend",
        "generate:api"
    ) | Out-Null

    Invoke-Pnpm -Name "business-iiot-runtime-business-console-typecheck" -WorkingDirectory $root -Arguments @(
        "-C",
        "frontend",
        "--filter",
        "@nerv-iip/business-console",
        "typecheck"
    ) | Out-Null

    Invoke-Pnpm -Name "business-iiot-runtime-business-console-test" -WorkingDirectory $root -Arguments @(
        "-C",
        "frontend",
        "--filter",
        "@nerv-iip/business-console",
        "test"
    ) | Out-Null

    Invoke-Pnpm -Name "business-iiot-runtime-business-console-build" -WorkingDirectory $root -Arguments @(
        "-C",
        "frontend",
        "--filter",
        "@nerv-iip/business-console",
        "build"
    ) | Out-Null
}

Write-Host "Business IIoT runtime facts APS/MES verified."
