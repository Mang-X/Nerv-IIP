# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Runs .NET restore and focused MES execution domain/web test commands only
#     - Reads api-client generated types.gen.ts and stable barrel for the facade-coverage assertion
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
    Invoke-DotNet -Name "business-mes-restore" -WorkingDirectory $root -Arguments @(
        "restore",
        "backend/Nerv.IIP.sln"
    ) | Out-Null
}

Invoke-DotNet -Name "business-mes-domain-tests" -WorkingDirectory $root -Arguments @(
    "test",
    "backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Domain.Tests/Nerv.IIP.Business.Mes.Domain.Tests.csproj",
    "--no-restore"
) | Out-Null

Invoke-DotNet -Name "business-mes-web-tests" -WorkingDirectory $root -Arguments @(
    "test",
    "backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj",
    "--no-restore"
) | Out-Null

# Facade-coverage (MAN-475 / #841) sample: MES work-order hold/cancel governance is
# declared `exposed` in facade-coverage-matrix.json (#833 backfill). Assert the two-hop
# delivery — the generated type is queryable in types.gen.ts and re-exported from the
# stable barrel — so a dropped facade type/export fails this focused gate, not only CI.
Assert-FacadeTypesGenExport `
    -Surface "business-console" `
    -TypeName @("HoldBusinessConsoleMesWorkOrderData", "CancelBusinessConsoleMesWorkOrderData") `
    -ExportName @("holdBusinessConsoleMesWorkOrder", "cancelBusinessConsoleMesWorkOrder")

Write-Host "Business MES persistence and execution MVP verified."
