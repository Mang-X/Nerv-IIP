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

Write-Host "PASS: MAN-661 policy schema; registered source assignments=$($liveAssignments.Count)."
