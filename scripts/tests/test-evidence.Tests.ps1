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
Assert-True (($liveAssignments | Where-Object sourcePath -like '*SimulatedConnectorHostProcessTests.cs').sourceText.Contains('Windows runs the platform-specific executable resolution contract only')) 'Quote-aware scanner must retain semicolons inside a C# string literal.'
$livePolicy = Import-NervTestEvidencePolicy -Path (Join-Path $repoRoot 'scripts/test-evidence-policy.json')
$liveViolations = Test-NervTestEvidencePolicy -Policy $livePolicy -RepoRoot $repoRoot -AsOfUtc ([DateTimeOffset]::UtcNow)
Assert-Equal 0 @($liveViolations).Count 'The committed live skip policy must be valid.'
$brokenClosure = ($livePolicy | ConvertTo-Json -Depth 100 | ConvertFrom-Json -Depth 100)
$brokenClosure.rules[0].sourceId = 'missing-source'
Assert-Violation (Test-NervTestEvidencePolicy -Policy $brokenClosure -RepoRoot $repoRoot -AsOfUtc ([DateTimeOffset]::UtcNow)) 'unregistered-skip'
$brokenCount = ($livePolicy | ConvertTo-Json -Depth 100 | ConvertFrom-Json -Depth 100)
$brokenCount.rules[0].expectedRuntimeTestCount++
Assert-Violation (Test-NervTestEvidencePolicy -Policy $brokenCount -RepoRoot $repoRoot -AsOfUtc ([DateTimeOffset]::UtcNow)) 'unregistered-skip'

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
Assert-Equal 3000.0 $run.trxElapsedMilliseconds 'TRX elapsed time must remain separate from summed test duration.'
Assert-Equal 3000.0 $run.trxRuns[0].elapsedMilliseconds 'Per-assembly TRX elapsed time must be retained.'

$counterMismatchFailed = $false
try { Read-NervTrxResults -Path @((Join-Path $fixtures 'counter-mismatch.trx')) -RunMetadata $run | Out-Null }
catch { $counterMismatchFailed = $_.Exception.Message.Contains('Counters') }
Assert-True $counterMismatchFailed 'TRX counter/result mismatches must fail closed.'

$parameterized = Read-NervTrxResults -Path @((Join-Path $fixtures 'parameterized-results.trx')) -RunMetadata $run
Assert-Equal 2 @($parameterized.displayName | Sort-Object -Unique).Count 'Parameterized display names must remain distinct.'
Assert-Equal 2 @($parameterized.testInstanceId | Sort-Object -Unique).Count 'Parameterized instances need stable distinct identities.'
Assert-Equal 1 @($parameterized.definitionId | Sort-Object -Unique).Count 'Parameterized instances must share their method definition.'

$classifiedViolations = Get-NervTestEvidenceViolations -Records $records -Policy $policy -SelectedLanes @('backend') -RunnerOs 'Linux'
Assert-Equal 0 @($classifiedViolations).Count 'Registered fixture skip must match exactly one rule.'
Assert-Equal 'environment-gated' ($records | Where-Object outcome -eq 'skipped').skipClassification 'Matched skip classification must be retained for aggregation.'
Assert-Equal 'postgres-gated' ($records | Where-Object outcome -eq 'skipped').skipPolicyId 'Matched skip policy entry must be retained for aggregation.'
$classifiedSummary = New-NervTestEvidenceSummary -Records $records -RunMetadata $run -Violations @() -Baseline $null -PriorAttemptOutcome $null -TopCount 5
Assert-Equal 1 ($classifiedSummary.skipClassifications | Where-Object classification -eq 'environment-gated').count 'Summary must aggregate matched skip classifications.'
Assert-Equal 1 ($classifiedSummary.skipPolicies | Where-Object policyId -eq 'postgres-gated').count 'Summary must aggregate matched skip policy entries.'

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
$futureSharedFact = @([pscustomobject]@{ lane = 'backend'; outcome = 'skipped'; testName = 'Fixture.Postgres.New_ninth_method'; skipReason = 'Set NERV_IIP_TEST_POSTGRES to run the fixture.' })
Assert-Violation (Get-NervTestEvidenceViolations -Records $futureSharedFact -Policy $policy -SelectedLanes @('backend') -RunnerOs 'Linux') 'unregistered-skip'

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
$otherShard = @([pscustomobject]@{ lane = 'postgres-shard-2'; outcome = 'passed'; testName = 'Fixture.Test'; skipReason = $null })
$shardViolations = Get-NervTestEvidenceViolations -Records $otherShard -Policy $livePolicy -SelectedLanes @('postgres-shard-1') -RunnerOs 'Linux'
Assert-Violation $shardViolations 'zero-execution'

