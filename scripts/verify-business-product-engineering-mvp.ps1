# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Runs .NET restore, build, and test commands only
#   Writes:
#     - bin/ and obj/ build outputs under tested .NET projects
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
    Invoke-DotNet -Name "business-product-engineering-restore" -WorkingDirectory $root -Arguments @(
        "restore",
        "backend/Nerv.IIP.sln"
    ) | Out-Null
}

Invoke-DotNet -Name "business-product-engineering-domain-tests" -WorkingDirectory $root -Arguments @(
    "test",
    "backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests/Nerv.IIP.Business.ProductEngineering.Domain.Tests.csproj",
    "--no-restore"
) | Out-Null

Invoke-DotNet -Name "business-product-engineering-web-tests" -WorkingDirectory $root -Arguments @(
    "test",
    "backend/services/Business/ProductEngineering/tests/Nerv.IIP.Business.ProductEngineering.Web.Tests/Nerv.IIP.Business.ProductEngineering.Web.Tests.csproj",
    "--no-restore"
) | Out-Null

Invoke-DotNet -Name "business-product-engineering-contract-tests" -WorkingDirectory $root -Arguments @(
    "test",
    "backend/tests/Nerv.IIP.Contracts.ProductEngineering.Tests/Nerv.IIP.Contracts.ProductEngineering.Tests.csproj",
    "--no-restore"
) | Out-Null

if (-not $SkipRestore) {
    Invoke-DotNet -Name "business-product-engineering-apphost-restore" -WorkingDirectory $root -Arguments @(
        "restore",
        "infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj"
    ) | Out-Null

    Invoke-DotNet -Name "business-product-engineering-apphost-build" -WorkingDirectory $root -Arguments @(
        "build",
        "infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj",
        "--no-restore"
    ) | Out-Null
}

Write-Host "Business ProductEngineering MVP verified."
