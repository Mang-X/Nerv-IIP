# Script-Governance:
#   Category: library
#   SideEffects:
#     - Creates caller-verified full-stack publication directories, lock files, authority records, and manifests
#     - Replaces authenticated manifests through the verified record-store CAS primitive
#   Writes:
#     - Caller-supplied StateRoot control records and same-directory publication/CAS temporary files
#   Cleanup:
#     - Releases all streams, opened-object handles, RegistryLease, and SessionVerifiedLease before returning
#     - Preserves publication, probe, and snapshot crash residues for read-only classification
#     - Starts or stops no external process, Aspire runtime, or Docker resource
#   Requires:
#     - PowerShell 7

Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'FullStackControlProtocol.ps1')
. (Join-Path $PSScriptRoot 'FullStackControlFileSystem.ps1')
. (Join-Path $PSScriptRoot 'FullStackVerifiedRecordStore.ps1')

$script:NervFullStackManifestStoreRecordKind = 'fullstack-session-authority'

function Invoke-NervFullStackAuthorityPublicationCrashSeam {
    param(
        [Parameter(Mandatory)]
        [string] $Boundary,

        [Parameter(Mandatory)]
        [object] $Context
    )

    $actionVariable = Get-Variable -Name NervFullStackAuthorityPublicationCrashAction -Scope Script -ErrorAction SilentlyContinue
    if ($null -ne $actionVariable -and $actionVariable.Value -is [scriptblock]) {
        [scriptblock] $action = $actionVariable.Value
        $null = $action.Invoke($Boundary, $Context)
    }
}

function Get-NervFullStackPublicationProperty {
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

function Test-NervFullStackPublicationPositiveInteger {
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

function ConvertTo-NervFullStackPublicationTimestamp {
    [OutputType([DateTime])]
    param(
        [Parameter(Mandatory)]
        [object] $Value,

        [Parameter(Mandatory)]
        [string] $ErrorCode
    )

    $parsed = [DateTimeOffset]::MinValue
    if ($Value -is [DateTimeOffset]) {
        $parsed = [DateTimeOffset] $Value
    }
    elseif ($Value -is [DateTime]) {
        $parsed = [DateTimeOffset] ([DateTime] $Value)
    }
    elseif ($Value -isnot [string] -or -not [DateTimeOffset]::TryParseExact(
            [string] $Value,
            'O',
            [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::RoundtripKind,
            [ref] $parsed
        )) {
        throw $ErrorCode
    }

    # Keep the normalized value in UTC so A3 persists the field with the Z wire
    # designator and its mandatory-field fingerprint remains stable after the
    # durable ConvertFrom-Json readback.
    return $parsed.UtcDateTime
}

function ConvertTo-NervFullStackPublicationIdentity {
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)]
        [object] $Identity,

        [Parameter(Mandatory)]
        [bool] $RequireRole,

        [Parameter(Mandatory)]
        [string] $ErrorCode
    )

    $pidProperty = Get-NervFullStackPublicationProperty -Record $Identity -Name 'pid'
    $startProperty = Get-NervFullStackPublicationProperty -Record $Identity -Name 'processStartTimeUtc'
    $roleProperty = Get-NervFullStackPublicationProperty -Record $Identity -Name 'role'
    if ($null -eq $pidProperty -or
        -not (Test-NervFullStackPublicationPositiveInteger -Value $pidProperty.Value) -or
        $null -eq $startProperty -or
        ($RequireRole -and
            ($null -eq $roleProperty -or
                $roleProperty.Value -isnot [string] -or
                [string]::IsNullOrWhiteSpace([string] $roleProperty.Value)))) {
        throw $ErrorCode
    }

    $normalizedStart = ConvertTo-NervFullStackPublicationTimestamp -Value $startProperty.Value -ErrorCode $ErrorCode
    $normalized = [ordered]@{
        pid = [int64] $pidProperty.Value
        processStartTimeUtc = $normalizedStart
    }
    if ($RequireRole) {
        $normalized.role = [string] $roleProperty.Value
    }

    return [pscustomobject] $normalized
}

function Get-NervFullStackCanonicalPublicationWorktree {
    [OutputType([string])]
    param(
        [Parameter(Mandatory)]
        [string] $WorktreeRoot
    )

    $normalized = Get-NervFullStackNormalizedFullPath -Path $WorktreeRoot
    $item = Get-NervFullStackFileSystemItem -Path $normalized
    if ($null -eq $item -or -not $item.PSIsContainer) {
        throw "publication:worktree-root-unavailable '$normalized'"
    }
    Assert-NervFullStackOrdinaryItem -Item $item
    return Get-NervFullStackCanonicalExistingPath -Path $normalized
}

