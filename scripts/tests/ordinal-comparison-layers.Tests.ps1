# Script-Governance:
#   Category: test
#   SideEffects:
#     - Reads PowerShell sources and writes one temporary repository mirror with per-layer mutation probes
#   Writes:
#     - System temporary directory only: repository mirror and per-layer mutation probes
#   Cleanup:
#     - Removes the temporary repository mirror in finally
#   Requires:
#     - PowerShell 7

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $repoRoot 'scripts/lib/OrdinalComparisonContract.ps1')
. (Join-Path $repoRoot 'scripts/lib/OrdinalString.ps1')

function Assert-Layer([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Get-LayerProbeFindings([string] $Source) {
    $probePath = Join-Path ([IO.Path]::GetTempPath()) "nerv-ordinal-comparer-$([guid]::NewGuid().ToString('N')).ps1"
    try {
        [IO.File]::WriteAllText($probePath, $Source, [Text.UTF8Encoding]::new($false))
        $result = Get-NervOrdinalComparisonFindings -ScriptPath $probePath -DisplayName 'comparer-probe.ps1'
        return @($result.Findings)
    }
    finally { Remove-Item -LiteralPath $probePath -Force -ErrorAction SilentlyContinue }
}

$workflow = [IO.File]::ReadAllText((Join-Path $repoRoot '.github/workflows/ci.yml'))
$compatibility = [IO.File]::ReadAllText((Join-Path $repoRoot 'scripts/check-script-compatibility.ps1'))
Assert-Layer ($workflow.Contains('run: ./scripts/tests/ordinal-comparison-layers.Tests.ps1', [StringComparison]::Ordinal)) 'Script Governance CI must run the ordinal layer contract.'
Assert-Layer ($compatibility.Contains('scripts/tests/ordinal-comparison-layers.Tests.ps1', [StringComparison]::Ordinal)) 'compat-fast must run the ordinal layer contract.'

$softHyphen = [char]0x00AD
$ordinalPair = @(Get-NervStringsSorted -Values @('Passed', "Passed$softHyphen") -Comparer ([StringComparer]::Ordinal) -Unique)
$ignoreCasePair = @(Get-NervStringsSorted -Values @('Passed', 'passed', "Passed$softHyphen") -Comparer ([StringComparer]::OrdinalIgnoreCase) -Unique)
Assert-Layer ($ordinalPair.Count -eq 2) 'The shared ordinal primitive must retain identifiers separated by U+00AD.'
Assert-Layer ($ignoreCasePair.Count -eq 2) 'OrdinalIgnoreCase may fold case, but must retain the U+00AD-distinct identifier.'

$safeComparerFindings = @(Get-LayerProbeFindings -Source @'
function Test-SafeComparer {
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    return $value.StartsWith($prefix, $comparison)
}
'@)
Assert-Layer ($safeComparerFindings.Count -eq 0) 'A local comparer whose every branch is ordinal must be accepted.'
foreach ($invalidComparerCase in @(
    [pscustomobject]@{ Name = 'culture'; Source = 'function Test-CultureComparer { $comparison = [StringComparison]::CurrentCulture; return $value.StartsWith($prefix, $comparison) }' },
    [pscustomobject]@{ Name = 'unknown'; Source = 'function Test-UnknownComparer { param($comparison) return $value.StartsWith($prefix, $comparison) }' },
    [pscustomobject]@{ Name = 'reassigned'; Source = 'function Test-ReassignedComparer { $comparison = [StringComparison]::Ordinal; $comparison = [StringComparison]::CurrentCulture; return $value.StartsWith($prefix, $comparison) }' },
    [pscustomobject]@{ Name = 'non-ordinal-branch'; Source = 'function Test-NonOrdinalBranchComparer { $comparison = if ($IsWindows) { [StringComparison]::Ordinal } else { [StringComparison]::InvariantCulture }; return $value.StartsWith($prefix, $comparison) }' },
    [pscustomobject]@{ Name = 'conditional-assignment'; Source = 'function Test-ConditionalComparer { if ($condition) { $comparison = [StringComparison]::Ordinal }; return $value.StartsWith($prefix, $comparison) }' }
)) {
    $findings = @(Get-LayerProbeFindings -Source $invalidComparerCase.Source)
    Assert-Layer (@($findings | Where-Object { $_.StartsWith('[string-method-without-ordinal-comparison]', [StringComparison]::Ordinal) }).Count -eq 1) `
        "The $($invalidComparerCase.Name) local comparer mutation must remain a finding."
}

$identityFindings = @(Get-LayerProbeFindings -Source 'function Test-Identity { return $leftId -eq $rightId }')
Assert-Layer (@($identityFindings | Where-Object { $_.StartsWith('[culture-operator-with-identity-variable]', [StringComparison]::Ordinal) }).Count -eq 1) `
    'Identity variables compared with -eq must be reported.'
foreach ($nonStringIdentityCase in @(
    'function Test-Number { return [int]$leftId -eq $rightId }',
    'function Test-TypedLocalNumber { [int]$leftId = 1; [int]$rightId = 2; return $leftId -eq $rightId }',
    'function Test-Date { return [datetime]$createdAt -eq $updatedAt }',
    'function Test-Collection { return [Collections.Generic.HashSet[string]]$leftId -eq $rightId }'
)) {
    Assert-Layer (@(Get-LayerProbeFindings -Source $nonStringIdentityCase).Count -eq 0) `
        'Typed numeric, date, and collection comparisons must not be treated as string identity.'
}
$cultureComparerFindings = @(Get-LayerProbeFindings -Source 'function Test-CultureFactory { return [StringComparer]::Create([Globalization.CultureInfo]::InvariantCulture, $true) }')
Assert-Layer (@($cultureComparerFindings | Where-Object { $_.StartsWith('[culture-created-stringcomparer]', [StringComparison]::Ordinal) }).Count -eq 1) `
    'Creating a StringComparer from CultureInfo must be reported.'

foreach ($safeCharacterCase in @(
    'function Test-IndexedCharacter { param([string]$text) return $text[0] -eq ''{'' }',
    'function Test-AssignedCharacter { param([string]$text) $character = $text[0]; return $character -ne ''\'' }',
    'function Test-EnumeratedCharacter { param([string]$text) foreach ($character in $text.ToCharArray()) { if ($character -eq ''"'') { return $true } } }'
)) {
    $findings = @(Get-LayerProbeFindings -Source $safeCharacterCase)
    Assert-Layer ($findings.Count -eq 0) `
        "A character proven from a typed string index or ToCharArray enumeration must not be treated as a string identity comparison: $safeCharacterCase -> $($findings -join '; ')"
}
foreach ($invalidCharacterCase in @(
    'function Test-UnknownCharacter { param($character) return $character -eq ''{'' }',
    'function Test-StringValue { param([string]$character) return $character -eq ''{'' }',
    'function Test-ReassignedCharacter { param([string]$text) $character = $text[0]; $character = ''value''; return $character -eq ''{'' }',
    'function Test-ReassignedForeachCharacter { param([string]$text) foreach ($character in $text.ToCharArray()) { $character = ''{''; if ($character -eq ''{'') { return $true } } }'
)) {
    $findings = @(Get-LayerProbeFindings -Source $invalidCharacterCase)
    Assert-Layer (@($findings | Where-Object { $_.StartsWith('[culture-operator-with-string-literal]', [StringComparison]::Ordinal) }).Count -eq 1) `
        'Unknown, string-typed, or reassigned character-shaped values must remain findings.'
}

foreach ($safeOrdinalSetCase in @(
    'function Test-DirectOrdinalSet { return [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal).Contains(''Name'') }',
    'function Test-LocalOrdinalSet { $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase); return $names.Contains(''Name'') }'
)) {
    Assert-Layer (@(Get-LayerProbeFindings -Source $safeOrdinalSetCase).Count -eq 0) `
        'Contains on a receiver proven to be an ordinal HashSet[string] must be accepted.'
}
foreach ($invalidOrdinalSetCase in @(
    'function Test-DefaultSet { $names = [Collections.Generic.HashSet[string]]::new(); return $names.Contains(''Name'') }',
    'function Test-CultureSet { $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::CurrentCulture); return $names.Contains(''Name'') }',
    'function Test-UnknownSet { param($names) return $names.Contains(''Name'') }',
    'function Test-ReassignedSet { $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal); $names = [Collections.Generic.HashSet[string]]::new(); return $names.Contains(''Name'') }',
    'function Test-ConditionalSet { if ($condition) { $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal) }; return $names.Contains(''Name'') }'
)) {
    $findings = @(Get-LayerProbeFindings -Source $invalidOrdinalSetCase)
    Assert-Layer (@($findings | Where-Object { $_.StartsWith('[ambiguous-method-with-string-literal]', [StringComparison]::Ordinal) }).Count -eq 1) `
        'Default, culture, unknown, or reassigned set receivers must remain findings.'
}

Assert-Layer (@(Get-LayerProbeFindings -Source 'function Test-ArrayIndex { return [Array]::IndexOf($values, $needle) }').Count -eq 0) `
    '[Array]::IndexOf is a collection lookup and must not be treated as a string method.'
$stringIndexFindings = @(Get-LayerProbeFindings -Source 'function Test-StringIndex { return $value.IndexOf($needle) }')
Assert-Layer (@($stringIndexFindings | Where-Object { $_.StartsWith('[string-method-without-ordinal-comparison]', [StringComparison]::Ordinal) }).Count -eq 1) `
    'An unknown instance IndexOf call must remain a finding.'

Assert-Layer (@(Get-LayerProbeFindings -Source 'function Test-CharacterIndex { param([string]$value, [int]$start) return $value.IndexOf('';'', $start) }').Count -eq 0) `
    'A one-character IndexOf overload with a proven integer start index must be accepted.'
Assert-Layer (@(Get-LayerProbeFindings -Source '$value = Get-Content -LiteralPath ''input.txt'' -Raw; $start = $value.IndexOf(''name'', [StringComparison]::Ordinal); $result = $value.IndexOf('';'', $start)').Count -eq 0) `
    'A Get-Content -Raw receiver and a start index returned by ordinal IndexOf must prove the character overload used by source-contract tests.'
foreach ($invalidIndexCase in @(
    'function Test-StringNeedleIndex { param([int]$start) return $value.IndexOf('';;'', $start) }',
    'function Test-UnknownReceiverIndex { param([int]$start) return $value.IndexOf('';'', $start) }',
    '$value = Get-Content -LiteralPath ''input.txt'' -Raw; $value = Get-Thing; $result = $value.IndexOf('';'', 0)',
    'function Get-Content { ''one;two'' }; function Test-ShadowedGetContent { $value = Get-Content -LiteralPath ''input.txt'' -Raw; return $value.IndexOf('';'', 0) }',
    'function Test-UnknownStartIndex { param($start) return $value.IndexOf('';'', $start) }',
    'function Test-OneArgumentIndex { return $value.IndexOf('';'') }',
    'function Test-ThreeArgumentIndex { param([string]$value, [int]$start, [StringComparison]$comparison) return $value.IndexOf(''i'', $start, $comparison) }',
    'function Test-FourArgumentIndex { param([string]$value, [int]$start, [StringComparison]$comparison) return $value.IndexOf(''i'', $start, 1, $comparison) }'
)) {
    $findings = @(Get-LayerProbeFindings -Source $invalidIndexCase)
    Assert-Layer (@($findings | Where-Object { $_.StartsWith('[string-method-without-ordinal-comparison]', [StringComparison]::Ordinal) }).Count -eq 1) `
        'Multi-character, unknown-start, and one-argument IndexOf forms must remain findings.'
}

Assert-Layer (@(Get-LayerProbeFindings -Source 'function Test-DateSort { Get-ChildItem | Sort-Object LastWriteTimeUtc -Descending }').Count -eq 0) `
    'A Get-ChildItem pipeline sorted by the DateTime LastWriteTimeUtc property must be accepted.'
foreach ($invalidSortCase in @(
    'function Test-NameSort { Get-ChildItem | Sort-Object Name }',
    'function Test-UnknownSort { param($property) Get-ChildItem | Sort-Object $property }',
    'function Test-DateLikeSort { Get-Thing | Sort-Object LastWriteTimeUtc }',
    'function Get-ChildItem { [pscustomobject]@{ LastWriteTimeUtc = ''apple'' } }; function Test-ShadowedDateSort { Get-ChildItem | Sort-Object LastWriteTimeUtc }'
)) {
    $findings = @(Get-LayerProbeFindings -Source $invalidSortCase)
    Assert-Layer (@($findings | Where-Object { $_.StartsWith('[sort-object]', [StringComparison]::Ordinal) }).Count -eq 1) `
        'String, dynamic, and unproven DateTime-looking Sort-Object keys must remain findings.'
}

$compositeCases = @(
    [pscustomobject]@{ Name = 'empty-sequence'; Components = [object[]]@() },
    [pscustomobject]@{ Name = 'single-empty'; Components = [object[]]@('') },
    [pscustomobject]@{ Name = 'single-null'; Components = [object[]]@($null) },
    [pscustomobject]@{ Name = 'plain'; Components = [object[]]@('alpha') },
    [pscustomobject]@{ Name = 'trailing-empty'; Components = [object[]]@('alpha', '') },
    [pscustomobject]@{ Name = 'reserved-combination-a'; Components = [object[]]@('alpha\|beta', 'gamma') },
    [pscustomobject]@{ Name = 'reserved-combination-b'; Components = [object[]]@('alpha\', '|beta', 'gamma') }
)
$compositeKeys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($case in $compositeCases) {
    $key = Get-NervStringCompositeKey -Components $case.Components
    Assert-Layer ($compositeKeys.Add($key)) "Composite key case '$($case.Name)' collided with an earlier string sequence."
}
Assert-Layer ([string]::Equals((Get-NervStringCompositeKey -Components @('lane', 'assembly', 'test')), 'lane|assembly|test', [StringComparison]::Ordinal)) `
    'Composite keys without null, empty, or reserved components must retain their historical bytes and line order.'

$exceptions = @(
    @{ Site = 'New-NervTestEvidenceSummary'; Text = 'Group-Object { Get-NervRetainedSkipReason $_ }'; Reason = 'Skip reasons are human prose; folding visually equivalent reasons is intentional.' }
)

function Get-NervOrdinalLayers([string] $Root) {
    $scriptsRoot = Join-Path $Root 'scripts'
    $testsRoot = Join-Path $scriptsRoot 'tests'
    $fixturesRoot = Join-Path $testsRoot 'fixtures'
    $fixturePrefix = [IO.Path]::GetFullPath($fixturesRoot).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar

    $layers = [ordered]@{
        verify = @(Get-ChildItem $scriptsRoot -File -Filter 'verify-*.ps1')
        top = @(Get-ChildItem $scriptsRoot -File -Filter '*.ps1' | Where-Object Name -NotLike 'verify-*')
        tests = @(Get-ChildItem $testsRoot -Recurse -File -Filter '*.ps1' | Where-Object {
            -not ([IO.Path]::GetFullPath($_.FullName).StartsWith($fixturePrefix, [StringComparison]::Ordinal))
        })
        test_fixtures = @(Get-ChildItem $fixturesRoot -Recurse -File -Filter '*.ps1')
        install_package_support = @(Get-ChildItem @(
            (Join-Path $scriptsRoot 'install'),
            (Join-Path $scriptsRoot 'package'),
            (Join-Path $scriptsRoot 'support')) -Recurse -File -Filter '*.ps1' -ErrorAction SilentlyContinue)
        lib = @(Get-ChildItem (Join-Path $scriptsRoot 'lib') -Recurse -File -Filter '*.ps1')
    }
    $listedPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($layer in $layers.Values) {
        foreach ($file in $layer) { [void]$listedPaths.Add($file.FullName) }
    }
    $layers.other = @(Get-ChildItem $scriptsRoot -Recurse -File -Filter '*.ps1' | Where-Object {
        -not $listedPaths.Contains($_.FullName)
    })
    return $layers
}

function Invoke-NervOrdinalLayerGate([string] $Root, [object[]] $NamedExceptions) {
    $layers = Get-NervOrdinalLayers -Root $Root
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $exceptionHits = [Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)
    foreach ($exception in $NamedExceptions) { $exceptionHits["$($exception.Site)|$($exception.Text)"] = 0 }
    foreach ($layer in $layers.GetEnumerator()) {
        if (-not [string]::Equals([string] $layer.Key, 'other', [StringComparison]::Ordinal)) {
            Assert-Layer ($layer.Value.Count -gt 0) "Ordinal layer '$($layer.Key)' selected no files."
        }
        foreach ($file in $layer.Value) {
            Assert-Layer ($seen.Add($file.FullName)) "Script source '$($file.FullName)' appeared in more than one ordinal layer."
            $result = Get-NervOrdinalComparisonFindings -ScriptPath $file.FullName -Exceptions $NamedExceptions -DisplayName $file.Name
            Assert-Layer ($result.Findings.Count -eq 0) "$($layer.Key)/$($file.Name) has culture-aware identifier comparisons:`n  $(@($result.Findings) -join "`n  ")"
            foreach ($key in $result.ExceptionHits.Keys) { $exceptionHits[$key] += [int]$result.ExceptionHits[$key] }
        }
    }
    $allScriptPaths = [Collections.Generic.HashSet[string]]::new(
        [string[]]@(Get-ChildItem (Join-Path $Root 'scripts') -Recurse -File -Filter '*.ps1' | ForEach-Object FullName),
        [StringComparer]::Ordinal)
    Assert-Layer ($seen.SetEquals($allScriptPaths)) 'Ordinal layers must cover every governed scripts/**/*.ps1 source exactly once.'
    foreach ($key in $exceptionHits.Keys) { Assert-Layer ($exceptionHits[$key] -eq 1) "Named ordinal exception '$key' matched $($exceptionHits[$key]) sites; expected exactly one." }

    return $layers
}

$layers = Invoke-NervOrdinalLayerGate -Root $repoRoot -NamedExceptions $exceptions
$expectedTestFiles = @(Get-ChildItem (Join-Path $repoRoot 'scripts/tests') -Recurse -File -Filter '*.ps1')
$actualTestPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($testFile in @($layers.tests) + @($layers.test_fixtures)) {
    Assert-Layer ($actualTestPaths.Add($testFile.FullName)) "Test source '$($testFile.FullName)' appeared in both the tests and fixture layers."
}
$expectedTestPaths = [Collections.Generic.HashSet[string]]::new([string[]]@($expectedTestFiles.FullName), [StringComparer]::Ordinal)
Assert-Layer ($actualTestPaths.SetEquals($expectedTestPaths)) 'The recursive tests layer and independent fixture layer must cover scripts/tests/** exactly once.'

$mutationRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-ordinal-layers-$([guid]::NewGuid().ToString('N'))"
try {
    [IO.Directory]::CreateDirectory($mutationRoot) | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot 'scripts') -Destination $mutationRoot -Recurse
    $mutationPaths = @(
        [pscustomobject]@{ Layer = 'verify'; Path = 'scripts/verify-ordinal-layer-mutation.ps1' },
        [pscustomobject]@{ Layer = 'top'; Path = 'scripts/ordinal-layer-mutation.ps1' },
        [pscustomobject]@{ Layer = 'tests'; Path = 'scripts/tests/new-layer/culture.ps1' },
        [pscustomobject]@{ Layer = 'test_fixtures'; Path = 'scripts/tests/fixtures/new-layer/culture.ps1' },
        [pscustomobject]@{ Layer = 'install_package_support'; Path = 'scripts/install/ordinal-layer-mutation.ps1' },
        [pscustomobject]@{ Layer = 'install_package_support'; Path = 'scripts/support/nested/ordinal-layer-mutation.ps1' },
        [pscustomobject]@{ Layer = 'lib'; Path = 'scripts/lib/ordinal-layer-mutation.ps1' },
        [pscustomobject]@{ Layer = 'lib'; Path = 'scripts/lib/nested/ordinal-layer-mutation.ps1' },
        [pscustomobject]@{ Layer = 'other'; Path = 'scripts/tools/new-path.ps1' }
    )
    foreach ($mutation in $mutationPaths) {
        $layerName = [string]$mutation.Layer
        $relativeMutationPath = [string]$mutation.Path
        $mutationPath = Join-Path $mutationRoot $relativeMutationPath
        [IO.Directory]::CreateDirectory((Split-Path -Parent $mutationPath)) | Out-Null
        [IO.File]::WriteAllText($mutationPath, "`$result = `$identifier -eq 'layer-$layerName'", [Text.UTF8Encoding]::new($false))
        try {
            $failure = $null
            try { Invoke-NervOrdinalLayerGate -Root $mutationRoot -NamedExceptions $exceptions | Out-Null }
            catch { $failure = $_ }
            Assert-Layer ($null -ne $failure) "Layer '$layerName' mutation survived the complete layer gate."
            $expectedDiagnostic = "$layerName/$([IO.Path]::GetFileName($mutationPath)) has culture-aware identifier comparisons:"
            Assert-Layer ($failure.Exception.Message.Contains($expectedDiagnostic, [StringComparison]::Ordinal)) `
                "Layer '$layerName' mutation failed through the wrong layer or diagnostic: $($failure.Exception.Message)"
        }
        finally { Remove-Item -LiteralPath $mutationPath -Force -ErrorAction SilentlyContinue }
    }

    $nonScriptPath = Join-Path $mutationRoot 'scripts/tools/not-a-script.txt'
    [IO.File]::WriteAllText($nonScriptPath, "`$result = `$identifier -eq 'not-a-script'", [Text.UTF8Encoding]::new($false))
    Invoke-NervOrdinalLayerGate -Root $mutationRoot -NamedExceptions $exceptions | Out-Null
}
finally { Remove-Item -LiteralPath $mutationRoot -Recurse -Force -ErrorAction SilentlyContinue }

Write-Host 'Ordinal comparison layer contracts passed.'
