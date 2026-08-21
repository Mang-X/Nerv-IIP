# Script-Governance:
#   Category: library
#   SideEffects:
#     - Reads caller-supplied full-stack protocol records and filesystem residue
#   Writes:
#     - None
#   Cleanup:
#     - Releases every opened-object handle before returning; owns no process or external resource
#   Requires:
#     - PowerShell 7

Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'FullStackControlProtocol.ps1')
. (Join-Path $PSScriptRoot 'FullStackControlFileSystem.ps1')
. (Join-Path $PSScriptRoot 'FullStackVerifiedRecordStore.ps1')

function Get-NervFullStackClassifierProperty {
    param(
        [AllowNull()]
        [object] $Record,

        [Parameter(Mandatory)]
        [string] $Name
    )

    if ($null -eq $Record -or $Record -is [System.Array]) {
        return $null
    }

    foreach ($property in $Record.PSObject.Properties) {
        if ([string]::Equals([string] $property.Name, $Name, [StringComparison]::Ordinal)) {
            return $property
        }
    }

    return $null
}

function Test-NervFullStackClassifierIntegerValue {
    [OutputType([bool])]
    param(
        [AllowNull()]
        [object] $Value,

        [Parameter(Mandatory)]
        [long] $Expected
    )

    return ($Value -is [byte] -or
        $Value -is [sbyte] -or
        $Value -is [int16] -or
        $Value -is [uint16] -or
        $Value -is [int32] -or
        $Value -is [uint32] -or
        $Value -is [int64] -or
        $Value -is [uint64]) -and [int64] $Value -eq $Expected
}

function Test-NervFullStackClassifierPositiveInteger {
    [OutputType([bool])]
    param([AllowNull()] [object] $Value)

    return ($Value -is [byte] -or
        $Value -is [sbyte] -or
        $Value -is [int16] -or
        $Value -is [uint16] -or
        $Value -is [int32] -or
        $Value -is [uint32] -or
        $Value -is [int64] -or
        $Value -is [uint64]) -and [int64] $Value -gt 0
}

function Test-NervFullStackClassifierExactString {
    [OutputType([bool])]
    param(
        [AllowNull()]
        [object] $Value,

        [Parameter(Mandatory)]
        [string] $Expected,

        [StringComparison] $Comparison = [StringComparison]::Ordinal
    )

    return $Value -is [string] -and [string]::Equals([string] $Value, $Expected, $Comparison)
}

function Test-NervFullStackClassifierPattern {
    [OutputType([bool])]
    param(
        [AllowNull()]
        [object] $Value,

        [Parameter(Mandatory)]
        [string] $Pattern
    )

    return $Value -is [string] -and [regex]::IsMatch(
        [string] $Value,
        $Pattern,
        [Text.RegularExpressions.RegexOptions]::CultureInvariant
    )
}

function Test-NervFullStackClassifierTimestamp {
    [OutputType([bool])]
    param([AllowNull()] [object] $Value)

    if ($Value -is [DateTime] -or $Value -is [DateTimeOffset]) {
        return $true
    }
    if ($Value -isnot [string] -or [string]::IsNullOrWhiteSpace([string] $Value)) {
        return $false
    }

    $parsed = [DateTimeOffset]::MinValue
    return [DateTimeOffset]::TryParseExact(
        [string] $Value,
        'O',
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind,
        [ref] $parsed
    )
}

