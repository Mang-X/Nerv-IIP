# Script-Governance:
#   Category: library
#   SideEffects:
#     - None; defines TestEvidence policy functions
#   Writes:
#     - None
#   Requires:
#     - PowerShell 7
function New-NervTestEvidenceViolation {
    param([string] $Code, [string] $Id, [string] $Message)
    [pscustomobject]@{ code = $Code; id = $Id; message = $Message }
}

function Import-NervTestEvidencePolicy {
    param([Parameter(Mandatory)] [string] $Path)
    $policy = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -Depth 100
    if ([int] $policy.schemaVersion -ne 1) {
        throw "Unsupported test-evidence policy schemaVersion '$($policy.schemaVersion)'."
    }
    return $policy
}

function Test-NervQuarantineRuleMetadata {
    param(
        [Parameter(Mandatory)] [object] $Rule,
        [Parameter(Mandatory)] [DateTimeOffset] $AsOfUtc
    )

    $expiry = [DateTimeOffset]::MinValue
    $validDate = [DateTimeOffset]::TryParseExact(
        [string]$Rule.expiresOn,
        'yyyy-MM-dd',
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::AssumeUniversal,
        [ref]$expiry)
    return -not [string]::IsNullOrWhiteSpace([string]$Rule.responsibilityIssue) -and
        -not [string]::IsNullOrWhiteSpace([string]$Rule.exitCondition) -and
        $validDate -and
        $expiry.Date -ge $AsOfUtc.UtcDateTime.Date
}

