# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Runs .NET restore and focused ERP Procurement MVP test commands only
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
    Invoke-DotNet -Name "business-erp-procurement-restore" -WorkingDirectory $root -Arguments @(
        "restore",
        "backend/Nerv.IIP.sln"
    ) | Out-Null
}

Invoke-DotNet -Name "business-erp-procurement-domain-tests" -WorkingDirectory $root -Arguments @(
    "test",
    "backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Domain.Tests/Nerv.IIP.Business.Erp.Domain.Tests.csproj",
    "--no-restore",
    "--filter",
    "FullyQualifiedName~ErpProcurementAggregateTests"
) | Out-Null

Invoke-DotNet -Name "business-erp-procurement-web-tests" -WorkingDirectory $root -Arguments @(
    "test",
    "backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/Nerv.IIP.Business.Erp.Web.Tests.csproj",
    "--no-restore",
    "--filter",
    "FullyQualifiedName~ErpProcurement"
) | Out-Null

Write-Host "Business ERP Procurement MVP verified."
