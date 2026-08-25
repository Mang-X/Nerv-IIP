# Script-Governance:
#   Category: library, check
#   SideEffects:
#     - Reads governed planning, manifest, v1 authority, and canonical result JSON files supplied by the caller
#   Writes:
#     - A caller-declared machine-readable equivalence report through atomic file replacement
#   Cleanup:
#     - Removes owned temporary report files after every persistence attempt
#   Requires:
#     - PowerShell 7

. (Join-Path $PSScriptRoot 'AcceptanceScenarioMatrixRuntime.ps1')

function Get-NervAcceptanceEquivalenceValueDigest {
    param([Parameter(Mandatory)] [object] $Value)

    $bytes = [Text.UTF8Encoding]::new($false).GetBytes(($Value | ConvertTo-Json -Depth 50 -Compress))
    $sha256 = [Security.Cryptography.SHA256]::Create()
    try { return ([Convert]::ToHexString($sha256.ComputeHash($bytes))).ToLowerInvariant() }
    finally { $sha256.Dispose() }
}

function Get-NervAcceptanceEquivalenceStableVectorDigest {
    param([Parameter(Mandatory)] [object] $Vector)

    $stableVector = $Vector | ConvertTo-Json -Depth 50 | ConvertFrom-Json -Depth 50 -DateKind String
    [void]$stableVector.provenance.PSObject.Properties.Remove('runAttempt')
    return Get-NervAcceptanceEquivalenceValueDigest -Value $stableVector
}

function New-NervAcceptanceEquivalenceReport {
    param(
        [Parameter(Mandatory)] [string] $Status,
        [AllowNull()] [string] $FailureClassification,
        [AllowNull()] [object] $Provenance,
        [AllowNull()] [string] $ArtifactDigest,
        [AllowNull()] [string] $ManifestDigest,
        [Parameter(Mandatory)] [string] $ScenarioId,
        [Parameter(Mandatory)] [int] $PlanningRunAttempt,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Tracks
    )

    return [pscustomobject][ordered]@{
        schemaVersion = 1
        status = $Status
        failureClassification = $FailureClassification
        provenance = $Provenance
        planning = [pscustomobject][ordered]@{
            artifactDigest = $ArtifactDigest
            manifestDigest = $ManifestDigest
            scenarioId = $ScenarioId
            sourceRunAttempt = $PlanningRunAttempt
        }
        tracks = @($Tracks)
    }
}

