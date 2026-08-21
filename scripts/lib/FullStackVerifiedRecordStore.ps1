# Script-Governance:
#   Category: library
#   SideEffects:
#     - Creates caller-verified JSON records with CreateNew semantics
#     - Replaces authenticated session records through same-directory atomic rename
#   Writes:
#     - Caller-supplied verified full-stack record paths and same-directory temporary files
#   Cleanup:
#     - Releases every stream, opened-object handle, and SessionVerifiedLease before returning
#     - Preserves crash residues for later classification instead of deleting uncertain records
#   Requires:
#     - PowerShell 7

Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'FullStackControlProtocol.ps1')
. (Join-Path $PSScriptRoot 'FullStackControlFileSystem.ps1')

function Assert-NervFullStackVerifiedRecordKind {
    param(
        [Parameter(Mandatory)]
        [string] $RecordKind
    )

    if (-not (Test-NervFullStackProtocolValue -Domain 'recordKind' -Value $RecordKind)) {
        throw "record:invalid-kind '$RecordKind'"
    }
}

function Get-NervFullStackRecordProperty {
    param(
        [Parameter(Mandatory)]
        [object] $Record,

        [Parameter(Mandatory)]
        [string] $Name
    )

    if ($null -eq $Record -or $Record -is [System.Array]) {
        return $null
    }

    return $Record.PSObject.Properties[$Name]
}

function Assert-NervFullStackRecordFields {
    param(
        [Parameter(Mandatory)]
        [object] $Record,

        [Parameter(Mandatory)]
        [string] $RecordKind
    )

    Assert-NervFullStackVerifiedRecordKind -RecordKind $RecordKind
    $kindProperty = Get-NervFullStackRecordProperty -Record $Record -Name 'kind'
    if ($null -eq $kindProperty -or
        $kindProperty.Value -isnot [string] -or
        -not [string]::Equals([string] $kindProperty.Value, $RecordKind, [StringComparison]::Ordinal)) {
        throw "record:field-mismatch 'kind' expected '$RecordKind'"
    }
}

function ConvertTo-NervFullStackRecordBytes {
    [OutputType([byte[]])]
    param(
        [Parameter(Mandatory)]
        [object] $Record,

        [Parameter(Mandatory)]
        [string] $RecordKind
    )

    Assert-NervFullStackRecordFields -Record $Record -RecordKind $RecordKind
    try {
        $json = $Record | ConvertTo-Json -Depth 100 -Compress -ErrorAction Stop
    }
    catch {
        throw "record:serialization-failed '$($_.Exception.Message)'"
    }

    return [System.Text.UTF8Encoding]::new($false, $true).GetBytes($json)
}

function ConvertFrom-NervFullStackRecordBytes {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyCollection()]
        [byte[]] $RawBytes,

        [Parameter(Mandatory)]
        [string] $RecordKind
    )

    try {
        $json = [System.Text.UTF8Encoding]::new($false, $true).GetString($RawBytes)
        $record = ConvertFrom-Json -InputObject $json -NoEnumerate -ErrorAction Stop
    }
    catch {
        throw "record:invalid-json '$($_.Exception.Message)'"
    }

    Assert-NervFullStackRecordFields -Record $record -RecordKind $RecordKind
    return $record
}

function Get-NervFullStackRecordFieldFingerprint {
    [OutputType([string])]
    param(
        [Parameter(Mandatory)]
        [object] $Record
    )

    try {
        return ($Record | ConvertTo-Json -Depth 100 -Compress -ErrorAction Stop)
    }
    catch {
        return $null
    }
}

function Test-NervFullStackByteArrayEqual {
    [OutputType([bool])]
    param(
        [AllowNull()]
        [byte[]] $Left,

        [AllowNull()]
        [byte[]] $Right
    )

    if ($null -eq $Left -or $null -eq $Right) {
        return $null -eq $Left -and $null -eq $Right
    }

    return [System.Linq.Enumerable]::SequenceEqual([byte[]] $Left, [byte[]] $Right)
}

