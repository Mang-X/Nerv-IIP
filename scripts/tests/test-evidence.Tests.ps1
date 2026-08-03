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

$collectorSource = Get-Content (Join-Path $repoRoot 'scripts/collect-test-evidence.ps1') -Raw
$baselineGeneratorSource = Get-Content (Join-Path $repoRoot 'scripts/generate-test-evidence-baseline.ps1') -Raw

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-Equal($Expected, $Actual, [string] $Message) {
    if ($Expected -ne $Actual) { throw "$Message Expected=[$Expected] Actual=[$Actual]" }
}

function Assert-Violation([object[]] $Violations, [string] $Code) {
    Assert-True (@($Violations | Where-Object code -eq $Code).Count -gt 0) "Expected violation '$Code'."
}

Assert-True (-not $collectorSource.Contains('TestOnly')) 'Production collector must expose no test-only authority replacement parameter.'
Assert-True (-not $baselineGeneratorSource.Contains('TestOnly')) 'Production baseline generator must expose no test-only authority replacement parameter.'
Assert-True ($collectorSource.Contains('deterministic .failure[-N] sibling')) 'Collector governance Writes must declare its owned failure sibling output.'

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
Assert-Equal 1 @($classifiedSummary.skipReasons).Count 'Summary must retain one exact nonempty skip-reason group.'
Assert-Equal 'Set NERV_IIP_TEST_POSTGRES to run the fixture.' $classifiedSummary.skipReasons[0].reason 'Summary skip reason value mismatch.'
Assert-Equal 1 $classifiedSummary.skipReasons[0].count 'Summary skip reason count mismatch.'
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
$classifiedArtifactRoot = "$artifactRoot-classified"
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
    $failedCurrentRun = $recoveryRun.Clone(); $failedCurrentRun.currentTestOutcome = 'failure'
    Assert-Equal 'rerun' (New-NervTestEvidenceSummary -Records $parameterized -RunMetadata $failedCurrentRun -Violations @() -Baseline $null -PriorAttemptOutcome 'failure').attemptClassification 'A failed current native test step must never be called recovered.'
    Assert-Equal 'rerun' (New-NervTestEvidenceSummary -Records @() -RunMetadata $recoveryRun -Violations @() -Baseline $null -PriorAttemptOutcome 'failure').attemptClassification 'A zero-execution rerun must never be called recovered.'
    $unverifiedPriorRun = $recoveryRun.Clone(); $unverifiedPriorRun.priorAttemptVerified = $false
    Assert-Equal 'rerun' (New-NervTestEvidenceSummary -Records $parameterized -RunMetadata $unverifiedPriorRun -Violations @() -Baseline $null -PriorAttemptOutcome 'failure').attemptClassification 'Unverified prior-attempt provenance must never be called recovered.'
    Write-NervTestEvidenceArtifacts -Records $parameterized -Summary $recoverySummary -SourceTrxPaths @((Join-Path $fixtures 'parameterized-results.trx')) -OutputDirectory $parameterArtifactRoot
    $parameterRoundTripRun = $recoveryRun.Clone()
    $parameterRoundTrip = Read-NervTrxResults -Path @((Get-ChildItem (Join-Path $parameterArtifactRoot 'trx') -Filter '*.trx').FullName) -RunMetadata $parameterRoundTripRun
    Assert-Equal 2 @($parameterRoundTrip.displayName | Sort-Object -Unique).Count 'Parameterized display identity must survive normalized TRX round-trip.'
    Assert-Equal 1 @($parameterRoundTrip.definitionId | Sort-Object -Unique).Count 'Parameterized definition identity must survive round-trip.'
    Assert-True ($summary.baseline.enforcement -eq 'report-only') 'Baseline delta must remain report-only.'
    $summaryMarkdown = Get-Content (Join-Path $artifactRoot 'summary.md') -Raw
    foreach ($heading in @('## Assemblies', '## Slowest assemblies and tests', '## Skip reasons', 'Baseline source:', 'Privacy redactions:', 'Retained artifact:')) { Assert-True $summaryMarkdown.Contains($heading) "Markdown is missing '$heading'." }
    Write-NervTestEvidenceArtifacts -Records $records -Summary $classifiedSummary -SourceTrxPaths @((Join-Path $fixtures 'backend-results.trx')) -OutputDirectory $classifiedArtifactRoot
    $classifiedJson = Get-Content (Join-Path $classifiedArtifactRoot 'summary.json') -Raw | ConvertFrom-Json
    Assert-Equal 'Set NERV_IIP_TEST_POSTGRES to run the fixture.' $classifiedJson.skipReasons[0].reason 'Retained JSON skip reason mismatch.'
    Assert-Equal 1 $classifiedJson.skipReasons[0].count 'Retained JSON skip reason count mismatch.'
    Assert-Equal 'environment-gated' $classifiedJson.skipClassifications[0].classification 'Retained JSON skip classification mismatch.'
    Assert-Equal 1 $classifiedJson.skipClassifications[0].count 'Retained JSON skip classification count mismatch.'
    Assert-Equal 'postgres-gated' $classifiedJson.skipPolicies[0].policyId 'Retained JSON skip policy mismatch.'
    Assert-Equal 'environment-gated' $classifiedJson.skipPolicies[0].classification 'Retained JSON skip policy classification mismatch.'
    Assert-Equal 1 $classifiedJson.skipPolicies[0].count 'Retained JSON skip policy count mismatch.'
    $classifiedMarkdown = Get-Content (Join-Path $classifiedArtifactRoot 'summary.md') -Raw
    Assert-True $classifiedMarkdown.Contains('- Set NERV_IIP_TEST_POSTGRES to run the fixture.: 1') 'Markdown must render the exact nonempty skip reason and count.'
    Assert-True $classifiedMarkdown.Contains('- environment-gated / postgres-gated: 1') 'Markdown must render the exact nonempty skip classification/policy/count.'
}
finally {
    if (Test-Path $artifactRoot) { Remove-Item $artifactRoot -Recurse -Force }
    if (Test-Path $parameterArtifactRoot) { Remove-Item $parameterArtifactRoot -Recurse -Force }
    if (Test-Path $classifiedArtifactRoot) { Remove-Item $classifiedArtifactRoot -Recurse -Force }
}

