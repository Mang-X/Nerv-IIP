# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Runs .NET restore and test commands only
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
    Invoke-DotNet -Name "business-masterdata-restore" -WorkingDirectory $root -Arguments @(
        "restore",
        "backend/Nerv.IIP.sln"
    ) | Out-Null
}

Invoke-DotNet -Name "business-masterdata-domain-tests" -WorkingDirectory $root -Arguments @(
    "test",
    "backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Domain.Tests/Nerv.IIP.Business.MasterData.Domain.Tests.csproj",
    "--no-restore"
) | Out-Null

Invoke-DotNet -Name "business-masterdata-web-tests" -WorkingDirectory $root -Arguments @(
    "test",
    "backend/services/Business/MasterData/tests/Nerv.IIP.Business.MasterData.Web.Tests/Nerv.IIP.Business.MasterData.Web.Tests.csproj",
    "--no-restore"
) | Out-Null

Invoke-DotNet -Name "iam-foundation-tests" -WorkingDirectory $root -Arguments @(
    "test",
    "backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj",
    "--no-restore",
    "--filter",
    "FullyQualifiedName~IamFoundationTests"
) | Out-Null

Write-Host "Business master data realignment verified."
