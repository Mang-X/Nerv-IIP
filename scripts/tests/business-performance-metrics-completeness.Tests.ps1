# Script-Governance:
#   Category: check
#   SideEffects:
#     - Executes focused business performance metric completeness fixtures
#     - Executes the production verifier in child PowerShell processes with a fake dotnet executable
#   Writes:
#     - Temporary files under the operating-system temp directory
#   Cleanup:
#     - Removes the owned temporary fixture directory in finally
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$libraryPath = Join-Path $repoRoot 'scripts/lib/BusinessPerformanceMetrics.ps1'
$verifierPath = Join-Path $repoRoot 'scripts/checks/verify-business-performance-baseline.ps1'
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')

function Assert-Contract {
    param(
        [Parameter(Mandatory)] [bool] $Condition,
        [Parameter(Mandatory)] [string] $Message
    )

    if (-not $Condition) { throw $Message }
}

function New-TestMetric {
    param([Parameter(Mandatory)] [string] $Scenario)

    return [pscustomobject]@{
        scenario = $Scenario
        elapsedMilliseconds = 10
    }
}

function Assert-CompletenessViolations {
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Actual,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $Expected
    )

    $actualRows = @(Get-NervStringsSorted -Values @($Actual | ForEach-Object {
                "$($_.code)|$($_.metricScenario)|$($_.expectedCount)|$($_.actualCount)"
            }) -Comparer ([StringComparer]::Ordinal))
    $expectedRows = @(Get-NervStringsSorted -Values $Expected -Comparer ([StringComparer]::Ordinal))
    Assert-Contract ($actualRows.Count -eq $expectedRows.Count) "Completeness violation count mismatch. Expected=[$($expectedRows -join ',')] Actual=[$($actualRows -join ',')]"
    for ($index = 0; $index -lt $expectedRows.Count; $index++) {
        Assert-Contract ([string]::Equals($actualRows[$index], $expectedRows[$index], [StringComparison]::Ordinal)) "Completeness violation mismatch at index $index. Expected=[$($expectedRows[$index])] Actual=[$($actualRows[$index])]"
    }
}

function Write-MetricFixture {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [object[]] $Metrics
    )

    $lines = @($Metrics | ForEach-Object { $_ | ConvertTo-Json -Compress })
    [IO.File]::WriteAllLines($Path, $lines, [Text.UTF8Encoding]::new($false))
}