$metadata = Get-Content (Join-Path $fixtures 'github-run-metadata.json') -Raw | ConvertFrom-Json -AsHashtable
$imported = ConvertFrom-NervDotNetConsoleSummary -Text (Get-Content (Join-Path $fixtures 'github-backend-console.log.txt') -Raw) -RunMetadata $metadata
Assert-Equal 'project' $imported.granularity 'Console import is project-granularity.'
Assert-Equal 822000 ($imported.assemblies | Where-Object assembly -eq 'Nerv.IIP.BusinessGateway.Web.Tests.dll').elapsedMilliseconds '13m42s must normalize to milliseconds.'
$baselineA = New-NervTestEvidenceBaseline -Summaries @($imported) -SourceMetadata $metadata -GeneratedAtUtc ([DateTimeOffset]'2026-08-03T14:11:22Z')
$baselineB = New-NervTestEvidenceBaseline -Summaries @($imported) -SourceMetadata $metadata -GeneratedAtUtc ([DateTimeOffset]'2026-08-03T14:11:22Z')
Assert-Equal ($baselineA | ConvertTo-Json -Depth 100) ($baselineB | ConvertTo-Json -Depth 100) 'Baseline generation must be deterministic.'
$selectorMetadata = $metadata.Clone(); $selectorMetadata.runnerImage = 'ubuntu-latest'; $selectorMetadata.dotnetSdk = '10.0.x'
$selectorRejected = $false
try { New-NervTestEvidenceBaseline -Summaries @($imported) -SourceMetadata $selectorMetadata -GeneratedAtUtc ([DateTimeOffset]'2026-08-03T14:11:22Z') | Out-Null } catch { $selectorRejected = $true }
Assert-True $selectorRejected 'Baseline provenance must reject runner selectors and wildcard SDK versions.'
$shardedBaseline = New-NervTestEvidenceBaseline -Summaries @(
    [pscustomobject]@{ granularity = 'test'; assemblies = @([pscustomobject]@{ lane = 'backend-shard-1'; assembly = 'Shared.Tests.dll'; passed = 1; failed = 0; skipped = 0; executed = 1; total = 1; elapsedMilliseconds = 10 }) },
    [pscustomobject]@{ granularity = 'test'; assemblies = @([pscustomobject]@{ lane = 'backend-shard-2'; assembly = 'Shared.Tests.dll'; passed = 1; failed = 0; skipped = 0; executed = 1; total = 1; elapsedMilliseconds = 20 }) }
) -SourceMetadata $metadata -GeneratedAtUtc ([DateTimeOffset]'2026-08-03T14:11:22Z')
Assert-Equal 2 @($shardedBaseline.assemblies).Count 'Baseline identity must be lane plus assembly, not assembly alone.'
Assert-True (-not ($classifiedSummary.baseline.assemblies[0].available)) 'Project wall-clock baseline must not be compared with TRX elapsed timing.'

$invalidBaselineRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-iip-man-661-invalid-baseline-$([Guid]::NewGuid().ToString('N'))"
try {
    $baselineGenerator = Join-Path $repoRoot 'scripts/generate-test-evidence-baseline.ps1'
    $summaryTemplate = [ordered]@{ workflowRunId = '101'; runAttempt = 1; commitSha = '0123456789abcdef0123456789abcdef01234567'; repository = 'Mang-X/Nerv-IIP'; event = 'push'; headBranch = 'main'; sourceUrl = 'https://github.com/Mang-X/Nerv-IIP/actions/runs/101'; runnerOs = 'Linux'; runnerImage = 'ubuntu24@20260720.247.2'; dotnetSdk = '10.0.302'; currentTestOutcome = 'success'; collectionStatus = 'succeeded'; attemptClassification = 'initial'; failed = 0; executed = 1; violations = @(); lane = 'backend'; jobName = 'Backend Tests'; artifactName = 'backend-evidence'; assemblies = @() }
    foreach ($invalidCase in @(
        @{ Name = 'mixed-sha'; Field = 'commitSha'; Value = '1123456789abcdef0123456789abcdef01234567' },
        @{ Name = 'missing-image'; Field = 'runnerImage'; Value = '' },
        @{ Name = 'mixed-sdk'; Field = 'dotnetSdk'; Value = '10.0.303' },
        @{ Name = 'wrong-url'; Field = 'sourceUrl'; Value = 'https://example.invalid/actions/runs/101' }
    )) {
        $caseRoot = Join-Path $invalidBaselineRoot $invalidCase.Name
        [IO.Directory]::CreateDirectory((Join-Path $caseRoot 'a')) | Out-Null; [IO.Directory]::CreateDirectory((Join-Path $caseRoot 'b')) | Out-Null
        Write-NervUtf8NoBom (Join-Path $caseRoot 'a/summary.json') (($summaryTemplate | ConvertTo-Json -Depth 20) + "`n")
        $changed = ($summaryTemplate | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable); $changed[$invalidCase.Field] = $invalidCase.Value; $changed.lane = 'connector-host'; $changed.jobName = 'Connector Host Tests'; $changed.artifactName = 'connector-evidence'
        Write-NervUtf8NoBom (Join-Path $caseRoot 'b/summary.json') (($changed | ConvertTo-Json -Depth 20) + "`n")
        $caseFailed = $false
        try { Invoke-PwshScript -ScriptPath $baselineGenerator -WorkingDirectory $repoRoot -Name "man-661-baseline-$($invalidCase.Name)" -Arguments @('-EvidenceRoot',$caseRoot,'-OutputPath',(Join-Path $caseRoot 'baseline.json')) | Out-Null } catch { $caseFailed = $true }
        Assert-True $caseFailed "Baseline provenance case '$($invalidCase.Name)' must fail."
    }

    $validRoot = Join-Path $invalidBaselineRoot 'valid-authority'
    [IO.Directory]::CreateDirectory((Join-Path $validRoot 'a')) | Out-Null; [IO.Directory]::CreateDirectory((Join-Path $validRoot 'b')) | Out-Null
    Write-NervUtf8NoBom (Join-Path $validRoot 'a/summary.json') (($summaryTemplate | ConvertTo-Json -Depth 20) + "`n")
    $connectorSummary = ($summaryTemplate | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable); $connectorSummary.lane = 'connector-host'; $connectorSummary.jobName = 'Connector Host Tests'; $connectorSummary.artifactName = 'connector-evidence'
    Write-NervUtf8NoBom (Join-Path $validRoot 'b/summary.json') (($connectorSummary | ConvertTo-Json -Depth 20) + "`n")
    $authorityTemplate = [ordered]@{ run = [ordered]@{ databaseId = '101'; event = 'push'; headBranch = 'main'; headSha = '0123456789abcdef0123456789abcdef01234567'; attempt = 1; conclusion = 'success'; url = 'https://github.com/Mang-X/Nerv-IIP/actions/runs/101'; workflowName = 'CI'; jobs = @([ordered]@{ databaseId = '201'; name = 'Backend Tests'; conclusion = 'success' },[ordered]@{ databaseId = '202'; name = 'Connector Host Tests'; conclusion = 'success' }) }; latestRuns = @([ordered]@{ databaseId = '101'; attempt = 1; headSha = '0123456789abcdef0123456789abcdef01234567'; conclusion = 'success'; event = 'push'; headBranch = 'main' }); jobLogs = [ordered]@{ 'Backend Tests' = "Image: ubuntu-24.04`nVersion: 20260720.247.2`ndotnet-install: .NET Core SDK with version '10.0.302' is already installed."; 'Connector Host Tests' = "Image: ubuntu-24.04`nVersion: 20260720.247.2`ndotnet-install: .NET Core SDK with version '10.0.302' is already installed." } }
    $sourceSummaries = @($summaryTemplate, $connectorSummary | ForEach-Object { [pscustomobject]$_ })
    Assert-NervEvidenceRootAuthority -SourceSummaries $sourceSummaries -Run ([pscustomobject]$authorityTemplate.run) -LatestRuns @([pscustomobject]$authorityTemplate.latestRuns[0]) -JobLogs $authorityTemplate.jobLogs | Out-Null
    foreach ($authorityCase in @('wrong-workflow','wrong-job','not-latest','runner-os-mismatch','wrong-resolved-image','wrong-resolved-sdk','latest-attempt-drift','latest-sha-drift','latest-conclusion-drift','latest-event-drift','latest-branch-drift')) {
        $authority = ($authorityTemplate | ConvertTo-Json -Depth 30 | ConvertFrom-Json -AsHashtable)
        $caseSummaries = @($sourceSummaries | ForEach-Object { $_ | ConvertTo-Json -Depth 20 | ConvertFrom-Json })
        if ($authorityCase -eq 'wrong-workflow') { $authority.run.workflowName = 'Other' }
        elseif ($authorityCase -eq 'wrong-job') { $authority.run.jobs[0].name = 'Wrong Backend Job' }
        elseif ($authorityCase -eq 'not-latest') { $authority.latestRuns[0].databaseId = '999' }
        elseif ($authorityCase -eq 'runner-os-mismatch') { $caseSummaries | ForEach-Object { $_.runnerOs = 'Windows' } }
        elseif ($authorityCase -eq 'wrong-resolved-image') { $caseSummaries | ForEach-Object { $_.runnerImage = 'ubuntu24@20260720.247.3' } }
        elseif ($authorityCase -eq 'wrong-resolved-sdk') { $caseSummaries | ForEach-Object { $_.dotnetSdk = '10.0.303' } }
        elseif ($authorityCase -eq 'latest-attempt-drift') { $authority.latestRuns[0].attempt = 2 }
        elseif ($authorityCase -eq 'latest-sha-drift') { $authority.latestRuns[0].headSha = '1123456789abcdef0123456789abcdef01234567' }
        elseif ($authorityCase -eq 'latest-conclusion-drift') { $authority.latestRuns[0].conclusion = 'failure' }
        elseif ($authorityCase -eq 'latest-event-drift') { $authority.latestRuns[0].event = 'pull_request' }
        elseif ($authorityCase -eq 'latest-branch-drift') { $authority.latestRuns[0].headBranch = 'feature' }
        $authorityFailed = $false
        try { Assert-NervEvidenceRootAuthority -SourceSummaries $caseSummaries -Run ([pscustomobject]$authority.run) -LatestRuns @([pscustomobject]$authority.latestRuns[0]) -JobLogs $authority.jobLogs } catch { $authorityFailed = $true }
        Assert-True $authorityFailed "Actions authority case '$authorityCase' must fail."
    }
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
        '-Repository', 'Mang-X/Nerv-IIP', '-JobName', 'Backend Tests', '-CurrentTestOutcome', 'success',
        '-Event', 'push', '-HeadBranch', 'main', '-SourceUrl', 'https://github.com/Mang-X/Nerv-IIP/actions/runs/fixture-success',
        '-RunnerImage', 'ubuntu24@20260720.247.2', '-DotnetSdk', '10.0.302', '-ArtifactName', 'fixture-artifact', '-RetentionDays', '14',
        '-PolicyPath', (Join-Path $repoRoot 'scripts/test-evidence-policy.json'),
        '-BaselinePath', (Join-Path $repoRoot 'scripts/test-evidence-baseline.json')
    ) | Out-Null
    foreach ($required in @('tests.jsonl', 'summary.json', 'summary.md', 'diagnostics.log')) { Assert-True (Test-Path (Join-Path $successOut $required)) "Collector missing '$required'." }
    $successMarkdown = Get-Content (Join-Path $successOut 'summary.md') -Raw
    foreach ($exact in @(
        '| Lane | Assembly | Passed | Failed | Skipped | Executed | Total | Test duration (ms) | TRX elapsed (ms) |',
        '| backend | Nerv.IIP.Connector.Sample.Tests.dll | 1 | 0 | 0 | 1 | 1 | 10 | 20 |',
        'Retained artifact: fixture-artifact, retention=14 days, location=artifact://fixture-artifact/',
        'Baseline source: https://github.com/Mang-X/Nerv-IIP/actions/runs/30819675007',
        'Privacy redactions: 0', 'Assembly backend/Nerv.IIP.Connector.Sample.Tests.dll: 20ms elapsed',
        'Test Fixture.ConnectorTests.connector: 10ms', '## Skip reasons', '- None.'
    )) { Assert-True $successMarkdown.Contains($exact) "Job Summary is missing exact value '$exact'." }

    foreach ($adversarial in @(
        @{ Name = 'identity'; Lane = 'Authorization=Bearer lane-secret'; Selected = 'backend'; Run = 'Authorization=Bearer run-secret'; Repository = 'Authorization=Bearer repo-secret'; Job = 'Authorization=Bearer job-secret' },
        @{ Name = 'violation'; Lane = 'backend'; Selected = 'Authorization=Bearer violation-secret'; Run = 'safe-run'; Repository = 'Mang-X/Nerv-IIP'; Job = 'Backend Tests' }
    )) {
        $adversarialOut = Join-Path $collectorRoot "$($adversarial.Name)-failure"
        $adversarialStep = Join-Path $collectorRoot "$($adversarial.Name)-step.md"
        $adversarialManifest = Join-Path $collectorRoot "$($adversarial.Name)-output.txt"
        $adversarialFailed = $false
        try { Invoke-PwshScript -ScriptPath $collector -WorkingDirectory $repoRoot -Name "man-661-adversarial-$($adversarial.Name)" -Arguments @('-Lane',$adversarial.Lane,'-SelectedLanes',$adversarial.Selected,'-ResultsDirectory',$successRaw,'-OutputDirectory',$adversarialOut,'-WorkflowRunId',$adversarial.Run,'-RunAttempt','1','-CommitSha','Authorization=Bearer commit-secret','-RunnerOs','Linux','-Repository',$adversarial.Repository,'-JobName',$adversarial.Job,'-StepSummaryPath',$adversarialStep,'-EvidencePathOutputFile',$adversarialManifest) | Out-Null } catch { $adversarialFailed = $true }
        Assert-True $adversarialFailed 'Adversarial identity input must fail.'
        $adversarialRetained = [string]::Join("`n", @(Get-ChildItem $adversarialOut -File -Recurse | ForEach-Object { Get-Content $_.FullName -Raw })) + (Get-Content $adversarialStep -Raw) + (Get-Content $adversarialManifest -Raw)
        foreach ($sentinel in @('lane-secret','run-secret','repo-secret','job-secret','violation-secret','commit-secret')) { Assert-True (-not $adversarialRetained.Contains($sentinel)) "Failure bundle leaked '$sentinel'." }
    }

    $conflictOut = Join-Path $collectorRoot 'writer-conflict'
    [IO.Directory]::CreateDirectory($conflictOut) | Out-Null
    Write-NervUtf8NoBom (Join-Path $conflictOut 'unrelated.txt') 'preserve-me'
    $conflictManifest = Join-Path $collectorRoot 'writer-conflict-output.txt'
    $conflictStep = Join-Path $collectorRoot 'writer-conflict-step.md'
    $conflictFailed = $false
    try { Invoke-PwshScript -ScriptPath $collector -WorkingDirectory $repoRoot -Name 'man-661-writer-conflict' -Arguments @('-Lane','backend','-SelectedLanes','backend','-ResultsDirectory',$successRaw,'-OutputDirectory',$conflictOut,'-WorkflowRunId','writer-conflict','-RunAttempt','1','-CommitSha','0123456789abcdef0123456789abcdef01234567','-RunnerOs','Linux','-StepSummaryPath',$conflictStep,'-EvidencePathOutputFile',$conflictManifest) | Out-Null } catch { $conflictFailed = $true }
    Assert-True $conflictFailed 'Writer conflict must preserve nonzero collector status.'
    Assert-Equal 'preserve-me' (Get-Content (Join-Path $conflictOut 'unrelated.txt') -Raw) 'Writer conflict must not overwrite unrelated data.'
    $conflictEvidencePath = ((Get-Content $conflictManifest -Raw).Trim() -replace '^evidence-path=', '')
    Assert-True ($conflictEvidencePath -ne $conflictOut -and (Test-Path (Join-Path $conflictEvidencePath 'summary.json'))) 'Writer conflict must publish an owned failure sibling selected for workflow upload.'
    Assert-True ((Get-Content $conflictStep -Raw).Contains('evidence-collection-failed')) 'Writer conflict must publish Step Summary diagnostics.'

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
    $rerunOut = Join-Path $collectorRoot 'rerun-self-supplied'
    Invoke-PwshScript -ScriptPath $collector -WorkingDirectory $repoRoot -Name 'man-661-collector-rerun' -Arguments @(
        '-Lane', 'backend', '-SelectedLanes', 'backend', '-ResultsDirectory', $rerunRaw,
        '-OutputDirectory', $rerunOut, '-WorkflowRunId', 'fixture-rerun', '-RunAttempt', '2',
        '-CommitSha', '0123456789abcdef0123456789abcdef01234567', '-RunnerOs', 'Linux', '-CurrentTestOutcome', 'success'
    ) | Out-Null
    Assert-True ((Get-Content (Join-Path $rerunOut 'summary.md') -Raw).Contains('- Attempt: rerun ')) 'A rerun without authenticated GitHub evidence must not certify recovery.'
    $priorRun = [pscustomobject]@{ id = 'fixture-rerun'; head_sha = '0123456789abcdef0123456789abcdef01234567'; run_attempt = 2 }
    $priorJobs = @([pscustomobject]@{ name = 'Backend Tests'; run_attempt = 1; conclusion = 'failure' })
    $priorAuthority = Resolve-NervPriorAttemptAuthority -Run $priorRun -Jobs $priorJobs -WorkflowRunId 'fixture-rerun' -CommitSha '0123456789abcdef0123456789abcdef01234567' -RunAttempt 2 -Lane backend -JobName 'Backend Tests'
    Assert-True $priorAuthority.verified 'Pure prior-attempt validation must accept exact authenticated response data.'
    Assert-Equal 'failure' $priorAuthority.outcome 'Pure prior-attempt validation must return the authoritative failed outcome.'
    foreach ($invalidPrior in @(
        @{ Name = 'wrong-run'; Run = [pscustomobject]@{ id = 'other'; head_sha = '0123456789abcdef0123456789abcdef01234567'; run_attempt = 2 }; Jobs = $priorJobs; JobName = 'Backend Tests' },
        @{ Name = 'wrong-sha'; Run = [pscustomobject]@{ id = 'fixture-rerun'; head_sha = '1123456789abcdef0123456789abcdef01234567'; run_attempt = 2 }; Jobs = $priorJobs; JobName = 'Backend Tests' },
        @{ Name = 'wrong-current-attempt'; Run = [pscustomobject]@{ id = 'fixture-rerun'; head_sha = '0123456789abcdef0123456789abcdef01234567'; run_attempt = 3 }; Jobs = $priorJobs; JobName = 'Backend Tests' },
        @{ Name = 'wrong-job'; Run = $priorRun; Jobs = $priorJobs; JobName = 'Other Job' },
        @{ Name = 'wrong-prior-attempt'; Run = $priorRun; Jobs = @([pscustomobject]@{ name = 'Backend Tests'; run_attempt = 2; conclusion = 'failure' }); JobName = 'Backend Tests' },
        @{ Name = 'nonfailure'; Run = $priorRun; Jobs = @([pscustomobject]@{ name = 'Backend Tests'; run_attempt = 1; conclusion = 'success' }); JobName = 'Backend Tests' }
    )) {
        $invalidAuthority = Resolve-NervPriorAttemptAuthority -Run $invalidPrior.Run -Jobs $invalidPrior.Jobs -WorkflowRunId 'fixture-rerun' -CommitSha '0123456789abcdef0123456789abcdef01234567' -RunAttempt 2 -Lane backend -JobName $invalidPrior.JobName
        Assert-True (-not $invalidAuthority.verified) "Prior-attempt authority case '$($invalidPrior.Name)' must fail closed."
    }
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
Assert-True (-not $workflow.Contains('TestOnly')) 'Production workflow must not use any test-only authority seam.'
Assert-True ($workflow.Contains('outputs.evidence-path')) 'Workflow upload must use the collector-selected owned evidence path.'
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