function New-NervFullStackPublicationNonce {
    [OutputType([string])]
    param()

    $bytes = [byte[]]::new(16)
    [System.Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    return [Convert]::ToHexString($bytes).ToLowerInvariant()
}

function Test-NervFullStackPublicationSnapshotContentEqual {
    [OutputType([bool])]
    param(
        [Parameter(Mandatory)]
        [object] $Left,

        [Parameter(Mandatory)]
        [object] $Right
    )

    return (Test-NervFullStackByteArrayEqual -Left $Left.RawBytes -Right $Right.RawBytes) -and
        [string]::Equals(
            (Get-NervFullStackRecordFieldFingerprint -Record $Left.Record),
            (Get-NervFullStackRecordFieldFingerprint -Record $Right.Record),
            [StringComparison]::Ordinal
        ) -and
        (Test-NervFullStackRecordIdentityEqual -Left $Left.Identity -Right $Right.Identity)
}

function Assert-NervFullStackPublicationPair {
    param(
        [Parameter(Mandatory)]
        [object] $PathSet,

        [Parameter(Mandatory)]
        [string] $CanonicalWorktreeRoot,

        [Parameter(Mandatory)]
        [object] $AuthoritySnapshot,

        [Parameter(Mandatory)]
        [object] $ManifestSnapshot
    )

    $authority = $AuthoritySnapshot.Record
    $manifest = $ManifestSnapshot.Record
    $authoritySchema = Get-NervFullStackPublicationProperty -Record $authority -Name 'schemaVersion'
    $authorityKind = Get-NervFullStackPublicationProperty -Record $authority -Name 'kind'
    $authoritySession = Get-NervFullStackPublicationProperty -Record $authority -Name 'sessionId'
    $authorityNonce = Get-NervFullStackPublicationProperty -Record $authority -Name 'creationNonce'
    $authorityWorktree = Get-NervFullStackPublicationProperty -Record $authority -Name 'worktreeRoot'
    $authorityManifest = Get-NervFullStackPublicationProperty -Record $authority -Name 'manifestPath'
    $authorityCreatedBy = Get-NervFullStackPublicationProperty -Record $authority -Name 'createdBy'
    $authorityCreatedAt = Get-NervFullStackPublicationProperty -Record $authority -Name 'createdAtUtc'
    $manifestSchema = Get-NervFullStackPublicationProperty -Record $manifest -Name 'schemaVersion'
    $manifestProtocol = Get-NervFullStackPublicationProperty -Record $manifest -Name 'controlProtocolVersion'
    $manifestSession = Get-NervFullStackPublicationProperty -Record $manifest -Name 'sessionId'
    $manifestNonce = Get-NervFullStackPublicationProperty -Record $manifest -Name 'creationNonce'
    $manifestWorktree = Get-NervFullStackPublicationProperty -Record $manifest -Name 'worktreeRoot'
    $manifestState = Get-NervFullStackPublicationProperty -Record $manifest -Name 'state'
    $snapshotComplete = Get-NervFullStackPublicationProperty -Record $manifest -Name 'toolchainSnapshotComplete'
    $probeIdentities = Get-NervFullStackPublicationProperty -Record $manifest -Name 'toolchainProbeIdentities'
    $runtimeStartAttempted = Get-NervFullStackPublicationProperty -Record $manifest -Name 'runtimeStartAttempted'
    $runtimeIdentities = Get-NervFullStackPublicationProperty -Record $manifest -Name 'runtimeIdentities'

    if ($null -eq $authoritySchema -or [int64] $authoritySchema.Value -ne 2 -or
        $null -eq $authorityKind -or $authorityKind.Value -isnot [string] -or
        -not [string]::Equals([string] $authorityKind.Value, 'fullstack-session-authority', [StringComparison]::Ordinal) -or
        $null -eq $authoritySession -or $authoritySession.Value -isnot [string] -or
        -not [string]::Equals([string] $authoritySession.Value, $PathSet.SessionId, [StringComparison]::Ordinal) -or
        $null -eq $authorityNonce -or $authorityNonce.Value -isnot [string] -or
        -not [string]::Equals([string] $authorityNonce.Value, $PathSet.CreationNonce, [StringComparison]::Ordinal) -or
        $null -eq $authorityCreatedBy -or $null -eq $authorityCreatedAt) {
        throw 'publication:pair-mismatch authority'
    }
    [void] (ConvertTo-NervFullStackPublicationIdentity -Identity $authorityCreatedBy.Value -RequireRole $false -ErrorCode 'publication:pair-mismatch created-by')
    [void] (ConvertTo-NervFullStackPublicationTimestamp -Value $authorityCreatedAt.Value -ErrorCode 'publication:pair-mismatch created-at')

    if ($null -eq $manifestSchema -or [int64] $manifestSchema.Value -ne 2 -or
        $null -eq $manifestProtocol -or [int64] $manifestProtocol.Value -ne 2 -or
        $null -eq $manifestSession -or $manifestSession.Value -isnot [string] -or
        -not [string]::Equals([string] $manifestSession.Value, $PathSet.SessionId, [StringComparison]::Ordinal) -or
        $null -eq $manifestNonce -or $manifestNonce.Value -isnot [string] -or
        -not [string]::Equals([string] $manifestNonce.Value, $PathSet.CreationNonce, [StringComparison]::Ordinal) -or
        $null -eq $manifestState -or $manifestState.Value -isnot [string] -or
        [string]::IsNullOrWhiteSpace([string] $manifestState.Value) -or
        $null -eq $snapshotComplete -or $snapshotComplete.Value -isnot [bool] -or
        $null -eq $probeIdentities -or $probeIdentities.Value -isnot [System.Array] -or
        $null -eq $runtimeStartAttempted -or $runtimeStartAttempted.Value -isnot [bool] -or
        $null -eq $runtimeIdentities -or $runtimeIdentities.Value -isnot [System.Array]) {
        throw 'publication:pair-mismatch manifest'
    }

    foreach ($identity in @($probeIdentities.Value)) {
        [void] (ConvertTo-NervFullStackPublicationIdentity -Identity $identity -RequireRole $true -ErrorCode 'publication:pair-mismatch probe-identity')
    }
    foreach ($identity in @($runtimeIdentities.Value)) {
        [void] (ConvertTo-NervFullStackPublicationIdentity -Identity $identity -RequireRole $true -ErrorCode 'publication:pair-mismatch runtime-identity')
    }

    if ($null -eq $authorityWorktree -or $authorityWorktree.Value -isnot [string] -or
        $null -eq $manifestWorktree -or $manifestWorktree.Value -isnot [string] -or
        $null -eq $authorityManifest -or $authorityManifest.Value -isnot [string]) {
        throw 'publication:pair-mismatch paths'
    }
    try {
        $authorityWorktreePath = Get-NervFullStackNormalizedFullPath -Path ([string] $authorityWorktree.Value)
        $manifestWorktreePath = Get-NervFullStackNormalizedFullPath -Path ([string] $manifestWorktree.Value)
        $authorityManifestPath = Get-NervFullStackNormalizedFullPath -Path ([string] $authorityManifest.Value)
        $expectedManifestPath = Get-NervFullStackNormalizedFullPath -Path $PathSet.ManifestPath
    }
    catch {
        throw 'publication:pair-mismatch paths'
    }

    $pathComparison = Get-NervFullStackPathComparison
    if (-not [string]::Equals($authorityWorktreePath, $CanonicalWorktreeRoot, $pathComparison) -or
        -not [string]::Equals($manifestWorktreePath, $CanonicalWorktreeRoot, $pathComparison) -or
        -not [string]::Equals($authorityManifestPath, $expectedManifestPath, $pathComparison)) {
        throw 'publication:pair-mismatch paths'
    }
}

function Read-NervFullStackPublicationPair {
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)]
        [object] $PathSet,

        [Parameter(Mandatory)]
        [string] $CanonicalWorktreeRoot
    )

    $authorityTarget = Test-NervFullStackTrustedPathGraph `
        -StateRoot $PathSet.StateRoot `
        -CandidatePath $PathSet.AuthorityPath `
        -ExpectedKind File
    $manifestTarget = Test-NervFullStackTrustedPathGraph `
        -StateRoot $PathSet.StateRoot `
        -CandidatePath $PathSet.ManifestPath `
        -ExpectedKind File
    $authoritySnapshot = Read-NervFullStackVerifiedRecord `
        -VerifiedTarget $authorityTarget `
        -RecordKind 'fullstack-session-authority'
    $manifestSnapshot = Read-NervFullStackVerifiedRecord `
        -VerifiedTarget $manifestTarget `
        -RecordKind $script:NervFullStackManifestStoreRecordKind
    Assert-NervFullStackPublicationPair `
        -PathSet $PathSet `
        -CanonicalWorktreeRoot $CanonicalWorktreeRoot `
        -AuthoritySnapshot $authoritySnapshot `
        -ManifestSnapshot $manifestSnapshot

    return [pscustomobject][ordered]@{
        AuthoritySnapshot = $authoritySnapshot
        ManifestSnapshot = $manifestSnapshot
    }
}

