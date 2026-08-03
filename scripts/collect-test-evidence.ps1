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
    [switch] $TestOnlyUsePriorAttemptFixture,
    [string] $PriorAttemptFixturePath,
    [string] $Event,
    [string] $HeadBranch,
    [string] $SourceUrl,
    [string] $RunnerImage,
    [string] $DotnetSdk,
    [string] $ArtifactName,
    [ValidateRange(1, 90)] [int] $RetentionDays = 14,
    [string] $StepSummaryPath,
    [string] $EvidencePathOutputFile = $env:GITHUB_OUTPUT
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
    retentionLocation = if ([string]::IsNullOrWhiteSpace($ArtifactName)) { 'local-output' } else { "artifact://$ArtifactName/" }
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

    $resolvedPriorOutcome = $null
    if ($RunAttempt -gt 1) {
        try {
            $priorAttempt = $RunAttempt - 1
            $expectedJobs = @{ backend = 'Backend Tests'; 'connector-host' = 'Connector Host Tests' }
            $priorRun = $null
            $priorJobs = @()
            if ($TestOnlyUsePriorAttemptFixture) {
                if ([string]::IsNullOrWhiteSpace($PriorAttemptFixturePath) -or -not (Test-Path -LiteralPath $PriorAttemptFixturePath -PathType Leaf)) { throw 'Test-only prior-attempt fixture path is missing.' }
                $fixture = Get-Content -LiteralPath $PriorAttemptFixturePath -Raw | ConvertFrom-Json
                if ([int]$fixture.fixtureVersion -ne 1) { throw 'Unsupported test-only prior-attempt fixture version.' }
                $priorRun = $fixture.run
                $priorJobs = @($fixture.jobs)
            }
            elseif (-not [string]::IsNullOrWhiteSpace($Repository) -and -not [string]::IsNullOrWhiteSpace($JobName) -and -not [string]::IsNullOrWhiteSpace($env:GH_TOKEN)) {
                $runLookup = Invoke-NativeCommandOutput -Command 'gh' -Arguments @('api', "repos/$Repository/actions/runs/$WorkflowRunId") -WorkingDirectory $repoRoot -Name 'man-661-prior-run'
                $priorRun = (Protect-ScriptAutomationText $runLookup.Stdout) | ConvertFrom-Json
                $lookup = Invoke-NativeCommandOutput -Command 'gh' -Arguments @('api', "repos/$Repository/actions/runs/$WorkflowRunId/attempts/$priorAttempt/jobs") -WorkingDirectory $repoRoot -Name 'man-661-prior-attempt'
                $priorJobs = @(((Protect-ScriptAutomationText $lookup.Stdout) | ConvertFrom-Json).jobs)
            }
            $jobs = @($priorJobs | Where-Object { [string]$_.name -ceq $JobName -and [int]$_.run_attempt -eq $priorAttempt -and [string]$_.conclusion -ceq 'failure' })
            if ($expectedJobs.ContainsKey($Lane) -and [string]$expectedJobs[$Lane] -ceq $JobName -and
                $null -ne $priorRun -and [string]$priorRun.id -ceq $WorkflowRunId -and [string]$priorRun.head_sha -ceq $CommitSha -and $jobs.Count -eq 1) {
                $resolvedPriorOutcome = 'failure'
                $runMetadata.priorAttemptVerified = $true
            }
        }
        catch { Write-Diagnostic -Level 'WARN' -Message "Prior attempt lookup unavailable: $(Protect-ScriptAutomationText $_.Exception.Message)" }
    }
    $summary = New-NervTestEvidenceSummary -Records $records -RunMetadata $runMetadata -Violations $violations -Baseline $baseline -PriorAttemptOutcome $resolvedPriorOutcome -TopCount 10
    $summary | Add-Member -NotePropertyName collectionStatus -NotePropertyValue 'succeeded' -Force
    Write-NervTestEvidenceArtifacts -Records $records -Summary $summary -SourceTrxPaths $trxPaths -OutputDirectory $OutputDirectory
    Write-NervEvidenceOutputPath -Path $OutputDirectory -ManifestPath $EvidencePathOutputFile
    if (-not [string]::IsNullOrWhiteSpace($StepSummaryPath)) { [IO.File]::AppendAllText($StepSummaryPath, (Get-Content -LiteralPath (Join-Path $OutputDirectory 'summary.md') -Raw), [Text.UTF8Encoding]::new($false)) }
    Write-Host "Test evidence: lane=$Lane passed=$($summary.passed) failed=$($summary.failed) skipped=$($summary.skipped) executed=$($summary.executed) attempt=$($summary.attemptClassification) timing=report-only"
    if ($violations.Count -gt 0) { foreach ($violation in $violations) { Write-Error "$($violation.code): $($violation.id): $($violation.message)" -ErrorAction Continue }; exit 1 }
}
catch {
    $safeFailure = Protect-NervTestEvidenceText $_.Exception.Message
    $failureOutput = Write-NervTestEvidenceFailureArtifacts -OutputDirectory $OutputDirectory -RunMetadata $runMetadata -Diagnostic $safeFailure
    Write-NervEvidenceOutputPath -Path $failureOutput -ManifestPath $EvidencePathOutputFile
    if (-not [string]::IsNullOrWhiteSpace($StepSummaryPath)) { [IO.File]::AppendAllText($StepSummaryPath, (Get-Content -LiteralPath (Join-Path $failureOutput 'summary.md') -Raw), [Text.UTF8Encoding]::new($false)) }
    Write-Error $safeFailure -ErrorAction Continue
    exit 1
}
