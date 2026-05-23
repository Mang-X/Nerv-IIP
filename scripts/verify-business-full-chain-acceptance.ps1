# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Restores the #77 business full-chain acceptance baseline test project unless -SkipRestore is set
#     - Runs the #77 business full-chain acceptance baseline tests
#     - Does not replace the final #77 full-chain HTTP acceptance gate
#   Writes:
#     - bin/ and obj/ build outputs under verified .NET projects
#     - artifacts/script-logs/**
#   Cleanup:
#     - No long-running services or external dependencies are started
#   Requires:
#     - PowerShell 7
#     - .NET SDK 10

param(
    [switch]$SkipRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root
. (Join-Path $root "scripts/lib/ScriptAutomation.ps1")

$acceptanceProject = "backend/tests/Nerv.IIP.Business.Acceptance.Tests/Nerv.IIP.Business.Acceptance.Tests.csproj"

if (-not $SkipRestore) {
    Invoke-DotNet -Name "business-full-chain-acceptance-restore" -WorkingDirectory $root -Arguments @(
        "restore",
        $acceptanceProject
    ) | Out-Null
}

$testArguments = @(
    "test",
    $acceptanceProject,
    "--no-restore"
)

Invoke-DotNet -Name "business-full-chain-acceptance-test" -WorkingDirectory $root -Arguments $testArguments | Out-Null

Write-Host "Business full-chain acceptance #77 harness baseline verified. This is not the final #77 full-chain HTTP acceptance gate."
