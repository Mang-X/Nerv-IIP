function Assert-BackendTestShardSelectorDiscovery {
    param(
        [Parameter(Mandatory)] [string] $Selector,
        [Parameter(Mandatory)] [bool] $MethodSelector,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $DiscoveredTests
    )

    $matchedTests = @($DiscoveredTests | Where-Object { $_.StartsWith($Selector, [StringComparison]::Ordinal) })
    if ($matchedTests.Count -eq 0 -or ($MethodSelector -and $matchedTests.Count -ne 1)) {
        $expected = if ($MethodSelector) { 'exactly one test' } else { 'at least one test' }
        throw "Real PostgreSQL selector '$Selector' discovery must match $expected; matched $($matchedTests.Count)."
    }

    return $matchedTests
}

function Assert-BackendTestShardSelectorExecution {
    param(
        [Parameter(Mandatory)] [string] $Selector,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $DiscoveredTests,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $TrxResults
    )

    $expectedTests = @($DiscoveredTests | Where-Object { $_.StartsWith($Selector, [StringComparison]::Ordinal) })
    $matchedResults = @(
        $TrxResults | Where-Object {
            $testName = if ($_ -is [string]) { $_ } else { [string] $_.testName }
            $testName.StartsWith($Selector, [StringComparison]::Ordinal)
        }
    )
    $executedNames = @($matchedResults | ForEach-Object { if ($_ -is [string]) { $_ } else { [string] $_.testName } })
    $missingTests = @($expectedTests | Where-Object { $executedNames -notcontains $_ })
    $failedResults = @($matchedResults | Where-Object { $_ -isnot [string] -and [string] $_.outcome -ne 'Passed' })
    if ($missingTests.Count -gt 0 -or $failedResults.Count -gt 0) {
        throw "Real PostgreSQL selector '$Selector' must execute every discovered test as Passed; discovered=$($expectedTests.Count), trx=$($matchedResults.Count), missing=$($missingTests.Count)."
    }
}