function Get-NervSourceSkipAssignments {
    param([Parameter(Mandatory)] [string] $RepoRoot)

    $roots = @(
        (Join-Path $RepoRoot 'backend/tests'),
        (Join-Path $RepoRoot 'backend/services'),
        (Join-Path $RepoRoot 'connector-hosts/tests')
    )
    $files = foreach ($root in $roots) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        Get-ChildItem -LiteralPath $root -Filter '*.cs' -File -Recurse | Where-Object {
            $relative = [IO.Path]::GetRelativePath($RepoRoot, $_.FullName).Replace('\', '/')
            $relative -match '^(backend/tests/|backend/services/[^/]+/tests/|backend/services/Business/[^/]+/tests/|connector-hosts/tests/)'
        }
    }

    # Ordinal, unique (#1509): a file path is an identifier. `Sort-Object FullName -Unique` folds two
    # distinct paths differing by an ignorable character into one, which silently removes a source
    # file — and every Skip assignment in it — from the live scan this gate is built on.
    $filesByPath = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    foreach ($file in @($files)) {
        if (-not $filesByPath.ContainsKey([string]$file.FullName)) { $filesByPath[[string]$file.FullName] = $file }
    }
    $results = foreach ($filePath in @(Get-NervOrdinalSorted -Values @($filesByPath.Keys))) {
        $file = $filesByPath[$filePath]
        $relative = [IO.Path]::GetRelativePath($RepoRoot, $file.FullName).Replace('\', '/')
        $content = Get-Content -LiteralPath $file.FullName -Raw
        $starts = @([regex]::Matches($content, '\bSkip\s*=') | ForEach-Object Index)
        for ($index = 0; $index -lt $starts.Count; $index++) {
            $start = [int]$starts[$index]
            $position = $start
            while ($position -lt $content.Length) {
                $character = $content[$position]
                if ($character -eq [char]'"' -or $character -eq [char]"'") {
                    $position = Find-NervQuotedTextEnd -Text $content -QuoteStart $position -AllowCSharpVerbatim
                    continue
                }
                elseif ($character -eq [char]';') {
                    break
                }
                $position++
            }
            if ($position -ge $content.Length) { continue }
            $sourceText = [regex]::Replace($content.Substring($start, $position - $start + 1), '\s+', ' ').Trim()
            [pscustomobject]@{
                sourcePath = $relative
                sourceOrdinal = $index + 1
                sourceText = $sourceText
            }
        }
    }
    @($results)
}

function Test-NervTestEvidencePolicy {
    param(
        [Parameter(Mandatory)] [object] $Policy,
        [Parameter(Mandatory)] [string] $RepoRoot,
        [Parameter(Mandatory)] [DateTimeOffset] $AsOfUtc
    )

    $violations = [Collections.Generic.List[object]]::new()
    $classifications = @('optional', 'environment-gated', 'quarantined')
    foreach ($kind in @('sources', 'rules')) {
        $duplicates = @(Get-NervOrdinalGroups -Items @($Policy.$kind) -KeySelector { param($row) [string]$row.id } |
            Where-Object { @($_.Group).Count -gt 1 })
        foreach ($duplicate in $duplicates) {
            $violations.Add((New-NervTestEvidenceViolation 'unregistered-skip' $duplicate.Name "Duplicate $kind id '$($duplicate.Name)'."))
        }
    }
    foreach ($lane in @($Policy.lanes)) {
        try { [void][regex]::new([string]$lane.namePattern) }
        catch { $violations.Add((New-NervTestEvidenceViolation 'unregistered-skip' ([string]$lane.namePattern) 'Invalid lane pattern.')) }
    }
    foreach ($rule in @($Policy.rules)) {
        $sourceMatches = @($Policy.sources | Where-Object { Test-NervOrdinalEquals ([string]$_.id) ([string]$rule.sourceId) })
        if ($sourceMatches.Count -ne 1) {
            $violations.Add((New-NervTestEvidenceViolation 'unregistered-skip' ([string]$rule.id) 'Rule sourceId must resolve to exactly one registered source.'))
        }
        if (-not (Get-NervOrdinalSet -Values $classifications).Contains([string]$rule.classification)) {
            $violations.Add((New-NervTestEvidenceViolation 'illegal-quarantine' ([string]$rule.id) "Unknown classification '$($rule.classification)'."))
            continue
        }
        foreach ($patternName in @('testPattern', 'reasonPattern')) {
            $pattern = [string]$rule.$patternName
            # Ordinal (#1509 round 3): `.StartsWith('^')` with no [StringComparison] is culture-aware,
            # and `"$([char]0x00AD)^x".StartsWith('^')` is True — an unanchored pattern prefixed with
            # one ignorable character passes the anchor guard and then matches far more skip reasons
            # than the rule froze. Both ends, same reason.
            if (-not ($pattern.StartsWith('^', [StringComparison]::Ordinal) -and $pattern.EndsWith('$', [StringComparison]::Ordinal))) {
                $violations.Add((New-NervTestEvidenceViolation 'unregistered-skip' ([string]$rule.id) "$patternName must be fully anchored."))
                continue
            }
            try { [void][regex]::new($pattern) }
            catch { $violations.Add((New-NervTestEvidenceViolation 'unregistered-skip' ([string]$rule.id) "Invalid $patternName.")) }
        }
        $identities = if (Test-NervHasProperty -Object $rule -Name 'testIdentities') { @($rule.testIdentities) } else { @() }
        $expectedCount = if (Test-NervHasProperty -Object $rule -Name 'expectedRuntimeTestCount') { [int]$rule.expectedRuntimeTestCount } else { 0 }
        # Uniqueness is ordinal: a frozen identity is an identifier, and `Sort-Object -Unique` is
        # culture-aware, so two rows differing only by an ignorable character (U+00AD is the one
        # #1509 measured) collapse into one and the count check reports the wrong reason.
        $uniqueIdentities = Get-NervOrdinalSet -Values @($identities | ForEach-Object { [string]$_ })
        if (@($identities).Count -eq 0 -or $expectedCount -ne @($identities).Count -or $uniqueIdentities.Count -ne @($identities).Count) {
            $violations.Add((New-NervTestEvidenceViolation 'unregistered-skip' ([string]$rule.id) 'Rule must freeze a non-empty unique test identity set and exact expectedRuntimeTestCount.'))
        }
        foreach ($identity in $identities) {
            # Padding ruling (#1509): every consumer compares a frozen identity by ordinal equality
            # or ordinal prefix — Get-BackendTestShardPolicyIdentityMatches, the runtime rule matcher
            # below, the shard exclusion gate. None of them trims, and none of them should: trimming
            # at the point of comparison would let two rows MAN-661 stores as distinct strings
            # resolve to the same selector while the padding survives into the evidence key. So the
            # padding is rejected here, at the only boundary where the policy text is authored, and
            # `identity as written == identity as compared` holds everywhere downstream. An anchored
            # testPattern already rejects *leading* whitespace as a side effect; trailing whitespace
            # used to pass, because `.+$` happily consumes it.
            $identityText = [string]$identity
            if ($identityText.Length -ne $identityText.Trim().Length) {
                $violations.Add((New-NervTestEvidenceViolation 'unregistered-skip' ([string]$rule.id) "Frozen test identity '$identityText' must not carry leading or trailing whitespace; identities are compared as written."))
            }
            if ($identityText -cnotmatch [string]$rule.testPattern) {
                $violations.Add((New-NervTestEvidenceViolation 'unregistered-skip' ([string]$rule.id) "Frozen test identity '$identity' does not match testPattern."))
            }
        }
        foreach ($laneName in @($rule.allowedLanes) + @($rule.requiredLane | Where-Object { $_ })) {
            if (-not (Test-NervTestEvidenceLaneName ([string]$laneName))) {
                $violations.Add((New-NervTestEvidenceViolation 'unregistered-skip' ([string]$rule.id) "Invalid lane '$laneName'."))
            }
        }
        if (Test-NervOrdinalEquals ([string]$rule.classification) 'quarantined') {
            if (-not (Test-NervQuarantineRuleMetadata -Rule $rule -AsOfUtc $AsOfUtc)) {
                $violations.Add((New-NervTestEvidenceViolation 'illegal-quarantine' ([string]$rule.id) 'Quarantine requires issue, valid unexpired ISO date, and exit condition.'))
            }
        }
    }

    $live = @(Get-NervSourceSkipAssignments -RepoRoot $RepoRoot)
    foreach ($assignment in $live) {
        $matchedSources = @($Policy.sources | Where-Object {
            (Test-NervOrdinalEquals ([string]$_.sourcePath) ([string]$assignment.sourcePath)) -and
            [int]$_.sourceOrdinal -eq [int]$assignment.sourceOrdinal -and
            [string]$assignment.sourceText -cmatch [string]$_.sourceReasonPattern
        })
        if ($matchedSources.Count -ne 1) {
            $id = "$($assignment.sourcePath):$($assignment.sourceOrdinal)"
            $violations.Add((New-NervTestEvidenceViolation 'unregistered-skip' $id 'Source Skip assignment is missing, duplicated, or reason-mismatched.'))
        }
    }
    foreach ($source in @($Policy.sources)) {
        if (@($Policy.rules | Where-Object { Test-NervOrdinalEquals ([string]$_.sourceId) ([string]$source.id) }).Count -eq 0) {
            $violations.Add((New-NervTestEvidenceViolation 'unregistered-skip' ([string]$source.id) 'Registered source is not referenced by any runtime rule.'))
        }
        $matchedAssignments = @($live | Where-Object {
            (Test-NervOrdinalEquals ([string]$_.sourcePath) ([string]$source.sourcePath)) -and
            [int]$_.sourceOrdinal -eq [int]$source.sourceOrdinal
        })
        if ($matchedAssignments.Count -ne 1) {
            $violations.Add((New-NervTestEvidenceViolation 'unregistered-skip' ([string]$source.id) 'Registered source does not map to exactly one live Skip assignment.'))
        }
    }
    @($violations)
}

function Test-NervRuleApplies {
    param(
        [Parameter(Mandatory)] [object] $Rule,
        [Parameter(Mandatory)] [string[]] $SelectedLanes,
        [Parameter(Mandatory)] [string] $RunnerOs
    )

    # Ordinal membership, not `-ccontains` (#1509): lane names, the required lane and the runner OS
    # are identifiers, and the `c` prefix only turns off case-insensitivity — the comparison stays
    # culture-aware, so a lane carrying an ignorable character resolves to a rule that does not
    # govern it. This is the applicability condition the `zero-execution` hard gate is built on, so a
    # fold here silently changes which rule applies.
    $baseLanes = @($SelectedLanes | ForEach-Object { [string]($_ -replace '-shard-[1-9][0-9]*$', '') })
    $allowedLanes = Get-NervOrdinalSet -Values @(@($Rule.allowedLanes) | ForEach-Object { [string]$_ })
    if ($allowedLanes.Count -gt 0 -and @($baseLanes | Where-Object { $allowedLanes.Contains($_) }).Count -eq 0) {
        return $false
    }
    $baseLaneSet = Get-NervOrdinalSet -Values $baseLanes
    if (-not [string]::IsNullOrWhiteSpace([string]$Rule.requiredLane) -and $baseLaneSet.Contains([string]$Rule.requiredLane)) {
        return $false
    }
    $allowedOperatingSystems = Get-NervOrdinalSet -Values @(@($Rule.allowedOperatingSystems) | ForEach-Object { [string]$_ })
    if ($allowedOperatingSystems.Count -gt 0 -and -not $allowedOperatingSystems.Contains($RunnerOs)) {
        return $false
    }
    return $true
}

function Get-NervTestEvidenceViolations {
    param(
        [Parameter(Mandatory)] [AllowNull()] [AllowEmptyCollection()] [object[]] $Records,
        [Parameter(Mandatory)] [object] $Policy,
        [Parameter(Mandatory)] [string[]] $SelectedLanes,
        [Parameter(Mandatory)] [string] $RunnerOs
    )

    $violations = [Collections.Generic.List[object]]::new()
    $safeRecords = if ($null -eq $Records) { @() } else { @($Records) }
    foreach ($rule in @($Policy.rules | Where-Object { Test-NervOrdinalEquals ([string]$_.classification) 'quarantined' })) {
        if (-not (Test-NervQuarantineRuleMetadata -Rule $rule -AsOfUtc ([DateTimeOffset]::UtcNow))) {
            $violations.Add((New-NervTestEvidenceViolation 'illegal-quarantine' ([string]$rule.id) 'Quarantine metadata is missing, invalid, or expired.'))
        }
    }

    foreach ($record in @($safeRecords | Where-Object { Test-NervOrdinalEquals ([string]$_.outcome) 'skipped' })) {
        $matchedRules = @($Policy.rules | Where-Object {
            # The frozen-identity comparison itself, and therefore the one the padding ruling above
            # depends on: an identity is matched as written, by ordinal equality. `-ccontains` folds
            # ignorable characters, which would let a runtime skip claim a rule that froze a
            # different string.
            @(@($_.testIdentities) | Where-Object { Test-NervOrdinalEquals ([string]$_) ([string]$record.testName) }).Count -gt 0 -and
            [string]$record.testName -cmatch [string]$_.testPattern -and
            [string]$record.skipReason -cmatch [string]$_.reasonPattern -and
            (Test-NervRuleApplies -Rule $_ -SelectedLanes $SelectedLanes -RunnerOs $RunnerOs)
        })
        if ($matchedRules.Count -ne 1) {
            $violations.Add((New-NervTestEvidenceViolation 'unregistered-skip' ([string]$record.testName) "Runtime skip matched $($matchedRules.Count) applicable rules."))
        }
        else {
            $record | Add-Member -NotePropertyName skipClassification -NotePropertyValue ([string]$matchedRules[0].classification) -Force
            $record | Add-Member -NotePropertyName skipPolicyId -NotePropertyValue ([string]$matchedRules[0].id) -Force
        }
    }

    $selectedLaneContracts = [Collections.Generic.List[object]]::new()
    foreach ($selectedLane in @(Get-NervOrdinalSorted -Unique -Values @($SelectedLanes | ForEach-Object { [string]$_ }))) {
        $laneMatches = @($Policy.lanes | Where-Object { $selectedLane -cmatch [string]$_.namePattern })
        if ($laneMatches.Count -ne 1) {
            $violations.Add((New-NervTestEvidenceViolation 'unregistered-skip' $selectedLane "Selected lane matched $($laneMatches.Count) lane contracts."))
            continue
        }
        $selectedLaneContracts.Add([pscustomobject]@{
            selectedLane = $selectedLane
            baseLane = ($selectedLane -replace '-shard-[1-9][0-9]*$', '')
            realDependency = [bool]$laneMatches[0].realDependency
        })
    }
    # Built once, not once per record: inside a Where-Object block this allocated a HashSet for every
    # row it filtered (#1509 round 3 review). It is read-only from here on.
    $executedOutcomes = Get-NervOrdinalSet -Values @('passed', 'failed')
    foreach ($laneGroup in @(Get-NervOrdinalGroups -Items @($selectedLaneContracts) -KeySelector { param($row) [string]$row.baseLane })) {
        if (-not [bool]$laneGroup.Group[0].realDependency) { continue }
        $selectors = @($laneGroup.Group | ForEach-Object { [string]$_.selectedLane })
        $baseLane = [string]$laneGroup.Name
        $executed = @($safeRecords | Where-Object {
            if (-not $executedOutcomes.Contains([string]$_.outcome)) { return $false }
            $recordLane = [string]$_.lane
            # Ordinal (#1509): a lane name is a selector, and this comparison decides whether a lane
            # counted as executed at all — the input to the `zero-execution` gate.
            if ($selectors.Count -eq 1 -and -not [string]::Equals([string]$selectors[0], $baseLane, [StringComparison]::Ordinal)) {
                return [string]::Equals($recordLane, [string]$selectors[0], [StringComparison]::Ordinal)
            }
            return [string]::Equals([string]($recordLane -replace '-shard-[1-9][0-9]*$', ''), $baseLane, [StringComparison]::Ordinal)
        }).Count
        if ($executed -eq 0) {
            $violationId = if ($selectors.Count -eq 1) { [string]$selectors[0] } else { $baseLane }
            $violations.Add((New-NervTestEvidenceViolation 'zero-execution' $violationId 'Selected real-dependency lane executed no passed or failed tests.'))
        }
    }
    @($violations)
}