function Read-NervFullStackClassifierJsonRecord {
    param(
        [Parameter(Mandatory)]
        [string] $StateRoot,

        [Parameter(Mandatory)]
        [string] $Path,

        [string] $RecordKind
    )

    $trustedTarget = Test-NervFullStackTrustedPathGraph `
        -StateRoot $StateRoot `
        -CandidatePath $Path `
        -ExpectedKind File

    if ($PSBoundParameters.ContainsKey('RecordKind')) {
        return Read-NervFullStackVerifiedRecord -VerifiedTarget $trustedTarget -RecordKind $RecordKind
    }

    $proof = Open-NervFullStackVerifiedPathHandle -TrustedPath $trustedTarget -Access Read
    try {
        if (-not [string]::Equals([string] $proof.Status, 'Verified', [StringComparison]::Ordinal) -or
            $null -eq $proof.Handle -or $proof.Handle.IsClosed -or $proof.Handle.IsInvalid) {
            throw 'classifier:opened-object-identity-required'
        }

        $rawBytes = Read-NervFullStackOpenedRecordBytes -Handle $proof.Handle
        try {
            $json = [System.Text.UTF8Encoding]::new($false, $true).GetString($rawBytes)
            $record = ConvertFrom-Json -InputObject $json -NoEnumerate -ErrorAction Stop
        }
        catch {
            throw "classifier:invalid-json '$($_.Exception.Message)'"
        }

        if ($null -eq $record -or $record -is [System.Array]) {
            throw 'classifier:invalid-record-shape'
        }

        return [pscustomobject][ordered]@{
            Verified = $true
            StateRoot = [string] $trustedTarget.StateRoot
            CandidatePath = [string] $trustedTarget.CandidatePath
            CanonicalPath = [string] $trustedTarget.CanonicalPath
            RawBytes = [byte[]] $rawBytes.Clone()
            Record = $record
            Identity = [pscustomobject][ordered]@{
                Provider = [string] $proof.Provider
                Key = [string] $proof.Identity.Key
                Device = $proof.Identity.Device
                Inode = $proof.Identity.Inode
                Kind = [string] $proof.Identity.Kind
            }
        }
    }
    finally {
        if ($null -ne $proof.Handle) {
            $proof.Handle.Dispose()
        }
    }
}

function Get-NervFullStackClassifierArrayCount {
    [OutputType([int])]
    param(
        [AllowNull()]
        [object] $Record,

        [Parameter(Mandatory)]
        [string] $Name
    )

    $property = Get-NervFullStackClassifierProperty -Record $Record -Name $Name
    if ($null -eq $property -or $null -eq $property.Value) {
        return -1
    }

    if ($property.Value -isnot [System.Array]) {
        return -1
    }

    return @($property.Value).Count
}

function Test-NervFullStackClassifierIdentityRecord {
    [OutputType([bool])]
    param(
        [AllowNull()]
        [object] $Identity,

        [bool] $RequireRole = $true
    )

    if ($null -eq $Identity -or $Identity -is [System.Array]) {
        return $false
    }

    $pidProperty = Get-NervFullStackClassifierProperty -Record $Identity -Name 'pid'
    $startProperty = Get-NervFullStackClassifierProperty -Record $Identity -Name 'processStartTimeUtc'
    $roleProperty = Get-NervFullStackClassifierProperty -Record $Identity -Name 'role'
    return $null -ne $pidProperty -and
        (Test-NervFullStackClassifierPositiveInteger -Value $pidProperty.Value) -and
        $null -ne $startProperty -and
        (Test-NervFullStackClassifierTimestamp -Value $startProperty.Value) -and
        (-not $RequireRole -or
            ($null -ne $roleProperty -and
                $roleProperty.Value -is [string] -and
                -not [string]::IsNullOrWhiteSpace([string] $roleProperty.Value)))
}

function Test-NervFullStackClassifierIdentityArray {
    [OutputType([bool])]
    param(
        [AllowNull()]
        [object] $Record,

        [Parameter(Mandatory)]
        [string] $Name
    )

    $property = Get-NervFullStackClassifierProperty -Record $Record -Name $Name
    if ($null -eq $property -or $property.Value -isnot [System.Array]) {
        return $false
    }

    foreach ($identity in @($property.Value)) {
        if (-not (Test-NervFullStackClassifierIdentityRecord -Identity $identity)) {
            return $false
        }
    }

    return $true
}

function Get-NervFullStackClassifierSessionReadback {
    param(
        [Parameter(Mandatory)]
        [string] $StateRoot,

        [Parameter(Mandatory)]
        [string] $SessionId
    )

    $pathSet = Get-NervFullStackControlPathSet -StateRoot $StateRoot -SessionId $SessionId
    [void] (Test-NervFullStackTrustedPathGraph -StateRoot $pathSet.StateRoot -CandidatePath $pathSet.StateRoot -ExpectedKind Directory)
    [void] (Test-NervFullStackTrustedPathGraph -StateRoot $pathSet.StateRoot -CandidatePath $pathSet.SessionsDirectory -ExpectedKind Directory)
    if ([System.IO.Directory]::Exists($pathSet.ControlsDirectory)) {
        [void] (Test-NervFullStackTrustedPathGraph -StateRoot $pathSet.StateRoot -CandidatePath $pathSet.ControlsDirectory -ExpectedKind Directory)
    }

    $authorityExists = [System.IO.File]::Exists($pathSet.AuthorityPath)
    $manifestExists = [System.IO.File]::Exists($pathSet.ManifestPath)
    $authoritySnapshot = $null
    $manifestSnapshot = $null
    $authorityError = $null
    $manifestError = $null

    if ($authorityExists) {
        try {
            $authoritySnapshot = Read-NervFullStackClassifierJsonRecord `
                -StateRoot $pathSet.StateRoot `
                -Path $pathSet.AuthorityPath `
                -RecordKind 'fullstack-session-authority'
        }
        catch {
            $authorityError = $_.Exception.Message
        }
    }
    if ($manifestExists) {
        try {
            $manifestSnapshot = Read-NervFullStackClassifierJsonRecord `
                -StateRoot $pathSet.StateRoot `
                -Path $pathSet.ManifestPath
        }
        catch {
            $manifestError = $_.Exception.Message
        }
    }

    $tempDirectories = [System.Collections.Generic.List[string]]::new()
    $tempPrefix = ".tmp-$SessionId-"
    if ([System.IO.Directory]::Exists($pathSet.ControlsDirectory)) {
        foreach ($candidate in [System.IO.Directory]::EnumerateDirectories($pathSet.ControlsDirectory, '*', [System.IO.SearchOption]::TopDirectoryOnly)) {
            $name = [System.IO.Path]::GetFileName($candidate)
            if ($name.StartsWith($tempPrefix, [StringComparison]::Ordinal)) {
                $nonce = $name.Substring($tempPrefix.Length)
                if (Test-NervFullStackClassifierPattern -Value $nonce -Pattern '^[a-f0-9]{32}$') {
                    [void] (Test-NervFullStackTrustedPathGraph -StateRoot $pathSet.StateRoot -CandidatePath $candidate -ExpectedKind Directory)
                    $tempDirectories.Add($candidate)
                }
            }
        }
    }

    return [pscustomobject][ordered]@{
        PathSet = $pathSet
        AuthorityExists = $authorityExists
        ManifestExists = $manifestExists
        AuthoritySnapshot = $authoritySnapshot
        ManifestSnapshot = $manifestSnapshot
        AuthorityError = $authorityError
        ManifestError = $manifestError
        TempDirectories = [string[]] $tempDirectories.ToArray()
    }
}