function New-NervFullStackPublishedSessionResult {
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)]
        [object] $PathSet,

        [Parameter(Mandatory)]
        [string] $CanonicalWorktreeRoot,

        [Parameter(Mandatory)]
        [object] $AuthoritySnapshot,

        [Parameter(Mandatory)]
        [object] $ManifestSnapshot
    )

    $trustedAuthority = Test-NervFullStackTrustedPathGraph `
        -StateRoot $PathSet.StateRoot `
        -CandidatePath $PathSet.AuthorityPath `
        -ExpectedKind File
    $authorityProof = Open-NervFullStackVerifiedPathHandle -TrustedPath $trustedAuthority -Access Read
    try {
        $capability = New-NervFullStackVerifiedSessionCapability -PathSet $PathSet -AuthorityProof $authorityProof
    }
    finally {
        if ($null -ne $authorityProof.Handle) {
            $authorityProof.Handle.Dispose()
        }
    }

    if (-not (Test-NervFullStackRecordIdentityEqual -Left $capability.AuthorityIdentity -Right $AuthoritySnapshot.Identity)) {
        throw 'publication:authority-identity-mismatch'
    }

    return [pscustomobject][ordered]@{
        Verified = $true
        PublicationComplete = $true
        StateRoot = [string] $PathSet.StateRoot
        SessionId = [string] $PathSet.SessionId
        CreationNonce = [string] $PathSet.CreationNonce
        WorktreeRoot = $CanonicalWorktreeRoot
        ManifestPath = [string] $PathSet.ManifestPath
        PathSet = $PathSet
        AuthorityIdentity = $capability.AuthorityIdentity
        AuthoritySnapshot = $AuthoritySnapshot
        ManifestSnapshot = $ManifestSnapshot
    }
}

function Assert-NervFullStackPublishedSessionInput {
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)]
        [object] $VerifiedSession,

        [bool] $RequireCurrentManifestSnapshot
    )

    $verifiedProperty = Get-NervFullStackPublicationProperty -Record $VerifiedSession -Name 'Verified'
    $completeProperty = Get-NervFullStackPublicationProperty -Record $VerifiedSession -Name 'PublicationComplete'
    $rootProperty = Get-NervFullStackPublicationProperty -Record $VerifiedSession -Name 'StateRoot'
    $sessionProperty = Get-NervFullStackPublicationProperty -Record $VerifiedSession -Name 'SessionId'
    $nonceProperty = Get-NervFullStackPublicationProperty -Record $VerifiedSession -Name 'CreationNonce'
    $worktreeProperty = Get-NervFullStackPublicationProperty -Record $VerifiedSession -Name 'WorktreeRoot'
    $identityProperty = Get-NervFullStackPublicationProperty -Record $VerifiedSession -Name 'AuthorityIdentity'
    $manifestSnapshotProperty = Get-NervFullStackPublicationProperty -Record $VerifiedSession -Name 'ManifestSnapshot'
    if ($null -eq $verifiedProperty -or $verifiedProperty.Value -isnot [bool] -or -not [bool] $verifiedProperty.Value -or
        $null -eq $completeProperty -or $completeProperty.Value -isnot [bool] -or -not [bool] $completeProperty.Value -or
        $null -eq $rootProperty -or $rootProperty.Value -isnot [string] -or
        $null -eq $sessionProperty -or $sessionProperty.Value -isnot [string] -or
        $null -eq $nonceProperty -or $nonceProperty.Value -isnot [string] -or
        $null -eq $worktreeProperty -or $worktreeProperty.Value -isnot [string] -or
        $null -eq $identityProperty -or
        $null -eq $manifestSnapshotProperty -or -not $manifestSnapshotProperty.Value.Verified) {
        throw 'publication:verified-complete-session-required'
    }

    $canonicalWorktree = Get-NervFullStackCanonicalPublicationWorktree -WorktreeRoot ([string] $worktreeProperty.Value)
    $pathSet = Get-NervFullStackControlPathSet `
        -StateRoot ([string] $rootProperty.Value) `
        -SessionId ([string] $sessionProperty.Value) `
        -CreationNonce ([string] $nonceProperty.Value)
    $pair = Read-NervFullStackPublicationPair -PathSet $pathSet -CanonicalWorktreeRoot $canonicalWorktree
    if (-not (Test-NervFullStackRecordIdentityEqual -Left $identityProperty.Value -Right $pair.AuthoritySnapshot.Identity)) {
        throw 'publication:authority-identity-mismatch'
    }
    if ($RequireCurrentManifestSnapshot -and
        -not (Test-NervFullStackRecordSnapshotEqual -Left $manifestSnapshotProperty.Value -Right $pair.ManifestSnapshot)) {
        throw 'record:cas-conflict'
    }

    return [pscustomobject][ordered]@{
        PathSet = $pathSet
        CanonicalWorktreeRoot = $canonicalWorktree
        AuthoritySnapshot = $pair.AuthoritySnapshot
        ManifestSnapshot = $pair.ManifestSnapshot
    }
}

