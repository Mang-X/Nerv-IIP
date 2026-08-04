function Get-BackendTestShardExecutedAssemblies {
    param([Parameter(Mandatory)] [string] $ResultsDirectory)

    $assemblies = [System.Collections.Generic.List[string]]::new()
    foreach ($trxPath in @(Get-ChildItem -LiteralPath $ResultsDirectory -Filter '*.trx' -File -Recurse | Sort-Object FullName)) {
        $document = [xml] (Get-Content -LiteralPath $trxPath.FullName -Raw)
        foreach ($definition in @($document.GetElementsByTagName('UnitTest', '*'))) {
            $storage = [string] $definition.GetAttribute('storage')
            if (-not [string]::IsNullOrWhiteSpace($storage)) {
                # Same storage-to-assembly rule as the MAN-661 collector.
                $assemblies.Add([System.IO.Path]::GetFileName($storage))
            }
        }
    }

    return @($assemblies | Sort-Object -Unique)
}

function Assert-BackendTestShardProjectExecution {
    param(
        [Parameter(Mandatory)] [string] $ShardId,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $ClassifiedProjects,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $ExecutedAssemblies
    )

    $expected = @(
        $ClassifiedProjects |
            ForEach-Object { [System.IO.Path]::GetFileNameWithoutExtension([string] $_) } |
            Sort-Object -Unique
    )
    $observed = @(
        $ExecutedAssemblies |
            ForEach-Object { [System.IO.Path]::GetFileNameWithoutExtension([string] $_) } |
            Sort-Object -Unique
    )

    $missing = @($expected | Where-Object { $observed -notcontains $_ })
    if ($missing.Count -gt 0) {
        throw "Fast shard '$ShardId' produced no executed test result for classified projects: $($missing -join ', '). Narrow the excluded real-dependency selectors or move the project to an explicit heavy lane."
    }

    $unexpected = @($observed | Where-Object { $expected -notcontains $_ })
    if ($unexpected.Count -gt 0) {
        throw "Fast shard '$ShardId' executed assemblies it does not classify: $($unexpected -join ', '). The solution filter and the shard manifest have drifted."
    }
}

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
