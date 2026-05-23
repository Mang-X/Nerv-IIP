# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Restores the backend solution once for child verify scripts
#     - Runs Equipment Reliability verify scripts when present
#     - Restores the Aspire AppHost project and builds it with --no-restore
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

Invoke-DotNet -Name "business-equipment-reliability-backend-restore" -WorkingDirectory $root -Arguments @(
    "restore",
    "backend/Nerv.IIP.sln"
) | Out-Null

$verifyScripts = @(
    "scripts/verify-business-industrial-telemetry-mvp.ps1",
    "scripts/verify-business-maintenance-mvp.ps1"
)

foreach ($verifyScript in $verifyScripts) {
    $scriptPath = Join-Path $root $verifyScript
    if (-not (Test-Path $scriptPath)) {
        throw "Required Equipment Reliability verify script is missing: $verifyScript"
    }

    $scriptName = [System.IO.Path]::GetFileNameWithoutExtension($verifyScript)
    Invoke-PwshScript -Name $scriptName -WorkingDirectory $root -ScriptPath $scriptPath -Arguments @(
        "-SkipRestore"
    ) | Out-Null
}

Invoke-DotNet -Name "business-equipment-reliability-apphost-restore" -WorkingDirectory $root -Arguments @(
    "restore",
    "infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj"
) | Out-Null

Invoke-DotNet -Name "business-equipment-reliability-apphost-build" -WorkingDirectory $root -Arguments @(
    "build",
    "infra/aspire/Nerv.IIP.AppHost/Nerv.IIP.AppHost.csproj",
    "--no-restore"
) | Out-Null

Write-Host "Business Equipment Reliability verified."