function Test-NervFullStackRecordIdentityEqual {
    [OutputType([bool])]
    param(
        [AllowNull()]
        [object] $Left,

        [AllowNull()]
        [object] $Right
    )

    if ($null -eq $Left -or $null -eq $Right) {
        return $false
    }

    return [string]::Equals([string] $Left.Provider, [string] $Right.Provider, [StringComparison]::Ordinal) -and
        [string]::Equals([string] $Left.Key, [string] $Right.Key, [StringComparison]::Ordinal) -and
        [string]::Equals([string] $Left.Kind, [string] $Right.Kind, [StringComparison]::Ordinal) -and
        $Left.Device -eq $Right.Device -and
        $Left.Inode -eq $Right.Inode
}

function Test-NervFullStackRecordSnapshotEqual {
    [OutputType([bool])]
    param(
        [Parameter(Mandatory)]
        [object] $Left,

        [Parameter(Mandatory)]
        [object] $Right
    )

    if (-not $Left.Verified -or -not $Right.Verified) {
        return $false
    }

    try {
        Assert-NervFullStackRecordFields -Record $Left.Record -RecordKind $Left.RecordKind
        Assert-NervFullStackRecordFields -Record $Right.Record -RecordKind $Right.RecordKind
    }
    catch {
        return $false
    }

    $pathComparison = Get-NervFullStackPathComparison
    return [string]::Equals([string] $Left.StateRoot, [string] $Right.StateRoot, $pathComparison) -and
        [string]::Equals([string] $Left.CanonicalPath, [string] $Right.CanonicalPath, $pathComparison) -and
        [string]::Equals([string] $Left.RecordKind, [string] $Right.RecordKind, [StringComparison]::Ordinal) -and
        (Test-NervFullStackByteArrayEqual -Left $Left.RawBytes -Right $Right.RawBytes) -and
        [string]::Equals(
            (Get-NervFullStackRecordFieldFingerprint -Record $Left.Record),
            (Get-NervFullStackRecordFieldFingerprint -Record $Right.Record),
            [StringComparison]::Ordinal
        ) -and
        (Test-NervFullStackRecordIdentityEqual -Left $Left.Identity -Right $Right.Identity)
}

function Read-NervFullStackOpenedRecordBytes {
    [OutputType([byte[]])]
    param(
        [Parameter(Mandatory)]
        [object] $Handle
    )

    $handleReferenceAdded = $false
    $borrowedHandle = $null
    $stream = $null
    $memory = $null
    try {
        $Handle.DangerousAddRef([ref] $handleReferenceAdded)
        $borrowedHandle = [Microsoft.Win32.SafeHandles.SafeFileHandle]::new($Handle.DangerousGetHandle(), $false)
        $stream = [System.IO.FileStream]::new($borrowedHandle, [System.IO.FileAccess]::Read, 4096, $false)
        $memory = [System.IO.MemoryStream]::new()
        $stream.CopyTo($memory)
        return $memory.ToArray()
    }
    finally {
        if ($null -ne $memory) { $memory.Dispose() }
        if ($null -ne $stream) { $stream.Dispose() }
        if ($null -ne $borrowedHandle) { $borrowedHandle.Dispose() }
        if ($handleReferenceAdded) { $Handle.DangerousRelease() }
    }
}

function Invoke-NervFullStackVerifiedRecordCrashSeam {
    param(
        [Parameter(Mandatory)]
        [string] $Boundary,

        [Parameter(Mandatory)]
        [object] $Context
    )

    $actionVariable = Get-Variable -Name NervFullStackVerifiedRecordStoreCrashAction -Scope Script -ErrorAction SilentlyContinue
    if ($null -ne $actionVariable -and $actionVariable.Value -is [scriptblock]) {
        [scriptblock] $action = $actionVariable.Value
        $null = $action.Invoke($Boundary, $Context)
    }
}

