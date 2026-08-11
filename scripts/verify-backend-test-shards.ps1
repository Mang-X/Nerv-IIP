# Script-Governance:
#   Category: check
#   SideEffects:
#     - Reads every backend project file, the backend solution, and the shard solution filters
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7
#     - Ruby 3.4 with yaml/json standard libraries

[CmdletBinding()]
param(
    [string] $ManifestPath = (Join-Path $PSScriptRoot 'backend-test-shards.json'),

    [string] $WorkflowPath = (Join-Path $PSScriptRoot '../.github/workflows/ci.yml'),

    [string] $PolicyPath = (Join-Path $PSScriptRoot 'test-evidence-policy.json'),

    # 测试专用 seam：仅供 backend-test-shards contract test 注入仓库外的镜像 inventory。
    [string] $BackendInventoryRoot
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'lib/ScriptAutomation.ps1')
. (Join-Path $PSScriptRoot 'lib/BackendTestShardSelectors.ps1')

function Get-RepoRelativePath {
    param(
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [string] $Path
    )

    return ([System.IO.Path]::GetRelativePath($RepositoryRoot, $Path) -replace '\\', '/')
}

# Two repo-relative spellings name the same file far more often than string equality admits:
# `./backend/Nerv.IIP.sln`, `backend\Nerv.IIP.sln`, `backend//Nerv.IIP.sln`,
# `backend/./Nerv.IIP.sln`, `backend/../backend/Nerv.IIP.sln` and an absolute path are all the
# backend solution. Stripping a single `^\./` prefix by hand only covers the first two, so every
# other spelling used to slip past the whole-solution rejection below and be reported as malformed
# JSON instead — a misleading diagnostic for the exact case that branch exists to name.
# GetFullPath collapses `.`, `..` and duplicate separators and resolves a relative path against the
# repository root, which reduces all of them to one comparable string.
function Get-CanonicalRepoPath {
    param(
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $Path
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return ''
    }

    $slashed = $Path -replace '\\', '/'
    try {
        return ([System.IO.Path]::GetFullPath([System.IO.Path]::Combine($RepositoryRoot, $slashed)) -replace '\\', '/')
    }
    catch {
        # A spelling the runtime cannot canonicalize (invalid path characters) is not the solution
        # path either. Returning it verbatim keeps the existence check below as the reporter rather
        # than making two uncanonicalizable spellings compare equal to each other.
        return $slashed
    }
}

function ConvertFrom-CiWorkflowYaml {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $WorkingDirectory
    )

    $rubyProgram = "require 'yaml'; require 'json'; puts JSON.generate(YAML.safe_load(File.read(ARGV.fetch(0))))"
    $result = Invoke-NativeCommandOutput -Command 'ruby' -Arguments @(
        '-ryaml',
        '-rjson',
        '-e', $rubyProgram,
        $Path
    ) -WorkingDirectory $WorkingDirectory -Name 'parse-ci-workflow'

    return ($result.Stdout | ConvertFrom-Json -ErrorAction Stop)
}

function Get-WorkflowStepValues {
    param(
        [AllowNull()] [object[]] $Steps,
        [Parameter(Mandatory)] [string] $PropertyName
    )

    return @(
        foreach ($step in @($Steps)) {
            $property = $step.PSObject.Properties[$PropertyName]
            if ($null -ne $property) {
                [string] $property.Value
            }
        }
    )
}

function Get-WorkflowStepsById {
    param(
        [AllowNull()] [object[]] $Steps,
        [Parameter(Mandatory)] [string] $StepId
    )

    return @(
        foreach ($step in @($Steps)) {
            $property = $step.PSObject.Properties['id']
            if ($null -ne $property -and [string]::Equals([string]([string] $property.Value), [string]($StepId), [StringComparison]::OrdinalIgnoreCase)) {
                $step
            }
        }
    )
}

function Get-WorkflowStringValue {
    param(
        [Parameter(Mandatory)] [object] $Object,
        [Parameter(Mandatory)] [string] $PropertyName
    )

    $property = $Object.PSObject.Properties[$PropertyName]
    if ($null -eq $property) {
        return ''
    }

    return [string] $property.Value
}

function Get-NervCSharpInterpolationHoleLayout {
    param(
        [Parameter(Mandatory)] [string] $SourceText,
        [Parameter(Mandatory)] [int] $ContentStart,
        [Parameter(Mandatory)] [int] $ClosingBraceCount
    )

    $characters = $SourceText.ToCharArray()
    $slash = [char]0x002F
    $asterisk = [char]0x002A
    $doubleQuote = [char]0x0022
    $singleQuote = [char]0x0027
    $backslash = [char]0x005C
    $openBrace = [char]0x007B
    $closeBrace = [char]0x007D
    $carriageReturn = [char]0x000D
    $lineFeed = [char]0x000A
    $depth = 0
    $index = $ContentStart
    while ($index -lt $characters.Length) {
        if ($characters[$index] -eq $slash -and $index + 1 -lt $characters.Length -and $characters[$index + 1] -eq $slash) {
            $index += 2
            while ($index -lt $characters.Length -and $characters[$index] -ne $carriageReturn -and $characters[$index] -ne $lineFeed) {
                $index++
            }
            continue
        }
        if ($characters[$index] -eq $slash -and $index + 1 -lt $characters.Length -and $characters[$index + 1] -eq $asterisk) {
            $index += 2
            while ($index + 1 -lt $characters.Length -and -not ($characters[$index] -eq $asterisk -and $characters[$index + 1] -eq $slash)) {
                $index++
            }
            $index = [Math]::Min($characters.Length, $index + 2)
            continue
        }
        if ($characters[$index] -eq $doubleQuote) {
            $nestedString = Get-NervCSharpStringTokenLayout -SourceText $SourceText -QuoteIndex $index
            $index = [Math]::Max($index + 1, $nestedString.TokenEnd)
            continue
        }
        if ($characters[$index] -eq $singleQuote) {
            $index++
            while ($index -lt $characters.Length) {
                if ($characters[$index] -eq $backslash) {
                    $index = [Math]::Min($characters.Length, $index + 2)
                    continue
                }
                if ($characters[$index] -eq $singleQuote) {
                    $index++
                    break
                }
                $index++
            }
            continue
        }
        if ($characters[$index] -eq $openBrace) {
            $depth++
            $index++
            continue
        }
        if ($characters[$index] -eq $closeBrace) {
            if ($depth -gt 0) {
                $depth--
                $index++
                continue
            }

            $closingRun = 1
            while ($index + $closingRun -lt $characters.Length -and $characters[$index + $closingRun] -eq $closeBrace) {
                $closingRun++
            }
            if ($closingRun -ge $ClosingBraceCount) {
                return [pscustomobject]@{
                    CloseStart = $index
                    CloseEnd = $index + $ClosingBraceCount
                }
            }
            $index += $closingRun
            continue
        }
        $index++
    }

    return $null
}

