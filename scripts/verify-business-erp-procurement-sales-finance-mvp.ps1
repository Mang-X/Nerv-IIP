# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Restores the backend solution once for ERP verify scripts
#     - Runs ERP Procurement, Sales and Finance focused verify scripts
#     - Restores and builds the Aspire AppHost project
#   Writes:
#     - bin/ and obj/ build outputs under verified .NET projects
#     - artifacts/script-logs/**
#   Cleanup:
#     - Nested scripts clean up their own managed resources
#   Requires:
#     - PowerShell 7
#     - .NET SDK 10

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root
. (Join-Path $root "scripts/lib/ScriptAutomation.ps1")

Invoke-DotNet -Name "business-erp-backend-restore" -WorkingDirectory $root -Arguments @(
    "restore",
    "backend/Nerv.IIP.sln"
) | Out-Null

$verifyScripts = @(
    "scripts/verify-business-erp-procurement-mvp.ps1",
    "scripts/verify-business-erp-sales-mvp.ps1",
    "scripts/verify-business-erp-finance-mvp.ps1"
)

foreach ($verifyScript in $verifyScripts) {
    $scriptPath = Join-Path $root $verifyScript
    if (-not (Test-Path $scriptPath)) {
        throw "Required ERP verify script is missing: $verifyScript"
    }

    $scriptName = [System.IO.Path]::GetFileNameWithoutExtension($verifyScript)
    Invoke-PwshScript -Name $scriptName -WorkingDirectory $root -ScriptPath $scriptPath -Arguments @(
        "-SkipRestore"
    ) | Out-Null
}

Invoke-DotNet -Name "business-erp-apphost-restore" -WorkingDirectory $root -Arguments @(
    "restore",
    "infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj"
) | Out-Null

Invoke-DotNet -Name "business-erp-apphost-build" -WorkingDirectory $root -Arguments @(
    "build",
    "infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj",
    "--no-restore"
) | Out-Null

Write-Host "Business ERP Procurement Sales Finance MVP verified."
