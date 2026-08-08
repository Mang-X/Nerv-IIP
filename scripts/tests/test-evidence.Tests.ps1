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
. (Join-Path $repoRoot 'scripts/lib/CiWorkflowBudgets.ps1')

$repoScriptLogRoot = Join-Path $repoRoot 'artifacts/script-logs'
$initialRepoCommandLogs = @(
    if (Test-Path $repoScriptLogRoot) {
        Get-ChildItem $repoScriptLogRoot -Directory -Filter 'man-661-*' |
            ForEach-Object { Get-ChildItem $_.FullName -Directory | ForEach-Object FullName }
    }
)

$collectorSource = Get-Content (Join-Path $repoRoot 'scripts/collect-test-evidence.ps1') -Raw
$baselineGeneratorSource = Get-Content (Join-Path $repoRoot 'scripts/generate-test-evidence-baseline.ps1') -Raw

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-Equal($Expected, $Actual, [string] $Message) {
    if ($Expected -ne $Actual) { throw "$Message Expected=[$Expected] Actual=[$Actual]" }
}

# Exact, not "contains": a fixture that also trips codes nobody asked for means the classification
# under test is bleeding into its neighbours, and a containment assertion cannot see that.
function Assert-ViolationSet([object[]] $Violations, [string[]] $Codes) {
    $actual = @($Violations | ForEach-Object code | Sort-Object -Unique) -join ','
    $expected = @($Codes | Sort-Object -Unique) -join ','
    $callerLine = (Get-PSCallStack)[1].ScriptLineNumber
    Assert-True ($actual -ceq $expected) "Violation code set mismatch at line $callerLine. Expected=[$expected] Actual=[$actual]"
}

