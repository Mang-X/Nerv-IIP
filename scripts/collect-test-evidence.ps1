# Script-Governance:
#   Category: check, generate
#   SideEffects:
#     - Reads VSTest TRX, policy, baseline, and optional prior GitHub Actions attempt metadata
#   Writes:
#     - Normalized redacted evidence under the exact OutputDirectory
#     - A deterministic .failure[-N] sibling when the exact OutputDirectory already exists during failure publication
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
    [Parameter(Mandatory)] [string] $HeadSha,
    [Parameter(Mandatory)] [string] $TestedSha,
    [Parameter(Mandatory)] [ValidateSet('Linux', 'Windows', 'macOS')] [string] $RunnerOs,
    [string] $Repository,
    [string] $JobName,
    [ValidateSet('success', 'failure', 'cancelled', 'skipped')] [string] $CurrentTestOutcome,
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

$failureMetadata = @{
    workflowRunId = $WorkflowRunId
    runAttempt = $RunAttempt
    headSha = $HeadSha
    testedSha = $TestedSha
    lane = $Lane
    repository = $Repository
    jobName = $JobName
}
try {
    $runMetadata = New-NervTestEvidenceRunMetadata -WorkflowRunId $WorkflowRunId -RunAttempt $RunAttempt `
        -HeadSha $HeadSha -TestedSha $TestedSha -Lane $Lane -SelectedLanes $SelectedLanes `
        -Repository $Repository -Event $Event -HeadBranch $HeadBranch -JobName $JobName -SourceUrl $SourceUrl `
        -RunnerOs $RunnerOs -RunnerImage $RunnerImage -DotnetSdk $DotnetSdk -ArtifactName $ArtifactName `
        -RetentionDays $RetentionDays
    if (-not (Test-Path -LiteralPath $ResultsDirectory -PathType Container)) { throw "Results directory does not exist: '$ResultsDirectory'." }
    $policy = Import-NervTestEvidencePolicy -Path $PolicyPath
    $policyViolations = @(Test-NervTestEvidencePolicy -Policy $policy -RepoRoot $repoRoot -AsOfUtc ([DateTimeOffset]::UtcNow))
    if ($policyViolations.Count -gt 0) { throw "Test evidence policy is invalid: $($policyViolations[0].code)/$($policyViolations[0].id)." }
    $baseline = if (Test-Path -LiteralPath $BaselinePath) { Get-Content -LiteralPath $BaselinePath -Raw | ConvertFrom-Json -Depth 100 } else { $null }
    $trxPaths = @(Get-NervItemsSortedByString -Items @(Get-ChildItem -LiteralPath $ResultsDirectory -Filter '*.trx' -File -Recurse) -KeySelector { param($row) [string]$row.FullName } -Comparer ([StringComparer]::Ordinal) | ForEach-Object FullName)
    if ($trxPaths.Count -eq 0) { throw "No TRX files found under '$ResultsDirectory'." }
    $trxParseResult = Read-NervTrxResults -Path $trxPaths -RunMetadata $runMetadata
    $records = @($trxParseResult.Records)
    $violations = @(Get-NervTestEvidenceViolations -Records $records -Policy $policy -SelectedLanes $SelectedLanes -RunnerOs $RunnerOs)

    $resolvedPriorOutcome = $null
    $priorAttemptVerified = $false
    if ($RunAttempt -gt 1) {
        try {
            $priorAttempt = $RunAttempt - 1
            $priorRun = $null
            $priorJobs = @()
            if (-not [string]::IsNullOrWhiteSpace($Repository) -and -not [string]::IsNullOrWhiteSpace($JobName) -and -not [string]::IsNullOrWhiteSpace($env:GH_TOKEN)) {
                $runLookup = Invoke-NativeCommandOutput -Command 'gh' -Arguments @('api', "repos/$Repository/actions/runs/$WorkflowRunId") -WorkingDirectory $repoRoot -Name 'man-661-prior-run'
                $priorRun = (Protect-ScriptAutomationText $runLookup.Stdout) | ConvertFrom-Json
                $lookup = Invoke-NativeCommandOutput -Command 'gh' -Arguments @('api', "repos/$Repository/actions/runs/$WorkflowRunId/attempts/$priorAttempt/jobs") -WorkingDirectory $repoRoot -Name 'man-661-prior-attempt'
                $priorJobs = @(((Protect-ScriptAutomationText $lookup.Stdout) | ConvertFrom-Json).jobs)
            }
            if ($null -ne $priorRun) {
                $priorAuthority = Resolve-NervPriorAttemptAuthority -Run $priorRun -Jobs $priorJobs -WorkflowRunId $WorkflowRunId -HeadSha $HeadSha -RunAttempt $RunAttempt -Lane $Lane -JobName $JobName
                $resolvedPriorOutcome = $priorAuthority.outcome
                $priorAttemptVerified = [bool]$priorAuthority.verified
            }
        }
        catch { Write-Diagnostic -Level 'WARN' -Message "Prior attempt lookup unavailable: $(Protect-ScriptAutomationText $_.Exception.Message)" }
    }
    $summary = New-NervTestEvidenceSummary -Records $records -RunMetadata $runMetadata -TrxParseResult $trxParseResult `
        -Violations $violations -Baseline $baseline -PriorAttemptOutcome $resolvedPriorOutcome `
        -PriorAttemptVerified $priorAttemptVerified -CurrentTestOutcome $CurrentTestOutcome -TopCount 10
    $summary | Add-Member -NotePropertyName collectionStatus -NotePropertyValue 'succeeded' -Force
    Write-NervTestEvidenceArtifacts -Records $records -Summary $summary -OutputDirectory $OutputDirectory
    Write-NervEvidenceOutputPath -Path $OutputDirectory -ManifestPath $EvidencePathOutputFile
    if (-not [string]::IsNullOrWhiteSpace($StepSummaryPath)) { [IO.File]::AppendAllText($StepSummaryPath, (Get-Content -LiteralPath (Join-Path $OutputDirectory 'summary.md') -Raw), [Text.UTF8Encoding]::new($false)) }
    Write-Host "Test evidence: lane=$Lane passed=$($summary.passed) failed=$($summary.failed) skipped=$($summary.skipped) executed=$($summary.executed) attempt=$($summary.attemptClassification) timing=report-only"
    if ($violations.Count -gt 0) { foreach ($violation in $violations) { Write-Error "$($violation.code): $($violation.id): $($violation.message)" -ErrorAction Continue }; exit 1 }
}
catch {
    $safeFailure = Protect-ScriptAutomationText $_.Exception.Message
    $failureOutput = Write-NervTestEvidenceFailureArtifacts -OutputDirectory $OutputDirectory -RunMetadata $failureMetadata -Diagnostic $safeFailure
    Write-NervEvidenceOutputPath -Path $failureOutput -ManifestPath $EvidencePathOutputFile
    if (-not [string]::IsNullOrWhiteSpace($StepSummaryPath)) { [IO.File]::AppendAllText($StepSummaryPath, (Get-Content -LiteralPath (Join-Path $failureOutput 'summary.md') -Raw), [Text.UTF8Encoding]::new($false)) }
    Write-Error $safeFailure -ErrorAction Continue
    exit 1
}
