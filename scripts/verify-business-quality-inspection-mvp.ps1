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

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root
. (Join-Path $root "scripts/lib/ScriptAutomation.ps1")

Invoke-DotNet -Name "business-quality-restore" -WorkingDirectory $root -Arguments @(
    "restore",
    "backend/Nerv.IIP.sln"
) | Out-Null

Invoke-DotNet -Name "business-quality-domain-tests" -WorkingDirectory $root -Arguments @(
    "test",
    "backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Domain.Tests/Nerv.IIP.Business.Quality.Domain.Tests.csproj",
    "--no-restore"
) | Out-Null

Invoke-DotNet -Name "business-quality-web-tests" -WorkingDirectory $root -Arguments @(
    "test",
    "backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Web.Tests/Nerv.IIP.Business.Quality.Web.Tests.csproj",
    "--no-restore"
) | Out-Null

Invoke-DotNet -Name "business-quality-contract-tests" -WorkingDirectory $root -Arguments @(
    "test",
    "backend/tests/Nerv.IIP.Contracts.Quality.Tests/Nerv.IIP.Contracts.Quality.Tests.csproj",
    "--no-restore"
) | Out-Null

Invoke-DotNet -Name "iam-quality-seed-tests" -WorkingDirectory $root -Arguments @(
    "test",
    "backend/services/Iam/tests/Nerv.IIP.Iam.Web.Tests/Nerv.IIP.Iam.Web.Tests.csproj",
    "--no-restore",
    "--filter",
    "FullyQualifiedName~In_memory_role_management_creates_role_updates_permissions_and_lists_catalog"
) | Out-Null

Write-Host "Business quality inspection MVP verified."