function Invoke-TestPwshScript {
    param(
        [Parameter(Mandatory)] [string] $ScriptPath,
        [Parameter(Mandatory)] [string] $LogRoot,
        [string[]] $Arguments = @(),
        [string] $WorkingDirectory = (Get-Location).Path,
        [int] $TimeoutSeconds = 600,
        [string] $Name = 'pwsh-script'
    )

    $safeName = [regex]::Replace($Name, '[^A-Za-z0-9_.-]+', '-').Trim('-')
    $logDirectory = Join-Path $LogRoot "command-logs/$safeName-$([Guid]::NewGuid().ToString('N'))"
    Invoke-NativeCommandWithTimeout `
        -Command 'pwsh' `
        -Arguments (@('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $ScriptPath) + $Arguments) `
        -WorkingDirectory $WorkingDirectory `
        -TimeoutSeconds $TimeoutSeconds `
        -Name $Name `
        -LogDirectory $logDirectory
}

Assert-True (-not $collectorSource.Contains('TestOnly')) 'Production collector must expose no test-only authority replacement parameter.'
Assert-True (-not $baselineGeneratorSource.Contains('TestOnly')) 'Production baseline generator must expose no test-only authority replacement parameter.'
Assert-True ($collectorSource.Contains('deterministic .failure[-N] sibling')) 'Collector governance Writes must declare its owned failure sibling output.'
Assert-True (-not (Get-Command Write-NervTestEvidenceArtifacts).Parameters.ContainsKey('SourceTrxPaths')) 'Artifact writer must not require an unread raw-TRX path parameter.'

Assert-True (Test-NervTestEvidenceLaneName 'backend') 'backend must be valid.'
Assert-True (Test-NervTestEvidenceLaneName 'backend-shard-1') 'backend-shard-1 must use schema v1.'
Assert-True (-not (Test-NervTestEvidenceLaneName 'backend/shard/1')) 'slash lane must be rejected.'

$policy = Import-NervTestEvidencePolicy -Path (Join-Path $fixtures 'policy-valid.json')
Assert-Equal 1 $policy.schemaVersion 'Policy schema version must be one.'

$illegal = Import-NervTestEvidencePolicy -Path (Join-Path $fixtures 'policy-illegal-quarantine.json')
$violations = Test-NervTestEvidencePolicy -Policy $illegal -RepoRoot $repoRoot -AsOfUtc ([DateTimeOffset]'2026-08-03T16:00:00Z')
# The fixture's rule is both illegally quarantined and not closed over a real source skip, so both
# codes are expected; the set is asserted exactly so a third code could never slip in unnoticed.
Assert-ViolationSet $violations @('illegal-quarantine', 'unregistered-skip')
Assert-Equal 'Quarantine requires issue, valid unexpired ISO date, and exit condition.' ($violations | Where-Object code -eq 'illegal-quarantine' | Select-Object -First 1).message 'Policy validation must retain its illegal-quarantine detail.'

$quarantineBoundaryRule = [pscustomobject]@{
    responsibilityIssue = 'MAN-TEST'
    expiresOn = '2026-08-04'
    exitCondition = 'Remove quarantine after the tracked defect is fixed.'
}
Assert-True (Test-NervQuarantineRuleMetadata -Rule $quarantineBoundaryRule -AsOfUtc ([DateTimeOffset]'2026-08-04T23:59:59Z')) 'Quarantine expiry must remain valid through its ISO expiry date.'
Assert-True (-not (Test-NervQuarantineRuleMetadata -Rule $quarantineBoundaryRule -AsOfUtc ([DateTimeOffset]'2026-08-05T00:00:00Z'))) 'Quarantine expiry must become invalid on the following UTC date.'
$quarantineWithoutIssue = $quarantineBoundaryRule.PSObject.Copy()
$quarantineWithoutIssue.responsibilityIssue = ''
Assert-True (-not (Test-NervQuarantineRuleMetadata -Rule $quarantineWithoutIssue -AsOfUtc ([DateTimeOffset]'2026-08-04T00:00:00Z'))) 'Quarantine metadata must require a responsibility issue.'

$liveAssignments = Get-NervSourceSkipAssignments -RepoRoot $repoRoot
Assert-Equal 40 $liveAssignments.Count 'The approved initial source skip inventory changed; classify the diff explicitly.'
Assert-True (($liveAssignments | Where-Object sourcePath -like '*SimulatedConnectorHostProcessTests.cs').sourceText.Contains('Windows runs the platform-specific executable resolution contract only')) 'Quote-aware scanner must retain semicolons inside a C# string literal.'
$livePolicy = Import-NervTestEvidencePolicy -Path (Join-Path $repoRoot 'scripts/test-evidence-policy.json')
$liveViolations = Test-NervTestEvidencePolicy -Policy $livePolicy -RepoRoot $repoRoot -AsOfUtc ([DateTimeOffset]::UtcNow)
Assert-Equal 0 @($liveViolations).Count 'The committed live skip policy must be valid.'
$brokenClosure = ($livePolicy | ConvertTo-Json -Depth 100 | ConvertFrom-Json -Depth 100)
$brokenClosure.rules[0].sourceId = 'missing-source'
Assert-ViolationSet (Test-NervTestEvidencePolicy -Policy $brokenClosure -RepoRoot $repoRoot -AsOfUtc ([DateTimeOffset]::UtcNow)) 'unregistered-skip'
$brokenCount = ($livePolicy | ConvertTo-Json -Depth 100 | ConvertFrom-Json -Depth 100)
$brokenCount.rules[0].expectedRuntimeTestCount++
Assert-ViolationSet (Test-NervTestEvidencePolicy -Policy $brokenCount -RepoRoot $repoRoot -AsOfUtc ([DateTimeOffset]::UtcNow)) 'unregistered-skip'

# Identity padding ruling (#1509). Every consumer of a frozen identity compares it as written —
# ordinal equality or ordinal prefix, never trimmed (see Get-BackendTestShardPolicyIdentityMatches).
# That is only safe if a padded identity cannot be authored in the first place, so the schema
# rejects it here. Trailing padding is the case that used to pass silently: an anchored testPattern
# ending in `.+$` consumes a trailing space, so the pattern check saw nothing wrong, while the shard
# exclusion gate would then never match the row. Leading padding is asserted too, but it fails the
# anchored pattern as well, so only its message is pinned rather than the exact violation set.
$paddedIdentityPolicy = ($livePolicy | ConvertTo-Json -Depth 100 | ConvertFrom-Json -Depth 100)
$paddedIdentityPolicy.rules[0].testIdentities[0] = "$([string] $livePolicy.rules[0].testIdentities[0]) "
$paddedIdentityViolations = @(Test-NervTestEvidencePolicy -Policy $paddedIdentityPolicy -RepoRoot $repoRoot -AsOfUtc ([DateTimeOffset]::UtcNow))
# The named-message assertion comes first on purpose: it is the discriminating one, and asserting it
# before the set keeps the failure readable when the padding rule is removed (an empty violation list
# would otherwise fail inside Assert-ViolationSet as a null-input error).
Assert-True (@($paddedIdentityViolations | Where-Object { [string]$_.message -clike '*must not carry leading or trailing whitespace*' }).Count -eq 1) 'A trailing-padded frozen identity must be rejected by the policy schema, not silently accepted by the anchored testPattern.'
Assert-ViolationSet $paddedIdentityViolations 'unregistered-skip'
$leadingPaddedPolicy = ($livePolicy | ConvertTo-Json -Depth 100 | ConvertFrom-Json -Depth 100)
$leadingPaddedPolicy.rules[0].testIdentities[0] = " $([string] $livePolicy.rules[0].testIdentities[0])"
Assert-True (@((Test-NervTestEvidencePolicy -Policy $leadingPaddedPolicy -RepoRoot $repoRoot -AsOfUtc ([DateTimeOffset]::UtcNow)) | Where-Object { [string]$_.message -clike '*must not carry leading or trailing whitespace*' }).Count -eq 1) 'A leading-padded frozen identity must be rejected for the padding itself, not only as a pattern mismatch.'

# Ordinal identifier comparison in the evidence library (#1509). Every probe below uses a U+00AD soft
# hyphen, which PowerShell's `-ceq`/`-cne`/`-ccontains` and `Sort-Object -Unique` all fold — the `c`
# prefix only turns off case-insensitivity, the comparison itself stays culture-aware. Measured, not
# assumed: on this runtime `@($a) -ccontains "$a$softHyphen"` is True and an ordinal HashSet keeps two
# entries. Each probe therefore passes under the culture-aware spelling and fails under it once the
# implementation is ordinal, which is what makes them regressions rather than restatements.
$softHyphen = [string][char]0x00AD

# 1. Identity-set uniqueness. Two frozen identities differing only by an ignorable character are two
#    identities; folding them makes uniqueCount < count and the rule is rejected for the wrong
#    reason ("not unique") while the real defect is a testPattern mismatch.
$ordinalIdentityPolicy = ($livePolicy | ConvertTo-Json -Depth 100 | ConvertFrom-Json -Depth 100)
$ordinalIdentityPolicy.rules[0].testIdentities = @(
    @($livePolicy.rules[0].testIdentities | ForEach-Object { [string]$_ }) +
    "$([string] $livePolicy.rules[0].testIdentities[0])$softHyphen")
$ordinalIdentityPolicy.rules[0].expectedRuntimeTestCount = @($ordinalIdentityPolicy.rules[0].testIdentities).Count
Assert-True (@((Test-NervTestEvidencePolicy -Policy $ordinalIdentityPolicy -RepoRoot $repoRoot -AsOfUtc ([DateTimeOffset]::UtcNow)) |
    Where-Object { [string]$_.message -clike '*unique test identity set*' }).Count -eq 0) 'Two frozen identities differing only by an ignorable character must count as two; identity uniqueness must be ordinal.'

# 2. Rule applicability. allowedLanes / requiredLane / allowedOperatingSystems are the applicability
#    condition the zero-execution hard gate is built on, so a fold here silently changes which rule
#    governs a lane. Positive controls included so the probes cannot pass by rejecting everything.
$allowedLaneRule = [pscustomobject]@{ allowedLanes = @('postgres'); requiredLane = $null; allowedOperatingSystems = @() }
Assert-True (Test-NervRuleApplies -Rule $allowedLaneRule -SelectedLanes @('postgres') -RunnerOs 'Linux') 'A rule must apply to the lane it allows.'
Assert-True (-not (Test-NervRuleApplies -Rule $allowedLaneRule -SelectedLanes @("postgres$softHyphen") -RunnerOs 'Linux')) 'A lane differing from an allowed lane by an ignorable character is a different lane; allowedLanes membership must be ordinal.'
$requiredLaneRule = [pscustomobject]@{ allowedLanes = @(); requiredLane = 'postgres'; allowedOperatingSystems = @() }
Assert-True (-not (Test-NervRuleApplies -Rule $requiredLaneRule -SelectedLanes @('postgres') -RunnerOs 'Linux')) 'A rule must stop applying once its required lane is actually selected.'
Assert-True (Test-NervRuleApplies -Rule $requiredLaneRule -SelectedLanes @("postgres$softHyphen") -RunnerOs 'Linux') 'A lane differing from the required lane by an ignorable character must not count as having selected it; requiredLane membership must be ordinal.'
$runnerOsRule = [pscustomobject]@{ allowedLanes = @(); requiredLane = $null; allowedOperatingSystems = @('Linux') }
Assert-True (Test-NervRuleApplies -Rule $runnerOsRule -SelectedLanes @('backend') -RunnerOs 'Linux') 'A rule must apply on the operating system it allows.'
Assert-True (-not (Test-NervRuleApplies -Rule $runnerOsRule -SelectedLanes @('backend') -RunnerOs "Linux$softHyphen")) 'allowedOperatingSystems membership must be ordinal.'

# 3. Frozen-identity membership at runtime — the comparison the padding ruling above depends on. The
#    fixture policy anchors testPattern exactly, so it cannot separate the two axes; this policy uses
#    a `.+` tail so the padded name still matches the pattern and only the identity membership
#    decides. Under `-ccontains` the skip claims a rule that froze a different string.
$identityMembershipPolicy = [pscustomobject]@{
    schemaVersion = 1
    lanes = @([pscustomobject]@{ namePattern = '^backend(?:-shard-[1-9][0-9]*)?$'; realDependency = $false })
    sources = @()
    rules = @([pscustomobject]@{
        id = 'ordinal-identity'
        sourceId = 'fixture-source'
        classification = 'optional'
        testPattern = '^Fixture\.Ordinal\..+$'
        reasonPattern = '^Opt in\.$'
        allowedLanes = @('backend')
        requiredLane = $null
        allowedOperatingSystems = @()
        testIdentities = @('Fixture.Ordinal.Alpha')
        expectedRuntimeTestCount = 1
    })
}
$exactIdentityRecord = @([pscustomobject]@{ lane = 'backend'; outcome = 'skipped'; testName = 'Fixture.Ordinal.Alpha'; skipReason = 'Opt in.' })
# Counted rather than passed through Assert-ViolationSet: an empty result is the expectation here,
# and that helper cannot take a null pipeline input.
Assert-Equal 0 @(Get-NervTestEvidenceViolations -Records $exactIdentityRecord -Policy $identityMembershipPolicy -SelectedLanes @('backend') -RunnerOs 'Linux').Count 'A skip whose name is exactly a frozen identity must match its rule; the ordinal probe below must not pass by rejecting everything.'
$foldedIdentityRecord = @([pscustomobject]@{ lane = 'backend'; outcome = 'skipped'; testName = "Fixture.Ordinal.Alpha$softHyphen"; skipReason = 'Opt in.' })
Assert-ViolationSet (Get-NervTestEvidenceViolations -Records $foldedIdentityRecord -Policy $identityMembershipPolicy -SelectedLanes @('backend') -RunnerOs 'Linux') 'unregistered-skip'

$run = @{
    workflowRunId = '1001'
    runAttempt = 2
    headSha = '0123456789abcdef0123456789abcdef01234567'
    testedSha = '89abcdef0123456789abcdef0123456789abcdef'
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
Assert-Equal $run.headSha $records[0].headSha 'Branch-head provenance must be retained separately.'
Assert-Equal $run.testedSha $records[0].testedSha 'Actually-tested checkout provenance must be retained separately.'
Assert-True (-not ($records[0].PSObject.Properties.Name -contains 'commitSha')) 'Ambiguous commitSha must not remain in the retained record schema.'

$counterMismatchFailed = $false
try { Read-NervTrxResults -Path @((Join-Path $fixtures 'counter-mismatch.trx')) -RunMetadata $run | Out-Null }
catch { $counterMismatchFailed = $_.Exception.Message.Contains('Counters') }
Assert-True $counterMismatchFailed 'TRX counter/result mismatches must fail closed.'

# Normalized-TRX provenance is an identifier comparison too (#1509). A rewritten TRX carries the head
# and tested SHAs it was produced under, and this guard is what stops it from being re-read under
# someone else's run metadata. `-cne` folds an ignorable character into the SHA, so a mismatched
# artifact would pass; ordinal equality is what makes the guard fail closed.
$provenanceDirectory = Join-Path ([IO.Path]::GetTempPath()) ("nerv-iip-man-661-provenance-{0}" -f [Guid]::NewGuid().ToString('N'))
try {
    New-Item -ItemType Directory -Path $provenanceDirectory -Force | Out-Null
    $trxTemplate = Get-Content (Join-Path $fixtures 'backend-results.trx') -Raw
    $matchingProvenanceTrx = Join-Path $provenanceDirectory 'matching.trx'
    Set-Content -LiteralPath $matchingProvenanceTrx -NoNewline -Value ($trxTemplate -replace '<TestRun id="', "<TestRun headSha=`"$($run.headSha)`" testedSha=`"$($run.testedSha)`" id=`"")
    Assert-Equal 3 (Read-NervTrxResults -Path @($matchingProvenanceTrx) -RunMetadata $run.Clone()).Count 'A normalized TRX whose persisted provenance matches the run must be readable.'

    $foldedProvenanceTrx = Join-Path $provenanceDirectory 'folded.trx'
    Set-Content -LiteralPath $foldedProvenanceTrx -NoNewline -Value ($trxTemplate -replace '<TestRun id="', "<TestRun headSha=`"$($run.headSha)$softHyphen`" testedSha=`"$($run.testedSha)`" id=`"")
    $foldedProvenanceRejected = $false
    try { Read-NervTrxResults -Path @($foldedProvenanceTrx) -RunMetadata $run.Clone() | Out-Null }
    catch { $foldedProvenanceRejected = $_.Exception.Message.Contains('Normalized TRX provenance does not match') }
    Assert-True $foldedProvenanceRejected 'A persisted head SHA differing by an ignorable character must not be accepted as this run''s provenance; the comparison must be ordinal.'
}
finally {
    if (Test-Path -LiteralPath $provenanceDirectory) { Remove-Item -LiteralPath $provenanceDirectory -Recurse -Force }
}

$parameterized = Read-NervTrxResults -Path @((Join-Path $fixtures 'parameterized-results.trx')) -RunMetadata $run
Assert-Equal 2 @($parameterized.displayName | Sort-Object -Unique).Count 'Parameterized display names must remain distinct.'
Assert-Equal 2 @($parameterized.testInstanceId | Sort-Object -Unique).Count 'Parameterized instances need stable distinct identities.'
Assert-Equal 1 @($parameterized.definitionId | Sort-Object -Unique).Count 'Parameterized instances must share their method definition.'
Assert-Equal '66666666-6666-6666-6666-666666666611' $parameterized[0].testInstanceId 'Persisted execution identity must survive case-sensitive parameter displays.'

$displayPayload = Read-NervTrxResults -Path @((Join-Path $fixtures 'display-payload-results.trx')) -RunMetadata $run
Assert-Equal 3 $displayPayload.Count 'Display-payload fixture count mismatch.'
Assert-Equal 3 @($displayPayload.testInstanceId | Sort-Object -Unique).Count 'Persisted execution IDs must preserve parameterized instance identity.'
Assert-Equal '77777777-7777-7777-7777-777777777701' $displayPayload[0].testInstanceId 'TRX executionId must be preferred over a derived identity.'
Assert-Equal 1000 $displayPayload[0].durationTicks 'Duration ticks must retain TRX precision exactly.'
Assert-Equal 0.1 $displayPayload[0].durationMilliseconds 'Duration milliseconds must be derived reversibly from ticks.'
Assert-Equal 1 @($displayPayload.testName | Sort-Object -Unique).Count 'Display redaction must not alter exact policy-matching test identity.'
Assert-True ($displayPayload[0].displayName.Contains('enveloped: True')) 'Safe display parameters must remain visible.'
Assert-True ($displayPayload[2].displayName.Contains('mode: "POSTGRESQL"')) 'Safe case-sensitive display parameters must survive redaction.'
Assert-Equal 3 @($displayPayload.displayName | Sort-Object -Unique).Count 'Body redaction digests must preserve instance distinguishability.'
foreach ($record in $displayPayload) {
    Assert-True ($record.displayName -match '(?i)(?:body|requestBody|responseBody):\s*"<redacted-body:[0-9a-f]{16}>"') 'Body-valued display parameters must use the non-reversible marker.'
    foreach ($sentinel in @('org-secret-A','org-secret-B','inner-secret','still-secret','third-secret')) {
        Assert-True (-not $record.displayName.Contains($sentinel)) "Sanitized display name leaked '$sentinel'."
    }
}
Assert-Equal 4 (($displayPayload | Measure-Object redactionCount -Sum).Sum) 'Every body-valued display parameter must be counted as a privacy redaction.'
$boundaryDisplay = ConvertTo-NervRetainedDisplayName 'sends(somebody: "safe-value", mode: True)'
Assert-Equal 'sends(somebody: "safe-value", mode: True)' $boundaryDisplay.text 'Body redaction must not match a longer parameter name.'
Assert-Equal 0 $boundaryDisplay.redactionCount 'Safe parameter names must not increment redaction count.'
$nestedDisplay = ConvertTo-NervRetainedDisplayName 'sends(body: {"values":[1,{"text":","}]}, responseBODY: plain-secret, mode: True)'
Assert-Equal 2 $nestedDisplay.redactionCount 'Nested and multiple unquoted body parameters must both be redacted.'
Assert-True ($nestedDisplay.text.Contains('mode: True')) 'Multiple body redaction must preserve trailing safe parameters.'
Assert-True (-not $nestedDisplay.text.Contains('plain-secret')) 'Unquoted response body value must not survive.'
$sensitiveBodyA = ConvertTo-NervRetainedDisplayName 'sends(body: "{\"customerName\":\"Alice\",\"password\":\"first\"}")'
$sensitiveBodyB = ConvertTo-NervRetainedDisplayName 'sends(body: "{\"customerName\":\"Bob\",\"password\":\"second\"}")'
Assert-True ($sensitiveBodyA.text -cne $sensitiveBodyB.text) 'Body digests must preserve instance distinction even when generic text redaction would collapse the raw values.'

$classifiedViolations = Get-NervTestEvidenceViolations -Records $records -Policy $policy -SelectedLanes @('backend') -RunnerOs 'Linux'
Assert-Equal 0 @($classifiedViolations).Count 'Registered fixture skip must match exactly one rule.'
Assert-Equal 'environment-gated' ($records | Where-Object outcome -eq 'skipped').skipClassification 'Matched skip classification must be retained for aggregation.'
Assert-Equal 'postgres-gated' ($records | Where-Object outcome -eq 'skipped').skipPolicyId 'Matched skip policy entry must be retained for aggregation.'
$summaryRun = $run.Clone()
$summaryRun.selectedLanes = @('backend')
$classifiedSummary = New-NervTestEvidenceSummary -Records $records -RunMetadata $summaryRun -Violations @() -Baseline $null -PriorAttemptOutcome $null -TopCount 5
Assert-Equal 1 @($classifiedSummary.selectedLanes).Count 'Summary must retain the selected lane selectors.'
Assert-Equal 'backend' $classifiedSummary.selectedLanes[0] 'Summary selected lane selector mismatch.'
Assert-Equal 1 @($classifiedSummary.selectedLaneResults).Count 'Summary must emit one logical selected-lane result.'
Assert-Equal 'backend' $classifiedSummary.selectedLaneResults[0].baseLane 'Selected-lane result must identify the logical base lane.'
Assert-Equal 'backend-shard-1' $classifiedSummary.selectedLaneResults[0].observedLanes[0] 'Selected-lane result must identify the observed physical lane.'
Assert-Equal 2 $classifiedSummary.selectedLaneResults[0].executed 'Selected-lane result executed count mismatch.'
Assert-Equal 'pass' $classifiedSummary.selectedLaneResults[0].gateResult 'Selected-lane result gate status mismatch.'
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
Assert-ViolationSet $violations 'unregistered-skip'
$futureSharedFact = @([pscustomobject]@{ lane = 'backend'; outcome = 'skipped'; testName = 'Fixture.Postgres.New_ninth_method'; skipReason = 'Set NERV_IIP_TEST_POSTGRES to run the fixture.' })
Assert-ViolationSet (Get-NervTestEvidenceViolations -Records $futureSharedFact -Policy $policy -SelectedLanes @('backend') -RunnerOs 'Linux') 'unregistered-skip'

$postgresSelected = Get-NervTestEvidenceViolations -Records $records -Policy $policy -SelectedLanes @('postgres') -RunnerOs 'Linux'
# Backend records under a postgres selection: the skip is unregistered for that selection *and* the
# selected real-dependency lane executed nothing. Both are expected, and nothing else is.
Assert-ViolationSet $postgresSelected @('unregistered-skip', 'zero-execution')

$postgresRun = $run.Clone()
$postgresRun.lane = 'postgres'
$allSkipped = Read-NervTrxResults -Path @((Join-Path $fixtures 'postgres-all-skipped.trx')) -RunMetadata $postgresRun
$violations = Get-NervTestEvidenceViolations -Records $allSkipped -Policy $livePolicy -SelectedLanes @('postgres') -RunnerOs 'Linux'
# Every postgres test skipped: the lane executed nothing *and* the fixture skip reason is not in the
# committed live policy. Both are expected here; only the empty-result fixture below is single-code.
Assert-ViolationSet $violations @('unregistered-skip', 'zero-execution')

$empty = Read-NervTrxResults -Path @((Join-Path $fixtures 'postgres-zero-results.trx')) -RunMetadata $postgresRun
$violations = Get-NervTestEvidenceViolations -Records $empty -Policy $livePolicy -SelectedLanes @('postgres') -RunnerOs 'Linux'
Assert-ViolationSet $violations 'zero-execution'

$backendEmptyViolations = Get-NervTestEvidenceViolations -Records @() -Policy $livePolicy -SelectedLanes @('backend-shard-1') -RunnerOs 'Linux'
Assert-True (-not (@($backendEmptyViolations | ForEach-Object code) -contains 'zero-execution')) 'Ordinary backend shard zero execution is outside the MAN-661 real-dependency gate.'
$currentShard = @([pscustomobject]@{ lane = 'postgres-shard-1'; outcome = 'passed'; testName = 'Fixture.Test'; skipReason = $null })
$baseSelectedShardViolations = Get-NervTestEvidenceViolations -Records $currentShard -Policy $livePolicy -SelectedLanes @('postgres') -RunnerOs 'Linux'
Assert-True (-not (@($baseSelectedShardViolations | ForEach-Object code) -contains 'zero-execution')) 'A logical base-lane selection must recognize execution from its current physical shard.'
$siblingSelectedShardViolations = Get-NervTestEvidenceViolations -Records $currentShard -Policy $livePolicy -SelectedLanes @('postgres-shard-1', 'postgres-shard-2') -RunnerOs 'Linux'
Assert-True (-not (@($siblingSelectedShardViolations | ForEach-Object code) -contains 'zero-execution')) 'A single-lane collector must not report an unobserved selected sibling as zero execution.'
$emptyShardViolations = Get-NervTestEvidenceViolations -Records @() -Policy $livePolicy -SelectedLanes @('postgres-shard-1') -RunnerOs 'Linux'
Assert-ViolationSet $emptyShardViolations 'zero-execution'
$otherShard = @([pscustomobject]@{ lane = 'postgres-shard-2'; outcome = 'passed'; testName = 'Fixture.Test'; skipReason = $null })
$shardViolations = Get-NervTestEvidenceViolations -Records $otherShard -Policy $livePolicy -SelectedLanes @('postgres-shard-1') -RunnerOs 'Linux'
Assert-ViolationSet $shardViolations 'zero-execution'

# Lane names are selectors, so the zero-execution gate's own lane matching must be ordinal too
# (#1509). This is the same shape as the two probes above, with one ignorable character inserted: a
# record from `postgres<U+00AD>-shard-1` is not execution of `postgres-shard-1`, and a culture-aware
# `-ceq` would report the lane as covered and suppress the violation.
$foldedLaneShard = @([pscustomobject]@{ lane = "postgres$softHyphen-shard-1"; outcome = 'passed'; testName = 'Fixture.Test'; skipReason = $null })
Assert-ViolationSet (Get-NervTestEvidenceViolations -Records $foldedLaneShard -Policy $livePolicy -SelectedLanes @('postgres-shard-1') -RunnerOs 'Linux') 'zero-execution'
Assert-ViolationSet (Get-NervTestEvidenceViolations -Records $foldedLaneShard -Policy $livePolicy -SelectedLanes @('postgres') -RunnerOs 'Linux') 'zero-execution'

# …and so must the summary's own lane/selector merge, which is a second implementation of the same
# decision: it groups records into a base lane and decides which violations belong to that group.
$foldedSummaryRun = @{
    workflowRunId = '1001'
    runAttempt = 1
    headSha = '0123456789abcdef0123456789abcdef01234567'
    testedSha = '0123456789abcdef0123456789abcdef01234567'
    lane = 'postgres-shard-1'
    selectedLanes = @('postgres-shard-1')
}
# Cloned from a real parsed record so the summary sees the full retained schema; only the lane
# differs between the probes.
$passedRecordTemplate = @($records | Where-Object outcome -eq 'passed')[0]
$executedShardRecordSource = $passedRecordTemplate.PSObject.Copy()
$executedShardRecordSource.lane = 'postgres-shard-1'
$executedShardRecord = @($executedShardRecordSource)
$foldedViolations = @(
    New-NervTestEvidenceViolation 'zero-execution' "postgres$softHyphen-shard-1" 'Folded selector must not be attributed to this lane.'
)
$foldedSelectorSummary = New-NervTestEvidenceSummary -Records $executedShardRecord -RunMetadata $foldedSummaryRun -Violations $foldedViolations -Baseline $null -PriorAttemptOutcome $null -TopCount 5
Assert-Equal 'pass' $foldedSelectorSummary.selectedLaneResults[0].gateResult 'A violation whose id differs from the selector by an ignorable character belongs to a different selector; the summary selector set must be ordinal.'
$foldedCodeViolations = @(
    New-NervTestEvidenceViolation "zero-execution$softHyphen" 'postgres-shard-1' 'Folded violation code must not be read as zero-execution.'
)
$foldedCodeSummary = New-NervTestEvidenceSummary -Records $executedShardRecord -RunMetadata $foldedSummaryRun -Violations $foldedCodeViolations -Baseline $null -PriorAttemptOutcome $null -TopCount 5
Assert-Equal 'pass' $foldedCodeSummary.selectedLaneResults[0].gateResult 'A violation code differing by an ignorable character is a different code; the summary code comparison must be ordinal.'
$realZeroExecutionSummary = New-NervTestEvidenceSummary -Records $executedShardRecord -RunMetadata $foldedSummaryRun -Violations @(New-NervTestEvidenceViolation 'zero-execution' 'postgres-shard-1' 'Real violation.') -Baseline $null -PriorAttemptOutcome $null -TopCount 5
Assert-Equal 'zero-execution' $realZeroExecutionSummary.selectedLaneResults[0].gateResult 'The exact selector and code must still be attributed to the lane; ordinal comparison must not turn the summary into "nothing ever matches".'
Assert-Equal 1 $foldedSelectorSummary.selectedLaneResults[0].executed 'The exact lane must still be counted as executed; ordinal grouping must not empty the lane.'
$foldedLaneRecordSource = $passedRecordTemplate.PSObject.Copy()
$foldedLaneRecordSource.lane = "postgres$softHyphen-shard-1"
$foldedRecordLaneSummary = New-NervTestEvidenceSummary -Records @($foldedLaneRecordSource) -RunMetadata $foldedSummaryRun -Violations @() -Baseline $null -PriorAttemptOutcome $null -TopCount 5
Assert-Equal 0 $foldedRecordLaneSummary.selectedLaneResults[0].executed 'A record from a lane differing by an ignorable character must not be counted as this lane executing; the summary lane grouping must be ordinal.'

# The `invalid-selection` half of the same gateResult decision (#1509 round 2). The probes above only
# exercise the `zero-execution` branch, so the `unregistered-skip` branch had no control at all and
# reverting its comparison to `-ceq` left the suite green. Three cases, because the branch makes two
# ordinal decisions (code and selector id) and a positive control is needed so "always pass" cannot
# satisfy the other two.
$invalidSelectionSummary = New-NervTestEvidenceSummary -Records $executedShardRecord -RunMetadata $foldedSummaryRun -Violations @(
    New-NervTestEvidenceViolation 'unregistered-skip' 'postgres-shard-1' 'Real invalid selection.'
) -Baseline $null -PriorAttemptOutcome $null -TopCount 5
Assert-Equal 'invalid-selection' $invalidSelectionSummary.selectedLaneResults[0].gateResult 'An unregistered-skip violation carrying this lane selector must be reported as invalid-selection.'
$foldedInvalidCodeSummary = New-NervTestEvidenceSummary -Records $executedShardRecord -RunMetadata $foldedSummaryRun -Violations @(
    New-NervTestEvidenceViolation "unregistered-skip$softHyphen" 'postgres-shard-1' 'Folded code must not be read as unregistered-skip.'
) -Baseline $null -PriorAttemptOutcome $null -TopCount 5
Assert-Equal 'pass' $foldedInvalidCodeSummary.selectedLaneResults[0].gateResult 'A violation code differing by an ignorable character is a different code; the invalid-selection comparison must be ordinal.'
$foldedInvalidSelectorSummary = New-NervTestEvidenceSummary -Records $executedShardRecord -RunMetadata $foldedSummaryRun -Violations @(
    New-NervTestEvidenceViolation 'unregistered-skip' "postgres$softHyphen-shard-1" 'Folded selector must not be attributed to this lane.'
) -Baseline $null -PriorAttemptOutcome $null -TopCount 5
Assert-Equal 'pass' $foldedInvalidSelectorSummary.selectedLaneResults[0].gateResult 'An unregistered-skip id differing from the selector by an ignorable character belongs to a different selector.'

# The selected-lane dedup that feeds the lane-contract loop (#1509 round 2). `Sort-Object -Unique`
# folded the two spellings into one, so only one of them was ever matched against the lane patterns
# and the other silently stopped being checked — an unregistered lane that reports nothing.
$foldedSelectedLaneViolations = @(Get-NervTestEvidenceViolations -Records @() -Policy $livePolicy -SelectedLanes @('postgres', "postgres$softHyphen") -RunnerOs 'Linux')
Assert-True (@($foldedSelectedLaneViolations | Where-Object { (Test-NervOrdinalEquals ([string]$_.code) 'unregistered-skip') -and (Test-NervOrdinalEquals ([string]$_.id) "postgres$softHyphen") }).Count -eq 1) 'A selected lane differing from a real lane by an ignorable character is its own lane and must be reported as matching no lane contract; the selected-lane dedup must be ordinal.'
Assert-Equal 0 @(Get-NervTestEvidenceViolations -Records @() -Policy $livePolicy -SelectedLanes @('backend', 'backend') -RunnerOs 'Linux' | Where-Object { Test-NervOrdinalEquals ([string]$_.code) 'unregistered-skip' }).Count 'Ordinal dedup must still collapse two identical lane spellings; the probe above must not pass by refusing to deduplicate at all.'

# The policy schema's own source/rule cross-reference (#1509 round 2): `sourceId` is the first segment
# of the frozen evidence key `sourceId|ruleId|identity`, and it was compared with `-ceq`, so a rule
# pointing at `<source><U+00AD>` resolved to `<source>` and the mis-registration was invisible.
$foldedSourceIdPolicy = ($livePolicy | ConvertTo-Json -Depth 100 | ConvertFrom-Json -Depth 100)
$foldedSourceIdPolicy.rules[0].sourceId = "$([string]$foldedSourceIdPolicy.rules[0].sourceId)$softHyphen"
$foldedSourceIdViolations = @(Test-NervTestEvidencePolicy -Policy $foldedSourceIdPolicy -RepoRoot $repoRoot -AsOfUtc ([DateTimeOffset]::UtcNow))
Assert-True (@($foldedSourceIdViolations | Where-Object { [string]$_.message -clike 'Rule sourceId must resolve*' }).Count -eq 1) 'A rule whose sourceId differs from a registered source by an ignorable character resolves to no source; the sourceId comparison must be ordinal.'
Assert-True (@($foldedSourceIdViolations | Where-Object { [string]$_.message -clike 'Registered source is not referenced*' }).Count -eq 1) 'The reverse cross-reference must fail for the same reason; the source-to-rule comparison must be ordinal too.'

$expired = Import-NervTestEvidencePolicy -Path (Join-Path $fixtures 'policy-expired-quarantine.json')
$expiredViolations = Test-NervTestEvidencePolicy -Policy $expired -RepoRoot $repoRoot -AsOfUtc ([DateTimeOffset]'2026-08-03T16:00:00Z')
# Same shape as the illegal-quarantine fixture: the expired rule is also not closed over a source
# skip, so both codes are expected and pinned exactly.
Assert-ViolationSet $expiredViolations @('illegal-quarantine', 'unregistered-skip')
$runtimeExpiredViolations = Get-NervTestEvidenceViolations -Records @() -Policy $expired -SelectedLanes @('backend') -RunnerOs 'Linux'
Assert-ViolationSet $runtimeExpiredViolations 'illegal-quarantine'
Assert-Equal 'Quarantine metadata is missing, invalid, or expired.' ($runtimeExpiredViolations | Where-Object code -eq 'illegal-quarantine' | Select-Object -First 1).message 'Runtime validation must retain its illegal-quarantine detail.'
$runtimeBoundaryPolicy = ($expired | ConvertTo-Json -Depth 100 | ConvertFrom-Json -Depth 100)
$runtimeBoundaryPolicy.rules[0].expiresOn = [DateTimeOffset]::UtcNow.AddDays(1).ToString('yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)
$runtimeBoundaryViolations = Get-NervTestEvidenceViolations -Records @() -Policy $runtimeBoundaryPolicy -SelectedLanes @('backend') -RunnerOs 'Linux'
Assert-Equal 0 @($runtimeBoundaryViolations | Where-Object code -eq 'illegal-quarantine').Count 'Runtime validation must accept complete unexpired quarantine metadata.'
$allowedCodes = @('unregistered-skip', 'illegal-quarantine', 'zero-execution')
Assert-Equal 0 @($expiredViolations | Where-Object { $allowedCodes -notcontains $_.code }).Count 'Evidence layer emitted an unapproved hard-gate code.'

$artifactRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-iip-man-661-artifacts-$([Guid]::NewGuid().ToString('N'))"
$parameterArtifactRoot = "$artifactRoot-parameters"
$displayPayloadArtifactRoot = "$artifactRoot-display-payload"
$classifiedArtifactRoot = "$artifactRoot-classified"
try {
    $sensitiveRecords = Read-NervTrxResults -Path @((Join-Path $fixtures 'sensitive-results.trx')) -RunMetadata $run
    $summary = New-NervTestEvidenceSummary -Records $sensitiveRecords -RunMetadata $run -Violations @() -Baseline (Get-Content (Join-Path $fixtures 'baseline-report-only.json') -Raw | ConvertFrom-Json) -PriorAttemptOutcome 'failure' -TopCount 5
    Write-NervTestEvidenceArtifacts -Records $sensitiveRecords -Summary $summary -OutputDirectory $artifactRoot
    foreach ($required in @('tests.jsonl', 'summary.json', 'summary.md', 'diagnostics.log')) {
        Assert-True (Test-Path (Join-Path $artifactRoot $required)) "Missing retained artifact '$required'."
    }
    $retainedRootFiles = @(Get-ChildItem $artifactRoot -File | ForEach-Object Name | Sort-Object)
    Assert-Equal 'diagnostics.log,summary.json,summary.md,tests.jsonl' ($retainedRootFiles -join ',') 'Artifact root allowlist must remain unchanged.'
    Assert-True (-not (Test-Path (Join-Path $artifactRoot 'sensitive-results.trx'))) 'Raw source TRX must not be copied into retained evidence.'
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
    Write-NervTestEvidenceArtifacts -Records $parameterized -Summary $recoverySummary -OutputDirectory $parameterArtifactRoot
    $parameterRoundTripRun = $recoveryRun.Clone()
    $parameterRoundTrip = Read-NervTrxResults -Path @((Get-ChildItem (Join-Path $parameterArtifactRoot 'trx') -Filter '*.trx').FullName) -RunMetadata $parameterRoundTripRun
    Assert-Equal 2 @($parameterRoundTrip.displayName | Sort-Object -Unique).Count 'Parameterized display identity must survive normalized TRX round-trip.'
    Assert-Equal 1 @($parameterRoundTrip.definitionId | Sort-Object -Unique).Count 'Parameterized definition identity must survive round-trip.'
    foreach ($expectedRecord in $parameterized) {
        Assert-Equal 1 @($parameterRoundTrip | Where-Object testInstanceId -ceq $expectedRecord.testInstanceId).Count 'Parameterized persisted execution IDs must survive normalized TRX round-trip.'
    }
    $displaySummary = New-NervTestEvidenceSummary -Records $displayPayload -RunMetadata $run -Violations @() -Baseline $null -PriorAttemptOutcome $null
    Write-NervTestEvidenceArtifacts -Records $displayPayload -Summary $displaySummary -OutputDirectory $displayPayloadArtifactRoot
    $displayRetainedText = [string]::Join("`n", @(Get-ChildItem $displayPayloadArtifactRoot -File -Recurse | ForEach-Object { Get-Content $_.FullName -Raw }))
    foreach ($sentinel in @('org-secret-A','org-secret-B','inner-secret','still-secret','third-secret')) {
        Assert-True (-not $displayRetainedText.Contains($sentinel)) "Retained display evidence leaked '$sentinel'."
    }
    $displayRoundTripRun = $run.Clone()
    $displayRoundTrip = Read-NervTrxResults -Path @((Get-ChildItem (Join-Path $displayPayloadArtifactRoot 'trx') -Filter '*.trx').FullName) -RunMetadata $displayRoundTripRun
    foreach ($expectedRecord in $displayPayload) {
        $actualRecord = @($displayRoundTrip | Where-Object testInstanceId -ceq $expectedRecord.testInstanceId)
        Assert-Equal 1 $actualRecord.Count 'Normalized TRX must preserve persisted execution IDs exactly.'
        Assert-Equal $expectedRecord.durationTicks $actualRecord[0].durationTicks 'Normalized TRX must preserve duration ticks exactly.'
        Assert-Equal $expectedRecord.durationMilliseconds $actualRecord[0].durationMilliseconds 'Normalized TRX must preserve duration milliseconds exactly.'
        Assert-Equal $expectedRecord.displayName $actualRecord[0].displayName 'Normalized TRX must preserve the redacted display name exactly.'
        Assert-Equal $expectedRecord.redactionCount $actualRecord[0].redactionCount 'Normalized TRX must preserve the privacy-redaction count exactly.'
    }
    Assert-True ($summary.baseline.enforcement -eq 'report-only') 'Baseline delta must remain report-only.'
    $summaryMarkdown = Get-Content (Join-Path $artifactRoot 'summary.md') -Raw
    foreach ($heading in @('## Assemblies', '## Slowest assemblies and tests', '## Skip reasons', 'Baseline source:', 'Privacy redactions:', 'Retained artifact:')) { Assert-True $summaryMarkdown.Contains($heading) "Markdown is missing '$heading'." }
    Write-NervTestEvidenceArtifacts -Records $records -Summary $classifiedSummary -OutputDirectory $classifiedArtifactRoot
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
    if (Test-Path $displayPayloadArtifactRoot) { Remove-Item $displayPayloadArtifactRoot -Recurse -Force }
    if (Test-Path $classifiedArtifactRoot) { Remove-Item $classifiedArtifactRoot -Recurse -Force }
}

$metadata = Get-Content (Join-Path $fixtures 'github-run-metadata.json') -Raw | ConvertFrom-Json -AsHashtable
$runnerLogBase = "Image: ubuntu-24.04`nVersion: 20260720.247.2`ndotnet-sdk=10.0.302"
Assert-Equal 'ubuntu24' (ConvertTo-NervResolvedRunnerImage -Image 'ubuntu-24.04') 'Ubuntu runner normalization must retain the resolved major without relying on the automatic Matches variable.'
Assert-Equal 'windows-2025' (ConvertTo-NervResolvedRunnerImage -Image 'windows-2025') 'Non-Ubuntu runner images must remain unchanged.'
$missingTestedShaFailed = $false
try { Get-NervGitHubRunnerProvenance -Text $runnerLogBase | Out-Null } catch { $missingTestedShaFailed = $true }
Assert-True $missingTestedShaFailed 'Runner provenance without independent tested-SHA authority must fail closed.'
$historicalCheckoutLog = "$runnerLogBase`n[command]/usr/bin/git log -1 --format=%H`n9dafb512c992b240222c8d9b5ada43e4bfc8ac3d"
$historicalCheckout = Get-NervGitHubRunnerProvenance -Text $historicalCheckoutLog
Assert-Equal '9dafb512c992b240222c8d9b5ada43e4bfc8ac3d' $historicalCheckout.testedSha 'Historical checkout log authority must resolve the tested SHA.'
Assert-Equal 'ubuntu24@20260720.247.2' $historicalCheckout.runnerImage 'Runner provenance must retain normalized Ubuntu major plus resolved image version.'
$ambiguousCheckoutFailed = $false
try { Get-NervGitHubRunnerProvenance -Text "$historicalCheckoutLog`ntested-sha=1123456789abcdef0123456789abcdef01234567" | Out-Null } catch { $ambiguousCheckoutFailed = $true }
Assert-True $ambiguousCheckoutFailed 'Conflicting checkout authorities must fail closed.'
$pushCheckout = Assert-NervGitHubRunCheckoutProvenance -Run ([pscustomobject]@{ event = 'push'; headSha = '9dafb512c992b240222c8d9b5ada43e4bfc8ac3d' }) -RunnerProvenance $historicalCheckout
Assert-Equal $pushCheckout.headSha $pushCheckout.testedSha 'Push checkout authority must match the run head.'
$pushMismatchFailed = $false
try { Assert-NervGitHubRunCheckoutProvenance -Run ([pscustomobject]@{ event = 'push'; headSha = '0123456789abcdef0123456789abcdef01234567' }) -RunnerProvenance $historicalCheckout | Out-Null } catch { $pushMismatchFailed = $true }
Assert-True $pushMismatchFailed 'Push checkout authority must reject a tested SHA different from run head.'
$prCheckout = Assert-NervGitHubRunCheckoutProvenance -Run ([pscustomobject]@{ event = 'pull_request'; headSha = '0123456789abcdef0123456789abcdef01234567' }) -RunnerProvenance $historicalCheckout
Assert-Equal '0123456789abcdef0123456789abcdef01234567' $prCheckout.headSha 'PR provenance must retain the branch head.'
Assert-Equal '9dafb512c992b240222c8d9b5ada43e4bfc8ac3d' $prCheckout.testedSha 'PR provenance must allow a distinct synthetic merge checkout.'
# Baseline lane provenance must cover exactly the lanes a baseline records, so every fixture that
# records a lane has to bring that lane's row (with its allowlisted job name) along with it.
function New-NervLaneProvenanceFor {
    param([Parameter(Mandatory)] [string[]] $Lanes, [string] $RunnerImage = 'ubuntu24@20260720.247.2', [string] $DotnetSdk = '10.0.302')
    $jobs = Get-NervTestEvidenceLaneJobs
    return @($Lanes | Sort-Object -Unique | ForEach-Object {
        [pscustomobject][ordered]@{
            lane = [string]$_
            jobName = if ($jobs.Contains([string]$_)) { [string]$jobs[[string]$_] } else { 'Backend Tests' }
            runnerOs = 'Linux'
            runnerImage = $RunnerImage
            dotnetSdk = $DotnetSdk
        }
    })
}

$imported = ConvertFrom-NervDotNetConsoleSummary -Text (Get-Content (Join-Path $fixtures 'github-backend-console.log.txt') -Raw) -RunMetadata $metadata
Assert-Equal 'project' $imported.granularity 'Console import is project-granularity.'
Assert-Equal 822000 ($imported.assemblies | Where-Object assembly -eq 'Nerv.IIP.BusinessGateway.Web.Tests.dll').elapsedMilliseconds '13m42s must normalize to milliseconds.'
$baselineA = New-NervTestEvidenceBaseline -Summaries @($imported) -SourceMetadata $metadata -GeneratedAtUtc ([DateTimeOffset]'2026-08-03T14:11:22Z')
$baselineB = New-NervTestEvidenceBaseline -Summaries @($imported) -SourceMetadata $metadata -GeneratedAtUtc ([DateTimeOffset]'2026-08-03T14:11:22Z')
Assert-Equal ($baselineA | ConvertTo-Json -Depth 100) ($baselineB | ConvertTo-Json -Depth 100) 'Baseline generation must be deterministic.'
$selectorMetadata = $metadata.Clone()
$selectorMetadata.laneProvenance = @(@{ lane = 'backend'; jobName = 'Backend Tests'; runnerOs = 'Linux'; runnerImage = 'ubuntu-latest'; dotnetSdk = '10.0.x' })
$selectorRejected = $false
try { New-NervTestEvidenceBaseline -Summaries @($imported) -SourceMetadata $selectorMetadata -GeneratedAtUtc ([DateTimeOffset]'2026-08-03T14:11:22Z') | Out-Null } catch { $selectorRejected = $true }
Assert-True $selectorRejected 'Baseline provenance must reject runner selectors and wildcard SDK versions.'
$missingProvenanceMetadata = $metadata.Clone(); $missingProvenanceMetadata.laneProvenance = @()
$missingProvenanceRejected = $false
try { New-NervTestEvidenceBaseline -Summaries @($imported) -SourceMetadata $missingProvenanceMetadata -GeneratedAtUtc ([DateTimeOffset]'2026-08-03T14:11:22Z') | Out-Null } catch { $missingProvenanceRejected = $true }
Assert-True $missingProvenanceRejected 'A baseline with no per-lane runner environment row must be rejected, not silently unprovenanced.'
$pushMismatchMetadata = $metadata.Clone(); $pushMismatchMetadata.testedSha = '1123456789abcdef0123456789abcdef01234567'
$pushMismatchRejected = $false
try { New-NervTestEvidenceBaseline -Summaries @($imported) -SourceMetadata $pushMismatchMetadata -GeneratedAtUtc ([DateTimeOffset]'2026-08-03T14:11:22Z') | Out-Null } catch { $pushMismatchRejected = $true }
Assert-True $pushMismatchRejected 'Non-PR push provenance must reject different head and tested SHAs.'
$shardedMetadata = $metadata.Clone(); $shardedMetadata.laneProvenance = New-NervLaneProvenanceFor -Lanes @('backend-shard-1', 'backend-shard-2')
$shardedBaseline = New-NervTestEvidenceBaseline -Summaries @(
    [pscustomobject]@{ granularity = 'test'; assemblies = @([pscustomobject]@{ lane = 'backend-shard-1'; assembly = 'Shared.Tests.dll'; passed = 1; failed = 0; skipped = 0; executed = 1; total = 1; elapsedMilliseconds = 10 }) },
    [pscustomobject]@{ granularity = 'test'; assemblies = @([pscustomobject]@{ lane = 'backend-shard-2'; assembly = 'Shared.Tests.dll'; passed = 1; failed = 0; skipped = 0; executed = 1; total = 1; elapsedMilliseconds = 20 }) }
) -SourceMetadata $shardedMetadata -GeneratedAtUtc ([DateTimeOffset]'2026-08-03T14:11:22Z')
# Storage still records one row per lane+assembly, because a lane is real provenance for where a
# measurement was taken. What changed in #1507 is the *reader's* key, asserted further down: it looks
# rows up by assembly alone so a shard rearrangement cannot invalidate them.
Assert-Equal 2 @($shardedBaseline.assemblies).Count 'Baseline storage must keep one row per lane+assembly so provenance is not collapsed.'
$incompatibleBaselineSummary = New-NervTestEvidenceSummary -Records $records -RunMetadata $summaryRun -Violations @() -Baseline ($baselineA | ConvertTo-Json -Depth 100 | ConvertFrom-Json) -PriorAttemptOutcome $null -TopCount 5
Assert-True (-not $incompatibleBaselineSummary.baseline.available) 'Project wall-clock baseline must not be compared with TRX elapsed timing.'
Assert-Equal 'incompatible-granularity-or-duration-metric' $incompatibleBaselineSummary.baseline.unavailableReason 'Incompatible baseline must expose a structured unavailable reason.'
Assert-Equal 'incompatible-granularity-or-duration-metric' $incompatibleBaselineSummary.baseline.assemblies[0].unavailableReason 'Each unavailable assembly comparison must expose its reason.'
$compatibleRun = @{ workflowRunId = '1001'; runAttempt = 1; headSha = '0123456789abcdef0123456789abcdef01234567'; testedSha = '0123456789abcdef0123456789abcdef01234567'; lane = 'backend-shard-1'; selectedLanes = @('backend-shard-1') }
$compatibleRecords = @(Read-NervTrxResults -Path @((Join-Path $fixtures 'backend-results.trx')) -RunMetadata $compatibleRun)
$compatibleMetadata = $metadata.Clone(); $compatibleMetadata.laneProvenance = New-NervLaneProvenanceFor -Lanes @([string]$compatibleRecords[0].lane)
$compatibleBaseline = New-NervTestEvidenceBaseline -Summaries @(
    [pscustomobject]@{ granularity = 'test'; assemblies = @([pscustomobject]@{ lane = [string]$compatibleRecords[0].lane; assembly = [string]$compatibleRecords[0].assembly; passed = 1; failed = 0; skipped = 0; executed = 1; total = 1; elapsedMilliseconds = 6000.0 }) }
) -SourceMetadata $compatibleMetadata -GeneratedAtUtc ([DateTimeOffset]'2026-08-05T11:06:53Z')
Assert-Equal 'test' $compatibleBaseline.granularity 'A TRX-sourced baseline must stay test-granularity.'
Assert-Equal 'trx-elapsed' $compatibleBaseline.durationMetric 'A test-granularity baseline must expose the trx-elapsed metric.'
$compatibleBaselineSummary = New-NervTestEvidenceSummary -Records $compatibleRecords -RunMetadata $compatibleRun -Violations @() -Baseline ($compatibleBaseline | ConvertTo-Json -Depth 100 | ConvertFrom-Json) -PriorAttemptOutcome $null -TopCount 5
Assert-True ([bool]$compatibleBaselineSummary.baseline.available) 'A test-granularity trx-elapsed baseline must produce an available comparison.'
Assert-Equal $null $compatibleBaselineSummary.baseline.unavailableReason 'An available baseline comparison must not carry an unavailable reason.'
Assert-Equal -50 $compatibleBaselineSummary.baseline.assemblies[0].deltaPercent 'Available comparison must report the exact signed delta percent.'

# #1507: the timing comparison key is the assembly alone, so a shard rearrangement cannot lose it.
# This fixture is the exact MAN-669 PR-A shape — the snapshot recorded the assembly under one lane,
# the current run reports it under another. Under the old lane+assembly key the row fell to
# `lane-assembly-not-in-baseline` with a null delta, and the only way to clear it was for a human to
# regenerate and re-commit the snapshot. Re-introducing the lane into the key turns this red.
$rehomedBaseline = ($compatibleBaseline | ConvertTo-Json -Depth 100 | ConvertFrom-Json)
Assert-True (-not [string]::Equals([string]$rehomedBaseline.assemblies[0].lane, 'backend-shard-4', [StringComparison]::Ordinal)) 'The re-homing fixture must actually move the assembly to a different lane.'
$rehomedBaseline.assemblies[0].lane = 'backend-shard-4'
$rehomedSummary = New-NervTestEvidenceSummary -Records $compatibleRecords -RunMetadata $compatibleRun -Violations @() -Baseline $rehomedBaseline -PriorAttemptOutcome $null -TopCount 5
Assert-True ([bool]$rehomedSummary.baseline.available) 'An assembly re-homed to another shard must keep its timing comparison key.'
Assert-Equal $null $rehomedSummary.baseline.assemblies[0].unavailableReason 'A re-homed assembly must not report an unavailable comparison reason.'
Assert-Equal -50 $rehomedSummary.baseline.assemblies[0].deltaPercent 'A re-homed assembly must report the same signed delta as before the move.'

# An assembly the snapshot genuinely never recorded is still report-only unavailable; only the reason
# name changed with the key, and nothing about this path fails a gate.
$absentBaseline = ($compatibleBaseline | ConvertTo-Json -Depth 100 | ConvertFrom-Json)
$absentBaseline.assemblies[0].assembly = 'never.observed.tests.dll'
$absentSummary = New-NervTestEvidenceSummary -Records $compatibleRecords -RunMetadata $compatibleRun -Violations @() -Baseline $absentBaseline -PriorAttemptOutcome $null -TopCount 5
Assert-True (-not [bool]$absentSummary.baseline.available) 'An assembly with no snapshot row must not produce a comparison.'
Assert-Equal 'assembly-not-in-baseline' $absentSummary.baseline.assemblies[0].unavailableReason 'A missing assembly must report the assembly-keyed unavailable reason.'
Assert-Equal 'no-compatible-assembly' $absentSummary.baseline.unavailableReason 'A summary with no comparable assembly must report the assembly-keyed summary reason.'
Assert-Equal 'report-only' $absentSummary.baseline.enforcement 'Missing timing data must stay report-only, never a gate.'

# Two snapshot rows for one assembly are two different measurements, not one. This lane's row wins;
# with a row for neither lane the comparison is ambiguous and stays unavailable rather than guessing.
$currentLane = [string]$compatibleRecords[0].lane
$duplicateRow = ($compatibleBaseline.assemblies[0] | ConvertTo-Json -Depth 100 | ConvertFrom-Json)
$duplicateRow.lane = 'backend-shard-4'
$duplicateRow.elapsedMilliseconds = 99000.0
$sameLaneBaseline = ($compatibleBaseline | ConvertTo-Json -Depth 100 | ConvertFrom-Json)
$sameLaneBaseline.assemblies = @($sameLaneBaseline.assemblies[0], $duplicateRow)
$sameLaneSummary = New-NervTestEvidenceSummary -Records $compatibleRecords -RunMetadata $compatibleRun -Violations @() -Baseline $sameLaneBaseline -PriorAttemptOutcome $null -TopCount 5
Assert-True ([bool]$sameLaneSummary.baseline.available) "A duplicated assembly must still compare when this lane ('$currentLane') has its own row."
Assert-Equal 6000.0 ([double]$sameLaneSummary.baseline.assemblies[0].baselineDurationMilliseconds) 'A duplicated assembly must resolve to this lane row, not to an arbitrary sibling.'
$ambiguousBaseline = ($compatibleBaseline | ConvertTo-Json -Depth 100 | ConvertFrom-Json)
$ambiguousBaseline.assemblies[0].lane = 'backend-shard-3'
$ambiguousBaseline.assemblies = @($ambiguousBaseline.assemblies[0], $duplicateRow)
$ambiguousSummary = New-NervTestEvidenceSummary -Records $compatibleRecords -RunMetadata $compatibleRun -Violations @() -Baseline $ambiguousBaseline -PriorAttemptOutcome $null -TopCount 5
Assert-True (-not [bool]$ambiguousSummary.baseline.available) 'An assembly recorded under two foreign lanes must not silently pick one.'
Assert-Equal 'ambiguous-assembly-in-baseline' $ambiguousSummary.baseline.assemblies[0].unavailableReason 'An ambiguous duplicated assembly must report its own reason.'

# The reader must be able to tell which `source` shape it has. Both known schema versions compare
# identically (the key is the assembly); an unknown or missing version fails closed to report-only.
Assert-Equal 2 $compatibleBaseline.schemaVersion 'A freshly generated baseline must declare schema 2.'
foreach ($schemaCase in @(
    @{ Name = 'schema-1-legacy'; Version = 1; Expected = $null; Available = $true },
    @{ Name = 'schema-2-current'; Version = 2; Expected = $null; Available = $true },
    @{ Name = 'schema-3-unknown'; Version = 3; Expected = 'unsupported-baseline-schema-version'; Available = $false },
    @{ Name = 'schema-0-bogus'; Version = 0; Expected = 'unsupported-baseline-schema-version'; Available = $false }
)) {
    $schemaBaseline = $compatibleBaseline | ConvertTo-Json -Depth 100 | ConvertFrom-Json
    $schemaBaseline.schemaVersion = [int]$schemaCase.Version
    $schemaSummary = New-NervTestEvidenceSummary -Records $compatibleRecords -RunMetadata $compatibleRun -Violations @() -Baseline $schemaBaseline -PriorAttemptOutcome $null -TopCount 5
    Assert-Equal $schemaCase.Expected $schemaSummary.baseline.unavailableReason "Baseline schema case '$($schemaCase.Name)' must report the exact unavailable reason."
    Assert-Equal ([bool]$schemaCase.Available) ([bool]$schemaSummary.baseline.available) "Baseline schema case '$($schemaCase.Name)' availability mismatch."
}
$schemalessBaseline = $compatibleBaseline | ConvertTo-Json -Depth 100 | ConvertFrom-Json | Select-Object -ExcludeProperty schemaVersion
$schemalessSummary = New-NervTestEvidenceSummary -Records $compatibleRecords -RunMetadata $compatibleRun -Violations @() -Baseline $schemalessBaseline -PriorAttemptOutcome $null -TopCount 5
Assert-Equal 'unsupported-baseline-schema-version' $schemalessSummary.baseline.unavailableReason 'A baseline with no schemaVersion at all must fail closed, not compare.'

# Non-integer `schemaVersion` shapes a hand-edited baseline can hold. Why the guard uses TryParse is in
# docs/architecture/test-evidence-governance.md; what matters *here* is which rows are load-bearing:
#   guards  — `non-numeric-text`, `array-value`, `fractional`, `boolean-true` each go red if the
#             TryParse is reverted to `[int]`. Deleting one silently removes a regression guard.
#   coverage — `json-null` (and the schemaless case above) reject identically under both spellings, so
#             they document the contract but prove nothing about this fix. Never cite them as evidence.
# Each value goes through a real ConvertTo-Json/ConvertFrom-Json round trip so it arrives in the exact
# shape a caller reading a hand-edited file off disk would hand in.
foreach ($malformedSchemaCase in @(
    @{ Name = 'non-numeric-text'; Value = 'abc' },
    @{ Name = 'array-value'; Value = @(1, 2) },
    @{ Name = 'fractional'; Value = 1.5 },
    @{ Name = 'boolean-true'; Value = $true },
    @{ Name = 'json-null'; Value = $null }
)) {
    $malformedBaseline = $compatibleBaseline | ConvertTo-Json -Depth 100 | ConvertFrom-Json
    $malformedBaseline.schemaVersion = $malformedSchemaCase.Value
    $malformedBaseline = $malformedBaseline | ConvertTo-Json -Depth 100 | ConvertFrom-Json
    $malformedSummary = $null
    $malformedError = $null
    try {
        $malformedSummary = New-NervTestEvidenceSummary -Records $compatibleRecords -RunMetadata $compatibleRun -Violations @() -Baseline $malformedBaseline -PriorAttemptOutcome $null -TopCount 5
    }
    catch { $malformedError = [string]$_.Exception.Message }
    Assert-Equal $null $malformedError "Baseline schemaVersion case '$($malformedSchemaCase.Name)' must be reported as an unavailable reason, never thrown."
    Assert-Equal 'unsupported-baseline-schema-version' $malformedSummary.baseline.unavailableReason "Baseline schemaVersion case '$($malformedSchemaCase.Name)' must report the documented unavailable reason."
    Assert-True (-not [bool]$malformedSummary.baseline.available) "Baseline schemaVersion case '$($malformedSchemaCase.Name)' must not produce a comparison."
    Assert-Equal 'unsupported-baseline-schema-version' $malformedSummary.baseline.assemblies[0].unavailableReason "Baseline schemaVersion case '$($malformedSchemaCase.Name)' must propagate its reason to every assembly row."
}

# The committed snapshot is a *fallback measurement*, not a governed list (#1507). What is asserted
# here is only that it is still the shape the reader can consume: provenance is honest and the
# durations are usable. What is deliberately NOT asserted any more is topology coverage — "this file
# must carry a row for every authenticated lane" made a shard rearrangement or a new test project
# turn a measurement into a red gate, which is the ceremony #1507 removed. Coverage gaps are now
# reported by scripts/report-backend-test-shard-balance.ps1 as report-only warnings with an estimate.
$committedBaseline = Get-Content (Join-Path $repoRoot 'scripts/test-evidence-baseline.json') -Raw | ConvertFrom-Json
Assert-Equal 'test' $committedBaseline.granularity 'Committed snapshot must be test-granularity.'
Assert-Equal 'trx-elapsed' $committedBaseline.durationMetric 'Committed snapshot must use the trx-elapsed duration metric.'
Assert-Equal 'trx-evidence' $committedBaseline.source.kind 'Committed snapshot must be generated from TRX evidence, not console wall clock.'
Assert-Equal 'push' $committedBaseline.source.event 'Committed snapshot must come from a push run.'
Assert-Equal 'main' $committedBaseline.source.headBranch 'Committed snapshot must come from main.'
Assert-Equal 'success' $committedBaseline.source.conclusion 'Committed snapshot must come from a successful run.'
Assert-True (@($committedBaseline.assemblies).Count -gt 0) 'Committed snapshot must carry at least one assembly row.'
Assert-Equal 0 @($committedBaseline.assemblies | Where-Object { [double]$_.elapsedMilliseconds -le 0 }).Count 'Committed snapshot must not contain non-positive assembly durations.'
Assert-Equal 0 @($committedBaseline.assemblies | Where-Object { [string]::IsNullOrWhiteSpace([string]$_.assembly) }).Count 'Every committed snapshot row must carry the assembly that is now the comparison key.'

$invalidBaselineRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-iip-man-661-invalid-baseline-$([Guid]::NewGuid().ToString('N'))"
try {
    $baselineGenerator = Join-Path $repoRoot 'scripts/generate-test-evidence-baseline.ps1'
    $summaryTemplate = [ordered]@{ workflowRunId = '101'; runAttempt = 1; headSha = '0123456789abcdef0123456789abcdef01234567'; testedSha = '0123456789abcdef0123456789abcdef01234567'; repository = 'Mang-X/Nerv-IIP'; event = 'push'; headBranch = 'main'; sourceUrl = 'https://github.com/Mang-X/Nerv-IIP/actions/runs/101'; runnerOs = 'Linux'; runnerImage = 'ubuntu24@20260720.247.2'; dotnetSdk = '10.0.302'; currentTestOutcome = 'success'; collectionStatus = 'succeeded'; attemptClassification = 'initial'; failed = 0; executed = 1; violations = @(); lane = 'backend-shard-1'; jobName = 'Backend Tests - BusinessGateway'; artifactName = 'backend-shard-1-evidence'; assemblies = @() }
    # A baseline refresh consumes every CI-wired lane, so the fixture set is the full allowlist.
    $evidenceLaneJobs = Get-NervTestEvidenceLaneJobs
    # The fixture reproduces the real per-job image spread observed on run 31149427664 (2026-08-07):
    # two lanes on the old image, three on the new one, in one successful attempt-1 main push.
    # A homogeneous fixture would let the cross-lane equality rule come back unnoticed.
    $laneRunnerImages = [ordered]@{
        'backend-shard-1' = 'ubuntu24@20260720.247.2'
        'backend-shard-2' = 'ubuntu24@20260804.265.1'
        'backend-shard-3' = 'ubuntu24@20260720.247.2'
        'backend-shard-4' = 'ubuntu24@20260804.265.1'
        'connector-host' = 'ubuntu24@20260804.265.1'
    }
    function New-NervEvidenceJobLog {
        param([Parameter(Mandatory)] [string] $RunnerImage, [string] $DotnetSdk = '10.0.302')
        $imageVersion = $RunnerImage.Split('@')[1]
        return "Image: ubuntu-24.04`nVersion: $imageVersion`ndotnet-install: .NET Core SDK with version '$DotnetSdk' is already installed.`ntested-sha=0123456789abcdef0123456789abcdef01234567"
    }
    function New-NervEvidenceSummarySet {
        param([hashtable] $Overrides = @{}, [string] $OverrideLane = 'connector-host')
        return @(
            foreach ($laneName in $evidenceLaneJobs.Keys) {
                $summary = ($summaryTemplate | ConvertTo-Json -Depth 20 | ConvertFrom-Json -AsHashtable)
                $summary.lane = $laneName
                $summary.jobName = [string] $evidenceLaneJobs[$laneName]
                $summary.artifactName = "$laneName-evidence"
                $summary.runnerImage = [string] $laneRunnerImages[$laneName]
                if ($laneName -ceq $OverrideLane) {
                    foreach ($key in $Overrides.Keys) { $summary[$key] = $Overrides[$key] }
                }
                , $summary
            }
        )
    }
    foreach ($invalidCase in @(
        @{ Name = 'mixed-head-sha'; Field = 'headSha'; Value = '1123456789abcdef0123456789abcdef01234567' },
        @{ Name = 'mixed-tested-sha'; Field = 'testedSha'; Value = '1123456789abcdef0123456789abcdef01234567' },
        @{ Name = 'missing-image'; Field = 'runnerImage'; Value = '' },
        @{ Name = 'missing-runner-os'; Field = 'runnerOs'; Value = '' },
        @{ Name = 'missing-sdk'; Field = 'dotnetSdk'; Value = '' },
        @{ Name = 'wrong-url'; Field = 'sourceUrl'; Value = 'https://example.invalid/actions/runs/101' }
    )) {
        $caseRoot = Join-Path $invalidBaselineRoot $invalidCase.Name
        $caseIndex = 0
        foreach ($caseSummary in (New-NervEvidenceSummarySet -Overrides @{ $invalidCase.Field = $invalidCase.Value })) {
            $caseIndex++
            [IO.Directory]::CreateDirectory((Join-Path $caseRoot "lane-$caseIndex")) | Out-Null
            Write-NervUtf8NoBom (Join-Path $caseRoot "lane-$caseIndex/summary.json") (($caseSummary | ConvertTo-Json -Depth 20) + "`n")
        }
        $caseFailed = $false
        try { Invoke-TestPwshScript -ScriptPath $baselineGenerator -LogRoot $invalidBaselineRoot -WorkingDirectory $repoRoot -Name "man-661-baseline-$($invalidCase.Name)" -Arguments @('-EvidenceRoot',$caseRoot,'-OutputPath',(Join-Path $caseRoot 'baseline.json')) | Out-Null } catch { $caseFailed = $true }
        Assert-True $caseFailed "Baseline provenance case '$($invalidCase.Name)' must fail."
    }

    $validRoot = Join-Path $invalidBaselineRoot 'valid-authority'
    $validIndex = 0
    foreach ($validSummary in (New-NervEvidenceSummarySet)) {
        $validIndex++
        [IO.Directory]::CreateDirectory((Join-Path $validRoot "lane-$validIndex")) | Out-Null
        Write-NervUtf8NoBom (Join-Path $validRoot "lane-$validIndex/summary.json") (($validSummary | ConvertTo-Json -Depth 20) + "`n")
    }
    $authorityJobs = [Collections.Generic.List[object]]::new()
    $authorityJobLogs = [ordered]@{}
    # The test-free shard aggregate must still be a successful job, but owns no lane and no log.
    $authorityJobs.Add([ordered]@{ databaseId = '200'; name = 'Backend Tests'; conclusion = 'success' })
    $authorityJobId = 200
    foreach ($laneName in $evidenceLaneJobs.Keys) {
        $authorityJobId++
        $authorityJobs.Add([ordered]@{ databaseId = "$authorityJobId"; name = [string] $evidenceLaneJobs[$laneName]; conclusion = 'success' })
        # Each lane's log carries that lane's own image, which is what the authority check compares against.
        $authorityJobLogs[[string] $evidenceLaneJobs[$laneName]] = New-NervEvidenceJobLog -RunnerImage ([string] $laneRunnerImages[$laneName])
    }
    $authorityTemplate = [ordered]@{ run = [ordered]@{ databaseId = '101'; event = 'push'; headBranch = 'main'; headSha = '0123456789abcdef0123456789abcdef01234567'; attempt = 1; conclusion = 'success'; url = 'https://github.com/Mang-X/Nerv-IIP/actions/runs/101'; workflowName = 'CI'; jobs = @($authorityJobs) }; latestRuns = @([ordered]@{ databaseId = '101'; attempt = 1; headSha = '0123456789abcdef0123456789abcdef01234567'; conclusion = 'success'; event = 'push'; headBranch = 'main' }); jobLogs = $authorityJobLogs }
    $sourceSummaries = @(New-NervEvidenceSummarySet | ForEach-Object { [pscustomobject]$_ })
    Assert-NervEvidenceRootAuthority -SourceSummaries $sourceSummaries -Run ([pscustomobject]$authorityTemplate.run) -LatestRuns @([pscustomobject]$authorityTemplate.latestRuns[0]) -JobLogs $authorityTemplate.jobLogs | Out-Null
    foreach ($authorityCase in @('partial-shard-family','wrong-workflow','wrong-job','not-latest','runner-os-mismatch','wrong-resolved-image','wrong-resolved-sdk','lane-image-swapped','single-lane-sdk-forged','forged-tested-sha','latest-attempt-drift','latest-sha-drift','latest-conclusion-drift','latest-event-drift','latest-branch-drift')) {
        $authority = ($authorityTemplate | ConvertTo-Json -Depth 30 | ConvertFrom-Json -AsHashtable)
        $caseSummaries = @($sourceSummaries | ForEach-Object { $_ | ConvertTo-Json -Depth 20 | ConvertFrom-Json })
        if ($authorityCase -eq 'partial-shard-family') { $caseSummaries = @($caseSummaries | Where-Object { [string]$_.lane -cne 'backend-shard-3' }) }
        elseif ($authorityCase -eq 'wrong-workflow') { $authority.run.workflowName = 'Other' }
        elseif ($authorityCase -eq 'wrong-job') { $authority.run.jobs[0].name = 'Wrong Backend Job' }
        elseif ($authorityCase -eq 'not-latest') { $authority.latestRuns[0].databaseId = '999' }
        elseif ($authorityCase -eq 'runner-os-mismatch') { $caseSummaries | ForEach-Object { $_.runnerOs = 'Windows' } }
        elseif ($authorityCase -eq 'wrong-resolved-image') { $caseSummaries | ForEach-Object { $_.runnerImage = 'ubuntu24@20260720.247.3' } }
        elseif ($authorityCase -eq 'wrong-resolved-sdk') { $caseSummaries | ForEach-Object { $_.dotnetSdk = '10.0.303' } }
        # Dropping cross-lane equality must not let one lane borrow a sibling's image. The swapped value
        # is genuinely present in this very run, so only the per-lane job-log comparison can reject it.
        elseif ($authorityCase -eq 'lane-image-swapped') { @($caseSummaries | Where-Object { [string]$_.lane -ceq 'backend-shard-1' }) | ForEach-Object { $_.runnerImage = [string]$laneRunnerImages['backend-shard-2'] } }
        elseif ($authorityCase -eq 'single-lane-sdk-forged') { @($caseSummaries | Where-Object { [string]$_.lane -ceq 'connector-host' }) | ForEach-Object { $_.dotnetSdk = '10.0.303' } }
        elseif ($authorityCase -eq 'forged-tested-sha') { $caseSummaries | ForEach-Object { $_.testedSha = '1123456789abcdef0123456789abcdef01234567' } }
        elseif ($authorityCase -eq 'latest-attempt-drift') { $authority.latestRuns[0].attempt = 2 }
        elseif ($authorityCase -eq 'latest-sha-drift') { $authority.latestRuns[0].headSha = '1123456789abcdef0123456789abcdef01234567' }
        elseif ($authorityCase -eq 'latest-conclusion-drift') { $authority.latestRuns[0].conclusion = 'failure' }
        elseif ($authorityCase -eq 'latest-event-drift') { $authority.latestRuns[0].event = 'pull_request' }
        elseif ($authorityCase -eq 'latest-branch-drift') { $authority.latestRuns[0].headBranch = 'feature' }
        $authorityFailed = $false
        try { Assert-NervEvidenceRootAuthority -SourceSummaries $caseSummaries -Run ([pscustomobject]$authority.run) -LatestRuns @([pscustomobject]$authority.latestRuns[0]) -JobLogs $authority.jobLogs } catch { $authorityFailed = $true }
        Assert-True $authorityFailed "Actions authority case '$authorityCase' must fail."
    }

    # ---------------------------------------------------------------------------------------------
    # Provenance splits into run identity (structurally shared by every job of one run) and per-job
    # environment (never shared by construction). Three contract tests hold that split in place; each
    # one fails if the split is weakened in either direction.
    # ---------------------------------------------------------------------------------------------

    # (1) A qualifying run whose jobs landed on different runner images is ACCEPTED.
    #     Weakening guard: put 'runnerOs'/'runnerImage'/'dotnetSdk' back into the run-identity list and
    #     this fails with "Evidence summaries have mixed provenance field '<f>'".
    $mixedImages = @($sourceSummaries | ForEach-Object { [string]$_.runnerImage } | Sort-Object -Unique)
    Assert-True ($mixedImages.Count -gt 1) 'The mixed-environment fixture must actually span more than one runner image.'
    $mixedIdentity = Assert-NervEvidenceSourceSummaries -SourceSummaries $sourceSummaries
    Assert-Equal '101' $mixedIdentity.workflowRunId 'A mixed-runner-image run must still resolve one run identity.'
    Assert-NervEvidenceRootAuthority -SourceSummaries $sourceSummaries -Run ([pscustomobject]$authorityTemplate.run) -LatestRuns @([pscustomobject]$authorityTemplate.latestRuns[0]) -JobLogs $authorityTemplate.jobLogs | Out-Null
    # The returned identity is narrowed on purpose: no caller can read a per-job field off it and
    # promote one lane's machine to a run-wide fact. Reintroducing those properties fails this.
    foreach ($leakedField in (Get-NervEvidenceLaneEnvironmentFields)) {
        Assert-True (-not ($mixedIdentity.PSObject.Properties.Name -ccontains $leakedField)) "Run identity must not expose per-job environment field '$leakedField'."
    }
    # Both memberships are pinned to a LITERAL list, not to the implementation's own list. Deriving
    # the expectation from `Get-NervEvidenceRunIdentityFields` would let a field be deleted from the
    # list and the projection at the same time with nothing going red — five of the eight are not
    # read by `Assert-NervEvidenceRootAuthority`, so StrictMode would not catch them either.
    Assert-Equal 'workflowRunId,runAttempt,headSha,testedSha,repository,event,headBranch,sourceUrl' ((Get-NervEvidenceRunIdentityFields) -join ',') 'The run-identity field set is frozen; adding or removing one changes what cross-lane equality certifies.'
    Assert-Equal 'runnerOs,runnerImage,dotnetSdk' ((Get-NervEvidenceLaneEnvironmentFields) -join ',') 'The per-job environment field set is frozen; moving a field here silently drops it from cross-lane equality.'
    Assert-Equal 'workflowRunId,runAttempt,headSha,testedSha,repository,event,headBranch,sourceUrl' (@($mixedIdentity.PSObject.Properties.Name) -join ',') 'Run identity must project exactly the frozen run-identity field set.'

    # (2) Run identity stays byte-for-byte equal across lanes. One mismatching lane is still REJECTED,
    #     field by field, with the exact message — so moving any field out of the identity set fails here.
    foreach ($identityField in (Get-NervEvidenceRunIdentityFields)) {
        $driftValue = switch ($identityField) {
            'workflowRunId' { '102' }
            'runAttempt' { 2 }
            'headSha' { '1123456789abcdef0123456789abcdef01234567' }
            'testedSha' { '1123456789abcdef0123456789abcdef01234567' }
            'repository' { 'Mang-X/Other' }
            'event' { 'pull_request' }
            'headBranch' { 'feature' }
            'sourceUrl' { 'https://github.com/Mang-X/Nerv-IIP/actions/runs/102' }
        }
        $driftSummaries = @(New-NervEvidenceSummarySet -Overrides @{ $identityField = $driftValue } -OverrideLane 'connector-host' | ForEach-Object { [pscustomobject]$_ })
        $driftMessage = $null
        try { Assert-NervEvidenceSourceSummaries -SourceSummaries $driftSummaries | Out-Null } catch { $driftMessage = [string]$_.Exception.Message }
        Assert-Equal "Evidence summaries have mixed provenance field '$identityField'." $driftMessage "Mixed run identity field '$identityField' must be rejected by the cross-lane equality rule."
    }

    # (3) Per-lane environment is recorded per lane in the baseline, and there is no run-wide runner
    #     field left to mistake for the whole. Reintroducing a flat source.runnerImage fails this.
    $laneProvenance = @(Get-NervEvidenceLaneProvenance -SourceSummaries $sourceSummaries)
    $provenanceBaseline = New-NervTestEvidenceBaseline -Summaries @($sourceSummaries | ForEach-Object {
        [pscustomobject]@{ granularity = 'test'; assemblies = @([pscustomobject]@{ lane = [string]$_.lane; assembly = 'Shared.Tests.dll'; passed = 1; failed = 0; skipped = 0; executed = 1; total = 1; elapsedMilliseconds = 10 }) }
    }) -SourceMetadata @{
        sourceKind = 'trx-evidence'; repository = 'Mang-X/Nerv-IIP'; workflowRunId = '101'; runAttempt = 1; jobId = ''
        headSha = '0123456789abcdef0123456789abcdef01234567'; testedSha = '0123456789abcdef0123456789abcdef01234567'
        sourceUrl = 'https://github.com/Mang-X/Nerv-IIP/actions/runs/101'; event = 'push'; headBranch = 'main'; conclusion = 'success'; jobConclusion = 'success'
        laneProvenance = $laneProvenance; selectedLanes = @($sourceSummaries.lane | Sort-Object -Unique); generatorCommand = 'fixture'
    } -GeneratedAtUtc ([DateTimeOffset]'2026-08-07T00:00:00Z')
    Assert-Equal 2 $provenanceBaseline.schemaVersion 'Per-lane provenance is schema 2; a schema-1 file carries the misleading flat trio instead.'
    foreach ($flatField in (Get-NervEvidenceLaneEnvironmentFields)) {
        Assert-True (-not ($provenanceBaseline.source.PSObject.Properties.Name -ccontains $flatField)) "Baseline source must not carry a run-wide '$flatField'; one lane's value is not the run's."
    }
    Assert-Equal @($sourceSummaries).Count @($provenanceBaseline.source.laneProvenance).Count 'Baseline must record one runner-environment row per evidence lane.'
    foreach ($expectedRow in $laneProvenance) {
        $actualRow = @($provenanceBaseline.source.laneProvenance | Where-Object { [string]$_.lane -ceq [string]$expectedRow.lane })
        Assert-Equal 1 $actualRow.Count "Baseline lane provenance must contain exactly one row for lane '$($expectedRow.lane)'."
        Assert-Equal ([string]$expectedRow.runnerImage) ([string]$actualRow[0].runnerImage) "Baseline lane provenance must record lane '$($expectedRow.lane)' own runner image."
        Assert-Equal ([string]$expectedRow.jobName) ([string]$actualRow[0].jobName) "Baseline lane provenance must bind lane '$($expectedRow.lane)' to its own job name."
        Assert-Equal ([string]$expectedRow.dotnetSdk) ([string]$actualRow[0].dotnetSdk) "Baseline lane provenance must record lane '$($expectedRow.lane)' own SDK."
        Assert-Equal ([string]$expectedRow.runnerOs) ([string]$actualRow[0].runnerOs) "Baseline lane provenance must record lane '$($expectedRow.lane)' own runner OS."
    }
    # Both observed images survive into the baseline: collapsing the rows to a single value fails here.
    Assert-Equal ($mixedImages -join ',') ((@($provenanceBaseline.source.laneProvenance | ForEach-Object { [string]$_.runnerImage }) | Sort-Object -Unique) -join ',') 'Baseline must preserve every distinct runner image the run actually used.'
    # "Per lane" only means something if the rows cover the lanes the baseline actually records.
    # A partial record is worse than the old flat trio: it reads as complete while certifying one
    # lane's machine for five lanes of timing — literally the failure this whole split exists to
    # prevent. Each case below asserts the exact rejection message, so deleting the check goes red.
    $provenanceSummaries = @($sourceSummaries | ForEach-Object {
        [pscustomobject]@{ granularity = 'test'; assemblies = @([pscustomobject]@{ lane = [string]$_.lane; assembly = 'Shared.Tests.dll'; passed = 1; failed = 0; skipped = 0; executed = 1; total = 1; elapsedMilliseconds = 10 }) }
    })
    $recordedLaneList = (@($sourceSummaries | ForEach-Object { [string]$_.lane } | Sort-Object -Unique) -join ', ')
    function Invoke-NervProvenanceBaseline {
        param([Parameter(Mandatory)] [object[]] $LaneProvenance, [object[]] $Summaries = $provenanceSummaries)
        New-NervTestEvidenceBaseline -Summaries $Summaries -SourceMetadata @{
            sourceKind = 'trx-evidence'; repository = 'Mang-X/Nerv-IIP'; workflowRunId = '101'; runAttempt = 1; jobId = ''
            headSha = '0123456789abcdef0123456789abcdef01234567'; testedSha = '0123456789abcdef0123456789abcdef01234567'
            sourceUrl = 'https://github.com/Mang-X/Nerv-IIP/actions/runs/101'; event = 'push'; headBranch = 'main'; conclusion = 'success'; jobConclusion = 'success'
            laneProvenance = $LaneProvenance; selectedLanes = @($sourceSummaries.lane | Sort-Object -Unique); generatorCommand = 'fixture'
        } -GeneratedAtUtc ([DateTimeOffset]'2026-08-07T00:00:00Z')
    }
    # Baseline for the negative cases: the full, correct row set is accepted.
    Assert-Equal @($sourceSummaries).Count @((Invoke-NervProvenanceBaseline -LaneProvenance $laneProvenance).source.laneProvenance).Count 'The complete per-lane provenance row set must be accepted.'

    $strayRow = [pscustomobject][ordered]@{ lane = 'postgres'; jobName = 'Real PostgreSQL Tests'; runnerOs = 'Linux'; runnerImage = 'ubuntu24@20260720.247.2'; dotnetSdk = '10.0.302' }
    foreach ($coverageCase in @(
        @{ Name = 'missing-lane-rows'; Rows = @($laneProvenance[0]); Expected = "Baseline lane provenance must cover exactly the lanes the baseline records; provenance=[$([string]$laneProvenance[0].lane)] recorded=[$recordedLaneList]." },
        @{ Name = 'stray-lane-row'; Rows = @($laneProvenance + @($strayRow)); Expected = "Baseline lane provenance must cover exactly the lanes the baseline records; provenance=[$recordedLaneList, postgres] recorded=[$recordedLaneList]." },
        @{ Name = 'duplicate-lane-rows'; Rows = @($laneProvenance + @($laneProvenance[0])); Expected = 'Baseline lane provenance rows must name unique lanes.' }
    )) {
        $coverageMessage = $null
        try { Invoke-NervProvenanceBaseline -LaneProvenance $coverageCase.Rows | Out-Null } catch { $coverageMessage = [string]$_.Exception.Message }
        Assert-Equal $coverageCase.Expected $coverageMessage "Lane provenance coverage case '$($coverageCase.Name)' must be rejected with its exact reason."
    }

    # `jobName` is written into the retained baseline, so it is provenance too. A row must name the
    # allowlisted job for its own lane — it cannot be blank, invented, or borrowed from a sibling.
    $laneJobsForProvenance = Get-NervTestEvidenceLaneJobs
    foreach ($jobNameCase in @(
        @{ Name = 'empty'; Value = ''; Expected = "Baseline lane provenance for lane '$([string]$laneProvenance[0].lane)' must name the job that produced it." },
        @{ Name = 'invented'; Value = 'Totally Bogus Job'; Expected = "Baseline lane provenance for lane '$([string]$laneProvenance[0].lane)' names the wrong authoritative job 'Totally Bogus Job'." },
        @{ Name = 'sibling-borrowed'; Value = [string]$laneJobsForProvenance['backend-shard-2']; Expected = "Baseline lane provenance for lane '$([string]$laneProvenance[0].lane)' names the wrong authoritative job '$([string]$laneJobsForProvenance['backend-shard-2'])'." }
    )) {
        $jobNameRows = @($laneProvenance | ForEach-Object { $_ | Select-Object * })
        $jobNameRows[0].jobName = [string]$jobNameCase.Value
        $jobNameMessage = $null
        try { Invoke-NervProvenanceBaseline -LaneProvenance $jobNameRows | Out-Null } catch { $jobNameMessage = [string]$_.Exception.Message }
        Assert-Equal $jobNameCase.Expected $jobNameMessage "Lane provenance jobName case '$($jobNameCase.Name)' must be rejected with its exact reason."
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
    # Rendering of an unavailable comparison is asserted against an explicit project-granularity baseline,
    # not against the committed baseline, whose granularity and rows move with every MAN-661 refresh.
    $projectBaselinePath = Join-Path $collectorRoot 'project-granularity-baseline.json'
    Write-NervUtf8NoBom -Path $projectBaselinePath -Text (($baselineA | ConvertTo-Json -Depth 100) + "`n")
    Invoke-TestPwshScript -ScriptPath $collector -LogRoot $collectorRoot -WorkingDirectory $repoRoot -Name 'man-661-collector-success' -Arguments @(
        '-Lane', 'backend', '-SelectedLanes', 'backend', '-ResultsDirectory', $successRaw,
        '-OutputDirectory', $successOut, '-WorkflowRunId', 'fixture-success', '-RunAttempt', '1',
        '-HeadSha', '0123456789abcdef0123456789abcdef01234567', '-TestedSha', '0123456789abcdef0123456789abcdef01234567', '-RunnerOs', 'Linux',
        '-Repository', 'Mang-X/Nerv-IIP', '-JobName', 'Backend Tests', '-CurrentTestOutcome', 'success',
        '-Event', 'push', '-HeadBranch', 'main', '-SourceUrl', 'https://github.com/Mang-X/Nerv-IIP/actions/runs/fixture-success',
        '-RunnerImage', 'ubuntu24@20260720.247.2', '-DotnetSdk', '10.0.302', '-ArtifactName', 'fixture-artifact', '-RetentionDays', '14',
        '-PolicyPath', (Join-Path $repoRoot 'scripts/test-evidence-policy.json'),
        '-BaselinePath', $projectBaselinePath
    ) | Out-Null
    foreach ($required in @('tests.jsonl', 'summary.json', 'summary.md', 'diagnostics.log')) { Assert-True (Test-Path (Join-Path $successOut $required)) "Collector missing '$required'." }
    $successSummary = Get-Content (Join-Path $successOut 'summary.json') -Raw | ConvertFrom-Json
    Assert-Equal 'backend' $successSummary.selectedLanes[0] 'Collector summary must retain the selected lane selector.'
    Assert-Equal 'backend' $successSummary.selectedLaneResults[0].baseLane 'Collector summary must emit the selected logical lane result.'
    Assert-Equal 1 $successSummary.selectedLaneResults[0].executed 'Collector selected-lane result executed count mismatch.'
    Assert-Equal 'incompatible-granularity-or-duration-metric' $successSummary.baseline.unavailableReason 'A project wall-clock baseline must remain explicitly unavailable for TRX comparison.'
    $successMarkdown = Get-Content (Join-Path $successOut 'summary.md') -Raw
    foreach ($exact in @(
        '## Selected lane results',
        '| backend | backend | backend | 1 | 0 | 0 | 1 | 1 | pass |',
        '| Lane | Assembly | Passed | Failed | Skipped | Executed | Total | Test duration (ms) | TRX elapsed (ms) |',
        '| backend | Nerv.IIP.Connector.Sample.Tests.dll | 1 | 0 | 0 | 1 | 1 | 10 | 20 |',
        'Retained artifact: fixture-artifact, retention=14 days, location=artifact://fixture-artifact/',
        'Baseline source: https://github.com/Mang-X/Nerv-IIP/actions/runs/30819675007',
        'Privacy redactions: 0', 'Baseline comparison: unavailable (incompatible-granularity-or-duration-metric)', 'Assembly backend/Nerv.IIP.Connector.Sample.Tests.dll: 20ms elapsed',
        'Test Fixture.ConnectorTests.connector: 10ms', '## Skip reasons', '- None.'
    )) { Assert-True $successMarkdown.Contains($exact) "Job Summary is missing exact value '$exact'." }
    Assert-True (-not $successMarkdown.Contains('baseline=ms, delta=%')) 'Unavailable baseline comparison must not render empty metric placeholders.'
    Assert-True ($successMarkdown.Contains('unavailable (incompatible-granularity-or-duration-metric)')) 'Each incompatible assembly comparison must render its unavailable reason.'

    # Field-level assertions on the committed snapshot cannot show the one property that matters:
    # that the collector can actually consume it. So run the real collector end to end with
    # `-BaselinePath` pointing at the committed artifact, using synthesized TRX evidence for an
    # assembly the snapshot covers — deliberately reported from a **different** shard lane than the
    # one the snapshot recorded it under (#1507). This is the whole-pipeline version of the unit
    # assertion above: if the lane ever re-enters the comparison key, this end-to-end case goes red.
    $committedRow = @($committedBaseline.assemblies | Sort-Object lane, assembly | Select-Object -First 1)[0]
    $committedLane = @((Get-NervTestEvidenceLaneJobs).Keys | Sort-Object | Where-Object { -not [string]::Equals([string]$_, [string]$committedRow.lane, [StringComparison]::Ordinal) })[0]
    Assert-True (-not [string]::IsNullOrWhiteSpace($committedLane)) 'The committed-snapshot collector case needs a second authenticated lane to report from.'
    $committedAssembly = [string]$committedRow.assembly
    $committedRaw = Join-Path $collectorRoot 'committed-baseline-raw'
    $committedOut = Join-Path $collectorRoot 'committed-baseline'
    [IO.Directory]::CreateDirectory($committedRaw) | Out-Null
    Write-NervUtf8NoBom -Path (Join-Path $committedRaw 'committed-baseline-results.trx') -Text @"
<?xml version="1.0" encoding="utf-8"?>
<TestRun id="33333333-3333-3333-3333-333333333333" xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Times creation="2026-08-05T11:00:00Z" queuing="2026-08-05T11:00:00Z" start="2026-08-05T11:00:00Z" finish="2026-08-05T11:00:00.020Z" />
  <Results><UnitTestResult testId="33333333-3333-3333-3333-333333333301" testName="committedBaselineComparable" duration="00:00:00.0100000" outcome="Passed" /></Results>
  <TestDefinitions><UnitTest id="33333333-3333-3333-3333-333333333301" name="committedBaselineComparable" storage="$([Security.SecurityElement]::Escape($committedAssembly))"><TestMethod className="Fixture.CommittedBaselineTests" name="committedBaselineComparable" /></UnitTest></TestDefinitions>
  <ResultSummary outcome="Completed"><Counters total="1" executed="1" passed="1" failed="0" notExecuted="0" /></ResultSummary>
</TestRun>
"@
    Invoke-TestPwshScript -ScriptPath $collector -LogRoot $collectorRoot -WorkingDirectory $repoRoot -Name 'man-661-collector-committed-baseline' -Arguments @(
        '-Lane', $committedLane, '-SelectedLanes', $committedLane, '-ResultsDirectory', $committedRaw,
        '-OutputDirectory', $committedOut, '-WorkflowRunId', 'fixture-committed-baseline', '-RunAttempt', '1',
        '-HeadSha', '0123456789abcdef0123456789abcdef01234567', '-TestedSha', '0123456789abcdef0123456789abcdef01234567', '-RunnerOs', 'Linux',
        '-Repository', 'Mang-X/Nerv-IIP', '-JobName', [string](Get-NervTestEvidenceLaneJobs)[$committedLane], '-CurrentTestOutcome', 'success',
        '-Event', 'push', '-HeadBranch', 'main', '-SourceUrl', 'https://github.com/Mang-X/Nerv-IIP/actions/runs/fixture-committed-baseline',
        '-RunnerImage', 'ubuntu24@20260720.247.2', '-DotnetSdk', '10.0.302', '-ArtifactName', 'fixture-artifact', '-RetentionDays', '14',
        '-PolicyPath', (Join-Path $repoRoot 'scripts/test-evidence-policy.json'),
        '-BaselinePath', (Join-Path $repoRoot 'scripts/test-evidence-baseline.json')
    ) | Out-Null
    $committedSummary = Get-Content (Join-Path $committedOut 'summary.json') -Raw | ConvertFrom-Json
    Assert-True ([bool]$committedSummary.baseline.available) "Committed baseline must be consumable by the collector for lane '$committedLane'."
    Assert-Equal $null $committedSummary.baseline.unavailableReason 'Collector comparison against the committed baseline must not report an unavailable reason.'
    $committedDelta = @($committedSummary.baseline.assemblies | Where-Object { [string]$_.assembly -ceq $committedAssembly })
    Assert-Equal 1 $committedDelta.Count "Collector must emit exactly one comparison row for '$committedAssembly'."
    Assert-True ([bool]$committedDelta[0].available) 'The comparison row backed by the committed baseline must be available.'
    Assert-Equal ([double]$committedRow.elapsedMilliseconds) ([double]$committedDelta[0].baselineDurationMilliseconds) 'Collector must compare against the committed baseline duration verbatim.'
    Assert-True ((Get-Content (Join-Path $committedOut 'summary.md') -Raw).Contains('Baseline comparison: available')) 'Job Summary must render the committed baseline comparison as available.'

    foreach ($adversarial in @(
        @{ Name = 'identity'; Lane = 'Authorization=Bearer lane-secret'; Selected = 'backend'; Run = 'Authorization=Bearer run-secret'; Repository = 'Authorization=Bearer repo-secret'; Job = 'Authorization=Bearer job-secret' },
        @{ Name = 'violation'; Lane = 'backend'; Selected = 'Authorization=Bearer violation-secret'; Run = 'safe-run'; Repository = 'Mang-X/Nerv-IIP'; Job = 'Backend Tests' }
    )) {
        $adversarialOut = Join-Path $collectorRoot "$($adversarial.Name)-failure"
        $adversarialStep = Join-Path $collectorRoot "$($adversarial.Name)-step.md"
        $adversarialManifest = Join-Path $collectorRoot "$($adversarial.Name)-output.txt"
        $adversarialFailed = $false
        try { Invoke-TestPwshScript -ScriptPath $collector -LogRoot $collectorRoot -WorkingDirectory $repoRoot -Name "man-661-adversarial-$($adversarial.Name)" -Arguments @('-Lane',$adversarial.Lane,'-SelectedLanes',$adversarial.Selected,'-ResultsDirectory',$successRaw,'-OutputDirectory',$adversarialOut,'-WorkflowRunId',$adversarial.Run,'-RunAttempt','1','-HeadSha','Authorization=Bearer head-secret','-TestedSha','Authorization=Bearer tested-secret','-RunnerOs','Linux','-Repository',$adversarial.Repository,'-JobName',$adversarial.Job,'-StepSummaryPath',$adversarialStep,'-EvidencePathOutputFile',$adversarialManifest) | Out-Null } catch { $adversarialFailed = $true }
        Assert-True $adversarialFailed 'Adversarial identity input must fail.'
        $adversarialRetained = [string]::Join("`n", @(Get-ChildItem $adversarialOut -File -Recurse | ForEach-Object { Get-Content $_.FullName -Raw })) + (Get-Content $adversarialStep -Raw) + (Get-Content $adversarialManifest -Raw)
        foreach ($sentinel in @('lane-secret','run-secret','repo-secret','job-secret','violation-secret','head-secret','tested-secret')) { Assert-True (-not $adversarialRetained.Contains($sentinel)) "Failure bundle leaked '$sentinel'." }
    }

    $conflictOut = Join-Path $collectorRoot 'writer-conflict'
    [IO.Directory]::CreateDirectory($conflictOut) | Out-Null
    Write-NervUtf8NoBom (Join-Path $conflictOut 'unrelated.txt') 'preserve-me'
    $conflictManifest = Join-Path $collectorRoot 'writer-conflict-output.txt'
    $conflictStep = Join-Path $collectorRoot 'writer-conflict-step.md'
    $conflictFailed = $false
    try { Invoke-TestPwshScript -ScriptPath $collector -LogRoot $collectorRoot -WorkingDirectory $repoRoot -Name 'man-661-writer-conflict' -Arguments @('-Lane','backend','-SelectedLanes','backend','-ResultsDirectory',$successRaw,'-OutputDirectory',$conflictOut,'-WorkflowRunId','writer-conflict','-RunAttempt','1','-HeadSha','0123456789abcdef0123456789abcdef01234567','-TestedSha','0123456789abcdef0123456789abcdef01234567','-RunnerOs','Linux','-StepSummaryPath',$conflictStep,'-EvidencePathOutputFile',$conflictManifest) | Out-Null } catch { $conflictFailed = $true }
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
        try { Invoke-TestPwshScript -ScriptPath $collector -LogRoot $collectorRoot -WorkingDirectory $repoRoot -Name "man-661-collector-$($failureCase.Name)" -Arguments @('-Lane','backend','-SelectedLanes','backend','-ResultsDirectory',$failureCase.Results,'-OutputDirectory',$failureOut,'-WorkflowRunId','fixture-failure','-RunAttempt','1','-HeadSha','0123456789abcdef0123456789abcdef01234567','-TestedSha','0123456789abcdef0123456789abcdef01234567','-RunnerOs','Linux','-StepSummaryPath',$failureSummary) | Out-Null }
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
        Invoke-TestPwshScript -ScriptPath $collector -LogRoot $collectorRoot -WorkingDirectory $repoRoot -Name 'man-661-collector-unregistered' -Arguments @(
            '-Lane', 'backend', '-SelectedLanes', 'backend', '-ResultsDirectory', $badRaw,
            '-OutputDirectory', (Join-Path $collectorRoot 'bad'), '-WorkflowRunId', 'fixture-bad', '-RunAttempt', '1',
            '-HeadSha', '0123456789abcdef0123456789abcdef01234567', '-TestedSha', '0123456789abcdef0123456789abcdef01234567', '-RunnerOs', 'Linux'
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
        Invoke-TestPwshScript -ScriptPath $collector -LogRoot $collectorRoot -WorkingDirectory $repoRoot -Name 'man-661-collector-zero' -Arguments @(
            '-Lane', 'postgres', '-SelectedLanes', 'postgres', '-ResultsDirectory', $postgresRaw,
            '-OutputDirectory', (Join-Path $collectorRoot 'postgres'), '-WorkflowRunId', 'fixture-postgres', '-RunAttempt', '1',
            '-HeadSha', '0123456789abcdef0123456789abcdef01234567', '-TestedSha', '0123456789abcdef0123456789abcdef01234567', '-RunnerOs', 'Linux'
        ) | Out-Null
    }
    catch { $postgresFailed = $true }
    Assert-True $postgresFailed 'All-skipped real dependency lane must exit nonzero.'
    Assert-True ((Get-Content (Join-Path $collectorRoot 'postgres/summary.json') -Raw).Contains('zero-execution')) 'Zero-execution summary is missing.'

    $rerunRaw = Join-Path $collectorRoot 'rerun-raw'
    [IO.Directory]::CreateDirectory($rerunRaw) | Out-Null
    Copy-Item (Join-Path $fixtures 'connector-results.trx') $rerunRaw
    $rerunOut = Join-Path $collectorRoot 'rerun-self-supplied'
    Invoke-TestPwshScript -ScriptPath $collector -LogRoot $collectorRoot -WorkingDirectory $repoRoot -Name 'man-661-collector-rerun' -Arguments @(
        '-Lane', 'backend', '-SelectedLanes', 'backend', '-ResultsDirectory', $rerunRaw,
        '-OutputDirectory', $rerunOut, '-WorkflowRunId', 'fixture-rerun', '-RunAttempt', '2',
        '-HeadSha', '0123456789abcdef0123456789abcdef01234567', '-TestedSha', '89abcdef0123456789abcdef0123456789abcdef', '-RunnerOs', 'Linux', '-CurrentTestOutcome', 'success'
    ) | Out-Null
    Assert-True ((Get-Content (Join-Path $rerunOut 'summary.md') -Raw).Contains('- Attempt: rerun ')) 'A rerun without authenticated GitHub evidence must not certify recovery.'
    $priorRun = [pscustomobject]@{ id = 'fixture-rerun'; head_sha = '0123456789abcdef0123456789abcdef01234567'; run_attempt = 2 }
    $priorJobs = @([pscustomobject]@{ name = 'Backend Tests - Platform'; run_attempt = 1; conclusion = 'failure' })
    $priorAuthority = Resolve-NervPriorAttemptAuthority -Run $priorRun -Jobs $priorJobs -WorkflowRunId 'fixture-rerun' -HeadSha '0123456789abcdef0123456789abcdef01234567' -RunAttempt 2 -Lane backend-shard-2 -JobName 'Backend Tests - Platform'
    Assert-True $priorAuthority.verified 'Pure prior-attempt validation must accept exact authenticated response data.'
    Assert-Equal 'failure' $priorAuthority.outcome 'Pure prior-attempt validation must return the authoritative failed outcome.'
    foreach ($invalidPrior in @(
        @{ Name = 'wrong-run'; Run = [pscustomobject]@{ id = 'other'; head_sha = '0123456789abcdef0123456789abcdef01234567'; run_attempt = 2 }; Jobs = $priorJobs; JobName = 'Backend Tests - Platform' },
        @{ Name = 'wrong-sha'; Run = [pscustomobject]@{ id = 'fixture-rerun'; head_sha = '1123456789abcdef0123456789abcdef01234567'; run_attempt = 2 }; Jobs = $priorJobs; JobName = 'Backend Tests - Platform' },
        @{ Name = 'wrong-current-attempt'; Run = [pscustomobject]@{ id = 'fixture-rerun'; head_sha = '0123456789abcdef0123456789abcdef01234567'; run_attempt = 3 }; Jobs = $priorJobs; JobName = 'Backend Tests - Platform' },
        @{ Name = 'wrong-job'; Run = $priorRun; Jobs = $priorJobs; JobName = 'Other Job' },
        @{ Name = 'wrong-prior-attempt'; Run = $priorRun; Jobs = @([pscustomobject]@{ name = 'Backend Tests - Platform'; run_attempt = 2; conclusion = 'failure' }); JobName = 'Backend Tests - Platform' },
        @{ Name = 'nonfailure'; Run = $priorRun; Jobs = @([pscustomobject]@{ name = 'Backend Tests - Platform'; run_attempt = 1; conclusion = 'success' }); JobName = 'Backend Tests - Platform' },
        @{ Name = 'aggregate-cannot-certify-a-lane'; Run = $priorRun; Jobs = @([pscustomobject]@{ name = 'Backend Tests'; run_attempt = 1; conclusion = 'failure' }); JobName = 'Backend Tests' }
    )) {
        $invalidLane = if ($invalidPrior.Name -ceq 'aggregate-cannot-certify-a-lane') { 'backend' } else { 'backend-shard-2' }
        $invalidAuthority = Resolve-NervPriorAttemptAuthority -Run $invalidPrior.Run -Jobs $invalidPrior.Jobs -WorkflowRunId 'fixture-rerun' -HeadSha '0123456789abcdef0123456789abcdef01234567' -RunAttempt 2 -Lane $invalidLane -JobName $invalidPrior.JobName
        Assert-True (-not $invalidAuthority.verified) "Prior-attempt authority case '$($invalidPrior.Name)' must fail closed."
    }
    Assert-True (-not (Test-Path (Join-Path $successOut 'backend-results.trx'))) 'Collector must not copy raw result paths.'
}
finally {
    if (Test-Path $collectorRoot) { Remove-Item $collectorRoot -Recurse -Force }
}

$workflow = Get-Content (Join-Path $repoRoot '.github/workflows/ci.yml') -Raw
$compatibilitySource = Get-Content (Join-Path $repoRoot 'scripts/check-script-compatibility.ps1') -Raw
$reviewWiringGaps = [Collections.Generic.List[string]]::new()
if (-not $collectorSource.Contains('#   Category: check, generate')) { $reviewWiringGaps.Add('collector composite category') }
if (-not $workflow.Contains('- name: Run test evidence contract tests') -or -not $workflow.Contains('run: ./scripts/tests/test-evidence.Tests.ps1')) { $reviewWiringGaps.Add('Script Governance CI runner') }
if (-not $compatibilitySource.Contains('scripts/tests/test-evidence.Tests.ps1')) { $reviewWiringGaps.Add('compat-fast runner') }
if ($workflow.Contains('-HeadBranch ${{ github.head_ref || github.ref_name }}')) { $reviewWiringGaps.Add('direct HeadBranch expression interpolation') }
if (-not $workflow.Contains('HEAD_BRANCH: ${{ github.head_ref || github.ref_name }}') -or -not $workflow.Contains('-HeadBranch $env:HEAD_BRANCH')) { $reviewWiringGaps.Add('HeadBranch environment transport') }
Assert-Equal 0 $reviewWiringGaps.Count "Review wiring gaps remain: $([string]::Join(', ', $reviewWiringGaps))"
Assert-True ($workflow.Contains('actions: read')) 'Rerun lookup needs read-only Actions permission.'
Assert-True ($workflow.Contains('GH_TOKEN: ${{ github.token }}')) 'Rerun lookup must receive the read-only workflow token.'
Assert-True ($workflow.Contains('-CurrentTestOutcome ${{ steps.shard-tests.outcome }}')) 'Backend shard native test outcome must flow into rerun classification.'
Assert-True ($workflow.Contains('-CurrentTestOutcome ${{ steps.connector-host-tests.outcome }}')) 'Connector native test outcome must flow into rerun classification.'
Assert-True (-not $workflow.Contains('-CurrentTestOutcome ${{ steps.backend-tests.outcome }}')) 'The shard aggregate runs no tests and must not certify an outcome.'
Assert-True ($workflow.Contains('dotnet-sdk=$(dotnet --version)')) 'Evidence provenance must resolve the actual SDK version.'
Assert-True ($workflow.Contains('$testedSha = (git rev-parse HEAD).Trim()')) 'Evidence provenance must resolve the actual checked-out commit.'
Assert-True ($workflow.Contains('Write-Host "tested-sha=$testedSha"')) 'The tested SHA must be independently recoverable from authoritative job logs.'
Assert-True ($workflow.Contains('-HeadSha ${{ github.event.pull_request.head.sha || github.sha }}')) 'PR branch-head provenance must not use the synthetic merge SHA.'
Assert-True ($workflow.Contains('-TestedSha ${{ steps.shard-evidence-environment.outputs.tested-sha }}')) 'Backend shard tested-checkout provenance must flow from git.'
Assert-True ($workflow.Contains('-TestedSha ${{ steps.connector-evidence-environment.outputs.tested-sha }}')) 'Connector tested-checkout provenance must flow from git.'
Assert-True (-not $workflow.Contains('-CommitSha')) 'Ambiguous commit SHA workflow input must be removed.'
Assert-True (-not $workflow.Contains('TestOnly')) 'Production workflow must not use any test-only authority seam.'
Assert-True ($workflow.Contains('outputs.evidence-path')) 'Workflow upload must use the collector-selected owned evidence path.'
Assert-True (-not $workflow.Contains('continue-on-error')) 'MAN-661 forbids continue-on-error.'
Assert-True ($workflow.Contains('--logger trx')) 'Connector Host must emit TRX.'
Assert-True ((Get-Content (Join-Path $repoRoot 'scripts/run-backend-test-shard.ps1') -Raw).Contains("'--logger', `"trx;LogFilePrefix=`$TrxFilePrefix`"")) 'Backend shards must emit uniquely prefixed TRX.'
Assert-True ($workflow.Contains('./scripts/collect-test-evidence.ps1')) 'CI must use the governed collector.'
Assert-True ($workflow.Contains('if: always()')) 'Collection/upload must run after failures.'
foreach ($shardLane in @('backend-shard-1', 'backend-shard-2', 'backend-shard-3', 'backend-shard-4')) {
    Assert-True ($workflow.Contains("test-evidence-$shardLane-`${{ github.run_id }}-`${{ github.run_attempt }}")) "Backend shard artifact identity mismatch for '$shardLane'."
    Assert-True ($workflow.Contains("-Lane $shardLane")) "Backend shard lane '$shardLane' must be collected."
    Assert-True ($workflow.Contains("-SelectedLanes $shardLane")) "Backend shard lane '$shardLane' must select only itself."
}
Assert-True (-not $workflow.Contains('-Lane backend ')) 'The unsharded backend lane must no longer be collected once shards own it.'
$laneJobAllowlist = Get-NervTestEvidenceLaneJobs
Assert-Equal 5 $laneJobAllowlist.Count 'The lane-to-job allowlist must cover exactly the four backend shards and connector-host.'
Assert-True (-not $laneJobAllowlist.Contains('backend')) 'No job may certify the unsharded backend lane once the shards own it.'
Assert-True (-not (@($laneJobAllowlist.Values) -ccontains 'Backend Tests')) 'The test-free shard aggregate must own no evidence lane.'
Assert-True ($workflow.Contains('test-evidence-connector-host-${{ github.run_id }}-${{ github.run_attempt }}')) 'Connector artifact identity mismatch.'
Assert-True (-not $workflow.Contains('path: artifacts/test-evidence-raw')) 'Raw TRX must not be uploaded.'
Assert-True (-not $workflow.Contains('path: TestResults')) 'Backend shards must not upload unredacted result directories.'
foreach ($laneContract in @(
    @{ Test = '- name: Test BusinessGateway shard'; Collect = '- name: Collect BusinessGateway shard evidence'; Upload = '- name: Upload BusinessGateway shard evidence' },
    @{ Test = '- name: Test platform shard'; Collect = '- name: Collect platform shard evidence'; Upload = '- name: Upload platform shard evidence' },
    @{ Test = '- name: Test business core A shard'; Collect = '- name: Collect business core A shard evidence'; Upload = '- name: Upload business core A shard evidence' },
    @{ Test = '- name: Test business core B shard'; Collect = '- name: Collect business core B shard evidence'; Upload = '- name: Upload business core B shard evidence' },
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

# --- MAN-799 CI timeout-budget invariants -----------------------------------------------------
# Evidence collection is only reachable if the job survives long enough to run its `if: always()`
# steps. These assertions are the enforcement the governance document promises; without them the
# invariant survives only as a comment and the next added step silently breaks it.
$workflowPath = Join-Path $repoRoot '.github/workflows/ci.yml'
$ciJobs = Get-NervCiWorkflowBudgets -Path $workflowPath
$ciViolations = Test-NervCiWorkflowBudgets -Jobs $ciJobs
Assert-Equal 0 $ciViolations.Count "CI timeout-budget violations: $([string]::Join('; ', @($ciViolations | ForEach-Object { "$($_.code): $($_.message)" })))"

$evidenceJobs = @($ciJobs | Where-Object { @($_.Steps | Where-Object { $_.AlwaysRuns }).Count -gt 0 })
Assert-True ($evidenceJobs.Count -ge 6) "Expected at least six evidence-publishing CI jobs; found $($evidenceJobs.Count)."
# MAN-669 moved the backend evidence face off the single `backend-tests` job onto the four fast
# shards; `backend-tests` is now the test-free aggregate and publishes nothing. Naming the shards
# individually is deliberate — a count-only assertion would stay green if a shard silently stopped
# collecting, which is exactly the regression this list exists to catch.
foreach ($expectedEvidenceJob in @(
        'backend-tests-business-gateway',
        'backend-tests-platform',
        'backend-tests-business-core-a',
        'backend-tests-business-core-b',
        'connector-host-tests',
        'erp-sales-order-demand-acceptance')) {
    Assert-True (@($evidenceJobs | Where-Object Name -eq $expectedEvidenceJob).Count -eq 1) "Job '$expectedEvidenceJob' must still publish evidence under if: always()."
}

# The reader must actually be able to see a missing budget / an exceeded budget, otherwise "zero
# violations" above proves nothing. Each negative fixture is the real workflow shape with one
# deliberate defect.
$ciFixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) "man-799-ci-budgets-$([Guid]::NewGuid().ToString('N'))"
try {
    $null = New-Item -ItemType Directory -Path $ciFixtureRoot -Force
    $ciFixtures = @(
        @{
            Name = 'missing-job-timeout'
            Yaml = @'
jobs:
  sample:
    runs-on: ubuntu-latest
    steps:
      - name: Checkout
        timeout-minutes: 3
        uses: actions/checkout@v4
'@
        },
        @{
            Name = 'missing-step-timeout'
            Yaml = @'
jobs:
  sample:
    timeout-minutes: 20
    runs-on: ubuntu-latest
    steps:
      - name: Checkout
        uses: actions/checkout@v4
'@
        },
        @{
            Name = 'evidence-job-budget-not-above-step-sum'
            Yaml = @'
jobs:
  sample:
    timeout-minutes: 10
    runs-on: ubuntu-latest
    steps:
      - name: Test
        timeout-minutes: 8
        run: ./run.ps1
      - name: Upload evidence
        if: always()
        timeout-minutes: 5
        uses: actions/upload-artifact@v4
'@
        },
        @{
            Name = 'job-budget-not-above-largest-step'
            Yaml = @'
jobs:
  sample:
    timeout-minutes: 10
    runs-on: ubuntu-latest
    steps:
      - name: Build
        timeout-minutes: 10
        run: ./build.ps1
'@
        }
    )

    foreach ($ciFixture in $ciFixtures) {
        $fixturePath = Join-Path $ciFixtureRoot "$($ciFixture.Name).yml"
        Set-Content -Path $fixturePath -Value $ciFixture.Yaml -Encoding utf8
        $fixtureViolations = Test-NervCiWorkflowBudgets -Jobs (Get-NervCiWorkflowBudgets -Path $fixturePath)
        Assert-ViolationSet $fixtureViolations $ciFixture.Name
    }

    # Tier classification must fail *closed*. Matching only the literal `always()` demoted every
    # other legal spelling to tier B and silently switched off `evidence-job-budget-not-above-step-sum`
    # — the one rule this gate exists for. Each spelling below is the same 10m job with 13m of step
    # budget, so a job that is correctly recognized as evidence-publishing must report exactly that
    # violation and a genuinely success-gated `if:` must report none.
    $conditionCases = @(
        @{ Name = 'literal-always'; Condition = 'always()'; Evidence = $true },
        @{ Name = 'wrapped-always'; Condition = '${{ always() }}'; Evidence = $true },
        @{ Name = 'compound-always'; Condition = "always() && github.event_name == 'push'"; Evidence = $true },
        @{ Name = 'commented-always'; Condition = 'always() # keep the evidence on failure'; Evidence = $true },
        @{ Name = 'not-cancelled'; Condition = '!cancelled()'; Evidence = $true },
        @{ Name = 'wrapped-not-cancelled'; Condition = '${{ !cancelled() }}'; Evidence = $true },
        @{ Name = 'failure-only'; Condition = 'failure()'; Evidence = $true },
        @{ Name = 'unrecognized-function'; Condition = 'someFutureStatusFunction()'; Evidence = $true },
        @{ Name = 'success-gated'; Condition = "github.event_name == 'push'"; Evidence = $false },
        @{ Name = 'explicit-success'; Condition = "success() && contains(github.ref, 'main')"; Evidence = $false },
        @{ Name = 'comment-mentioning-always'; Condition = "github.event_name == 'push' # not always()"; Evidence = $false },
        # A `#` that belongs to the *value* must not be cut. YAML only starts a plain-scalar comment
        # at a whitespace-preceded `#`, so both spellings below are one legal condition whose
        # `always()` sits after the `#`. Cutting at the first `#` regardless (the naive
        # `-replace '#.*'`) leaves `contains(github.event.head_commit.message,` — status-neutral,
        # therefore tier B, therefore this gate's one rule switched off.
        @{ Name = 'single-quoted-hash'; Condition = "contains(github.event.head_commit.message, '# not a comment') && always()"; Evidence = $true },
        @{ Name = 'double-quoted-hash'; Condition = 'contains(github.event.head_commit.message, "# not a comment") && always()'; Evidence = $true }
    )

    foreach ($conditionCase in $conditionCases) {
        $conditionPath = Join-Path $ciFixtureRoot "condition-$($conditionCase.Name).yml"
        Set-Content -Path $conditionPath -Encoding utf8 -Value @"
jobs:
  sample:
    timeout-minutes: 10
    runs-on: ubuntu-latest
    steps:
      - name: Test
        timeout-minutes: 8
        run: ./run.ps1
      - name: Upload evidence
        if: $($conditionCase.Condition)
        timeout-minutes: 5
        uses: actions/upload-artifact@v4
"@
        $conditionJobs = Get-NervCiWorkflowBudgets -Path $conditionPath
        $detected = @($conditionJobs[0].Steps | Where-Object AlwaysRuns).Count -gt 0
        Assert-Equal $conditionCase.Evidence $detected "Condition '$($conditionCase.Condition)' was classified into the wrong tier."
        Assert-ViolationSet (Test-NervCiWorkflowBudgets -Jobs $conditionJobs) $(
            if ($conditionCase.Evidence) { @('evidence-job-budget-not-above-step-sum') } else { @() })
    }

    # An `if:` whose value continues on later lines cannot be classified from the step mapping the
    # structural reader sees, so it must land in the stricter tier rather than be assumed benign.
    # Every legal block-scalar header spelling has to be recognized: YAML permits the indentation
    # indicator and the chomping indicator in either order, and a header pattern that accepted only
    # chomping-then-digits let `>2-` through as an ordinary scalar and demoted the step to tier B.
    $blockHeaderIndex = 0
    foreach ($blockHeader in @('>', '|', '>-', '|+', '>2-', '|1+', '>-2')) {
        $blockHeaderIndex++
        $blockConditionPath = Join-Path $ciFixtureRoot "condition-block-scalar-$blockHeaderIndex.yml"
        Set-Content -Path $blockConditionPath -Encoding utf8 -Value @"
jobs:
  sample:
    timeout-minutes: 10
    runs-on: ubuntu-latest
    steps:
      - name: Test
        timeout-minutes: 8
        run: ./run.ps1
      - name: Upload evidence
        if: $blockHeader
          always()
        timeout-minutes: 5
        uses: actions/upload-artifact@v4
"@
        $blockJobs = Get-NervCiWorkflowBudgets -Path $blockConditionPath
        Assert-True (@($blockJobs[0].Steps | Where-Object AlwaysRuns).Count -gt 0) "Block-scalar header '$blockHeader' must classify its continued condition into the stricter tier."
        Assert-ViolationSet (Test-NervCiWorkflowBudgets -Jobs $blockJobs) 'evidence-job-budget-not-above-step-sum'
    }

    # Inline-comment stripping is the step between the raw `if:` text and tier classification, and
    # its whole reason to exist is that a `#` can belong to the value. Asserted directly because the
    # tier fixtures above cannot distinguish "kept the value" from "cut the value and happened to
    # land on the same tier": replacing this function with `-replace '#.*'` must turn these red.
    Assert-Equal "contains(github.event.head_commit.message, '# not a comment') && always()" (Remove-NervCiWorkflowInlineComment -Text "contains(github.event.head_commit.message, '# not a comment') && always()") 'A single-quoted `#` is part of the value, not a comment.'
    Assert-Equal 'contains(github.event.head_commit.message, "# not a comment") && always()' (Remove-NervCiWorkflowInlineComment -Text 'contains(github.event.head_commit.message, "# not a comment") && always()') 'A double-quoted `#` is part of the value, not a comment.'
    Assert-Equal "contains(github.event.head_commit.message, 'release # 1') && always()" (Remove-NervCiWorkflowInlineComment -Text "contains(github.event.head_commit.message, 'release # 1') && always()") 'A whitespace-preceded `#` inside a quoted run is still part of the value.'
    Assert-Equal 'github.ref == 1#2 && always()' (Remove-NervCiWorkflowInlineComment -Text 'github.ref == 1#2 && always()') 'A `#` glued to the previous character never starts a YAML comment.'
    Assert-Equal '"a \" # b" && always()' (Remove-NervCiWorkflowInlineComment -Text '"a \" # b" && always()') 'An escaped quote must not end the double-quoted run and reopen the rest to comment stripping.'
    Assert-Equal "'it''s # here' && always()" (Remove-NervCiWorkflowInlineComment -Text "'it''s # here' && always()") 'A doubled single quote is an escape, not the end of the single-quoted run.'
    # …and the stripping itself must still happen, or the function would trivially pass the above by
    # returning its input unchanged.
    Assert-Equal 'always()' (Remove-NervCiWorkflowInlineComment -Text 'always() # 真注释') 'A genuine trailing comment must still be removed.'
    Assert-Equal "github.event_name == 'push'" (Remove-NervCiWorkflowInlineComment -Text "github.event_name == 'push' # not always()") 'A trailing comment after a closed quoted run must still be removed.'
    Assert-Equal '' (Remove-NervCiWorkflowInlineComment -Text '# whole line is a comment') 'A leading `#` comments out the entire value.'

    # Job-level sequences (`needs:`, `strategy.matrix` shorthand) indent their items exactly like
    # step entries. Counting them as steps made the reader throw `step parse mismatch` on any
    # workflow that used them — a hard red naming a parse error instead of the real finding.
    $sequencePath = Join-Path $ciFixtureRoot 'job-level-sequences.yml'
    Set-Content -Path $sequencePath -Encoding utf8 -Value @'
jobs:
  build:
    timeout-minutes: 10
    runs-on: ubuntu-latest
    steps:
      - name: Build
        timeout-minutes: 5
        run: ./build.ps1
  publish:
    timeout-minutes: 10
    needs:
      - build
    strategy:
      matrix:
        shard:
          - 1
          - 2
    runs-on: ubuntu-latest
    steps:
      - name: Publish
        timeout-minutes: 5
        run: ./publish.ps1
'@
    $sequenceJobs = Get-NervCiWorkflowBudgets -Path $sequencePath
    Assert-Equal 2 @($sequenceJobs).Count 'Both jobs must be read.'
    Assert-Equal 2 (($sequenceJobs | ForEach-Object { $_.Steps.Count } | Measure-Object -Sum).Sum) 'Job-level sequence items must not be counted as steps.'
    Assert-ViolationSet (Test-NervCiWorkflowBudgets -Jobs $sequenceJobs) @()

    # A job header the reader cannot open must fail closed rather than be skipped: skipping it
    # merges that job's `steps:` and `timeout-minutes` into the previous job and certifies a budget
    # pairing that does not exist.
    $unreadableJobPath = Join-Path $ciFixtureRoot 'unreadable-job-header.yml'
    Set-Content -Path $unreadableJobPath -Encoding utf8 -Value @'
jobs:
  build:
    timeout-minutes: 10
    runs-on: ubuntu-latest
    steps:
      - name: Build
        timeout-minutes: 5
        run: ./build.ps1
  "quoted job name":
    timeout-minutes: 10
    runs-on: ubuntu-latest
    steps:
      - name: Publish
        timeout-minutes: 5
        run: ./publish.ps1
'@
    $misparseDetected = $false
    try { Get-NervCiWorkflowBudgets -Path $unreadableJobPath | Out-Null }
    catch { $misparseDetected = $_.Exception.Message.Contains('unreadable job header') }
    Assert-True $misparseDetected 'A job header the reader cannot open must fail closed.'

    # A tier-B job (no step that can run after a failure) is deliberately allowed to keep a job
    # budget below its step sum: there is no evidence to lose, so the budget tracks observed runtime
    # instead and is itself the fail-fast bound.
    $tierBPath = Join-Path $ciFixtureRoot 'tier-b-tight-budget.yml'
    Set-Content -Path $tierBPath -Encoding utf8 -Value @'
jobs:
  sample:
    timeout-minutes: 20
    runs-on: ubuntu-latest
    steps:
      - name: Install
        timeout-minutes: 12
        run: ./install.ps1
      - name: Build
        timeout-minutes: 15
        run: ./build.ps1
'@
    Assert-Equal 0 (Test-NervCiWorkflowBudgets -Jobs (Get-NervCiWorkflowBudgets -Path $tierBPath)).Count 'A job without evidence steps must not be forced above its step sum.'
}
finally {
    if (Test-Path $ciFixtureRoot) { Remove-Item $ciFixtureRoot -Recurse -Force }
}
Assert-True (-not (Test-Path $ciFixtureRoot)) 'CI budget fixtures must be cleaned up.'

$governanceDocPath = Join-Path $repoRoot 'docs/architecture/test-evidence-governance.md'
Assert-True (Test-Path $governanceDocPath) 'Test evidence governance document is missing.'
$governanceDoc = Get-Content $governanceDocPath -Raw
foreach ($requiredText in @(
    'optional', 'environment-gated', 'quarantined',
    'unregistered-skip', 'illegal-quarantine', 'zero-execution',
    'backend-shard-1', 'MAN-669', 'recovered-after-rerun', 'report-only',
    'continue-on-error', 'Nerv-IIP Platform CI/Test Governance', 'MAN-663',
    'selectedLaneResults', 'incompatible-granularity-or-duration-metric', 'single-lane collector',
    '2000-01-01T00:00:00Z', 'Actions job log',
    'pwsh scripts/generate-test-evidence-baseline.ps1 -EvidenceRoot artifacts/test-evidence -OutputPath scripts/test-evidence-baseline.json',
    'raw TRX', '30819675007', '91706113150', '9dafb512c992b240222c8d9b5ada43e4bfc8ac3d',
    # #1507. The operator contract has to keep saying which of the two things it governs, or the
    # boundary decays back into "one file, two purposes" the next time someone adds a gate.
    'Timing data is a cache, not a governed asset',
    'assembly-not-in-baseline', 'ambiguous-assembly-in-baseline', 'no-compatible-assembly',
    'timing-assembly-missing', 'timing-source-unavailable',
    'scripts/update-backend-test-shard-timings.ps1', 'scripts/report-backend-test-shard-balance.ps1',
    'There are no longer any mandatory refresh triggers'
)) {
    Assert-True ($governanceDoc.Contains($requiredText)) "Governance document is missing '$requiredText'."
}
foreach ($registeredScriptPath in @(
    'update-backend-test-shard-timings.ps1', 'report-backend-test-shard-balance.ps1', 'scripts/lib/BackendTestShardTimings.ps1'
)) {
    Assert-True ((Get-Content (Join-Path $repoRoot 'docs/architecture/script-automation-governance.md') -Raw).Contains($registeredScriptPath)) "Script governance registry is missing '$registeredScriptPath'."
}
$scriptGovernanceDoc = Get-Content (Join-Path $repoRoot 'docs/architecture/script-automation-governance.md') -Raw
foreach ($registeredPath in @('collect-test-evidence.ps1', 'generate-test-evidence-baseline.ps1', 'scripts/lib/TestEvidence.ps1', 'scripts/tests/test-evidence.Tests.ps1')) {
    Assert-True ($scriptGovernanceDoc.Contains($registeredPath)) "Script governance registry is missing '$registeredPath'."
}

# ---------------------------------------------------------------------------------------------
# Get-NervOrdinalRankedTop decides the *content and order* of summary.json's slowestAssemblies and
# slowestTests, which are retained artifacts. Until #1509 round 4 it had no behavioural coverage at
# all: reversing the comparison, dropping the rank tie-break, and removing the TopCount truncation
# each left the whole suite green. The ordinal sweep above can only prove the library contains no
# culture-aware *construct* — it says nothing about whether the ordering is right, which is exactly
# the failure class this issue exists to remove.
#
# One case pins all three at once. The input is fed in reverse, so an implementation that just
# returns its argument fails too:
#   - descending by metric      → 'Cherry' (9) must lead a field of 5s;
#   - ordinal tie-break         → the 5s must come out A00…A23 then 'apple', because ordinal puts
#                                 uppercase before lowercase (culture collation gives apple, A00…);
#   - deterministic tie-break   → 25 rows tied at 5 (A00…A23 plus 'apple') inside a 26-row list, past
#                                 the threshold where .NET's introsort stops being an insertion sort,
#                                 so `return 0` in the comparator really does reorder them rather
#                                 than accidentally staying stable;
#   - TopCount truncation       → a second call with -Count 2 must return exactly two rows.
#
# The threshold is 16: `List<T>.Sort` runs introsort, which insertion-sorts any partition of 16 or
# fewer elements, and insertion sort with a `return 0` comparator leaves the order untouched.
# Measured on pwsh 7.6.4 by sorting N identical rows with a constant-zero comparator — stable for
# every N ≤ 16, shuffled for every N in 17…34. 26 rows is the fixture size, so the margin is 10.
# That margin is *not* self-evident from reading the case, and shrinking the tie group is a plausible
# tidy-up: at 16 rows the `return 0` mutation passes and this case silently stops discriminating.
# The meta-assertion below fails outright in that situation rather than going quietly green.
$rankedTie = @(0..23 | ForEach-Object { [pscustomobject]@{ name = ('A{0:D2}' -f $_); elapsed = 5 } })
$rankedInput = @(@([pscustomobject]@{ name = 'apple'; elapsed = 5 }) + $rankedTie + @([pscustomobject]@{ name = 'Cherry'; elapsed = 9 }))
$rankedSortProbe = [Collections.Generic.List[object]]::new()
for ($rankedProbeIndex = 0; $rankedProbeIndex -lt $rankedInput.Count; $rankedProbeIndex++) {
    $rankedSortProbe.Add([pscustomobject]@{ Rank = $rankedProbeIndex })
}
$rankedProbeBefore = (@($rankedSortProbe | ForEach-Object { [string]$_.Rank }) -join ',')
$rankedSortProbe.Sort([Comparison[object]] { param($Left, $Right) return 0 })
$rankedProbeAfter = (@($rankedSortProbe | ForEach-Object { [string]$_.Rank }) -join ',')
Assert-True (-not [string]::Equals($rankedProbeBefore, $rankedProbeAfter, [StringComparison]::Ordinal)) `
    ("The ranked-tie fixture is $($rankedInput.Count) rows, which List<T>.Sort still insertion-sorts: a constant-zero comparator left the order unchanged, so the tie-break case below cannot detect that mutation. Grow the tie group back above the introsort threshold (16 on the measured runtime).")
[array]::Reverse($rankedInput)
$rankedAll = @(Get-NervOrdinalRankedTop -Items $rankedInput -Count 99 `
    -MetricSelector { param($row) [double]$row.elapsed } -TieBreakSelector { param($row) [string]$row.name })
$expectedRankedOrder = (@('Cherry') + @($rankedTie | ForEach-Object { [string]$_.name }) + @('apple')) -join '|'
$actualRankedOrder = (@($rankedAll | ForEach-Object { [string]$_.name })) -join '|'
Assert-True ([string]::Equals($actualRankedOrder, $expectedRankedOrder, [StringComparison]::Ordinal)) `
    "Get-NervOrdinalRankedTop must rank descending by metric with a deterministic ordinal tie-break.`nExpected: $expectedRankedOrder`nActual:   $actualRankedOrder"
$rankedTop2 = @(Get-NervOrdinalRankedTop -Items $rankedInput -Count 2 `
    -MetricSelector { param($row) [double]$row.elapsed } -TieBreakSelector { param($row) [string]$row.name })
Assert-True ([string]::Equals((@($rankedTop2 | ForEach-Object { [string]$_.name })) -join '|', 'Cherry|A00', [StringComparison]::Ordinal)) `
    "Get-NervOrdinalRankedTop must truncate to -Count; got: $(@($rankedTop2 | ForEach-Object { [string]$_.name }) -join ', ')"

# ---------------------------------------------------------------------------------------------
# Ordinal sweep contract (#1509 rounds 2 and 3). The file-wide sweep of scripts/lib/TestEvidence.ps1
# is only worth anything if it stays swept, and three prior rounds showed that a prose boundary
# ("the rest is out of scope") drifts from the code within one PR. So the boundary is parsed
# instead: every culture-aware identifier comparison in that library must either be gone or be a
# named, justified exception here.
#
# The scan itself lives in scripts/lib/OrdinalComparisonContract.ps1, because
# scripts/tests/backend-test-shards.Tests.ps1 makes the same claim about a different library and a
# claim asserted by two hand-written scanners is two different claims. What that scan can and cannot
# see is enumerated by the library and pinned below against synthetic sources, so "a contract exists"
# can never be read as "the semantics are protected" — round 3 found the previous version blind to
# `switch`, to `Sort-Object` without `-Unique`, to `Select-Object -Unique`, to `.StartsWith`/
# `.EndsWith` without a [StringComparison], and to a written-out `[StringComparison]::CurrentCulture`.
. (Join-Path $repoRoot 'scripts/lib/OrdinalComparisonContract.ps1')

# Named exceptions, each identified by *where* it is and *what* it is: the enclosing function plus
# the offending node's exact extent text. Text rather than line number, because a line number is
# invalidated by any edit above it and then silently exempts something else; exact text rather than
# a substring, because a substring exception can be widened from one expression to a bare cmdlet
# name and quietly absorb an unrelated future call site. Site as well as text, because moving the
# expression into another function has to be re-reviewed.
$ordinalSweepExceptions = @(
    @{
        Site = 'New-NervTestEvidenceSummary'
        Text = "Group-Object { Get-NervRetainedSkipReason `$_ }"
        Reason = 'skipReasons groups on a human-readable skip reason, which is prose, not an identifier; folding two visually identical reasons into one reported row is the intended reading. Only the grouping — the row ordering next to it is ordinal.'
    }
)

