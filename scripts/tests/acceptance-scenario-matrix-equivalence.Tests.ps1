# Script-Governance:
#   Category: check
#   SideEffects:
#     - Validates two-track acceptance equivalence with repository and temporary JSON fixtures
#   Writes:
#     - Temporary planning, canonical-result, report, and workflow fixtures under the operating-system temp directory
#   Cleanup:
#     - Removes owned temporary fixtures in finally
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$libraryPath = Join-Path $repoRoot 'scripts/lib/AcceptanceScenarioMatrixEquivalence.ps1'
$runnerPath = Join-Path $repoRoot 'scripts/verify-acceptance-scenario-matrix-equivalence.ps1'
$manifestPath = Join-Path $repoRoot 'scripts/acceptance-scenario-matrix.json'
$v1ManifestPath = Join-Path $repoRoot 'scripts/full-chain-test-lane.json'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-acceptance-equivalence-$([Guid]::NewGuid().ToString('N'))"

function Assert-Contract([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Copy-JsonObject([object] $Value) {
    return ($Value | ConvertTo-Json -Depth 50 | ConvertFrom-Json -Depth 50 -DateKind String)
}

function Write-JsonFixture([string] $Path, [object] $Value) {
    [IO.Directory]::CreateDirectory((Split-Path -Parent $Path)) | Out-Null
    [IO.File]::WriteAllText($Path, (($Value | ConvertTo-Json -Depth 50) + "`n"), [Text.UTF8Encoding]::new($false))
}

function Get-FileDigest([string] $Path) {
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try { return ([Convert]::ToHexString($sha256.ComputeHash([IO.File]::ReadAllBytes($Path)))).ToLowerInvariant() }
    finally { $sha256.Dispose() }
}

function New-CanonicalResult([string] $Track, [string] $ManifestDigest, [string] $VolatileMarker, [int] $RunAttempt = 2) {
    return [pscustomobject][ordered]@{
        schemaVersion = 1
        provenance = [pscustomobject][ordered]@{
            repository = 'Mang-X/Nerv-IIP'
            runId = '123456789'
            runAttempt = $RunAttempt
            testedSha = '0123456789abcdef0123456789abcdef01234567'
            manifestDigest = $ManifestDigest
            scenarioId = 'sales-order-demand'
        }
        track = $Track
        conclusion = 'passed'
        test = [pscustomobject][ordered]@{
            identity = 'Nerv.IIP.Business.FullChain.Tests.SalesOrderDemandPlanningPostgresRedisAcceptanceTests.External_process_injects_duplicate_and_out_of_order_sales_order_events'
            expected = 1
            discovered = 1
            passed = 1
            failed = 0
            skipped = 0
        }
        businessFacts = [pscustomobject][ordered]@{
            sourceStateCommittedBeforeMutation = $true
            changeV2Converged = $true
            changeV3Converged = $true
            duplicateConverged = $true
            outOfOrderConverged = $true
            cancellationConverged = $true
        }
        diagnostics = [pscustomobject][ordered]@{
            schemas = @('erp', 'master_data', 'demand_planning')
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
            processIds = @(100 + $VolatileMarker.Length)
            capSuffix = "cap-$VolatileMarker"
            startedAtUtc = '2026-08-21T00:00:00.0000000+00:00'
            completedAtUtc = '2026-08-21T00:01:00.0000000+00:00'
            cleanupErrors = @()
            ports = [pscustomobject][ordered]@{ masterData = 41001; erp = 41002; demandPlanning = 41003 }
            paths = [pscustomobject][ordered]@{
                businessEvidence = "/tmp/$VolatileMarker/business-secret-value.json"
                probeTrx = "/tmp/$VolatileMarker/probe.trx"
                cleanupEvidence = "/tmp/$VolatileMarker/cleanup.json"
                canonicalResult = "/tmp/$VolatileMarker/result.json"
            }
        }
    }
}

function Invoke-ComparatorFixture {
    param(
        [Parameter(Mandatory)] [string[]] $ResultPaths,
        [Parameter(Mandatory)] [string] $ReportPath,
        [string] $ExpectedArtifactDigest = $script:planningDigest,
        [string] $ExpectedManifestDigest = $script:manifestDigest,
        [string] $ArtifactPath = $script:planningPath,
        [int] $PlanningRunAttempt = 2,
        [int] $V1RunAttempt = 2,
        [int] $ShadowRunAttempt = 2,
        [int] $RunAttempt = 2
    )

    return Invoke-NervAcceptanceScenarioMatrixEquivalence `
        -ArtifactPath $ArtifactPath `
        -ExpectedArtifactDigest $ExpectedArtifactDigest `
        -ManifestFilePath $manifestPath `
        -ExpectedManifestDigest $ExpectedManifestDigest `
        -V1ManifestPath $v1ManifestPath `
        -RepositoryRoot $repoRoot `
        -Repository 'Mang-X/Nerv-IIP' `
        -TestedSha '0123456789abcdef0123456789abcdef01234567' `
        -RunId '123456789' `
        -RunAttempt $RunAttempt `
        -PlanningRunAttempt $PlanningRunAttempt `
        -ManifestRepositoryPath 'scripts/acceptance-scenario-matrix.json' `
        -Event 'push' `
        -V1ResultPath $ResultPaths[0] `
        -V1RunAttempt $V1RunAttempt `
        -ShadowResultPath $ResultPaths[1] `
        -ShadowRunAttempt $ShadowRunAttempt `
        -ReportPath $ReportPath
}

function Assert-RejectedWithReport {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string[]] $ResultPaths,
        [Parameter(Mandatory)] [string] $Classification,
        [string] $ExpectedMessage,
        [string] $ExpectedArtifactDigest = $script:planningDigest,
        [string] $ExpectedManifestDigest = $script:manifestDigest
    )

    $reportPath = Join-Path $fixtureRoot "$Name-report.json"
    $failure = $null
    try {
        Invoke-ComparatorFixture -ResultPaths $ResultPaths -ReportPath $reportPath -ExpectedArtifactDigest $ExpectedArtifactDigest -ExpectedManifestDigest $ExpectedManifestDigest | Out-Null
    }
    catch { $failure = $_ }
    Assert-Contract ($null -ne $failure) "Equivalence mutation '$Name' must fail."
    if (-not [string]::IsNullOrWhiteSpace($ExpectedMessage)) {
        Assert-Contract ($failure.Exception.Message.Contains($ExpectedMessage, [StringComparison]::OrdinalIgnoreCase)) "Equivalence mutation '$Name' failed for the wrong reason: $($failure.Exception.Message)"
    }
    Assert-Contract (Test-Path -LiteralPath $reportPath -PathType Leaf) "Equivalence mutation '$Name' must atomically write a failure report."
    $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json -Depth 50
    Assert-Contract ([string]::Equals([string]$report.status, 'failed', [StringComparison]::Ordinal)) "Equivalence mutation '$Name' report must be failed."
    Assert-Contract ([string]::Equals([string]$report.failureClassification, $Classification, [StringComparison]::Ordinal)) "Equivalence mutation '$Name' must report stable classification '$Classification', observed '$($report.failureClassification)'."
    Assert-Contract (@(Get-ChildItem -LiteralPath (Split-Path -Parent $reportPath) -Filter ".$([IO.Path]::GetFileName($reportPath)).*.tmp" -File).Count -eq 0) "Equivalence mutation '$Name' must leave no partial report."
}