function Test-NervFullStackClassifierV2Readback {
    [OutputType([bool])]
    param(
        [Parameter(Mandatory)]
        [object] $Readback
    )

    if ($null -eq $Readback.AuthoritySnapshot -or $null -eq $Readback.ManifestSnapshot) {
        return $false
    }

    $authority = $Readback.AuthoritySnapshot.Record
    $manifest = $Readback.ManifestSnapshot.Record
    $pathSet = $Readback.PathSet

    $authoritySchema = Get-NervFullStackClassifierProperty -Record $authority -Name 'schemaVersion'
    $authorityKind = Get-NervFullStackClassifierProperty -Record $authority -Name 'kind'
    $authoritySession = Get-NervFullStackClassifierProperty -Record $authority -Name 'sessionId'
    $authorityNonce = Get-NervFullStackClassifierProperty -Record $authority -Name 'creationNonce'
    $authorityWorktree = Get-NervFullStackClassifierProperty -Record $authority -Name 'worktreeRoot'
    $authorityManifest = Get-NervFullStackClassifierProperty -Record $authority -Name 'manifestPath'
    $authorityCreatedBy = Get-NervFullStackClassifierProperty -Record $authority -Name 'createdBy'
    $authorityCreatedAt = Get-NervFullStackClassifierProperty -Record $authority -Name 'createdAtUtc'

    $manifestSchema = Get-NervFullStackClassifierProperty -Record $manifest -Name 'schemaVersion'
    $manifestProtocol = Get-NervFullStackClassifierProperty -Record $manifest -Name 'controlProtocolVersion'
    $manifestSession = Get-NervFullStackClassifierProperty -Record $manifest -Name 'sessionId'
    $manifestNonce = Get-NervFullStackClassifierProperty -Record $manifest -Name 'creationNonce'
    $manifestWorktree = Get-NervFullStackClassifierProperty -Record $manifest -Name 'worktreeRoot'
    $manifestState = Get-NervFullStackClassifierProperty -Record $manifest -Name 'state'
    $snapshotComplete = Get-NervFullStackClassifierProperty -Record $manifest -Name 'toolchainSnapshotComplete'
    $runtimeStartAttempted = Get-NervFullStackClassifierProperty -Record $manifest -Name 'runtimeStartAttempted'

    if ($null -eq $authoritySchema -or -not (Test-NervFullStackClassifierIntegerValue -Value $authoritySchema.Value -Expected 2) -or
        $null -eq $authorityKind -or -not (Test-NervFullStackClassifierExactString -Value $authorityKind.Value -Expected 'fullstack-session-authority') -or
        $null -eq $authoritySession -or -not (Test-NervFullStackClassifierExactString -Value $authoritySession.Value -Expected $pathSet.SessionId) -or
        $null -eq $authorityNonce -or -not (Test-NervFullStackClassifierPattern -Value $authorityNonce.Value -Pattern '^[a-f0-9]{32}$') -or
        $null -eq $authorityWorktree -or $authorityWorktree.Value -isnot [string] -or
        $null -eq $authorityManifest -or $authorityManifest.Value -isnot [string] -or
        $null -eq $authorityCreatedBy -or -not (Test-NervFullStackClassifierIdentityRecord -Identity $authorityCreatedBy.Value -RequireRole $false) -or
        $null -eq $authorityCreatedAt -or -not (Test-NervFullStackClassifierTimestamp -Value $authorityCreatedAt.Value)) {
        return $false
    }

    if ($null -eq $manifestSchema -or -not (Test-NervFullStackClassifierIntegerValue -Value $manifestSchema.Value -Expected 2) -or
        $null -eq $manifestProtocol -or -not (Test-NervFullStackClassifierIntegerValue -Value $manifestProtocol.Value -Expected 2) -or
        $null -eq $manifestSession -or -not (Test-NervFullStackClassifierExactString -Value $manifestSession.Value -Expected $pathSet.SessionId) -or
        $null -eq $manifestNonce -or -not (Test-NervFullStackClassifierExactString -Value $manifestNonce.Value -Expected ([string] $authorityNonce.Value)) -or
        $null -eq $manifestWorktree -or $manifestWorktree.Value -isnot [string] -or
        $null -eq $manifestState -or $manifestState.Value -isnot [string] -or [string]::IsNullOrWhiteSpace([string] $manifestState.Value) -or
        $null -eq $snapshotComplete -or $snapshotComplete.Value -isnot [bool] -or
        $null -eq $runtimeStartAttempted -or $runtimeStartAttempted.Value -isnot [bool] -or
        -not (Test-NervFullStackClassifierIdentityArray -Record $manifest -Name 'toolchainProbeIdentities') -or
        -not (Test-NervFullStackClassifierIdentityArray -Record $manifest -Name 'runtimeIdentities')) {
        return $false
    }

    $pathComparison = Get-NervFullStackPathComparison
    try {
        $authorityWorktreePath = Get-NervFullStackNormalizedFullPath -Path ([string] $authorityWorktree.Value)
        $manifestWorktreePath = Get-NervFullStackNormalizedFullPath -Path ([string] $manifestWorktree.Value)
        $authorityManifestPath = Get-NervFullStackNormalizedFullPath -Path ([string] $authorityManifest.Value)
        $expectedManifestPath = Get-NervFullStackNormalizedFullPath -Path $pathSet.ManifestPath
    }
    catch {
        return $false
    }

    return [string]::Equals($authorityWorktreePath, $manifestWorktreePath, $pathComparison) -and
        [string]::Equals($authorityManifestPath, $expectedManifestPath, $pathComparison)
}