# The exception table's identity, asserted verbatim. This is what replaces the old
# `$ordinalSweepExceptions.Count -eq 1` (#1509 round 3): a count cannot tell *which* exception it is
# counting, so the table could be held at one row while that row was broadened from one expression
# to the whole `Group-Object` cmdlet — after which a genuinely new culture-aware grouping passes the
# sweep with the count still equal to 1. Under this assertion any add, removal or edit — including
# widening the Text — is a diff against a named contract.
$expectedOrdinalSweepIdentity = @(
    "New-NervTestEvidenceSummary::Group-Object { Get-NervRetainedSkipReason `$_ }::skipReasons groups on a human-readable skip reason, which is prose, not an identifier; folding two visually identical reasons into one reported row is the intended reading. Only the grouping — the row ordering next to it is ordinal."
) -join "`n"
$actualOrdinalSweepIdentity = (@($ordinalSweepExceptions | ForEach-Object {
    "$([string]$_.Site)::$([string]$_.Text)::$([string]$_.Reason)"
}) -join "`n")
Assert-True ([string]::Equals($actualOrdinalSweepIdentity, $expectedOrdinalSweepIdentity, [StringComparison]::Ordinal)) `
    "The ordinal sweep exception table changed. Expected:`n$expectedOrdinalSweepIdentity`nActual:`n$actualOrdinalSweepIdentity"

