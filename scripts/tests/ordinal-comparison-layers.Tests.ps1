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
    [pscustomobject]@{ Name = 'set-variable-reassigned'; Source = 'function Test-SetVariableReassignedComparer { $comparison = [StringComparison]::Ordinal; Set-Variable comparison $external; return $value.StartsWith($prefix, $comparison) }' },
    [pscustomobject]@{ Name = 'typed-reassigned'; Source = 'function Test-TypedReassignedComparer { $comparison = [StringComparison]::Ordinal; [StringComparison]$comparison = $external; return $value.StartsWith($prefix, $comparison) }' },
    [pscustomobject]@{ Name = 'dynamic-set-variable-reassigned'; Source = 'function Test-DynamicSetVariableReassignedComparer { $comparison = [StringComparison]::Ordinal; Set-Variable -Name $target -Value $external; return $value.StartsWith($prefix, $comparison) }' },
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
$arrayExpressionMembershipFindings = @(Get-LayerProbeFindings -Source 'function Test-StringArrayMembership { return $event -notin @(''push'', ''pull_request'') }')
Assert-Layer (@($arrayExpressionMembershipFindings | Where-Object { $_.StartsWith('[culture-operator-with-string-literal]', [StringComparison]::Ordinal) }).Count -eq 1) `
    'A string array expression used by a culture membership operator must be reported.'
Assert-Layer (@(Get-LayerProbeFindings -Source 'function Test-NumericArrayMembership { return $statusCode -notin @(400, 422) }').Count -eq 0) `
    'A numeric array expression must not be treated as a string operand.'
