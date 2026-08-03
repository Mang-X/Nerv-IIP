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
    [string] $TestOnlyActionsFixturePath,
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
    if ([string]$run.event -cne 'push' -or [string]$run.headBranch -cne 'main' -or [int]$run.attempt -ne 1 -or
        [string]$run.conclusion -cne 'success' -or [string]$job.conclusion -cne 'success' -or
        [string]$run.headSha -notmatch '^[0-9a-f]{40}$' -or [string]$job.name -cne 'Backend Tests') {
        throw 'Baseline source must be a successful attempt-1 main push and successful Backend Tests job with a full SHA.'
    }
    $logResult = Invoke-NativeCommandOutput -Command 'gh' -Arguments @('run', 'view', $GitHubRunId, '--repo', $Repository, '--job', $GitHubJobId, '--log') -WorkingDirectory $repoRoot -TimeoutSeconds 180 -Name 'man-661-baseline-log'
    $safeLog = Protect-ScriptAutomationText $logResult.Stdout
    $runnerProvenance = Get-NervGitHubRunnerProvenance -Text $safeLog
    $metadata = @{
        sourceKind = 'github-console'; repository = $Repository; workflowRunId = $GitHubRunId
        runAttempt = [int]$run.attempt; jobId = $GitHubJobId; commitSha = [string]$run.headSha
        sourceUrl = [string]$run.url; event = [string]$run.event; headBranch = [string]$run.headBranch
        conclusion = [string]$run.conclusion; jobConclusion = [string]$job.conclusion
        runnerOs = 'Linux'; runnerImage = $runnerProvenance.runnerImage; dotnetSdk = $runnerProvenance.dotnetSdk; selectedLanes = @('backend'); lane = 'backend'
        generatorCommand = "pwsh scripts/generate-test-evidence-baseline.ps1 -Repository $Repository -GitHubRunId $GitHubRunId -GitHubJobId $GitHubJobId -OutputPath scripts/test-evidence-baseline.json"
    }
    $summaries = @(ConvertFrom-NervDotNetConsoleSummary -Text $safeLog -RunMetadata $metadata)
}
else {
    $summaryPaths = @(Get-ChildItem -LiteralPath $EvidenceRoot -Filter 'summary.json' -File -Recurse | Sort-Object FullName)
    if ($summaryPaths.Count -eq 0) { throw "No normalized summary.json files found under '$EvidenceRoot'." }
    $sourceSummaries = @($summaryPaths | ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json })
    $first = $sourceSummaries[0]
    $commonFields = @('workflowRunId', 'runAttempt', 'commitSha', 'repository', 'event', 'headBranch', 'sourceUrl', 'runnerOs', 'runnerImage', 'dotnetSdk')
    foreach ($summary in $sourceSummaries) {
        foreach ($field in $commonFields) {
            if ([string]::IsNullOrWhiteSpace([string]$summary.$field)) { throw "Evidence summary provenance field '$field' must be nonempty." }
            if ([string]$summary.$field -cne [string]$first.$field) { throw "Evidence summaries have mixed provenance field '$field'." }
        }
        foreach ($field in @('lane', 'jobName', 'artifactName')) {
            if ([string]::IsNullOrWhiteSpace([string]$summary.$field)) { throw "Evidence summary metadata field '$field' must be nonempty." }
        }
        if ([int]$summary.runAttempt -ne 1 -or [string]$summary.attemptClassification -cne 'initial' -or
            [string]$summary.currentTestOutcome -cne 'success' -or [string]$summary.collectionStatus -cne 'succeeded' -or
            [int]$summary.failed -ne 0 -or [int]$summary.executed -le 0 -or @($summary.violations).Count -ne 0 -or
            [string]$summary.event -cne 'push' -or [string]$summary.headBranch -cne 'main' -or
            [string]$summary.commitSha -notmatch '^[0-9a-f]{40}$' -or
            [string]$summary.sourceUrl -cne "https://github.com/$($summary.repository)/actions/runs/$($summary.workflowRunId)" -or
            [string]$summary.runnerImage -notmatch '^(?:ubuntu[0-9]{2}|(?:ubuntu|windows|macos)-[^@\s]+)@[0-9A-Za-z._-]+$' -or
            [string]$summary.dotnetSdk -notmatch '^[0-9]+\.[0-9]+\.[0-9]+$' -or -not (Test-NervTestEvidenceLaneName ([string]$summary.lane))) {
            throw 'Evidence baseline requires clean successful attempt-1 initial summaries from one main push.'
        }
    }
    if (@($sourceSummaries.lane | Sort-Object -Unique).Count -ne $sourceSummaries.Count) { throw 'Evidence summaries must have unique lane metadata.' }
    $actionsFixture = $null
    if (-not [string]::IsNullOrWhiteSpace($TestOnlyActionsFixturePath)) {
        $actionsFixture = Get-Content -LiteralPath $TestOnlyActionsFixturePath -Raw | ConvertFrom-Json
        if ([int]$actionsFixture.fixtureVersion -ne 1) { throw 'Unsupported test-only Actions fixture version.' }
        $run = $actionsFixture.run
        $latestRun = @($actionsFixture.latestRuns)
    }
    else {
        $view = Invoke-NativeCommandOutput -Command 'gh' -Arguments @('run', 'view', [string]$first.workflowRunId, '--repo', [string]$first.repository, '--json', 'databaseId,event,headBranch,headSha,attempt,conclusion,url,workflowName,jobs') -WorkingDirectory $repoRoot -Name 'man-661-evidence-baseline-run'
        $run = (Protect-ScriptAutomationText $view.Stdout) | ConvertFrom-Json
        $latest = Invoke-NativeCommandOutput -Command 'gh' -Arguments @('run', 'list', '--repo', [string]$first.repository, '--workflow', 'CI', '--branch', 'main', '--event', 'push', '--status', 'success', '--limit', '1', '--json', 'databaseId,attempt,headSha,conclusion') -WorkingDirectory $repoRoot -Name 'man-661-latest-baseline-run'
        $latestRun = @((Protect-ScriptAutomationText $latest.Stdout) | ConvertFrom-Json)
    }
    $requiredJobs = @('Backend Tests', 'Connector Host Tests')
    $jobByLane = @{ backend = 'Backend Tests'; 'connector-host' = 'Connector Host Tests' }
    if ([string]$run.event -cne 'push' -or [string]$run.headBranch -cne 'main' -or [int]$run.attempt -ne 1 -or
        [string]$run.conclusion -cne 'success' -or [string]$run.headSha -cne [string]$first.commitSha -or
        [string]$run.url -cne [string]$first.sourceUrl -or [string]$run.workflowName -cne 'CI' -or
        [string]$run.databaseId -cne [string]$first.workflowRunId -or $latestRun.Count -ne 1 -or
        [string]$latestRun[0].databaseId -cne [string]$first.workflowRunId -or
        @($run.jobs | Where-Object { $requiredJobs -contains [string]$_.name -and [string]$_.conclusion -ceq 'success' }).Count -ne $requiredJobs.Count) {
        throw 'Evidence source is not the latest qualifying successful attempt-1 main CI run with all required jobs successful.'
    }
    foreach ($summary in $sourceSummaries) {
        if (-not $jobByLane.ContainsKey([string]$summary.lane) -or [string]$summary.jobName -cne [string]$jobByLane[[string]$summary.lane]) {
            throw "Evidence lane '$($summary.lane)' has the wrong authoritative job name."
        }
        $job = @($run.jobs | Where-Object { [string]$_.name -ceq [string]$summary.jobName -and [string]$_.conclusion -ceq 'success' })
        if ($job.Count -ne 1) { throw "Evidence job '$($summary.jobName)' is missing, ambiguous, or unsuccessful." }
        $jobLogText = if ($null -ne $actionsFixture) {
            [string]$actionsFixture.jobLogs.PSObject.Properties[[string]$summary.jobName].Value
        }
        else {
            $jobLog = Invoke-NativeCommandOutput -Command 'gh' -Arguments @('run', 'view', [string]$first.workflowRunId, '--repo', [string]$first.repository, '--job', [string]$job[0].databaseId, '--log') -WorkingDirectory $repoRoot -TimeoutSeconds 180 -Name "man-661-evidence-$($summary.lane)-log"
            $jobLog.Stdout
        }
        $authority = Get-NervGitHubRunnerProvenance -Text (Protect-ScriptAutomationText $jobLogText)
        if ([string]$summary.runnerImage -cne [string]$authority.runnerImage -or [string]$summary.dotnetSdk -cne [string]$authority.dotnetSdk) {
            throw "Evidence runner provenance for lane '$($summary.lane)' does not match the authoritative Actions log."
        }
    }
    $summaries = @($sourceSummaries | ForEach-Object {
        [pscustomobject]@{ schemaVersion = 1; granularity = 'test'; durationMetric = 'trx-elapsed'; lane = $_.lane; assemblies = @($_.assemblies) }
    })
    $metadata = @{
        sourceKind = 'trx-evidence'; repository = [string]$first.repository; workflowRunId = [string]$first.workflowRunId; runAttempt = 1; jobId = ''
        commitSha = [string]$first.commitSha; sourceUrl = [string]$first.sourceUrl; event = 'push'; headBranch = 'main'; conclusion = 'success'; jobConclusion = 'success'
        runnerOs = [string]$first.runnerOs; runnerImage = [string]$first.runnerImage; dotnetSdk = [string]$first.dotnetSdk; selectedLanes = @($summaries.lane | Sort-Object -Unique)
        generatorCommand = 'pwsh scripts/generate-test-evidence-baseline.ps1 -EvidenceRoot artifacts/test-evidence -OutputPath scripts/test-evidence-baseline.json'
    }
}

$baseline = New-NervTestEvidenceBaseline -Summaries $summaries -SourceMetadata $metadata -GeneratedAtUtc $GeneratedAtUtc
$outputParent = Split-Path -Parent ([IO.Path]::GetFullPath($OutputPath))
[IO.Directory]::CreateDirectory($outputParent) | Out-Null
Write-NervUtf8NoBom -Path ([IO.Path]::GetFullPath($OutputPath)) -Text (($baseline | ConvertTo-Json -Depth 100) + "`n")
Write-Host "Generated test evidence baseline: $OutputPath"
