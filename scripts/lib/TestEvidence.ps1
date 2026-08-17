# Script-Governance:
#   Category: library
#   SideEffects:
#     - Reads test policy, C# test sources, and VSTest evidence
#   Writes:
#     - None; callers own all evidence output paths
#   Cleanup:
#     - No process or external resource ownership
#   Requires:
#     - PowerShell 7

Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'OrdinalString.ps1')

# --------------------------------------------------------------------------------------------
# Ordinal identifier primitives (#1509 round 2).
#
# Everything this file keys, freezes, groups or certifies on is an identifier: a lane, a selector,
# a frozen test identity, an assembly, a source id, a rule id, a commit SHA, a job name, a violation
# code, an outcome token. PowerShell's defaults are all culture-aware, and the `c` prefix does *not*
# fix that — it only turns off case-insensitivity. Measured on this machine, with `$shy` a single
# U+00AD soft hyphen and `$a = 'alpha'`, `$b = "alpha$shy"`:
#
#   -eq / -ceq / -contains / -in            → True   (the two identifiers compare equal)
#   Sort-Object -Unique                     → 1 item (one identifier disappears)
#   Group-Object -Property / -scriptblock   → 1 group
#   Compare-Object                          → 0 differences
#   Sort-Object (ordering only)             → culture collation, so the order of a retained artifact
#                                             depends on the machine's culture
#   [StringComparer]::Ordinal HashSet       → 2 items  ← the only one that is right
#
# Two constructs measured as *not* folding, and therefore left alone where they appear:
#   [hashtable] / [ordered] .Contains(…)    → False (case-insensitive, but ordinal)
#   [char] comparisons                      → numeric
#
# The sweep is enforced, not just performed: scripts/tests/test-evidence.Tests.ps1 parses this file
# and fails on any culture-aware identifier comparison outside a named allowlist, so a `-ceq` cannot
# come back in quietly.
function Test-NervOrdinalEquals {
    param([AllowNull()] [string] $Left, [AllowNull()] [string] $Right)
    return [string]::Equals([string]$Left, [string]$Right, [StringComparison]::Ordinal)
}

function Resolve-NervTrxOutcomeMapping {
    [CmdletBinding(DefaultParameterSetName = 'TrxOutcome')]
    param(
        [Parameter(Mandatory, ParameterSetName = 'TrxOutcome')]
        [AllowEmptyString()]
        [string] $TrxOutcome,

        [Parameter(Mandatory, ParameterSetName = 'NormalizedOutcome')]
        [AllowEmptyString()]
        [string] $NormalizedOutcome,

        [Parameter(Mandatory, ParameterSetName = 'WriteFallback')]
        [switch] $WriteFallback
    )

    $mappings = @(
        [pscustomobject][ordered]@{ TrxOutcome = 'Passed'; NormalizedOutcome = 'passed'; IsWriteFallback = $false },
        [pscustomobject][ordered]@{ TrxOutcome = 'Failed'; NormalizedOutcome = 'failed'; IsWriteFallback = $false },
        [pscustomobject][ordered]@{ TrxOutcome = 'NotExecuted'; NormalizedOutcome = 'skipped'; IsWriteFallback = $true }
    )

    if ($WriteFallback) {
        $fallbacks = @($mappings | Where-Object { [bool]$_.IsWriteFallback })
        if ($fallbacks.Count -ne 1) {
            throw [InvalidOperationException]::new('TRX outcome mappings must declare exactly one write fallback.')
        }
        return $fallbacks[0]
    }

    foreach ($mapping in $mappings) {
        if ((Test-NervOrdinalEquals $PSCmdlet.ParameterSetName 'TrxOutcome') -and (Test-NervOrdinalEquals $mapping.TrxOutcome $TrxOutcome)) {
            return $mapping
        }
        if ((Test-NervOrdinalEquals $PSCmdlet.ParameterSetName 'NormalizedOutcome') -and (Test-NervOrdinalEquals $mapping.NormalizedOutcome $NormalizedOutcome)) {
            return $mapping
        }
    }
    return $null
}

