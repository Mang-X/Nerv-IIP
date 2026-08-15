# Script-Governance:
#   Category: library
#   SideEffects:
#     - None
#   Writes:
#     - None
#   Cleanup:
#     - No process or external resource ownership
#   Requires:
#     - PowerShell 7

Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'OrdinalString.ps1')

function Get-NervBusinessPerformanceMetricCompletenessViolations {
    param(
        [Parameter(Mandatory)] [string] $SelectedScenario,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [AllowNull()] [object[]] $Metrics
    )

    $contracts = [Collections.Generic.Dictionary[string, string[]]]::new([StringComparer]::Ordinal)
    $contracts.Add('all', [string[]]@(
            'inventory-high-write',
            'mes-work-order-high-read',
            'erp-sales-order-list-high-read'
        ))
    $contracts.Add('inventory', [string[]]@('inventory-high-write'))
    $contracts.Add('mes', [string[]]@('mes-work-order-high-read'))
    $contracts.Add('erp', [string[]]@('erp-sales-order-list-high-read'))

    if (-not $contracts.ContainsKey($SelectedScenario)) {
        throw "Unsupported performance scenario '$SelectedScenario'. Expected one of: all, inventory, mes, erp."
    }

    $expectedMetrics = $contracts[$SelectedScenario]
    $expectedSet = Get-NervStringSet -Values $expectedMetrics -Comparer ([StringComparer]::Ordinal)
    $observedCounts = [Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)
    foreach ($metric in @($Metrics)) {
        $metricScenario = if ($null -eq $metric) {
            '<null>'
        }
        else {
            $scenarioProperty = $metric.PSObject.Properties['scenario']
            if ($null -eq $scenarioProperty -or [string]::IsNullOrEmpty([string]$scenarioProperty.Value)) {
                '<missing>'
            }
            else {
                [string]$scenarioProperty.Value
            }
        }

        if ($observedCounts.ContainsKey($metricScenario)) {
            $observedCounts[$metricScenario]++
        }
        else {
            $observedCounts.Add($metricScenario, 1)
        }
    }

    $violations = [Collections.Generic.List[object]]::new()
    foreach ($expectedMetric in $expectedMetrics) {
        $actualCount = if ($observedCounts.ContainsKey($expectedMetric)) { $observedCounts[$expectedMetric] } else { 0 }
        if ($actualCount -eq 0) {
            $violations.Add([pscustomobject]@{
                    category = 'completeness'
                    code = 'missing-performance-metric'
                    metricScenario = $expectedMetric
                    expectedCount = 1
                    actualCount = 0
                })
        }
        elseif ($actualCount -gt 1) {
            $violations.Add([pscustomobject]@{
                    category = 'completeness'
                    code = 'duplicate-performance-metric'
                    metricScenario = $expectedMetric
                    expectedCount = 1
                    actualCount = $actualCount
                })
        }
    }

    foreach ($observedMetric in @(Get-NervStringsSorted -Values @($observedCounts.Keys) -Comparer ([StringComparer]::Ordinal))) {
        if (-not $expectedSet.Contains($observedMetric)) {
            $violations.Add([pscustomobject]@{
                    category = 'completeness'
                    code = 'unexpected-performance-metric'
                    metricScenario = $observedMetric
                    expectedCount = 0
                    actualCount = $observedCounts[$observedMetric]
                })
        }
    }

    return @($violations)
}
