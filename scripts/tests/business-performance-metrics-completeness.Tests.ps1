# Script-Governance:
#   Category: check
#   SideEffects:
#     - Executes focused business performance metric completeness fixtures
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
    $mutationPath = Join-Path $mutationRoot 'BusinessPerformanceMetrics.ps1'
    Copy-Item -LiteralPath (Join-Path $repoRoot 'scripts/lib/OrdinalString.ps1') -Destination (Join-Path $mutationRoot 'OrdinalString.ps1')
    $librarySource = [IO.File]::ReadAllText($libraryPath)
    $returnContract = 'return @($violations)'
    Assert-Contract ([regex]::Matches($librarySource, [regex]::Escape($returnContract)).Count -eq 1) 'Completeness weakening mutation must target exactly one production return.'
    [IO.File]::WriteAllText($mutationPath, $librarySource.Replace($returnContract, 'return @()'), [Text.UTF8Encoding]::new($false))

    Remove-Item -LiteralPath 'Function:\Get-NervBusinessPerformanceMetricCompletenessViolations' -Force
    . $mutationPath
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
