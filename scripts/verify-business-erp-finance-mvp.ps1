# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Runs .NET restore and focused ERP Finance MVP test commands only
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
    Invoke-DotNet -Name "business-erp-finance-restore" -WorkingDirectory $root -Arguments @(
        "restore",
        "backend/Nerv.IIP.sln"
    ) | Out-Null
}

Invoke-DotNet -Name "business-erp-finance-domain-tests" -WorkingDirectory $root -Arguments @(
    "test",
    "backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests/Nerv.IIP.Business.Erp.Domain.Tests.csproj",
    "--no-restore",
    "--filter",
    "FullyQualifiedName~ErpSalesFinanceAggregateTests"
) | Out-Null

Invoke-DotNet -Name "business-erp-finance-web-tests" -WorkingDirectory $root -Arguments @(
    "test",
    "backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/Nerv.IIP.Business.Erp.Web.Tests.csproj",
    "--no-restore",
    "--filter",
    "FullyQualifiedName~ErpSalesFinanceEndpointContractTests"
) | Out-Null

Write-Host "Business ERP Finance MVP verified."