function Copy-NervFullStackPublicationRecord {
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)]
        [object] $Record
    )

    $copy = [ordered]@{}
    foreach ($property in $Record.PSObject.Properties) {
        $copy[[string] $property.Name] = $property.Value
    }

    return [pscustomobject] $copy
}

function Publish-NervFullStackInitialV2Session {
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)]
        [string] $StateRoot,

        [Parameter(Mandatory)]
        [string] $SessionId,

        [Parameter(Mandatory)]
        [string] $WorktreeRoot,

        [Parameter(Mandatory)]
        [object] $CreatedByIdentity
    )

    $createdBy = ConvertTo-NervFullStackPublicationIdentity `
        -Identity $CreatedByIdentity `
        -RequireRole $false `
        -ErrorCode 'publication:invalid-created-by-identity'
    $canonicalWorktree = Get-NervFullStackCanonicalPublicationWorktree -WorktreeRoot $WorktreeRoot
    $normalizedRoot = Get-NervFullStackNormalizedFullPath -Path $StateRoot

    return Invoke-WithNervFullStackRegistryLease -StateRoot $normalizedRoot -ScriptBlock {
        $creationNonce = New-NervFullStackPublicationNonce
        $pathSet = Get-NervFullStackControlPathSet `
            -StateRoot $normalizedRoot `
            -SessionId $SessionId `
            -CreationNonce $creationNonce
        $context = [pscustomobject][ordered]@{
            StateRoot = $pathSet.StateRoot
            SessionId = $pathSet.SessionId
            CreationNonce = $pathSet.CreationNonce
            TempDirectory = $pathSet.PublicationTempDirectory
            FinalDirectory = $pathSet.SessionDirectory
            AuthorityPath = $pathSet.AuthorityPath
            ManifestPath = $pathSet.ManifestPath
        }

        if ($null -ne (Get-NervFullStackFileSystemItem -Path $pathSet.SessionDirectory) -or
            $null -ne (Get-NervFullStackFileSystemItem -Path $pathSet.ManifestPath)) {
            throw "publication:session-target-exists '$SessionId'"
        }
        [void] (Test-NervFullStackTrustedPathGraph -StateRoot $pathSet.StateRoot -CandidatePath $pathSet.SessionDirectory -ExpectedKind Directory -AllowMissingLeaf)
        [void] (Test-NervFullStackTrustedPathGraph -StateRoot $pathSet.StateRoot -CandidatePath $pathSet.ManifestPath -ExpectedKind File -AllowMissingLeaf)
        if ($null -ne (Get-NervFullStackFileSystemItem -Path $pathSet.PublicationTempDirectory)) {
            throw "publication:temp-target-exists '$($pathSet.PublicationTempDirectory)'"
        }

        Invoke-NervFullStackAuthorityPublicationCrashSeam -Boundary 'before-temp-directory-create' -Context $context
        [void] [System.IO.Directory]::CreateDirectory($pathSet.PublicationTempDirectory)
        [void] (Test-NervFullStackTrustedPathGraph -StateRoot $pathSet.StateRoot -CandidatePath $pathSet.PublicationTempDirectory -ExpectedKind Directory)
        Invoke-NervFullStackAuthorityPublicationCrashSeam -Boundary 'after-temp-directory-create-before-lock-create' -Context $context

        $tempLockPath = Join-Path $pathSet.PublicationTempDirectory '.session.lock'
        $lockStream = $null
        try {
            $lockStream = [System.IO.File]::Open(
                $tempLockPath,
                [System.IO.FileMode]::CreateNew,
                [System.IO.FileAccess]::ReadWrite,
                [System.IO.FileShare]::ReadWrite
            )
            $lockStream.Flush($true)
        }
        finally {
            if ($null -ne $lockStream) {
                $lockStream.Dispose()
            }
        }
        if (-not $IsWindows) {
            [System.IO.File]::SetUnixFileMode(
                $tempLockPath,
                [System.IO.UnixFileMode]::UserRead -bor [System.IO.UnixFileMode]::UserWrite
            )
        }
        [void] (Test-NervFullStackTrustedPathGraph -StateRoot $pathSet.StateRoot -CandidatePath $tempLockPath -ExpectedKind File)
        Invoke-NervFullStackAuthorityPublicationCrashSeam -Boundary 'after-lock-create-before-authority-create' -Context $context

        $tempAuthorityPath = Join-Path $pathSet.PublicationTempDirectory 'authority.json'
        $authorityRecord = [pscustomobject][ordered]@{
            schemaVersion = 2
            kind = 'fullstack-session-authority'
            sessionId = $pathSet.SessionId
            creationNonce = $pathSet.CreationNonce
            worktreeRoot = $canonicalWorktree
            manifestPath = $pathSet.ManifestPath
            createdBy = $createdBy
            createdAtUtc = [DateTimeOffset]::UtcNow.UtcDateTime
        }
        $tempAuthorityTarget = Test-NervFullStackTrustedPathGraph `
            -StateRoot $pathSet.StateRoot `
            -CandidatePath $tempAuthorityPath `
            -ExpectedKind File `
            -AllowMissingLeaf
        $tempAuthoritySnapshot = New-NervFullStackVerifiedRecord `
            -VerifiedTarget $tempAuthorityTarget `
            -RecordKind 'fullstack-session-authority' `
            -Record $authorityRecord
        Invoke-NervFullStackAuthorityPublicationCrashSeam -Boundary 'after-temp-authority-readback-before-rename' -Context $context

        [void] (Test-NervFullStackTrustedPathGraph -StateRoot $pathSet.StateRoot -CandidatePath $pathSet.ControlsDirectory -ExpectedKind Directory)
        $trustedTempDirectory = Test-NervFullStackTrustedPathGraph -StateRoot $pathSet.StateRoot -CandidatePath $pathSet.PublicationTempDirectory -ExpectedKind Directory
        [void] (Test-NervFullStackTrustedPathGraph -StateRoot $pathSet.StateRoot -CandidatePath $tempLockPath -ExpectedKind File)
        $tempAuthorityReadback = Read-NervFullStackVerifiedRecord `
            -VerifiedTarget (Test-NervFullStackTrustedPathGraph -StateRoot $pathSet.StateRoot -CandidatePath $tempAuthorityPath -ExpectedKind File) `
            -RecordKind 'fullstack-session-authority'
        if (-not (Test-NervFullStackRecordSnapshotEqual -Left $tempAuthoritySnapshot -Right $tempAuthorityReadback)) {
            throw 'publication:authority-readback-mismatch'
        }
        if ($null -ne (Get-NervFullStackFileSystemItem -Path $pathSet.SessionDirectory) -or
            $null -ne (Get-NervFullStackFileSystemItem -Path $pathSet.ManifestPath)) {
            throw "publication:session-target-exists '$SessionId'"
        }
        $tempDirectoryProof = Open-NervFullStackVerifiedPathHandle -TrustedPath $trustedTempDirectory -Access Read
        try {
            if (-not [string]::Equals([string] $tempDirectoryProof.Status, 'Verified', [StringComparison]::Ordinal)) {
                throw 'publication:directory-identity-required'
            }
            $tempDirectoryIdentity = [pscustomobject][ordered]@{
                Provider = [string] $tempDirectoryProof.Provider
                Key = [string] $tempDirectoryProof.Identity.Key
                Device = $tempDirectoryProof.Identity.Device
                Inode = $tempDirectoryProof.Identity.Inode
                Kind = [string] $tempDirectoryProof.Identity.Kind
            }
        }
        finally {
            if ($null -ne $tempDirectoryProof.Handle) {
                $tempDirectoryProof.Handle.Dispose()
            }
        }
        Invoke-NervFullStackAuthorityPublicationCrashSeam -Boundary 'after-temp-revalidation-before-rename' -Context $context

        try {
            [System.IO.Directory]::Move($pathSet.PublicationTempDirectory, $pathSet.SessionDirectory)
        }
        catch [System.IO.IOException] {
            if ($null -ne (Get-NervFullStackFileSystemItem -Path $pathSet.SessionDirectory)) {
                throw "publication:session-target-exists '$SessionId'"
            }
            throw
        }
        Invoke-NervFullStackAuthorityPublicationCrashSeam -Boundary 'after-rename-before-final-authority-readback' -Context $context

        $trustedFinalDirectory = Test-NervFullStackTrustedPathGraph -StateRoot $pathSet.StateRoot -CandidatePath $pathSet.SessionDirectory -ExpectedKind Directory
        $finalDirectoryProof = Open-NervFullStackVerifiedPathHandle -TrustedPath $trustedFinalDirectory -Access Read
        try {
            $finalDirectoryIdentity = [pscustomobject][ordered]@{
                Provider = [string] $finalDirectoryProof.Provider
                Key = [string] $finalDirectoryProof.Identity.Key
                Device = $finalDirectoryProof.Identity.Device
                Inode = $finalDirectoryProof.Identity.Inode
                Kind = [string] $finalDirectoryProof.Identity.Kind
            }
            if (-not [string]::Equals([string] $finalDirectoryProof.Status, 'Verified', [StringComparison]::Ordinal) -or
                -not (Test-NervFullStackRecordIdentityEqual -Left $tempDirectoryIdentity -Right $finalDirectoryIdentity)) {
                throw 'publication:directory-identity-mismatch'
            }
        }
        finally {
            if ($null -ne $finalDirectoryProof.Handle) {
                $finalDirectoryProof.Handle.Dispose()
            }
        }
        [void] (Test-NervFullStackTrustedPathGraph -StateRoot $pathSet.StateRoot -CandidatePath $pathSet.SessionLeasePath -ExpectedKind File)
        $finalAuthoritySnapshot = Read-NervFullStackVerifiedRecord `
            -VerifiedTarget (Test-NervFullStackTrustedPathGraph -StateRoot $pathSet.StateRoot -CandidatePath $pathSet.AuthorityPath -ExpectedKind File) `
            -RecordKind 'fullstack-session-authority'
        if (-not (Test-NervFullStackPublicationSnapshotContentEqual -Left $tempAuthoritySnapshot -Right $finalAuthoritySnapshot)) {
            throw 'publication:authority-readback-mismatch'
        }
        Invoke-NervFullStackAuthorityPublicationCrashSeam -Boundary 'after-final-authority-readback-before-manifest-create' -Context $context

        $manifestRecord = [pscustomobject][ordered]@{
            schemaVersion = 2
            kind = $script:NervFullStackManifestStoreRecordKind
            controlProtocolVersion = 2
            sessionId = $pathSet.SessionId
            creationNonce = $pathSet.CreationNonce
            worktreeRoot = $canonicalWorktree
            state = 'Creating'
            toolchainSnapshotComplete = $false
            toolchainProbeIdentities = [object[]] @()
            runtimeStartAttempted = $false
            runtimeIdentities = [object[]] @()
        }
        $manifestTarget = Test-NervFullStackTrustedPathGraph `
            -StateRoot $pathSet.StateRoot `
            -CandidatePath $pathSet.ManifestPath `
            -ExpectedKind File `
            -AllowMissingLeaf
        $manifestSnapshot = New-NervFullStackVerifiedRecord `
            -VerifiedTarget $manifestTarget `
            -RecordKind $script:NervFullStackManifestStoreRecordKind `
            -Record $manifestRecord
        Invoke-NervFullStackAuthorityPublicationCrashSeam -Boundary 'after-manifest-readback-before-pair-revalidation' -Context $context

        $pair = Read-NervFullStackPublicationPair -PathSet $pathSet -CanonicalWorktreeRoot $canonicalWorktree
        if (-not (Test-NervFullStackPublicationSnapshotContentEqual -Left $finalAuthoritySnapshot -Right $pair.AuthoritySnapshot) -or
            -not (Test-NervFullStackRecordSnapshotEqual -Left $manifestSnapshot -Right $pair.ManifestSnapshot)) {
            throw 'publication:pair-mismatch readback'
        }
        $result = New-NervFullStackPublishedSessionResult `
            -PathSet $pathSet `
            -CanonicalWorktreeRoot $canonicalWorktree `
            -AuthoritySnapshot $pair.AuthoritySnapshot `
            -ManifestSnapshot $pair.ManifestSnapshot
        Invoke-NervFullStackAuthorityPublicationCrashSeam -Boundary 'after-pair-revalidation-before-return' -Context $context
        return $result
    }
}