try {
    Assert-Contract (Test-Path -LiteralPath $libraryPath -PathType Leaf) 'Two-track equivalence library must exist.'
    Assert-Contract (Test-Path -LiteralPath $runnerPath -PathType Leaf) 'Two-track equivalence CLI must exist.'
    . (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')
    . $libraryPath
    [IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null

    $manifest = Import-NervAcceptanceScenarioMatrixManifest -ManifestPath $manifestPath -V1ManifestPath $v1ManifestPath -RepositoryRoot $repoRoot
    $script:manifestDigest = Get-NervAcceptanceManifestDigest -ManifestPath $manifestPath
    $rawManifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -Depth 50
    $rawV1Manifest = Get-Content -LiteralPath $v1ManifestPath -Raw | ConvertFrom-Json -Depth 50
    $runtimeManifest = Assert-NervAcceptanceRuntimeManifestObject -Manifest $rawManifest -V1Manifest $rawV1Manifest -RepositoryRoot $repoRoot
    $selection = [pscustomobject][ordered]@{
        selectionMode = 'main-active-core'
        reasons = @('main')
        scenarios = @($runtimeManifest.scenarios | Where-Object {
                [string]::Equals([string]$_.status, 'active', [StringComparison]::Ordinal) -and
                [string]::Equals([string]$_.tier, 'core', [StringComparison]::Ordinal)
            })
    }
    $projects = @(Get-NervAcceptancePlanningProjects -Scenarios $selection.scenarios)
    $discovered = [Collections.Generic.Dictionary[string, string[]]]::new([StringComparer]::Ordinal)
    foreach ($project in $projects) { $discovered.Add([string]$project.path, [string[]]@($project.expectedTestIdentities)) }
    $planning = New-NervAcceptancePlanningArtifact `
        -Manifest $manifest `
        -Selection $selection `
        -Projects $projects `
        -DiscoveredByProject $discovered `
        -Repository 'Mang-X/Nerv-IIP' `
        -TestedSha '0123456789abcdef0123456789abcdef01234567' `
        -RunId '123456789' `
        -RunAttempt 2 `
        -ManifestPath 'scripts/acceptance-scenario-matrix.json' `
        -ManifestDigest $script:manifestDigest `
        -Event push
    $script:planningPath = Join-Path $fixtureRoot 'planning.json'
    Write-JsonFixture -Path $script:planningPath -Value $planning
    $script:planningDigest = Get-FileDigest -Path $script:planningPath

    $resultPaths = @()
    foreach ($track in @('v1', 'shadow')) {
        $path = Join-Path $fixtureRoot "$track.json"
        Write-JsonFixture -Path $path -Value (New-CanonicalResult -Track $track -ManifestDigest $script:manifestDigest -VolatileMarker $track)
        $resultPaths += $path
    }

    $successReportPath = Join-Path $fixtureRoot 'success-report.json'
    $success = Invoke-ComparatorFixture -ResultPaths $resultPaths -ReportPath $successReportPath
    Assert-Contract ([string]::Equals([string]$success.status, 'passed', [StringComparison]::Ordinal)) 'Two valid tracks with only volatile differences must pass.'
    Assert-Contract ([string]::Equals((@($success.tracks.track) -join '|'), 'v1|shadow', [StringComparison]::Ordinal)) 'The report must retain the exact governed track order.'
    Assert-Contract (@($success.tracks | Where-Object { [string]$_.canonicalResultDigest -cnotmatch '^[0-9a-f]{64}$' -or [string]$_.stableVectorDigest -cnotmatch '^[0-9a-f]{64}$' }).Count -eq 0) 'Every track must publish canonical-result and stable-vector digests.'
    $stableVectorDigests = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($stableVectorDigest in @($success.tracks.stableVectorDigest)) {
        [void]$stableVectorDigests.Add([string]$stableVectorDigest)
    }
    Assert-Contract ($stableVectorDigests.Count -eq 1) 'Allowed volatile drift must leave one common stable-vector digest.'
    $successReportText = [IO.File]::ReadAllText($successReportPath)
    Assert-Contract (-not $successReportText.Contains('business-secret-value', [StringComparison]::Ordinal)) 'The report must not retain volatile path values.'
    Assert-Contract (-not $successReportText.Contains('databaseName', [StringComparison]::Ordinal)) 'The report must not serialize the volatile object.'

    $mixedPlanning = Copy-JsonObject $planning
    $mixedPlanning.runAttempt = 1
    $mixedPlanningPath = Join-Path $fixtureRoot 'mixed-planning.json'
    Write-JsonFixture -Path $mixedPlanningPath -Value $mixedPlanning
    $mixedResultPaths = @()
    foreach ($mixedTrack in @(
            @{ Track = 'v1'; Attempt = 2 },
            @{ Track = 'shadow'; Attempt = 1 }
        )) {
        $mixedPath = Join-Path $fixtureRoot "mixed-$($mixedTrack.Track).json"
        Write-JsonFixture -Path $mixedPath -Value (New-CanonicalResult -Track $mixedTrack.Track -ManifestDigest $script:manifestDigest -VolatileMarker "mixed-$($mixedTrack.Track)" -RunAttempt $mixedTrack.Attempt)
        $mixedResultPaths += $mixedPath
    }
    $mixedReportPath = Join-Path $fixtureRoot 'mixed-attempt-report.json'
    $mixed = Invoke-ComparatorFixture `
        -ArtifactPath $mixedPlanningPath `
        -ExpectedArtifactDigest (Get-FileDigest -Path $mixedPlanningPath) `
        -PlanningRunAttempt 1 `
        -V1RunAttempt 2 `
        -ShadowRunAttempt 1 `
        -RunAttempt 2 `
        -ResultPaths $mixedResultPaths `
        -ReportPath $mixedReportPath
    Assert-Contract ([string]::Equals([string]$mixed.status, 'passed', [StringComparison]::Ordinal)) 'Mixed physical producer attempts with one common run/SHA/manifest and stable vector must pass equivalence.'
    Assert-Contract ($mixed.provenance.runAttempt -eq 2 -and $mixed.planning.sourceRunAttempt -eq 1) 'The equivalence report must distinguish comparison attempt 2 from planning source attempt 1.'
    Assert-Contract ([string]::Equals((@($mixed.tracks | ForEach-Object { "$($_.track):$($_.sourceRunAttempt)" }) -join '|'), 'v1:2|shadow:1', [StringComparison]::Ordinal)) 'The equivalence report must retain the exact physical source attempt for every governed track.'
    $mixedReportText = [IO.File]::ReadAllText($mixedReportPath)
    Assert-Contract (-not $mixedReportText.Contains('mixed-v1', [StringComparison]::Ordinal) -and -not $mixedReportText.Contains('databaseName', [StringComparison]::Ordinal)) 'Mixed-attempt reporting must not serialize volatile markers or objects.'

    foreach ($attemptMutation in @(
            @{ Name = 'v1-source-attempt'; V1 = 1; Shadow = 1 },
            @{ Name = 'shadow-source-attempt'; V1 = 2; Shadow = 2 }
        )) {
        $attemptMutationReportPath = Join-Path $fixtureRoot "$($attemptMutation.Name)-report.json"
        $attemptMutationFailure = $null
        try {
            Invoke-ComparatorFixture -ArtifactPath $mixedPlanningPath -ExpectedArtifactDigest (Get-FileDigest -Path $mixedPlanningPath) -PlanningRunAttempt 1 -V1RunAttempt $attemptMutation.V1 -ShadowRunAttempt $attemptMutation.Shadow -RunAttempt 2 -ResultPaths $mixedResultPaths -ReportPath $attemptMutationReportPath | Out-Null
        }
        catch { $attemptMutationFailure = $_ }
        Assert-Contract ($null -ne $attemptMutationFailure -and $attemptMutationFailure.Exception.Message.Contains('provenance runAttempt', [StringComparison]::Ordinal)) "Equivalence must reject mismatched $($attemptMutation.Name)."
    }

    $swappedTrackReportPath = Join-Path $fixtureRoot 'swapped-track-binding-report.json'
    $swappedTrackFailure = $null
    try {
        Invoke-ComparatorFixture -ArtifactPath $mixedPlanningPath -ExpectedArtifactDigest (Get-FileDigest -Path $mixedPlanningPath) -PlanningRunAttempt 1 -V1RunAttempt 1 -ShadowRunAttempt 2 -RunAttempt 2 -ResultPaths @($mixedResultPaths[1], $mixedResultPaths[0]) -ReportPath $swappedTrackReportPath | Out-Null
    }
    catch { $swappedTrackFailure = $_ }
    Assert-Contract ($null -ne $swappedTrackFailure -and $swappedTrackFailure.Exception.Message.Contains("must identify track 'v1'", [StringComparison]::Ordinal)) 'Equivalence must reject swapped producer path/track/attempt bindings instead of reclassifying by artifact self-report.'

    $wrongPlanningSourceReportPath = Join-Path $fixtureRoot 'wrong-planning-source-attempt-report.json'
    $wrongPlanningSourceFailure = $null
    try {
        Invoke-ComparatorFixture -ArtifactPath $mixedPlanningPath -ExpectedArtifactDigest (Get-FileDigest -Path $mixedPlanningPath) -PlanningRunAttempt 2 -V1RunAttempt 2 -ShadowRunAttempt 1 -RunAttempt 2 -ResultPaths $mixedResultPaths -ReportPath $wrongPlanningSourceReportPath | Out-Null
    }
    catch { $wrongPlanningSourceFailure = $_ }
    Assert-Contract ($null -ne $wrongPlanningSourceFailure -and $wrongPlanningSourceFailure.Exception.Message.Contains('Planning artifact runAttempt does not match expected provenance.', [StringComparison]::Ordinal)) 'Equivalence must reject a planning artifact whose source attempt output is wrong.'

    $drifted = Get-Content -LiteralPath $resultPaths[1] -Raw | ConvertFrom-Json -Depth 50 -DateKind String
    $drifted.businessFacts.changeV2Converged = $false
    Write-JsonFixture -Path $resultPaths[1] -Value $drifted
    Assert-RejectedWithReport -Name 'stable-business-drift' -ResultPaths $resultPaths -Classification 'stable-vector-drift' -ExpectedMessage 'stable equivalence vector'
    Write-JsonFixture -Path $resultPaths[1] -Value (New-CanonicalResult -Track shadow -ManifestDigest $script:manifestDigest -VolatileMarker shadow)

    $missingTrackPath = Join-Path $fixtureRoot 'missing-shadow.json'
    Assert-RejectedWithReport -Name 'missing-track-file' -ResultPaths @($resultPaths[0], $missingTrackPath) -Classification 'track-result-invalid' -ExpectedMessage 'existing file'
    Write-JsonFixture -Path $resultPaths[1] -Value (New-CanonicalResult -Track extra -ManifestDigest $script:manifestDigest -VolatileMarker extra)
    Assert-RejectedWithReport -Name 'unexpected-track-binding' -ResultPaths $resultPaths -Classification 'track-result-invalid' -ExpectedMessage "must identify track 'shadow'"
    Write-JsonFixture -Path $resultPaths[1] -Value (New-CanonicalResult -Track shadow -ManifestDigest $script:manifestDigest -VolatileMarker shadow)
    $duplicateTrack = Get-Content -LiteralPath $resultPaths[1] -Raw | ConvertFrom-Json -Depth 50 -DateKind String
    $duplicateTrack.track = 'v1'
    Write-JsonFixture -Path $resultPaths[1] -Value $duplicateTrack
    Assert-RejectedWithReport -Name 'duplicate-track' -ResultPaths $resultPaths -Classification 'track-result-invalid' -ExpectedMessage "must identify track 'shadow'"
    Write-JsonFixture -Path $resultPaths[1] -Value (New-CanonicalResult -Track shadow -ManifestDigest $script:manifestDigest -VolatileMarker shadow)

    $wrongProvenance = Get-Content -LiteralPath $resultPaths[0] -Raw | ConvertFrom-Json -Depth 50 -DateKind String
    $wrongProvenance.provenance.runId = '999'
    Write-JsonFixture -Path $resultPaths[0] -Value $wrongProvenance
    Assert-RejectedWithReport -Name 'provenance-drift' -ResultPaths $resultPaths -Classification 'track-result-invalid' -ExpectedMessage 'provenance runId'
    Write-JsonFixture -Path $resultPaths[0] -Value (New-CanonicalResult -Track v1 -ManifestDigest $script:manifestDigest -VolatileMarker v1)

    [IO.File]::WriteAllText($resultPaths[1], '{"track":', [Text.UTF8Encoding]::new($false))
    Assert-RejectedWithReport -Name 'malformed-result' -ResultPaths $resultPaths -Classification 'track-result-invalid' -ExpectedMessage 'JSON'
    Write-JsonFixture -Path $resultPaths[1] -Value (New-CanonicalResult -Track shadow -ManifestDigest $script:manifestDigest -VolatileMarker shadow)

    $failed = Get-Content -LiteralPath $resultPaths[1] -Raw | ConvertFrom-Json -Depth 50 -DateKind String
    $failed.conclusion = 'failed'
    Write-JsonFixture -Path $resultPaths[1] -Value $failed
    Assert-RejectedWithReport -Name 'failed-track' -ResultPaths $resultPaths -Classification 'stable-vector-drift' -ExpectedMessage 'stable equivalence vector'
    Write-JsonFixture -Path $resultPaths[1] -Value (New-CanonicalResult -Track shadow -ManifestDigest $script:manifestDigest -VolatileMarker shadow)

    $skipped = Get-Content -LiteralPath $resultPaths[1] -Raw | ConvertFrom-Json -Depth 50 -DateKind String
    $skipped.test.passed = 0
    $skipped.test.skipped = 1
    Write-JsonFixture -Path $resultPaths[1] -Value $skipped
    Assert-RejectedWithReport -Name 'skipped-track' -ResultPaths $resultPaths -Classification 'stable-vector-drift' -ExpectedMessage 'stable equivalence vector'
    Write-JsonFixture -Path $resultPaths[1] -Value (New-CanonicalResult -Track shadow -ManifestDigest $script:manifestDigest -VolatileMarker shadow)

    $cleanupIncomplete = Get-Content -LiteralPath $resultPaths[1] -Raw | ConvertFrom-Json -Depth 50 -DateKind String
    $cleanupIncomplete.cleanup.ownedResourcesRemaining = 1
    Write-JsonFixture -Path $resultPaths[1] -Value $cleanupIncomplete
    Assert-RejectedWithReport -Name 'cleanup-incomplete' -ResultPaths $resultPaths -Classification 'stable-vector-drift' -ExpectedMessage 'stable equivalence vector'
    Write-JsonFixture -Path $resultPaths[1] -Value (New-CanonicalResult -Track shadow -ManifestDigest $script:manifestDigest -VolatileMarker shadow)

    Assert-RejectedWithReport -Name 'artifact-digest-drift' -ResultPaths $resultPaths -Classification 'planning-input-invalid' -ExpectedMessage 'bytes do not match' -ExpectedArtifactDigest ('0' * 64)
    Assert-RejectedWithReport -Name 'manifest-digest-drift' -ResultPaths $resultPaths -Classification 'planning-input-invalid' -ExpectedMessage 'bytes do not match' -ExpectedManifestDigest ('0' * 64)

    $cliReportPath = Join-Path $fixtureRoot 'cli-report.json'
    Invoke-PwshScript `
        -ScriptPath $runnerPath `
        -Arguments @(
            '-ArtifactPath', $script:planningPath,
            '-ExpectedArtifactDigest', $script:planningDigest,
            '-ManifestFilePath', $manifestPath,
            '-ExpectedManifestDigest', $script:manifestDigest,
            '-V1ManifestPath', $v1ManifestPath,
            '-RepositoryRoot', $repoRoot,
            '-Repository', 'Mang-X/Nerv-IIP',
            '-TestedSha', '0123456789abcdef0123456789abcdef01234567',
            '-RunId', '123456789',
            '-RunAttempt', '2',
            '-ManifestRepositoryPath', 'scripts/acceptance-scenario-matrix.json',
            '-Event', 'push',
            '-V1ResultPath', $resultPaths[0],
            '-ShadowResultPath', $resultPaths[1],
            '-ReportPath', $cliReportPath
        ) `
        -WorkingDirectory $repoRoot `
        -TimeoutSeconds 60 `
        -Name 'acceptance-equivalence-cli-fixture' | Out-Null
    Assert-Contract (Test-Path -LiteralPath $cliReportPath -PathType Leaf) 'The governed CLI must publish a passing report.'

    $runnerCommand = Get-Command -Name $runnerPath -CommandType ExternalScript -ErrorAction Stop
    Assert-Contract (-not $runnerCommand.Parameters.ContainsKey('LegacyErpResultPath')) 'The two-track CLI must not expose the retired LegacyErpResultPath parameter.'
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
}

Write-Output 'Acceptance scenario matrix two-track equivalence contract tests passed.'