function Read-NervFullStackVerifiedRecord {
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)]
        [object] $VerifiedTarget,

        [Parameter(Mandatory)]
        [string] $RecordKind
    )

    Assert-NervFullStackVerifiedRecordKind -RecordKind $RecordKind
    if (-not $VerifiedTarget.Verified -or
        -not [string]::Equals([string] $VerifiedTarget.ExpectedKind, 'File', [StringComparison]::Ordinal)) {
        throw 'record:verified-file-target-required'
    }

    $trustedTarget = Test-NervFullStackTrustedPathGraph `
        -StateRoot $VerifiedTarget.StateRoot `
        -CandidatePath $VerifiedTarget.CandidatePath `
        -ExpectedKind File
    $proof = Open-NervFullStackVerifiedPathHandle -TrustedPath $trustedTarget -Access Read
    try {
        if (-not [string]::Equals($proof.Status, 'Verified', [StringComparison]::Ordinal) -or
            $null -eq $proof.Handle -or $proof.Handle.IsClosed -or $proof.Handle.IsInvalid) {
            throw 'record:opened-object-identity-required'
        }

        $rawBytes = Read-NervFullStackOpenedRecordBytes -Handle $proof.Handle
        $record = ConvertFrom-NervFullStackRecordBytes -RawBytes $rawBytes -RecordKind $RecordKind
        return [pscustomobject][ordered]@{
            Verified = $true
            StateRoot = [string] $trustedTarget.StateRoot
            CandidatePath = [string] $trustedTarget.CandidatePath
            CanonicalPath = [string] $trustedTarget.CanonicalPath
            RecordKind = $RecordKind
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
        if ($null -ne $proof.Handle) { $proof.Handle.Dispose() }
    }
}

function Write-NervFullStackVerifiedRecordCreateNew {
    param(
        [Parameter(Mandatory)]
        [object] $VerifiedTarget,

        [Parameter(Mandatory)]
        [string] $RecordKind,

        [Parameter(Mandatory)]
        [object] $Record,

        [Parameter(Mandatory)]
        [string] $ReadbackBoundary
    )

    if (-not $VerifiedTarget.Verified -or
        -not [string]::Equals([string] $VerifiedTarget.ExpectedKind, 'File', [StringComparison]::Ordinal)) {
        throw 'record:verified-file-target-required'
    }

    $rawBytes = ConvertTo-NervFullStackRecordBytes -Record $Record -RecordKind $RecordKind
    $trustedTarget = Test-NervFullStackTrustedPathGraph `
        -StateRoot $VerifiedTarget.StateRoot `
        -CandidatePath $VerifiedTarget.CandidatePath `
        -ExpectedKind File `
        -AllowMissingLeaf
    if ($trustedTarget.Exists) {
        throw "record:target-exists '$($trustedTarget.CanonicalPath)'"
    }

    $stream = $null
    try {
        $stream = [System.IO.File]::Open(
            $trustedTarget.CanonicalPath,
            [System.IO.FileMode]::CreateNew,
            [System.IO.FileAccess]::Write,
            [System.IO.FileShare]::None
        )
        $stream.Write($rawBytes, 0, $rawBytes.Length)
        $stream.Flush($true)
    }
    catch [System.IO.IOException] {
        if ([System.IO.File]::Exists($trustedTarget.CanonicalPath)) {
            throw "record:target-exists '$($trustedTarget.CanonicalPath)'"
        }
        throw
    }
    finally {
        if ($null -ne $stream) { $stream.Dispose() }
    }

    Invoke-NervFullStackVerifiedRecordCrashSeam `
        -Boundary $ReadbackBoundary `
        -Context ([pscustomobject][ordered]@{ Path = $trustedTarget.CanonicalPath; RecordKind = $RecordKind })

    try {
        $existingTarget = Test-NervFullStackTrustedPathGraph `
            -StateRoot $trustedTarget.StateRoot `
            -CandidatePath $trustedTarget.CandidatePath `
            -ExpectedKind File
        $snapshot = Read-NervFullStackVerifiedRecord -VerifiedTarget $existingTarget -RecordKind $RecordKind
    }
    catch {
        throw "record:readback-mismatch '$($_.Exception.Message)'"
    }

    $readbackFields = Get-NervFullStackRecordFieldFingerprint -Record $snapshot.Record
    $expectedFields = Get-NervFullStackRecordFieldFingerprint -Record $Record
    if (-not (Test-NervFullStackByteArrayEqual -Left $rawBytes -Right $snapshot.RawBytes) -or
        -not [string]::Equals($expectedFields, $readbackFields, [StringComparison]::Ordinal)) {
        throw 'record:readback-mismatch'
    }

    return $snapshot
}

