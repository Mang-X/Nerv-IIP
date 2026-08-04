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

if ($PSCmdlet.ParameterSetName -eq 'GitHubConsole') {
    $view = Invoke-NativeCommandOutput -Command 'gh' -Arguments @('run', 'view', $GitHubRunId, '--repo', $Repository, '--json', 'event,headBranch,headSha,attempt,conclusion,url,jobs') -WorkingDirectory $repoRoot -Name 'man-661-baseline-run'
    $run = (Protect-ScriptAutomationText $view.Stdout) | ConvertFrom-Json -AsHashtable
    $jobs = @($run.jobs | Where-Object { [string]$_.databaseId -eq $GitHubJobId })
    if ($jobs.Count -ne 1) { throw "GitHub job '$GitHubJobId' was missing or ambiguous in run '$GitHubRunId'." }
    $job = $jobs[0]
    if ([int]$run.attempt -ne 1 -or [string]$run.conclusion -cne 'success' -or [string]$job.conclusion -cne 'success' -or
        [string]$run.headSha -notmatch '^[0-9a-f]{40}$' -or [string]$job.name -cne 'Backend Tests') {
        throw 'Baseline source must be a successful attempt-1 run and successful Backend Tests job with a full head SHA.'
    }
    $logResult = Invoke-NativeCommandOutput -Command 'gh' -Arguments @('run', 'view', $GitHubRunId, '--repo', $Repository, '--job', $GitHubJobId, '--log') -WorkingDirectory $repoRoot -TimeoutSeconds 180 -Name 'man-661-baseline-log'
    $safeLog = Protect-ScriptAutomationText $logResult.Stdout
    $runnerProvenance = Get-NervGitHubRunnerProvenance -Text $safeLog
    $checkoutProvenance = Assert-NervGitHubRunCheckoutProvenance -Run ([pscustomobject]$run) -RunnerProvenance $runnerProvenance
    if ([string]$run.event -cne 'push' -or [string]$run.headBranch -cne 'main') {
        throw 'Baseline source must be a main push; pull-request checkout provenance is validated but is not baseline-eligible.'
    }
    $metadata = @{
        sourceKind = 'github-console'; repository = $Repository; workflowRunId = $GitHubRunId
        runAttempt = [int]$run.attempt; jobId = $GitHubJobId; headSha = [string]$checkoutProvenance.headSha; testedSha = [string]$checkoutProvenance.testedSha
        sourceUrl = [string]$run.url; event = [string]$run.event; headBranch = [string]$run.headBranch
        conclusion = [string]$run.conclusion; jobConclusion = [string]$job.conclusion
        runnerOs = $runnerProvenance.runnerOs; runnerImage = $runnerProvenance.runnerImage; dotnetSdk = $runnerProvenance.dotnetSdk; selectedLanes = @('backend'); lane = 'backend'
        generatorCommand = "pwsh scripts/generate-test-evidence-baseline.ps1 -Repository $Repository -GitHubRunId $GitHubRunId -GitHubJobId $GitHubJobId -OutputPath scripts/test-evidence-baseline.json"
    }
    $summaries = @(ConvertFrom-NervDotNetConsoleSummary -Text $safeLog -RunMetadata $metadata)
}
else {
    $summaryPaths = @(Get-ChildItem -LiteralPath $EvidenceRoot -Filter 'summary.json' -File -Recurse | Sort-Object FullName)
    if ($summaryPaths.Count -eq 0) { throw "No normalized summary.json files found under '$EvidenceRoot'." }
    $sourceSummaries = @($summaryPaths | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json })
    $first = Assert-NervEvidenceSourceSummaries -SourceSummaries $sourceSummaries
    $view = Invoke-NativeCommandOutput -Command 'gh' -Arguments @('run', 'view', [string]$first.workflowRunId, '--repo', [string]$first.repository, '--json', 'databaseId,event,headBranch,headSha,attempt,conclusion,url,workflowName,jobs') -WorkingDirectory $repoRoot -Name 'man-661-evidence-baseline-run'
    $run = (Protect-ScriptAutomationText $view.Stdout) | ConvertFrom-Json
    $latest = Invoke-NativeCommandOutput -Command 'gh' -Arguments @('run', 'list', '--repo', [string]$first.repository, '--workflow', 'CI', '--branch', 'main', '--event', 'push', '--status', 'success', '--limit', '1', '--json', 'databaseId,attempt,headSha,conclusion,event,headBranch') -WorkingDirectory $repoRoot -Name 'man-661-latest-baseline-run'
    $latestRun = @((Protect-ScriptAutomationText $latest.Stdout) | ConvertFrom-Json)
    $jobLogs = @{}
    foreach ($summary in $sourceSummaries) {
        $job = @($run.jobs | Where-Object { [string]$_.name -ceq [string]$summary.jobName -and [string]$_.conclusion -ceq 'success' })
        if ($job.Count -ne 1) { throw "Evidence job '$($summary.jobName)' is missing, ambiguous, or unsuccessful." }
        $jobLog = Invoke-NativeCommandOutput -Command 'gh' -Arguments @('run', 'view', [string]$first.workflowRunId, '--repo', [string]$first.repository, '--job', [string]$job[0].databaseId, '--log') -WorkingDirectory $repoRoot -TimeoutSeconds 180 -Name "man-661-evidence-$($summary.lane)-log"
        $jobLogs[[string]$summary.jobName] = $jobLog.Stdout
    }
    Assert-NervEvidenceRootAuthority -SourceSummaries $sourceSummaries -Run $run -LatestRuns $latestRun -JobLogs $jobLogs | Out-Null
    $summaries = @($sourceSummaries | ForEach-Object {
        [pscustomobject]@{ schemaVersion = 1; granularity = 'test'; durationMetric = 'trx-elapsed'; lane = $_.lane; assemblies = @($_.assemblies) }
    })
    $metadata = @{
        sourceKind = 'trx-evidence'; repository = [string]$first.repository; workflowRunId = [string]$first.workflowRunId; runAttempt = 1; jobId = ''
        headSha = [string]$first.headSha; testedSha = [string]$first.testedSha; sourceUrl = [string]$first.sourceUrl; event = 'push'; headBranch = 'main'; conclusion = 'success'; jobConclusion = 'success'
        runnerOs = [string]$first.runnerOs; runnerImage = [string]$first.runnerImage; dotnetSdk = [string]$first.dotnetSdk; selectedLanes = @($summaries.lane | Sort-Object -Unique)
        generatorCommand = 'pwsh scripts/generate-test-evidence-baseline.ps1 -EvidenceRoot artifacts/test-evidence -OutputPath scripts/test-evidence-baseline.json'
    }
}

$baseline = New-NervTestEvidenceBaseline -Summaries $summaries -SourceMetadata $metadata -GeneratedAtUtc $GeneratedAtUtc
$outputParent = Split-Path -Parent ([IO.Path]::GetFullPath($OutputPath))
[IO.Directory]::CreateDirectory($outputParent) | Out-Null
Write-NervUtf8NoBom -Path ([IO.Path]::GetFullPath($OutputPath)) -Text (($baseline | ConvertTo-Json -Depth 100) + "`n")
Write-Host "Generated test evidence baseline: $OutputPath"
