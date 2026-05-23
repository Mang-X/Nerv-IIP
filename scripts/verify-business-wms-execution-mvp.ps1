# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Runs .NET restore and focused WMS execution MVP test commands only
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
    Invoke-DotNet -Name "business-wms-restore" -WorkingDirectory $root -Arguments @(
        "restore",
        "backend/Nerv.IIP.sln"
    ) | Out-Null
}

Invoke-DotNet -Name "business-wms-domain-tests" -WorkingDirectory $root -Arguments @(
    "test",
    "backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Domain.Tests/Nerv.IIP.Business.Wms.Domain.Tests.csproj",
    "--no-restore"
) | Out-Null

Invoke-DotNet -Name "business-wms-web-tests" -WorkingDirectory $root -Arguments @(
    "test",
    "backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Web.Tests/Nerv.IIP.Business.Wms.Web.Tests.csproj",
    "--no-restore"
) | Out-Null

Write-Host "Business WMS execution MVP verified."
