# Script-Governance:
#   Category: library
#   SideEffects:
#     - None
#   Writes:
#     - None
#   Cleanup:
#     - No process or external resource ownership
#   Requires:
#     - PowerShell 7

Set-StrictMode -Version Latest

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