function Get-NervCSharpStringTokenLayout {
    param(
        [Parameter(Mandatory)] [string] $SourceText,
        [Parameter(Mandatory)] [int] $QuoteIndex
    )

    $characters = $SourceText.ToCharArray()
    $doubleQuote = [char]0x0022
    $atSign = [char]0x0040
    $dollarSign = [char]0x0024
    $backslash = [char]0x005C
    $openBrace = [char]0x007B
    $holes = [System.Collections.Generic.List[object]]::new()
    $quoteCount = 1
    while ($QuoteIndex + $quoteCount -lt $characters.Length -and $characters[$QuoteIndex + $quoteCount] -eq $doubleQuote) {
        $quoteCount++
    }

    $verbatim = ($QuoteIndex -gt 0 -and $characters[$QuoteIndex - 1] -eq $atSign) -or
        ($QuoteIndex -gt 1 -and $characters[$QuoteIndex - 2] -eq $atSign -and $characters[$QuoteIndex - 1] -eq $dollarSign)
    $raw = -not $verbatim -and $quoteCount -ge 3
    if (-not $raw) {
        $quoteCount = 1
    }

    $interpolationDollarCount = 0
    if ($raw) {
        $prefixIndex = $QuoteIndex - 1
        while ($prefixIndex -ge 0 -and $characters[$prefixIndex] -eq $dollarSign) {
            $interpolationDollarCount++
            $prefixIndex--
        }
    }
    else {
        if (($QuoteIndex -gt 0 -and $characters[$QuoteIndex - 1] -eq $dollarSign) -or
            ($QuoteIndex -gt 1 -and $characters[$QuoteIndex - 2] -eq $dollarSign -and $characters[$QuoteIndex - 1] -eq $atSign)) {
            $interpolationDollarCount = 1
        }
    }

    $index = $QuoteIndex + $quoteCount
    while ($index -lt $characters.Length) {
        if ($raw) {
            $closingQuoteCount = 0
            while ($index + $closingQuoteCount -lt $characters.Length -and $characters[$index + $closingQuoteCount] -eq $doubleQuote) {
                $closingQuoteCount++
            }
            if ($closingQuoteCount -ge $quoteCount) {
                return [pscustomobject]@{ TokenEnd = $index + $quoteCount; Holes = @($holes) }
            }
            if ($closingQuoteCount -gt 0) {
                $index += $closingQuoteCount
                continue
            }

            if ($interpolationDollarCount -gt 0 -and $characters[$index] -eq $openBrace) {
                $openingRun = 1
                while ($index + $openingRun -lt $characters.Length -and $characters[$index + $openingRun] -eq $openBrace) {
                    $openingRun++
                }
                if ($openingRun -ge $interpolationDollarCount) {
                    $delimiterStart = $index + ($openingRun - $interpolationDollarCount)
                    $contentStart = $delimiterStart + $interpolationDollarCount
                    $holeLayout = Get-NervCSharpInterpolationHoleLayout -SourceText $SourceText -ContentStart $contentStart -ClosingBraceCount $interpolationDollarCount
                    if ($null -eq $holeLayout) {
                        [void] $holes.Add([pscustomobject]@{ ContentStart = $contentStart; ContentEnd = $characters.Length })
                        return [pscustomobject]@{ TokenEnd = $characters.Length; Holes = @($holes) }
                    }
                    [void] $holes.Add([pscustomobject]@{ ContentStart = $contentStart; ContentEnd = $holeLayout.CloseStart })
                    $index = $holeLayout.CloseEnd
                    continue
                }
                $index += $openingRun
                continue
            }
            $index++
            continue
        }

        if (-not $verbatim -and $characters[$index] -eq $backslash) {
            $index = [Math]::Min($characters.Length, $index + 2)
            continue
        }
        if ($characters[$index] -eq $doubleQuote) {
            if ($verbatim -and $index + 1 -lt $characters.Length -and $characters[$index + 1] -eq $doubleQuote) {
                $index += 2
                continue
            }
            return [pscustomobject]@{ TokenEnd = $index + 1; Holes = @($holes) }
        }
        if ($interpolationDollarCount -gt 0 -and $characters[$index] -eq $openBrace) {
            if ($index + 1 -lt $characters.Length -and $characters[$index + 1] -eq $openBrace) {
                $index += 2
                continue
            }
            $contentStart = $index + 1
            $holeLayout = Get-NervCSharpInterpolationHoleLayout -SourceText $SourceText -ContentStart $contentStart -ClosingBraceCount 1
            if ($null -eq $holeLayout) {
                [void] $holes.Add([pscustomobject]@{ ContentStart = $contentStart; ContentEnd = $characters.Length })
                return [pscustomobject]@{ TokenEnd = $characters.Length; Holes = @($holes) }
            }
            [void] $holes.Add([pscustomobject]@{ ContentStart = $contentStart; ContentEnd = $holeLayout.CloseStart })
            $index = $holeLayout.CloseEnd
            continue
        }
        $index++
    }

    return [pscustomobject]@{ TokenEnd = $characters.Length; Holes = @($holes) }
}

function ConvertTo-NervCSharpStructuralText {
    param(
        [Parameter(Mandatory)] [string] $SourceText
    )

    $sourceCharacters = $SourceText.ToCharArray()
    $structuralCharacters = $SourceText.ToCharArray()
    $slash = [char]0x002F
    $asterisk = [char]0x002A
    $doubleQuote = [char]0x0022
    $singleQuote = [char]0x0027
    $backslash = [char]0x005C
    $carriageReturn = [char]0x000D
    $lineFeed = [char]0x000A
    $index = 0
    while ($index -lt $sourceCharacters.Length) {
        $tokenEnd = -1
        $tokenHoles = @()
        if ($sourceCharacters[$index] -eq $slash -and $index + 1 -lt $sourceCharacters.Length -and $sourceCharacters[$index + 1] -eq $slash) {
            $tokenEnd = $index + 2
            while ($tokenEnd -lt $sourceCharacters.Length -and $sourceCharacters[$tokenEnd] -ne $carriageReturn -and $sourceCharacters[$tokenEnd] -ne $lineFeed) {
                $tokenEnd++
            }
        }
        elseif ($sourceCharacters[$index] -eq $slash -and $index + 1 -lt $sourceCharacters.Length -and $sourceCharacters[$index + 1] -eq $asterisk) {
            $tokenEnd = $index + 2
            while ($tokenEnd + 1 -lt $sourceCharacters.Length -and -not ($sourceCharacters[$tokenEnd] -eq $asterisk -and $sourceCharacters[$tokenEnd + 1] -eq $slash)) {
                $tokenEnd++
            }
            $tokenEnd = [Math]::Min($sourceCharacters.Length, $tokenEnd + 2)
        }
        elseif ($sourceCharacters[$index] -eq $doubleQuote) {
            $stringLayout = Get-NervCSharpStringTokenLayout -SourceText $SourceText -QuoteIndex $index
            $tokenEnd = $stringLayout.TokenEnd
            $tokenHoles = @($stringLayout.Holes)
        }
        elseif ($sourceCharacters[$index] -eq $singleQuote) {
            $tokenEnd = $index + 1
            while ($tokenEnd -lt $sourceCharacters.Length) {
                if ($sourceCharacters[$tokenEnd] -eq $backslash) {
                    $tokenEnd = [Math]::Min($sourceCharacters.Length, $tokenEnd + 2)
                    continue
                }
                if ($sourceCharacters[$tokenEnd] -eq $singleQuote) {
                    $tokenEnd++
                    break
                }
                $tokenEnd++
            }
        }

        if ($tokenEnd -le $index) {
            $index++
            continue
        }

        $tokenText = $SourceText.Substring($index, $tokenEnd - $index)
        $preserveAuditedDockerLiteral = [string]::Equals($tokenText, '"docker"', [StringComparison]::Ordinal)
        if (-not $preserveAuditedDockerLiteral) {
            for ($maskedIndex = $index; $maskedIndex -lt $tokenEnd; $maskedIndex++) {
                if ($structuralCharacters[$maskedIndex] -ne $carriageReturn -and $structuralCharacters[$maskedIndex] -ne $lineFeed) {
                    $structuralCharacters[$maskedIndex] = ' '
                }
            }
        }

        foreach ($hole in $tokenHoles) {
            $holeLength = $hole.ContentEnd - $hole.ContentStart
            if ($holeLength -le 0) {
                continue
            }
            $holeText = $SourceText.Substring($hole.ContentStart, $holeLength)
            $holeStructuralText = ConvertTo-NervCSharpStructuralText -SourceText $holeText
            for ($holeIndex = 0; $holeIndex -lt $holeLength; $holeIndex++) {
                $structuralCharacters[$hole.ContentStart + $holeIndex] = $holeStructuralText[$holeIndex]
            }
        }
        $index = $tokenEnd
    }

    return [string]::new($structuralCharacters)
}