function Get-NervFullStackProtocolGenerationObservation {
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)]
        [string] $StateRoot,

        [Parameter(Mandatory)]
        [string] $SessionId
    )

    $readback = Get-NervFullStackClassifierSessionReadback -StateRoot $StateRoot -SessionId $SessionId
    $warnings = [System.Collections.Generic.List[string]]::new()
    $generation = 'v0'
    $state = $null

    if ($readback.ManifestExists -and $null -eq $readback.ManifestSnapshot) {
        $generation = 'invalid'
        $warnings.Add("classifier:manifest-invalid '$SessionId' $($readback.ManifestError)")
    }
    elseif ($readback.AuthorityExists -and $null -eq $readback.AuthoritySnapshot) {
        $generation = 'invalid'
        $warnings.Add("classifier:authority-invalid '$SessionId' $($readback.AuthorityError)")
    }
    else {
        $manifest = if ($null -ne $readback.ManifestSnapshot) { $readback.ManifestSnapshot.Record } else { $null }
        if ($null -ne $manifest) {
            $stateProperty = Get-NervFullStackClassifierProperty -Record $manifest -Name 'state'
            if ($null -ne $stateProperty -and $stateProperty.Value -is [string]) {
                $state = [string] $stateProperty.Value
            }
        }

        if (Test-NervFullStackClassifierV2Readback -Readback $readback) {
            $generation = 'v2'
        }
        else {
            $protocolProperty = Get-NervFullStackClassifierProperty -Record $manifest -Name 'controlProtocolVersion'
            $nonceProperty = Get-NervFullStackClassifierProperty -Record $manifest -Name 'creationNonce'
            $sessionsDirectory = $readback.PathSet.SessionsDirectory
            $flatSidecars = @(
                Join-Path $sessionsDirectory "$SessionId.authority"
                Join-Path $sessionsDirectory "$SessionId.guardian-stop.request.json"
                Join-Path $sessionsDirectory "$SessionId.guardian-stop.ack.json"
            )
            $hasFlatPrototype = $false
            foreach ($sidecar in $flatSidecars) {
                if ([System.IO.File]::Exists($sidecar)) {
                    [void] (Test-NervFullStackTrustedPathGraph -StateRoot $readback.PathSet.StateRoot -CandidatePath $sidecar -ExpectedKind File)
                    $hasFlatPrototype = $true
                    $warnings.Add("classifier:prototype-v1-sidecar '$sidecar'")
                }
            }

            if ($hasFlatPrototype -or $null -ne $nonceProperty) {
                $generation = 'v1'
                if ($null -ne $nonceProperty) {
                    $warnings.Add("classifier:prototype-v1-nonce-bearing-manifest '$SessionId'")
                }
            }
            elseif ($readback.AuthorityExists -or
                ($null -ne $protocolProperty -and (Test-NervFullStackClassifierIntegerValue -Value $protocolProperty.Value -Expected 2))) {
                $generation = 'invalid'
                $warnings.Add("classifier:incomplete-v2-readback '$SessionId'")
            }
        }
    }

    Assert-NervFullStackProtocolValue -Domain 'generation' -Value $generation
    return [pscustomobject][ordered]@{
        Generation = $generation
        SessionId = $SessionId
        State = $state
        Warnings = [string[]] $warnings.ToArray()
        VerifiedAuthority = [bool] ($null -ne $readback.AuthoritySnapshot)
        VerifiedManifest = [bool] ($null -ne $readback.ManifestSnapshot)
    }
}

