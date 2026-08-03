# Script-Governance:
#   Category: check
#   SideEffects:
#     - Runs deterministic MAN-661 evidence fixtures
#   Writes:
#     - Temporary files under the operating-system temp directory
#   Cleanup:
#     - Removes owned temporary fixture directories in finally
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
$fixtures = Join-Path $PSScriptRoot 'fixtures/test-evidence'
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')
. (Join-Path $repoRoot 'scripts/lib/TestEvidence.ps1')

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-Equal($Expected, $Actual, [string] $Message) {
    if ($Expected -ne $Actual) { throw "$Message Expected=[$Expected] Actual=[$Actual]" }
}

function Assert-Violation([object[]] $Violations, [string] $Code) {
    Assert-True (@($Violations | Where-Object code -eq $Code).Count -gt 0) "Expected violation '$Code'."
}

Assert-True (Test-NervTestEvidenceLaneName 'backend') 'backend must be valid.'
Assert-True (Test-NervTestEvidenceLaneName 'backend-shard-1') 'backend-shard-1 must use schema v1.'
Assert-True (-not (Test-NervTestEvidenceLaneName 'backend/shard/1')) 'slash lane must be rejected.'

$policy = Import-NervTestEvidencePolicy -Path (Join-Path $fixtures 'policy-valid.json')
Assert-Equal 1 $policy.schemaVersion 'Policy schema version must be one.'

$illegal = Import-NervTestEvidencePolicy -Path (Join-Path $fixtures 'policy-illegal-quarantine.json')
$violations = Test-NervTestEvidencePolicy -Policy $illegal -RepoRoot $repoRoot -AsOfUtc ([DateTimeOffset]'2026-08-03T16:00:00Z')
Assert-Violation $violations 'illegal-quarantine'

$liveAssignments = Get-NervSourceSkipAssignments -RepoRoot $repoRoot
Assert-Equal 40 $liveAssignments.Count 'The approved initial source skip inventory changed; classify the diff explicitly.'
$livePolicy = Import-NervTestEvidencePolicy -Path (Join-Path $repoRoot 'scripts/test-evidence-policy.json')
$liveViolations = Test-NervTestEvidencePolicy -Policy $livePolicy -RepoRoot $repoRoot -AsOfUtc ([DateTimeOffset]::UtcNow)
Assert-Equal 0 @($liveViolations).Count 'The committed live skip policy must be valid.'

$run = @{
    workflowRunId = '1001'
    runAttempt = 2
    commitSha = '0123456789abcdef0123456789abcdef01234567'
    lane = 'backend-shard-1'
}
$records = Read-NervTrxResults -Path @((Join-Path $fixtures 'backend-results.trx')) -RunMetadata $run
Assert-Equal 3 $records.Count 'TRX must yield one record per UnitTestResult.'
Assert-Equal 1 @($records | Where-Object outcome -eq 'passed').Count 'Passed outcome mismatch.'
Assert-Equal 1 @($records | Where-Object outcome -eq 'failed').Count 'Failed outcome mismatch.'
Assert-Equal 1 @($records | Where-Object outcome -eq 'skipped').Count 'NotExecuted must normalize to skipped.'
Assert-Equal 'backend-shard-1' $records[0].lane 'Shard lane must not alter schema.'
Assert-Equal 'Nerv.IIP.Sample.Tests.dll' $records[0].assembly 'Assembly must come from UnitTest storage.'
Assert-Equal 1250.0 ($records | Where-Object outcome -eq 'passed').durationMilliseconds 'Duration must use invariant TimeSpan parsing.'
Assert-Equal 'Set NERV_IIP_TEST_POSTGRES to run the fixture.' ($records | Where-Object outcome -eq 'skipped').skipReason 'Skip reason mismatch.'

