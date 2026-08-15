# Script-Governance:
#   Category: generate
#   SideEffects:
#     - Reads normalized test evidence or read-only GitHub Actions metadata and logs
#   Writes:
#     - The exact baseline file passed through OutputPath
#   Cleanup:
#     - No process or external resource ownership
#   Requires:
#     - PowerShell 7
#     - GitHub CLI for the GitHubConsole parameter set

[CmdletBinding(DefaultParameterSetName = 'Evidence')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Evidence')] [string] $EvidenceRoot,
    [Parameter(Mandatory, ParameterSetName = 'GitHubConsole')] [string] $Repository,
    [Parameter(Mandatory, ParameterSetName = 'GitHubConsole')] [string] $GitHubRunId,
    [Parameter(Mandatory, ParameterSetName = 'GitHubConsole')] [string] $GitHubJobId,
    [Parameter(Mandatory)] [string] $OutputPath,
    [DateTimeOffset] $GeneratedAtUtc = [DateTimeOffset]::UtcNow
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')
. (Join-Path $repoRoot 'scripts/lib/TestEvidence.ps1')

if ([string]::Equals([string]($PSCmdlet.ParameterSetName), [string]('GitHubConsole'), [StringComparison]::OrdinalIgnoreCase)) {
    $view = Invoke-NativeCommandOutput -Command 'gh' -Arguments @('run', 'view', $GitHubRunId, '--repo', $Repository, '--json', 'event,headBranch,headSha,attempt,conclusion,url,jobs') -WorkingDirectory $repoRoot -Name 'man-661-baseline-run'
    $run = (Protect-ScriptAutomationText $view.Stdout) | ConvertFrom-Json -AsHashtable
    $jobs = @($run.jobs | Where-Object { [string]::Equals([string]([string]$_.databaseId), [string]($GitHubJobId), [StringComparison]::OrdinalIgnoreCase) })
    if ($jobs.Count -ne 1) { throw "GitHub job '$GitHubJobId' was missing or ambiguous in run '$GitHubRunId'." }
    $job = $jobs[0]
    if ([int]$run.attempt -ne 1 -or (-not [string]::Equals([string]([string]$run.conclusion), [string]('success'), [StringComparison]::Ordinal)) -or (-not [string]::Equals([string]([string]$job.conclusion), [string]('success'), [StringComparison]::Ordinal)) -or
        [string]$run.headSha -notmatch '^[0-9a-f]{40}$' -or (-not [string]::Equals([string]([string]$job.name), [string]('Backend Tests'), [StringComparison]::Ordinal))) {
        throw 'Baseline source must be a successful attempt-1 run and successful Backend Tests job with a full head SHA.'
    }
    $logResult = Invoke-NativeCommandOutput -Command 'gh' -Arguments @('run', 'view', $GitHubRunId, '--repo', $Repository, '--job', $GitHubJobId, '--log') -WorkingDirectory $repoRoot -TimeoutSeconds 180 -Name 'man-661-baseline-log'
    $safeLog = Protect-ScriptAutomationText $logResult.Stdout
    $runnerProvenance = Get-NervGitHubRunnerProvenance -Text $safeLog
    $checkoutProvenance = Assert-NervGitHubRunCheckoutProvenance -Run ([pscustomobject]$run) -RunnerProvenance $runnerProvenance
    if ((-not [string]::Equals([string]([string]$run.event), [string]('push'), [StringComparison]::Ordinal)) -or (-not [string]::Equals([string]([string]$run.headBranch), [string]('main'), [StringComparison]::Ordinal))) {
        throw 'Baseline source must be a main push; pull-request checkout provenance is validated but is not baseline-eligible.'
    }
    $metadata = @{
        sourceKind = 'github-console'; repository = $Repository; workflowRunId = $GitHubRunId
        runAttempt = [int]$run.attempt; jobId = $GitHubJobId; headSha = [string]$checkoutProvenance.headSha; testedSha = [string]$checkoutProvenance.testedSha
        sourceUrl = [string]$run.url; event = [string]$run.event; headBranch = [string]$run.headBranch
        conclusion = [string]$run.conclusion; jobConclusion = [string]$job.conclusion
        laneProvenance = @([pscustomobject][ordered]@{
            lane = 'backend'; jobName = [string]$job.name
            runnerOs = [string]$runnerProvenance.runnerOs
            runnerImage = [string]$runnerProvenance.runnerImage
            dotnetSdk = [string]$runnerProvenance.dotnetSdk
        })
        selectedLanes = @('backend'); lane = 'backend'
        generatorCommand = "pwsh scripts/generate-test-evidence-baseline.ps1 -Repository $Repository -GitHubRunId $GitHubRunId -GitHubJobId $GitHubJobId -OutputPath scripts/test-evidence-baseline.json"
    }
    $summaries = @(ConvertFrom-NervDotNetConsoleSummary -Text $safeLog -RunMetadata $metadata)
}
else {
    $summaryPaths = @(Get-NervItemsSortedByString -Items @(Get-ChildItem -LiteralPath $EvidenceRoot -Filter 'summary.json' -File -Recurse) -KeySelector { param($row) [string]$row.FullName } -Comparer ([StringComparer]::Ordinal))
    if ($summaryPaths.Count -eq 0) { throw "No normalized summary.json files found under '$EvidenceRoot'." }
    $sourceSummaries = @($summaryPaths | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json })
    $first = Assert-NervEvidenceSourceSummaries -SourceSummaries $sourceSummaries
    $view = Invoke-NativeCommandOutput -Command 'gh' -Arguments @('run', 'view', [string]$first.workflowRunId, '--repo', [string]$first.repository, '--json', 'databaseId,event,headBranch,headSha,attempt,conclusion,url,workflowName,jobs') -WorkingDirectory $repoRoot -Name 'man-661-evidence-baseline-run'
    $run = (Protect-ScriptAutomationText $view.Stdout) | ConvertFrom-Json
    $latest = Invoke-NativeCommandOutput -Command 'gh' -Arguments @('run', 'list', '--repo', [string]$first.repository, '--workflow', 'CI', '--branch', 'main', '--event', 'push', '--status', 'success', '--limit', '1', '--json', 'databaseId,attempt,headSha,conclusion,event,headBranch') -WorkingDirectory $repoRoot -Name 'man-661-latest-baseline-run'
    $latestRun = @((Protect-ScriptAutomationText $latest.Stdout) | ConvertFrom-Json)
    $jobLogs = @{}
    foreach ($summary in $sourceSummaries) {
        $job = @($run.jobs | Where-Object { [string]::Equals([string]([string]$_.name), [string]([string]$summary.jobName), [StringComparison]::Ordinal) -and [string]::Equals([string]([string]$_.conclusion), [string]('success'), [StringComparison]::Ordinal) })
        if ($job.Count -ne 1) { throw "Evidence job '$($summary.jobName)' is missing, ambiguous, or unsuccessful." }
        $jobLog = Invoke-NativeCommandOutput -Command 'gh' -Arguments @('run', 'view', [string]$first.workflowRunId, '--repo', [string]$first.repository, '--job', [string]$job[0].databaseId, '--log') -WorkingDirectory $repoRoot -TimeoutSeconds 180 -Name "man-661-evidence-$($summary.lane)-log"
        $jobLogs[[string]$summary.jobName] = $jobLog.Stdout
    }
    Assert-NervEvidenceRootAuthority -SourceSummaries $sourceSummaries -Run $run -LatestRuns $latestRun -JobLogs $jobLogs | Out-Null
    $summaries = @($sourceSummaries | ForEach-Object {
        [pscustomobject]@{ schemaVersion = 1; granularity = 'test'; durationMetric = 'trx-elapsed'; lane = $_.lane; assemblies = @($_.assemblies) }
    })
    # Runner environment is read per lane from each lane's own summary — never from `$first`, whose
    # runner image is only the first lane's and is not a property of the run (see TestEvidence.ps1).
    $metadata = @{
        sourceKind = 'trx-evidence'; repository = [string]$first.repository; workflowRunId = [string]$first.workflowRunId; runAttempt = 1; jobId = ''
        headSha = [string]$first.headSha; testedSha = [string]$first.testedSha; sourceUrl = [string]$first.sourceUrl; event = 'push'; headBranch = 'main'; conclusion = 'success'; jobConclusion = 'success'
        laneProvenance = @(Get-NervEvidenceLaneProvenance -SourceSummaries $sourceSummaries); selectedLanes = @(Get-NervStringsSorted -Values @($summaries.lane) -Comparer ([StringComparer]::Ordinal) -Unique)
        generatorCommand = 'pwsh scripts/generate-test-evidence-baseline.ps1 -EvidenceRoot artifacts/test-evidence -OutputPath scripts/test-evidence-baseline.json'
    }
}

$baseline = New-NervTestEvidenceBaseline -Summaries $summaries -SourceMetadata $metadata -GeneratedAtUtc $GeneratedAtUtc
$outputParent = Split-Path -Parent ([IO.Path]::GetFullPath($OutputPath))
[IO.Directory]::CreateDirectory($outputParent) | Out-Null
Write-NervUtf8NoBom -Path ([IO.Path]::GetFullPath($OutputPath)) -Text (($baseline | ConvertTo-Json -Depth 100) + "`n")
Write-Host "Generated test evidence baseline: $OutputPath"
