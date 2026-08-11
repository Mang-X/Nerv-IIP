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

# 复合键必须保留组件边界，且不能让保留标记与内容碰撞。
$pipeOnLeft = Get-NervStringCompositeKey -Components @('a|b', 'c')
$pipeOnRight = Get-NervStringCompositeKey -Components @('a', 'b|c')
if ([string]::Equals($pipeOnLeft, $pipeOnRight, [StringComparison]::Ordinal)) {
    throw 'Get-NervStringCompositeKey must distinguish component boundaries.'
}

$zeroComponents = Get-NervStringCompositeKey -Components @()
$nullComponent = Get-NervStringCompositeKey -Components @($null)
$emptyComponent = Get-NervStringCompositeKey -Components @('')
if (@($zeroComponents, $nullComponent, $emptyComponent | Select-Object -Unique).Count -ne 3) {
    throw 'Get-NervStringCompositeKey must distinguish zero, null, and empty-string components.'
}

$nonStringRejected = $false
try {
    Get-NervStringCompositeKey -Components ([object[]]@('valid', 42)) | Out-Null
}
catch {
    $nonStringRejected = $true
}
if (-not $nonStringRejected) {
    throw 'Get-NervStringCompositeKey must reject non-string components.'
}

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

$sortedArrays = @(Get-NervItemsSortedByString `
    -Items $arrayItems `
    -KeySelector {
        param($item)
        if ([object]::ReferenceEquals($item, $firstArray)) { return 'second' }
        if ([object]::ReferenceEquals($item, $secondArray)) { return 'first' }
        throw 'Unexpected array item.'
    } `
    -Comparer ([StringComparer]::Ordinal))

if ($sortedArrays.Count -ne 2 -or
    $sortedArrays[0].GetType() -ne [object[]] -or
    $sortedArrays[1].GetType() -ne [object[]] -or
    -not [object]::ReferenceEquals($sortedArrays[0], $secondArray) -or
    -not [object]::ReferenceEquals($sortedArrays[1], $firstArray)) {
    throw 'Get-NervItemsSortedByString must sort array items without enumerating or replacing them.'
}

Write-Host 'OrdinalString contract tests passed.'