function Get-NervOrdinalCompositeKey {
    <#
        Encodes a sequence of identity components into one injective ordinal key.

        A delimiter-only key is ambiguous: ('a|b','c') and ('a','b|c') both become 'a|b|c'.
        Escape backslash first and the delimiter second, then join with the unescaped delimiter.
        The result is prefix-decodable and leaves today's keys byte-for-byte unchanged when their
        components contain neither reserved character. Components stay objects until the shared
        encoder validates them, so null and empty remain distinct instead of being collapsed by
        PowerShell's [string] conversion or rejected by [string[]] parameter binding.
    #>
    param([Parameter(Mandatory)] [AllowEmptyCollection()] [AllowNull()] [object[]] $Components)

    return Get-NervStringCompositeKey -Components $Components
}

function Get-NervOrdinalSet {
    param([Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $Values)
    return Get-NervStringSet -Values $Values -Comparer ([StringComparer]::Ordinal)
}

function Get-NervOrdinalSorted {
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $Values,
        [switch] $Unique
    )
    # Built with an explicit statement rather than `$items = if (…) { [List]::new(…) } else { … }`:
    # PowerShell unrolls an IEnumerable produced by a block, so that spelling hands back an object[]
    # (or $null when empty) and `$items.Sort(…)` fails at run time instead of sorting.
    return Get-NervStringsSorted -Values $Values -Comparer ([StringComparer]::Ordinal) -Unique:$Unique
}

function Get-NervOrdinalGroups {
    <#
        Group-Object with an ordinal key, ordered by that key.

        Returns rows of { Name; Group } so call sites read the same as the Group-Object they replace.
        `-Property`/scriptblock Group-Object folds ignorable characters (measured above), which would
        merge two lanes, two assemblies or two policy ids into one row and report the merged counts
        under whichever spelling happened to arrive first.
    #>
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [AllowNull()] [object[]] $Items,
        [Parameter(Mandatory)] [scriptblock] $KeySelector
    )

    return Get-NervStringGroups -Items $Items -KeySelector $KeySelector -Comparer ([StringComparer]::Ordinal)
}

function Get-NervOrdinalSortedBy {
    <#
        Orders objects by an ordinal string key, stably.

        Built on Get-NervOrdinalGroups, so items sharing a key keep their input order — which is what
        makes a retained artifact byte-reproducible. `Sort-Object <property>` would order by culture
        collation instead, so the same run would lay out differently on a differently-configured
        machine.
    #>
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [AllowNull()] [object[]] $Items,
        [Parameter(Mandatory)] [scriptblock] $KeySelector
    )

    return Get-NervItemsSortedByString -Items $Items -KeySelector $KeySelector -Comparer ([StringComparer]::Ordinal)
}

function Get-NervOrdinalRankedTop {
    <#
        The "top N by a number, ties broken by an identifier" ordering, with no culture collation
        anywhere in it.

        `Sort-Object @{ Expression = 'elapsedMilliseconds'; Descending = $true }, @{ Expression =
        'assembly' }` reads as if only the numeric key mattered, but the tie-break is a *string* key
        and Sort-Object compares strings by culture collation — measured, `apple, Banana, Cherry`
        under culture versus `Banana, Cherry, apple` ordinal. summary.json is a retained artifact, so
        that made two runs of the same evidence lay out differently on differently-configured
        machines (#1509 round 3; the sibling `assemblies` list next to it had already been moved to
        Get-NervOrdinalSortedBy, these two rows had not).

        The numeric rank is applied as an explicitly stable descending sort *after* an ordinal sort on
        the tie-break key, so equal metrics keep ordinal order and nothing but a double comparison
        decides the rest.
    #>
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [AllowNull()] [object[]] $Items,
        [Parameter(Mandatory)] [scriptblock] $MetricSelector,
        [Parameter(Mandatory)] [scriptblock] $TieBreakSelector,
        [Parameter(Mandatory)] [int] $Count
    )

    if ($Count -le 0) { return @() }
    $ordered = @(Get-NervOrdinalSortedBy -Items @($Items) -KeySelector $TieBreakSelector)
    if ($ordered.Count -eq 0) { return @() }

    $decorated = [Collections.Generic.List[object]]::new()
    for ($index = 0; $index -lt $ordered.Count; $index++) {
        $decorated.Add([pscustomobject]@{
            Rank = $index
            Metric = [double](& $MetricSelector $ordered[$index])
            Item = $ordered[$index]
        })
    }
    $decorated.Sort([Comparison[object]] {
        param($Left, $Right)
        if ([double]$Right.Metric -gt [double]$Left.Metric) { return 1 }
        if ([double]$Right.Metric -lt [double]$Left.Metric) { return -1 }
        return [int]$Left.Rank - [int]$Right.Rank
    })

    $take = [Math]::Min($Count, $decorated.Count)
    return @(0..($take - 1) | ForEach-Object { $decorated[$_].Item })
}