function Invoke-NervAcceptanceScenarioMatrixEquivalence {
    param(
        [Parameter(Mandatory)] [AllowNull()] [AllowEmptyString()] [string] $ArtifactPath,
        [Parameter(Mandatory)] [AllowNull()] [AllowEmptyString()] [string] $ExpectedArtifactDigest,
        [Parameter(Mandatory)] [AllowNull()] [AllowEmptyString()] [string] $ManifestFilePath,
        [Parameter(Mandatory)] [AllowNull()] [AllowEmptyString()] [string] $ExpectedManifestDigest,
        [Parameter(Mandatory)] [AllowNull()] [AllowEmptyString()] [string] $V1ManifestPath,
        [Parameter(Mandatory)] [AllowNull()] [AllowEmptyString()] [string] $RepositoryRoot,
        [Parameter(Mandatory)] [AllowNull()] [AllowEmptyString()] [string] $Repository,
        [Parameter(Mandatory)] [AllowNull()] [AllowEmptyString()] [string] $TestedSha,
        [Parameter(Mandatory)] [AllowNull()] [AllowEmptyString()] [string] $RunId,
        [Parameter(Mandatory)] [int] $RunAttempt,
        [int] $PlanningRunAttempt = $RunAttempt,
        [Parameter(Mandatory)] [AllowNull()] [AllowEmptyString()] [string] $ManifestRepositoryPath,
        [Parameter(Mandatory)] [AllowNull()] [AllowEmptyString()] [string] $Event,
        [string] $ScenarioId = 'sales-order-demand',
        [Parameter(Mandatory)] [AllowNull()] [AllowEmptyString()] [string] $V1ResultPath,
        [int] $V1RunAttempt = $RunAttempt,
        [Parameter(Mandatory)] [AllowNull()] [AllowEmptyString()] [string] $ShadowResultPath,
        [int] $ShadowRunAttempt = $RunAttempt,
        [Parameter(Mandatory)] [string] $ReportPath
    )

    $failureClassification = 'planning-input-invalid'
    $artifactDigest = $null
    $manifestDigest = $null
    $reportTracks = [Collections.Generic.List[object]]::new()
    $provenance = $null
    try {
        if ($ExpectedArtifactDigest -cnotmatch '^[0-9a-f]{64}$') { throw 'Acceptance equivalence artifact digest must be a lowercase SHA-256 digest.' }
        if ($ExpectedManifestDigest -cnotmatch '^[0-9a-f]{64}$') { throw 'Acceptance equivalence manifest digest must be a lowercase SHA-256 digest.' }
        if (-not (Test-NervAcceptanceRepositoryIdentifier -Repository $Repository)) { throw 'Acceptance equivalence repository must be a canonical owner/name identifier.' }
        if ($TestedSha -cnotmatch '^[0-9a-f]{40}$') { throw 'Acceptance equivalence tested SHA must be a lowercase 40-character Git SHA.' }
        if ($RunId -cnotmatch '^[1-9][0-9]*$') { throw 'Acceptance equivalence run id must be a canonical positive decimal identifier.' }
        if ($RunAttempt -le 0) { throw 'Acceptance equivalence run attempt must be positive.' }
        foreach ($sourceAttempt in @(
                @{ Name = 'planning'; Value = $PlanningRunAttempt },
                @{ Name = 'v1'; Value = $V1RunAttempt },
                @{ Name = 'shadow'; Value = $ShadowRunAttempt }
            )) {
            if ([int]$sourceAttempt.Value -le 0) {
                throw "Acceptance equivalence $($sourceAttempt.Name) source run attempt must be positive."
            }
        }
        if (-not (Test-NervAcceptanceChangedPath -Path $ManifestRepositoryPath)) { throw 'Acceptance equivalence manifest repository path must be canonical.' }
        [void](Assert-NervAcceptanceScenarioRuntimeInvocation -RunAttempt ([string]$RunAttempt) -Event $Event)

        $authorityPaths = Assert-NervAcceptanceRuntimeAuthorityPaths `
            -RepositoryRoot $RepositoryRoot `
            -ManifestPath $ManifestRepositoryPath `
            -ManifestFilePath $ManifestFilePath `
            -V1ManifestPath $V1ManifestPath
        $artifactSnapshot = Read-NervAcceptanceRuntimeJsonSnapshot -Path $ArtifactPath -ExpectedDigest $ExpectedArtifactDigest -Context 'equivalence planning artifact'
        $manifestSnapshot = Read-NervAcceptanceRuntimeJsonSnapshot -Path $authorityPaths.manifestPath -ExpectedDigest $ExpectedManifestDigest -Context 'equivalence acceptance manifest'
        $v1ManifestSnapshot = Read-NervAcceptanceRuntimeJsonSnapshot -Path $authorityPaths.v1ManifestPath -Context 'equivalence FullChain v1 manifest'
        $artifactDigest = [string]$artifactSnapshot.digest
        $manifestDigest = [string]$manifestSnapshot.digest
        $manifest = Assert-NervAcceptanceRuntimeManifestObject -Manifest $manifestSnapshot.value -V1Manifest $v1ManifestSnapshot.value -RepositoryRoot $authorityPaths.repositoryRoot
        $selection = Get-NervAcceptanceRuntimeArtifactSelection -Artifact $artifactSnapshot.value -Manifest $manifest -Event $Event
        [void](Assert-NervAcceptancePlanningArtifact `
            -Artifact $artifactSnapshot.value `
            -Manifest $manifest `
            -Selection $selection `
            -Repository $Repository `
            -TestedSha $TestedSha `
            -RunId $RunId `
            -RunAttempt $PlanningRunAttempt `
            -ManifestPath $ManifestRepositoryPath `
            -ManifestDigest $ExpectedManifestDigest `
            -Event $Event)
        $adapter = Get-NervAcceptanceRuntimeScenarioAdapter -ScenarioId $ScenarioId
        $scenarioSelections = @($selection.scenarios | Where-Object {
                [string]::Equals([string]$_.id, [string]$adapter.scenarioId, [StringComparison]::Ordinal) -and
                [string]::Equals([string]$_.status, 'active', [StringComparison]::Ordinal) -and
                [string]::Equals([string]$_.tier, 'core', [StringComparison]::Ordinal)
            })
        if ($scenarioSelections.Count -ne 1) { throw "Acceptance equivalence planning must select exactly one active/core '$ScenarioId' scenario." }
        $scenario = Get-NervAcceptanceRuntimeScenario -Manifest $manifest -ScenarioId $ScenarioId
        $provenance = [pscustomobject][ordered]@{
            repository = $Repository
            runId = $RunId
            runAttempt = $RunAttempt
            testedSha = $TestedSha
            manifestDigest = $ExpectedManifestDigest
            scenarioId = $ScenarioId
        }

        $vectorsByTrack = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
        $resultDescriptors = @(
            [pscustomobject]@{ track = 'v1'; path = $V1ResultPath; sourceRunAttempt = $V1RunAttempt },
            [pscustomobject]@{ track = 'shadow'; path = $ShadowResultPath; sourceRunAttempt = $ShadowRunAttempt }
        )
        foreach ($descriptor in $resultDescriptors) {
            $failureClassification = 'track-result-invalid'
            $resultSnapshot = Read-NervAcceptanceRuntimeJsonSnapshot -Path $descriptor.path -Context "equivalence $($descriptor.track) canonical result"
            if (-not [string]::Equals([string]$resultSnapshot.value.track, [string]$descriptor.track, [StringComparison]::Ordinal)) {
                throw "Acceptance equivalence input must identify track '$($descriptor.track)'."
            }
            $expectedTrackProvenance = [pscustomobject][ordered]@{
                repository = $Repository
                runId = $RunId
                runAttempt = [int]$descriptor.sourceRunAttempt
                testedSha = $TestedSha
                manifestDigest = $ExpectedManifestDigest
                scenarioId = $ScenarioId
            }
            $vector = New-NervAcceptanceScenarioEquivalenceVector -Result $resultSnapshot.value -ValidatedScenario $scenario -ExpectedProvenance $expectedTrackProvenance
            $track = [string]$descriptor.track
            if ($vectorsByTrack.ContainsKey($track)) {
                $failureClassification = 'track-set-invalid'
                throw "Acceptance equivalence track set contains duplicate track '$track'."
            }
            $vectorsByTrack.Add($track, $vector)
            $reportTracks.Add([pscustomobject][ordered]@{
                    track = $track
                    sourceRunAttempt = [int]$descriptor.sourceRunAttempt
                    canonicalResultDigest = [string]$resultSnapshot.digest
                    stableVectorDigest = Get-NervAcceptanceEquivalenceStableVectorDigest -Vector $vector
                })
        }

        $governedTracks = [string[]]@('v1', 'shadow')
        $observedTracks = [string[]]@($vectorsByTrack.Keys)
        [Array]::Sort($observedTracks, [StringComparer]::Ordinal)
        $expectedTracks = [string[]]@($governedTracks)
        [Array]::Sort($expectedTracks, [StringComparer]::Ordinal)
        $failureClassification = 'track-set-invalid'
        if (-not (Test-NervAcceptanceOrdinalSequenceEqual -Left $observedTracks -Right $expectedTracks)) {
            throw "Acceptance equivalence track set must be exactly 'v1' and 'shadow'."
        }

        $orderedReportTracks = [Collections.Generic.List[object]]::new()
        foreach ($track in $governedTracks) {
            $orderedReportTracks.Add(@($reportTracks | Where-Object { [string]::Equals([string]$_.track, $track, [StringComparison]::Ordinal) })[0])
        }
        $reportTracks = $orderedReportTracks

        $failureClassification = 'stable-vector-drift'
        $stableDigests = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($reportTrack in $reportTracks) {
            [void]$stableDigests.Add([string]$reportTrack.stableVectorDigest)
        }
        if ($stableDigests.Count -ne 1) { throw 'Acceptance equivalence stable equivalence vectors drifted across tracks.' }

        $failureClassification = 'track-result-invalid'
        foreach ($track in $governedTracks) { [void](Assert-NervAcceptanceScenarioRuntimeResult -ResultSnapshot $vectorsByTrack[$track]) }

        $report = New-NervAcceptanceEquivalenceReport `
            -Status passed `
            -FailureClassification $null `
            -Provenance $provenance `
            -ArtifactDigest $artifactDigest `
            -ManifestDigest $manifestDigest `
            -ScenarioId $ScenarioId `
            -PlanningRunAttempt $PlanningRunAttempt `
            -Tracks $reportTracks.ToArray()
        Write-NervAcceptanceScenarioRuntimeSummary -Summary $report -Path $ReportPath
        return $report
    }
    catch {
        $failure = $_
        $report = New-NervAcceptanceEquivalenceReport `
            -Status failed `
            -FailureClassification $failureClassification `
            -Provenance $provenance `
            -ArtifactDigest $artifactDigest `
            -ManifestDigest $manifestDigest `
            -ScenarioId $ScenarioId `
            -PlanningRunAttempt $PlanningRunAttempt `
            -Tracks $reportTracks.ToArray()
        Write-NervAcceptanceScenarioRuntimeSummary -Summary $report -Path $ReportPath
        throw $failure
    }
}