function New-NervFullStackVerifiedRecord {
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)]
        [object] $VerifiedTarget,

        [Parameter(Mandatory)]
        [string] $RecordKind,

        [Parameter(Mandatory)]
        [object] $Record
    )

    return Write-NervFullStackVerifiedRecordCreateNew `
        -VerifiedTarget $VerifiedTarget `
        -RecordKind $RecordKind `
        -Record $Record `
        -ReadbackBoundary 'after-create-flush-before-readback'
}

function Get-NervFullStackVerifiedSessionRecordPathSet {
    param(
        [Parameter(Mandatory)]
        [object] $VerifiedSession
    )

    $parameters = @{
        StateRoot = [string] $VerifiedSession.StateRoot
        SessionId = [string] $VerifiedSession.SessionId
    }
    if ($null -ne $VerifiedSession.CreationNonce) {
        $parameters.CreationNonce = [string] $VerifiedSession.CreationNonce
    }

    return Get-NervFullStackControlPathSet @parameters
}

function Assert-NervFullStackCasTarget {
    param(
        [Parameter(Mandatory)]
        [object] $VerifiedSession,

        [Parameter(Mandatory)]
        [object] $ExpectedSnapshot
    )

    if (-not $VerifiedSession.Verified -or -not $ExpectedSnapshot.Verified) {
        throw 'record:verified-session-and-snapshot-required'
    }
    $pathSet = Get-NervFullStackVerifiedSessionRecordPathSet -VerifiedSession $VerifiedSession
    $comparison = Get-NervFullStackPathComparison
    if (-not [string]::Equals(
            [string] $ExpectedSnapshot.StateRoot,
            (Get-NervFullStackNormalizedFullPath -Path $pathSet.StateRoot),
            $comparison
        )) {
        throw 'record:target-not-session-owned'
    }
    if ([string]::Equals(
            [string] $ExpectedSnapshot.CandidatePath,
            (Get-NervFullStackNormalizedFullPath -Path $pathSet.AuthorityPath),
            $comparison
        )) {
        throw 'record:authority-immutable'
    }

    $allowedPaths = @($pathSet.ManifestPath, $pathSet.GuardianRequestPath, $pathSet.GuardianAckPath)
    $targetAllowed = $false
    foreach ($allowedPath in $allowedPaths) {
        if ([string]::Equals(
                [string] $ExpectedSnapshot.CandidatePath,
                (Get-NervFullStackNormalizedFullPath -Path $allowedPath),
                $comparison
            )) {
            $targetAllowed = $true
            break
        }
    }
    if (-not $targetAllowed) {
        throw "record:target-not-session-owned '$($ExpectedSnapshot.CanonicalPath)'"
    }

    return $pathSet
}

