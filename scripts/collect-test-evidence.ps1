# Script-Governance:
#   Category: check
#   SideEffects:
#     - Reads VSTest TRX, policy, baseline, and optional prior GitHub Actions attempt metadata
#   Writes:
#     - Normalized redacted evidence under the exact OutputDirectory
#     - Optional Markdown appended to StepSummaryPath
#   Cleanup:
#     - Atomically publishes only completed retained evidence
#   Requires:
#     - PowerShell 7
#     - GitHub CLI only when prior-attempt lookup is requested

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $Lane,
    [Parameter(Mandatory)] [string[]] $SelectedLanes,
    [Parameter(Mandatory)] [string] $ResultsDirectory,
    [Parameter(Mandatory)] [string] $OutputDirectory,
    [string] $PolicyPath = (Join-Path $PSScriptRoot 'test-evidence-policy.json'),
    [string] $BaselinePath = (Join-Path $PSScriptRoot 'test-evidence-baseline.json'),
    [Parameter(Mandatory)] [string] $WorkflowRunId,
    [Parameter(Mandatory)] [int] $RunAttempt,
    [Parameter(Mandatory)] [string] $CommitSha,
    [Parameter(Mandatory)] [ValidateSet('Linux', 'Windows', 'macOS')] [string] $RunnerOs,
    [string] $Repository,
    [string] $JobName,
    [ValidateSet('success', 'failure', 'cancelled', 'skipped')] [string] $CurrentTestOutcome,
    [ValidateSet('success', 'failure', 'cancelled', 'skipped')] [string] $PriorAttemptOutcome,
    [string] $PriorAttemptWorkflowRunId,
    [string] $PriorAttemptCommitSha,
    [string] $PriorAttemptLane,
    [string] $Event,
    [string] $HeadBranch,
    [string] $SourceUrl,
    [string] $RunnerImage,
    [string] $DotnetSdk,
    [string] $ArtifactName,
    [ValidateRange(1, 90)] [int] $RetentionDays = 14,
    [string] $StepSummaryPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')
. (Join-Path $repoRoot 'scripts/lib/TestEvidence.ps1')

