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
    [ValidateSet('success', 'failure', 'cancelled', 'skipped')] [string] $PriorAttemptOutcome,
    [string] $StepSummaryPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')
. (Join-Path $repoRoot 'scripts/lib/TestEvidence.ps1')

if (-not (Test-NervTestEvidenceLaneName $Lane)) { throw "Invalid evidence lane '$Lane'." }
foreach ($selected in $SelectedLanes) {
    if (-not (Test-NervTestEvidenceLaneName $selected)) { throw "Invalid selected lane '$selected'." }
}
if ($RunAttempt -lt 1) { throw 'RunAttempt must be positive.' }
if ($CommitSha -notmatch '^[0-9a-f]{40}$') { throw 'CommitSha must be a lowercase 40-character SHA.' }
if (-not (Test-Path -LiteralPath $ResultsDirectory -PathType Container)) { throw "Results directory does not exist: '$ResultsDirectory'." }

$policy = Import-NervTestEvidencePolicy -Path $PolicyPath
$policyViolations = @(Test-NervTestEvidencePolicy -Policy $policy -RepoRoot $repoRoot -AsOfUtc ([DateTimeOffset]::UtcNow))
if ($policyViolations.Count -gt 0) {
    throw "Test evidence policy is invalid: $($policyViolations[0].code)/$($policyViolations[0].id)."
}
$baseline = if (Test-Path -LiteralPath $BaselinePath) { Get-Content -LiteralPath $BaselinePath -Raw | ConvertFrom-Json -Depth 100 } else { $null }
$trxPaths = @(Get-ChildItem -LiteralPath $ResultsDirectory -Filter '*.trx' -File -Recurse | Sort-Object FullName | ForEach-Object FullName)
if ($trxPaths.Count -eq 0) { throw "No TRX files found under '$ResultsDirectory'." }

$runMetadata = @{
    workflowRunId = $WorkflowRunId
    runAttempt = $RunAttempt
    commitSha = $CommitSha
    lane = $Lane
}
$records = @(Read-NervTrxResults -Path $trxPaths -RunMetadata $runMetadata)
$violations = @(Get-NervTestEvidenceViolations -Records $records -Policy $policy -SelectedLanes $SelectedLanes -RunnerOs $RunnerOs)

$resolvedPriorOutcome = $PriorAttemptOutcome
if ($RunAttempt -gt 1 -and [string]::IsNullOrWhiteSpace($resolvedPriorOutcome) -and
    -not [string]::IsNullOrWhiteSpace($Repository) -and -not [string]::IsNullOrWhiteSpace($JobName)) {
    try {
        $priorAttempt = $RunAttempt - 1
        $lookup = Invoke-NativeCommandOutput -Command 'gh' -Arguments @(
            'api', "repos/$Repository/actions/runs/$WorkflowRunId/attempts/$priorAttempt/jobs"
        ) -WorkingDirectory $repoRoot -Name 'man-661-prior-attempt'
        $jobs = @(((Protect-ScriptAutomationText $lookup.Stdout) | ConvertFrom-Json).jobs | Where-Object name -eq $JobName)
        if ($jobs.Count -eq 1) { $resolvedPriorOutcome = [string]$jobs[0].conclusion }
    }
    catch {
        $safePriorError = Protect-ScriptAutomationText $_.Exception.Message
        Write-Diagnostic -Level 'WARN' -Message "Prior attempt lookup unavailable: $safePriorError"
    }
}

$summary = New-NervTestEvidenceSummary -Records $records -RunMetadata $runMetadata -Violations $violations -Baseline $baseline -PriorAttemptOutcome $resolvedPriorOutcome -TopCount 10
Write-NervTestEvidenceArtifacts -Records $records -Summary $summary -SourceTrxPaths $trxPaths -OutputDirectory $OutputDirectory
if (-not [string]::IsNullOrWhiteSpace($StepSummaryPath)) {
    $markdown = Get-Content -LiteralPath (Join-Path $OutputDirectory 'summary.md') -Raw
    [IO.File]::AppendAllText($StepSummaryPath, $markdown, [Text.UTF8Encoding]::new($false))
}

Write-Host "Test evidence: lane=$Lane passed=$($summary.passed) failed=$($summary.failed) skipped=$($summary.skipped) executed=$($summary.executed) attempt=$($summary.attemptClassification) timing=report-only"
if ($violations.Count -gt 0) {
    foreach ($violation in $violations) { Write-Error "$($violation.code): $($violation.id): $($violation.message)" -ErrorAction Continue }
    exit 1
}