function Update-NervFullStackVerifiedRecordCas {
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)]
        [object] $VerifiedSession,

        [Parameter(Mandatory)]
        [object] $ExpectedSnapshot,

        [Parameter(Mandatory)]
        [object] $NextRecord
    )

    [void] (Assert-NervFullStackCasTarget -VerifiedSession $VerifiedSession -ExpectedSnapshot $ExpectedSnapshot)
    $recordKind = [string] $ExpectedSnapshot.RecordKind
    [void] (ConvertTo-NervFullStackRecordBytes -Record $NextRecord -RecordKind $recordKind)

    return Invoke-WithNervFullStackSessionVerifiedLease -VerifiedSession $VerifiedSession -ScriptBlock {
        $target = Test-NervFullStackTrustedPathGraph `
            -StateRoot $ExpectedSnapshot.StateRoot `
            -CandidatePath $ExpectedSnapshot.CandidatePath `
            -ExpectedKind File
        $currentSnapshot = Read-NervFullStackVerifiedRecord -VerifiedTarget $target -RecordKind $recordKind
        if (-not (Test-NervFullStackRecordSnapshotEqual -Left $ExpectedSnapshot -Right $currentSnapshot)) {
            throw 'record:cas-conflict'
        }

        $targetDirectory = Split-Path -Parent $ExpectedSnapshot.CandidatePath
        $targetLeaf = Split-Path -Leaf $ExpectedSnapshot.CandidatePath
        $tempPath = Join-Path $targetDirectory ".$targetLeaf.tmp-$([Guid]::NewGuid().ToString('N'))"
        $tempTarget = Test-NervFullStackTrustedPathGraph `
            -StateRoot $ExpectedSnapshot.StateRoot `
            -CandidatePath $tempPath `
            -ExpectedKind File `
            -AllowMissingLeaf
        $tempSnapshot = Write-NervFullStackVerifiedRecordCreateNew `
            -VerifiedTarget $tempTarget `
            -RecordKind $recordKind `
            -Record $NextRecord `
            -ReadbackBoundary 'after-temp-flush-before-readback'

        Invoke-NervFullStackVerifiedRecordCrashSeam `
            -Boundary 'after-temp-readback-before-replace' `
            -Context ([pscustomobject][ordered]@{ Path = $ExpectedSnapshot.CanonicalPath; TempPath = $tempPath; RecordKind = $recordKind })

        $preReplaceTarget = Test-NervFullStackTrustedPathGraph `
            -StateRoot $ExpectedSnapshot.StateRoot `
            -CandidatePath $ExpectedSnapshot.CandidatePath `
            -ExpectedKind File
        $preReplaceSnapshot = Read-NervFullStackVerifiedRecord -VerifiedTarget $preReplaceTarget -RecordKind $recordKind
        if (-not (Test-NervFullStackRecordSnapshotEqual -Left $ExpectedSnapshot -Right $preReplaceSnapshot)) {
            throw 'record:cas-conflict'
        }

        Invoke-NervFullStackVerifiedRecordCrashSeam `
            -Boundary 'after-cas-recheck-before-replace' `
            -Context ([pscustomobject][ordered]@{ Path = $ExpectedSnapshot.CandidatePath; TempPath = $tempPath; RecordKind = $recordKind })

        [System.IO.File]::Move($tempPath, $ExpectedSnapshot.CandidatePath, $true)
        Invoke-NervFullStackVerifiedRecordCrashSeam `
            -Boundary 'after-replace-before-final-readback' `
            -Context ([pscustomobject][ordered]@{ Path = $ExpectedSnapshot.CandidatePath; RecordKind = $recordKind })

        try {
            $finalTarget = Test-NervFullStackTrustedPathGraph `
                -StateRoot $ExpectedSnapshot.StateRoot `
                -CandidatePath $ExpectedSnapshot.CandidatePath `
                -ExpectedKind File
            $finalSnapshot = Read-NervFullStackVerifiedRecord -VerifiedTarget $finalTarget -RecordKind $recordKind
        }
        catch {
            throw "record:readback-mismatch '$($_.Exception.Message)'"
        }

        $expectedBytes = ConvertTo-NervFullStackRecordBytes -Record $NextRecord -RecordKind $recordKind
        if (-not (Test-NervFullStackByteArrayEqual -Left $expectedBytes -Right $finalSnapshot.RawBytes) -or
            -not [string]::Equals(
                (Get-NervFullStackRecordFieldFingerprint -Record $NextRecord),
                (Get-NervFullStackRecordFieldFingerprint -Record $finalSnapshot.Record),
                [StringComparison]::Ordinal
            ) -or
            -not (Test-NervFullStackRecordIdentityEqual -Left $tempSnapshot.Identity -Right $finalSnapshot.Identity)) {
            throw 'record:readback-mismatch'
        }

        return $finalSnapshot
    }
}