function Get-NervCSharpClassRanges {
    param(
        [Parameter(Mandatory)] [string] $StructuralText
    )

    $classPattern = '(?m)^\s*(?:(?:public|internal|private|protected|sealed|abstract|static|partial)\s+)*class\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)'
    $openBrace = [char]0x007B
    $closeBrace = [char]0x007D
    foreach ($classMatch in [regex]::Matches($StructuralText, $classPattern)) {
        $openBraceIndex = $StructuralText.IndexOf([string] $openBrace, $classMatch.Index + $classMatch.Length, [StringComparison]::Ordinal)
        if ($openBraceIndex -lt 0) {
            continue
        }

        $depth = 0
        $closeBraceIndex = -1
        for ($braceIndex = $openBraceIndex; $braceIndex -lt $StructuralText.Length; $braceIndex++) {
            if ($StructuralText[$braceIndex] -eq $openBrace) {
                $depth++
            }
            elseif ($StructuralText[$braceIndex] -eq $closeBrace) {
                $depth--
                if ($depth -eq 0) {
                    $closeBraceIndex = $braceIndex
                    break
                }
            }
        }

        if ($closeBraceIndex -ge 0) {
            [pscustomobject]@{
                Name = $classMatch.Groups['name'].Value
                StartIndex = $classMatch.Index
                OpenBraceIndex = $openBraceIndex
                CloseBraceIndex = $closeBraceIndex
            }
        }
    }
}

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$resolvedManifestPath = (Resolve-Path $ManifestPath).Path
$manifest = Get-Content -LiteralPath $resolvedManifestPath -Raw | ConvertFrom-Json
$errors = [System.Collections.Generic.List[string]]::new()

if ($manifest.schemaVersion -ne 1) {
    $errors.Add('backend test shard manifest schemaVersion must be 1.')
}

$fastShards = @($manifest.fastShards)
$heavyLanes = @($manifest.heavyLanes)
if ($fastShards.Count -ne 4) {
    $errors.Add('backend test shard manifest must define exactly four fast shards for phase 1.')
}

$classificationEntries = @()
foreach ($shard in $fastShards) {
    if ([string]::IsNullOrWhiteSpace($shard.id)) {
        $errors.Add('Fast shard is missing id.')
        continue
    }

    if ([string]::IsNullOrWhiteSpace($shard.solutionFilter)) {
        $errors.Add("Fast shard '$($shard.id)' is missing solutionFilter.")
    }

    foreach ($project in @($shard.projects)) {
        $classificationEntries += [pscustomobject]@{ Lane = $shard.id; Project = [string] $project; Fast = $true }
    }

    if ($null -eq $shard.PSObject.Properties['excludedTestLanes']) {
        $errors.Add("Fast shard '$($shard.id)' must declare excludedTestLanes, even when empty.")
    }

    if ([string] $shard.evidenceLane -notmatch '^backend-shard-[1-9][0-9]*$') {
        $errors.Add("Fast shard '$($shard.id)' must declare a schema-v1 backend shard evidence lane, not '$($shard.evidenceLane)'.")
    }

    if ([string] $shard.jobName -notmatch '^Backend Tests - \S.*$') {
        $errors.Add("Fast shard '$($shard.id)' must declare the CI job name that owns its evidence lane.")
    }
}

foreach ($duplicateEvidenceLane in Get-NervStringGroups -Items @($fastShards.evidenceLane) -KeySelector { param($value) [string]$value } -Comparer ([StringComparer]::Ordinal) | Where-Object Count -gt 1) {
    $errors.Add("Duplicate fast shard evidence lane: $($duplicateEvidenceLane.Name).")
}
foreach ($duplicateJobName in Get-NervStringGroups -Items @($fastShards.jobName) -KeySelector { param($value) [string]$value } -Comparer ([StringComparer]::Ordinal) | Where-Object Count -gt 1) {
    $errors.Add("Duplicate fast shard job name: $($duplicateJobName.Name).")
}
foreach ($lane in $heavyLanes) {
    if ([string]::IsNullOrWhiteSpace($lane.id)) {
        $errors.Add('Heavy lane is missing id.')
        continue
    }

    foreach ($project in @($lane.projects)) {
        $classificationEntries += [pscustomobject]@{ Lane = $lane.id; Project = [string] $project; Fast = $false }
    }

    if ([string]::IsNullOrWhiteSpace([string] $lane.ownerScript)) {
        $errors.Add("Heavy lane '$($lane.id)' must declare an executable ownerScript.")
    }
    elseif (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot ([string] $lane.ownerScript)) -PathType Leaf)) {
        $errors.Add("Heavy lane '$($lane.id)' ownerScript does not exist: $($lane.ownerScript).")
    }
}

$allLaneIds = @($fastShards.id) + @($heavyLanes.id)
$heavyLaneIdSet = Get-NervStringSet -Values @($heavyLanes.id) -Comparer ([StringComparer]::Ordinal)
foreach ($duplicateLaneId in Get-NervStringGroups -Items @($allLaneIds) -KeySelector { param($value) [string]$value } -Comparer ([StringComparer]::Ordinal) | Where-Object Count -gt 1) {
    $errors.Add("Duplicate shard or lane id: $($duplicateLaneId.Name).")
}