function Test-NervFullStackClassifierActivationMarker {
    [OutputType([bool])]
    param(
        [Parameter(Mandatory)]
        [object] $Marker,

        [Parameter(Mandatory)]
        [string] $StateRoot
    )

    $requiredExactStrings = [ordered]@{
        kind = 'fullstack-protocol-mode'
        e1CapabilityVersion = $null
        e3CapabilityVersion = $null
    }
    $schema = Get-NervFullStackClassifierProperty -Record $Marker -Name 'schemaVersion'
    $protocol = Get-NervFullStackClassifierProperty -Record $Marker -Name 'controlProtocolVersion'
    if ($null -eq $schema -or -not (Test-NervFullStackClassifierIntegerValue -Value $schema.Value -Expected 2) -or
        $null -eq $protocol -or -not (Test-NervFullStackClassifierIntegerValue -Value $protocol.Value -Expected 2)) {
        return $false
    }

    foreach ($entry in $requiredExactStrings.GetEnumerator()) {
        $property = Get-NervFullStackClassifierProperty -Record $Marker -Name $entry.Key
        if ($null -eq $property -or $property.Value -isnot [string] -or [string]::IsNullOrWhiteSpace([string] $property.Value)) {
            return $false
        }
        if ($null -ne $entry.Value -and -not (Test-NervFullStackClassifierExactString -Value $property.Value -Expected $entry.Value)) {
            return $false
        }
    }

    $stateRootProperty = Get-NervFullStackClassifierProperty -Record $Marker -Name 'stateRoot'
    if ($null -eq $stateRootProperty -or $stateRootProperty.Value -isnot [string]) {
        return $false
    }
    try {
        $observedRoot = Get-NervFullStackNormalizedFullPath -Path ([string] $stateRootProperty.Value)
        $expectedRoot = Get-NervFullStackNormalizedFullPath -Path $StateRoot
    }
    catch {
        return $false
    }
    if (-not [string]::Equals($observedRoot, $expectedRoot, (Get-NervFullStackPathComparison))) {
        return $false
    }

    $head = Get-NervFullStackClassifierProperty -Record $Marker -Name 'activatedFromHeadSha'
    $manifestHash = Get-NervFullStackClassifierProperty -Record $Marker -Name 'f1FrozenManifestHash'
    $evidenceHash = Get-NervFullStackClassifierProperty -Record $Marker -Name 'f1EvidenceHash'
    $nonce = Get-NervFullStackClassifierProperty -Record $Marker -Name 'activationNonce'
    $activatedAt = Get-NervFullStackClassifierProperty -Record $Marker -Name 'activatedAtUtc'
    return $null -ne $head -and (Test-NervFullStackClassifierPattern -Value $head.Value -Pattern '^[a-f0-9]{40}$') -and
        $null -ne $manifestHash -and (Test-NervFullStackClassifierPattern -Value $manifestHash.Value -Pattern '^[a-f0-9]{64}$') -and
        $null -ne $evidenceHash -and (Test-NervFullStackClassifierPattern -Value $evidenceHash.Value -Pattern '^[a-f0-9]{64}$') -and
        $null -ne $nonce -and (Test-NervFullStackClassifierPattern -Value $nonce.Value -Pattern '^[a-f0-9]{32}$') -and
        $null -ne $activatedAt -and (Test-NervFullStackClassifierTimestamp -Value $activatedAt.Value)
}