$expired = Import-NervTestEvidencePolicy -Path (Join-Path $fixtures 'policy-expired-quarantine.json')
$expiredViolations = Test-NervTestEvidencePolicy -Policy $expired -RepoRoot $repoRoot -AsOfUtc ([DateTimeOffset]'2026-08-03T16:00:00Z')
Assert-Violation $expiredViolations 'illegal-quarantine'
$allowedCodes = @('unregistered-skip', 'illegal-quarantine', 'zero-execution')
Assert-Equal 0 @($expiredViolations | Where-Object { $allowedCodes -notcontains $_.code }).Count 'Evidence layer emitted an unapproved hard-gate code.'

$artifactRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-iip-man-661-artifacts-$([Guid]::NewGuid().ToString('N'))"
$parameterArtifactRoot = "$artifactRoot-parameters"
try {
    $sensitiveRecords = Read-NervTrxResults -Path @((Join-Path $fixtures 'sensitive-results.trx')) -RunMetadata $run
    $summary = New-NervTestEvidenceSummary -Records $sensitiveRecords -RunMetadata $run -Violations @() -Baseline (Get-Content (Join-Path $fixtures 'baseline-report-only.json') -Raw | ConvertFrom-Json) -PriorAttemptOutcome 'failure' -TopCount 5
    Write-NervTestEvidenceArtifacts -Records $sensitiveRecords -Summary $summary -SourceTrxPaths @((Join-Path $fixtures 'sensitive-results.trx')) -OutputDirectory $artifactRoot
    foreach ($required in @('tests.jsonl', 'summary.json', 'summary.md', 'diagnostics.log')) {
        Assert-True (Test-Path (Join-Path $artifactRoot $required)) "Missing retained artifact '$required'."
    }
    $normalizedTrx = @(Get-ChildItem (Join-Path $artifactRoot 'trx') -Filter '*.trx')
    Assert-True ($normalizedTrx.Count -gt 0) 'Normalized TRX artifact is missing.'
    $retainedText = [string]::Join("`n", @(Get-ChildItem $artifactRoot -File -Recurse | ForEach-Object { Get-Content $_.FullName -Raw }))
    foreach ($sentinel in @('user:password', 'fixture-bearer-value', 'fixture-client-secret', 'Fixture Customer', '13800000000', 'fixture@example.invalid', 'Fixture Address', 'quoted-password', 'quoted-auth', 'pem-secret', 'request body must never be retained')) {
        Assert-True (-not $retainedText.Contains($sentinel)) "Retained evidence leaked sentinel '$sentinel'."
    }
    Assert-True ($summary.redactionCount -gt 0) 'Summary must count privacy redactions.'
    $roundTripRun = $run.Clone()
    $roundTrip = Read-NervTrxResults -Path @($normalizedTrx.FullName) -RunMetadata $roundTripRun
    Assert-Equal $sensitiveRecords.Count $roundTrip.Count 'Normalized TRX must round-trip through the parser.'
    Assert-Equal @($sensitiveRecords | Where-Object outcome -eq 'failed').Count @($roundTrip | Where-Object outcome -eq 'failed').Count 'Round-trip counters must remain valid.'
    $recoveryRun = $run.Clone(); $recoveryRun.currentTestOutcome = 'success'; $recoveryRun.priorAttemptVerified = $true
    $recoverySummary = New-NervTestEvidenceSummary -Records $parameterized -RunMetadata $recoveryRun -Violations @() -Baseline $null -PriorAttemptOutcome 'failure'
    Assert-True ($recoverySummary.attemptClassification -eq 'recovered-after-rerun') 'Verified prior failure and current successful non-empty rerun must be report-only recovery.'
    Write-NervTestEvidenceArtifacts -Records $parameterized -Summary $recoverySummary -SourceTrxPaths @((Join-Path $fixtures 'parameterized-results.trx')) -OutputDirectory $parameterArtifactRoot
    $parameterRoundTripRun = $recoveryRun.Clone()
    $parameterRoundTrip = Read-NervTrxResults -Path @((Get-ChildItem (Join-Path $parameterArtifactRoot 'trx') -Filter '*.trx').FullName) -RunMetadata $parameterRoundTripRun
    Assert-Equal 2 @($parameterRoundTrip.displayName | Sort-Object -Unique).Count 'Parameterized display identity must survive normalized TRX round-trip.'
    Assert-Equal 1 @($parameterRoundTrip.definitionId | Sort-Object -Unique).Count 'Parameterized definition identity must survive round-trip.'
    Assert-True ($summary.baseline.enforcement -eq 'report-only') 'Baseline delta must remain report-only.'
    $summaryMarkdown = Get-Content (Join-Path $artifactRoot 'summary.md') -Raw
    foreach ($heading in @('## Assemblies', '## Slowest assemblies and tests', '## Skip reasons', 'Baseline source:', 'Privacy redactions:', 'Retained artifact:')) { Assert-True $summaryMarkdown.Contains($heading) "Markdown is missing '$heading'." }
}
finally {
    if (Test-Path $artifactRoot) { Remove-Item $artifactRoot -Recurse -Force }
    if (Test-Path $parameterArtifactRoot) { Remove-Item $parameterArtifactRoot -Recurse -Force }
}