$excludedClassOwners = @{}
$excludedClassSelectorsByFastShard = @{}
foreach ($shard in $fastShards) {
    $shardClassSelectors = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($excludedLane in @($shard.excludedTestLanes | ForEach-Object { [string] $_ })) {
        if (-not $heavyLaneIdSet.Contains([string]$excludedLane)) {
            $errors.Add("Fast shard '$($shard.id)' must assign excluded tests to a declared heavy lane, not '$excludedLane'.")
        }
    }

    foreach ($testName in @(Get-BackendTestShardExcludedSelectors -Shard $shard)) {
        if ($testName -notmatch '^[A-Za-z_][A-Za-z0-9_.]+$') {
            $errors.Add("Fast shard '$($shard.id)' has an invalid excluded test selector: $testName.")
            continue
        }
        if ($excludedClassOwners.ContainsKey($testName)) {
            $errors.Add("Excluded real test selector is assigned more than once: $testName ($($excludedClassOwners[$testName]), $($shard.id)).")
            continue
        }
        $excludedClassOwners[$testName] = $shard.id
    }

    $classSelectorsProperty = $shard.PSObject.Properties['excludedTestClasses']
    if ($null -ne $classSelectorsProperty) {
        foreach ($classSelector in @($classSelectorsProperty.Value)) {
            [void] $shardClassSelectors.Add([string] $classSelector)
        }
    }
    $excludedClassSelectorsByFastShard[[string] $shard.id] = $shardClassSelectors
}

if (-not (Test-Path -LiteralPath $PolicyPath -PathType Leaf)) {
    $errors.Add("MAN-661 test evidence policy does not exist: $PolicyPath.")
}
else {
    $policy = Get-Content -LiteralPath (Resolve-Path $PolicyPath).Path -Raw | ConvertFrom-Json
    $policySourcePaths = @{}
    foreach ($source in @($policy.sources)) {
        $policySourcePaths[[string] $source.id] = [string] $source.sourcePath
    }

    # The rules a fast-shard exclusion is allowed to appeal to: environment-gated, with a
    # requiredLane that resolves to exactly one policy lane and that lane is a real dependency.
    # Selector-to-identity resolution itself lives in Get-BackendTestShardPolicyIdentityMatches so
    # that the gate and its contract tests run the same derivation rather than two copies of it.
    $realDependencyRules = @(
        foreach ($rule in @($policy.rules)) {
            if (-not [string]::Equals([string] $rule.classification, 'environment-gated', [StringComparison]::Ordinal)) { continue }
            $requiredLane = [string] $rule.requiredLane
            if ([string]::IsNullOrWhiteSpace($requiredLane)) { continue }
            $laneMatches = @($policy.lanes | Where-Object { $requiredLane -cmatch [string] $_.namePattern })
            if ($laneMatches.Count -ne 1 -or -not [bool] $laneMatches[0].realDependency) { continue }
            $rule
        }
    )

    $heavyLaneByPolicyLane = @{}
    foreach ($lane in $heavyLanes) {
        $policyLane = [string] $lane.policyLane
        if ([string]::IsNullOrWhiteSpace($policyLane)) {
            $errors.Add("Heavy lane '$($lane.id)' must declare the MAN-661 policy lane it owns.")
            continue
        }
        $heavyLaneByPolicyLane[$policyLane] = [string] $lane.id
    }

    foreach ($shard in $fastShards) {
        $ownerLanes = [System.Collections.Generic.HashSet[string]]::new()
        foreach ($selector in @(Get-BackendTestShardExcludedSelectors -Shard $shard)) {
            # A fast shard may only filter away work that MAN-661 has registered as an
            # environment-gated real-dependency skip. Without this the exclusion list is a private
            # escape hatch for dropping anything from the default gate.
            $covering = @(Get-BackendTestShardPolicyIdentityMatches -Selector $selector -Rules $realDependencyRules)
            if ($covering.Count -eq 0) {
                $errors.Add("Fast shard exclusion '$selector' is not registered in the MAN-661 evidence policy as an environment-gated real-dependency skip.")
                continue
            }
            foreach ($match in $covering) {
                $policyLane = [string] $match.requiredLane
                if (-not $heavyLaneByPolicyLane.ContainsKey($policyLane)) {
                    $errors.Add("MAN-661 policy lane '$policyLane' required by '$selector' has no owning heavy lane in the shard manifest.")
                    continue
                }
                [void] $ownerLanes.Add([string] $heavyLaneByPolicyLane[$policyLane])
            }
        }

        # The declared owner lanes must equal the lanes MAN-661 actually requires, so a shard cannot
        # attribute a full-chain or performance exclusion to the real-postgres owner script.
        $declaredLanes = @(Get-NervStringsSorted -Values @($shard.excludedTestLanes | ForEach-Object { [string] $_ }) -Comparer ([StringComparer]::Ordinal) -Unique)
        $derivedLanes = @(Get-NervStringsSorted -Values @($ownerLanes) -Comparer ([StringComparer]::Ordinal))
        if ((-not [string]::Equals([string]((@($declaredLanes) -join '|')), [string]((@($derivedLanes) -join '|')), [StringComparison]::Ordinal))) {
            $errors.Add("Fast shard '$($shard.id)' must declare excludedTestLanes [$(@($derivedLanes) -join ', ')] to match the MAN-661 requiredLane of its exclusions; it declares [$(@($declaredLanes) -join ', ')].")
        }

        # A method selector stays a substring filter, so a sibling method whose name merely extends
        # it would be silently excluded too. Class selectors are anchored with a trailing dot in the
        # runner and cannot collide this way.
        foreach ($methodSelector in @(Get-BackendTestShardExcludedSelectors -Shard $shard -Kind 'method')) {
            $sourceIds = @(
                Get-NervStringsSorted -Values @(Get-BackendTestShardPolicyIdentityMatches -Selector $methodSelector -Rules $realDependencyRules |
                    ForEach-Object { [string] $_.sourceId }) -Comparer ([StringComparer]::Ordinal) -Unique
            )
            if ($sourceIds.Count -eq 0) {
                $errors.Add("Method selector '$methodSelector' has no MAN-661 source registration to scan for prefix collisions.")
                continue
            }
            $methodName = $methodSelector.Substring($methodSelector.LastIndexOf('.', [StringComparison]::Ordinal) + 1)
            foreach ($sourceId in $sourceIds) {
                $sourcePath = Join-Path $repositoryRoot ([string] $policySourcePaths[$sourceId])
                if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
                    $errors.Add("MAN-661 source '$sourceId' for '$methodSelector' does not exist: $($policySourcePaths[$sourceId]).")
                    continue
                }
                $sourceText = Get-Content -LiteralPath $sourcePath -Raw
                $collisions = @([regex]::Matches($sourceText, "\b$([regex]::Escape($methodName))[A-Za-z0-9_]+\s*[(<]") | ForEach-Object { $_.Value })
                if ($collisions.Count -gt 0) {
                    $errors.Add("Method selector '$methodSelector' would also substring-exclude a sibling member in $($policySourcePaths[$sourceId]): $(@($collisions) -join ', ').")
                }
            }
        }
    }
}

