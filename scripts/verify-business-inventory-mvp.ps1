# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Runs .NET restore and focused Inventory MVP test commands only
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
    Invoke-DotNet -Name "business-inventory-restore" -WorkingDirectory $root -Arguments @(
        "restore",
        "backend/Nerv.IIP.sln"
    ) | Out-Null
}

Invoke-DotNet -Name "business-inventory-domain-tests" -WorkingDirectory $root -Arguments @(
    "test",
    "backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Domain.Tests/Nerv.IIP.Business.Inventory.Domain.Tests.csproj",
    "--no-restore"
) | Out-Null

Invoke-DotNet -Name "business-inventory-web-tests" -WorkingDirectory $root -Arguments @(
    "test",
    "backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/Nerv.IIP.Business.Inventory.Web.Tests.csproj",
    "--no-restore"
) | Out-Null

Write-Host "Business Inventory MVP verified."