function Invoke-VerifierFixture {
    param(
        [Parameter(Mandatory)] [string] $ScriptPath,
        [Parameter(Mandatory)] [string] $MetricFixturePath,
        [Parameter(Mandatory)] [string] $FixtureRoot,
        [Parameter(Mandatory)] [string] $FakeDotNetDirectory,
        [Parameter(Mandatory)] [string] $CaseName
    )

    $caseRoot = Join-Path $FixtureRoot $CaseName
    [IO.Directory]::CreateDirectory($caseRoot) | Out-Null
    $metricsPath = Join-Path $caseRoot 'metrics.jsonl'
    $summaryPath = Join-Path $caseRoot 'summary.json'
    $pathSeparator = [IO.Path]::PathSeparator
    $scopedPath = "$FakeDotNetDirectory$pathSeparator$([Environment]::GetEnvironmentVariable('PATH', 'Process'))"
    $failure = $null

    try {
        Invoke-WithScopedEnvironment -Variables @{
            PATH = $scopedPath
            NERV_FAKE_METRICS_SOURCE = $MetricFixturePath
        } -ScriptBlock {
            Invoke-NativeCommandOutput `
                -Command 'pwsh' `
                -Arguments @(
                    '-NoLogo',
                    '-NoProfile',
                    '-File', $ScriptPath,
                    '-ConnectionString', 'Host=fake;Database=nerv_performance_contract',
                    '-Scenario', 'all',
                    '-MetricsOutputPath', $metricsPath,
                    '-SummaryOutputPath', $summaryPath,
                    '-InventoryMaxElapsedMilliseconds', '600000',
                    '-MesMaxElapsedMilliseconds', '600000',
                    '-ErpMaxElapsedMilliseconds', '600000'
                ) `
                -WorkingDirectory $repoRoot `
                -TimeoutSeconds 60 `
                -Name "business-performance-completeness-$CaseName" | Out-Null
        }
    }
    catch {
        $failure = $_
    }

    $summary = if (Test-Path -LiteralPath $summaryPath -PathType Leaf) {
        Get-Content -LiteralPath $summaryPath -Raw | ConvertFrom-Json
    }
    else {
        $null
    }

    return [pscustomobject]@{
        Failed = $null -ne $failure
        Summary = $summary
    }
}

Assert-Contract (Test-Path -LiteralPath $libraryPath -PathType Leaf) 'Business performance metric completeness library is missing.'
. $libraryPath

$inventory = New-TestMetric -Scenario 'inventory-high-write'
$mes = New-TestMetric -Scenario 'mes-work-order-high-read'
$erp = New-TestMetric -Scenario 'erp-sales-order-list-high-read'

Assert-CompletenessViolations -Actual @(Get-NervBusinessPerformanceMetricCompletenessViolations -SelectedScenario 'all' -Metrics @($inventory, $mes, $erp)) -Expected @()
Assert-CompletenessViolations -Actual @(Get-NervBusinessPerformanceMetricCompletenessViolations -SelectedScenario 'all' -Metrics @()) -Expected @(
    'missing-performance-metric|erp-sales-order-list-high-read|1|0',
    'missing-performance-metric|inventory-high-write|1|0',
    'missing-performance-metric|mes-work-order-high-read|1|0'
)
Assert-CompletenessViolations -Actual @(Get-NervBusinessPerformanceMetricCompletenessViolations -SelectedScenario 'all' -Metrics @($inventory, $mes)) -Expected @(
    'missing-performance-metric|erp-sales-order-list-high-read|1|0'
)
Assert-CompletenessViolations -Actual @(Get-NervBusinessPerformanceMetricCompletenessViolations -SelectedScenario 'all' -Metrics @($inventory, $inventory, $mes, $erp)) -Expected @(
    'duplicate-performance-metric|inventory-high-write|1|2'
)
Assert-CompletenessViolations -Actual @(Get-NervBusinessPerformanceMetricCompletenessViolations -SelectedScenario 'all' -Metrics @($inventory, $mes, $erp, (New-TestMetric -Scenario 'unknown-performance-row'))) -Expected @(
    'unexpected-performance-metric|unknown-performance-row|0|1'
)

foreach ($singleScenario in @(
        @{ Selected = 'inventory'; Metric = $inventory },
        @{ Selected = 'mes'; Metric = $mes },
        @{ Selected = 'erp'; Metric = $erp }
    )) {
    Assert-CompletenessViolations -Actual @(Get-NervBusinessPerformanceMetricCompletenessViolations -SelectedScenario $singleScenario.Selected -Metrics @($singleScenario.Metric)) -Expected @()
}

Assert-CompletenessViolations -Actual @(Get-NervBusinessPerformanceMetricCompletenessViolations -SelectedScenario 'inventory' -Metrics @($inventory, $mes)) -Expected @(
    'unexpected-performance-metric|mes-work-order-high-read|0|1'
)
Assert-CompletenessViolations -Actual @(Get-NervBusinessPerformanceMetricCompletenessViolations -SelectedScenario 'mes' -Metrics @($mes, $mes)) -Expected @(
    'duplicate-performance-metric|mes-work-order-high-read|1|2'
)

$ordinalFailure = $null
try {
    Get-NervBusinessPerformanceMetricCompletenessViolations -SelectedScenario 'Inventory' -Metrics @($inventory) | Out-Null
}
catch {
    $ordinalFailure = $_
}
Assert-Contract ($null -ne $ordinalFailure -and $ordinalFailure.Exception.Message.Contains("Unsupported performance scenario 'Inventory'", [StringComparison]::Ordinal)) 'Selected scenario identity must be ordinal and reject non-canonical casing.'

$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-business-performance-completeness-$([Guid]::NewGuid().ToString('N'))"
try {
    [IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
    $fakeDotNetDirectory = Join-Path $fixtureRoot 'fake-dotnet'
    [IO.Directory]::CreateDirectory($fakeDotNetDirectory) | Out-Null

    if ($IsWindows) {
        $fakeDotNetPath = Join-Path $fakeDotNetDirectory 'dotnet.cmd'
        [IO.File]::WriteAllText(
            $fakeDotNetPath,
            "@echo off`r`ncopy /Y `"%NERV_FAKE_METRICS_SOURCE%`" `"%NERV_IIP_PERF_METRICS_PATH%`" >nul`r`nexit /b 0`r`n",
            [Text.ASCIIEncoding]::new()
        )
    }
    else {
        $fakeDotNetPath = Join-Path $fakeDotNetDirectory 'dotnet'
        [IO.File]::WriteAllText(
            $fakeDotNetPath,
            "#!/bin/sh`ncp `"`$NERV_FAKE_METRICS_SOURCE`" `"`$NERV_IIP_PERF_METRICS_PATH`"`n",
            [Text.UTF8Encoding]::new($false)
        )
        Invoke-NativeCommandOutput -Command 'chmod' -Arguments @('+x', $fakeDotNetPath) -Name 'make-fake-dotnet-executable' | Out-Null
    }

    $completeFixturePath = Join-Path $fixtureRoot 'complete.jsonl'
    $partialFixturePath = Join-Path $fixtureRoot 'partial.jsonl'
    Write-MetricFixture -Path $completeFixturePath -Metrics @($inventory, $mes, $erp)
    Write-MetricFixture -Path $partialFixturePath -Metrics @($inventory, $mes)

    $completeResult = Invoke-VerifierFixture `
        -ScriptPath $verifierPath `
        -MetricFixturePath $completeFixturePath `
        -FixtureRoot $fixtureRoot `
        -FakeDotNetDirectory $fakeDotNetDirectory `
        -CaseName 'production-complete'
    Assert-Contract (-not $completeResult.Failed) 'The production verifier must accept exactly one canonical metric for each selected scenario.'
    Assert-Contract ($null -ne $completeResult.Summary -and $completeResult.Summary.passed -eq $true) 'The production verifier must publish passed=true for a complete metric set.'

    $partialResult = Invoke-VerifierFixture `
        -ScriptPath $verifierPath `
        -MetricFixturePath $partialFixturePath `
        -FixtureRoot $fixtureRoot `
        -FakeDotNetDirectory $fakeDotNetDirectory `
        -CaseName 'production-partial'
    Assert-Contract $partialResult.Failed 'The production verifier must reject a partial metric set end to end.'
    Assert-Contract ($null -ne $partialResult.Summary -and $partialResult.Summary.passed -eq $false) 'The production verifier must publish passed=false before rejecting a partial metric set.'
    $partialViolationCodes = @($partialResult.Summary.violations | ForEach-Object { "$($_.code)|$($_.metricScenario)" })
    $missingErpViolations = @($partialViolationCodes | Where-Object {
            [string]::Equals($_, 'missing-performance-metric|erp-sales-order-list-high-read', [StringComparison]::Ordinal)
        })
    Assert-Contract ($missingErpViolations.Count -eq 1) 'The production verifier summary must identify the missing ERP metric exactly once.'

    $emptyFixturePath = Join-Path $fixtureRoot 'empty.jsonl'
    [IO.File]::WriteAllText($emptyFixturePath, '')
    $emptyResult = Invoke-VerifierFixture -ScriptPath $verifierPath -MetricFixturePath $emptyFixturePath -FixtureRoot $fixtureRoot -FakeDotNetDirectory $fakeDotNetDirectory -CaseName 'production-empty'
    Assert-Contract ($emptyResult.Failed -and $null -ne $emptyResult.Summary -and $emptyResult.Summary.passed -eq $false) 'An empty metrics file must publish a failed summary.'
    Assert-CompletenessViolations -Actual @($emptyResult.Summary.violations) -Expected @(
        'missing-performance-metric|erp-sales-order-list-high-read|1|0'
        'missing-performance-metric|inventory-high-write|1|0'
        'missing-performance-metric|mes-work-order-high-read|1|0'
    )
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
}

Write-Host 'Business performance metric completeness contract tests passed.'