Assert-Layer (@(Get-LayerProbeFindings -Source 'function Test-LowercaseIdentityNames { return $name -eq $path }').Count -eq 0) `
    'Lowercase bare names outside the case-sensitive identity suffix set must remain a documented blind spot.'
foreach ($nonStringIdentityCase in @(
    'function Test-Number { return [int]$leftId -eq $rightId }',
    'function Test-TypedLocalNumber { [int]$leftId = 1; [int]$rightId = 2; return $leftId -eq $rightId }',
    'function Test-Date { return [datetime]$createdAt -eq $updatedAt }',
    'function Test-Collection { return [Collections.Generic.HashSet[string]]$leftId -eq $rightId }'
)) {
    Assert-Layer (@(Get-LayerProbeFindings -Source $nonStringIdentityCase).Count -eq 0) `
        'Typed numeric, date, and collection comparisons must not be treated as string identity.'
}
$conditionalTypedIdentityFindings = @(Get-LayerProbeFindings -Source 'function Test-ConditionalTypedIdentity { if ($false) { [int]$leftId = 1; [int]$rightId = 2 }; return $leftId -eq $rightId }')
Assert-Layer (@($conditionalTypedIdentityFindings | Where-Object { $_.StartsWith('[culture-operator-with-identity-variable]', [StringComparison]::Ordinal) }).Count -eq 1) `
    'A typed local declared only in a conditional block must not suppress an identity comparison finding.'
$setVariableTypedIdentityFindings = @(Get-LayerProbeFindings -Source 'function Test-SetVariableTypedIdentity { [int]$leftId = 1; Set-Variable leftId $external; return $leftId -eq $rightId }')
Assert-Layer (@($setVariableTypedIdentityFindings | Where-Object { $_.StartsWith('[culture-operator-with-identity-variable]', [StringComparison]::Ordinal) }).Count -eq 1) `
    'A Set-Variable write must invalidate a prior typed-local identity proof.'
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
    'function Test-ReassignedForeachCharacter { param([string]$text) foreach ($character in $text.ToCharArray()) { $character = ''{''; if ($character -eq ''{'') { return $true } } }',
    'function Test-TypedReassignedForeachCharacter { param([string]$text) foreach ($character in $text.ToCharArray()) { [string]$character = ''{''; if ($character -eq ''{'') { return $true } } }',
    'function Test-PositionalSetVariableForeachCharacter { param([string]$text) foreach ($character in $text.ToCharArray()) { Set-Variable character ''{''; if ($character -eq ''{'') { return $true } } }',
    'function Test-AttachedSetVariableForeachCharacter { param([string]$text) foreach ($character in $text.ToCharArray()) { Set-Variable -Name:character -Value ''{''; if ($character -eq ''{'') { return $true } } }',
    'function Test-BuiltinAliasSetVariableForeachCharacter { param([string]$text) foreach ($character in $text.ToCharArray()) { sv character ''{''; if ($character -eq ''{'') { return $true } } }',
    'function Test-ScopedNamedSetVariableForeachCharacter { param([string]$text) foreach ($character in $text.ToCharArray()) { Set-Variable -Name:local:character -Value ''{''; if ($character -eq ''{'') { return $true } } }',
    'function Test-ScopedPositionalSetVariableForeachCharacter { param([string]$text) foreach ($character in $text.ToCharArray()) { Set-Variable local:character ''{''; if ($character -eq ''{'') { return $true } } }',
    'function Test-PrivateSetVariableForeachCharacter { param([string]$text) foreach ($character in $text.ToCharArray()) { Set-Variable private:character ''{''; if ($character -eq ''{'') { return $true } } }',
    'function Test-GlobalSetVariableForeachCharacter { param([string]$text) foreach ($character in $text.ToCharArray()) { Set-Variable global:character ''{''; if ($character -eq ''{'') { return $true } } }',
    'function Test-QualifiedSetVariableForeachCharacter { param([string]$text) foreach ($character in $text.ToCharArray()) { Microsoft.PowerShell.Utility\Set-Variable character ''{''; if ($character -eq ''{'') { return $true } } }',
    'function Test-DynamicSetVariableForeachCharacter { param([string]$text) foreach ($character in $text.ToCharArray()) { Set-Variable -Name $target -Value ''{''; if ($character -eq ''{'') { return $true } } }',
    'function Test-SubexpressionSetVariableForeachCharacter { param([string]$text) foreach ($character in $text.ToCharArray()) { Set-Variable $(''character'') ''{''; if ($character -eq ''{'') { return $true } } }',
    'function Test-NewVariableForeachCharacter { param([string]$text) foreach ($character in $text.ToCharArray()) { New-Variable character ''{'' -Force; if ($character -eq ''{'') { return $true } } }',
    'function Test-BuiltinAliasNewVariableForeachCharacter { param([string]$text) foreach ($character in $text.ToCharArray()) { nv character ''{'' -Force; if ($character -eq ''{'') { return $true } } }',
    'function Test-QualifiedNewVariableForeachCharacter { param([string]$text) foreach ($character in $text.ToCharArray()) { Microsoft.PowerShell.Utility\New-Variable character ''{'' -Force; if ($character -eq ''{'') { return $true } } }',
    'function Test-ClearVariableForeachCharacter { param([string]$text) foreach ($character in $text.ToCharArray()) { Clear-Variable character; if ($character -eq ''{'') { return $true } } }',
    'function Test-RemoveVariableForeachCharacter { param([string]$text) foreach ($character in $text.ToCharArray()) { Remove-Variable character; if ($character -eq ''{'') { return $true } } }',
    'function Test-SetItemForeachCharacter { param([string]$text) foreach ($character in $text.ToCharArray()) { Set-Item variable:character ''{''; if ($character -eq ''{'') { return $true } } }',
    'function Test-QualifiedSetItemForeachCharacter { param([string]$text) foreach ($character in $text.ToCharArray()) { Microsoft.PowerShell.Management\Set-Item variable:character ''{''; if ($character -eq ''{'') { return $true } } }',
    'function Test-ClearItemForeachCharacter { param([string]$text) foreach ($character in $text.ToCharArray()) { Clear-Item variable:character; if ($character -eq ''{'') { return $true } } }',
    'function Test-RemoveItemForeachCharacter { param([string]$text) foreach ($character in $text.ToCharArray()) { Remove-Item variable:character; if ($character -eq ''{'') { return $true } } }',
    'function Test-CopyItemForeachCharacter { param([string]$text) foreach ($character in $text.ToCharArray()) { Copy-Item variable:source variable:character; if ($character -eq ''{'') { return $true } } }',
    'function Test-MoveItemForeachCharacter { param([string]$text) foreach ($character in $text.ToCharArray()) { Move-Item variable:character variable:other; if ($character -eq ''{'') { return $true } } }',
    'function Test-RenameItemForeachCharacter { param([string]$text) foreach ($character in $text.ToCharArray()) { Rename-Item variable:character changed; if ($character -eq ''{'') { return $true } } }'
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
    'function Test-SetVariableReassignedSet { $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal); Set-Variable names $external; return $names.Contains(''Name'') }',
    'function Test-TypedReassignedSet { $names = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal); [object]$names = $external; return $names.Contains(''Name'') }',
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

