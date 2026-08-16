# Script-Governance:
#   Category: library
#   SideEffects:
#     - Writes caller-declared redacted test-evidence artifacts
#   Writes:
#     - Caller-provided evidence output directories and manifest paths
#   Cleanup:
#     - No process or external resource ownership
#   Requires:
#     - PowerShell 7

Set-StrictMode -Version Latest
function New-NervTestEvidenceSummary {
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Records,
        [Parameter(Mandatory)] [object] $RunMetadata,
        [AllowNull()] [object] $TrxParseResult,
        [Parameter(Mandatory)] [AllowNull()] [AllowEmptyCollection()] [object[]] $Violations,
        [AllowNull()] [object] $Baseline,
        [AllowNull()] [string] $PriorAttemptOutcome,
        [bool] $PriorAttemptVerified = $false,
        [AllowNull()] [string] $CurrentTestOutcome,
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
    [string[]] $selectedLanes = Get-NervOrdinalSorted -Unique -Values @($RunMetadata.selectedLanes | ForEach-Object { [string]$_ })
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
    $trxRuns = if ($null -ne $TrxParseResult) { @($TrxParseResult.TrxRuns) } else { @() }
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
    elseif ((Test-NervOrdinalEquals ([string]$PriorAttemptOutcome) 'failure') -and $PriorAttemptVerified -and
        (Test-NervOrdinalEquals ([string]$CurrentTestOutcome) 'success') -and
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
        repository = [string]$RunMetadata.repository
        event = [string]$RunMetadata.event
        headBranch = [string]$RunMetadata.headBranch
        jobName = [string]$RunMetadata.jobName
        currentTestOutcome = [string]$CurrentTestOutcome
        sourceUrl = [string]$RunMetadata.sourceUrl
        runnerOs = [string]$RunMetadata.runnerOs
        runnerImage = [string]$RunMetadata.runnerImage
        dotnetSdk = [string]$RunMetadata.dotnetSdk
        artifactName = [string]$RunMetadata.artifactName
        retentionDays = [int]$RunMetadata.retentionDays
        retentionLocation = [string]$RunMetadata.retentionLocation
        passed = $passed
        failed = $failed
        skipped = $skipped
        executed = $passed + $failed
        total = $safeRecords.Count
        testDurationMilliseconds = if ($safeRecords.Count -gt 0) { [double](($safeRecords | Measure-Object durationMilliseconds -Sum).Sum) } else { 0.0 }
        trxElapsedMilliseconds = if ($null -ne $TrxParseResult) { [double]$TrxParseResult.TrxElapsedMilliseconds } else { $null }
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
    $safe = Protect-ScriptAutomationText $Text
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
        $safeSummaryJson = Protect-ScriptAutomationText ($Summary | ConvertTo-Json -Depth 100)
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
        Write-NervUtf8NoBom (Join-Path $temporary 'summary.md') ((Protect-ScriptAutomationText ([string]::Join("`n", $markdown))) + "`n")
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
                # The shared lookup keeps the reader and writer inverse mappings in one descriptor
                # table. Unknown values retain the historical writer-only NotExecuted fallback.
                $outcomeMapping = Resolve-NervTrxOutcomeMapping -NormalizedOutcome ([string]$record.outcome)
                if ($null -eq $outcomeMapping) {
                    $outcomeMapping = Resolve-NervTrxOutcomeMapping -WriteFallback
                }
                $outcome = [string]$outcomeMapping.TrxOutcome
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
            Write-NervUtf8NoBom (Join-Path $temporary 'diagnostics.log') ((Protect-ScriptAutomationText $_.Exception.Message) + "`n")
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
    $safeDiagnostic = Protect-ScriptAutomationText $Diagnostic
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
    Write-NervUtf8NoBom (Join-Path $temporary 'summary.md') (Protect-ScriptAutomationText $safeMarkdown)
    Write-NervUtf8NoBom (Join-Path $temporary 'diagnostics.log') ($safeDiagnostic + "`n")
    [IO.Directory]::Move($temporary, $target)
    return $target
}
