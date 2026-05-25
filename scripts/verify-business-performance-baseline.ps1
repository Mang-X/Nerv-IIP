# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Runs focused business performance baseline tests against the configured PostgreSQL database
#     - Applies EF Core migrations for Inventory, MES, and ERP schemas as required by selected tests
#     - Writes small opt-in baseline rows for the Inventory high-write scenario
#   Writes:
#     - bin/ and obj/ build outputs under tested .NET projects
#     - artifacts/script-logs/**
#   Cleanup:
#     - Restores scoped environment variables after the test command finishes
#   Requires:
#     - PowerShell 7
#     - .NET SDK 10
#     - PostgreSQL database reachable from NERV_IIP_PERF_POSTGRES or -ConnectionString

[CmdletBinding()]
param(
    [string] $ConnectionString,

    [ValidateSet("inventory", "mes", "erp", "all")]
    [string] $Scenario = "all",

    [string] $Profile = "local-baseline",

    [int] $Rows = 25,

    [switch] $SkipRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root
. (Join-Path $root "scripts/lib/ScriptAutomation.ps1")

$effectiveConnectionString = if (-not [string]::IsNullOrWhiteSpace($ConnectionString)) {
    $ConnectionString
}
else {
    [Environment]::GetEnvironmentVariable("NERV_IIP_PERF_POSTGRES", "Process")
}

if ([string]::IsNullOrWhiteSpace($effectiveConnectionString)) {
    throw "Set -ConnectionString or NERV_IIP_PERF_POSTGRES to run the opt-in PostgreSQL performance baseline. Use a disposable/perf database; InMemory baselines are intentionally unsupported."
}

if ($Rows -le 0 -or $Rows -gt 500) {
    throw "-Rows must be between 1 and 500. This script is a skeleton baseline, not a load test runner."
}

$project = "backend/tests/Nerv.IIP.Business.Performance.Tests/Nerv.IIP.Business.Performance.Tests.csproj"
$testArguments = @("test", $project, "--logger", "console;verbosity=normal")

if ($SkipRestore) {
    $testArguments += "--no-restore"
}

if ($Scenario -ne "all") {
    $testArguments += @("--filter", "Category=$Scenario")
}

$environment = @{
    NERV_IIP_PERF_POSTGRES = $effectiveConnectionString
    NERV_IIP_PERF_SCENARIO = $Scenario
    NERV_IIP_PERF_PROFILE = $Profile
    NERV_IIP_PERF_ROWS = $Rows.ToString()
}

Write-Diagnostic "Running business performance baseline scenario=$Scenario profile=$Profile rows=$Rows."

Invoke-WithScopedEnvironment -Variables $environment -ScriptBlock {
    Invoke-DotNet -Name "business-performance-baseline-tests" -WorkingDirectory $root -Arguments $testArguments | Out-Null
}

Write-Host "Business performance baseline verified for scenario '$Scenario'."
