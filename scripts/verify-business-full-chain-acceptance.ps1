# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Restores the #77 business full-chain acceptance harness test project unless -SkipRestore is set
#     - Runs the #77 business full-chain acceptance harness tests, including correlation, event recorder, and HTTP envelope helpers
#     - Does not start real service hosts, Docker, or PostgreSQL
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
    )
}

$testArguments = @(
    "test",
    $acceptanceProject,
    "--no-restore"
)

Invoke-DotNet -Name "business-full-chain-acceptance-test" -WorkingDirectory $root -Arguments $testArguments

Write-Host "Business full-chain acceptance #77 harness verified: contract surface, acceptance fixture, event recorder, and HTTP response envelope helpers are ready for real chain tests."