function Register-NervFullStackToolchainProbeIdentity {
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)]
        [object] $VerifiedSession,

        [Parameter(Mandatory)]
        [object] $ProbeIdentity
    )

    $probe = ConvertTo-NervFullStackPublicationIdentity `
        -Identity $ProbeIdentity `
        -RequireRole $true `
        -ErrorCode 'publication:invalid-probe-identity'
    $validated = Assert-NervFullStackPublishedSessionInput -VerifiedSession $VerifiedSession -RequireCurrentManifestSnapshot $true
    $expectedSnapshot = $validated.ManifestSnapshot
    if ([bool] $expectedSnapshot.Record.toolchainSnapshotComplete -or [bool] $expectedSnapshot.Record.runtimeStartAttempted) {
        throw 'publication:probe-registration-closed'
    }
    foreach ($existingIdentity in @($expectedSnapshot.Record.toolchainProbeIdentities)) {
        if ([int64] $existingIdentity.pid -eq [int64] $probe.pid -and
            [string]::Equals([string] $existingIdentity.processStartTimeUtc, [string] $probe.processStartTimeUtc, [StringComparison]::Ordinal) -and
            [string]::Equals([string] $existingIdentity.role, [string] $probe.role, [StringComparison]::Ordinal)) {
            throw 'publication:probe-identity-already-registered'
        }
    }

    $nextManifest = Copy-NervFullStackPublicationRecord -Record $expectedSnapshot.Record
    $nextIdentities = [System.Collections.Generic.List[object]]::new()
    foreach ($existingIdentity in @($expectedSnapshot.Record.toolchainProbeIdentities)) {
        $nextIdentities.Add($existingIdentity)
    }
    $nextIdentities.Add($probe)
    $nextManifest.toolchainProbeIdentities = [object[]] $nextIdentities.ToArray()
    $context = [pscustomobject][ordered]@{
        StateRoot = $validated.PathSet.StateRoot
        SessionId = $validated.PathSet.SessionId
        CreationNonce = $validated.PathSet.CreationNonce
        AuthorityPath = $validated.PathSet.AuthorityPath
        ManifestPath = $validated.PathSet.ManifestPath
    }
    Invoke-NervFullStackAuthorityPublicationCrashSeam -Boundary 'before-probe-manifest-cas' -Context $context
    $manifestSnapshot = Update-NervFullStackVerifiedRecordCas `
        -VerifiedSession $VerifiedSession `
        -ExpectedSnapshot $expectedSnapshot `
        -NextRecord $nextManifest
    Invoke-NervFullStackAuthorityPublicationCrashSeam -Boundary 'after-probe-manifest-cas-readback' -Context $context

    $pair = Read-NervFullStackPublicationPair -PathSet $validated.PathSet -CanonicalWorktreeRoot $validated.CanonicalWorktreeRoot
    if (-not (Test-NervFullStackRecordSnapshotEqual -Left $manifestSnapshot -Right $pair.ManifestSnapshot)) {
        throw 'publication:probe-readback-mismatch'
    }
    return New-NervFullStackPublishedSessionResult `
        -PathSet $validated.PathSet `
        -CanonicalWorktreeRoot $validated.CanonicalWorktreeRoot `
        -AuthoritySnapshot $pair.AuthoritySnapshot `
        -ManifestSnapshot $pair.ManifestSnapshot
}

