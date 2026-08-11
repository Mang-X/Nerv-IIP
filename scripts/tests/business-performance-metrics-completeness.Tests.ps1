# Script-Governance:
#   Category: check
#   SideEffects:
#     - Executes focused business performance metric completeness fixtures
#     - Executes the production verifier in child PowerShell processes with a fake dotnet executable
#     - Writes a temporary weakening mutation under the operating-system temp directory
#   Writes:
#     - Temporary files under the operating-system temp directory
#   Cleanup:
#     - Removes the owned temporary mutation directory in finally
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$libraryPath = Join-Path $repoRoot 'scripts/lib/BusinessPerformanceMetrics.ps1'
$verifierPath = Join-Path $repoRoot 'scripts/verify-business-performance-baseline.ps1'
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

$tokens = $null
$parseErrors = $null
$verifierAst = [Management.Automation.Language.Parser]::ParseFile($verifierPath, [ref]$tokens, [ref]$parseErrors)
Assert-Contract (@($parseErrors).Count -eq 0) 'Business performance verifier must parse before completeness wiring is inspected.'
$completenessCalls = @($verifierAst.FindAll({
            param($node)
            $node -is [Management.Automation.Language.CommandAst] -and
            [string]::Equals($node.GetCommandName(), 'Get-NervBusinessPerformanceMetricCompletenessViolations', [StringComparison]::Ordinal)
        }, $true))
Assert-Contract ($completenessCalls.Count -eq 1) 'Business performance verifier must invoke completeness enforcement exactly once before it can publish a passing summary.'
$thresholdCalls = @($verifierAst.FindAll({
            param($node)
            $node -is [Management.Automation.Language.CommandAst] -and
            [string]::Equals($node.GetCommandName(), 'Get-PerformanceMetricThreshold', [StringComparison]::Ordinal)
        }, $true))
Assert-Contract ($thresholdCalls.Count -eq 1 -and $completenessCalls[0].Extent.StartOffset -lt $thresholdCalls[0].Extent.StartOffset) 'Business performance verifier must enforce completeness before evaluating any threshold.'
$prematureEmptyThrows = @($verifierAst.FindAll({
            param($node)
            $node -is [Management.Automation.Language.ThrowStatementAst] -and
            $node.Extent.Text.Contains('Performance baseline metrics file is empty', [StringComparison]::Ordinal)
        }, $true))
Assert-Contract ($prematureEmptyThrows.Count -eq 0) 'An empty metrics file must flow through completeness enforcement so summary.json records machine-readable missing-metric violations before failure.'
$summaryWrites = @($verifierAst.FindAll({
            param($node)
            $node -is [Management.Automation.Language.CommandAst] -and
            [string]::Equals($node.GetCommandName(), 'Set-Content', [StringComparison]::Ordinal) -and
            $node.Extent.Text.Contains('$effectiveSummaryOutputPath', [StringComparison]::Ordinal)
        }, $true))
$completenessThrows = @($verifierAst.FindAll({
            param($node)
            $node -is [Management.Automation.Language.ThrowStatementAst] -and
            $node.Extent.Text.Contains('Performance metrics completeness validation failed', [StringComparison]::Ordinal)
        }, $true))
Assert-Contract ($summaryWrites.Count -eq 1 -and $completenessThrows.Count -eq 1 -and $summaryWrites[0].Extent.StartOffset -lt $completenessThrows[0].Extent.StartOffset) 'Business performance verifier must write summary.json before throwing a completeness failure.'

$mutationRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-business-performance-completeness-$([Guid]::NewGuid().ToString('N'))"
try {
    [IO.Directory]::CreateDirectory($mutationRoot) | Out-Null
    $fakeDotNetDirectory = Join-Path $mutationRoot 'fake-dotnet'
    $mutationScriptsDirectory = Join-Path $mutationRoot 'scripts'
    $mutationLibraryDirectory = Join-Path $mutationScriptsDirectory 'lib'
    [IO.Directory]::CreateDirectory($fakeDotNetDirectory) | Out-Null
    [IO.Directory]::CreateDirectory($mutationLibraryDirectory) | Out-Null

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

    $completeFixturePath = Join-Path $mutationRoot 'complete.jsonl'
    $partialFixturePath = Join-Path $mutationRoot 'partial.jsonl'
    Write-MetricFixture -Path $completeFixturePath -Metrics @($inventory, $mes, $erp)
    Write-MetricFixture -Path $partialFixturePath -Metrics @($inventory, $mes)

    $completeResult = Invoke-VerifierFixture `
        -ScriptPath $verifierPath `
        -MetricFixturePath $completeFixturePath `
        -FixtureRoot $mutationRoot `
        -FakeDotNetDirectory $fakeDotNetDirectory `
        -CaseName 'production-complete'
    Assert-Contract (-not $completeResult.Failed) 'The production verifier must accept exactly one canonical metric for each selected scenario.'
    Assert-Contract ($null -ne $completeResult.Summary -and $completeResult.Summary.passed -eq $true) 'The production verifier must publish passed=true for a complete metric set.'

    $partialResult = Invoke-VerifierFixture `
        -ScriptPath $verifierPath `
        -MetricFixturePath $partialFixturePath `
        -FixtureRoot $mutationRoot `
        -FakeDotNetDirectory $fakeDotNetDirectory `
        -CaseName 'production-partial'
    Assert-Contract $partialResult.Failed 'The production verifier must reject a partial metric set end to end.'
    Assert-Contract ($null -ne $partialResult.Summary -and $partialResult.Summary.passed -eq $false) 'The production verifier must publish passed=false before rejecting a partial metric set.'
    $partialViolationCodes = @($partialResult.Summary.violations | ForEach-Object { "$($_.code)|$($_.metricScenario)" })
    $missingErpViolations = @($partialViolationCodes | Where-Object {
            [string]::Equals($_, 'missing-performance-metric|erp-sales-order-list-high-read', [StringComparison]::Ordinal)
        })
    Assert-Contract ($missingErpViolations.Count -eq 1) 'The production verifier summary must identify the missing ERP metric exactly once.'

    Copy-Item -LiteralPath (Join-Path $repoRoot 'scripts/lib/OrdinalString.ps1') -Destination (Join-Path $mutationLibraryDirectory 'OrdinalString.ps1')
    Copy-Item -LiteralPath $libraryPath -Destination (Join-Path $mutationLibraryDirectory 'BusinessPerformanceMetrics.ps1')
    Copy-Item -LiteralPath (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1') -Destination (Join-Path $mutationLibraryDirectory 'ScriptAutomation.ps1')
    $mutationPath = Join-Path $mutationScriptsDirectory 'verify-business-performance-baseline.ps1'
    $verifierSource = [IO.File]::ReadAllText($verifierPath)
    $completenessAssignment = '$completenessViolations = @(Get-NervBusinessPerformanceMetricCompletenessViolations -SelectedScenario $Scenario -Metrics $metrics)'
    Assert-Contract ([regex]::Matches($verifierSource, [regex]::Escape($completenessAssignment)).Count -eq 1) 'Verifier weakening mutation must target exactly one completeness assignment.'
    $weakenedAssignment = "$completenessAssignment`n`$completenessViolations = @()"
    [IO.File]::WriteAllText($mutationPath, $verifierSource.Replace($completenessAssignment, $weakenedAssignment), [Text.UTF8Encoding]::new($false))

    $weakenedResult = Invoke-VerifierFixture `
        -ScriptPath $mutationPath `
        -MetricFixturePath $partialFixturePath `
        -FixtureRoot $mutationRoot `
        -FakeDotNetDirectory $fakeDotNetDirectory `
        -CaseName 'weakened-partial'
    Assert-Contract (-not $weakenedResult.Failed -and $null -ne $weakenedResult.Summary -and $weakenedResult.Summary.passed -eq $true) 'The end-to-end fixture must prove that clearing completeness results weakens the verifier.'

    $mutationLibraryPath = Join-Path $mutationRoot 'BusinessPerformanceMetrics.ps1'
    Copy-Item -LiteralPath (Join-Path $repoRoot 'scripts/lib/OrdinalString.ps1') -Destination (Join-Path $mutationRoot 'OrdinalString.ps1')
    $librarySource = [IO.File]::ReadAllText($libraryPath)
    $returnContract = 'return @($violations)'
    Assert-Contract ([regex]::Matches($librarySource, [regex]::Escape($returnContract)).Count -eq 1) 'Completeness weakening mutation must target exactly one production return.'
    [IO.File]::WriteAllText($mutationLibraryPath, $librarySource.Replace($returnContract, 'return @()'), [Text.UTF8Encoding]::new($false))

    Remove-Item -LiteralPath 'Function:\Get-NervBusinessPerformanceMetricCompletenessViolations' -Force
    . $mutationLibraryPath
    $mutationRejected = $false
    try {
        Assert-CompletenessViolations -Actual @(Get-NervBusinessPerformanceMetricCompletenessViolations -SelectedScenario 'all' -Metrics @($inventory, $mes)) -Expected @(
            'missing-performance-metric|erp-sales-order-list-high-read|1|0'
        )
    }
    catch {
        $mutationRejected = $true
    }
    Assert-Contract $mutationRejected 'Bypassing completeness violations must make the executable completeness contract fail.'
}
finally {
    if (Test-Path -LiteralPath $mutationRoot) { Remove-Item -LiteralPath $mutationRoot -Recurse -Force }
}

Write-Host 'Business performance metric completeness contract tests passed.'
