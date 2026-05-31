# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Runs .NET restore and focused BusinessScheduling APS lite / BusinessGateway facade test/build commands only
#   Writes:
#     - bin/ and obj/ build outputs under tested .NET projects
#     - artifacts/script-logs/**
#   Cleanup:
#     - None required
#   Requires:
#     - PowerShell 7
#     - .NET SDK 10

[CmdletBinding()]
param(
    [switch] $SkipRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root
. (Join-Path $root "scripts/lib/ScriptAutomation.ps1")

if (-not $SkipRestore) {
    Invoke-DotNet -Name "business-scheduling-restore" -WorkingDirectory $root -Arguments @(
        "restore",
        "backend/Nerv.IIP.sln"
    ) | Out-Null

    Invoke-DotNet -Name "business-scheduling-apphost-restore" -WorkingDirectory $root -Arguments @(
        "restore",
        "infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj"
    ) | Out-Null
}

Invoke-DotNet -Name "business-scheduling-contract-tests" -WorkingDirectory $root -Arguments @(
    "test",
    "backend/tests/Nerv.IIP.Contracts.Scheduling.Tests/Nerv.IIP.Contracts.Scheduling.Tests.csproj",
    "--no-restore"
) | Out-Null

Invoke-DotNet -Name "business-scheduling-domain-tests" -WorkingDirectory $root -Arguments @(
    "test",
    "backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Domain.Tests/Nerv.IIP.Business.Scheduling.Domain.Tests.csproj",
    "--no-restore"
) | Out-Null

Invoke-DotNet -Name "business-scheduling-web-tests" -WorkingDirectory $root -Arguments @(
    "test",
    "backend/services/Business/Scheduling/tests/Nerv.IIP.Business.Scheduling.Web.Tests/Nerv.IIP.Business.Scheduling.Web.Tests.csproj",
    "--no-restore"
) | Out-Null

Invoke-DotNet -Name "business-scheduling-gateway-facade-tests" -WorkingDirectory $root -Arguments @(
    "test",
    "backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj",
    "--no-restore",
    "--filter",
    "FullyQualifiedName~Scheduling"
) | Out-Null

Invoke-DotNet -Name "business-scheduling-gateway-openapi-tests" -WorkingDirectory $root -Arguments @(
    "test",
    "backend/gateway/BusinessGateway/tests/Nerv.IIP.BusinessGateway.Web.Tests/Nerv.IIP.BusinessGateway.Web.Tests.csproj",
    "--no-restore",
    "--filter",
    "FullyQualifiedName~BusinessGatewayOpenApiTests"
) | Out-Null

Invoke-DotNet -Name "business-scheduling-apphost-build" -WorkingDirectory $root -Arguments @(
    "build",
    "infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj",
    "--no-restore"
) | Out-Null

Write-Host "Business Scheduling APS lite verified."