$combined = Read-NervTrxResults -Path @(
    (Join-Path $fixtures 'backend-results.trx'),
    (Join-Path $fixtures 'connector-results.trx')
) -RunMetadata $run
Assert-Equal 4 $combined.Count 'Multiple TRX files must aggregate.'
Assert-Equal 2 @($combined.assembly | Sort-Object -Unique).Count 'Assemblies must remain distinct.'

$malformedFailed = $false
try { Read-NervTrxResults -Path @((Join-Path $fixtures 'malformed-results.trx')) -RunMetadata $run | Out-Null }
catch {
    $malformedFailed = $true
    Assert-True ($_.Exception.Message.Contains('malformed-results.trx')) 'Malformed diagnostic must name the redacted path.'
    Assert-True (-not $_.Exception.Message.Contains('must-not-appear')) 'Malformed diagnostic must not include raw XML.'
}
Assert-True $malformedFailed 'Malformed TRX must fail parsing.'

$unregisteredRecords = Read-NervTrxResults -Path @((Join-Path $fixtures 'unregistered-skip.trx')) -RunMetadata $run
$violations = Get-NervTestEvidenceViolations -Records $unregisteredRecords -Policy $policy -SelectedLanes @('backend') -RunnerOs 'Linux'
Assert-Violation $violations 'unregistered-skip'

$postgresSelected = Get-NervTestEvidenceViolations -Records $records -Policy $policy -SelectedLanes @('postgres') -RunnerOs 'Linux'
Assert-Violation $postgresSelected 'unregistered-skip'

$postgresRun = $run.Clone()
$postgresRun.lane = 'postgres'
$allSkipped = Read-NervTrxResults -Path @((Join-Path $fixtures 'postgres-all-skipped.trx')) -RunMetadata $postgresRun
$violations = Get-NervTestEvidenceViolations -Records $allSkipped -Policy $livePolicy -SelectedLanes @('postgres') -RunnerOs 'Linux'
Assert-Violation $violations 'zero-execution'

$empty = Read-NervTrxResults -Path @((Join-Path $fixtures 'postgres-zero-results.trx')) -RunMetadata $postgresRun
$violations = Get-NervTestEvidenceViolations -Records $empty -Policy $livePolicy -SelectedLanes @('postgres') -RunnerOs 'Linux'
Assert-Violation $violations 'zero-execution'

$backendEmptyViolations = Get-NervTestEvidenceViolations -Records @() -Policy $livePolicy -SelectedLanes @('backend-shard-1') -RunnerOs 'Linux'
Assert-True (-not (@($backendEmptyViolations | ForEach-Object code) -contains 'zero-execution')) 'Ordinary backend shard zero execution is outside the MAN-661 real-dependency gate.'

$expired = Import-NervTestEvidencePolicy -Path (Join-Path $fixtures 'policy-expired-quarantine.json')
$expiredViolations = Test-NervTestEvidencePolicy -Policy $expired -RepoRoot $repoRoot -AsOfUtc ([DateTimeOffset]'2026-08-03T16:00:00Z')
Assert-Violation $expiredViolations 'illegal-quarantine'
$allowedCodes = @('unregistered-skip', 'illegal-quarantine', 'zero-execution')
Assert-Equal 0 @($expiredViolations | Where-Object { $allowedCodes -notcontains $_.code }).Count 'Evidence layer emitted an unapproved hard-gate code.'

$artifactRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-iip-man-661-artifacts-$([Guid]::NewGuid().ToString('N'))"
try {
    $sensitiveRecords = Read-NervTrxResults -Path @((Join-Path $fixtures 'sensitive-results.trx')) -RunMetadata $run
    $summary = New-NervTestEvidenceSummary -Records $sensitiveRecords -RunMetadata $run -Violations @() -Baseline (Get-Content (Join-Path $fixtures 'baseline-report-only.json') -Raw | ConvertFrom-Json) -PriorAttemptOutcome 'failure' -TopCount 5
    Write-NervTestEvidenceArtifacts -Records $sensitiveRecords -Summary $summary -SourceTrxPaths @((Join-Path $fixtures 'sensitive-results.trx')) -OutputDirectory $artifactRoot
    foreach ($required in @('tests.jsonl', 'summary.json', 'summary.md', 'diagnostics.log')) {
        Assert-True (Test-Path (Join-Path $artifactRoot $required)) "Missing retained artifact '$required'."
    }
    Assert-True (@(Get-ChildItem (Join-Path $artifactRoot 'trx') -Filter '*.trx').Count -gt 0) 'Normalized TRX artifact is missing.'
    $retainedText = [string]::Join("`n", @(Get-ChildItem $artifactRoot -File -Recurse | ForEach-Object { Get-Content $_.FullName -Raw }))
    foreach ($sentinel in @('user:password', 'fixture-bearer-value', 'fixture-client-secret', 'Fixture Customer', '13800000000', 'fixture@example.invalid', 'Fixture Address', 'request body must never be retained')) {
        Assert-True (-not $retainedText.Contains($sentinel)) "Retained evidence leaked sentinel '$sentinel'."
    }
    Assert-True ($summary.attemptClassification -eq 'recovered-after-rerun') 'Prior failure and current clean rerun must be classified report-only.'
    Assert-True ($summary.baseline.enforcement -eq 'report-only') 'Baseline delta must remain report-only.'
    Assert-True ((Get-Content (Join-Path $artifactRoot 'summary.md') -Raw).Contains('recovered-after-rerun')) 'Markdown must render rerun classification.'
}
finally {
    if (Test-Path $artifactRoot) { Remove-Item $artifactRoot -Recurse -Force }
}

$metadata = Get-Content (Join-Path $fixtures 'github-run-metadata.json') -Raw | ConvertFrom-Json -AsHashtable
$imported = ConvertFrom-NervDotNetConsoleSummary -Text (Get-Content (Join-Path $fixtures 'github-backend-console.log.txt') -Raw) -RunMetadata $metadata
Assert-Equal 'project' $imported.granularity 'Console import is project-granularity.'
Assert-Equal 822000 ($imported.assemblies | Where-Object assembly -eq 'Nerv.IIP.BusinessGateway.Web.Tests.dll').durationMilliseconds '13m42s must normalize to milliseconds.'
$baselineA = New-NervTestEvidenceBaseline -Summaries @($imported) -SourceMetadata $metadata -GeneratedAtUtc ([DateTimeOffset]'2026-08-03T14:11:22Z')
$baselineB = New-NervTestEvidenceBaseline -Summaries @($imported) -SourceMetadata $metadata -GeneratedAtUtc ([DateTimeOffset]'2026-08-03T14:11:22Z')
Assert-Equal ($baselineA | ConvertTo-Json -Depth 100) ($baselineB | ConvertTo-Json -Depth 100) 'Baseline generation must be deterministic.'

$collectorRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-iip-man-661-collector-$([Guid]::NewGuid().ToString('N'))"
try {
    $collector = Join-Path $repoRoot 'scripts/collect-test-evidence.ps1'
    $successOut = Join-Path $collectorRoot 'success'
    $successRaw = Join-Path $collectorRoot 'success-raw'
    [IO.Directory]::CreateDirectory($successRaw) | Out-Null
    Copy-Item (Join-Path $fixtures 'connector-results.trx') $successRaw
    Invoke-PwshScript -ScriptPath $collector -WorkingDirectory $repoRoot -Name 'man-661-collector-success' -Arguments @(
        '-Lane', 'backend', '-SelectedLanes', 'backend', '-ResultsDirectory', $successRaw,
        '-OutputDirectory', $successOut, '-WorkflowRunId', 'fixture-success', '-RunAttempt', '1',
        '-CommitSha', '0123456789abcdef0123456789abcdef01234567', '-RunnerOs', 'Linux',
        '-PolicyPath', (Join-Path $repoRoot 'scripts/test-evidence-policy.json'),
        '-BaselinePath', (Join-Path $repoRoot 'scripts/test-evidence-baseline.json')
    ) | Out-Null
    foreach ($required in @('tests.jsonl', 'summary.json', 'summary.md', 'diagnostics.log')) { Assert-True (Test-Path (Join-Path $successOut $required)) "Collector missing '$required'." }

    $badRaw = Join-Path $collectorRoot 'bad-raw'
    [IO.Directory]::CreateDirectory($badRaw) | Out-Null
    Copy-Item (Join-Path $fixtures 'unregistered-skip.trx') $badRaw
    $badFailed = $false
    try {
        Invoke-PwshScript -ScriptPath $collector -WorkingDirectory $repoRoot -Name 'man-661-collector-unregistered' -Arguments @(
            '-Lane', 'backend', '-SelectedLanes', 'backend', '-ResultsDirectory', $badRaw,
            '-OutputDirectory', (Join-Path $collectorRoot 'bad'), '-WorkflowRunId', 'fixture-bad', '-RunAttempt', '1',
            '-CommitSha', '0123456789abcdef0123456789abcdef01234567', '-RunnerOs', 'Linux'
        ) | Out-Null
    }
    catch { $badFailed = $true }
    Assert-True $badFailed 'Unregistered runtime skip must exit nonzero.'
    Assert-True (Test-Path (Join-Path $collectorRoot 'bad/summary.json')) 'Violation collection must retain its summary.'

    $postgresRaw = Join-Path $collectorRoot 'postgres-raw'
    [IO.Directory]::CreateDirectory($postgresRaw) | Out-Null
    Copy-Item (Join-Path $fixtures 'postgres-all-skipped.trx') $postgresRaw
    $postgresFailed = $false
    try {
        Invoke-PwshScript -ScriptPath $collector -WorkingDirectory $repoRoot -Name 'man-661-collector-zero' -Arguments @(
            '-Lane', 'postgres', '-SelectedLanes', 'postgres', '-ResultsDirectory', $postgresRaw,
            '-OutputDirectory', (Join-Path $collectorRoot 'postgres'), '-WorkflowRunId', 'fixture-postgres', '-RunAttempt', '1',
            '-CommitSha', '0123456789abcdef0123456789abcdef01234567', '-RunnerOs', 'Linux'
        ) | Out-Null
    }
    catch { $postgresFailed = $true }
    Assert-True $postgresFailed 'All-skipped real dependency lane must exit nonzero.'
    Assert-True ((Get-Content (Join-Path $collectorRoot 'postgres/summary.json') -Raw).Contains('zero-execution')) 'Zero-execution summary is missing.'

    $rerunRaw = Join-Path $collectorRoot 'rerun-raw'
    [IO.Directory]::CreateDirectory($rerunRaw) | Out-Null
    Copy-Item (Join-Path $fixtures 'connector-results.trx') $rerunRaw
    $rerunOut = Join-Path $collectorRoot 'rerun'
    Invoke-PwshScript -ScriptPath $collector -WorkingDirectory $repoRoot -Name 'man-661-collector-rerun' -Arguments @(
        '-Lane', 'backend', '-SelectedLanes', 'backend', '-ResultsDirectory', $rerunRaw,
        '-OutputDirectory', $rerunOut, '-WorkflowRunId', 'fixture-rerun', '-RunAttempt', '2',
        '-CommitSha', '0123456789abcdef0123456789abcdef01234567', '-RunnerOs', 'Linux', '-PriorAttemptOutcome', 'failure'
    ) | Out-Null
    Assert-True ((Get-Content (Join-Path $rerunOut 'summary.md') -Raw).Contains('recovered-after-rerun')) 'Recovered rerun must be report-only and successful.'
    Assert-True (-not (Test-Path (Join-Path $successOut 'backend-results.trx'))) 'Collector must not copy raw result paths.'
}
finally {
    if (Test-Path $collectorRoot) { Remove-Item $collectorRoot -Recurse -Force }
}

Write-Host "PASS: MAN-661 policy schema; registered source assignments=$($liveAssignments.Count)."
