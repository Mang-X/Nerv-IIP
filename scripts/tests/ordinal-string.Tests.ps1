# Script-Governance:
#   Category: check
#   SideEffects:
#     - None
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
. (Join-Path $repoRoot 'scripts/lib/OrdinalString.ps1')

$firstArray = [object[]]@('first-a', 'first-b')
$secondArray = [object[]]@('second-a', 'second-b')
$arrayItems = [object[]]::new(2)
$arrayItems[0] = $firstArray
$arrayItems[1] = $secondArray

$uniqueArrays = @(Get-NervItemsUniqueSortedByString `
    -Items $arrayItems `
    -KeySelector {
        param($item)
        if ([object]::ReferenceEquals($item, $firstArray)) { return 'first' }
        if ([object]::ReferenceEquals($item, $secondArray)) { return 'second' }
        throw 'Unexpected array item.'
    } `
    -Comparer ([StringComparer]::Ordinal))

if ($uniqueArrays.Count -ne 2) {
    throw "Get-NervItemsUniqueSortedByString must retain two array items, but returned $($uniqueArrays.Count) output values."
}
if ($uniqueArrays[0].GetType() -ne [object[]] -or $uniqueArrays[1].GetType() -ne [object[]]) {
    throw "Get-NervItemsUniqueSortedByString must retain array item types, but returned [$($uniqueArrays[0].GetType().FullName), $($uniqueArrays[1].GetType().FullName)]."
}
if (-not [object]::ReferenceEquals($uniqueArrays[0], $firstArray) -or
    -not [object]::ReferenceEquals($uniqueArrays[1], $secondArray)) {
    throw 'Get-NervItemsUniqueSortedByString must preserve each selected array object identity.'
}

$uniqueScalars = @(Get-NervItemsUniqueSortedByString `
    -Items ([object[]]@('bravo', 'alpha', 'bravo')) `
    -KeySelector { param($item) $item } `
    -Comparer ([StringComparer]::Ordinal))
if (-not [string]::Equals(($uniqueScalars -join '|'), 'alpha|bravo', [StringComparison]::Ordinal)) {
    throw "Get-NervItemsUniqueSortedByString changed scalar behavior: [$($uniqueScalars -join ', ')]."
}

Write-Host 'OrdinalString contract tests passed.'
