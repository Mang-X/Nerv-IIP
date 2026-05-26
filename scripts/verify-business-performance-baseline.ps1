# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Runs focused business performance baseline tests against the configured PostgreSQL database
#     - Applies EF Core migrations for Inventory, MES, and ERP schemas as required by selected tests
#     - Writes small opt-in baseline rows for the Inventory high-write scenario
#   Writes:
#     - bin/ and obj/ build outputs under tested .NET projects
#     - artifacts/script-logs/**
#     - Machine-readable metrics JSONL and summary JSON under artifacts/script-logs/**
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

    [int] $MaxElapsedMilliseconds = 0,

    [int] $InventoryMaxElapsedMilliseconds = 0,

    [int] $MesMaxElapsedMilliseconds = 0,

    [int] $ErpMaxElapsedMilliseconds = 0,

    [string] $MetricsOutputPath,

    [string] $SummaryOutputPath,

    [switch] $SkipRestore
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $root
. (Join-Path $root "scripts/lib/ScriptAutomation.ps1")

function Resolve-PerformanceOutputPath {
    param(
        [string] $Path,

        [string] $DefaultPath
    )

    $effectivePath = if ([string]::IsNullOrWhiteSpace($Path)) {
        $DefaultPath
    }
    else {
        $Path
    }

    if ([System.IO.Path]::IsPathRooted($effectivePath)) {
        return $effectivePath
    }

    return (Join-Path $root $effectivePath)
}

function Get-PerformanceMetricThreshold {
    param(
        [Parameter(Mandatory)]
        [object] $Metric
    )

    $scenarioName = [string] $Metric.scenario

    # Scenario-specific thresholds rely on the stable inventory-/mes-/erp- metric prefixes.
    if ($scenarioName.StartsWith("inventory-", [StringComparison]::OrdinalIgnoreCase) -and $InventoryMaxElapsedMilliseconds -gt 0) {
        return $InventoryMaxElapsedMilliseconds
    }

    if ($scenarioName.StartsWith("mes-", [StringComparison]::OrdinalIgnoreCase) -and $MesMaxElapsedMilliseconds -gt 0) {
        return $MesMaxElapsedMilliseconds
    }

    if ($scenarioName.StartsWith("erp-", [StringComparison]::OrdinalIgnoreCase) -and $ErpMaxElapsedMilliseconds -gt 0) {
        return $ErpMaxElapsedMilliseconds
    }

    return $MaxElapsedMilliseconds
}

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

$metricThresholds = @(
    $MaxElapsedMilliseconds,
    $InventoryMaxElapsedMilliseconds,
    $MesMaxElapsedMilliseconds,
    $ErpMaxElapsedMilliseconds
)
foreach ($threshold in $metricThresholds) {
    if ($threshold -lt 0) {
        throw "Performance threshold values must be greater than or equal to 0. Use 0 to disable a threshold."
    }
}

$metricsDirectory = New-ScriptAutomationLogDirectory -Name "business-performance-baseline-metrics"
$effectiveMetricsOutputPath = Resolve-PerformanceOutputPath -Path $MetricsOutputPath -DefaultPath (Join-Path $metricsDirectory "metrics.jsonl")
$effectiveSummaryOutputPath = Resolve-PerformanceOutputPath -Path $SummaryOutputPath -DefaultPath (Join-Path $metricsDirectory "summary.json")
$metricsOutputDirectory = Split-Path -Parent $effectiveMetricsOutputPath
$summaryOutputDirectory = Split-Path -Parent $effectiveSummaryOutputPath

New-Item -ItemType Directory -Force -Path $metricsOutputDirectory | Out-Null
New-Item -ItemType Directory -Force -Path $summaryOutputDirectory | Out-Null
Remove-Item -LiteralPath $effectiveMetricsOutputPath -Force -ErrorAction SilentlyContinue

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
    NERV_IIP_PERF_METRICS_PATH = $effectiveMetricsOutputPath
}

Write-Diagnostic "Running business performance baseline scenario=$Scenario profile=$Profile rows=$Rows."

Invoke-WithScopedEnvironment -Variables $environment -ScriptBlock {
    Invoke-DotNet -Name "business-performance-baseline-tests" -WorkingDirectory $root -Arguments $testArguments | Out-Null
}

if (-not (Test-Path $effectiveMetricsOutputPath)) {
    throw "Performance baseline completed but no machine-readable metrics were written to $effectiveMetricsOutputPath."
}

$metricLines = @(Get-Content -Path $effectiveMetricsOutputPath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
if ($metricLines.Count -eq 0) {
    throw "Performance baseline metrics file is empty: $effectiveMetricsOutputPath."
}

$metrics = @(
    foreach ($line in $metricLines) {
        $line | ConvertFrom-Json
    }
)

$violations = New-Object System.Collections.Generic.List[object]
foreach ($metric in $metrics) {
    $threshold = Get-PerformanceMetricThreshold -Metric $metric
    if ($threshold -gt 0 -and ([long] $metric.elapsedMilliseconds) -gt $threshold) {
        $violations.Add([pscustomobject]@{
            scenario = $metric.scenario
            elapsedMilliseconds = [long] $metric.elapsedMilliseconds
            maxElapsedMilliseconds = $threshold
        })
    }
}

$summary = [pscustomobject]@{
    scenario = $Scenario
    profile = $Profile
    rows = $Rows
    metricsPath = $effectiveMetricsOutputPath
    thresholds = [pscustomobject]@{
        defaultMaxElapsedMilliseconds = $MaxElapsedMilliseconds
        inventoryMaxElapsedMilliseconds = $InventoryMaxElapsedMilliseconds
        mesMaxElapsedMilliseconds = $MesMaxElapsedMilliseconds
        erpMaxElapsedMilliseconds = $ErpMaxElapsedMilliseconds
    }
    metrics = @($metrics)
    passed = $violations.Count -eq 0
    violations = @($violations)
}

$summaryJson = $summary | ConvertTo-Json -Depth 8 -Compress
Set-Content -Path $effectiveSummaryOutputPath -Value $summaryJson -Encoding utf8NoBOM
Write-Host $summaryJson

if ($violations.Count -gt 0) {
    $violationText = ($violations | ForEach-Object {
            "$($_.scenario) elapsedMs=$($_.elapsedMilliseconds) maxMs=$($_.maxElapsedMilliseconds)"
        }) -join "; "
    throw "Performance threshold exceeded: $violationText. Summary: $effectiveSummaryOutputPath"
}

Write-Host "Business performance baseline verified for scenario '$Scenario'. Metrics: $effectiveMetricsOutputPath Summary: $effectiveSummaryOutputPath"