$runMetadata = @{
    workflowRunId = $WorkflowRunId
    runAttempt = $RunAttempt
    commitSha = $CommitSha
    lane = $Lane
    repository = $Repository
    event = $Event
    headBranch = $HeadBranch
    jobName = $JobName
    currentTestOutcome = $CurrentTestOutcome
    sourceUrl = $SourceUrl
    runnerOs = $RunnerOs
    runnerImage = $RunnerImage
    dotnetSdk = $DotnetSdk
    artifactName = $ArtifactName
    retentionDays = $RetentionDays
    priorAttemptVerified = $false
}
try {
    if (-not (Test-NervTestEvidenceLaneName $Lane)) { throw "Invalid evidence lane '$Lane'." }
    foreach ($selected in $SelectedLanes) { if (-not (Test-NervTestEvidenceLaneName $selected)) { throw "Invalid selected lane '$selected'." } }
    if ($RunAttempt -lt 1) { throw 'RunAttempt must be positive.' }
    if ($CommitSha -notmatch '^[0-9a-f]{40}$') { throw 'CommitSha must be a lowercase 40-character SHA.' }
    if (-not (Test-Path -LiteralPath $ResultsDirectory -PathType Container)) { throw "Results directory does not exist: '$ResultsDirectory'." }
    $policy = Import-NervTestEvidencePolicy -Path $PolicyPath
    $policyViolations = @(Test-NervTestEvidencePolicy -Policy $policy -RepoRoot $repoRoot -AsOfUtc ([DateTimeOffset]::UtcNow))
    if ($policyViolations.Count -gt 0) { throw "Test evidence policy is invalid: $($policyViolations[0].code)/$($policyViolations[0].id)." }
    $baseline = if (Test-Path -LiteralPath $BaselinePath) { Get-Content -LiteralPath $BaselinePath -Raw | ConvertFrom-Json -Depth 100 } else { $null }
    $trxPaths = @(Get-ChildItem -LiteralPath $ResultsDirectory -Filter '*.trx' -File -Recurse | Sort-Object FullName | ForEach-Object FullName)
    if ($trxPaths.Count -eq 0) { throw "No TRX files found under '$ResultsDirectory'." }
    $records = @(Read-NervTrxResults -Path $trxPaths -RunMetadata $runMetadata)
    $violations = @(Get-NervTestEvidenceViolations -Records $records -Policy $policy -SelectedLanes $SelectedLanes -RunnerOs $RunnerOs)

    $resolvedPriorOutcome = $PriorAttemptOutcome
    if ($RunAttempt -gt 1 -and -not [string]::IsNullOrWhiteSpace($resolvedPriorOutcome) -and
        $PriorAttemptWorkflowRunId -ceq $WorkflowRunId -and $PriorAttemptCommitSha -ceq $CommitSha -and $PriorAttemptLane -ceq $Lane) {
        $runMetadata.priorAttemptVerified = $true
    }
    if ($RunAttempt -gt 1 -and [string]::IsNullOrWhiteSpace($resolvedPriorOutcome) -and
        -not [string]::IsNullOrWhiteSpace($Repository) -and -not [string]::IsNullOrWhiteSpace($JobName)) {
        try {
            $priorAttempt = $RunAttempt - 1
            $runLookup = Invoke-NativeCommandOutput -Command 'gh' -Arguments @('api', "repos/$Repository/actions/runs/$WorkflowRunId") -WorkingDirectory $repoRoot -Name 'man-661-prior-run'
            $run = (Protect-ScriptAutomationText $runLookup.Stdout) | ConvertFrom-Json
            $lookup = Invoke-NativeCommandOutput -Command 'gh' -Arguments @('api', "repos/$Repository/actions/runs/$WorkflowRunId/attempts/$priorAttempt/jobs") -WorkingDirectory $repoRoot -Name 'man-661-prior-attempt'
            $jobs = @(((Protect-ScriptAutomationText $lookup.Stdout) | ConvertFrom-Json).jobs | Where-Object { [string]$_.name -ceq $JobName -and [int]$_.run_attempt -eq $priorAttempt })
            if ([string]$run.id -ceq $WorkflowRunId -and [string]$run.head_sha -ceq $CommitSha -and $jobs.Count -eq 1) {
                $resolvedPriorOutcome = [string]$jobs[0].conclusion
                $runMetadata.priorAttemptVerified = $true
            }
        }
        catch { Write-Diagnostic -Level 'WARN' -Message "Prior attempt lookup unavailable: $(Protect-ScriptAutomationText $_.Exception.Message)" }
    }
    $summary = New-NervTestEvidenceSummary -Records $records -RunMetadata $runMetadata -Violations $violations -Baseline $baseline -PriorAttemptOutcome $resolvedPriorOutcome -TopCount 10
    $summary | Add-Member -NotePropertyName collectionStatus -NotePropertyValue 'succeeded' -Force
    Write-NervTestEvidenceArtifacts -Records $records -Summary $summary -SourceTrxPaths $trxPaths -OutputDirectory $OutputDirectory
    if (-not [string]::IsNullOrWhiteSpace($StepSummaryPath)) { [IO.File]::AppendAllText($StepSummaryPath, (Get-Content -LiteralPath (Join-Path $OutputDirectory 'summary.md') -Raw), [Text.UTF8Encoding]::new($false)) }
    Write-Host "Test evidence: lane=$Lane passed=$($summary.passed) failed=$($summary.failed) skipped=$($summary.skipped) executed=$($summary.executed) attempt=$($summary.attemptClassification) timing=report-only"
    if ($violations.Count -gt 0) { foreach ($violation in $violations) { Write-Error "$($violation.code): $($violation.id): $($violation.message)" -ErrorAction Continue }; exit 1 }
}
catch {
    $safeFailure = Protect-NervTestEvidenceText $_.Exception.Message
    Write-NervTestEvidenceFailureArtifacts -OutputDirectory $OutputDirectory -RunMetadata $runMetadata -Diagnostic $safeFailure
    if (-not [string]::IsNullOrWhiteSpace($StepSummaryPath)) { [IO.File]::AppendAllText($StepSummaryPath, (Get-Content -LiteralPath (Join-Path $OutputDirectory 'summary.md') -Raw), [Text.UTF8Encoding]::new($false)) }
    Write-Error $safeFailure -ErrorAction Continue
    exit 1
}