function Test-NervHasProperty {
    <#
        Whether an object carries a property under this name.

        OrdinalIgnoreCase, and both halves are deliberate: PowerShell resolves `$x.Foo` and `$x.foo`
        to the same member, so case carries no information here — but `-contains` over
        `PSObject.Properties.Name` is *culture-aware*, measured: with `$o` carrying `expiresOn`,
        `$o.PSObject.Properties.Name -contains "expiresOn$([char]0x00AD)"` is True. That is the
        spelling this function replaces, and the failure it prevents: a JSON document spelling
        `expiresOn` with an embedded ignorable character would be accepted as carrying the real
        field — a mis-spelled key silently governing a quarantine.

        Correction (#1509 round 4): an earlier version of this comment also claimed the
        `PSObject.Properties[$Name]` indexer folds, and called it measured. It does not — on pwsh
        7.6.4 / macOS the same probe returns $null, so only the `-contains` half was ever real. The
        member walk is kept anyway, for a reason that does not depend on that claim: the indexer's
        comparer is an implementation detail of PSMemberInfoCollection, not a documented contract,
        while an explicit [StringComparison] argument is the same on every runtime. This function is
        the one place the answer is decided, so it states its comparison instead of inheriting one.
    #>
    param([Parameter(Mandatory)] [AllowNull()] [object] $Object, [Parameter(Mandatory)] [string] $Name)
    if ($null -eq $Object) { return $false }
    foreach ($property in $Object.PSObject.Properties) {
        if ([string]::Equals([string]$property.Name, $Name, [StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }
    return $false
}

function New-NervTestEvidenceViolation {
    param([string] $Code, [string] $Id, [string] $Message)
    [pscustomobject]@{ code = $Code; id = $Id; message = $Message }
}

function Get-NervTestEvidenceLaneJobs {
    # The allowlisted lane-to-job binding. One physical job owns one lane, so a job can never
    # certify a sibling shard. The unsharded `backend` lane is deliberately absent: since MAN-669
    # no job produces it, and `Backend Tests` is now a test-free aggregate that must never be able
    # to certify a lane. `backend` remains a valid logical base lane for `-SelectedLanes`.
    return [ordered]@{
        'backend-shard-1' = 'Backend Tests - BusinessGateway'
        'backend-shard-2' = 'Backend Tests - Platform'
        'backend-shard-3' = 'Backend Tests - Business Core A'
        'backend-shard-4' = 'Backend Tests - Business Core B'
        'connector-host' = 'Connector Host Tests'
        'postgres' = 'PostgreSQL Provider Tests'
        'redis-cap' = 'Redis/CAP Transport Tests'
        'full-chain' = 'Business FullChain Acceptance'
    }
}

function Test-NervTestEvidenceLaneName {
    param([Parameter(Mandatory)] [string] $Lane)
    if ($Lane.Contains('-shard-', [StringComparison]::Ordinal)) {
        return $Lane -cmatch '^[a-z0-9]+(?:-[a-z0-9]+)*-shard-[1-9][0-9]*$'
    }
    return $Lane -cmatch '^[a-z0-9]+(?:-[a-z0-9]+)*$'
}

function New-NervTestEvidenceRunMetadata {
    param(
        [Parameter(Mandatory)] [string] $WorkflowRunId,
        [Parameter(Mandatory)] [int] $RunAttempt,
        [Parameter(Mandatory)] [string] $HeadSha,
        [Parameter(Mandatory)] [string] $TestedSha,
        [Parameter(Mandatory)] [string] $Lane,
        [AllowNull()] [string[]] $SelectedLanes,
        [string] $Repository = '',
        [string] $Event = '',
        [string] $HeadBranch = '',
        [string] $JobName = '',
        [string] $SourceUrl = '',
        [string] $RunnerOs = '',
        [string] $RunnerImage = '',
        [string] $DotnetSdk = '',
        [string] $ArtifactName = '',
        [int] $RetentionDays = 0
    )

    if (-not (Test-NervTestEvidenceLaneName $Lane)) { throw "Invalid evidence lane '$Lane'." }
    [string[]] $resolvedSelectedLanes = @()
    if ($null -ne $SelectedLanes) { $resolvedSelectedLanes = @($SelectedLanes) }
    if ($resolvedSelectedLanes.Count -eq 0) { $resolvedSelectedLanes = @($Lane) }
    foreach ($selected in $resolvedSelectedLanes) {
        if (-not (Test-NervTestEvidenceLaneName $selected)) { throw "Invalid selected lane '$selected'." }
    }
    if ($RunAttempt -lt 1) { throw 'RunAttempt must be positive.' }
    if ($HeadSha -notmatch '^[0-9a-f]{40}$') { throw 'HeadSha must be a lowercase 40-character SHA.' }
    if ($TestedSha -notmatch '^[0-9a-f]{40}$') { throw 'TestedSha must be a lowercase 40-character SHA.' }
    $allowedEvents = [Collections.Generic.HashSet[string]]::new(
        [string[]]@('push', 'pull_request'),
        [StringComparer]::OrdinalIgnoreCase
    )
    if (-not [string]::IsNullOrWhiteSpace($Event) -and (-not $allowedEvents.Contains($Event))) { throw "Unsupported evidence event '$Event'." }
    if ([string]::Equals([string]$Event, 'push', [StringComparison]::OrdinalIgnoreCase) -and
        (-not [string]::Equals($HeadSha, $TestedSha, [StringComparison]::Ordinal))) {
        throw 'Push evidence requires HeadSha and TestedSha to be identical.'
    }

    return [pscustomobject][ordered]@{
        workflowRunId = $WorkflowRunId
        runAttempt = $RunAttempt
        headSha = $HeadSha
        testedSha = $TestedSha
        lane = $Lane
        selectedLanes = $resolvedSelectedLanes
        repository = $Repository
        event = $Event
        headBranch = $HeadBranch
        jobName = $JobName
        sourceUrl = $SourceUrl
        runnerOs = $RunnerOs
        runnerImage = $RunnerImage
        dotnetSdk = $DotnetSdk
        artifactName = $ArtifactName
        retentionDays = $RetentionDays
        retentionLocation = if ([string]::IsNullOrWhiteSpace($ArtifactName)) { 'local-output' } else { "artifact://$ArtifactName/" }
    }
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

function Find-NervQuotedTextEnd {
    param(
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $Text,
        [Parameter(Mandatory)] [int] $QuoteStart,
        [switch] $AllowCSharpVerbatim
    )

    if ($QuoteStart -lt 0 -or $QuoteStart -ge $Text.Length) {
        throw [ArgumentOutOfRangeException]::new('QuoteStart', $QuoteStart, 'QuoteStart must identify a character within Text.')
    }

    $quote = $Text[$QuoteStart]
    if ($quote -ne [char]'"' -and $quote -ne [char]"'") {
        throw [ArgumentException]::new('QuoteStart must identify a single or double quote.', 'QuoteStart')
    }

    $isCSharpVerbatim = $AllowCSharpVerbatim -and
        $quote -eq [char]'"' -and
        $QuoteStart -gt 0 -and
        $Text[$QuoteStart - 1] -eq [char]'@'
    $position = $QuoteStart + 1
    while ($position -lt $Text.Length) {
        if ($Text[$position] -ne $quote) {
            $position++
            continue
        }

        if ($isCSharpVerbatim) {
            if ($position + 1 -lt $Text.Length -and $Text[$position + 1] -eq $quote) {
                $position += 2
                continue
            }
            return $position + 1
        }

        $slashes = 0
        for ($lookBehind = $position - 1; $lookBehind -ge $QuoteStart -and $Text[$lookBehind] -eq [char]'\'; $lookBehind--) {
            $slashes++
        }
        if (($slashes % 2) -eq 0) {
            return $position + 1
        }
        $position++
    }

    return $Text.Length
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

function Get-NervStableEvidenceGuid {
    param([Parameter(Mandatory)] [string] $Value)
    $bytes = [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Value))
    $guidBytes = [byte[]]::new(16)
    [Array]::Copy($bytes, $guidBytes, 16)
    ([Guid]::new($guidBytes)).ToString()
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

. (Join-Path $PSScriptRoot 'TestEvidencePrivacy.ps1')
. (Join-Path $PSScriptRoot 'TestEvidenceParsing.ps1')
. (Join-Path $PSScriptRoot 'TestEvidenceArtifacts.ps1')
. (Join-Path $PSScriptRoot 'TestEvidenceProvenance.ps1')
. (Join-Path $PSScriptRoot 'TestEvidenceBaseline.ps1')