foreach ($safeCharacterIndexCase in @(
    'function Test-ExplicitCharacterIndex { param([string]$value, [int]$start) return $value.IndexOf([char]'';'', $start) }',
    'function Test-TypedCharacterIndex { param([string]$value, [int]$start, [char]$needle) return $value.IndexOf($needle, $start) }',
    '$value = Get-Content -LiteralPath ''input.txt'' -Raw; $start = $value.IndexOf(''name'', [StringComparison]::Ordinal); $result = $value.IndexOf([char]'';'', $start)'
)) {
    Assert-Layer (@(Get-LayerProbeFindings -Source $safeCharacterIndexCase).Count -eq 0) `
        'An IndexOf overload with an explicit or statically proven char needle must be accepted.'
}
foreach ($stringLiteralIndexCase in @(
    'function Test-OneCharacterStringIndex { param([string]$value, [int]$start) return $value.IndexOf(''é'', $start) }',
    'function Test-MultiCharacterStringIndex { param([string]$value, [int]$start) return $value.IndexOf(''éé'', $start) }'
)) {
    $findings = @(Get-LayerProbeFindings -Source $stringLiteralIndexCase)
    Assert-Layer (@($findings | Where-Object { $_.StartsWith('[string-method-without-ordinal-comparison]', [StringComparison]::Ordinal) }).Count -eq 1) `
        'An IndexOf string literal, including a one-character literal, must remain a finding.'
}
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
    'function Get-ChildItem { [pscustomobject]@{ LastWriteTimeUtc = ''apple'' } }; function Test-ShadowedDateSort { Get-ChildItem | Sort-Object LastWriteTimeUtc }',
    'Set-Alias -Name:Get-ChildItem Get-Thing; function Test-AttachedAliasDateSort { Get-ChildItem | Sort-Object LastWriteTimeUtc }',
    'New-Alias -Name Get-ChildItem Get-Thing; function Test-SeparateNewAliasDateSort { Get-ChildItem | Sort-Object LastWriteTimeUtc }',
    'Microsoft.PowerShell.Utility\Set-Alias -Name:Get-ChildItem Get-Thing; function Test-QualifiedSetAliasDateSort { Get-ChildItem | Sort-Object LastWriteTimeUtc }',
    'Microsoft.PowerShell.Utility\New-Alias -Name Get-ChildItem Get-Thing; function Test-QualifiedNewAliasDateSort { Get-ChildItem | Sort-Object LastWriteTimeUtc }',
    'sal Get-ChildItem Get-Thing; function Test-BuiltinAliasDateSort { Get-ChildItem | Sort-Object LastWriteTimeUtc }',
    'nal Get-ChildItem Get-Thing; function Test-BuiltinNewAliasDateSort { Get-ChildItem | Sort-Object LastWriteTimeUtc }',
    'function script:Get-ChildItem { [pscustomobject]@{ LastWriteTimeUtc = ''apple'' } }; function Test-ScopedFunctionDateSort { Get-ChildItem | Sort-Object LastWriteTimeUtc }',
    'filter script:Get-ChildItem { [pscustomobject]@{ LastWriteTimeUtc = ''apple'' } }; function Test-ScopedFilterDateSort { Get-ChildItem | Sort-Object LastWriteTimeUtc }'
)) {
    $findings = @(Get-LayerProbeFindings -Source $invalidSortCase)
    Assert-Layer (@($findings | Where-Object { $_.StartsWith('[sort-object]', [StringComparison]::Ordinal) }).Count -eq 1) `
        'String, dynamic, and unproven DateTime-looking Sort-Object keys must remain findings.'
}

foreach ($invalidGetContentShadowCase in @(
    'Set-Alias -Name:Get-Content Get-Thing; $value = Get-Content -LiteralPath ''input.txt'' -Raw; $result = $value.IndexOf([char]'';'', 0)',
    'New-Alias -Name Get-Content Get-Thing; $value = Get-Content -LiteralPath ''input.txt'' -Raw; $result = $value.IndexOf([char]'';'', 0)',
    'Microsoft.PowerShell.Utility\Set-Alias -Name:Get-Content Get-Thing; $value = Get-Content -LiteralPath ''input.txt'' -Raw; $result = $value.IndexOf([char]'';'', 0)',
    'Microsoft.PowerShell.Utility\New-Alias -Name Get-Content Get-Thing; $value = Get-Content -LiteralPath ''input.txt'' -Raw; $result = $value.IndexOf([char]'';'', 0)',
    'sal Get-Content Get-Thing; $value = Get-Content -LiteralPath ''input.txt'' -Raw; $result = $value.IndexOf([char]'';'', 0)',
    'nal Get-Content Get-Thing; $value = Get-Content -LiteralPath ''input.txt'' -Raw; $result = $value.IndexOf([char]'';'', 0)',
    'function script:Get-Content { ''one;two'' }; $value = Get-Content -LiteralPath ''input.txt'' -Raw; $result = $value.IndexOf([char]'';'', 0)',
    'filter script:Get-Content { ''one;two'' }; $value = Get-Content -LiteralPath ''input.txt'' -Raw; $result = $value.IndexOf([char]'';'', 0)'
)) {
    $findings = @(Get-LayerProbeFindings -Source $invalidGetContentShadowCase)
    Assert-Layer (@($findings | Where-Object { $_.StartsWith('[string-method-without-ordinal-comparison]', [StringComparison]::Ordinal) }).Count -eq 1) `
        'Attached alias, builtin alias command, and scope-qualified function shadows must invalidate Get-Content string inference.'
}

foreach ($customAliasNameCase in @(
    'function customSv { param($name, $value) }; function Test-CustomSvName { param([string]$text) foreach ($character in $text.ToCharArray()) { customSv character ''{''; if ($character -eq ''{'') { return $true } } }',
    'function customNv { param($name, $value) }; function Test-CustomNvName { param([string]$text) foreach ($character in $text.ToCharArray()) { customNv character ''{''; if ($character -eq ''{'') { return $true } } }',
    'function customSi { param($name, $value) }; function Test-CustomSiName { param([string]$text) foreach ($character in $text.ToCharArray()) { customSi variable:character ''{''; if ($character -eq ''{'') { return $true } } }',
    'function customSal { param($name, $value) }; customSal Get-ChildItem Get-Thing; function Test-CustomSalName { Get-ChildItem | Sort-Object LastWriteTimeUtc }',
    'function customNal { param($name, $value) }; customNal Get-Content Get-Thing; $value = Get-Content -LiteralPath ''input.txt'' -Raw; $result = $value.IndexOf([char]'';'', 0)'
)) {
    Assert-Layer (@(Get-LayerProbeFindings -Source $customAliasNameCase).Count -eq 0) `
        'A user-defined command name must not be statically treated as a PowerShell builtin alias.'
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