$projectOwners = @{}
$ambiguousProjectOwners = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($entry in $classificationEntries) {
    $project = $entry.Project -replace '\\', '/'
    if ([string]::IsNullOrWhiteSpace($project)) {
        $errors.Add("Shard or lane '$($entry.Lane)' contains an empty project path.")
        continue
    }

    if ($projectOwners.ContainsKey($project)) {
        $errors.Add("Backend test project is classified more than once: $project ($($projectOwners[$project]), $($entry.Lane)).")
        [void] $ambiguousProjectOwners.Add([string] $project)
        continue
    }

    $projectOwners[$project] = $entry.Lane
    if (-not (Test-Path -LiteralPath (Join-Path $repositoryRoot $project) -PathType Leaf)) {
        $errors.Add("Classified backend test project does not exist: $project.")
    }
}

$backendRoot = if ([string]::IsNullOrWhiteSpace($BackendInventoryRoot)) { Join-Path $repositoryRoot 'backend' } else { (Resolve-Path $BackendInventoryRoot).Path }
$discoveredProjects = @(
    Get-NervStringsSorted -Values @(Get-ChildItem -LiteralPath $backendRoot -Recurse -File -Filter '*.Tests.csproj' |
        Where-Object { $_.FullName -notmatch '[/\\](bin|obj)[/\\]' } |
        ForEach-Object { 'backend/' + ([IO.Path]::GetRelativePath($backendRoot, $_.FullName) -replace '\\', '/') }) -Comparer ([StringComparer]::Ordinal) -Unique
)

$discoveredBackendProjects = @(
    Get-NervStringsSorted -Values @(Get-ChildItem -LiteralPath $backendRoot -Recurse -File -Filter '*.csproj' |
        Where-Object { $_.FullName -notmatch '[/\\](bin|obj)[/\\]' } |
        ForEach-Object { 'backend/' + ([IO.Path]::GetRelativePath($backendRoot, $_.FullName) -replace '\\', '/') }) -Comparer ([StringComparer]::Ordinal) -Unique
)

$directDockerPatterns = @(
    # ProcessStartInfo(string fileName) and ProcessStartInfo(string fileName, string arguments),
    # including the equivalent named-argument spelling.
    'new\s+ProcessStartInfo\s*\(\s*(?:fileName\s*:\s*)?"docker"\s*(?:,|\))',
    # ProcessStartInfo() followed by the FileName setter through an object initializer.
    'new\s+ProcessStartInfo\s*(?:\(\s*\))?\s*\{(?s:[^}]*?)\bFileName\s*=\s*"docker"\s*(?:,|\})',
    # Process.Start(string fileName) and Process.Start(string fileName, string arguments).
    '(?:System\s*\.\s*Diagnostics\s*\.\s*)?\bProcess\s*\.\s*Start\s*\(\s*(?:fileName\s*:\s*)?"docker"\s*(?:,|\))'
)
$testProjectPaths = @(
    Get-NervStringsSorted -Values @(Get-ChildItem -LiteralPath $backendRoot -Recurse -File -Filter '*.Tests.csproj' |
        Where-Object { $_.FullName -notmatch '[/\\](bin|obj)[/\\]' } |
        ForEach-Object { $_.FullName }) -Comparer ([StringComparer]::Ordinal) -Unique
)
$auditedSourceProjects = @{}
foreach ($testProjectPath in $testProjectPaths) {
    $testProjectDirectory = Split-Path -Parent $testProjectPath
    $relativeTestProjectPath = 'backend/' + ([IO.Path]::GetRelativePath($backendRoot, $testProjectPath) -replace '\\', '/')
    foreach ($sourceFile in Get-ChildItem -LiteralPath $testProjectDirectory -Recurse -File -Filter '*.cs' |
            Where-Object { $_.FullName -notmatch '[/\\](bin|obj)[/\\]' }) {
        if ($auditedSourceProjects.ContainsKey([string] $sourceFile.FullName)) {
            $errors.Add("Backend test source '$($sourceFile.FullName)' maps to more than one containing test project: $($auditedSourceProjects[[string] $sourceFile.FullName]), $relativeTestProjectPath.")
            continue
        }
        $auditedSourceProjects[[string] $sourceFile.FullName] = $relativeTestProjectPath

        $sourceText = Get-Content -LiteralPath $sourceFile.FullName -Raw
        $structuralText = ConvertTo-NervCSharpStructuralText -SourceText $sourceText
        $directDockerMatches = @(
            foreach ($directDockerPattern in $directDockerPatterns) {
                [regex]::Matches($structuralText, $directDockerPattern)
            }
        )
        if ($directDockerMatches.Count -eq 0) {
            continue
        }

        $ownerExcludedClassSelectors = $null
        if ($ambiguousProjectOwners.Contains([string] $relativeTestProjectPath)) {
            $errors.Add("Real dependency Docker CLI primitive project '$relativeTestProjectPath' has ambiguous shard ownership.")
        }
        elseif (-not $projectOwners.ContainsKey($relativeTestProjectPath)) {
            $errors.Add("Real dependency Docker CLI primitive project '$relativeTestProjectPath' has no owning shard.")
        }
        else {
            $projectOwner = [string] $projectOwners[$relativeTestProjectPath]
            if ($excludedClassSelectorsByFastShard.ContainsKey($projectOwner)) {
                $ownerExcludedClassSelectors = $excludedClassSelectorsByFastShard[$projectOwner]
            }
            elseif ($heavyLaneIdSet.Contains($projectOwner)) {
                # A heavy lane is the intended home for real-dependency tests. Its owner script and
                # evidence policy govern execution, so fast-shard exclusion is neither required nor
                # meaningful for a project classified wholly into that lane.
                continue
            }
            else {
                $errors.Add("Real dependency Docker CLI primitive project '$relativeTestProjectPath' has unknown lane ownership '$projectOwner'.")
            }
        }

        $relativeSourcePath = 'backend/' + ([IO.Path]::GetRelativePath($backendRoot, $sourceFile.FullName) -replace '\\', '/')
        $namespaceMatches = @([regex]::Matches($structuralText, '(?m)^\s*namespace\s+(?<name>[A-Za-z_][A-Za-z0-9_.]*)\s*[;{]'))
        $classRanges = @(Get-NervCSharpClassRanges -StructuralText $structuralText)
        if ($namespaceMatches.Count -ne 1 -or $classRanges.Count -eq 0) {
            $errors.Add("Real dependency Docker CLI primitive in '$relativeSourcePath' could not be mapped to a namespace and test type.")
            continue
        }

        foreach ($directDockerMatch in $directDockerMatches) {
            $containingClasses = @($classRanges |
                Where-Object { $_.OpenBraceIndex -lt $directDockerMatch.Index -and $_.CloseBraceIndex -gt $directDockerMatch.Index })
            if ($containingClasses.Count -eq 0) {
                $errors.Add("Real dependency Docker CLI primitive in '$relativeSourcePath' could not be mapped to a namespace and test type.")
                continue
            }

            # Nested helpers belong to the outer test class selector used by VSTest and the shard manifest.
            $fullyQualifiedType = "$($namespaceMatches[0].Groups['name'].Value).$($containingClasses[0].Name)"
            if ($null -eq $ownerExcludedClassSelectors -or -not $ownerExcludedClassSelectors.Contains([string] $fullyQualifiedType)) {
                $errors.Add("Real dependency test type '$fullyQualifiedType' uses the audited Docker CLI primitive but is not excluded from its fast shard.")
            }
        }
    }
}