# The scan face must stay callable from any session state (#1509 round 3, second CI-only defect).
# The first version built its helpers as `{ … }.GetNewClosure()` and read its tables through
# `$script:` variables; both bind to the session state captured when the library was dot-sourced, and
# on the CI runner's PowerShell that binding could no longer see the library's own sibling functions
# — `Get-NervOrdinalContractEnclosingSite` came back "not recognized" while the identical file ran
# green locally on 7.6.4 under both `-File` and CI's `-command ". '…'"` form. Since the divergence is
# not reproducible here, the invariant is asserted structurally instead of behaviourally: this
# library binds nothing to a captured scope.
# Scoped to these three files on purpose. `$script:` state and closures work in this repository —
# FullStackSessionState.ps1, BackendTestShardTimings.ps1, ScriptAutomation.ps1 and
# LeaderDemoTelemetrySimulator.ps1 all depend on them — so this is a ban on the construct in the
# files that were measured failing, not a claim that the construct is broken. The root cause is
# still unidentified, which is registered as a follow-up on issue #1509: what this guard pins is a
# construct, not yet the real invariant.
# The ban covers the library *and* the two contract tests that consume it, because the second CI red
# was the test's own probe helper, not the library — the same construct, one file over.
foreach ($capturedScopeCandidate in @(
    'scripts/lib/OrdinalComparisonContract.ps1',
    'scripts/tests/test-evidence.Tests.ps1',
    'scripts/tests/backend-test-shards.Tests.ps1'
)) {
    $capturedScopeAst = [System.Management.Automation.Language.Parser]::ParseFile(
        (Join-Path $repoRoot $capturedScopeCandidate), [ref] $null, [ref] $null)
    $capturedScopeUses = @(
        @($capturedScopeAst.FindAll({
            param($node)
            $node -is [System.Management.Automation.Language.InvokeMemberExpressionAst] -and
            [string]::Equals([string] $node.Member.Value, 'GetNewClosure', [StringComparison]::OrdinalIgnoreCase)
        }, $true) | ForEach-Object { "GetNewClosure at line $($_.Extent.StartLineNumber)" })
        @($capturedScopeAst.FindAll({
            param($node)
            $node -is [System.Management.Automation.Language.VariableExpressionAst] -and
            ([string] $node.VariablePath.UserPath).StartsWith('script:', [StringComparison]::OrdinalIgnoreCase)
        }, $true) | ForEach-Object { "script-scope variable $($_.Extent.Text) at line $($_.Extent.StartLineNumber)" })
    )
    Assert-True ($capturedScopeUses.Count -eq 0) `
        "$capturedScopeCandidate must not bind the ordinal sweep to a captured session state: $(@($capturedScopeUses) -join '; ')"
}

$evidenceLibraryPath = Join-Path $repoRoot 'scripts/lib/TestEvidence.ps1'
$evidenceSweep = Get-NervOrdinalComparisonFindings -ScriptPath $evidenceLibraryPath -Exceptions $ordinalSweepExceptions -DisplayName 'TestEvidence.ps1'
Assert-True ($evidenceSweep.Findings.Count -eq 0) "scripts/lib/TestEvidence.ps1 must compare identifiers ordinally (#1509):`n  $(@($evidenceSweep.Findings) -join "`n  ")"
# Every exception must be earning its keep: a dead entry is a licence nobody is using, and an entry
# hit more than once is exempting call sites the reviewer of the original never saw.
foreach ($exceptionKey in @($evidenceSweep.ExceptionHits.Keys)) {
    Assert-True ([int]$evidenceSweep.ExceptionHits[$exceptionKey] -eq 1) `
        "Ordinal sweep exception '$exceptionKey' matched $($evidenceSweep.ExceptionHits[$exceptionKey]) call sites; it must match exactly one."
}

# ---------------------------------------------------------------------------------------------
# Discrimination for the scan face itself. Without this the sweep passing means only "no findings",
# which is equally true of a scanner that reports nothing. Each covered axis gets a source that must
# be reported; each documented blind spot gets a source that must *not* be, which is what stops the
# blind-spot list in docs/architecture/script-automation-governance.md from being aspirational.
$sweepProbeRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("nerv-iip-ordinal-sweep-probe-{0}" -f [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $sweepProbeRoot -Force | Out-Null
try {
    $sweepProbePath = Join-Path $sweepProbeRoot 'probe.ps1'
    # A plain function, not a `{ … }.GetNewClosure()` helper — the same CI-only defect this file
    # asserts against for the library itself (#1509 round 3): a closure carries the session state it
    # captured, and on the CI runner's PowerShell that state could not resolve the dot-sourced
    # Get-NervOrdinalComparisonFindings, while the identical file ran green locally on 7.6.4 under
    # both invocation forms. The structural guard above bans the construct in the library; this is
    # the same discipline applied to the test's own helper.
    function Invoke-NervSweepProbe {
        param(
            [Parameter(Mandatory)] [string] $ProbePath,
            [Parameter(Mandatory)] [AllowEmptyString()] [string] $Source
        )

        [System.IO.File]::WriteAllText($ProbePath, $Source, [System.Text.UTF8Encoding]::new($false))
        return @((Get-NervOrdinalComparisonFindings -ScriptPath $ProbePath -DisplayName 'probe.ps1').Findings)
    }

    $coveredProbes = [ordered]@{
        'banned-c-operator' = '$result = $left -ceq $right'
        'culture-operator-with-string-literal' = '$result = $left -eq ''passed'''
        'sort-object' = '$result = @($items | Sort-Object Name)'
        'group-object' = '$result = @($items | Group-Object lane)'
        'compare-object' = '$result = Compare-Object $left $right'
        'select-object-unique' = '$result = @($items | Select-Object -Unique)'
        'where-object-comparison-switch' = '$result = @($items | Where-Object outcome -eq ''passed'')'
        'switch-statement-string-clause' = 'switch ($outcome) { ''passed'' { 1 } default { 0 } }'
        'string-method-without-ordinal-comparison' = '$result = $text.StartsWith(''^'')'
        'ambiguous-method-with-string-literal' = '$result = $text.Contains(''SKIP'')'
        'non-ordinal-stringcomparison' = '$result = [string]::Equals($left, $right, [StringComparison]::CurrentCulture)'
    }
    $declaredAxes = @(Get-NervOrdinalContractCoveredAxes)
    Assert-True ([string]::Equals((@($coveredProbes.Keys) -join '|'), ($declaredAxes -join '|'), [StringComparison]::Ordinal)) `
        "Get-NervOrdinalContractCoveredAxes and the discrimination probes must enumerate the same axes; declared [$($declaredAxes -join ', ')], probed [$(@($coveredProbes.Keys) -join ', ')]."
    foreach ($axis in $coveredProbes.Keys) {
        $probeFindings = @(Invoke-NervSweepProbe -ProbePath $sweepProbePath -Source $coveredProbes[$axis])
        Assert-True (@($probeFindings | Where-Object { $_.StartsWith("[$axis]", [StringComparison]::Ordinal) }).Count -ge 1) `
            "The ordinal sweep must report axis '$axis' for: $($coveredProbes[$axis])"
    }

    # The blind spots, asserted as blind spots. Each of these is a real culture-aware comparison the
    # scan cannot distinguish from a safe one; the assertion is that the scan stays silent, so the day
    # any of them becomes detectable this list and the documentation have to be edited together.
    $blindSpotProbes = [ordered]@{
        'both-operands-non-literal-eq' = '$result = $left -eq $right'
        'both-operands-non-literal-in' = '$result = $candidate -in $known'
        'ambiguous-method-with-variable-argument' = '$result = $text.Contains($needle)'
        'like-and-match-operators' = '$result = ($text -like $pattern) -and ($text -match $expression)'
        'validateset-attribute' = 'function Invoke-Probe { param([ValidateSet(''all'', ''class'')] [string] $Kind) return $Kind }'
        'sort-object-via-splatted-parameters' = '$splat = @{ Property = ''name'' }; $result = @($items | Sort-Object @splat | Select-Object @splat)'
    }
    $declaredBlindSpots = @(Get-NervOrdinalContractBlindSpots)
    Assert-True ([string]::Equals((@($blindSpotProbes.Keys) -join '|'), ($declaredBlindSpots -join '|'), [StringComparison]::Ordinal)) `
        "Get-NervOrdinalContractBlindSpots and the blind-spot probes must enumerate the same list; declared [$($declaredBlindSpots -join ', ')], probed [$(@($blindSpotProbes.Keys) -join ', ')]."
    foreach ($blindSpot in $blindSpotProbes.Keys) {
        # The assertion is "this probe produces no finding the list does not already account for" —
        # deliberately *not* "no finding on a declared axis". The earlier spelling checked each
        # finding against Get-NervOrdinalContractCoveredAxes, so a new detection under a new axis
        # name that nobody had registered yet was invisible to it: measured in #1509 round 4 by
        # teaching the scan to report `[ValidateSet]` without registering the axis — every test
        # stayed green, and the list was not forced to change. Anchoring on the *permitted* findings
        # instead makes the list live in both directions: gaining detection turns it red whether or
        # not the new axis is declared, and losing the one permitted detection turns it red too.
        #
        # `Sort-Object @splat` is the one probe that is legitimately reported: the scan sees the
        # Sort-Object call, what it cannot see is *which* properties are compared, so the splat is a
        # blind spot for the Select-Object -Unique axis only. Stated as the exact permitted set
        # rather than rounded off in either direction.
        $permittedFindingAxes = if ([string]::Equals($blindSpot, 'sort-object-via-splatted-parameters', [StringComparison]::Ordinal)) {
            @('sort-object')
        }
        else { @() }
        $probeFindings = @(Invoke-NervSweepProbe -ProbePath $sweepProbePath -Source $blindSpotProbes[$blindSpot])
        $unexpected = @($probeFindings | Where-Object {
            $finding = $_
            @($permittedFindingAxes | Where-Object { $finding.StartsWith("[$_]", [StringComparison]::Ordinal) }).Count -eq 0
        })
        Assert-True ($unexpected.Count -eq 0) `
            "Blind spot '$blindSpot' is no longer a blind spot: $($unexpected -join '; '). Update Get-NervOrdinalContractBlindSpots and the governance document together."
        foreach ($permittedAxis in $permittedFindingAxes) {
            Assert-True (@($probeFindings | Where-Object { $_.StartsWith("[$permittedAxis]", [StringComparison]::Ordinal) }).Count -ge 1) `
                "Blind spot '$blindSpot' is documented as still being reported on axis '$permittedAxis'; it no longer is, so the documented boundary is wrong."
        }
    }

    # The anti-widening case for the exception table, run against the real table (#1509 round 3, M2).
    # A source carrying both the exempt expression and an unrelated Group-Object must report exactly
    # the unrelated one: if the exception ever matched on a substring, or on the cmdlet name, the
    # second call site would be swallowed here and nowhere else.
    $exemptionProbeSource = @'
function New-NervTestEvidenceSummary {
    $exempt = @($records | Group-Object { Get-NervRetainedSkipReason $_ })
    $smuggled = @($records | Group-Object lane)
    return @($exempt, $smuggled)
}
'@
    [System.IO.File]::WriteAllText($sweepProbePath, $exemptionProbeSource, [System.Text.UTF8Encoding]::new($false))
    $exemptionProbe = Get-NervOrdinalComparisonFindings -ScriptPath $sweepProbePath -Exceptions $ordinalSweepExceptions -DisplayName 'probe.ps1'
    Assert-True ($exemptionProbe.Findings.Count -eq 1) `
        "The named exception must exempt exactly its own expression; findings: $(@($exemptionProbe.Findings) -join '; ')"
    Assert-True ($exemptionProbe.Findings[0].Contains('Group-Object lane', [StringComparison]::Ordinal)) `
        "The unrelated Group-Object call must survive the exception; got: $($exemptionProbe.Findings[0])"

    # …and the same expression in a different function is not the exempted one.
    $misplacedExemptionSource = @'
function Get-NervSomethingElse {
    return @($records | Group-Object { Get-NervRetainedSkipReason $_ })
}
'@
    [System.IO.File]::WriteAllText($sweepProbePath, $misplacedExemptionSource, [System.Text.UTF8Encoding]::new($false))
    $misplacedProbe = Get-NervOrdinalComparisonFindings -ScriptPath $sweepProbePath -Exceptions $ordinalSweepExceptions -DisplayName 'probe.ps1'
    Assert-True ($misplacedProbe.Findings.Count -eq 1) `
        'An exception is bound to its site; the same expression in another function must still be reported.'
}
finally {
    if (Test-Path -LiteralPath $sweepProbeRoot) {
        Remove-Item -LiteralPath $sweepProbeRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}

$newRepoCommandLogs = @(
    if (Test-Path $repoScriptLogRoot) {
        Get-ChildItem $repoScriptLogRoot -Directory -Filter 'man-661-*' |
            ForEach-Object { Get-ChildItem $_.FullName -Directory | ForEach-Object FullName } |
            Where-Object { $initialRepoCommandLogs -cnotcontains $_ }
    }
)
Assert-Equal 0 $newRepoCommandLogs.Count 'Focused evidence suite must keep owned command logs under its temporary fixture roots.'
Assert-True (-not (Test-Path $invalidBaselineRoot)) 'Focused evidence suite must clean its baseline fixture and command-log root in finally.'
Assert-True (-not (Test-Path $collectorRoot)) 'Focused evidence suite must clean its collector fixture and command-log root in finally.'

Write-Host "PASS: MAN-661 policy schema; registered source assignments=$($liveAssignments.Count)."