$metadata = Get-Content (Join-Path $fixtures 'github-run-metadata.json') -Raw | ConvertFrom-Json -AsHashtable
$imported = ConvertFrom-NervDotNetConsoleSummary -Text (Get-Content (Join-Path $fixtures 'github-backend-console.log.txt') -Raw) -RunMetadata $metadata
Assert-Equal 'project' $imported.granularity 'Console import is project-granularity.'
Assert-Equal 822000 ($imported.assemblies | Where-Object assembly -eq 'Nerv.IIP.BusinessGateway.Web.Tests.dll').elapsedMilliseconds '13m42s must normalize to milliseconds.'
$baselineA = New-NervTestEvidenceBaseline -Summaries @($imported) -SourceMetadata $metadata -GeneratedAtUtc ([DateTimeOffset]'2026-08-03T14:11:22Z')
$baselineB = New-NervTestEvidenceBaseline -Summaries @($imported) -SourceMetadata $metadata -GeneratedAtUtc ([DateTimeOffset]'2026-08-03T14:11:22Z')
Assert-Equal ($baselineA | ConvertTo-Json -Depth 100) ($baselineB | ConvertTo-Json -Depth 100) 'Baseline generation must be deterministic.'
$shardedBaseline = New-NervTestEvidenceBaseline -Summaries @(
    [pscustomobject]@{ granularity = 'test'; assemblies = @([pscustomobject]@{ lane = 'backend-shard-1'; assembly = 'Shared.Tests.dll'; passed = 1; failed = 0; skipped = 0; executed = 1; total = 1; elapsedMilliseconds = 10 }) },
    [pscustomobject]@{ granularity = 'test'; assemblies = @([pscustomobject]@{ lane = 'backend-shard-2'; assembly = 'Shared.Tests.dll'; passed = 1; failed = 0; skipped = 0; executed = 1; total = 1; elapsedMilliseconds = 20 }) }
) -SourceMetadata $metadata -GeneratedAtUtc ([DateTimeOffset]'2026-08-03T14:11:22Z')
Assert-Equal 2 @($shardedBaseline.assemblies).Count 'Baseline identity must be lane plus assembly, not assembly alone.'
Assert-True (-not ($classifiedSummary.baseline.assemblies[0].available)) 'Project wall-clock baseline must not be compared with TRX elapsed timing.'

$invalidBaselineRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-iip-man-661-invalid-baseline-$([Guid]::NewGuid().ToString('N'))"
try {
    [IO.Directory]::CreateDirectory((Join-Path $invalidBaselineRoot 'a')) | Out-Null
    [IO.Directory]::CreateDirectory((Join-Path $invalidBaselineRoot 'b')) | Out-Null
    $invalidTemplate = [ordered]@{ workflowRunId = '101'; runAttempt = 1; commitSha = '0123456789abcdef0123456789abcdef01234567'; repository = 'Mang-X/Nerv-IIP'; event = 'push'; headBranch = 'main'; currentTestOutcome = 'success'; collectionStatus = 'succeeded'; attemptClassification = 'initial'; failed = 0; executed = 1; violations = @(); lane = 'backend'; assemblies = @() }
    Write-NervUtf8NoBom (Join-Path $invalidBaselineRoot 'a/summary.json') (($invalidTemplate | ConvertTo-Json -Depth 20) + "`n")
    $mixed = ($invalidTemplate | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable); $mixed.commitSha = '1123456789abcdef0123456789abcdef01234567'
    Write-NervUtf8NoBom (Join-Path $invalidBaselineRoot 'b/summary.json') (($mixed | ConvertTo-Json -Depth 20) + "`n")
    $invalidBaselineFailed = $false
    try { Invoke-PwshScript -ScriptPath (Join-Path $repoRoot 'scripts/generate-test-evidence-baseline.ps1') -WorkingDirectory $repoRoot -Name 'man-661-invalid-baseline' -Arguments @('-EvidenceRoot',$invalidBaselineRoot,'-OutputPath',(Join-Path $invalidBaselineRoot 'baseline.json')) | Out-Null }
    catch { $invalidBaselineFailed = $true }
    Assert-True $invalidBaselineFailed 'Mixed evidence provenance must fail before baseline generation.'
}
finally { if (Test-Path $invalidBaselineRoot) { Remove-Item $invalidBaselineRoot -Recurse -Force } }

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

    foreach ($failureCase in @(
        @{ Name = 'missing'; Results = (Join-Path $collectorRoot 'does-not-exist'); Prepare = $false },
        @{ Name = 'empty'; Results = (Join-Path $collectorRoot 'empty-raw'); Prepare = $true },
        @{ Name = 'malformed'; Results = (Join-Path $collectorRoot 'malformed-raw'); Prepare = $true }
    )) {
        if ($failureCase.Prepare) { [IO.Directory]::CreateDirectory($failureCase.Results) | Out-Null }
        if ($failureCase.Name -eq 'malformed') { Copy-Item (Join-Path $fixtures 'malformed-results.trx') $failureCase.Results }
        $failureOut = Join-Path $collectorRoot "$($failureCase.Name)-out"
        $failureSummary = Join-Path $collectorRoot "$($failureCase.Name)-step.md"
        $caseFailed = $false
        try { Invoke-PwshScript -ScriptPath $collector -WorkingDirectory $repoRoot -Name "man-661-collector-$($failureCase.Name)" -Arguments @('-Lane','backend','-SelectedLanes','backend','-ResultsDirectory',$failureCase.Results,'-OutputDirectory',$failureOut,'-WorkflowRunId','fixture-failure','-RunAttempt','1','-CommitSha','0123456789abcdef0123456789abcdef01234567','-RunnerOs','Linux','-StepSummaryPath',$failureSummary) | Out-Null }
        catch { $caseFailed = $true }
        Assert-True $caseFailed "$($failureCase.Name) collector input must exit nonzero."
        foreach ($required in @('tests.jsonl','summary.json','summary.md','diagnostics.log')) { Assert-True (Test-Path (Join-Path $failureOut $required)) "$($failureCase.Name) failure bundle missing '$required'." }
        Assert-True (Test-Path $failureSummary) "$($failureCase.Name) failure summary was not published."
    }

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
    $badRetained = [string]::Join("`n", @(Get-ChildItem (Join-Path $collectorRoot 'bad') -File -Recurse | ForEach-Object { Get-Content $_.FullName -Raw }))
    Assert-True (-not $badRetained.Contains('unregistered-secret')) 'Unregistered skip reasons must be omitted from every retained format.'

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
        '-CommitSha', '0123456789abcdef0123456789abcdef01234567', '-RunnerOs', 'Linux', '-CurrentTestOutcome', 'success', '-PriorAttemptOutcome', 'failure',
        '-PriorAttemptWorkflowRunId', 'fixture-rerun', '-PriorAttemptCommitSha', '0123456789abcdef0123456789abcdef01234567', '-PriorAttemptLane', 'backend'
    ) | Out-Null
    Assert-True ((Get-Content (Join-Path $rerunOut 'summary.md') -Raw).Contains('recovered-after-rerun')) 'Recovered rerun must be report-only and successful.'
    Assert-True (-not (Test-Path (Join-Path $successOut 'backend-results.trx'))) 'Collector must not copy raw result paths.'
}
finally {
    if (Test-Path $collectorRoot) { Remove-Item $collectorRoot -Recurse -Force }
}

