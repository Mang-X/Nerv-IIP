# Script-Governance:
#   Category: library
#   SideEffects:
#     - None; parses caller-provided GitHub Actions evidence and metadata
#   Writes:
#     - None
#   Cleanup:
#     - No process or external resource ownership
#   Requires:
#     - PowerShell 7

Set-StrictMode -Version Latest

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