function Complete-NervFullStackToolchainSnapshot {
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)]
        [object] $VerifiedSession,

        [Parameter(Mandatory)]
        [object] $ExpectedSnapshot,

        [Parameter(Mandatory)]
        [object] $ToolchainSnapshot
    )

    if ($null -eq $ToolchainSnapshot -or
        $ToolchainSnapshot -is [System.Array] -or
        @($ToolchainSnapshot.PSObject.Properties).Count -eq 0) {
        throw 'publication:invalid-toolchain-snapshot'
    }
    try {
        $toolchainFingerprint = Get-NervFullStackRecordFieldFingerprint -Record $ToolchainSnapshot
    }
    catch {
        throw 'publication:invalid-toolchain-snapshot'
    }
    if ([string]::IsNullOrWhiteSpace($toolchainFingerprint)) {
        throw 'publication:invalid-toolchain-snapshot'
    }

    $validated = Assert-NervFullStackPublishedSessionInput -VerifiedSession $VerifiedSession -RequireCurrentManifestSnapshot $false
    $expectedVerifiedProperty = Get-NervFullStackPublicationProperty -Record $ExpectedSnapshot -Name 'Verified'
    if ($null -eq $expectedVerifiedProperty -or $expectedVerifiedProperty.Value -isnot [bool] -or -not [bool] $expectedVerifiedProperty.Value -or
        -not (Test-NervFullStackRecordSnapshotEqual -Left $VerifiedSession.ManifestSnapshot -Right $ExpectedSnapshot)) {
        throw 'record:cas-conflict'
    }
    if (-not (Test-NervFullStackRecordSnapshotEqual -Left $validated.ManifestSnapshot -Right $ExpectedSnapshot)) {
        throw 'record:cas-conflict'
    }
    if ([bool] $ExpectedSnapshot.Record.toolchainSnapshotComplete -or
        @($ExpectedSnapshot.Record.toolchainProbeIdentities).Count -eq 0 -or
        [bool] $ExpectedSnapshot.Record.runtimeStartAttempted -or
        @($ExpectedSnapshot.Record.runtimeIdentities).Count -ne 0) {
        throw 'publication:snapshot-state-invalid'
    }

    $nextManifest = Copy-NervFullStackPublicationRecord -Record $ExpectedSnapshot.Record
    $nextManifest | Add-Member -NotePropertyName toolchainSnapshot -NotePropertyValue $ToolchainSnapshot
    $nextManifest.toolchainSnapshotComplete = $true
    $manifestSnapshot = Update-NervFullStackVerifiedRecordCas `
        -VerifiedSession $VerifiedSession `
        -ExpectedSnapshot $ExpectedSnapshot `
        -NextRecord $nextManifest

    $pair = Read-NervFullStackPublicationPair -PathSet $validated.PathSet -CanonicalWorktreeRoot $validated.CanonicalWorktreeRoot
    if (-not (Test-NervFullStackRecordSnapshotEqual -Left $manifestSnapshot -Right $pair.ManifestSnapshot) -or
        -not [bool] $pair.ManifestSnapshot.Record.toolchainSnapshotComplete -or
        @($pair.ManifestSnapshot.Record.toolchainProbeIdentities).Count -ne @($ExpectedSnapshot.Record.toolchainProbeIdentities).Count) {
        throw 'publication:snapshot-readback-mismatch'
    }
    $readbackSnapshotProperty = Get-NervFullStackPublicationProperty -Record $pair.ManifestSnapshot.Record -Name 'toolchainSnapshot'
    if ($null -eq $readbackSnapshotProperty -or
        -not [string]::Equals(
            $toolchainFingerprint,
            (Get-NervFullStackRecordFieldFingerprint -Record $readbackSnapshotProperty.Value),
            [StringComparison]::Ordinal
        )) {
        throw 'publication:snapshot-readback-mismatch'
    }

    return New-NervFullStackPublishedSessionResult `
        -PathSet $validated.PathSet `
        -CanonicalWorktreeRoot $validated.CanonicalWorktreeRoot `
        -AuthoritySnapshot $pair.AuthoritySnapshot `
        -ManifestSnapshot $pair.ManifestSnapshot
}

function Test-NervFullStackRuntimeStartAllowed {
    [OutputType([bool])]
    param(
        [Parameter(Mandatory)]
        [object] $VerifiedSession
    )

    try {
        $validated = Assert-NervFullStackPublishedSessionInput -VerifiedSession $VerifiedSession -RequireCurrentManifestSnapshot $false
        $manifest = $validated.ManifestSnapshot.Record
        $snapshotProperty = Get-NervFullStackPublicationProperty -Record $manifest -Name 'toolchainSnapshot'
        return [bool] $manifest.toolchainSnapshotComplete -and
            $null -ne $snapshotProperty -and
            $null -ne $snapshotProperty.Value -and
            $snapshotProperty.Value -isnot [System.Array] -and
            @($snapshotProperty.Value.PSObject.Properties).Count -gt 0 -and
            @($manifest.toolchainProbeIdentities).Count -gt 0 -and
            -not [bool] $manifest.runtimeStartAttempted -and
            @($manifest.runtimeIdentities).Count -eq 0
    }
    catch {
        return $false
    }
}