$unclassifiedProjects = @($discoveredProjects | Where-Object { -not $projectOwners.ContainsKey($_) })
if ($unclassifiedProjects.Count -gt 0) {
    $errors.Add("Unclassified backend test projects: $($unclassifiedProjects -join ', ').")
}

$discoveredProjectSet = Get-NervStringSet -Values $discoveredProjects -Comparer ([StringComparer]::Ordinal)
$unknownClassifications = @($projectOwners.Keys | Where-Object { -not $discoveredProjectSet.Contains([string]$_) })
if ($unknownClassifications.Count -gt 0) {
    $errors.Add("Classified projects are not discovered backend test projects: $($unknownClassifications -join ', ').")
}

$solutionPath = Join-Path $repositoryRoot ([string] $manifest.solution)
if (-not (Test-Path -LiteralPath $solutionPath -PathType Leaf)) {
    $errors.Add("Configured backend solution does not exist: $($manifest.solution).")
}
else {
    $solutionProjects = @(
        Get-NervStringsSorted -Values @(Get-Content -LiteralPath $solutionPath |
            ForEach-Object {
                if ($_ -match '"(?<path>[^" ]*\.csproj)"') {
                    'backend/' + ($Matches.path -replace '\\', '/')
                }
            }) -Comparer ([StringComparer]::Ordinal) -Unique
    )
    $solutionProjectSet = Get-NervStringSet -Values $solutionProjects -Comparer ([StringComparer]::Ordinal)
    $projectsMissingFromSolution = @($discoveredProjects | Where-Object { -not $solutionProjectSet.Contains([string]$_) })
    if ($projectsMissingFromSolution.Count -gt 0) {
        $errors.Add("Backend test projects must also be in backend/Nerv.IIP.sln: $($projectsMissingFromSolution -join ', ').")
    }

    # Solution membership is a build-configuration invariant, not bookkeeping, and it covers *every*
    # backend project rather than only the test ones. Each shard builds a `.slnf` over
    # backend/Nerv.IIP.sln with `--configuration Release`, and MSBuild resolves a project's
    # configuration through the solution's configuration map. A project that is only reachable as a
    # transitive ProjectReference has no entry in that map, so it falls back to its own default and
    # is emitted into bin/Debug — the shard then runs Release test assemblies linked against a Debug
    # dependency, on every shard at once, with nothing in the build output that fails. That is
    # exactly what backend/common/Contracts/Nerv.IIP.Contracts.Mes did until MAN-669 PR-B (visible
    # as one `-> …/bin/Debug/net10.0/…` line in each shard log of run 31136085020).
    #
    # There is deliberately NO allowlist and no owner-issue escape hatch here, unlike
    # backend/test-determinism-baseline.json. A registered exception would be a project that is
    # knowingly built under the wrong configuration, which is not a debt anyone can carry — the
    # coverage is currently 163/163 with no gap, and keeping it that way is cheaper than governing
    # exceptions. If a future change genuinely needs a backend project outside the solution, the
    # exemption path is to edit this script (with its own contract test) and go through script
    # governance; see docs/architecture/script-automation-governance.md.
    $projectsMissingFromSolutionSet = Get-NervStringSet -Values $projectsMissingFromSolution -Comparer ([StringComparer]::Ordinal)
    $backendProjectsMissingFromSolution = @(
        $discoveredBackendProjects |
            Where-Object { -not $solutionProjectSet.Contains([string]$_) -and -not $projectsMissingFromSolutionSet.Contains([string]$_) }
    )
    if ($backendProjectsMissingFromSolution.Count -gt 0) {
        $errors.Add("Backend projects must be registered in backend/Nerv.IIP.sln, otherwise a Release shard build resolves them through their own default configuration and emits them into bin/Debug: $($backendProjectsMissingFromSolution -join ', ').")
    }
}

foreach ($shard in $fastShards) {
    if ([string]::IsNullOrWhiteSpace($shard.solutionFilter)) {
        continue
    }

    # A shard exists to restore and build only its own dependency closure. Pointing it at
    # backend/Nerv.IIP.sln would keep the "shard" label while every job rebuilt the whole solution
    # again, which MAN-669 PR-B measured and rejected. The measurements, run ids and re-open
    # conditions live in exactly one place — docs/architecture/backend-ci-build-strategy.md — and
    # are deliberately not restated here: a restated number is a number that drifts the next time
    # MAN-664 re-measures. The rejection is explicit because the JSON parse below would otherwise
    # report the solution as a malformed solution filter and hide what actually happened.
    #
    # Note this is the narrow case only. A `.slnf` that *lists* the whole solution is already
    # rejected further down by "solution filter must match manifest projects exactly", which
    # predates MAN-669 PR-B. Both sides are canonicalized to an absolute path first (see
    # Get-CanonicalRepoPath) and compared case-insensitively, so every spelling of the same file —
    # `./backend/…`, `backend\…`, `backend//…`, `backend/./…`, `backend/../backend/…`, an absolute
    # path, or `backend/nerv.iip.sln` — lands in this branch instead of the "invalid JSON" report.
    $normalizedFilter = Get-CanonicalRepoPath -RepositoryRoot $repositoryRoot -Path ([string] $shard.solutionFilter)
    $normalizedSolution = Get-CanonicalRepoPath -RepositoryRoot $repositoryRoot -Path ([string] $manifest.solution)
    if ([string]::Equals($normalizedFilter, $normalizedSolution, [StringComparison]::OrdinalIgnoreCase)) {
        $errors.Add("Fast shard '$($shard.id)' must build its own solution filter, not the whole backend solution.")
        continue
    }

    $filterPath = Join-Path $repositoryRoot ([string] $shard.solutionFilter)
    if (-not (Test-Path -LiteralPath $filterPath -PathType Leaf)) {
        $errors.Add("Fast shard '$($shard.id)' solution filter does not exist: $($shard.solutionFilter).")
        continue
    }

    try {
        $filter = Get-Content -LiteralPath $filterPath -Raw | ConvertFrom-Json
        $filterSolutionPath = Join-Path (Split-Path -Parent $filterPath) $filter.solution.path
        $filterSolutionDirectory = Split-Path -Parent $filterSolutionPath
        $filterProjects = @(
            Get-NervStringsSorted -Values @(@($filter.solution.projects) |
                ForEach-Object {
                    Get-RepoRelativePath -RepositoryRoot $repositoryRoot -Path (Join-Path $filterSolutionDirectory $_)
                }) -Comparer ([StringComparer]::Ordinal) -Unique
        )
        $manifestProjects = @(Get-NervStringsSorted -Values @($shard.projects | ForEach-Object { $_ -replace '\\', '/' }) -Comparer ([StringComparer]::Ordinal) -Unique)
        $filterProjectSet = Get-NervStringSet -Values $filterProjects -Comparer ([StringComparer]::Ordinal)
        $manifestProjectSet = Get-NervStringSet -Values $manifestProjects -Comparer ([StringComparer]::Ordinal)
        $missingFromFilter = @($manifestProjects | Where-Object { -not $filterProjectSet.Contains([string]$_) })
        $unexpectedInFilter = @($filterProjects | Where-Object { -not $manifestProjectSet.Contains([string]$_) })
        if ($missingFromFilter.Count -gt 0 -or $unexpectedInFilter.Count -gt 0) {
            $errors.Add("Fast shard '$($shard.id)' solution filter must match manifest projects exactly. Missing: $($missingFromFilter -join ', '); unexpected: $($unexpectedInFilter -join ', ').")
        }
    }
    catch {
        $errors.Add("Fast shard '$($shard.id)' solution filter is invalid JSON: $($_.Exception.Message)")
    }
}

