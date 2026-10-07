# Script-Governance:
#   Category: check
#   SideEffects:
#     - Validates the canonical output and real WMS helper contracts
#   Writes:
#     - Temporary canonical and diagnostic fixtures under the operating-system temp directory
#   Cleanup:
#     - Removes owned temporary fixtures in finally
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$canonicalLibraryPath = Join-Path $repoRoot 'scripts/lib/AcceptanceCanonicalResult.ps1'
$wmsVerifierPath = Join-Path $repoRoot 'scripts/verify-erp-wms-delivery-completion.ps1'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-acceptance-canonical-$([Guid]::NewGuid().ToString('N'))"

if (-not (Test-Path -LiteralPath $canonicalLibraryPath -PathType Leaf)) {
    throw "Acceptance scenario matrix canonical library is missing at '$canonicalLibraryPath'."
}
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')
. $canonicalLibraryPath

function Assert-Contract([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Copy-JsonObject {
    param([Parameter(Mandatory)] [object] $Value)

    return ($Value | ConvertTo-Json -Depth 50 | ConvertFrom-Json -Depth 50 -DateKind String)
}

function New-WmsCanonicalFixture {
    param([string] $Track, [string] $VolatileMarker)

    return [pscustomobject][ordered]@{
        schemaVersion = 1
        provenance = [pscustomobject][ordered]@{
            repository = 'Mang-X/Nerv-IIP'
            runId = '123456789'
            runAttempt = 2
            testedSha = '0123456789abcdef0123456789abcdef01234567'
            manifestDigest = 'a' * 64
            scenarioId = 'wms-delivery-erp'
        }
        track = $Track
        conclusion = 'passed'
        test = [pscustomobject][ordered]@{
            identity = 'Nerv.IIP.Business.FullChain.Tests.ErpWmsDeliveryCompletionPostgresRedisAcceptanceTests.External_process_replays_completed_wms_event_without_duplicate_delivery_or_receivable_facts'
            expected = 1
            discovered = 1
            passed = 1
            failed = 0
            skipped = 0
        }
        businessFacts = [pscustomobject][ordered]@{
            outboundAssigned = $true
            pickingLifecycleCompleted = $true
            outboundCompleted = $true
            deliveryCompleted = $true
            receivableCreated = $true
            completionReplayConverged = $true
            repeatedEventConverged = $true
        }
        diagnostics = [pscustomobject][ordered]@{
            schemas = @('erp', 'inventory', 'wms')
            failureCaptureSupported = $true
            failureDiagnosticsCaptured = $false
            secretsRedacted = $true
        }
        cleanup = [pscustomobject][ordered]@{
            managedProcessesRemaining = 0
            disposableDatabasesRemaining = 0
            ownedResourcesRemaining = 0
            errorCodes = @()
        }
        volatile = [pscustomobject][ordered]@{
            databaseName = "db-$VolatileMarker"
            processIds = @(701, 702, 703)
            capSuffix = "cap-$VolatileMarker"
            startedAtUtc = '2026-08-24T00:00:00.0000000+00:00'
            completedAtUtc = '2026-08-24T00:01:00.0000000+00:00'
            cleanupErrors = @()
            ports = [pscustomobject][ordered]@{ erp = 42001; wms = 42002; inventory = 42003 }
            paths = [pscustomobject][ordered]@{
                businessEvidence = "/tmp/$VolatileMarker/evidence.json"
                probeTrx = "/tmp/$VolatileMarker/probe.trx"
                cleanupEvidence = "/tmp/$VolatileMarker/evidence.json"
                canonicalResult = "/tmp/$VolatileMarker/result.json"
            }
        }
    }
}


try {
    [IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
    $wmsDiagnosticSecret = 'man527-secret-token'
    $protectedWmsDiagnostic = Protect-NervAcceptanceWmsDiagnosticText `
        -Text "Authorization: Bearer $wmsDiagnosticSecret; Password=database-secret; endpoint-secret" `
        -SensitiveValues @($wmsDiagnosticSecret, 'database-secret', 'endpoint-secret')
    Assert-Contract (-not $protectedWmsDiagnostic.Contains($wmsDiagnosticSecret, [StringComparison]::Ordinal) -and
        -not $protectedWmsDiagnostic.Contains('database-secret', [StringComparison]::Ordinal) -and
        -not $protectedWmsDiagnostic.Contains('endpoint-secret', [StringComparison]::Ordinal)) 'MAN-527 diagnostic redaction must remove every caller-declared sensitive value.'
    Assert-Contract ($protectedWmsDiagnostic.Contains('<redacted>', [StringComparison]::Ordinal)) 'MAN-527 diagnostic redaction must retain an explicit redaction marker.'
    $wmsCanonicalResult = New-WmsCanonicalFixture -Track authority -VolatileMarker wms-authority
    $wmsBusinessEvidence = [pscustomobject][ordered]@{
        scenarioStatus = 'passed'
        deliveryOrderNo = 'DO-MAN527-1234ABCD'
        wmsOutboundOrder = [pscustomobject][ordered]@{
            firstAssignment = [pscustomobject][ordered]@{ poolCode = 'POOL-MAN527-SHIPPING-1234ABCD'; operatorPrincipalId = 'man527-operator-1234abcd' }
            pickingLifecycle = 'public create/read/assign/start/progress/complete for every outbound line'
            pickingLifecycleCompleted = $true
            completionHttpReplay = 'same idempotency key accepted twice'
            completionHttpReplayConverged = $true
            completionReadback = [pscustomobject][ordered]@{ status = 'Completed'; completedAtUtc = '2026-08-24T00:00:30Z' }
        }
        erpDelivery = [pscustomobject][ordered]@{ status = 'completed'; shippedQuantity = 2; shippedAtUtc = '2026-08-24T00:00:00Z'; completedAtUtc = '2026-08-24T00:01:00Z' }
        accountReceivable = [pscustomobject][ordered]@{ receivableNo = 'AR-001'; sourceDocumentNo = 'DO-MAN527-1234ABCD' }
        repeatedEvent = 'same event id published twice through Redis; one delivery projection, one receivable, one target-consumer durable inbox row, no target-consumer dead letter'
        repeatedEventConverged = $true
    }
    $wmsCounters = [pscustomobject][ordered]@{ total = 1; executed = 1; passed = 1; failed = 0; skipped = 0 }
    $wmsCleanup = [pscustomobject][ordered]@{ managedProcessRemaining = 0; exactDatabaseRemaining = 0; postgres = 'owned-stopped'; redis = 'owned-stopped'; errors = @() }
    $wmsDiagnostics = [pscustomobject][ordered]@{ failureCaptureSupported = $true; failureDiagnosticsCaptured = $false; secretsRedacted = $true; artifactPaths = @(); errors = @() }
    $wmsVolatile = [pscustomobject][ordered]@{
        databaseName = 'man527_1234567890abcdef1234567890abcdef'
        processIds = @(701, 702, 703)
        capSuffix = 'man527-123456789abc'
        startedAtUtc = '2026-08-24T00:00:00Z'
        completedAtUtc = '2026-08-24T00:01:00Z'
        ports = [pscustomobject][ordered]@{ erp = 42001; wms = 42002; inventory = 42003 }
        paths = [pscustomobject][ordered]@{ businessEvidence = '/tmp/evidence.json'; probeTrx = '/tmp/probe.trx'; cleanupEvidence = '/tmp/evidence.json'; canonicalResult = '/tmp/result.json' }
    }
    $wmsBuiltCanonical = New-NervAcceptanceWmsDeliveryCanonicalResult -Provenance $wmsCanonicalResult.provenance -Track authority -BusinessEvidence $wmsBusinessEvidence -TestCounters $wmsCounters -CleanupEvidence $wmsCleanup -DiagnosticEvidence $wmsDiagnostics -Volatile $wmsVolatile
    Assert-Contract ([string]::Equals([string]$wmsBuiltCanonical.provenance.scenarioId, 'wms-delivery-erp', [StringComparison]::Ordinal) -and $wmsBuiltCanonical.businessFacts.outboundCompleted -and $wmsBuiltCanonical.businessFacts.repeatedEventConverged) 'The MAN-527 adapter must construct the WMS canonical result from WMS completion evidence, exact TRX counters, and cleanup readback.'
    foreach ($wmsInputMutation in @(
        @{ Name = 'bad-provenance'; Message = 'runId must be a positive'; Provenance = { param($value) $value.runId = '01' } },
        @{ Name = 'missing-business-evidence'; Message = 'missing required field'; Business = { param($value) $value.PSObject.Properties.Remove('accountReceivable') } },
        @{ Name = 'empty-assignment'; Message = 'every business checkpoint'; Business = { param($value) $value.wmsOutboundOrder.firstAssignment.poolCode = '' } },
        @{ Name = 'picking-not-completed'; Message = 'every business checkpoint'; Business = { param($value) $value.wmsOutboundOrder.pickingLifecycleCompleted = $false } },
        @{ Name = 'missing-completion-readback'; Message = 'completionReadback'; Business = { param($value) $value.wmsOutboundOrder.PSObject.Properties.Remove('completionReadback') } },
        @{ Name = 'outbound-pending'; Message = 'every business checkpoint'; Business = { param($value) $value.wmsOutboundOrder.completionReadback.status = 'Pending' } },
        @{ Name = 'outbound-completion-time-missing'; Message = 'every business checkpoint'; Business = { param($value) $value.wmsOutboundOrder.completionReadback.completedAtUtc = '' } },
        @{ Name = 'delivery-pending'; Message = 'every business checkpoint'; Business = { param($value) $value.erpDelivery.status = 'pending' } },
        @{ Name = 'completion-replay-not-converged'; Message = 'every business checkpoint'; Business = { param($value) $value.wmsOutboundOrder.completionHttpReplayConverged = $false } },
        @{ Name = 'repeated-event-not-converged'; Message = 'every business checkpoint'; Business = { param($value) $value.repeatedEventConverged = $false } },
        @{ Name = 'business-checkpoint-string-false'; Message = 'business checkpoint flags must be JSON booleans'; Business = { param($value) $value.wmsOutboundOrder.pickingLifecycleCompleted = 'false' } },
        @{ Name = 'extra-test-identity'; Message = 'exact TRX counts'; Counters = { param($value) $value.total = 2; $value.executed = 2; $value.passed = 2 } },
        @{ Name = 'zero-execution'; Message = 'exact TRX counts'; Counters = { param($value) $value.total = 0; $value.executed = 0; $value.passed = 0 } },
        @{ Name = 'managed-process-residue'; Message = 'zero cleanup remaining'; Cleanup = { param($value) $value.managedProcessRemaining = 1 } },
        @{ Name = 'database-residue'; Message = 'zero cleanup remaining'; Cleanup = { param($value) $value.exactDatabaseRemaining = 1 } },
        @{ Name = 'cleanup-error'; Message = 'zero cleanup remaining'; Cleanup = { param($value) $value.errors = @('stop-erp: still running') } },
        @{ Name = 'postgres-pending'; Message = 'zero cleanup remaining'; Cleanup = { param($value) $value.postgres = 'owned-pending-cleanup' } },
        @{ Name = 'redis-pending'; Message = 'zero cleanup remaining'; Cleanup = { param($value) $value.redis = 'owned-pending-cleanup' } },
        @{ Name = 'diagnostic-capture-unsupported'; Message = 'diagnostic failure capture'; Diagnostics = { param($value) $value.failureCaptureSupported = $false } },
        @{ Name = 'success-with-failure-diagnostics'; Message = 'must not claim failure diagnostics'; Diagnostics = { param($value) $value.failureDiagnosticsCaptured = $true } },
        @{ Name = 'diagnostics-not-redacted'; Message = 'diagnostic secrets must be redacted'; Diagnostics = { param($value) $value.secretsRedacted = $false } },
        @{ Name = 'diagnostic-checkpoint-string-false'; Message = 'diagnostic evidence secretsRedacted must be a JSON boolean'; Diagnostics = { param($value) $value.secretsRedacted = 'false' } },
        @{ Name = 'success-with-diagnostic-artifact'; Message = 'must not retain failure diagnostic artifacts'; Diagnostics = { param($value) $value.artifactPaths = @('/tmp/failure-summary.json') } },
        @{ Name = 'diagnostic-capture-error'; Message = 'diagnostic capture errors must be empty'; Diagnostics = { param($value) $value.errors = @('capture-failed') } }
    )) {
        $mutatedProvenance = Copy-JsonObject $wmsCanonicalResult.provenance
        $mutatedBusiness = Copy-JsonObject $wmsBusinessEvidence
        $mutatedCounters = Copy-JsonObject $wmsCounters
        $mutatedCleanup = Copy-JsonObject $wmsCleanup
        $mutatedDiagnostics = Copy-JsonObject $wmsDiagnostics
        if ($null -ne $wmsInputMutation['Provenance']) { & $wmsInputMutation['Provenance'] $mutatedProvenance }
        if ($null -ne $wmsInputMutation['Business']) { & $wmsInputMutation['Business'] $mutatedBusiness }
        if ($null -ne $wmsInputMutation['Counters']) { & $wmsInputMutation['Counters'] $mutatedCounters }
        if ($null -ne $wmsInputMutation['Cleanup']) { & $wmsInputMutation['Cleanup'] $mutatedCleanup }
        if ($null -ne $wmsInputMutation['Diagnostics']) { & $wmsInputMutation['Diagnostics'] $mutatedDiagnostics }
        $mutationMessage = '<no exception>'
        try { New-NervAcceptanceWmsDeliveryCanonicalResult -Provenance $mutatedProvenance -Track authority -BusinessEvidence $mutatedBusiness -TestCounters $mutatedCounters -CleanupEvidence $mutatedCleanup -DiagnosticEvidence $mutatedDiagnostics -Volatile $wmsVolatile | Out-Null }
        catch { $mutationMessage = $_.Exception.Message }
        Assert-Contract ($mutationMessage.Contains([string]$wmsInputMutation.Message, [StringComparison]::Ordinal)) "WMS canonical input mutation '$($wmsInputMutation.Name)' must fail closed; observed '$mutationMessage'."
    }

    $validPickingReadbacks = @(
        [pscustomobject]@{ status = 'Completed'; plannedQuantity = 1; executedQuantity = 1; completedAtUtc = '2026-08-24T00:00:00Z' },
        [pscustomobject]@{ status = 'completed'; plannedQuantity = 2; executedQuantity = 2; completedAtUtc = '2026-08-24T00:00:01Z' }
    )
    Assert-Contract (Test-NervAcceptanceWmsPickingReadbacks -Readbacks $validPickingReadbacks -RequestedQuantities @(1, 2)) 'Completed public picking readbacks must satisfy the WMS picking checkpoint.'
    foreach ($pickingMutation in @(
        @{ Name = 'status'; Apply = { param($value) $value[0].status = 'InProgress' } },
        @{ Name = 'planned-quantity'; Apply = { param($value) $value[0].plannedQuantity = 2 } },
        @{ Name = 'requested-quantity'; Apply = { param($value) $value[0].plannedQuantity = 2; $value[0].executedQuantity = 2 } },
        @{ Name = 'completed-at'; Apply = { param($value) $value[0].completedAtUtc = '' } }
    )) {
        $mutatedPickingReadbacks = Copy-JsonObject $validPickingReadbacks
        & $pickingMutation.Apply $mutatedPickingReadbacks
        Assert-Contract (-not (Test-NervAcceptanceWmsPickingReadbacks -Readbacks $mutatedPickingReadbacks -RequestedQuantities @(1, 2))) "Picking readback mutation '$($pickingMutation.Name)' must fail closed."
    }
    Assert-Contract (-not (Test-NervAcceptanceWmsPickingReadbacks -Readbacks @($validPickingReadbacks[0]) -RequestedQuantities @(1, 2))) 'Missing public picking readbacks must fail closed.'

    $firstCompletionResponse = [pscustomobject]@{ data = [pscustomobject]@{ requestId = 'movement-request-1' } }
    $replayedCompletionResponse = [pscustomobject]@{ data = [pscustomobject]@{ requestId = 'movement-request-1' } }
    Assert-Contract (Test-NervAcceptanceWmsCompletionReplay -FirstCompletion $firstCompletionResponse -ReplayCompletion $replayedCompletionResponse) 'Matching non-empty completion requestIds must satisfy the WMS replay checkpoint.'
    foreach ($completionMutation in @(
        @{ Name = 'empty-first'; First = ''; Replay = 'movement-request-1' },
        @{ Name = 'empty-replay'; First = 'movement-request-1'; Replay = '' },
        @{ Name = 'both-empty'; First = ''; Replay = '' },
        @{ Name = 'ordinal-mismatch'; First = 'movement-request-1'; Replay = 'MOVEMENT-REQUEST-1' }
    )) {
        $mutatedFirstCompletion = [pscustomobject]@{ data = [pscustomobject]@{ requestId = $completionMutation.First } }
        $mutatedReplayCompletion = [pscustomobject]@{ data = [pscustomobject]@{ requestId = $completionMutation.Replay } }
        Assert-Contract (-not (Test-NervAcceptanceWmsCompletionReplay -FirstCompletion $mutatedFirstCompletion -ReplayCompletion $mutatedReplayCompletion)) "Completion replay mutation '$($completionMutation.Name)' must fail closed."
    }

    Assert-Contract (Test-NervAcceptanceWmsCompletedOutboundReadback -Readback ([pscustomobject]@{ status = 'Completed'; completedAtUtc = '2026-08-24T00:00:30Z' })) 'A public WMS outbound readback must prove both Completed status and a completion timestamp.'
    Assert-Contract (-not (Test-NervAcceptanceWmsCompletedOutboundReadback -Readback $null)) 'A null public WMS outbound readback must fail closed.'
    Assert-Contract (-not (Test-NervAcceptanceWmsCompletedOutboundReadback -Readback ([pscustomobject]@{ status = 'Pending'; completedAtUtc = '2026-08-24T00:00:30Z' }))) 'A pending public WMS outbound readback must not prove completion.'
    Assert-Contract (-not (Test-NervAcceptanceWmsCompletedOutboundReadback -Readback ([pscustomobject]@{ status = 'Completed'; completedAtUtc = '' }))) 'A completed public WMS outbound readback without completedAtUtc must fail closed.'

    $diagnosticArtifactPath = Join-Path $fixtureRoot 'wms-diagnostics/actual-evidence.json'
    $diagnosticSecret = 'wms-runtime-secret-123'
    $diagnosticWriteProof = Write-NervAcceptanceWmsDiagnosticArtifact `
        -Path $diagnosticArtifactPath `
        -Content "actual evidence Authorization: Bearer $diagnosticSecret" `
        -SensitiveValues @($diagnosticSecret)
    Assert-Contract ($diagnosticWriteProof.evidenceWritten -and $diagnosticWriteProof.secretsRedacted) 'The WMS diagnostic writer must earn capability flags from an actual persisted artifact and post-write scan.'
    $writtenDiagnosticContent = [IO.File]::ReadAllText($diagnosticArtifactPath)
    Assert-Contract (-not $writtenDiagnosticContent.Contains($diagnosticSecret, [StringComparison]::Ordinal)) 'The actual WMS diagnostic artifact must not retain the declared sensitive value.'
    $successfulDiagnosticEvidence = New-NervAcceptanceWmsSuccessfulDiagnosticEvidence -WriteProof $diagnosticWriteProof -FailureCaptureSupported $true
    Assert-Contract ($successfulDiagnosticEvidence.failureCaptureSupported -and $successfulDiagnosticEvidence.secretsRedacted -and -not $successfulDiagnosticEvidence.failureDiagnosticsCaptured) 'WMS canonical diagnostic capability must be derived from the actual artifact write proof.'
    $canonicalResultWithFactoryDiagnostics = New-NervAcceptanceWmsDeliveryCanonicalResult -Provenance $wmsCanonicalResult.provenance -Track authority -BusinessEvidence $wmsBusinessEvidence -TestCounters $wmsCounters -CleanupEvidence $wmsCleanup -DiagnosticEvidence $successfulDiagnosticEvidence -Volatile $wmsVolatile
    Assert-Contract $canonicalResultWithFactoryDiagnostics.diagnostics.failureCaptureSupported 'The successful diagnostic factory output must feed the canonical builder directly.'
    foreach ($invalidProofCase in @(
        @{ Proof = [pscustomobject]@{ artifactPath = $diagnosticArtifactPath; evidenceWritten = $false; secretsRedacted = $true }; Message = 'actual persisted artifact' },
        @{ Proof = [pscustomobject]@{ artifactPath = $diagnosticArtifactPath; evidenceWritten = $true; secretsRedacted = $false }; Message = 'actual persisted artifact' },
        @{ Proof = [pscustomobject]@{ artifactPath = $diagnosticArtifactPath; evidenceWritten = 'true'; secretsRedacted = $true }; Message = 'actual persisted artifact' },
        @{ Proof = [pscustomobject]@{ evidenceWritten = $true; secretsRedacted = $true }; Message = 'missing required field' }
    )) {
        $invalidProofRejected = $false
        try { New-NervAcceptanceWmsSuccessfulDiagnosticEvidence -WriteProof $invalidProofCase.Proof -FailureCaptureSupported $true | Out-Null }
        catch { $invalidProofRejected = $_.Exception.Message.Contains([string]$invalidProofCase.Message, [StringComparison]::Ordinal) }
        Assert-Contract $invalidProofRejected 'WMS diagnostic capability must reject missing, false, or non-boolean artifact write proof fields.'
    }
    foreach ($invalidFailureCaptureSupport in @($false, 'true')) {
        $invalidFailureCaptureRejected = $false
        try { New-NervAcceptanceWmsSuccessfulDiagnosticEvidence -WriteProof $diagnosticWriteProof -FailureCaptureSupported $invalidFailureCaptureSupport | Out-Null }
        catch { $invalidFailureCaptureRejected = $_.Exception.Message.Contains('failure-capture contract', [StringComparison]::Ordinal) }
        Assert-Contract $invalidFailureCaptureRejected 'WMS successful diagnostics must reject false or non-boolean failure-capture contract proof.'
    }

    $unannouncedSecretArtifactPath = Join-Path $fixtureRoot 'wms-diagnostics/unannounced-secret.json'
    [IO.File]::WriteAllText($unannouncedSecretArtifactPath, '{"authorization":"Bearer unannounced-secret-987"}', [Text.UTF8Encoding]::new($false))
    $unannouncedSecretRejected = $false
    try { Assert-NervAcceptanceWmsDiagnosticArtifactRedacted -Path $unannouncedSecretArtifactPath -SensitiveValues @() | Out-Null }
    catch { $unannouncedSecretRejected = $_.Exception.Message.Contains('secret pattern', [StringComparison]::Ordinal) }
    Assert-Contract ($unannouncedSecretRejected -and -not (Test-Path -LiteralPath $unannouncedSecretArtifactPath)) 'The independent diagnostic scanner must reject and remove an unpublished artifact containing an undeclared bearer secret.'
    $unannouncedPasswordArtifactPath = Join-Path $fixtureRoot 'wms-diagnostics/unannounced-password.json'
    [IO.File]::WriteAllText($unannouncedPasswordArtifactPath, 'Password=unannounced-password-321', [Text.UTF8Encoding]::new($false))
    $unannouncedPasswordRejected = $false
    try { Assert-NervAcceptanceWmsDiagnosticArtifactRedacted -Path $unannouncedPasswordArtifactPath -SensitiveValues @() | Out-Null }
    catch { $unannouncedPasswordRejected = $_.Exception.Message.Contains('secret pattern', [StringComparison]::Ordinal) }
    Assert-Contract ($unannouncedPasswordRejected -and -not (Test-Path -LiteralPath $unannouncedPasswordArtifactPath)) 'The independent diagnostic scanner must reject and remove an unpublished artifact containing an undeclared password field.'
    $atomicDiagnosticPath = Join-Path $fixtureRoot 'wms-diagnostics/atomic-evidence.json'
    [IO.File]::WriteAllText($atomicDiagnosticPath, '{"status":"previous-safe-evidence"}', [Text.UTF8Encoding]::new($false))
    $unannouncedUserInfoRejected = $false
    try { Write-NervAcceptanceWmsDiagnosticArtifact -Path $atomicDiagnosticPath -Content 'postgres://dbuser:unannounced-secret-654@localhost/db' -SensitiveValues @() | Out-Null }
    catch { $unannouncedUserInfoRejected = $_.Exception.Message.Contains('secret pattern', [StringComparison]::Ordinal) }
    $remainingDiagnosticTemps = @(Get-ChildItem -LiteralPath (Split-Path -Parent $atomicDiagnosticPath) -Filter '*.tmp' -File)
    Assert-Contract ($unannouncedUserInfoRejected -and [IO.File]::ReadAllText($atomicDiagnosticPath).Contains('previous-safe-evidence', [StringComparison]::Ordinal) -and $remainingDiagnosticTemps.Count -eq 0) 'The atomic diagnostic writer must reject an undeclared URI userinfo secret without replacing prior safe evidence or retaining its unpublished temporary artifact.'

    $verifierContract = Test-NervAcceptanceWmsVerifierContract -Path $wmsVerifierPath
    Assert-Contract ($verifierContract.failureCaptureSupported -and $verifierContract.pickingReadbackWired -and $verifierContract.completionReplayWired -and $verifierContract.outboundCompletionWired) 'The MAN-527 verifier must wire reachable failure capture and public WMS checkpoint predicates.'
    $wmsVerifierSource = [IO.File]::ReadAllText($wmsVerifierPath)
    Assert-Contract $wmsVerifierSource.Contains('-not $verifierContract.outboundCompletionWired', [StringComparison]::Ordinal) 'The production WMS verifier must reject an unproven completed-outbound readback wiring contract before starting infrastructure.'
    $mutationTokens = $mutationParseErrors = $null
    $mutationAst = [Management.Automation.Language.Parser]::ParseInput($wmsVerifierSource, [ref]$mutationTokens, [ref]$mutationParseErrors)
    Assert-Contract ($mutationParseErrors.Count -eq 0) 'The verifier must parse before generating reachability mutations.'
    $constantBindingCommands = @($mutationAst.FindAll({
                param($node)
                $node -is [Management.Automation.Language.CommandAst] -and
                    [string]::Equals([string]$node.GetCommandName(), 'New-Variable', [StringComparison]::Ordinal)
            }, $true) | Where-Object {
                $_.CommandElements.Count -eq 7 -and
                    $_.CommandElements[1] -is [Management.Automation.Language.CommandParameterAst] -and
                    [string]::Equals([string]$_.CommandElements[1].ParameterName, 'Name', [StringComparison]::OrdinalIgnoreCase) -and
                    $_.CommandElements[3] -is [Management.Automation.Language.CommandParameterAst] -and
                    [string]::Equals([string]$_.CommandElements[3].ParameterName, 'Option', [StringComparison]::OrdinalIgnoreCase) -and
                    [string]::Equals([string]$_.CommandElements[4].Extent.Text, 'Constant', [StringComparison]::Ordinal) -and
                    $_.CommandElements[5] -is [Management.Automation.Language.CommandParameterAst] -and
                    [string]::Equals([string]$_.CommandElements[5].ParameterName, 'Value', [StringComparison]::OrdinalIgnoreCase)
            })
    $constantBindingNames = @($constantBindingCommands | ForEach-Object { [string]$_.CommandElements[2].Extent.Text })
    Assert-Contract ($constantBindingCommands.Count -eq 4 -and
        @('pickingLifecycleCompleted', 'completionHttpReplayConverged', 'completedOutboundOrder', 'businessEvidence').Where({ $constantBindingNames -notcontains $_ }).Count -eq 0) 'The verifier must establish the four authority facts through exact top-level Constant bindings.'
    foreach ($contractMutation in @(
        @{ Name = 'failure-capture-if-false'; Command = 'Export-Man527FailureDiagnostics'; Variable = '$diagnosticEvidence'; Property = 'failureCaptureSupported' },
        @{ Name = 'picking-if-false'; Command = 'Test-NervAcceptanceWmsPickingReadbacks'; Variable = '$pickingLifecycleCompleted'; Property = 'pickingReadbackWired' },
        @{ Name = 'completion-if-false'; Command = 'Test-NervAcceptanceWmsCompletionReplay'; Variable = '$completionHttpReplayConverged'; Property = 'completionReplayWired' },
        @{ Name = 'outbound-completion-if-false'; Command = 'Wait-WmsOutboundOrder'; Variable = '$completedOutboundOrder'; Property = 'outboundCompletionWired' }
    )) {
        $mutationCommand = @($mutationAst.FindAll({
                    param($node)
                    $node -is [Management.Automation.Language.CommandAst] -and
                        [string]::Equals($node.GetCommandName(), [string]$contractMutation.Command, [StringComparison]::Ordinal)
                }, $true) | Where-Object {
                    $ancestor = $_.Parent
                    if ([string]::Equals([string]$contractMutation.Property, 'failureCaptureSupported', [StringComparison]::Ordinal)) {
                        while ($null -ne $ancestor -and $ancestor -isnot [Management.Automation.Language.AssignmentStatementAst]) { $ancestor = $ancestor.Parent }
                        return $null -ne $ancestor -and [string]::Equals($ancestor.Left.Extent.Text, [string]$contractMutation.Variable, [StringComparison]::Ordinal)
                    }
                    while ($null -ne $ancestor -and $ancestor -isnot [Management.Automation.Language.CommandAst]) { $ancestor = $ancestor.Parent }
                    return $null -ne $ancestor -and
                        [string]::Equals([string]$ancestor.GetCommandName(), 'New-Variable', [StringComparison]::Ordinal) -and
                        [string]::Equals([string]$ancestor.CommandElements[2].Extent.Text, ([string]$contractMutation.Variable).TrimStart('$'), [StringComparison]::Ordinal)
                })
        Assert-Contract ($mutationCommand.Count -eq 1) "Verifier contract mutation '$($contractMutation.Name)' requires one exact production authority binding."
        $mutationAssignment = $mutationCommand[0].Parent
        if ([string]::Equals([string]$contractMutation.Property, 'failureCaptureSupported', [StringComparison]::Ordinal)) {
            while ($mutationAssignment -isnot [Management.Automation.Language.AssignmentStatementAst]) { $mutationAssignment = $mutationAssignment.Parent }
        }
        else {
            while ($mutationAssignment -isnot [Management.Automation.Language.CommandAst] -or
                -not [string]::Equals([string]$mutationAssignment.GetCommandName(), 'New-Variable', [StringComparison]::Ordinal)) { $mutationAssignment = $mutationAssignment.Parent }
        }
        $mutatedVerifierPath = Join-Path $fixtureRoot "wms-diagnostics/$($contractMutation.Name).ps1"
        $mutatedVerifierSource = $wmsVerifierSource.Insert($mutationAssignment.Extent.EndOffset, ' }').Insert($mutationAssignment.Extent.StartOffset, 'if ($false) { ')
        [IO.File]::WriteAllText($mutatedVerifierPath, $mutatedVerifierSource, [Text.UTF8Encoding]::new($false))
        $mutatedContract = Test-NervAcceptanceWmsVerifierContract -Path $mutatedVerifierPath
        Assert-Contract (-not [bool]$mutatedContract.PSObject.Properties[$contractMutation.Property].Value) "Verifier contract mutation '$($contractMutation.Name)' must be killed by '$($contractMutation.Property)'."

        $unusedFunctionMutationPath = Join-Path $fixtureRoot "wms-diagnostics/$($contractMutation.Name)-unused-function.ps1"
        $unusedFunctionMutationSource = $wmsVerifierSource.Insert($mutationAssignment.Extent.EndOffset, ' }').Insert($mutationAssignment.Extent.StartOffset, "function Invoke-UnusedContractMutation { ")
        [IO.File]::WriteAllText($unusedFunctionMutationPath, $unusedFunctionMutationSource, [Text.UTF8Encoding]::new($false))
        $unusedFunctionContract = Test-NervAcceptanceWmsVerifierContract -Path $unusedFunctionMutationPath
        Assert-Contract (-not [bool]$unusedFunctionContract.PSObject.Properties[$contractMutation.Property].Value) "Verifier unused-function mutation '$($contractMutation.Name)' must be killed by '$($contractMutation.Property)'."

        if (-not [string]::Equals([string]$contractMutation.Property, 'failureCaptureSupported', [StringComparison]::Ordinal)) {
            $pipelineMapper = if ([string]::Equals([string]$contractMutation.Property, 'outboundCompletionWired', [StringComparison]::Ordinal)) {
                " | ForEach-Object { [pscustomobject]@{ status = 'Completed'; completedAtUtc = '2026-08-25T00:00:00Z' } }"
            }
            else { ' | ForEach-Object { $true }' }
            $pipelineMapperPath = Join-Path $fixtureRoot "wms-diagnostics/$($contractMutation.Name)-pipeline-mapper.ps1"
            $pipelineMapperSource = $wmsVerifierSource.Insert($mutationCommand[0].Extent.EndOffset, $pipelineMapper)
            [IO.File]::WriteAllText($pipelineMapperPath, $pipelineMapperSource, [Text.UTF8Encoding]::new($false))
            Assert-Contract (-not [bool](Test-NervAcceptanceWmsVerifierContract -Path $pipelineMapperPath).PSObject.Properties[$contractMutation.Property].Value) "Verifier pipeline mapper mutation '$($contractMutation.Name)' must be killed by '$($contractMutation.Property)'."

            foreach ($constantBindingMutation in @(
                @{ Name = 'missing-constant-option'; Old = '-Option Constant '; New = '' },
                @{ Name = 'wrong-option'; Old = '-Option Constant'; New = '-Option ReadOnly' },
                @{ Name = 'wrong-name'; Old = "-Name $(([string]$contractMutation.Variable).TrimStart('$'))"; New = '-Name unrelatedAuthorityFact' }
            )) {
                $constantMutationPath = Join-Path $fixtureRoot "wms-diagnostics/$($contractMutation.Name)-$($constantBindingMutation.Name).ps1"
                $constantMutationText = $mutationAssignment.Extent.Text.Replace([string]$constantBindingMutation.Old, [string]$constantBindingMutation.New)
                $constantMutationSource = $wmsVerifierSource.Remove($mutationAssignment.Extent.StartOffset, $mutationAssignment.Extent.EndOffset - $mutationAssignment.Extent.StartOffset).Insert($mutationAssignment.Extent.StartOffset, $constantMutationText)
                [IO.File]::WriteAllText($constantMutationPath, $constantMutationSource, [Text.UTF8Encoding]::new($false))
                Assert-Contract (-not [bool](Test-NervAcceptanceWmsVerifierContract -Path $constantMutationPath).PSObject.Properties[$contractMutation.Property].Value) "Verifier Constant mutation '$($contractMutation.Name)-$($constantBindingMutation.Name)' must be killed by '$($contractMutation.Property)'."
            }

            $overrideMutationPath = Join-Path $fixtureRoot "wms-diagnostics/$($contractMutation.Name)-top-level-override.ps1"
            $overrideMutationSource = $wmsVerifierSource.Insert($mutationAssignment.Extent.EndOffset, "`n$($contractMutation.Variable) = `$true")
            [IO.File]::WriteAllText($overrideMutationPath, $overrideMutationSource, [Text.UTF8Encoding]::new($false))
            $overrideContract = Test-NervAcceptanceWmsVerifierContract -Path $overrideMutationPath
            Assert-Contract (-not [bool]$overrideContract.PSObject.Properties[$contractMutation.Property].Value) "Verifier top-level override mutation '$($contractMutation.Name)' must be killed by '$($contractMutation.Property)'."

            $alternateOverrideMutations = if ([string]::Equals([string]$contractMutation.Property, 'pickingReadbackWired', [StringComparison]::Ordinal)) {
                @(
                        @{ Name = 'script-scoped'; Statement = '$script:pickingLifecycleCompleted = $true' },
                        @{ Name = 'global-scoped-uppercase'; Statement = '$GLOBAL:PICKINGLIFECYCLECOMPLETED = $true' },
                        @{ Name = 'local-scoped'; Statement = '$local:pickingLifecycleCompleted = $true' },
                        @{ Name = 'private-scoped'; Statement = '$private:pickingLifecycleCompleted = $true' },
                        @{ Name = 'variable-drive'; Statement = '$variable:pickingLifecycleCompleted = $true' },
                        @{ Name = 'braced'; Statement = '${pickingLifecycleCompleted} = $true' },
                        @{ Name = 'new-item-direct-variable-provider'; Statement = 'New-Item variable:pickingLifecycleCompleted -Value $true -Force | Out-Null' }
                    )
            }
            elseif ([string]::Equals([string]$contractMutation.Property, 'completionReplayWired', [StringComparison]::Ordinal)) {
                @(
                        @{ Name = 'set-variable-named'; Statement = 'Set-Variable -Name completionHttpReplayConverged -Value $true' },
                        @{ Name = 'set-variable-positional-after-valued'; Statement = 'Set-Variable -Scope Local completionHttpReplayConverged $true' },
                        @{ Name = 'set-variable-positional-after-switch'; Statement = 'Set-Variable -Force completionHttpReplayConverged $true' },
                        @{ Name = 'set-variable-alias'; Statement = 'sv -Name completionHttpReplayConverged -Value $true' },
                        @{ Name = 'set-variable-module-qualified-uppercase-scope'; Statement = 'microsoft.powershell.utility\SET-VARIABLE -Name SCRIPT:COMPLETIONHTTPREPLAYCONVERGED -Value $true' },
                        @{ Name = 'new-variable-named'; Statement = 'New-Variable -Name completionHttpReplayConverged -Value $true -Force' },
                        @{ Name = 'new-variable-alias'; Statement = 'nv completionHttpReplayConverged $true -Force' },
                        @{ Name = 'new-variable-module-qualified'; Statement = 'Microsoft.PowerShell.Utility\New-Variable -Name completionHttpReplayConverged -Value $true -Force' },
                        @{ Name = 'set-item-variable-provider'; Statement = 'Set-Item -LiteralPath variable:completionHttpReplayConverged -Value $true' },
                        @{ Name = 'set-item-common-parameter-before-path'; Statement = 'Set-Item -ErrorAction Stop variable:completionHttpReplayConverged -Value $true' },
                        @{ Name = 'set-item-value-before-path'; Statement = 'Set-Item -Value $true variable:completionHttpReplayConverged' },
                        @{ Name = 'set-item-force-before-path'; Statement = 'Set-Item -Force variable:completionHttpReplayConverged $true' },
                        @{ Name = 'set-item-passthru-before-path'; Statement = 'Set-Item -PassThru variable:completionHttpReplayConverged $true | Out-Null' },
                        @{ Name = 'new-item-alias-path'; Statement = 'ni -Path variable:completionHttpReplayConverged -Value $true -Force | Out-Null' },
                        @{ Name = 'new-item-dynamic-name-under-variable-root'; Statement = '$computedNewItemName = ''completionHttpReplayConverged''; New-Item -Path variable: -Name $computedNewItemName -Value $true -Force | Out-Null' },
                        @{ Name = 'typed'; Statement = '[bool]$completionHttpReplayConverged = $true' }
                    )
            }
            elseif ([string]::Equals([string]$contractMutation.Property, 'outboundCompletionWired', [StringComparison]::Ordinal)) {
                @(
                        @{ Name = 'typed'; Statement = '[object]$completedOutboundOrder = $null' },
                        @{ Name = 'set-variable'; Statement = 'Set-Variable -Name completedOutboundOrder -Value $null' },
                        @{ Name = 'new-item-module-qualified'; Statement = 'Microsoft.PowerShell.Management\New-Item -Path variable:completedOutboundOrder -Value $null -Force | Out-Null' }
                    )
            }
            foreach ($alternateOverrideMutation in $alternateOverrideMutations) {
                $alternateOverridePath = Join-Path $fixtureRoot "wms-diagnostics/$($contractMutation.Name)-$($alternateOverrideMutation.Name)-override.ps1"
                $alternateOverrideSource = $wmsVerifierSource.Insert($mutationAssignment.Extent.EndOffset, "`n$($alternateOverrideMutation.Statement)")
                [IO.File]::WriteAllText($alternateOverridePath, $alternateOverrideSource, [Text.UTF8Encoding]::new($false))
                $alternateOverrideContract = Test-NervAcceptanceWmsVerifierContract -Path $alternateOverridePath
                Assert-Contract (-not [bool]$alternateOverrideContract.PSObject.Properties[$contractMutation.Property].Value) "Verifier alternate top-level override mutation '$($contractMutation.Name)-$($alternateOverrideMutation.Name)' must be killed by '$($contractMutation.Property)'."

                $functionLocalOverridePath = Join-Path $fixtureRoot "wms-diagnostics/$($contractMutation.Name)-$($alternateOverrideMutation.Name)-function-local.ps1"
                $functionLocalOverrideSource = $wmsVerifierSource.Insert($mutationAssignment.Extent.EndOffset, "`nfunction Invoke-UnusedAlternateOverride { $($alternateOverrideMutation.Statement) }")
                [IO.File]::WriteAllText($functionLocalOverridePath, $functionLocalOverrideSource, [Text.UTF8Encoding]::new($false))
                $functionLocalOverrideContract = Test-NervAcceptanceWmsVerifierContract -Path $functionLocalOverridePath
                Assert-Contract ([bool]$functionLocalOverrideContract.PSObject.Properties[$contractMutation.Property].Value) "Verifier function-local write '$($contractMutation.Name)-$($alternateOverrideMutation.Name)' must not be counted as a top-level override."
            }
        }
    }

    foreach ($nonTargetWrite in @(
        @{ Name = 'set-variable-unrelated'; Statement = 'Set-Variable -Name unrelatedWmsEvidence -Value $true' },
        @{ Name = 'new-variable-unrelated'; Statement = 'New-Variable -Name unrelatedWmsEvidence -Value $true -Force' },
        @{ Name = 'new-item-unrelated'; Statement = 'New-Item -Path variable:unrelatedWmsEvidence -Value $true -Force | Out-Null' },
        @{ Name = 'new-item-ordinary-path'; Statement = 'New-Item -Path ./pickingLifecycleCompleted -ItemType File -Force | Out-Null' },
        @{ Name = 'set-item-unrelated'; Statement = 'Set-Item -LiteralPath variable:unrelatedWmsEvidence -Value $true' },
        @{ Name = 'set-item-common-parameter-unrelated'; Statement = 'Set-Item -ErrorAction Stop variable:unrelatedWmsEvidence -Value $true' },
        @{ Name = 'env-prefix'; Statement = '$env:pickingLifecycleCompleted = ''not-the-script-variable''' }
    )) {
        $nonTargetPath = Join-Path $fixtureRoot "wms-diagnostics/$($nonTargetWrite.Name).ps1"
        $nonTargetSource = $wmsVerifierSource.Insert($mutationAssignment.Extent.EndOffset, "`n$($nonTargetWrite.Statement)")
        [IO.File]::WriteAllText($nonTargetPath, $nonTargetSource, [Text.UTF8Encoding]::new($false))
        $nonTargetContract = Test-NervAcceptanceWmsVerifierContract -Path $nonTargetPath
        Assert-Contract ($nonTargetContract.failureCaptureSupported -and $nonTargetContract.pickingReadbackWired -and $nonTargetContract.completionReplayWired -and $nonTargetContract.outboundCompletionWired) "Verifier unrelated write '$($nonTargetWrite.Name)' must not invalidate any production wiring fact."
    }

    foreach ($residualWrite in @(
        @{ Name = 'computed-set-variable'; Statement = '$computedName = ''completionHttpReplayConverged''; Set-Variable -Name $computedName -Value $true' },
        @{ Name = 'splatted-set-variable'; Statement = '$setArguments = @{ Name = ''completionHttpReplayConverged''; Value = $true }; Set-Variable @setArguments' },
        @{ Name = 'computed-set-item-path'; Statement = '$computedPath = ''variable:completionHttpReplayConverged''; Set-Item -LiteralPath $computedPath -Value $true' },
        @{ Name = 'dynamic-new-item-provider-path'; Statement = '$computedProviderPath = ''variable:completionHttpReplayConverged''; New-Item -Path $computedProviderPath -Value $true -Force | Out-Null' },
        @{ Name = 'ref-rebinding'; Statement = '$completionReference = [ref]$completionHttpReplayConverged; $completionReference.Value = $true' }
    )) {
        $residualPath = Join-Path $fixtureRoot "wms-diagnostics/$($residualWrite.Name).ps1"
        $residualSource = $wmsVerifierSource.Insert($mutationAssignment.Extent.EndOffset, "`n$($residualWrite.Statement)")
        [IO.File]::WriteAllText($residualPath, $residualSource, [Text.UTF8Encoding]::new($false))
        $residualContract = Test-NervAcceptanceWmsVerifierContract -Path $residualPath
        Assert-Contract ($residualContract.completionReplayWired) "Documented static-analysis residual '$($residualWrite.Name)' must remain outside the verifier wiring proof."
    }

    foreach ($psVariableMutation in @(
        @{ Name = 'literal'; Statement = '$ExecutionContext.SessionState.PSVariable.Set(''completionHttpReplayConverged'', $true)' },
        @{ Name = 'computed'; Statement = '$computedPsVariableName = ''completionHttpReplayConverged''; $ExecutionContext.SessionState.PSVariable.Set($computedPsVariableName, $true)' }
    )) {
        $psVariablePath = Join-Path $fixtureRoot "wms-diagnostics/psvariable-set-$($psVariableMutation.Name).ps1"
        $psVariableSource = $wmsVerifierSource.Insert($mutationAssignment.Extent.EndOffset, "`n$($psVariableMutation.Statement)")
        [IO.File]::WriteAllText($psVariablePath, $psVariableSource, [Text.UTF8Encoding]::new($false))
        $psVariableContract = Test-NervAcceptanceWmsVerifierContract -Path $psVariablePath
        Assert-Contract (-not $psVariableContract.pickingReadbackWired -and -not $psVariableContract.completionReplayWired -and -not $psVariableContract.outboundCompletionWired) "PSVariable.Set $($psVariableMutation.Name) override must invalidate every Constant-backed verifier authority fact."

        $inactivePsVariablePath = Join-Path $fixtureRoot "wms-diagnostics/psvariable-set-$($psVariableMutation.Name)-unused-function.ps1"
        $inactivePsVariableSource = $wmsVerifierSource.Insert($mutationAssignment.Extent.EndOffset, "`nfunction Invoke-UnusedPsVariableOverride { $($psVariableMutation.Statement) }")
        [IO.File]::WriteAllText($inactivePsVariablePath, $inactivePsVariableSource, [Text.UTF8Encoding]::new($false))
        $inactivePsVariableContract = Test-NervAcceptanceWmsVerifierContract -Path $inactivePsVariablePath
        Assert-Contract ($inactivePsVariableContract.pickingReadbackWired -and $inactivePsVariableContract.completionReplayWired -and $inactivePsVariableContract.outboundCompletionWired) "Unused-function PSVariable.Set $($psVariableMutation.Name) must not invalidate top-level Constant bindings."
    }
    foreach ($constantRuntimeOverride in @(
        '$ExecutionContext.SessionState.PSVariable.Set(''constantAuthorityFact'', ''mutated'')',
        '$computedConstantName = ''constantAuthorityFact''; $ExecutionContext.SessionState.PSVariable.Set($computedConstantName, ''mutated'')'
    )) {
        $constantOverrideRejected = & ([scriptblock]::Create(@"
New-Variable -Name constantAuthorityFact -Option Constant -Value 'earned'
try {
    $constantRuntimeOverride
    return `$false
}
catch {
    return [string]::Equals([string]`$constantAuthorityFact, 'earned', [StringComparison]::Ordinal)
}
"@))
        Assert-Contract $constantOverrideRejected 'A Constant-backed authority fact must reject literal and computed PSVariable.Set runtime rebinding.'
    }

    $pickingPredicateCommand = @($mutationAst.FindAll({
                param($node)
                $node -is [Management.Automation.Language.CommandAst] -and
                    [string]::Equals($node.GetCommandName(), 'Test-NervAcceptanceWmsPickingReadbacks', [StringComparison]::Ordinal)
            }, $true))[0]
    $pickingPredicateAssignment = $pickingPredicateCommand.Parent
    while ($pickingPredicateAssignment -isnot [Management.Automation.Language.CommandAst] -or
        -not [string]::Equals([string]$pickingPredicateAssignment.GetCommandName(), 'New-Variable', [StringComparison]::Ordinal)) { $pickingPredicateAssignment = $pickingPredicateAssignment.Parent }
    $doublePredicatePath = Join-Path $fixtureRoot 'wms-diagnostics/picking-double-predicate.ps1'
    $pickingPredicateValue = $pickingPredicateAssignment.CommandElements[6]
    $doublePredicateRight = "(($($pickingPredicateValue.Pipeline.Extent.Text))) -and (($($pickingPredicateValue.Pipeline.Extent.Text)))"
    $doublePredicateSource = $wmsVerifierSource.Remove($pickingPredicateValue.Extent.StartOffset, $pickingPredicateValue.Extent.EndOffset - $pickingPredicateValue.Extent.StartOffset).Insert($pickingPredicateValue.Extent.StartOffset, $doublePredicateRight)
    [IO.File]::WriteAllText($doublePredicatePath, $doublePredicateSource, [Text.UTF8Encoding]::new($false))
    $doublePredicateContract = Test-NervAcceptanceWmsVerifierContract -Path $doublePredicatePath
    Assert-Contract (-not $doublePredicateContract.pickingReadbackWired) 'Two picking predicates inside one top-level assignment must invalidate pickingReadbackWired.'

    $failureExporterFunction = @($mutationAst.FindAll({
                param($node)
                $node -is [Management.Automation.Language.FunctionDefinitionAst] -and
                    [string]::Equals($node.Name, 'Export-Man527FailureDiagnostics', [StringComparison]::Ordinal)
            }, $true))[0]
    $duplicateFailureExporterPath = Join-Path $fixtureRoot 'wms-diagnostics/failure-exporter-function-duplicate.ps1'
    $duplicateFailureExporterSource = $wmsVerifierSource.Insert($failureExporterFunction.Extent.EndOffset, "`n$($failureExporterFunction.Extent.Text)")
    [IO.File]::WriteAllText($duplicateFailureExporterPath, $duplicateFailureExporterSource, [Text.UTF8Encoding]::new($false))
    $duplicateFailureExporterContract = Test-NervAcceptanceWmsVerifierContract -Path $duplicateFailureExporterPath
    Assert-Contract (-not $duplicateFailureExporterContract.failureCaptureSupported) 'A duplicate top-level failure exporter function definition must invalidate failureCaptureSupported.'

    foreach ($outboundWiringMutation in @(
        @{ Name = 'missing-require-completed'; Old = '-RequireCompleted'; New = '' },
        @{ Name = 'constant-completion-status'; Old = '$completedOutboundOrder.status'; New = "'Completed'" },
        @{ Name = 'constant-completion-time'; Old = '$completedOutboundOrder.completedAtUtc'; New = "'2026-08-24T00:00:30Z'" }
    )) {
        $occurrences = ([regex]::Matches($wmsVerifierSource, [regex]::Escape([string]$outboundWiringMutation.Old))).Count
        Assert-Contract ($occurrences -eq 1) "Outbound wiring mutation '$($outboundWiringMutation.Name)' requires one exact production occurrence."
        $outboundWiringPath = Join-Path $fixtureRoot "wms-diagnostics/$($outboundWiringMutation.Name).ps1"
        $outboundWiringSource = $wmsVerifierSource.Replace([string]$outboundWiringMutation.Old, [string]$outboundWiringMutation.New)
        [IO.File]::WriteAllText($outboundWiringPath, $outboundWiringSource, [Text.UTF8Encoding]::new($false))
        $outboundWiringContract = Test-NervAcceptanceWmsVerifierContract -Path $outboundWiringPath
        Assert-Contract (-not $outboundWiringContract.outboundCompletionWired) "Outbound wiring mutation '$($outboundWiringMutation.Name)' must invalidate outboundCompletionWired."
    }

    $completedReadbackPredicate = '(Test-NervAcceptanceWmsCompletedOutboundReadback -Readback $rows[0])'
    Assert-Contract (([regex]::Matches($wmsVerifierSource, [regex]::Escape($completedReadbackPredicate))).Count -eq 1) 'The completed-outbound helper mutation requires one exact production predicate call.'
    $bypassedReadbackPredicatePath = Join-Path $fixtureRoot 'wms-diagnostics/bypassed-completed-readback-predicate.ps1'
    [IO.File]::WriteAllText($bypassedReadbackPredicatePath, $wmsVerifierSource.Replace($completedReadbackPredicate, '$true'), [Text.UTF8Encoding]::new($false))
    Assert-Contract (-not (Test-NervAcceptanceWmsVerifierContract -Path $bypassedReadbackPredicatePath).outboundCompletionWired) 'Bypassing Test-NervAcceptanceWmsCompletedOutboundReadback inside Wait-WmsOutboundOrder must invalidate outboundCompletionWired.'

    $completedReadbackCommand = @($mutationAst.FindAll({
                param($node)
                $node -is [Management.Automation.Language.CommandAst] -and
                    [string]::Equals($node.GetCommandName(), 'Test-NervAcceptanceWmsCompletedOutboundReadback', [StringComparison]::Ordinal)
            }, $true))[0]
    $completionGate = $completedReadbackCommand.Parent
    while ($completionGate -isnot [Management.Automation.Language.BinaryExpressionAst] -or $completionGate.Operator -ne [Management.Automation.Language.TokenKind]::Or) { $completionGate = $completionGate.Parent }
    $completionGuard = $completedReadbackCommand.Parent
    while ($completionGuard -isnot [Management.Automation.Language.IfStatementAst]) { $completionGuard = $completionGuard.Parent }
    $ignoredGateGuard = $completionGuard.Extent.Text.Replace($completionGate.Extent.Text, '$true')
    $ignoredGateGuard = $ignoredGateGuard.Insert($ignoredGateGuard.IndexOf('{', [StringComparison]::Ordinal) + 1, "`n`$ignoredCompletionGate = $($completionGate.Extent.Text)")
    $ignoredGatePath = Join-Path $fixtureRoot 'wms-diagnostics/completed-readback-gate-in-if-body.ps1'
    [IO.File]::WriteAllText($ignoredGatePath, $wmsVerifierSource.Replace($completionGuard.Extent.Text, $ignoredGateGuard), [Text.UTF8Encoding]::new($false))
    Assert-Contract (-not (Test-NervAcceptanceWmsVerifierContract -Path $ignoredGatePath).outboundCompletionWired) 'The RequireCompleted helper gate must be the return if clause condition, not an ignored expression in its body.'

    $completionReadbackNode = 'completionReadback = [ordered]@{ status = $completedOutboundOrder.status; completedAtUtc = $completedOutboundOrder.completedAtUtc }'
    Assert-Contract (([regex]::Matches($wmsVerifierSource, [regex]::Escape($completionReadbackNode))).Count -eq 1) 'The canonical completionReadback mutation requires one exact production node.'
    $dummyReadbackNode = 'dummyCompletionEvidence = if ($false) { [ordered]@{ completionReadback = [ordered]@{ status = $completedOutboundOrder.status; completedAtUtc = $completedOutboundOrder.completedAtUtc } } }'
    $dummyReadbackPath = Join-Path $fixtureRoot 'wms-diagnostics/dead-dummy-completion-readback.ps1'
    [IO.File]::WriteAllText($dummyReadbackPath, $wmsVerifierSource.Replace($completionReadbackNode, $dummyReadbackNode), [Text.UTF8Encoding]::new($false))
    Assert-Contract (-not (Test-NervAcceptanceWmsVerifierContract -Path $dummyReadbackPath).outboundCompletionWired) 'A dead dummy completionReadback outside businessEvidence.wmsOutboundOrder must not prove outbound completion wiring.'

    foreach ($deadMemberReadMutation in @(
        @{ Name = 'dead-status-member-read'; Old = '$completedOutboundOrder.status'; New = "if (`$false) { `$completedOutboundOrder.status } else { 'Completed' }" },
        @{ Name = 'dead-time-member-read'; Old = '$completedOutboundOrder.completedAtUtc'; New = "if (`$false) { `$completedOutboundOrder.completedAtUtc } else { '2026-08-24T00:00:30Z' }" }
    )) {
        Assert-Contract (([regex]::Matches($wmsVerifierSource, [regex]::Escape([string]$deadMemberReadMutation.Old))).Count -eq 1) "Dead member-read mutation '$($deadMemberReadMutation.Name)' requires one exact production expression."
        $deadMemberReadPath = Join-Path $fixtureRoot "wms-diagnostics/$($deadMemberReadMutation.Name).ps1"
        [IO.File]::WriteAllText($deadMemberReadPath, $wmsVerifierSource.Replace([string]$deadMemberReadMutation.Old, [string]$deadMemberReadMutation.New), [Text.UTF8Encoding]::new($false))
        Assert-Contract (-not (Test-NervAcceptanceWmsVerifierContract -Path $deadMemberReadPath).outboundCompletionWired) "Dead member-read mutation '$($deadMemberReadMutation.Name)' must invalidate outboundCompletionWired."
    }

    $completedOutboundAssignmentText = $mutationAssignment.Extent.Text
    Assert-Contract $completedOutboundAssignmentText.Contains('Wait-WmsOutboundOrder', [StringComparison]::Ordinal) 'The unused scriptblock mutation must target the completed outbound Constant binding.'
    $unusedScriptblockPath = Join-Path $fixtureRoot 'wms-diagnostics/completed-outbound-unused-scriptblock.ps1'
    $unusedScriptblockSource = $wmsVerifierSource.Remove($mutationAssignment.Extent.StartOffset, $mutationAssignment.Extent.EndOffset - $mutationAssignment.Extent.StartOffset).Insert($mutationAssignment.Extent.StartOffset, "`$unusedCompletedOutboundRead = { $completedOutboundAssignmentText }")
    [IO.File]::WriteAllText($unusedScriptblockPath, $unusedScriptblockSource, [Text.UTF8Encoding]::new($false))
    Assert-Contract (-not (Test-NervAcceptanceWmsVerifierContract -Path $unusedScriptblockPath).outboundCompletionWired) 'Moving the completed outbound wait into an uninvoked scriptblock must invalidate outboundCompletionWired.'

    $businessEvidenceAssignment = @($mutationAst.FindAll({
                param($node)
                $node -is [Management.Automation.Language.CommandAst] -and
                    [string]::Equals([string]$node.GetCommandName(), 'New-Variable', [StringComparison]::Ordinal) -and
                    $node.CommandElements.Count -eq 7 -and
                    [string]::Equals([string]$node.CommandElements[2].Extent.Text, 'businessEvidence', [StringComparison]::Ordinal) -and
                    $node.CommandElements[6].Extent.Text.Contains('wmsOutboundOrder', [StringComparison]::Ordinal)
            }, $true))[0]
    foreach ($businessEvidenceOverride in @(
        @{ Name = 'direct'; Statement = '$businessEvidence = $null' },
        @{ Name = 'set-variable'; Statement = 'Set-Variable -Name businessEvidence -Value $null' },
        @{ Name = 'new-item-split-provider'; Statement = 'New-Item -Path variable: -Name businessEvidence -Value $null -Force | Out-Null' }
    )) {
        $businessEvidenceOverridePath = Join-Path $fixtureRoot "wms-diagnostics/business-evidence-$($businessEvidenceOverride.Name)-override.ps1"
        $businessEvidenceOverrideSource = $wmsVerifierSource.Insert($businessEvidenceAssignment.Extent.EndOffset, "`n$($businessEvidenceOverride.Statement)")
        [IO.File]::WriteAllText($businessEvidenceOverridePath, $businessEvidenceOverrideSource, [Text.UTF8Encoding]::new($false))
        Assert-Contract (-not (Test-NervAcceptanceWmsVerifierContract -Path $businessEvidenceOverridePath).outboundCompletionWired) "A later top-level businessEvidence $($businessEvidenceOverride.Name) override must invalidate outboundCompletionWired."
    }

    $evidencePayloadAssignment = @($mutationAst.FindAll({
                param($node)
                $node -is [Management.Automation.Language.AssignmentStatementAst] -and
                    $node.Left -is [Management.Automation.Language.VariableExpressionAst] -and
                    [string]::Equals([string]$node.Left.VariablePath.UserPath, 'evidencePayload', [StringComparison]::Ordinal)
            }, $true))[0]
    Assert-Contract ($null -ne $evidencePayloadAssignment) 'The failure evidence scope fixture requires the production evidencePayload assignment.'
    $failureEvidenceFixture = [scriptblock]::Create(@"
`$scenarioError = [Management.Automation.ErrorRecord]::new([InvalidOperationException]::new('fixture-scenario-failed'), 'fixture', [Management.Automation.ErrorCategory]::InvalidOperation, `$null)
`$deliveryOrderNo = 'fixture-delivery'
`$internalToken = `$null
`$PostgresAdminConnectionString = `$null
`$databaseConnectionString = `$null
`$RedisConnectionString = `$null
$($evidencePayloadAssignment.Extent.Text)
return `$evidencePayload
"@)
    $priorGlobalBusinessEvidence = Get-Variable -Name businessEvidence -Scope Global -ErrorAction SilentlyContinue
    try {
        $global:businessEvidence = [ordered]@{ scenarioStatus = 'polluted-global-success' }
        $failureEvidencePayload = & $failureEvidenceFixture
        Assert-Contract ([string]::Equals([string]$failureEvidencePayload.scenarioStatus, 'failed', [StringComparison]::Ordinal)) 'A failed verifier must not consume a caller or global businessEvidence value.'
    }
    finally {
        if ($null -ne $priorGlobalBusinessEvidence) {
            Set-Variable -Name businessEvidence -Scope Global -Value $priorGlobalBusinessEvidence.Value
        }
        else {
            Remove-Variable -Name businessEvidence -Scope Global -ErrorAction SilentlyContinue
        }
    }

    $failureAssignmentCommand = @($mutationAst.FindAll({
                param($node)
                $node -is [Management.Automation.Language.CommandAst] -and
                    [string]::Equals($node.GetCommandName(), 'Export-Man527FailureDiagnostics', [StringComparison]::Ordinal)
            }, $true))[0]
    $failureAssignment = $failureAssignmentCommand.Parent
    while ($failureAssignment -isnot [Management.Automation.Language.AssignmentStatementAst]) { $failureAssignment = $failureAssignment.Parent }
    $duplicateFailureCapturePath = Join-Path $fixtureRoot 'wms-diagnostics/failure-capture-duplicate.ps1'
    $duplicateFailureCaptureSource = $wmsVerifierSource.Insert($failureAssignment.Extent.EndOffset, "`n$($failureAssignment.Extent.Text)")
    [IO.File]::WriteAllText($duplicateFailureCapturePath, $duplicateFailureCaptureSource, [Text.UTF8Encoding]::new($false))
    $duplicateFailureCaptureContract = Test-NervAcceptanceWmsVerifierContract -Path $duplicateFailureCapturePath
    Assert-Contract (-not $duplicateFailureCaptureContract.failureCaptureSupported) 'A duplicate top-level failure exporter assignment must invalidate failureCaptureSupported.'
    Write-Output 'Acceptance canonical result contract tests passed.'
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
}
