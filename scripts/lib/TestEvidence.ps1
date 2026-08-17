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

function Get-NervStableEvidenceGuid {
    param([Parameter(Mandatory)] [string] $Value)
    $bytes = [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Value))
    $guidBytes = [byte[]]::new(16)
    [Array]::Copy($bytes, $guidBytes, 16)
    ([Guid]::new($guidBytes)).ToString()
}

. (Join-Path $PSScriptRoot 'TestEvidencePolicy.ps1')
. (Join-Path $PSScriptRoot 'TestEvidencePrivacy.ps1')
. (Join-Path $PSScriptRoot 'TestEvidenceParsing.ps1')
. (Join-Path $PSScriptRoot 'TestEvidenceArtifacts.ps1')
. (Join-Path $PSScriptRoot 'TestEvidenceProvenance.ps1')
. (Join-Path $PSScriptRoot 'TestEvidenceBaseline.ps1')
