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

function New-NervAcceptanceEquivalenceReport {
    param(
        [Parameter(Mandatory)] [string] $Status,
        [AllowNull()] [string] $FailureClassification,
        [AllowNull()] [object] $Provenance,
        [AllowNull()] [string] $ArtifactDigest,
        [AllowNull()] [string] $ManifestDigest,
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
            scenarioId = 'sales-order-demand'
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
        [Parameter(Mandatory)] [AllowNull()] [AllowEmptyString()] [string] $ManifestRepositoryPath,
        [Parameter(Mandatory)] [AllowNull()] [AllowEmptyString()] [string] $Event,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $ResultPaths,
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
            -RunAttempt $RunAttempt `
            -ManifestPath $ManifestRepositoryPath `
            -ManifestDigest $ExpectedManifestDigest `
            -Event $Event)
        $salesSelections = @($selection.scenarios | Where-Object {
                [string]::Equals([string]$_.id, 'sales-order-demand', [StringComparison]::Ordinal) -and
                [string]::Equals([string]$_.status, 'active', [StringComparison]::Ordinal) -and
                [string]::Equals([string]$_.tier, 'core', [StringComparison]::Ordinal)
            })
        if ($salesSelections.Count -ne 1) { throw "Acceptance equivalence planning must select exactly one active/core 'sales-order-demand' scenario." }
        $scenario = Get-NervAcceptanceSalesOrderRuntimeScenario -Manifest $manifest
        $provenance = [pscustomobject][ordered]@{
            repository = $Repository
            runId = $RunId
            runAttempt = $RunAttempt
            testedSha = $TestedSha
            manifestDigest = $ExpectedManifestDigest
            scenarioId = 'sales-order-demand'
        }

        $failureClassification = 'track-set-invalid'
        if (@($ResultPaths).Count -ne 3) { throw "Acceptance equivalence requires exactly three canonical results; observed $(@($ResultPaths).Count)." }

        $vectorsByTrack = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
        foreach ($resultPath in @($ResultPaths)) {
            $failureClassification = 'track-result-invalid'
            $resultSnapshot = Read-NervAcceptanceRuntimeJsonSnapshot -Path $resultPath -Context 'equivalence canonical result'
            $vector = New-NervAcceptanceScenarioEquivalenceVector -Result $resultSnapshot.value -ValidatedScenario $scenario -ExpectedProvenance $provenance
            $track = [string]$resultSnapshot.value.track
            if ($vectorsByTrack.ContainsKey($track)) {
                $failureClassification = 'track-set-invalid'
                throw "Acceptance equivalence track set contains duplicate track '$track'."
            }
            $vectorsByTrack.Add($track, $vector)
            $reportTracks.Add([pscustomobject][ordered]@{
                    track = $track
                    canonicalResultDigest = [string]$resultSnapshot.digest
                    stableVectorDigest = Get-NervAcceptanceEquivalenceValueDigest -Value $vector
                })
        }

        $governedTracks = [string[]]@('v1', 'shadow', 'legacy-erp')
        $observedTracks = [string[]]@($vectorsByTrack.Keys)
        [Array]::Sort($observedTracks, [StringComparer]::Ordinal)
        $expectedTracks = [string[]]@($governedTracks)
        [Array]::Sort($expectedTracks, [StringComparer]::Ordinal)
        $failureClassification = 'track-set-invalid'
        if (-not (Test-NervAcceptanceOrdinalSequenceEqual -Left $observedTracks -Right $expectedTracks)) {
            throw "Acceptance equivalence track set must be exactly 'v1', 'shadow', and 'legacy-erp'."
        }

        $orderedReportTracks = [Collections.Generic.List[object]]::new()
        foreach ($track in $governedTracks) {
            $orderedReportTracks.Add(@($reportTracks | Where-Object { [string]::Equals([string]$_.track, $track, [StringComparison]::Ordinal) })[0])
        }
        $reportTracks = $orderedReportTracks

        $failureClassification = 'stable-vector-drift'
        $stableDigests = @($reportTracks | ForEach-Object { [string]$_.stableVectorDigest } | Select-Object -Unique)
        if ($stableDigests.Count -ne 1) { throw 'Acceptance equivalence stable equivalence vectors drifted across tracks.' }

        $failureClassification = 'track-result-invalid'
        foreach ($track in $governedTracks) { [void](Assert-NervAcceptanceScenarioRuntimeResult -ResultSnapshot $vectorsByTrack[$track]) }

        $report = New-NervAcceptanceEquivalenceReport `
            -Status passed `
            -FailureClassification $null `
            -Provenance $provenance `
            -ArtifactDigest $artifactDigest `
            -ManifestDigest $manifestDigest `
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
            -Tracks $reportTracks.ToArray()
        Write-NervAcceptanceScenarioRuntimeSummary -Summary $report -Path $ReportPath
        throw $failure
    }
}