$workflow = Get-Content (Join-Path $repoRoot '.github/workflows/ci.yml') -Raw
Assert-True ($workflow.Contains('actions: read')) 'Rerun lookup needs read-only Actions permission.'
Assert-True ($workflow.Contains('GH_TOKEN: ${{ github.token }}')) 'Rerun lookup must receive the read-only workflow token.'
Assert-True ($workflow.Contains('-CurrentTestOutcome ${{ steps.backend-tests.outcome }}')) 'Backend native test outcome must flow into rerun classification.'
Assert-True ($workflow.Contains('dotnet-sdk=$(dotnet --version)')) 'Evidence provenance must resolve the actual SDK version.'
Assert-True (-not $workflow.Contains('continue-on-error')) 'MAN-661 forbids continue-on-error.'
Assert-True ($workflow.Contains('--logger trx')) 'Backend and Connector Host must emit TRX.'
Assert-True ($workflow.Contains('./scripts/collect-test-evidence.ps1')) 'CI must use the governed collector.'
Assert-True ($workflow.Contains('if: always()')) 'Collection/upload must run after failures.'
Assert-True ($workflow.Contains('test-evidence-backend-${{ github.run_id }}-${{ github.run_attempt }}')) 'Backend artifact identity mismatch.'
Assert-True ($workflow.Contains('test-evidence-connector-host-${{ github.run_id }}-${{ github.run_attempt }}')) 'Connector artifact identity mismatch.'
Assert-True (-not $workflow.Contains('path: artifacts/test-evidence-raw')) 'Raw TRX must not be uploaded.'
foreach ($laneContract in @(
    @{ Test = '- name: Test backend solution'; Collect = '- name: Collect backend test evidence'; Upload = '- name: Upload backend test evidence' },
    @{ Test = '- name: Test connector host solution'; Collect = '- name: Collect connector host test evidence'; Upload = '- name: Upload connector host test evidence' }
)) {
    $testIndex = $workflow.IndexOf($laneContract.Test, [StringComparison]::Ordinal)
    $collectIndex = $workflow.IndexOf($laneContract.Collect, [StringComparison]::Ordinal)
    $uploadIndex = $workflow.IndexOf($laneContract.Upload, [StringComparison]::Ordinal)
    Assert-True ($testIndex -ge 0 -and $testIndex -lt $collectIndex -and $collectIndex -lt $uploadIndex) "Workflow order is invalid for '$($laneContract.Test)'."
    $testBlock = $workflow.Substring($testIndex, $collectIndex - $testIndex)
    Assert-True (-not $testBlock.Contains('if:')) 'Test step must use natural failure semantics.'
    Assert-True (-not $testBlock.Contains('|')) 'Test step must not pipe away the dotnet exit code.'
    $retainedBlock = $workflow.Substring($collectIndex, [Math]::Min($workflow.Length - $collectIndex, $uploadIndex - $collectIndex + 250))
    Assert-True ($retainedBlock.Contains('if: always()')) 'Collector and upload must both be always().'
}

$governanceDocPath = Join-Path $repoRoot 'docs/architecture/test-evidence-governance.md'
Assert-True (Test-Path $governanceDocPath) 'Test evidence governance document is missing.'
$governanceDoc = Get-Content $governanceDocPath -Raw
foreach ($requiredText in @(
    'optional', 'environment-gated', 'quarantined',
    'unregistered-skip', 'illegal-quarantine', 'zero-execution',
    'backend-shard-1', 'MAN-669', 'recovered-after-rerun', 'report-only',
    'continue-on-error', 'Nerv-IIP Platform CI/Test Governance', 'MAN-663',
    'pwsh scripts/generate-test-evidence-baseline.ps1 -EvidenceRoot artifacts/test-evidence -OutputPath scripts/test-evidence-baseline.json',
    'raw TRX', '30819675007', '91706113150', '9dafb512c992b240222c8d9b5ada43e4bfc8ac3d'
)) {
    Assert-True ($governanceDoc.Contains($requiredText)) "Governance document is missing '$requiredText'."
}

Write-Host "PASS: MAN-661 policy schema; registered source assignments=$($liveAssignments.Count)."
