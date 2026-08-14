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
            $quote = [char]0
            $escaped = $false
            $verbatim = $false
            while ($position -lt $content.Length) {
                $character = $content[$position]
                if ($quote -ne [char]0) {
                    if ($verbatim -and $character -eq [char]'"' -and $position + 1 -lt $content.Length -and $content[$position + 1] -eq [char]'"') {
                        $position += 2
                        continue
                    }
                    if (-not $verbatim -and $character -eq [char]'\' -and -not $escaped) {
                        $escaped = $true
                        $position++
                        continue
                    }
                    if ($character -eq $quote -and -not $escaped) {
                        $quote = [char]0
                        $verbatim = $false
                    }
                    $escaped = $false
                }
                elseif ($character -eq [char]'"' -or $character -eq [char]"'") {
                    $quote = $character
                    $verbatim = $character -eq [char]'"' -and $position -gt 0 -and $content[$position - 1] -eq [char]'@'
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

function Get-NervTrxSkipReason {
    param([Parameter(Mandatory)] [Xml.XmlElement] $UnitTestResult)

    $message = $UnitTestResult.SelectSingleNode("./*[local-name()='Output']/*[local-name()='ErrorInfo']/*[local-name()='Message']")
    if ($null -ne $message -and -not [string]::IsNullOrWhiteSpace($message.InnerText)) {
        return $message.InnerText.Trim()
    }
    $stdout = $UnitTestResult.SelectSingleNode("./*[local-name()='Output']/*[local-name()='StdOut']")
    if ($null -ne $stdout) {
        foreach ($line in ($stdout.InnerText -split '\r?\n')) {
            if (-not [string]::IsNullOrWhiteSpace($line) -and $line.Contains('SKIP', [StringComparison]::OrdinalIgnoreCase)) {
                return $line.Trim()
            }
        }
    }
    return $null
}

function Get-NervStableEvidenceGuid {
    param([Parameter(Mandatory)] [string] $Value)
    $bytes = [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Value))
    $guidBytes = [byte[]]::new(16)
    [Array]::Copy($bytes, $guidBytes, 16)
    ([Guid]::new($guidBytes)).ToString()
}

function ConvertTo-NervRetainedDisplayName {
    param([AllowNull()] [string] $Text)

    $source = if ($null -eq $Text) { '' } else { $Text }
    if ([string]::IsNullOrWhiteSpace($source)) {
        return [pscustomobject]@{ text = (Protect-NervTestEvidenceText $source); redactionCount = 0 }
    }

    $pattern = [regex]::new('(?i)(?<prefix>(?:^|[(,]\s*))(?<label>(?:body|requestBody|responseBody)\s*:\s*)')
    $builder = [Text.StringBuilder]::new()
    $position = 0
    $redactionCount = 0
    while ($position -lt $source.Length) {
        $match = $pattern.Match($source, $position)
        if (-not $match.Success) {
            [void]$builder.Append($source.Substring($position))
            break
        }

        [void]$builder.Append($source.Substring($position, $match.Index - $position))
        [void]$builder.Append($match.Groups['prefix'].Value)
        [void]$builder.Append($match.Groups['label'].Value)
        $valueStart = $match.Index + $match.Length
        $valueEnd = $valueStart
        if ($valueStart -lt $source.Length -and ($source[$valueStart] -eq [char]'"' -or $source[$valueStart] -eq [char]"'")) {
            $quote = $source[$valueStart]
            $valueEnd++
            while ($valueEnd -lt $source.Length) {
                if ($source[$valueEnd] -eq $quote) {
                    $slashes = 0
                    for ($lookBehind = $valueEnd - 1; $lookBehind -ge $valueStart -and $source[$lookBehind] -eq [char]'\'; $lookBehind--) { $slashes++ }
                    if (($slashes % 2) -eq 0) { $valueEnd++; break }
                }
                $valueEnd++
            }
        }
        else {
            $depth = 0
            $quote = [char]0
            $escaped = $false
            while ($valueEnd -lt $source.Length) {
                $character = $source[$valueEnd]
                if ($quote -ne [char]0) {
                    if ($character -eq [char]'\' -and -not $escaped) { $escaped = $true; $valueEnd++; continue }
                    if ($character -eq $quote -and -not $escaped) { $quote = [char]0 }
                    $escaped = $false
                }
                elseif ($character -eq [char]'"' -or $character -eq [char]"'") { $quote = $character }
                # `[char]` casts, not `-in` over string literals: `-in` compares as *strings*, which is
                # culture-aware. Char equality is numeric and is what a brace matcher wants.
                elseif ($character -eq [char]'{' -or $character -eq [char]'[' -or $character -eq [char]'(') { $depth++ }
                elseif ($character -eq [char]'}' -or $character -eq [char]']') { if ($depth -gt 0) { $depth-- } }
                elseif ($character -eq [char]')' -and $depth -eq 0) { break }
                elseif ($character -eq [char]')' -and $depth -gt 0) { $depth-- }
                elseif ($character -eq [char]',' -and $depth -eq 0) { break }
                $valueEnd++
            }
        }

        $rawValue = $source.Substring($valueStart, $valueEnd - $valueStart)
        if ($rawValue -cmatch '^["'']<redacted-body:[0-9a-f]{16}>["'']$') {
            [void]$builder.Append($rawValue)
            $position = $valueEnd
            continue
        }
        $digest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($rawValue))).ToLowerInvariant().Substring(0, 16)
        [void]$builder.Append("`"<redacted-body:$digest>`"")
        $redactionCount++
        $position = $valueEnd
    }
    [pscustomobject]@{ text = (Protect-NervTestEvidenceText $builder.ToString()); redactionCount = $redactionCount }
}

function ConvertTo-NervRetainedFailureText {
    param([AllowNull()] [string] $Text)
    if ([string]::IsNullOrWhiteSpace($Text)) { return $null }
    return 'Test failed; raw failure details are intentionally omitted by evidence privacy policy.'
}

function Get-NervRetainedSkipReason {
    param([Parameter(Mandatory)] [object] $Record)
    if (-not (Test-NervHasProperty -Object $Record -Name 'skipPolicyId') -or [string]::IsNullOrWhiteSpace([string]$Record.skipPolicyId)) {
        return 'Skipped; raw reason omitted because no approved policy matched.'
    }
    $safe = Protect-NervTestEvidenceText ([string]$Record.skipReason)
    if ($safe.Length -gt 512) { return $safe.Substring(0, 512) }
    return $safe
}

function Read-NervTrxResults {
    param(
        [Parameter(Mandatory)] [string[]] $Path,
        [Parameter(Mandatory)] [hashtable] $RunMetadata
    )

    if (-not (Test-NervTestEvidenceLaneName ([string]$RunMetadata.lane))) {
        throw "Invalid evidence lane '$($RunMetadata.lane)'."
    }
    $outcomeMap = @{ Passed = 'passed'; Failed = 'failed'; NotExecuted = 'skipped' }
    $records = [Collections.Generic.List[object]]::new()
    $trxElapsedMilliseconds = 0.0
    $trxRuns = [Collections.Generic.List[object]]::new()
    foreach ($trxPath in @(Get-NervOrdinalSorted -Values @($Path | ForEach-Object { [string]$_ }))) {
        try {
            $document = [Xml.XmlDocument]::new()
            $document.PreserveWhitespace = $false
            $document.Load($trxPath)
        }
        catch {
            $safePath = [IO.Path]::GetFullPath($trxPath)
            throw [IO.InvalidDataException]::new("Failed to parse TRX '$safePath'.")
        }

        $root = $document.DocumentElement
        $persistedHeadSha = $root.GetAttribute('headSha')
        $persistedTestedSha = $root.GetAttribute('testedSha')
        $normalizedIdentityNamespace = 'urn:nerv-iip:test-evidence:assembly-identity:v1'
        $definitionNodes = @($document.SelectNodes("//*[local-name()='TestDefinitions']/*[local-name()='UnitTest']"))
        $reservedAssemblyIdentityAttributes = [Collections.Generic.List[object]]::new()
        foreach ($definitionNode in $definitionNodes) {
            foreach ($attribute in @($definitionNode.Attributes)) {
                if ([string]::Equals([string]$attribute.LocalName, 'assemblyIdentity', [StringComparison]::Ordinal)) {
                    $reservedAssemblyIdentityAttributes.Add($attribute)
                }
            }
        }
        $hasReservedAssemblyIdentityMarker = $reservedAssemblyIdentityAttributes.Count -gt 0
        if ($hasReservedAssemblyIdentityMarker) {
            # Fail closed on the reserved local name in any other namespace, duplicate marker
            # attributes, or a partially marked definition set. The namespace URI is the authority;
            # the XML prefix is intentionally irrelevant.
            foreach ($definitionNode in $definitionNodes) {
                $definitionMarkerAttributes = @($definitionNode.Attributes | Where-Object {
                    [string]::Equals([string]$_.LocalName, 'assemblyIdentity', [StringComparison]::Ordinal)
                })
                if ($definitionMarkerAttributes.Count -ne 1 -or
                    -not [string]::Equals([string]$definitionMarkerAttributes[0].NamespaceURI, $normalizedIdentityNamespace, [StringComparison]::Ordinal)) {
                    throw [IO.InvalidDataException]::new("TRX assembly identity marker metadata is malformed or uses an unsupported namespace in '$([IO.Path]::GetFullPath($trxPath))'.")
                }
            }
            if ($persistedHeadSha -notmatch '^[0-9a-f]{40}$' -or $persistedTestedSha -notmatch '^[0-9a-f]{40}$' -or
                -not [string]::Equals($persistedHeadSha, [string]$RunMetadata.headSha, [StringComparison]::Ordinal) -or
                -not [string]::Equals($persistedTestedSha, [string]$RunMetadata.testedSha, [StringComparison]::Ordinal)) {
                throw [IO.InvalidDataException]::new("TRX assembly identity markers require exact normalized head and tested provenance in '$([IO.Path]::GetFullPath($trxPath))'.")
            }
        }
        elseif (-not [string]::IsNullOrWhiteSpace($persistedHeadSha) -or -not [string]::IsNullOrWhiteSpace($persistedTestedSha)) {
            # Ordinal (#1509): a commit SHA is an identifier and this is the guard that stops a
            # normalized TRX from being read under someone else's provenance. `-cne` is culture-aware,
            # so a persisted SHA carrying an ignorable character would compare equal and pass.
            if (-not [string]::Equals($persistedHeadSha, [string]$RunMetadata.headSha, [StringComparison]::Ordinal) -or
                -not [string]::Equals($persistedTestedSha, [string]$RunMetadata.testedSha, [StringComparison]::Ordinal)) {
                throw [IO.InvalidDataException]::new("Normalized TRX provenance does not match run metadata in '$([IO.Path]::GetFullPath($trxPath))'.")
            }
        }

        $times = $document.SelectSingleNode("//*[local-name()='Times']")
        if ($null -eq $times -or [string]::IsNullOrWhiteSpace([string]$times.start) -or [string]::IsNullOrWhiteSpace([string]$times.finish)) {
            throw [IO.InvalidDataException]::new("TRX is missing valid Times metadata in '$([IO.Path]::GetFullPath($trxPath))'.")
        }
        $start = [DateTimeOffset]::MinValue
        $finish = [DateTimeOffset]::MinValue
        if (-not [DateTimeOffset]::TryParse([string]$times.start, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind, [ref]$start) -or
            -not [DateTimeOffset]::TryParse([string]$times.finish, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind, [ref]$finish) -or $finish -lt $start) {
            throw [IO.InvalidDataException]::new("TRX has invalid Times metadata in '$([IO.Path]::GetFullPath($trxPath))'.")
        }
        $elapsed = [double]($finish - $start).TotalMilliseconds
        $trxElapsedMilliseconds += $elapsed

        $definitions = @{}
        foreach ($definition in $definitionNodes) {
            $method = $definition.SelectSingleNode("./*[local-name()='TestMethod']")
            $hasAssemblyIdentityMarker = $definition.HasAttribute('assemblyIdentity', $normalizedIdentityNamespace)
            $assemblyIdentityMarker = $definition.GetAttribute('assemblyIdentity', $normalizedIdentityNamespace)
            if ($hasAssemblyIdentityMarker -and
                ([string]::Equals($assemblyIdentityMarker, 'null', [StringComparison]::Ordinal) -or [string]::Equals($assemblyIdentityMarker, 'empty', [StringComparison]::Ordinal)) -and
                -not [string]::IsNullOrEmpty([string]$definition.storage)) {
                throw [IO.InvalidDataException]::new("TRX assembly identity markers require empty standard storage in '$([IO.Path]::GetFullPath($trxPath))'.")
            }
            if ($hasAssemblyIdentityMarker -and [string]::Equals($assemblyIdentityMarker, 'verbatim', [StringComparison]::Ordinal) -and
                ([string]::IsNullOrWhiteSpace([string]$definition.storage) -or [string]$definition.storage -notmatch '[/\\]')) {
                throw [IO.InvalidDataException]::new("TRX verbatim assembly identity marker requires non-empty canonical path storage in '$([IO.Path]::GetFullPath($trxPath))'.")
            }
            $assembly = if (-not $hasAssemblyIdentityMarker) { [IO.Path]::GetFileName([string]$definition.storage) }
                elseif ([string]::Equals($assemblyIdentityMarker, 'null', [StringComparison]::Ordinal)) { $null }
                elseif ([string]::Equals($assemblyIdentityMarker, 'empty', [StringComparison]::Ordinal)) { '' }
                elseif ([string]::Equals($assemblyIdentityMarker, 'verbatim', [StringComparison]::Ordinal)) { [string]$definition.storage }
                else { throw [IO.InvalidDataException]::new("TRX has an unsupported normalized assembly identity marker in '$([IO.Path]::GetFullPath($trxPath))'.") }
            $testName = if ($null -ne $method -and -not [string]::IsNullOrWhiteSpace([string]$method.className)) {
                "$($method.className).$($method.name)"
            }
            else { [string]$definition.name }
            $definitions[[string]$definition.id] = [pscustomobject]@{
                assembly = $assembly
                testName = $testName
                className = if ($null -ne $method) { [string]$method.className } else { '' }
                methodName = if ($null -ne $method) { [string]$method.name } else { [string]$definition.name }
            }
        }

        $results = @($document.SelectNodes("//*[local-name()='Results']/*[local-name()='UnitTestResult']"))
        $counters = $document.SelectSingleNode("//*[local-name()='ResultSummary']/*[local-name()='Counters']")
        if ($null -eq $counters) { throw [IO.InvalidDataException]::new("TRX is missing ResultSummary/Counters in '$([IO.Path]::GetFullPath($trxPath))'.") }
        $counterTotal = [int]$counters.total
        $counterExecuted = [int]$counters.executed
        $counterPassed = [int]$counters.passed
        $counterFailed = [int]$counters.failed
        $counterSkipped = $counterTotal - $counterExecuted
        $actualPassed = @($results | Where-Object { Test-NervOrdinalEquals ([string]$_.outcome) 'Passed' }).Count
        $actualFailed = @($results | Where-Object { Test-NervOrdinalEquals ([string]$_.outcome) 'Failed' }).Count
        $actualSkipped = @($results | Where-Object { Test-NervOrdinalEquals ([string]$_.outcome) 'NotExecuted' }).Count
        if ($counterTotal -ne $results.Count -or $counterExecuted -ne ($counterPassed + $counterFailed) -or
            $counterPassed -ne $actualPassed -or $counterFailed -ne $actualFailed -or $counterSkipped -ne $actualSkipped) {
            throw [IO.InvalidDataException]::new("TRX ResultSummary/Counters do not match Results in '$([IO.Path]::GetFullPath($trxPath))'.")
        }
        # Assembly is an identity-bearing nullable value in normalized TRX. Do not route it through
        # Get-NervOrdinalSorted: that wrapper intentionally accepts ordinary non-empty identifiers,
        # and a [string] projection would collapse null and empty before it could validate them.
        $assembliesInRun = @(Get-NervOrdinalGroups -Items @($definitions.Values) -KeySelector {
            param($row) Get-NervOrdinalCompositeKey -Components @($row.assembly)
        } | ForEach-Object { $_.Group[0].assembly })
        if ($assembliesInRun.Count -gt 1) { throw [IO.InvalidDataException]::new("TRX contains multiple assemblies in '$([IO.Path]::GetFullPath($trxPath))'.") }
        $trxRuns.Add([pscustomobject][ordered]@{
            lane = [string]$RunMetadata.lane
            # Preserve the identity restored from the normalized marker. This projection feeds the
            # summary timing join; casting here would collapse null into empty after the record rows
            # had already restored it correctly.
            assembly = if ($assembliesInRun.Count -eq 1) { $assembliesInRun[0] } else { [IO.Path]::GetFileNameWithoutExtension($trxPath) }
            elapsedMilliseconds = $elapsed
            total = $counterTotal
            executed = $counterExecuted
            passed = $counterPassed
            failed = $counterFailed
            skipped = $counterSkipped
        })

        $ordinals = [Collections.Generic.Dictionary[string, int]]::new([StringComparer]::Ordinal)
        foreach ($result in $results) {
            $rawOutcome = [string]$result.outcome
            if (-not $outcomeMap.ContainsKey($rawOutcome)) {
                throw [IO.InvalidDataException]::new("Unsupported TRX outcome '$rawOutcome' in '$([IO.Path]::GetFullPath($trxPath))'.")
            }
            $definition = $definitions[[string]$result.testId]
            if ($null -eq $definition) {
                throw [IO.InvalidDataException]::new("TRX result references an unknown test definition in '$([IO.Path]::GetFullPath($trxPath))'.")
            }
            $duration = [TimeSpan]::Zero
            if (-not [string]::IsNullOrWhiteSpace([string]$result.duration)) {
                $duration = [TimeSpan]::Parse([string]$result.duration, [Globalization.CultureInfo]::InvariantCulture)
            }
            $retainedDisplay = ConvertTo-NervRetainedDisplayName $result.GetAttribute('testName')
            $displayName = [string]$retainedDisplay.text
            if ([string]::IsNullOrWhiteSpace($displayName)) { $displayName = [string]$definition.testName }
            if ($displayName.Length -gt 512) { $displayName = $displayName.Substring(0, 512) }
            $ordinalKey = Get-NervOrdinalCompositeKey -Components @($definition.testName, $displayName)
            $ordinal = if ($ordinals.ContainsKey($ordinalKey)) { [int]$ordinals[$ordinalKey] + 1 } else { 1 }
            $ordinals[$ordinalKey] = $ordinal
            $rawError = if (Test-NervOrdinalEquals $rawOutcome 'Failed') {
                $node = $result.SelectSingleNode("./*[local-name()='Output']/*[local-name()='ErrorInfo']/*[local-name()='Message']")
                if ($null -ne $node) { $node.InnerText.Trim() } else { $null }
            } else { $null }
            $persistedExecutionId = [Guid]::Empty
            $hasPersistedExecutionId = [Guid]::TryParse($result.GetAttribute('executionId'), [ref]$persistedExecutionId) -and $persistedExecutionId -ne [Guid]::Empty
            $persistedRedactionCount = 0
            $hasPersistedRedactionCount = -not [string]::IsNullOrWhiteSpace($persistedHeadSha) -and
                [int]::TryParse($result.GetAttribute('redactionCount'), [ref]$persistedRedactionCount) -and $persistedRedactionCount -ge 0
            $records.Add([pscustomobject][ordered]@{
                schemaVersion = 1
                workflowRunId = [string]$RunMetadata.workflowRunId
                runAttempt = [int]$RunMetadata.runAttempt
                headSha = [string]$RunMetadata.headSha
                testedSha = [string]$RunMetadata.testedSha
                lane = [string]$RunMetadata.lane
                project = [IO.Path]::GetFileNameWithoutExtension([string]$definition.assembly)
                assembly = $definition.assembly
                testName = [string]$definition.testName
                displayName = $displayName
                testClassName = [string]$definition.className
                testMethodName = [string]$definition.methodName
                definitionId = Get-NervStableEvidenceGuid (Get-NervOrdinalCompositeKey -Components @($definition.assembly, $definition.testName))
                testInstanceId = if ($hasPersistedExecutionId) { $persistedExecutionId.ToString() } else { Get-NervStableEvidenceGuid (Get-NervOrdinalCompositeKey -Components @($definition.assembly, $definition.testName, $displayName, [string]$ordinal)) }
                durationTicks = [long]$duration.Ticks
                durationMilliseconds = [double]$duration.TotalMilliseconds
                outcome = [string]$outcomeMap[$rawOutcome]
                skipReason = if (Test-NervOrdinalEquals $rawOutcome 'NotExecuted') { Get-NervTrxSkipReason -UnitTestResult $result } else { $null }
                errorMessage = ConvertTo-NervRetainedFailureText $rawError
                redactionCount = if ($hasPersistedRedactionCount) { $persistedRedactionCount } else { [int]$retainedDisplay.redactionCount + $(if ([string]::IsNullOrWhiteSpace($rawError)) { 0 } else { 1 }) }
            })
        }
    }
    $RunMetadata.trxElapsedMilliseconds = [double]$trxElapsedMilliseconds
    $RunMetadata.trxRuns = @($trxRuns)
    @($records)
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

function Protect-NervTestEvidenceText {
    param([AllowNull()] [string] $Text)
    Protect-ScriptAutomationText -Text $Text
}

function New-NervTestEvidenceSummary {
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Records,
        [Parameter(Mandatory)] [hashtable] $RunMetadata,
        [Parameter(Mandatory)] [AllowNull()] [AllowEmptyCollection()] [object[]] $Violations,
        [AllowNull()] [object] $Baseline,
        [AllowNull()] [string] $PriorAttemptOutcome,
        [int] $TopCount = 10
    )

    [object[]] $safeRecords = @($Records)
    [object[]] $safeViolations = @()
    if ($null -ne $Violations) { $safeViolations = @($Violations) }
    $passed = @($safeRecords | Where-Object { Test-NervOrdinalEquals ([string]$_.outcome) 'passed' }).Count
    $failed = @($safeRecords | Where-Object { Test-NervOrdinalEquals ([string]$_.outcome) 'failed' }).Count
    $skipped = @($safeRecords | Where-Object { Test-NervOrdinalEquals ([string]$_.outcome) 'skipped' }).Count
    # Built once, not once per filtered record (#1509 round 3 review); read-only from here on.
    $executedOutcomes = Get-NervOrdinalSet -Values @('passed', 'failed')
    # Ordinal throughout (#1509): the lane, the selector set and the violation code are all
    # identifiers. `-ceq`/`-ccontains` only disable case-insensitivity and stay culture-aware, and
    # `Sort-Object -Unique`/`Group-Object` fold outright — so a selector differing by an ignorable
    # character would be dropped here, or folded into a group whose gateResult belongs to a different
    # lane. The dedup has to be ordinal *before* the set is built, or the set never sees the second
    # spelling and the ordinal comparer below is decorative.
    [string[]] $selectedLanes = if ($RunMetadata.ContainsKey('selectedLanes')) {
        Get-NervOrdinalSorted -Unique -Values @($RunMetadata.selectedLanes | ForEach-Object { [string]$_ })
    } else { @([string]$RunMetadata.lane) }
    $selectedLaneResults = @(Get-NervOrdinalGroups -Items @($selectedLanes) -KeySelector { param($lane) [string]$lane -replace '-shard-[1-9][0-9]*$', '' } | ForEach-Object {
        $baseLane = [string]$_.Name
        [string[]] $selectors = @(Get-NervOrdinalSorted -Unique -Values @($_.Group | ForEach-Object { [string]$_ }))
        $selectorSet = Get-NervOrdinalSet -Values $selectors
        $laneRecords = @($safeRecords | Where-Object { [string]::Equals([string]([string]$_.lane -replace '-shard-[1-9][0-9]*$', ''), $baseLane, [StringComparison]::Ordinal) })
        $zeroExecution = @($safeViolations | Where-Object { [string]::Equals([string]$_.code, 'zero-execution', [StringComparison]::Ordinal) -and ([string]::Equals([string]$_.id, $baseLane, [StringComparison]::Ordinal) -or $selectorSet.Contains([string]$_.id)) }).Count -gt 0
        $invalidSelection = @($safeViolations | Where-Object { [string]::Equals([string]$_.code, 'unregistered-skip', [StringComparison]::Ordinal) -and $selectorSet.Contains([string]$_.id) }).Count -gt 0
        [pscustomobject][ordered]@{
            baseLane = $baseLane
            selectors = $selectors
            observedLanes = [string[]]@(Get-NervOrdinalSorted -Unique -Values @($laneRecords | ForEach-Object { [string]$_.lane }))
            passed = @($laneRecords | Where-Object { Test-NervOrdinalEquals ([string]$_.outcome) 'passed' }).Count
            failed = @($laneRecords | Where-Object { Test-NervOrdinalEquals ([string]$_.outcome) 'failed' }).Count
            skipped = @($laneRecords | Where-Object { Test-NervOrdinalEquals ([string]$_.outcome) 'skipped' }).Count
            executed = @($laneRecords | Where-Object { $executedOutcomes.Contains([string]$_.outcome) }).Count
            total = $laneRecords.Count
            gateResult = if ($zeroExecution) { 'zero-execution' } elseif ($invalidSelection) { 'invalid-selection' } else { 'pass' }
        }
    })
    $trxRuns = if ($RunMetadata.ContainsKey('trxRuns')) { @($RunMetadata.trxRuns) } else { @() }
    # Ordinal grouping and ordinal membership (#1509): the group key is `lane|assembly`, both
    # identifiers, and `Group-Object lane, assembly` folds them. Two assemblies differing by an
    # ignorable character would report one merged timing row under one of the two names.
    $assemblies = @(Get-NervOrdinalGroups -Items $safeRecords -KeySelector { param($row) Get-NervOrdinalCompositeKey -Components @($row.lane, $row.assembly) } | ForEach-Object {
        $items = @($_.Group)
        $laneName = $items[0].lane
        $assemblyName = $items[0].assembly
        $runIdentity = Get-NervOrdinalCompositeKey -Components @($laneName, $assemblyName)
        $runRows = @($trxRuns | Where-Object {
            Test-NervOrdinalEquals (Get-NervOrdinalCompositeKey -Components @($_.lane, $_.assembly)) $runIdentity
        })
        [pscustomobject][ordered]@{
            lane = $laneName
            assembly = $assemblyName
            passed = @($items | Where-Object { Test-NervOrdinalEquals ([string]$_.outcome) 'passed' }).Count
            failed = @($items | Where-Object { Test-NervOrdinalEquals ([string]$_.outcome) 'failed' }).Count
            skipped = @($items | Where-Object { Test-NervOrdinalEquals ([string]$_.outcome) 'skipped' }).Count
            executed = @($items | Where-Object { $executedOutcomes.Contains([string]$_.outcome) }).Count
            total = $items.Count
            testDurationMilliseconds = [double](($items | Measure-Object durationMilliseconds -Sum).Sum)
            elapsedMilliseconds = if (@($runRows).Count -gt 0) { [double](($runRows | Measure-Object elapsedMilliseconds -Sum).Sum) } else { 0.0 }
        }
    })
    $baselineAssemblies = if ($null -ne $Baseline -and (Test-NervHasProperty -Object $Baseline -Name 'assemblies')) { @($Baseline.assemblies) } else { @() }
    $baselineSchemaVersion = 0
    $baselineUnavailableReason = if ($null -eq $Baseline) {
        'baseline-not-provided'
    }
    # The reader must know which `source` shape it is looking at, or "schema 1's flat runner trio is
    # the first lane's, schema 2's laneProvenance is per lane" is a comment rather than a rule. Both
    # known versions compare identically (the comparison key is the assembly, no lane and no runner
    # field participates); an unknown or missing version fails closed to report-only unavailable
    # rather than comparing against a file whose layout this code has never seen.
    #
    # TryParse, not `[int]`: the cast mishandled three shapes a hand-edited JSON file can hold, each in
    # a different way. Which shapes, and why no NumberStyles/culture argument is needed, are in
    # docs/architecture/test-evidence-governance.md ("Run identity versus per-job environment").
    elseif (-not (Test-NervHasProperty -Object $Baseline -Name 'schemaVersion') -or
        -not [int]::TryParse([string]$Baseline.schemaVersion, [ref]$baselineSchemaVersion) -or
        $baselineSchemaVersion -notin @(1, 2)) {
        'unsupported-baseline-schema-version'
    }
    elseif (-not (Test-NervHasProperty -Object $Baseline -Name 'granularity') -or -not (Test-NervHasProperty -Object $Baseline -Name 'durationMetric')) {
        'baseline-metadata-incomplete'
    }
    elseif (-not (Test-NervOrdinalEquals ([string]$Baseline.granularity) 'test') -or -not (Test-NervOrdinalEquals ([string]$Baseline.durationMetric) 'trx-elapsed')) {
        'incompatible-granularity-or-duration-metric'
    }
    else { $null }
    # The comparison key is the **assembly alone** (#1507). It used to be lane plus assembly, which
    # made a pure "how we run the tests" change invalidate keys no test had touched: MAN-669 PR-A
    # re-homed 17 of 64 backend assemblies between shards and every one of those rows fell to
    # "not in baseline" until a human regenerated and re-committed the snapshot. Timing is a
    # measurement, not a governed list; a measurement of an assembly does not become a different
    # measurement because the assembly moved to another job. Lane survives on the row for display
    # and provenance and is used only to disambiguate, never to look up.
    #
    # Known residual, report-only: a baseline holding one row for an assembly while the current run
    # splits that assembly across two lanes compares both current rows against the whole previous
    # measurement, so both deltas overstate the change. Recorded rather than silently tolerated in
    # docs/architecture/test-evidence-governance.md, "One known report-only artefact".
    $deltas = @($assemblies | ForEach-Object {
        $current = $_
        $compatible = $null -eq $baselineUnavailableReason
        # Ordinal, not `-ceq`. The `c` prefix only disables case-insensitivity; the comparison still
        # runs through the collation table, which reports "equal" for strings that differ by an
        # ignorable character. An assembly name is an identifier, so it is compared as bytes.
        $currentAssemblyIdentity = Get-NervOrdinalCompositeKey -Components @($current.assembly)
        [object[]]$assemblyMatches = if ($compatible) { @($baselineAssemblies | Where-Object {
            [string]::Equals((Get-NervOrdinalCompositeKey -Components @($_.assembly)), $currentAssemblyIdentity, [StringComparison]::Ordinal)
        }) } else { @() }
        # One assembly classified into two lanes would give two rows that are not the same
        # measurement. Prefer this lane's row; with no lane match the comparison is genuinely
        # ambiguous and stays report-only unavailable rather than picking one arbitrarily.
        #
        # `Merge-NervShardTimingObservations` in scripts/lib/BackendTestShardTimings.ps1 resolves the
        # very same situation by *summing* the two rows, and the divergence is intentional. That one
        # builds a shard budget, where the answer wanted is total work and two lanes are two halves
        # of one number. This one compares one lane's run against one baseline row, where the answer
        # wanted is a row identity — summing would invent a measurement nobody took. Neither rule is
        # a fallback for the other; changing either does not imply changing the other.
        [object[]]$previous = if (@($assemblyMatches).Count -le 1) {
            @($assemblyMatches)
        }
        else {
            $currentLaneIdentity = Get-NervOrdinalCompositeKey -Components @($current.lane)
            @(@($assemblyMatches) | Where-Object {
                [string]::Equals((Get-NervOrdinalCompositeKey -Components @($_.lane)), $currentLaneIdentity, [StringComparison]::Ordinal)
            } | Select-Object -First 1)
        }
        $baselineDuration = if (@($previous).Count -eq 1) { [double]@($previous)[0].elapsedMilliseconds } else { $null }
        $unavailableReason = if ($null -ne $baselineUnavailableReason) { $baselineUnavailableReason }
            elseif (@($previous).Count -ne 1 -and @($assemblyMatches).Count -gt 1) { 'ambiguous-assembly-in-baseline' }
            elseif (@($previous).Count -ne 1) { 'assembly-not-in-baseline' }
            elseif ($baselineDuration -le 0) { 'baseline-duration-not-positive' }
            else { $null }
        [pscustomobject][ordered]@{
            lane = $current.lane
            assembly = $current.assembly
            metric = 'trx-elapsed'
            available = $null -eq $unavailableReason
            unavailableReason = $unavailableReason
            baselineDurationMilliseconds = $baselineDuration
            currentDurationMilliseconds = [double]$current.elapsedMilliseconds
            deltaPercent = if ($null -eq $unavailableReason) { [Math]::Round((([double]$current.elapsedMilliseconds - $baselineDuration) / $baselineDuration) * 100, 2) } else { $null }
        }
    })
    $baselineAvailable = @($deltas | Where-Object available).Count -gt 0
    $summaryBaselineUnavailableReason = if ($baselineAvailable) { $null } elseif ($null -ne $baselineUnavailableReason) { $baselineUnavailableReason } else { 'no-compatible-assembly' }
    $attemptClassification = if ([int]$RunMetadata.runAttempt -eq 1) {
        'initial'
    }
    elseif ((Test-NervOrdinalEquals ([string]$PriorAttemptOutcome) 'failure') -and $RunMetadata.ContainsKey('priorAttemptVerified') -and [bool]$RunMetadata.priorAttemptVerified -and
        $RunMetadata.ContainsKey('currentTestOutcome') -and (Test-NervOrdinalEquals ([string]$RunMetadata.currentTestOutcome) 'success') -and
        ($passed + $failed) -gt 0 -and $failed -eq 0 -and $safeViolations.Count -eq 0) {
        'recovered-after-rerun'
    }
    else { 'rerun' }
    $priorStatus = if ([string]::IsNullOrWhiteSpace($PriorAttemptOutcome)) { 'prior-attempt-unavailable' } else { $PriorAttemptOutcome }

    [pscustomobject][ordered]@{
        schemaVersion = 1
        workflowRunId = [string]$RunMetadata.workflowRunId
        runAttempt = [int]$RunMetadata.runAttempt
        headSha = [string]$RunMetadata.headSha
        testedSha = [string]$RunMetadata.testedSha
        lane = [string]$RunMetadata.lane
        selectedLanes = $selectedLanes
        selectedLaneResults = $selectedLaneResults
        repository = if ($RunMetadata.ContainsKey('repository')) { [string]$RunMetadata.repository } else { '' }
        event = if ($RunMetadata.ContainsKey('event')) { [string]$RunMetadata.event } else { '' }
        headBranch = if ($RunMetadata.ContainsKey('headBranch')) { [string]$RunMetadata.headBranch } else { '' }
        jobName = if ($RunMetadata.ContainsKey('jobName')) { [string]$RunMetadata.jobName } else { '' }
        currentTestOutcome = if ($RunMetadata.ContainsKey('currentTestOutcome')) { [string]$RunMetadata.currentTestOutcome } else { '' }
        sourceUrl = if ($RunMetadata.ContainsKey('sourceUrl')) { [string]$RunMetadata.sourceUrl } else { '' }
        runnerOs = if ($RunMetadata.ContainsKey('runnerOs')) { [string]$RunMetadata.runnerOs } else { '' }
        runnerImage = if ($RunMetadata.ContainsKey('runnerImage')) { [string]$RunMetadata.runnerImage } else { '' }
        dotnetSdk = if ($RunMetadata.ContainsKey('dotnetSdk')) { [string]$RunMetadata.dotnetSdk } else { '' }
        artifactName = if ($RunMetadata.ContainsKey('artifactName')) { [string]$RunMetadata.artifactName } else { '' }
        retentionDays = if ($RunMetadata.ContainsKey('retentionDays')) { [int]$RunMetadata.retentionDays } else { 0 }
        retentionLocation = if ($RunMetadata.ContainsKey('retentionLocation')) { [string]$RunMetadata.retentionLocation } else { 'local-output' }
        passed = $passed
        failed = $failed
        skipped = $skipped
        executed = $passed + $failed
        total = $safeRecords.Count
        testDurationMilliseconds = if ($safeRecords.Count -gt 0) { [double](($safeRecords | Measure-Object durationMilliseconds -Sum).Sum) } else { 0.0 }
        trxElapsedMilliseconds = if ($RunMetadata.ContainsKey('trxElapsedMilliseconds')) { [double]$RunMetadata.trxElapsedMilliseconds } else { $null }
        assemblies = $assemblies
        # Ordinal tie-break (#1509 round 3): both rows are retained in summary.json, and the second
        # sort key here is an identifier, so Sort-Object's culture collation made the artifact's byte
        # layout depend on the machine's locale.
        slowestAssemblies = @(Get-NervOrdinalRankedTop -Items @($assemblies) -Count $TopCount `
            -MetricSelector { param($row) [double]$row.elapsedMilliseconds } `
            -TieBreakSelector { param($row) [string]$row.assembly })
        slowestTests = @(Get-NervOrdinalRankedTop -Items @($safeRecords) -Count $TopCount `
            -MetricSelector { param($row) [double]$row.durationMilliseconds } `
            -TieBreakSelector { param($row) [string]$row.testName } |
            ForEach-Object { [pscustomobject]@{ lane = $_.lane; testName = $_.testName; displayName = $_.displayName; assembly = $_.assembly; durationMilliseconds = $_.durationMilliseconds } })
        # skipReasons *groups* culture-aware on purpose: the key is prose, not an identifier, and
        # folding two visually identical reasons into one reported row is the desired reading. That is
        # the single named exception in the ordinal sweep contract. The *ordering* is a separate
        # decision and gets no such licence — summary.json is retained, so the row order must not
        # depend on the machine's collation (#1509 round 3). skipClassification and skipPolicyId are
        # identifiers and are ordinal on both axes.
        skipReasons = @(Get-NervOrdinalSortedBy -KeySelector { param($group) [string]$group.Name } -Items @(
            $safeRecords | Where-Object { Test-NervOrdinalEquals ([string]$_.outcome) 'skipped' } | Group-Object { Get-NervRetainedSkipReason $_ }
        ) | ForEach-Object { [pscustomobject]@{ reason = $_.Name; count = $_.Count } })
        skipClassifications = @(Get-NervOrdinalGroups -Items @($safeRecords | Where-Object { (Test-NervOrdinalEquals ([string]$_.outcome) 'skipped') -and (Test-NervHasProperty -Object $_ -Name 'skipClassification') }) -KeySelector { param($row) [string]$row.skipClassification } | ForEach-Object { [pscustomobject]@{ classification = $_.Name; count = @($_.Group).Count } })
        skipPolicies = @(Get-NervOrdinalGroups -Items @($safeRecords | Where-Object { (Test-NervOrdinalEquals ([string]$_.outcome) 'skipped') -and (Test-NervHasProperty -Object $_ -Name 'skipPolicyId') }) -KeySelector { param($row) [string]$row.skipPolicyId } | ForEach-Object {
            [pscustomobject]@{ policyId = $_.Name; classification = [string]$_.Group[0].skipClassification; count = @($_.Group).Count }
        })
        violations = $safeViolations
        redactionCount = $(if ($safeRecords.Count -gt 0) { [int](($safeRecords | Measure-Object redactionCount -Sum).Sum) } else { 0 }) + @($safeRecords | Where-Object { (Test-NervOrdinalEquals ([string]$_.outcome) 'skipped') -and (-not (Test-NervHasProperty -Object $_ -Name 'skipPolicyId') -or [string]::IsNullOrWhiteSpace([string]$_.skipPolicyId)) }).Count
        baseline = [pscustomobject][ordered]@{
            enforcement = 'report-only'
            available = $baselineAvailable
            unavailableReason = $summaryBaselineUnavailableReason
            source = if ($null -ne $Baseline -and (Test-NervHasProperty -Object $Baseline -Name 'source')) { $Baseline.source } else { $null }
            assemblies = $deltas
        }
        priorAttemptStatus = $priorStatus
        attemptClassification = $attemptClassification
    }
}

function Write-NervUtf8NoBom {
    param([string] $Path, [AllowNull()] [string] $Text)
    [IO.File]::WriteAllText($Path, $(if ($null -eq $Text) { '' } else { $Text }), [Text.UTF8Encoding]::new($false))
}

function ConvertTo-NervEvidenceIdentity {
    param(
        [AllowNull()] [string] $Text,
        [Parameter(Mandatory)] [string] $Pattern,
        [Parameter(Mandatory)] [string] $Fallback,
        [ValidateRange(1, 256)] [int] $MaximumLength = 128
    )
    $safe = Protect-NervTestEvidenceText $Text
    if ([string]::IsNullOrWhiteSpace($safe) -or $safe.Length -gt $MaximumLength -or $safe -cnotmatch $Pattern) { return $Fallback }
    return $safe
}

function Write-NervEvidenceOutputPath {
    param([Parameter(Mandatory)] [string] $Path, [AllowNull()] [string] $ManifestPath)
    if ([string]::IsNullOrWhiteSpace($ManifestPath)) { return }
    [IO.File]::AppendAllText($ManifestPath, "evidence-path=$Path`n", [Text.UTF8Encoding]::new($false))
}

function New-NervNormalizedTrxFileNameSet {
    # The retained artifact can be unpacked on a case-insensitive filesystem even when it was
    # generated on Linux. Final-path uniqueness therefore uses the destination contract, not the
    # current host filesystem's comparison rules.
    return ,([Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase))
}

function Add-NervNormalizedTrxFileName {
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [Collections.Generic.HashSet[string]] $ResolvedFileNames,
        [Parameter(Mandatory)] [string] $FileName
    )
    if (-not $ResolvedFileNames.Add($FileName)) {
        throw 'Normalized TRX identities resolved to the same cross-platform artifact filename.'
    }
}

function Get-NervNormalizedTrxHashedFileName {
    param(
        [Parameter(Mandatory)] [object] $Group,
        [Parameter(Mandatory)] [object] $Summary,
        [Parameter(Mandatory)] [string] $Sha8,
        [int] $CollisionOrdinal = 0
    )

    $identityDigest = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData(
        [Text.Encoding]::UTF8.GetBytes([string]$Group.Identity))).ToLowerInvariant()
    $laneStem = [regex]::Replace([string]$Summary.lane, '[^A-Za-z0-9_.-]', '_')
    if ([string]::IsNullOrEmpty($laneStem)) { $laneStem = 'lane' }
    if ($laneStem.Length -gt 64) { $laneStem = $laneStem.Substring(0, 64) }
    $assemblyStem = [string]$Group.AssemblyName
    if ([string]::IsNullOrEmpty($assemblyStem)) { $assemblyStem = 'assembly' }
    $collisionSuffix = if ($CollisionOrdinal -eq 0) { '' } else { "-collision-$CollisionOrdinal" }
    $suffix = "-id-$identityDigest$collisionSuffix-$Sha8-attempt-$($Summary.runAttempt).trx"
    $maximumAssemblyLength = 240 - $laneStem.Length - 1 - $suffix.Length
    if ($maximumAssemblyLength -lt 1) { throw 'Normalized TRX filename metadata exceeds the cross-platform safe length budget.' }
    if ($assemblyStem.Length -gt $maximumAssemblyLength) { $assemblyStem = $assemblyStem.Substring(0, $maximumAssemblyLength) }
    return "$laneStem-$assemblyStem$suffix"
}

function Resolve-NervNormalizedTrxFileNames {
    param(
        [Parameter(Mandatory)] [object[]] $Groups,
        [Parameter(Mandatory)] [object] $Summary,
        [Parameter(Mandatory)] [string] $Sha8
    )

    $resolved = [Collections.Hashtable]::new([StringComparer]::Ordinal)
    $used = New-NervNormalizedTrxFileNameSet
    $legacyGroups = Get-NervStringGroups -Items $Groups -KeySelector { param($row) [string]$row.LegacyFileName } -Comparer ([StringComparer]::OrdinalIgnoreCase)
    $legacyCounts = [Collections.Hashtable]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($legacyGroup in $legacyGroups) { $legacyCounts[[string]$legacyGroup.Name] = @($legacyGroup.Group).Count }

    # Reserve every compatible unique legacy path first. Hashed candidates are allocated only after
    # that reservation, so input order cannot decide whether a legacy identity or an attacker-shaped
    # hash lookalike keeps the historical name.
    foreach ($group in @(Get-NervOrdinalSortedBy -Items $Groups -KeySelector { param($row) [string]$row.Identity })) {
        $legacy = [string]$group.LegacyFileName
        if ($legacy.Length -le 240 -and [int]$legacyCounts[$legacy] -eq 1) {
            Add-NervNormalizedTrxFileName -ResolvedFileNames $used -FileName $legacy
            $resolved[[string]$group.Identity] = $legacy
        }
    }
    foreach ($group in @(Get-NervOrdinalSortedBy -Items $Groups -KeySelector { param($row) [string]$row.Identity })) {
        if ($resolved.ContainsKey([string]$group.Identity)) { continue }
        $collisionOrdinal = 0
        do {
            $candidate = Get-NervNormalizedTrxHashedFileName -Group $group -Summary $Summary -Sha8 $Sha8 -CollisionOrdinal $collisionOrdinal
            $collisionOrdinal++
        } while (-not $used.Add($candidate))
        $resolved[[string]$group.Identity] = $candidate
    }
    return $resolved
}

function Write-NervTestEvidenceArtifacts {
    param(
        [Parameter(Mandatory)] [object[]] $Records,
        [Parameter(Mandatory)] [object] $Summary,
        [Parameter(Mandatory)] [string] $OutputDirectory
    )

    $parent = Split-Path -Parent $OutputDirectory
    [IO.Directory]::CreateDirectory($parent) | Out-Null
    if (Test-Path -LiteralPath $OutputDirectory) { throw "Evidence output already exists: '$OutputDirectory'." }
    $temporary = "$OutputDirectory.tmp-$([Guid]::NewGuid().ToString('N'))"
    try {
        [IO.Directory]::CreateDirectory($temporary) | Out-Null
        [IO.Directory]::CreateDirectory((Join-Path $temporary 'trx')) | Out-Null
        # Ordinal ordering (#1509): these keys are identifiers and this sort fixes the byte layout of
        # a retained artifact, so culture collation would make the same run produce different files on
        # different machines.
        $recordLines = foreach ($record in @(Get-NervOrdinalSortedBy -Items @($Records) -KeySelector { param($row) Get-NervOrdinalCompositeKey -Components @($row.assembly, $row.testName) })) {
            $safeRecord = [ordered]@{
                schemaVersion = [int]$record.schemaVersion
                workflowRunId = [string]$record.workflowRunId
                runAttempt = [int]$record.runAttempt
                headSha = [string]$record.headSha
                testedSha = [string]$record.testedSha
                lane = [string]$record.lane
                project = [string]$record.project
                # Nullable normalized assembly identity is retained in JSON as well as TRX. A
                # string cast here would make tests.jsonl disagree with the marker-bearing TRX.
                assembly = $record.assembly
                testName = [string]$record.testName
                displayName = [string]$record.displayName
                testClassName = [string]$record.testClassName
                testMethodName = [string]$record.testMethodName
                definitionId = [string]$record.definitionId
                testInstanceId = [string]$record.testInstanceId
                durationTicks = [long]$record.durationTicks
                durationMilliseconds = [double]$record.durationMilliseconds
                outcome = [string]$record.outcome
                skipReason = if (Test-NervOrdinalEquals ([string]$record.outcome) 'skipped') { Get-NervRetainedSkipReason $record } else { $null }
                skipClassification = if (Test-NervHasProperty -Object $record -Name 'skipClassification') { [string]$record.skipClassification } else { $null }
                skipPolicyId = if (Test-NervHasProperty -Object $record -Name 'skipPolicyId') { [string]$record.skipPolicyId } else { $null }
                redactionCount = [int]$record.redactionCount
            }
            $safeRecord | ConvertTo-Json -Compress -Depth 20
        }
        Write-NervUtf8NoBom (Join-Path $temporary 'tests.jsonl') ([string]::Join("`n", @($recordLines)) + $(if (@($recordLines).Count -gt 0) { "`n" } else { '' }))
        $safeSummaryJson = Protect-NervTestEvidenceText ($Summary | ConvertTo-Json -Depth 100)
        Write-NervUtf8NoBom (Join-Path $temporary 'summary.json') ($safeSummaryJson + "`n")
        $baselineSource = if ($null -ne $Summary.baseline.source) { [string]$Summary.baseline.source.sourceUrl } else { 'unavailable' }
        $markdown = @(
            "# Test evidence: $($Summary.lane)",
            '',
            "- Run: $($Summary.workflowRunId), attempt $($Summary.runAttempt), head $($Summary.headSha), tested $($Summary.testedSha)",
            "- Selected lanes: $([string]::Join(', ', @($Summary.selectedLanes)))",
            "- Counts: passed=$($Summary.passed), failed=$($Summary.failed), skipped=$($Summary.skipped), executed=$($Summary.executed), total=$($Summary.total)",
            "- Duration: summed tests=$($Summary.testDurationMilliseconds)ms, TRX elapsed=$($Summary.trxElapsedMilliseconds)ms",
            "- Attempt: $($Summary.attemptClassification) (prior: $($Summary.priorAttemptStatus))",
            "- Provenance: job=$($Summary.jobName), outcome=$($Summary.currentTestOutcome), runner=$($Summary.runnerOs)/$($Summary.runnerImage), dotnet=$($Summary.dotnetSdk)",
            "- Baseline source: $baselineSource",
            $(if ([bool]$Summary.baseline.available) { '- Baseline comparison: available' } else { "- Baseline comparison: unavailable ($($Summary.baseline.unavailableReason))" }),
            "- Privacy redactions: $($Summary.redactionCount)",
            '- Timing and baseline deltas: report-only',
            "- Retained artifact: $($Summary.artifactName), retention=$($Summary.retentionDays) days, location=$($Summary.retentionLocation); tests.jsonl, summary.json, summary.md, diagnostics.log, normalized trx/",
            '',
            '## Selected lane results',
            '',
            '| Logical lane | Selectors | Observed lanes | Passed | Failed | Skipped | Executed | Total | Gate result |',
            '|---|---|---|---:|---:|---:|---:|---:|---|'
        )
        foreach ($laneResult in @($Summary.selectedLaneResults)) {
            $markdown += "| $($laneResult.baseLane) | $([string]::Join(', ', @($laneResult.selectors))) | $([string]::Join(', ', @($laneResult.observedLanes))) | $($laneResult.passed) | $($laneResult.failed) | $($laneResult.skipped) | $($laneResult.executed) | $($laneResult.total) | $($laneResult.gateResult) |"
        }
        $markdown += @(
            '',
            '## Assemblies',
            '',
            '| Lane | Assembly | Passed | Failed | Skipped | Executed | Total | Test duration (ms) | TRX elapsed (ms) |',
            '|---|---|---:|---:|---:|---:|---:|---:|---:|'
        )
        foreach ($assembly in @($Summary.assemblies)) {
            $markdown += "| $($assembly.lane) | $($assembly.assembly) | $($assembly.passed) | $($assembly.failed) | $($assembly.skipped) | $($assembly.executed) | $($assembly.total) | $($assembly.testDurationMilliseconds) | $($assembly.elapsedMilliseconds) |"
        }
        $markdown += @(
            '',
            '## Slowest assemblies and tests',
            ''
        )
        foreach ($assembly in @($Summary.slowestAssemblies)) { $markdown += "- Assembly $($assembly.lane)/$($assembly.assembly): $($assembly.elapsedMilliseconds)ms elapsed" }
        foreach ($test in @($Summary.slowestTests)) { $markdown += "- Test $($test.testName): $($test.durationMilliseconds)ms" }
        $markdown += @(
            '',
            '## Skip reasons',
            ''
        )
        if (@($Summary.skipReasons).Count -eq 0) { $markdown += '- None.' }
        foreach ($reason in @($Summary.skipReasons)) { $markdown += "- $($reason.reason): $($reason.count)" }
        $markdown += @(
            '',
            '## Skip policy matches',
            ''
        )
        if (@($Summary.skipPolicies).Count -eq 0) {
            $markdown += '- No registered runtime skips.'
        }
        foreach ($policyMatch in @($Summary.skipPolicies)) {
            $markdown += "- $($policyMatch.classification) / $($policyMatch.policyId): $($policyMatch.count)"
        }
        $markdown += @(
            '',
            '## Evidence policy gates',
            ''
        )
        if (@($Summary.violations).Count -eq 0) {
            $markdown += '- unregistered-skip: pass'
            $markdown += '- illegal-quarantine: pass'
            $markdown += '- zero-execution: pass'
        }
        else {
            foreach ($gateName in @('unregistered-skip', 'illegal-quarantine', 'zero-execution')) {
                $markdown += "- $gateName`: $(@($Summary.violations | Where-Object { Test-NervOrdinalEquals ([string]$_.code) $gateName }).Count) violation(s)"
            }
        }
        $markdown += @(
            '',
            '## Assembly baseline deltas',
            ''
        )
        foreach ($delta in @($Summary.baseline.assemblies)) {
            if ([bool]$delta.available) {
                $markdown += "- $($delta.assembly): current=$($delta.currentDurationMilliseconds)ms, baseline=$($delta.baselineDurationMilliseconds)ms, delta=$($delta.deltaPercent)%"
            }
            else {
                $markdown += "- $($delta.assembly): current=$($delta.currentDurationMilliseconds)ms, unavailable ($($delta.unavailableReason))"
            }
        }
        Write-NervUtf8NoBom (Join-Path $temporary 'summary.md') ((Protect-NervTestEvidenceText ([string]::Join("`n", $markdown))) + "`n")
        Write-NervUtf8NoBom (Join-Path $temporary 'diagnostics.log') ''

        $sha8 = ([string]$Summary.testedSha).Substring(0, [Math]::Min(8, ([string]$Summary.testedSha).Length))
        # Ordinal grouping and ordinal ordering (#1509): `lane|assembly` and the record key are
        # identifiers, and this decides both which normalized TRX a record lands in and the byte order
        # inside it.
        $normalizedGroups = @(Get-NervOrdinalGroups -Items @($Records) -KeySelector { param($row) Get-NervOrdinalCompositeKey -Components @($row.lane, $row.assembly) } | ForEach-Object {
            $groupRecords = @(Get-NervOrdinalSortedBy -Items @($_.Group) -KeySelector { param($row) Get-NervOrdinalCompositeKey -Components @($row.testName, $row.displayName, $row.testInstanceId) })
            $assemblyName = [regex]::Replace([string]$groupRecords[0].assembly, '[^A-Za-z0-9_.-]', '_')
            [pscustomobject]@{
                Identity = [string]$_.Name
                Records = $groupRecords
                AssemblyName = $assemblyName
                LegacyFileName = "$($Summary.lane)-$assemblyName-$sha8-attempt-$($Summary.runAttempt).trx"
            }
        })
        $resolvedFileNames = Resolve-NervNormalizedTrxFileNames -Groups $normalizedGroups -Summary $Summary -Sha8 $sha8
        foreach ($normalizedGroup in $normalizedGroups) {
            $groupRecords = @($normalizedGroup.Records)
            $fileName = [string]$resolvedFileNames[[string]$normalizedGroup.Identity]
            $xmlRows = foreach ($record in $groupRecords) {
                $name = [Security.SecurityElement]::Escape([string]$record.displayName)
                # Explicit ordinal comparisons rather than `switch` (#1509 round 3): PowerShell's
                # switch matches culture-aware, and `-CaseSensitive` does not change that — measured,
                # `switch ("failed$([char]0x00AD)")` still takes the 'failed' branch. Here that wrote a
                # `Failed` outcome into a *retained* TRX for a record whose outcome token was not
                # `failed`, i.e. the artifact stopped agreeing with the record it was built from.
                $outcome = if (Test-NervOrdinalEquals ([string]$record.outcome) 'passed') { 'Passed' }
                    elseif (Test-NervOrdinalEquals ([string]$record.outcome) 'failed') { 'Failed' }
                    else { 'NotExecuted' }
                $duration = [TimeSpan]::FromTicks([long]$record.durationTicks).ToString('c', [Globalization.CultureInfo]::InvariantCulture)
                $message = if (Test-NervOrdinalEquals ([string]$record.outcome) 'skipped') { Get-NervRetainedSkipReason $record } elseif (Test-NervOrdinalEquals ([string]$record.outcome) 'failed') { ConvertTo-NervRetainedFailureText ([string]$record.errorMessage) } else { $null }
                $output = if ([string]::IsNullOrWhiteSpace($message)) { '' } else { "<Output><ErrorInfo><Message>$([Security.SecurityElement]::Escape($message))</Message></ErrorInfo></Output>" }
                "<UnitTestResult executionId=`"$($record.testInstanceId)`" testId=`"$($record.definitionId)`" testName=`"$name`" duration=`"$duration`" outcome=`"$outcome`" redactionCount=`"$([int]$record.redactionCount)`">$output</UnitTestResult>"
            }
            $xmlDefinitions = foreach ($definitionGroup in @(Get-NervOrdinalGroups -Items @($groupRecords) -KeySelector { param($row) [string]$row.definitionId })) {
                $record = $definitionGroup.Group[0]
                $assemblyIdentityAttribute = if ($null -eq $record.assembly) { ' nerv:assemblyIdentity="null"' }
                    elseif ($record.assembly -is [string] -and $record.assembly.Length -eq 0) { ' nerv:assemblyIdentity="empty"' }
                    elseif ([string]$record.assembly -match '[/\\]') { ' nerv:assemblyIdentity="verbatim"' }
                    else { '' }
                "<UnitTest id=`"$($record.definitionId)`" name=`"$([Security.SecurityElement]::Escape([string]$record.testName))`" storage=`"$([Security.SecurityElement]::Escape([string]$record.assembly))`"$assemblyIdentityAttribute><TestMethod className=`"$([Security.SecurityElement]::Escape([string]$record.testClassName))`" name=`"$([Security.SecurityElement]::Escape([string]$record.testMethodName))`" /></UnitTest>"
            }
            $passedCount = @($groupRecords | Where-Object { Test-NervOrdinalEquals ([string]$_.outcome) 'passed' }).Count
            $failedCount = @($groupRecords | Where-Object { Test-NervOrdinalEquals ([string]$_.outcome) 'failed' }).Count
            $skippedCount = @($groupRecords | Where-Object { Test-NervOrdinalEquals ([string]$_.outcome) 'skipped' }).Count
            $executedCount = $passedCount + $failedCount
            $groupIdentity = Get-NervOrdinalCompositeKey -Components @($groupRecords[0].lane, $groupRecords[0].assembly)
            $assemblySummary = @($Summary.assemblies | Where-Object {
                Test-NervOrdinalEquals (Get-NervOrdinalCompositeKey -Components @($_.lane, $_.assembly)) $groupIdentity
            })[0]
            $start = [DateTimeOffset]'2000-01-01T00:00:00Z'
            $finish = $start.AddMilliseconds([double]$assemblySummary.elapsedMilliseconds)
            $runIdentity = Get-NervOrdinalCompositeKey -Components @(
                $Summary.workflowRunId,
                [string]$Summary.runAttempt,
                $groupRecords[0].lane,
                $groupRecords[0].assembly)
            $runId = Get-NervStableEvidenceGuid $runIdentity
            $assemblyIdentityNamespace = if (@($groupRecords | Where-Object {
                    $null -eq $_.assembly -or
                    ($_.assembly -is [string] -and $_.assembly.Length -eq 0) -or
                    ([string]$_.assembly -match '[/\\]')
                }).Count -gt 0) { ' xmlns:nerv="urn:nerv-iip:test-evidence:assembly-identity:v1"' } else { '' }
            $safeXml = "<?xml version=`"1.0`" encoding=`"utf-8`"?><TestRun id=`"$runId`" headSha=`"$($Summary.headSha)`" testedSha=`"$($Summary.testedSha)`" xmlns=`"http://microsoft.com/schemas/VisualStudio/TeamTest/2010`"$assemblyIdentityNamespace><Times creation=`"$($start.ToString('o'))`" queuing=`"$($start.ToString('o'))`" start=`"$($start.ToString('o'))`" finish=`"$($finish.ToString('o'))`" /><Results>$([string]::Join('', @($xmlRows)))</Results><TestDefinitions>$([string]::Join('', @($xmlDefinitions)))</TestDefinitions><ResultSummary outcome=`"Completed`"><Counters total=`"$($groupRecords.Count)`" executed=`"$executedCount`" passed=`"$passedCount`" failed=`"$failedCount`" notExecuted=`"$skippedCount`" /></ResultSummary></TestRun>"
            Write-NervUtf8NoBom (Join-Path $temporary "trx/$fileName") $safeXml
        }
        [IO.Directory]::Move($temporary, $OutputDirectory)
    }
    catch {
        if (Test-Path -LiteralPath $temporary) {
            Write-NervUtf8NoBom (Join-Path $temporary 'diagnostics.log') ((Protect-NervTestEvidenceText $_.Exception.Message) + "`n")
        }
        throw
    }
}

function Write-NervTestEvidenceFailureArtifacts {
    param(
        [Parameter(Mandatory)] [string] $OutputDirectory,
        [Parameter(Mandatory)] [hashtable] $RunMetadata,
        [Parameter(Mandatory)] [string] $Diagnostic
    )
    $target = $OutputDirectory
    if (Test-Path -LiteralPath $target) {
        $target = "$OutputDirectory.failure"
        $suffix = 1
        while (Test-Path -LiteralPath $target) {
            $suffix++
            $target = "$OutputDirectory.failure-$suffix"
        }
    }
    $parent = Split-Path -Parent $target
    [IO.Directory]::CreateDirectory($parent) | Out-Null
    $temporary = "$target.tmp-$([Guid]::NewGuid().ToString('N'))"
    [IO.Directory]::CreateDirectory($temporary) | Out-Null
    [IO.Directory]::CreateDirectory((Join-Path $temporary 'trx')) | Out-Null
    $safeDiagnostic = Protect-NervTestEvidenceText $Diagnostic
    if ($safeDiagnostic.Length -gt 1024) { $safeDiagnostic = $safeDiagnostic.Substring(0, 1024) }
    $safeLane = ConvertTo-NervEvidenceIdentity ([string]$RunMetadata.lane) '^[a-z0-9]+(?:-[a-z0-9]+)*(?:-shard-[1-9][0-9]*)?$' 'invalid-lane' 64
    $safeRun = ConvertTo-NervEvidenceIdentity ([string]$RunMetadata.workflowRunId) '^[A-Za-z0-9._-]+$' 'invalid-run' 64
    $safeHeadSha = ConvertTo-NervEvidenceIdentity ([string]$RunMetadata.headSha) '^[0-9a-f]{40}$' 'invalid-head-sha' 40
    $safeTestedSha = ConvertTo-NervEvidenceIdentity ([string]$RunMetadata.testedSha) '^[0-9a-f]{40}$' 'invalid-tested-sha' 40
    $safeRepository = ConvertTo-NervEvidenceIdentity ([string]$RunMetadata.repository) '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$' 'invalid-repository' 128
    $safeJob = ConvertTo-NervEvidenceIdentity ([string]$RunMetadata.jobName) '^[A-Za-z0-9 ._/-]+$' 'invalid-job' 128
    $failure = [pscustomobject][ordered]@{
        schemaVersion = 1
        collectionStatus = 'failed'
        workflowRunId = $safeRun
        runAttempt = if (((([int]$RunMetadata.runAttempt) -ge (1))) -and ((([int]$RunMetadata.runAttempt) -le (1000)))) { [int]$RunMetadata.runAttempt } else { 0 }
        headSha = $safeHeadSha
        testedSha = $safeTestedSha
        lane = $safeLane
        repository = $safeRepository
        jobName = $safeJob
        passed = 0; failed = 0; skipped = 0; executed = 0; total = 0
        violations = @([pscustomobject]@{ code = 'evidence-collection-failed'; id = $safeLane; message = $safeDiagnostic })
    }
    Write-NervUtf8NoBom (Join-Path $temporary 'tests.jsonl') ''
    Write-NervUtf8NoBom (Join-Path $temporary 'summary.json') (($failure | ConvertTo-Json -Depth 20) + "`n")
    $safeMarkdown = "# Test evidence collection failed`n`n- run: $safeRun`n- lane: $safeLane`n- repository: $safeRepository`n- job: $safeJob`n- evidence-collection-failed: $safeDiagnostic`n"
    Write-NervUtf8NoBom (Join-Path $temporary 'summary.md') (Protect-NervTestEvidenceText $safeMarkdown)
    Write-NervUtf8NoBom (Join-Path $temporary 'diagnostics.log') ($safeDiagnostic + "`n")
    [IO.Directory]::Move($temporary, $target)
    return $target
}

function ConvertFrom-NervDotNetConsoleSummary {
    param(
        [Parameter(Mandatory)] [string] $Text,
        [Parameter(Mandatory)] [object] $RunMetadata
    )

    $pattern = '(?im)^.*?(?:Passed|Failed)!\s*-\s*Failed:\s*(?<failed>\d+),\s*Passed:\s*(?<passed>\d+),\s*Skipped:\s*(?<skipped>\d+),\s*Total:\s*(?<total>\d+),\s*Duration:\s*(?:(?<minutes>\d+)\s*m\s*)?(?<value>\d+(?:\.\d+)?)\s*(?<unit>ms|s)\s*-\s*(?<assembly>[^\s]+\.dll)\s*\('
    $summaryMatches = [regex]::Matches($Text, $pattern)
    if ($summaryMatches.Count -eq 0) { throw 'No unambiguous dotnet test project summaries were found.' }
    $assemblies = foreach ($summaryMatch in $summaryMatches) {
        $minutes = if ($summaryMatch.Groups['minutes'].Success) { [double]$summaryMatch.Groups['minutes'].Value } else { 0.0 }
        $tailMilliseconds = if (Test-NervOrdinalEquals ([string]$summaryMatch.Groups['unit'].Value) 'ms') { [double]$summaryMatch.Groups['value'].Value } else { [double]$summaryMatch.Groups['value'].Value * 1000.0 }
        [pscustomobject][ordered]@{
            lane = if ($RunMetadata.ContainsKey('lane')) { [string]$RunMetadata.lane } else { 'backend' }
            assembly = $summaryMatch.Groups['assembly'].Value
            passed = [int]$summaryMatch.Groups['passed'].Value
            failed = [int]$summaryMatch.Groups['failed'].Value
            skipped = [int]$summaryMatch.Groups['skipped'].Value
            executed = [int]$summaryMatch.Groups['passed'].Value + [int]$summaryMatch.Groups['failed'].Value
            total = [int]$summaryMatch.Groups['total'].Value
            elapsedMilliseconds = [double]($minutes * 60000.0 + $tailMilliseconds)
        }
    }
    $duplicates = @(Get-NervOrdinalGroups -Items @($assemblies) -KeySelector { param($row) [string]$row.assembly } | Where-Object { @($_.Group).Count -gt 1 })
    if ($duplicates.Count -gt 0) { throw "Ambiguous console summaries for assembly '$($duplicates[0].Name)'." }
    [pscustomobject][ordered]@{
        schemaVersion = 1
        granularity = 'project'
        durationMetric = 'project-wall-clock'
        lane = 'backend'
        assemblies = @(Get-NervOrdinalSortedBy -Items @($assemblies) -KeySelector { param($row) [string]$row.assembly })
    }
}

function ConvertTo-NervResolvedRunnerImage {
    param([Parameter(Mandatory)] [string] $Image)

    $regexMatch = [regex]::Match($Image, '^ubuntu-(?<major>[0-9]{2})\.04$')
    if ($regexMatch.Success) { return "ubuntu$($regexMatch.Groups['major'].Value)" }
    return $Image
}

function Get-NervGitHubRunnerProvenance {
    param([Parameter(Mandatory)] [string] $Text)
    $lines = @($Text -split '\r?\n')
    $image = $null
    $imageVersion = $null
    for ($index = 0; $index -lt $lines.Count; $index++) {
        $match = [regex]::Match($lines[$index], 'Image:\s*(?<value>(?:ubuntu|windows|macos)-[^\s]+)\s*$')
        if (-not $match.Success) { continue }
        $image = $match.Groups['value'].Value
        for ($next = $index + 1; $next -lt [Math]::Min($lines.Count, $index + 6); $next++) {
            $versionMatch = [regex]::Match($lines[$next], 'Version:\s*(?<value>[0-9][0-9A-Za-z._-]+)\s*$')
            if ($versionMatch.Success) { $imageVersion = $versionMatch.Groups['value'].Value; break }
        }
        break
    }
    $sdkMatch = [regex]::Match($Text, "(?im)(?:\.NET Core SDK with version\s+'|dotnet-sdk=|dotnet-sdk\s+)(?<sdk>[0-9]+\.[0-9]+\.[0-9]+)'?")
    if ([string]::IsNullOrWhiteSpace($image) -or [string]::IsNullOrWhiteSpace($imageVersion) -or -not $sdkMatch.Success) {
        throw 'Actions log does not contain resolved runner image/version and exact dotnet SDK provenance.'
    }
    $normalizedImage = ConvertTo-NervResolvedRunnerImage -Image $image
    $runnerOs = if ($image.StartsWith('ubuntu-', [StringComparison]::Ordinal)) { 'Linux' }
        elseif ($image.StartsWith('windows-', [StringComparison]::Ordinal)) { 'Windows' }
        elseif ($image.StartsWith('macos-', [StringComparison]::Ordinal)) { 'macOS' }
        else { throw "Unsupported Actions runner image '$image'." }
    $testedShaCandidates = [Collections.Generic.List[string]]::new()
    foreach ($match in [regex]::Matches($Text, '(?im)^.*tested-sha=(?<sha>[0-9a-f]{40})\s*$')) {
        $testedShaCandidates.Add($match.Groups['sha'].Value)
    }
    $checkoutPattern = '(?im)^.*\[command\].*git\s+log\s+-1\s+--format=%H\s*$\r?\n^.*?(?<sha>[0-9a-f]{40})\s*$'
    foreach ($match in [regex]::Matches($Text, $checkoutPattern)) {
        $testedShaCandidates.Add($match.Groups['sha'].Value)
    }
    $uniqueTestedShas = @(Get-NervOrdinalSorted -Unique -Values @($testedShaCandidates | ForEach-Object { [string]$_ }))
    if ($uniqueTestedShas.Count -ne 1) {
        throw 'Actions log must contain exactly one authoritative tested SHA from the checkout log or tested-sha marker.'
    }
    [pscustomobject]@{
        runnerOs = $runnerOs
        runnerImage = "$normalizedImage@$imageVersion"
        dotnetSdk = $sdkMatch.Groups['sdk'].Value
        testedSha = [string]$uniqueTestedShas[0]
    }
}

function Assert-NervGitHubRunCheckoutProvenance {
    param(
        [Parameter(Mandatory)] [object] $Run,
        [Parameter(Mandatory)] [object] $RunnerProvenance
    )

    $eventName = [string]$Run.event
    $headSha = [string]$Run.headSha
    $testedSha = [string]$RunnerProvenance.testedSha
    if (-not (Get-NervOrdinalSet -Values @('push', 'pull_request')).Contains([string]$eventName) -or $headSha -notmatch '^[0-9a-f]{40}$' -or $testedSha -notmatch '^[0-9a-f]{40}$') {
        throw 'GitHub run checkout provenance requires a supported event and authoritative head/tested SHAs.'
    }
    if ((Test-NervOrdinalEquals $eventName 'push') -and -not (Test-NervOrdinalEquals $headSha $testedSha)) {
        throw 'Push checkout provenance requires the authoritative tested SHA to equal the run head SHA.'
    }
    [pscustomobject][ordered]@{ headSha = $headSha; testedSha = $testedSha }
}

function Resolve-NervPriorAttemptAuthority {
    param(
        [Parameter(Mandatory)] [object] $Run,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Jobs,
        [Parameter(Mandatory)] [string] $WorkflowRunId,
        [Parameter(Mandatory)] [string] $HeadSha,
        [Parameter(Mandatory)] [int] $RunAttempt,
        [Parameter(Mandatory)] [string] $Lane,
        [Parameter(Mandatory)] [string] $JobName
    )

    $result = [pscustomobject][ordered]@{ verified = $false; outcome = $null }
    if ($RunAttempt -le 1) { return $result }
    $expectedJobs = Get-NervTestEvidenceLaneJobs
    if (-not $expectedJobs.Contains($Lane) -or -not (Test-NervOrdinalEquals ([string]$expectedJobs[$Lane]) $JobName) -or
        -not (Test-NervOrdinalEquals ([string]$Run.id) $WorkflowRunId) -or -not (Test-NervOrdinalEquals ([string]$Run.head_sha) $HeadSha) -or
        (-not (([int]$Run.run_attempt) -eq ($RunAttempt)))) {
        return $result
    }
    $priorAttempt = $RunAttempt - 1
    $failedJobs = @($Jobs | Where-Object {
        (Test-NervOrdinalEquals ([string]$_.name) $JobName) -and (([int]$_.run_attempt) -eq ($priorAttempt)) -and (Test-NervOrdinalEquals ([string]$_.conclusion) 'failure')
    })
    if ($failedJobs.Count -ne 1) { return $result }
    $result.verified = $true
    $result.outcome = 'failure'
    return $result
}

# Provenance splits in two, because the two halves have different kinds of truth behind them.
#
# `Get-NervEvidenceRunIdentityFields` names the run itself. Five jobs of one workflow run share one
# run id, attempt, head/tested SHA, repository, event, branch and run URL by construction, so
# cross-lane inequality there means the summaries came from different runs and the set is not one
# baseline. Equality is a real check and stays byte-for-byte strict.
#
# `Get-NervEvidenceLaneEnvironmentFields` names the machine a single job happened to land on. There
# is no such thing as "this run's runner image": GitHub schedules each job independently, and during
# an image rollout one run legitimately spans two images (run 31149427664 on 2026-08-07 mixed
# ubuntu24@20260720.247.2 and ubuntu24@20260804.265.1 across its five lanes, in a different mix from
# the run before it). Requiring cross-lane equality there asserted a property the platform never
# promised; it held only while the hosted fleet happened to be homogeneous, and it blocked baseline
# refresh the moment that stopped. Per-summary shape validation is kept, and the load-bearing check
# is `Assert-NervEvidenceRootAuthority`, which re-derives each lane's environment from *that lane's
# own* job log — strictly stronger than any cross-lane comparison could be.
function Get-NervEvidenceRunIdentityFields {
    return , @('workflowRunId', 'runAttempt', 'headSha', 'testedSha', 'repository', 'event', 'headBranch', 'sourceUrl')
}

function Get-NervEvidenceLaneEnvironmentFields {
    return , @('runnerOs', 'runnerImage', 'dotnetSdk')
}

function New-NervEvidenceRunIdentity {
    param([Parameter(Mandatory)] [object] $Summary)
    # Deliberately narrow: callers get the run identity and nothing else, so no downstream consumer
    # can reach through the "first summary" and quietly promote one lane's runner environment into a
    # run-wide fact. Under Set-StrictMode that is a hard error, not a silent empty string.
    [pscustomobject][ordered]@{
        workflowRunId = [string]$Summary.workflowRunId
        runAttempt = [int]$Summary.runAttempt
        headSha = [string]$Summary.headSha
        testedSha = [string]$Summary.testedSha
        repository = [string]$Summary.repository
        event = [string]$Summary.event
        headBranch = [string]$Summary.headBranch
        sourceUrl = [string]$Summary.sourceUrl
    }
}

function Get-NervEvidenceLaneProvenance {
    param([Parameter(Mandatory)] [object[]] $SourceSummaries)
    @(Get-NervOrdinalSortedBy -Items @($SourceSummaries) -KeySelector { param($row) [string]$row.lane } | ForEach-Object {
        [pscustomobject][ordered]@{
            lane = [string]$_.lane
            jobName = [string]$_.jobName
            runnerOs = [string]$_.runnerOs
            runnerImage = [string]$_.runnerImage
            dotnetSdk = [string]$_.dotnetSdk
        }
    })
}

function Assert-NervEvidenceSourceSummaries {
    param([Parameter(Mandatory)] [object[]] $SourceSummaries)

    if ($SourceSummaries.Count -eq 0) { throw 'Evidence baseline requires at least one summary.' }
    $first = $SourceSummaries[0]
    $runIdentityFields = Get-NervEvidenceRunIdentityFields
    $laneEnvironmentFields = Get-NervEvidenceLaneEnvironmentFields
    foreach ($summary in $SourceSummaries) {
        foreach ($field in $runIdentityFields) {
            if ([string]::IsNullOrWhiteSpace([string]$summary.$field)) { throw "Evidence summary provenance field '$field' must be nonempty." }
            if (-not (Test-NervOrdinalEquals ([string]$summary.$field) ([string]$first.$field))) { throw "Evidence summaries have mixed provenance field '$field'." }
        }
        foreach ($field in $laneEnvironmentFields) {
            if ([string]::IsNullOrWhiteSpace([string]$summary.$field)) { throw "Evidence summary per-job environment field '$field' must be nonempty." }
        }
        foreach ($field in @('lane', 'jobName', 'artifactName')) {
            if ([string]::IsNullOrWhiteSpace([string]$summary.$field)) { throw "Evidence summary metadata field '$field' must be nonempty." }
        }
        if ((-not (([int]$summary.runAttempt) -eq (1))) -or -not (Test-NervOrdinalEquals ([string]$summary.attemptClassification) 'initial') -or
            -not (Test-NervOrdinalEquals ([string]$summary.currentTestOutcome) 'success') -or -not (Test-NervOrdinalEquals ([string]$summary.collectionStatus) 'succeeded') -or
            (-not (([int]$summary.failed) -eq (0))) -or ((([int]$summary.executed) -le (0))) -or @($summary.violations).Count -ne 0 -or
            -not (Test-NervOrdinalEquals ([string]$summary.event) 'push') -or -not (Test-NervOrdinalEquals ([string]$summary.headBranch) 'main') -or
            [string]$summary.headSha -notmatch '^[0-9a-f]{40}$' -or [string]$summary.testedSha -notmatch '^[0-9a-f]{40}$' -or
            -not (Test-NervOrdinalEquals ([string]$summary.testedSha) ([string]$summary.headSha)) -or
            -not (Test-NervOrdinalEquals ([string]$summary.sourceUrl) "https://github.com/$($summary.repository)/actions/runs/$($summary.workflowRunId)") -or
            [string]$summary.runnerOs -cnotmatch '^(?:Linux|Windows|macOS)$' -or
            [string]$summary.runnerImage -notmatch '^(?:ubuntu[0-9]{2}|(?:ubuntu|windows|macos)-[^@\s]+)@[0-9A-Za-z._-]+$' -or
            [string]$summary.dotnetSdk -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$' -or -not (Test-NervTestEvidenceLaneName ([string]$summary.lane))) {
            throw 'Evidence baseline requires clean successful attempt-1 initial summaries from one main push.'
        }
    }
    if (@(Get-NervOrdinalSorted -Unique -Values @($SourceSummaries | ForEach-Object { [string]$_.lane })).Count -ne $SourceSummaries.Count) { throw 'Evidence summaries must have unique lane metadata.' }
    return New-NervEvidenceRunIdentity -Summary $first
}

function Assert-NervEvidenceRootAuthority {
    param(
        [Parameter(Mandatory)] [object[]] $SourceSummaries,
        [Parameter(Mandatory)] [object] $Run,
        [Parameter(Mandatory)] [object[]] $LatestRuns,
        [Parameter(Mandatory)] [System.Collections.IDictionary] $JobLogs
    )

    $first = Assert-NervEvidenceSourceSummaries -SourceSummaries $SourceSummaries
    if (-not (Test-NervOrdinalEquals ([string]$Run.event) 'push') -or -not (Test-NervOrdinalEquals ([string]$Run.headBranch) 'main') -or (-not (([int]$Run.attempt) -eq (1))) -or
        -not (Test-NervOrdinalEquals ([string]$Run.conclusion) 'success') -or -not (Test-NervOrdinalEquals ([string]$Run.headSha) ([string]$first.headSha)) -or
        -not (Test-NervOrdinalEquals ([string]$Run.url) ([string]$first.sourceUrl)) -or -not (Test-NervOrdinalEquals ([string]$Run.workflowName) 'CI') -or
        -not (Test-NervOrdinalEquals ([string]$Run.databaseId) ([string]$first.workflowRunId))) {
        throw 'Evidence source is not an authoritative successful attempt-1 main CI run.'
    }
    if ($LatestRuns.Count -ne 1 -or -not (Test-NervOrdinalEquals ([string]$LatestRuns[0].databaseId) ([string]$first.workflowRunId)) -or
        (-not (([int]$LatestRuns[0].attempt) -eq (1))) -or -not (Test-NervOrdinalEquals ([string]$LatestRuns[0].headSha) ([string]$first.headSha)) -or
        -not (Test-NervOrdinalEquals ([string]$LatestRuns[0].conclusion) 'success') -or -not (Test-NervOrdinalEquals ([string]$LatestRuns[0].event) 'push') -or
        -not (Test-NervOrdinalEquals ([string]$LatestRuns[0].headBranch) 'main')) {
        throw 'Evidence source is not the latest qualifying successful attempt-1 main CI run.'
    }
    $jobByLane = Get-NervTestEvidenceLaneJobs
    # `-cmatch` stays: that is a regex, not an identifier equality. The dedup and ordering around it
    # are ordinal, because a lane name is an identifier and this comparison decides whether the whole
    # shard family reported in.
    $actualLanes = @(Get-NervOrdinalSorted -Unique -Values @($SourceSummaries | ForEach-Object { [string]$_.lane }))
    $shardFamily = @(Get-NervOrdinalSorted -Values @($jobByLane.Keys | Where-Object { [string]$_ -cmatch '^backend-shard-[1-9][0-9]*$' } | ForEach-Object { [string]$_ }))
    $observedShardLanes = @(Get-NervOrdinalSorted -Values @($actualLanes | Where-Object { [string]$_ -cmatch '^backend-shard-[1-9][0-9]*$' } | ForEach-Object { [string]$_ }))
    if (@($observedShardLanes).Count -gt 0 -and -not (Test-NervOrdinalEquals (@($observedShardLanes) -join '|') (@($shardFamily) -join '|'))) {
        throw 'Evidence baseline requires one summary for every backend fast shard lane.'
    }
    $requiredJobs = @(Get-NervOrdinalSorted -Unique -Values @(@('Backend Tests', 'Connector Host Tests') + @($SourceSummaries | ForEach-Object { [string]$_.jobName })))
    foreach ($requiredJob in $requiredJobs) {
        if (@($Run.jobs | Where-Object { (Test-NervOrdinalEquals ([string]$_.name) $requiredJob) -and (Test-NervOrdinalEquals ([string]$_.conclusion) 'success') }).Count -ne 1) {
            throw "Required evidence job '$requiredJob' is missing, ambiguous, or unsuccessful."
        }
    }
    foreach ($summary in $SourceSummaries) {
        if (-not $jobByLane.Contains([string]$summary.lane) -or -not (Test-NervOrdinalEquals ([string]$summary.jobName) ([string]$jobByLane[[string]$summary.lane]))) {
            throw "Evidence lane '$($summary.lane)' has the wrong authoritative job name."
        }
        if (-not $JobLogs.Contains([string]$summary.jobName) -or [string]::IsNullOrWhiteSpace([string]$JobLogs[[string]$summary.jobName])) {
            throw "Authoritative Actions log for job '$($summary.jobName)' is missing."
        }
        $authority = Get-NervGitHubRunnerProvenance -Text (Protect-ScriptAutomationText ([string]$JobLogs[[string]$summary.jobName]))
        $checkout = Assert-NervGitHubRunCheckoutProvenance -Run $Run -RunnerProvenance $authority
        if (-not (Test-NervOrdinalEquals ([string]$summary.headSha) ([string]$checkout.headSha)) -or
            -not (Test-NervOrdinalEquals ([string]$summary.testedSha) ([string]$checkout.testedSha)) -or
            -not (Test-NervOrdinalEquals ([string]$summary.runnerOs) ([string]$authority.runnerOs)) -or
            -not (Test-NervOrdinalEquals ([string]$summary.runnerImage) ([string]$authority.runnerImage)) -or
            -not (Test-NervOrdinalEquals ([string]$summary.dotnetSdk) ([string]$authority.dotnetSdk))) {
            throw "Evidence runner provenance for lane '$($summary.lane)' does not match the authoritative Actions log."
        }
    }
    return $first
}

function New-NervTestEvidenceBaseline {
    param(
        [Parameter(Mandatory)] [object[]] $Summaries,
        [Parameter(Mandatory)] [object] $SourceMetadata,
        [Parameter(Mandatory)] [DateTimeOffset] $GeneratedAtUtc
    )

    if ([string]$SourceMetadata.headSha -notmatch '^[0-9a-f]{40}$' -or [string]$SourceMetadata.testedSha -notmatch '^[0-9a-f]{40}$' -or
        ((Test-NervOrdinalEquals ([string]$SourceMetadata.event) 'push') -and -not (Test-NervOrdinalEquals ([string]$SourceMetadata.headSha) ([string]$SourceMetadata.testedSha)))) {
        throw 'Baseline provenance requires valid headSha/testedSha values; push sources require equality.'
    }
    # Runner environment is recorded per lane and only per lane. There is no run-wide runnerImage
    # field to write, so no reader can mistake one lane's machine for the whole baseline's.
    #
    # "Per lane" is only honest if the rows actually cover the lanes the baseline records. A partial
    # `laneProvenance` would be worse than the old flat trio, not better: the flat field at least
    # claimed to be run-wide, whereas one row against five lanes of timing is a silent partial record
    # that reads as complete. So coverage is checked both directions — no missing lane, no stray lane.
    [object[]] $laneProvenance = @($SourceMetadata.laneProvenance)
    if ($laneProvenance.Count -eq 0) { throw 'Baseline provenance requires at least one per-lane runner environment row.' }
    $laneJobs = Get-NervTestEvidenceLaneJobs
    foreach ($row in $laneProvenance) {
        if (-not (Test-NervTestEvidenceLaneName ([string]$row.lane)) -or
            [string]$row.runnerOs -cnotmatch '^(?:Linux|Windows|macOS)$' -or
            [string]$row.runnerImage -notmatch '^(?:ubuntu[0-9]{2}|(?:ubuntu|windows|macos)-[^@\s]+)@[0-9A-Za-z._-]+$' -or
            [string]$row.runnerImage -match '(?i)latest' -or [string]$row.dotnetSdk -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$') {
            throw "Baseline provenance requires a resolved runner image/version and exact dotnet SDK for lane '$($row.lane)'."
        }
        # `jobName` is written into the retained baseline, so it is provenance and must be checked like
        # provenance. For an allowlisted lane the binding is exact — a row cannot claim a sibling's job.
        # A lane outside the allowlist (only the legacy console import's unsharded `backend`, which
        # `Get-NervTestEvidenceLaneJobs` deliberately omits) still has to name some job.
        if ([string]::IsNullOrWhiteSpace([string]$row.jobName)) {
            throw "Baseline lane provenance for lane '$($row.lane)' must name the job that produced it."
        }
        if ($laneJobs.Contains([string]$row.lane) -and -not (Test-NervOrdinalEquals ([string]$row.jobName) ([string]$laneJobs[[string]$row.lane]))) {
            throw "Baseline lane provenance for lane '$($row.lane)' names the wrong authoritative job '$($row.jobName)'."
        }
    }
    if (@(Get-NervOrdinalSorted -Unique -Values @($laneProvenance | ForEach-Object { [string]$_.lane })).Count -ne $laneProvenance.Count) {
        throw 'Baseline lane provenance rows must name unique lanes.'
    }
    [string[]] $recordedLanes = @(Get-NervOrdinalSorted -Unique -Values @($Summaries | ForEach-Object { @($_.assemblies) } | ForEach-Object { [string]$_.lane }))
    [string[]] $provenanceLanes = @(Get-NervOrdinalSorted -Unique -Values @($laneProvenance | ForEach-Object { [string]$_.lane }))
    if (-not (Test-NervOrdinalEquals ($provenanceLanes -join '|') ($recordedLanes -join '|'))) {
        throw "Baseline lane provenance must cover exactly the lanes the baseline records; provenance=[$($provenanceLanes -join ', ')] recorded=[$($recordedLanes -join ', ')]."
    }
    $assemblies = @(Get-NervOrdinalGroups -Items @($Summaries | ForEach-Object { @($_.assemblies) }) -KeySelector { param($row) Get-NervOrdinalCompositeKey -Components @($row.lane, $row.assembly) } | ForEach-Object {
        $items = @($_.Group)
        [pscustomobject][ordered]@{
            lane = $items[0].lane
            assembly = $items[0].assembly
            passed = [int](($items | Measure-Object passed -Sum).Sum)
            failed = [int](($items | Measure-Object failed -Sum).Sum)
            skipped = [int](($items | Measure-Object skipped -Sum).Sum)
            executed = [int](($items | Measure-Object executed -Sum).Sum)
            total = [int](($items | Measure-Object total -Sum).Sum)
            elapsedMilliseconds = [double](($items | Measure-Object elapsedMilliseconds -Sum).Sum)
        }
    })
    $granularities = @(Get-NervOrdinalSorted -Unique -Values @($Summaries | ForEach-Object { [string]$_.granularity }))
    [pscustomobject][ordered]@{
        # schema 2 replaced the flat source.runnerOs/runnerImage/dotnetSdk trio with source.laneProvenance.
        # A schema-1 file's flat trio is the *first lane's* environment and must never be read as run-wide.
        schemaVersion = 2
        toolVersion = 'MAN-661-v2'
        granularity = if ($granularities.Count -eq 1) { $granularities[0] } else { 'mixed' }
        durationMetric = if ($granularities.Count -eq 1 -and (Test-NervOrdinalEquals ([string]$granularities[0]) 'test')) { 'trx-elapsed' } else { 'project-wall-clock' }
        owner = 'Nerv-IIP Platform CI/Test Governance'
        generatedAtUtc = $GeneratedAtUtc.UtcDateTime.ToString('o', [Globalization.CultureInfo]::InvariantCulture)
        source = [pscustomobject][ordered]@{
            kind = if ($SourceMetadata.ContainsKey('sourceKind')) { [string]$SourceMetadata.sourceKind } else { 'github-console' }
            repository = if ($SourceMetadata.ContainsKey('repository')) { [string]$SourceMetadata.repository } else { 'Mang-X/Nerv-IIP' }
            workflowRunId = [string]$SourceMetadata.workflowRunId
            runAttempt = [int]$SourceMetadata.runAttempt
            jobId = [string]$SourceMetadata.jobId
            headSha = [string]$SourceMetadata.headSha
            testedSha = [string]$SourceMetadata.testedSha
            sourceUrl = [string]$SourceMetadata.sourceUrl
            event = [string]$SourceMetadata.event
            headBranch = [string]$SourceMetadata.headBranch
            conclusion = [string]$SourceMetadata.conclusion
            jobConclusion = [string]$SourceMetadata.jobConclusion
            laneProvenance = @(Get-NervOrdinalSortedBy -Items @($laneProvenance) -KeySelector { param($row) [string]$row.lane } | ForEach-Object {
                [pscustomobject][ordered]@{
                    lane = [string]$_.lane
                    jobName = [string]$_.jobName
                    runnerOs = [string]$_.runnerOs
                    runnerImage = [string]$_.runnerImage
                    dotnetSdk = [string]$_.dotnetSdk
                }
            })
            selectedLanes = @($SourceMetadata.selectedLanes)
            generatorCommand = [string]$SourceMetadata.generatorCommand
        }
        assemblies = $assemblies
    }
}