$resolvedWorkflowPath = Resolve-Path $WorkflowPath -ErrorAction SilentlyContinue
if ($null -eq $resolvedWorkflowPath) {
    $errors.Add("Configured CI workflow does not exist: $WorkflowPath.")
}
else {
    try {
        $workflow = ConvertFrom-CiWorkflowYaml -Path $resolvedWorkflowPath.Path -WorkingDirectory $repositoryRoot
        $jobs = $workflow.jobs
        if ($null -eq $jobs) {
            $errors.Add('CI workflow must contain a jobs mapping.')
        }
        else {
            $fastJobIds = @($fastShards | ForEach-Object { "backend-tests-$($_.id)" })
            foreach ($shard in $fastShards) {
                $jobId = "backend-tests-$($shard.id)"
                $job = $jobs.PSObject.Properties[$jobId].Value
                if ($null -eq $job) {
                    $errors.Add("CI workflow is missing fast shard job '$jobId'.")
                    continue
                }

                $lane = [string] $shard.evidenceLane
                $shardJobName = [string] $shard.jobName
                if (-not [string]::Equals((Get-WorkflowStringValue -Object $job -PropertyName 'name'), $shardJobName, [StringComparison]::OrdinalIgnoreCase)) {
                    $errors.Add("Fast shard job '$jobId' must be named '$shardJobName' so the evidence lane maps to one allowlisted job.")
                }

                $runText = (Get-WorkflowStepValues -Steps @($job.steps) -PropertyName 'run') -join "`n"
                if ($runText -notmatch [regex]::Escape("scripts/run-backend-test-shard.ps1 -ShardId $($shard.id)")) {
                    $errors.Add("Fast shard job '$jobId' must run the governed shard runner for '$($shard.id)'.")
                }
                if ($runText -match '(?m)(?:^|\s)-TestCommand(?:\s|$)') {
                    $errors.Add("Fast shard job '$jobId' must not supply a command replacement parameter.")
                }

                $rawResultsDirectory = "artifacts/test-evidence-raw/`${{ github.run_id }}/attempt-`${{ github.run_attempt }}/$lane"
                $evidenceDirectory = "artifacts/test-evidence/`${{ github.run_id }}/attempt-`${{ github.run_attempt }}/$lane"
                if ($runText -notmatch [regex]::Escape("-ResultsDirectory $rawResultsDirectory")) {
                    $errors.Add("Fast shard job '$jobId' must write raw TRX only to the job-local evidence input '$rawResultsDirectory'.")
                }
                if ($runText -notmatch [regex]::Escape("-TrxFilePrefix $jobId")) {
                    $errors.Add("Fast shard job '$jobId' must use its unique TRX file prefix '$jobId'.")
                }

                $testSteps = @(Get-WorkflowStepsById -Steps @($job.steps) -StepId 'shard-tests')
                if ($testSteps.Count -ne 1) {
                    $errors.Add("Fast shard job '$jobId' must declare exactly one 'shard-tests' step whose native exit code is authoritative.")
                }
                else {
                    $testStepRun = Get-WorkflowStringValue -Object $testSteps[0] -PropertyName 'run'
                    if ($testStepRun -match '\|') {
                        $errors.Add("Fast shard job '$jobId' test step must not wrap the shard runner in a shell pipeline.")
                    }
                    if ($null -ne $testSteps[0].PSObject.Properties['continue-on-error']) {
                        $errors.Add("Fast shard job '$jobId' test step must not set 'continue-on-error'.")
                    }
                }
                if ($null -ne $job.PSObject.Properties['continue-on-error']) {
                    $errors.Add("Fast shard job '$jobId' must not set 'continue-on-error'.")
                }

                $collectSteps = @(Get-WorkflowStepsById -Steps @($job.steps) -StepId 'collect-shard-evidence')
                if ($collectSteps.Count -ne 1) {
                    $errors.Add("Fast shard job '$jobId' must collect MAN-661 evidence in exactly one 'collect-shard-evidence' step.")
                }
                else {
                    $collectStep = $collectSteps[0]
                    $collectRun = Get-WorkflowStringValue -Object $collectStep -PropertyName 'run'
                    if ((-not [string]::Equals([string]((Get-WorkflowStringValue -Object $collectStep -PropertyName 'if')), [string]('always()'), [StringComparison]::OrdinalIgnoreCase))) {
                        $errors.Add("Fast shard job '$jobId' evidence collection must run with if: always().")
                    }
                    if ($null -ne $collectStep.PSObject.Properties['continue-on-error']) {
                        $errors.Add("Fast shard job '$jobId' evidence collection must not set 'continue-on-error'.")
                    }
                    foreach ($requiredArgument in @(
                            'scripts/collect-test-evidence.ps1',
                            "-Lane $lane",
                            "-SelectedLanes $lane",
                            "-ResultsDirectory $rawResultsDirectory",
                            "-OutputDirectory $evidenceDirectory",
                            "-JobName `"$shardJobName`"",
                            '-CurrentTestOutcome ${{ steps.shard-tests.outcome }}',
                            '-RetentionDays 14'
                        )) {
                        if ($collectRun -notmatch [regex]::Escape($requiredArgument)) {
                            $errors.Add("Fast shard job '$jobId' evidence collection must pass '$requiredArgument'.")
                        }
                    }
                    foreach ($siblingLane in @($fastShards | Where-Object { (-not [string]::Equals([string]([string] $_.id), [string]([string] $shard.id), [StringComparison]::OrdinalIgnoreCase)) } | ForEach-Object { [string] $_.evidenceLane })) {
                        if ($collectRun -match [regex]::Escape($siblingLane)) {
                            $errors.Add("Fast shard job '$jobId' must not claim the sibling evidence lane '$siblingLane'.")
                        }
                    }
                }

                $uploads = @($job.steps | Where-Object {
                        $uses = $_.PSObject.Properties['uses']
                        $null -ne $uses -and [string]::Equals([string]([string] $uses.Value), [string]('actions/upload-artifact@v4'), [StringComparison]::OrdinalIgnoreCase)
                    })
                if ($uploads.Count -ne 1 -or (-not [string]::Equals([string]((Get-WorkflowStringValue -Object $uploads[0] -PropertyName 'if')), [string]('always()'), [StringComparison]::OrdinalIgnoreCase))) {
                    $errors.Add("Fast shard job '$jobId' must always upload exactly one redacted evidence artifact.")
                }
                else {
                    $uploadWith = $uploads[0].with
                    if ((-not [string]::Equals([string]((Get-WorkflowStringValue -Object $uploadWith -PropertyName 'path')), [string]('${{ steps.collect-shard-evidence.outputs.evidence-path }}'), [StringComparison]::OrdinalIgnoreCase))) {
                        $errors.Add("Fast shard job '$jobId' must upload only the collector-published redacted evidence path.")
                    }
                    if ((-not [string]::Equals([string]((Get-WorkflowStringValue -Object $uploadWith -PropertyName 'name')), [string]("test-evidence-$lane-`${{ github.run_id }}-`${{ github.run_attempt }}"), [StringComparison]::OrdinalIgnoreCase))) {
                        $errors.Add("Fast shard job '$jobId' evidence artifact must use its unique lane-scoped artifact name.")
                    }
                    if ((-not [string]::Equals([string]((Get-WorkflowStringValue -Object $uploadWith -PropertyName 'if-no-files-found')), [string]('error'), [StringComparison]::OrdinalIgnoreCase)) -or
                        (-not [string]::Equals([string]((Get-WorkflowStringValue -Object $uploadWith -PropertyName 'retention-days')), [string]('14'), [StringComparison]::OrdinalIgnoreCase))) {
                        $errors.Add("Fast shard job '$jobId' evidence artifact must fail closed on missing files and retain for 14 days.")
                    }
                }
                foreach ($upload in $uploads) {
                    if ((Get-WorkflowStringValue -Object $upload.with -PropertyName 'path') -match 'test-evidence-raw') {
                        $errors.Add("Fast shard job '$jobId' must never upload the job-local raw TRX directory.")
                    }
                }
            }

            $aggregate = $jobs.PSObject.Properties['backend-tests'].Value
            if ($null -eq $aggregate) {
                $errors.Add("CI workflow is missing the stable 'backend-tests' aggregate job.")
            }
            else {
                $expectedNeeds = @('backend-test-shard-governance') + $fastJobIds
                $actualNeeds = @($aggregate.needs | ForEach-Object { [string] $_ })
                $expectedNeedSet = Get-NervStringSet -Values $expectedNeeds -Comparer ([StringComparer]::Ordinal)
                $actualNeedSet = Get-NervStringSet -Values $actualNeeds -Comparer ([StringComparer]::Ordinal)
                $missingNeeds = @($expectedNeeds | Where-Object { -not $actualNeedSet.Contains([string] $_) })
                $unexpectedNeeds = @($actualNeeds | Where-Object { -not $expectedNeedSet.Contains([string] $_) })
                if ($actualNeeds.Count -ne $expectedNeeds.Count -or $missingNeeds.Count -gt 0 -or $unexpectedNeeds.Count -gt 0) {
                    $errors.Add("Backend Tests aggregate must need exactly the governance and four fast shard jobs.")
                }
                if ((-not [string]::Equals([string]([string] $aggregate.name), [string]('Backend Tests'), [StringComparison]::OrdinalIgnoreCase)) -or (-not [string]::Equals([string]([string] $aggregate.if), [string]('always()'), [StringComparison]::OrdinalIgnoreCase))) {
                    $errors.Add("Backend Tests aggregate must retain name 'Backend Tests' and if: always().")
                }
                $aggregateHasContinueOnError = $null -ne $aggregate.PSObject.Properties['continue-on-error'] -or @(
                    @($aggregate.steps) | Where-Object { $null -ne $_.PSObject.Properties['continue-on-error'] }
                ).Count -gt 0
                if ($aggregateHasContinueOnError) {
                    $errors.Add("Backend Tests aggregate must not set 'continue-on-error' on the job or any step.")
                }

                $aggregateRun = (Get-WorkflowStepValues -Steps @($aggregate.steps) -PropertyName 'run') -join "`n"
                $aggregateCommands = @(
                    $aggregateRun -split "`r?`n" |
                        ForEach-Object { $_.Trim() } |
                        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
                )
                $requiredAssertions = @()
                $aggregateCommandSet = Get-NervStringSet -Values $aggregateCommands -Comparer ([StringComparer]::Ordinal)
                foreach ($requiredJob in $expectedNeeds) {
                    $requiredAssertion = 'test "${{ needs.' + $requiredJob + '.result }}" = "success"'
                    $requiredAssertions += $requiredAssertion
                    if (-not $aggregateCommandSet.Contains([string]$requiredAssertion)) {
                        $errors.Add("Backend Tests aggregate must fail when '$requiredJob' is not success.")
                    }
                }
                $actualAggregateAssertions = @(Get-NervStringsSorted -Values @($aggregateCommands) -Comparer ([StringComparer]::Ordinal)) -join '|'
                $expectedAggregateAssertions = @(Get-NervStringsSorted -Values @($requiredAssertions) -Comparer ([StringComparer]::Ordinal)) -join '|'
                if ($aggregateCommands.Count -ne $requiredAssertions.Count -or -not [string]::Equals($actualAggregateAssertions, $expectedAggregateAssertions, [StringComparison]::Ordinal)) {
                    $errors.Add('Backend Tests aggregate must contain only standalone success assertions for its exact dependencies.')
                }
            }
        }
    }
    catch {
        $errors.Add("CI workflow must be valid structured YAML: $($_.Exception.Message)")
    }
}

# Findings go to stdout and the script exits nonzero, the same shape as
# scripts/check-script-governance.ps1 and scripts/verify-solution-configuration-membership.ps1 —
# deliberately not `throw`, and callers must therefore check the exit code. In particular this file
# must never share a `run:` block with another script; .github/workflows/ci.yml gives it its own
# step. Why both rules hold is argued once, in docs/architecture/backend-ci-build-strategy.md
# ("走查收尾" 第 3 条).
if ($errors.Count -gt 0) {
    Write-Host 'Backend test shard governance failed:'
    foreach ($failure in $errors) {
        Write-Host "  $failure"
    }

    exit 1
}

Write-Output "Backend test shard governance passed: $($discoveredProjects.Count) projects classified exactly once across $($fastShards.Count) fast shards and $($heavyLanes.Count) heavy lanes; $($excludedClassOwners.Count) real test selectors are explicitly owned outside fast shards; $($discoveredBackendProjects.Count) backend projects are solution members and therefore build under the shard's own Release configuration."