function Get-NervFullStackProtocolActivationObservation {
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)]
        [string] $StateRoot
    )

    $root = Get-NervFullStackNormalizedFullPath -Path $StateRoot
    [void] (Test-NervFullStackTrustedPathGraph -StateRoot $root -CandidatePath $root -ExpectedKind Directory)
    $markerPath = Join-Path $root 'fullstack-sessions/.protocol-mode.json'
    if (-not [System.IO.File]::Exists($markerPath)) {
        return [pscustomobject][ordered]@{
            Activation = 'GateOff'
            MarkerPath = $markerPath
            Warnings = [string[]] @()
        }
    }

    try {
        $snapshot = Read-NervFullStackClassifierJsonRecord `
            -StateRoot $root `
            -Path $markerPath `
            -RecordKind 'fullstack-protocol-mode'
        $activation = if (Test-NervFullStackClassifierActivationMarker -Marker $snapshot.Record -StateRoot $root) {
            'ActiveV2'
        }
        else {
            'InvalidMarker'
        }
        $warnings = if ([string]::Equals($activation, 'InvalidMarker', [StringComparison]::Ordinal)) {
            [string[]] @("classifier:invalid-activation-marker '$markerPath'")
        }
        else {
            [string[]] @()
        }
    }
    catch {
        $activation = 'InvalidMarker'
        $warnings = [string[]] @("classifier:invalid-activation-marker '$markerPath' $($_.Exception.Message)")
    }

    Assert-NervFullStackProtocolValue -Domain 'activation' -Value $activation
    return [pscustomobject][ordered]@{
        Activation = $activation
        MarkerPath = $markerPath
        Warnings = $warnings
    }
}

function Get-NervFullStackCompatibilityDisposition {
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)]
        [object] $GenerationObservation,

        [Parameter(Mandatory)]
        [object] $ActivationObservation
    )

    $generationProperty = Get-NervFullStackClassifierProperty -Record $GenerationObservation -Name 'Generation'
    $activationProperty = Get-NervFullStackClassifierProperty -Record $ActivationObservation -Name 'Activation'
    if ($null -eq $generationProperty -or $generationProperty.Value -isnot [string] -or
        -not (Test-NervFullStackProtocolValue -Domain 'generation' -Value ([string] $generationProperty.Value)) -or
        $null -eq $activationProperty -or $activationProperty.Value -isnot [string] -or
        -not (Test-NervFullStackProtocolValue -Domain 'activation' -Value ([string] $activationProperty.Value))) {
        throw 'compatibility:invalid-observation'
    }

    $generation = [string] $generationProperty.Value
    $activation = [string] $activationProperty.Value
    if ([string]::Equals($generation, 'invalid', [StringComparison]::Ordinal) -or
        [string]::Equals($activation, 'InvalidMarker', [StringComparison]::Ordinal)) {
        throw 'compatibility:invalid-observation'
    }

    $stateProperty = Get-NervFullStackClassifierProperty -Record $GenerationObservation -Name 'State'
    $state = if ($null -ne $stateProperty -and $stateProperty.Value -is [string]) { [string] $stateProperty.Value } else { $null }
    if ([string]::Equals($generation, 'v0', [StringComparison]::Ordinal)) {
        if ([string]::Equals($state, 'Stopped', [StringComparison]::Ordinal)) {
            $compatibility = 'legacy-stopped'
            $disposition = 'ReadOnlyLegacyStopped'
        }
        else {
            $compatibility = 'legacy-active-blocked'
            $disposition = 'BlockedLegacyActive'
        }
    }
    elseif ([string]::Equals($generation, 'v1', [StringComparison]::Ordinal)) {
        $compatibility = 'prototype-v1-untrusted'
        $disposition = 'BlockedPrototypeV1'
    }
    else {
        $compatibility = 'v2'
        $disposition = 'v2'
    }

    Assert-NervFullStackProtocolValue -Domain 'compatibility' -Value $compatibility
    return [pscustomobject][ordered]@{
        Compatibility = $compatibility
        Disposition = $disposition
        Generation = $generation
        Activation = $activation
    }
}

function Get-NervFullStackPublicationBoundaryObservation {
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)]
        [string] $StateRoot,

        [Parameter(Mandatory)]
        [string] $SessionId
    )

    $readback = Get-NervFullStackClassifierSessionReadback -StateRoot $StateRoot -SessionId $SessionId
    $warnings = [System.Collections.Generic.List[string]]::new()
    $boundary = $null

    if (-not $readback.AuthorityExists -and -not $readback.ManifestExists) {
        if ($readback.TempDirectories.Count -gt 0) {
            $boundary = 'temp-publication-residue'
            $warnings.Add("classifier:temp-publication-residue '$SessionId'")
        }
        else {
            $boundary = 'not-published'
        }
    }
    elseif ($readback.AuthorityExists -and -not $readback.ManifestExists) {
        $boundary = 'final-authority-only-init-incomplete'
        $warnings.Add("classifier:final-authority-only-init-incomplete '$SessionId'")
    }
    elseif ($null -eq $readback.ManifestSnapshot -or $null -eq $readback.AuthoritySnapshot) {
        $boundary = 'manifest-init-incomplete'
        $warnings.Add("classifier:manifest-init-incomplete '$SessionId'")
    }
    elseif (-not (Test-NervFullStackClassifierV2Readback -Readback $readback)) {
        $boundary = 'manifest-init-incomplete'
        $warnings.Add("classifier:manifest-init-incomplete '$SessionId'")
    }
    else {
        $manifest = $readback.ManifestSnapshot.Record
        $snapshotComplete = [bool] (Get-NervFullStackClassifierProperty -Record $manifest -Name 'toolchainSnapshotComplete').Value
        $probeCount = Get-NervFullStackClassifierArrayCount -Record $manifest -Name 'toolchainProbeIdentities'
        $runtimeStartAttempted = [bool] (Get-NervFullStackClassifierProperty -Record $manifest -Name 'runtimeStartAttempted').Value
        $runtimeIdentityCount = Get-NervFullStackClassifierArrayCount -Record $manifest -Name 'runtimeIdentities'

        if (-not $snapshotComplete) {
            $boundary = if ($probeCount -gt 0) { 'toolchain-probe-incomplete' } else { 'published-unprobed' }
        }
        elseif ($runtimeIdentityCount -gt 0) {
            $boundary = $null
        }
        elseif ($runtimeStartAttempted) {
            $boundary = 'published-starting-uncertain'
            $warnings.Add("classifier:published-starting-uncertain '$SessionId'")
        }
        else {
            $boundary = 'published-unstarted'
        }
    }

    if ($null -ne $boundary) {
        Assert-NervFullStackProtocolValue -Domain 'publicationBoundary' -Value $boundary
    }

    return [pscustomobject][ordered]@{
        Boundary = $boundary
        SessionId = $SessionId
        Warnings = [string[]] $warnings.ToArray()
        WriteCount = 0
        MigrationCount = 0
        DeleteCount = 0
        AspireCallCount = 0
        ProcessCallCount = 0
        DockerCallCount = 0
    }
}
