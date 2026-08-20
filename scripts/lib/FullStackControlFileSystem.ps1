# Script-Governance:
#   Category: library
#   SideEffects:
#     - Creates and exclusively locks caller-owned full-stack control lock files
#     - Reads caller-owned full-stack control paths and opened-object identities
#   Writes:
#     - The caller-supplied StateRoot and its fixed empty lock files
#   Cleanup:
#     - Releases every native path handle and lease before returning
#   Requires:
#     - PowerShell 7

Set-StrictMode -Version Latest

$script:NervFullStackControlSessionIdPattern = '^nerv-[a-f0-9]{4}-[a-f0-9]{6}$'
$script:NervFullStackCreationNoncePattern = '^[a-f0-9]{32}$'
if ($null -eq (Get-Variable -Name NervFullStackHeldLeaseCount -Scope Script -ErrorAction SilentlyContinue)) {
    $script:NervFullStackHeldLeaseCount = 0
}

if ($IsMacOS -and -not ('Nerv.IIP.FullStack.DarwinPathHandle' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Nerv.IIP.FullStack
{
    public sealed class DarwinOpenedPathIdentity
    {
        public DarwinOpenedPathIdentity(int device, ulong inode, string kind)
        {
            Device = device;
            Inode = inode;
            Kind = kind;
        }

        public int Device { get; }
        public ulong Inode { get; }
        public string Kind { get; }
    }

    public sealed class DarwinPathHandle : SafeHandleMinusOneIsInvalid
    {
        private const int O_RDONLY = 0x00000000;
        private const int O_RDWR = 0x00000002;
        private const int O_CLOEXEC = 0x01000000;
        private const int O_NOFOLLOW_ANY = 0x20000000;
        private const int LOCK_EX = 0x02;
        private const int LOCK_NB = 0x04;
        private const int LOCK_UN = 0x08;
        private const int S_IFMT = 0xF000;
        private const int S_IFDIR = 0x4000;
        private const int S_IFREG = 0x8000;
        private bool leaseHeld;

        private DarwinPathHandle(int descriptor) : base(true)
        {
            SetHandle(new IntPtr(descriptor));
        }

        [DllImport("libSystem.B.dylib", EntryPoint = "open", SetLastError = true)]
        private static extern int OpenExisting(string path, int flags);

        [DllImport("libSystem.B.dylib", EntryPoint = "close", SetLastError = true)]
        private static extern int Close(int descriptor);

        [DllImport("libSystem.B.dylib", EntryPoint = "fstat", SetLastError = true)]
        private static extern int FStat(int descriptor, IntPtr statBuffer);

        [DllImport("libSystem.B.dylib", EntryPoint = "flock", SetLastError = true)]
        private static extern int FLock(int descriptor, int operation);

        [DllImport("libSystem.B.dylib", EntryPoint = "realpath", SetLastError = true)]
        private static extern IntPtr RealPath(string path, IntPtr resolvedPath);

        [DllImport("libSystem.B.dylib", EntryPoint = "free", SetLastError = false)]
        private static extern void Free(IntPtr pointer);

        public static DarwinPathHandle OpenPath(string path, bool readWrite)
        {
            int flags = (readWrite ? O_RDWR : O_RDONLY) | O_CLOEXEC | O_NOFOLLOW_ANY;
            int descriptor = OpenExisting(path, flags);
            if (descriptor < 0)
            {
                throw new IOException("open failed with errno " + Marshal.GetLastWin32Error() + ".");
            }

            return new DarwinPathHandle(descriptor);
        }

        public static DarwinPathHandle OpenLease(string path)
        {
            int descriptor = OpenExisting(path, O_RDWR | O_CLOEXEC | O_NOFOLLOW_ANY);
            if (descriptor < 0)
            {
                throw new IOException("lease open failed with errno " + Marshal.GetLastWin32Error() + ".");
            }

            return new DarwinPathHandle(descriptor);
        }

        public static string CanonicalizeExistingPath(string path)
        {
            IntPtr resolved = RealPath(path, IntPtr.Zero);
            if (resolved == IntPtr.Zero)
            {
                throw new IOException("realpath failed with errno " + Marshal.GetLastWin32Error() + ".");
            }

            try
            {
                return Marshal.PtrToStringUTF8(resolved) ?? throw new IOException("realpath returned no path.");
            }
            finally
            {
                Free(resolved);
            }
        }

        public DarwinOpenedPathIdentity ReadIdentity()
        {
            if (IsInvalid || IsClosed)
            {
                throw new ObjectDisposedException(nameof(DarwinPathHandle));
            }

            IntPtr statBuffer = Marshal.AllocHGlobal(512);
            try
            {
                if (FStat(handle.ToInt32(), statBuffer) != 0)
                {
                    throw new IOException("fstat failed with errno " + Marshal.GetLastWin32Error() + ".");
                }

                int device = Marshal.ReadInt32(statBuffer, 0);
                ushort mode = unchecked((ushort)Marshal.ReadInt16(statBuffer, 4));
                ulong inode = unchecked((ulong)Marshal.ReadInt64(statBuffer, 8));
                int fileType = mode & S_IFMT;
                string kind = fileType == S_IFDIR ? "Directory" : fileType == S_IFREG ? "File" : "Other";
                return new DarwinOpenedPathIdentity(device, inode, kind);
            }
            finally
            {
                Marshal.FreeHGlobal(statBuffer);
            }
        }

        public void AcquireExclusiveLease()
        {
            if (IsInvalid || IsClosed)
            {
                throw new ObjectDisposedException(nameof(DarwinPathHandle));
            }
            if (FLock(handle.ToInt32(), LOCK_EX | LOCK_NB) != 0)
            {
                throw new IOException("flock failed with errno " + Marshal.GetLastWin32Error() + ".");
            }

            leaseHeld = true;
        }

        protected override bool ReleaseHandle()
        {
            if (leaseHeld)
            {
                FLock(handle.ToInt32(), LOCK_UN);
                leaseHeld = false;
            }

            return Close(handle.ToInt32()) == 0;
        }
    }
}
'@
}

function Get-NervFullStackPathComparison {
    if ($IsWindows) {
        return [StringComparison]::OrdinalIgnoreCase
    }

    return [StringComparison]::Ordinal
}

function Get-NervFullStackNormalizedFullPath {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw 'path:empty'
    }

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $pathRoot = [System.IO.Path]::GetPathRoot($fullPath)
    if ([string]::Equals($fullPath, $pathRoot, (Get-NervFullStackPathComparison))) {
        return $fullPath
    }

    return [System.IO.Path]::TrimEndingDirectorySeparator($fullPath)
}

function Get-NervFullStackFileSystemItem {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    try {
        return Get-Item -LiteralPath $Path -Force -ErrorAction Stop
    }
    catch [System.Management.Automation.ItemNotFoundException] {
        return $null
    }
}

function Assert-NervFullStackOrdinaryItem {
    param(
        [Parameter(Mandatory)]
        [System.IO.FileSystemInfo] $Item
    )

    $isReparse = ($Item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0
    $hasLinkTarget = -not [string]::IsNullOrEmpty($Item.LinkTarget)
    if ($isReparse -or $hasLinkTarget) {
        throw "path:link-or-reparse '$($Item.FullName)'"
    }
}

function Test-NervFullStackPathContained {
    param(
        [Parameter(Mandatory)]
        [string] $Root,

        [Parameter(Mandatory)]
        [string] $Candidate
    )

    $comparison = Get-NervFullStackPathComparison
    if ([string]::Equals($Root, $Candidate, $comparison)) {
        return $true
    }

    $rootWithSeparator = $Root + [System.IO.Path]::DirectorySeparatorChar
    return $Candidate.StartsWith($rootWithSeparator, $comparison)
}

function Get-NervFullStackCanonicalExistingPath {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    if ($IsMacOS) {
        return [Nerv.IIP.FullStack.DarwinPathHandle]::CanonicalizeExistingPath($Path)
    }

    return Get-NervFullStackNormalizedFullPath -Path $Path
}

function Get-NervFullStackControlPathSet {
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)]
        [string] $StateRoot,

        [Parameter(Mandatory)]
        [string] $SessionId,

        [string] $CreationNonce
    )

    if ($SessionId -cnotmatch $script:NervFullStackControlSessionIdPattern) {
        throw "path:invalid-session-id '$SessionId'"
    }
    if ($PSBoundParameters.ContainsKey('CreationNonce') -and $CreationNonce -cnotmatch $script:NervFullStackCreationNoncePattern) {
        throw "path:invalid-creation-nonce '$CreationNonce'"
    }

    $root = Get-NervFullStackNormalizedFullPath -Path $StateRoot
    $sessions = Join-Path $root 'fullstack-sessions'
    $controls = Join-Path $root 'fullstack-controls'
    $sessionDirectory = Join-Path $controls $SessionId
    $guardianDirectory = Join-Path $sessionDirectory 'guardian'
    $publicationTempDirectory = if ($PSBoundParameters.ContainsKey('CreationNonce')) {
        Join-Path $controls ".tmp-$SessionId-$CreationNonce"
    }
    else {
        $null
    }

    return [pscustomobject][ordered]@{
        StateRoot = $root
        SessionId = $SessionId
        CreationNonce = if ($PSBoundParameters.ContainsKey('CreationNonce')) { $CreationNonce } else { $null }
        SessionsDirectory = $sessions
        ControlsDirectory = $controls
        ProtocolModePath = Join-Path $sessions '.protocol-mode.json'
        RegistryLeasePath = Join-Path $sessions '.sessions.lock'
        PublicationTempDirectory = $publicationTempDirectory
        SessionDirectory = $sessionDirectory
        SessionLeasePath = Join-Path $sessionDirectory '.session.lock'
        AuthorityPath = Join-Path $sessionDirectory 'authority.json'
        ManifestPath = Join-Path $sessions "$SessionId.json"
        GuardianDirectory = $guardianDirectory
        GuardianRequestPath = Join-Path $guardianDirectory 'stop.request.json'
        GuardianAckPath = Join-Path $guardianDirectory 'stop.ack.json'
    }
}

function Initialize-NervFullStackTrustedStateRoot {
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)]
        [string] $StateRoot
    )

    $root = Get-NervFullStackNormalizedFullPath -Path $StateRoot
    $fileSystemRoot = [System.IO.Path]::GetPathRoot($root)
    if ([string]::Equals($root, $fileSystemRoot, (Get-NervFullStackPathComparison))) {
        throw "path:filesystem-root '$root'"
    }

    $existingRoot = Get-NervFullStackFileSystemItem -Path $root
    if ($null -ne $existingRoot) {
        Assert-NervFullStackOrdinaryItem -Item $existingRoot
        if (-not $existingRoot.PSIsContainer) {
            throw "path:kind-mismatch '$root' expected Directory"
        }
    }
    else {
        $parent = Get-NervFullStackFileSystemItem -Path (Split-Path -Parent $root)
        if ($null -eq $parent -or -not $parent.PSIsContainer) {
            throw "path:parent-missing '$root'"
        }
        Assert-NervFullStackOrdinaryItem -Item $parent
        [void] [System.IO.Directory]::CreateDirectory($root)
    }

    [void] [System.IO.Directory]::CreateDirectory((Join-Path $root 'fullstack-sessions'))
    [void] [System.IO.Directory]::CreateDirectory((Join-Path $root 'fullstack-controls'))
    [void] (Test-NervFullStackTrustedPathGraph -StateRoot $root -CandidatePath (Join-Path $root 'fullstack-sessions') -ExpectedKind Directory)
    [void] (Test-NervFullStackTrustedPathGraph -StateRoot $root -CandidatePath (Join-Path $root 'fullstack-controls') -ExpectedKind Directory)
    return Test-NervFullStackTrustedPathGraph -StateRoot $root -CandidatePath $root -ExpectedKind Directory
}

function Test-NervFullStackTrustedPathGraph {
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)]
        [string] $StateRoot,

        [Parameter(Mandatory)]
        [string] $CandidatePath,

        [Parameter(Mandatory)]
        [ValidateSet('File', 'Directory')]
        [string] $ExpectedKind,

        [switch] $AllowMissingLeaf
    )

    $root = Get-NervFullStackNormalizedFullPath -Path $StateRoot
    $candidate = Get-NervFullStackNormalizedFullPath -Path $CandidatePath
    $fileSystemRoot = [System.IO.Path]::GetPathRoot($root)
    if ([string]::Equals($root, $fileSystemRoot, (Get-NervFullStackPathComparison))) {
        throw "path:filesystem-root '$root'"
    }
    if (-not (Test-NervFullStackPathContained -Root $root -Candidate $candidate)) {
        throw "path:outside-state-root '$candidate'"
    }

    $rootItem = Get-NervFullStackFileSystemItem -Path $root
    if ($null -eq $rootItem -or -not $rootItem.PSIsContainer) {
        throw "path:state-root-unavailable '$root'"
    }
    Assert-NervFullStackOrdinaryItem -Item $rootItem

    $relativePath = [System.IO.Path]::GetRelativePath($root, $candidate)
    [string[]] $components = @()
    if (-not [string]::Equals($relativePath, '.', [StringComparison]::Ordinal)) {
        $components = @($relativePath.Split([char[]]@([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar), [StringSplitOptions]::RemoveEmptyEntries))
    }

    $current = $root
    $leafItem = $rootItem
    for ($index = 0; $index -lt $components.Count; $index++) {
        $current = Join-Path $current $components[$index]
        $item = Get-NervFullStackFileSystemItem -Path $current
        $isLeaf = $index -eq ($components.Count - 1)
        if ($null -eq $item) {
            if ($isLeaf -and $AllowMissingLeaf) {
                $leafItem = $null
                break
            }

            throw "path:missing '$current'"
        }

        Assert-NervFullStackOrdinaryItem -Item $item
        if (-not $isLeaf -and -not $item.PSIsContainer) {
            throw "path:parent-not-directory '$current'"
        }
        $leafItem = $item
    }

    $exists = $null -ne $leafItem
    if ($exists) {
        $actualKind = if ($leafItem.PSIsContainer) { 'Directory' } else { 'File' }
        if (-not [string]::Equals($actualKind, $ExpectedKind, [StringComparison]::Ordinal)) {
            throw "path:kind-mismatch '$candidate' expected $ExpectedKind"
        }
    }

    $canonicalRoot = Get-NervFullStackCanonicalExistingPath -Path $root
    $canonicalCandidate = if ($exists) {
        Get-NervFullStackCanonicalExistingPath -Path $candidate
    }
    else {
        Join-Path (Get-NervFullStackCanonicalExistingPath -Path (Split-Path -Parent $candidate)) (Split-Path -Leaf $candidate)
    }
    if (-not (Test-NervFullStackPathContained -Root $canonicalRoot -Candidate $canonicalCandidate)) {
        throw "path:outside-state-root '$canonicalCandidate'"
    }

    return [pscustomobject][ordered]@{
        Verified = $true
        StateRoot = $root
        CanonicalStateRoot = $canonicalRoot
        CandidatePath = $candidate
        CanonicalPath = $canonicalCandidate
        ExpectedKind = $ExpectedKind
        Exists = $exists
    }
}

function Get-NervFullStackOpenedPathIdentity {
    param(
        [Parameter(Mandatory)]
        [object] $Handle,

        [Parameter(Mandatory)]
        [string] $CanonicalPath
    )

    $nativeIdentity = $Handle.ReadIdentity()
    return [pscustomobject][ordered]@{
        Key = "$($nativeIdentity.Device):$($nativeIdentity.Inode)"
        Device = $nativeIdentity.Device
        Inode = $nativeIdentity.Inode
        Kind = $nativeIdentity.Kind
    }
}

function Open-NervFullStackVerifiedPathHandle {
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)]
        [object] $TrustedPath,

        [Parameter(Mandatory)]
        [ValidateSet('Read', 'ReadWrite')]
        [string] $Access
    )

    if (-not $TrustedPath.Verified -or -not $TrustedPath.Exists) {
        throw 'path:not-verified-existing-object'
    }
    $revalidated = Test-NervFullStackTrustedPathGraph `
        -StateRoot $TrustedPath.StateRoot `
        -CandidatePath $TrustedPath.CandidatePath `
        -ExpectedKind $TrustedPath.ExpectedKind

    if (-not $IsMacOS) {
        return [pscustomobject][ordered]@{
            Status = 'Unknown'
            Reason = 'path:identity-unavailable'
            Provider = $null
            TrustedPath = $revalidated
            Identity = $null
            Handle = $null
        }
    }

    $handle = $null
    try {
        $handle = [Nerv.IIP.FullStack.DarwinPathHandle]::OpenPath(
            $revalidated.CanonicalPath,
            [string]::Equals($Access, 'ReadWrite', [StringComparison]::Ordinal)
        )
        $identity = Get-NervFullStackOpenedPathIdentity -Handle $handle -CanonicalPath $revalidated.CanonicalPath
        if (-not [string]::Equals($identity.Kind, $revalidated.ExpectedKind, [StringComparison]::Ordinal)) {
            throw "path:opened-kind-mismatch '$($revalidated.CanonicalPath)'"
        }

        return [pscustomobject][ordered]@{
            Status = 'Verified'
            Reason = $null
            Provider = 'macOS-fstat-opened-object-v1'
            TrustedPath = $revalidated
            Identity = $identity
            Handle = $handle
        }
    }
    catch {
        if ($null -ne $handle) {
            $handle.Dispose()
        }
        throw
    }
}

function New-NervFullStackVerifiedSessionCapability {
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)]
        [object] $PathSet,

        [Parameter(Mandatory)]
        [object] $AuthorityProof
    )

    if (-not [string]::Equals($AuthorityProof.Status, 'Verified', [StringComparison]::Ordinal) -or
        $null -eq $AuthorityProof.Handle -or $AuthorityProof.Handle.IsClosed -or $AuthorityProof.Handle.IsInvalid) {
        throw 'authority:opened-object-proof-required'
    }

    $expectedPathSetParameters = @{
        StateRoot = [string] $PathSet.StateRoot
        SessionId = [string] $PathSet.SessionId
    }
    if ($null -ne $PathSet.CreationNonce) {
        $expectedPathSetParameters.CreationNonce = [string] $PathSet.CreationNonce
    }
    $expectedPathSet = Get-NervFullStackControlPathSet @expectedPathSetParameters
    $trustedAuthority = Test-NervFullStackTrustedPathGraph `
        -StateRoot $expectedPathSet.StateRoot `
        -CandidatePath $expectedPathSet.AuthorityPath `
        -ExpectedKind File
    if (-not [string]::Equals($trustedAuthority.CanonicalPath, $AuthorityProof.TrustedPath.CanonicalPath, (Get-NervFullStackPathComparison))) {
        throw 'authority:path-mismatch'
    }
    if (-not [string]::Equals($AuthorityProof.Identity.Kind, 'File', [StringComparison]::Ordinal)) {
        throw 'authority:kind-mismatch'
    }

    return [pscustomobject][ordered]@{
        Verified = $true
        StateRoot = $expectedPathSet.StateRoot
        SessionId = $expectedPathSet.SessionId
        CreationNonce = $expectedPathSet.CreationNonce
        AuthorityIdentity = [pscustomobject][ordered]@{
            Provider = [string] $AuthorityProof.Provider
            Key = [string] $AuthorityProof.Identity.Key
            Device = $AuthorityProof.Identity.Device
            Inode = $AuthorityProof.Identity.Inode
            Kind = [string] $AuthorityProof.Identity.Kind
        }
    }
}

function Assert-NervFullStackMacOSIdentityProvider {
    if (-not $IsMacOS) {
        throw 'path:identity-unavailable'
    }
}

function Open-NervFullStackLeaseHandle {
    param(
        [Parameter(Mandatory)]
        [object] $TrustedLockPath
    )

    Assert-NervFullStackMacOSIdentityProvider
    if (-not $TrustedLockPath.Exists) {
        $createdHandle = $null
        try {
            $createdHandle = [System.IO.File]::OpenHandle(
                $TrustedLockPath.CanonicalPath,
                [System.IO.FileMode]::CreateNew,
                [System.IO.FileAccess]::ReadWrite,
                [System.IO.FileShare]::ReadWrite,
                [System.IO.FileOptions]::WriteThrough
            )
        }
        catch [System.IO.IOException] {
            # Another contender may have won CreateNew. O_NOFOLLOW_ANY and
            # opened-object fstat below still authenticate what this call opens.
        }
        finally {
            if ($null -ne $createdHandle) {
                $createdHandle.Dispose()
                [System.IO.File]::SetUnixFileMode(
                    $TrustedLockPath.CanonicalPath,
                    [System.IO.UnixFileMode]::UserRead -bor [System.IO.UnixFileMode]::UserWrite
                )
            }
        }
    }
    $handle = $null
    try {
        $handle = [Nerv.IIP.FullStack.DarwinPathHandle]::OpenLease($TrustedLockPath.CanonicalPath)
        $identity = Get-NervFullStackOpenedPathIdentity -Handle $handle -CanonicalPath $TrustedLockPath.CanonicalPath
        if (-not [string]::Equals($identity.Kind, 'File', [StringComparison]::Ordinal)) {
            throw "path:opened-kind-mismatch '$($TrustedLockPath.CanonicalPath)'"
        }
        try {
            $handle.AcquireExclusiveLease()
        }
        catch [System.IO.IOException] {
            throw "lease:unavailable '$($TrustedLockPath.CanonicalPath)'"
        }

        return $handle
    }
    catch {
        if ($null -ne $handle) {
            $handle.Dispose()
        }
        throw
    }
}

function Invoke-NervFullStackLeaseBody {
    param(
        [Parameter(Mandatory)]
        [object] $LeaseHandle,

        [Parameter(Mandatory)]
        [scriptblock] $ScriptBlock
    )

    $script:NervFullStackHeldLeaseCount++
    try {
        return (& $ScriptBlock)
    }
    finally {
        $script:NervFullStackHeldLeaseCount--
        $LeaseHandle.Dispose()
    }
}

function Invoke-WithNervFullStackRegistryLease {
    param(
        [Parameter(Mandatory)]
        [string] $StateRoot,

        [Parameter(Mandatory)]
        [scriptblock] $ScriptBlock
    )

    [void] (Initialize-NervFullStackTrustedStateRoot -StateRoot $StateRoot)
    $root = Get-NervFullStackNormalizedFullPath -Path $StateRoot
    [void] (Test-NervFullStackTrustedPathGraph -StateRoot $root -CandidatePath $root -ExpectedKind Directory)
    [void] (Test-NervFullStackTrustedPathGraph -StateRoot $root -CandidatePath (Join-Path $root 'fullstack-sessions') -ExpectedKind Directory)
    [void] (Test-NervFullStackTrustedPathGraph -StateRoot $root -CandidatePath (Join-Path $root 'fullstack-controls') -ExpectedKind Directory)
    Test-NervFullStackExistingOptionalPath -StateRoot $root -Path (Join-Path $root 'fullstack-sessions/.protocol-mode.json') -ExpectedKind File
    $lockPath = Join-Path $root 'fullstack-sessions/.sessions.lock'
    $trustedLock = Test-NervFullStackTrustedPathGraph -StateRoot $root -CandidatePath $lockPath -ExpectedKind File -AllowMissingLeaf
    $leaseHandle = Open-NervFullStackLeaseHandle -TrustedLockPath $trustedLock
    try {
        [void] (Test-NervFullStackTrustedPathGraph -StateRoot $root -CandidatePath $root -ExpectedKind Directory)
        [void] (Test-NervFullStackTrustedPathGraph -StateRoot $root -CandidatePath (Join-Path $root 'fullstack-sessions') -ExpectedKind Directory)
        [void] (Test-NervFullStackTrustedPathGraph -StateRoot $root -CandidatePath (Join-Path $root 'fullstack-controls') -ExpectedKind Directory)
        Test-NervFullStackExistingOptionalPath -StateRoot $root -Path (Join-Path $root 'fullstack-sessions/.protocol-mode.json') -ExpectedKind File
        [void] (Test-NervFullStackTrustedPathGraph -StateRoot $root -CandidatePath $lockPath -ExpectedKind File)
    }
    catch {
        $leaseHandle.Dispose()
        throw
    }

    return Invoke-NervFullStackLeaseBody -LeaseHandle $leaseHandle -ScriptBlock $ScriptBlock
}

function Test-NervFullStackExistingOptionalPath {
    param(
        [Parameter(Mandatory)]
        [string] $StateRoot,

        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [ValidateSet('File', 'Directory')]
        [string] $ExpectedKind
    )

    if ($null -ne (Get-NervFullStackFileSystemItem -Path $Path)) {
        [void] (Test-NervFullStackTrustedPathGraph -StateRoot $StateRoot -CandidatePath $Path -ExpectedKind $ExpectedKind)
    }
}

function Assert-NervFullStackSessionPathGraph {
    param(
        [Parameter(Mandatory)]
        [object] $PathSet
    )

    [void] (Test-NervFullStackTrustedPathGraph -StateRoot $PathSet.StateRoot -CandidatePath $PathSet.StateRoot -ExpectedKind Directory)
    [void] (Test-NervFullStackTrustedPathGraph -StateRoot $PathSet.StateRoot -CandidatePath $PathSet.SessionsDirectory -ExpectedKind Directory)
    [void] (Test-NervFullStackTrustedPathGraph -StateRoot $PathSet.StateRoot -CandidatePath $PathSet.ControlsDirectory -ExpectedKind Directory)
    [void] (Test-NervFullStackTrustedPathGraph -StateRoot $PathSet.StateRoot -CandidatePath $PathSet.SessionDirectory -ExpectedKind Directory)
    Test-NervFullStackExistingOptionalPath -StateRoot $PathSet.StateRoot -Path $PathSet.ProtocolModePath -ExpectedKind File
    Test-NervFullStackExistingOptionalPath -StateRoot $PathSet.StateRoot -Path $PathSet.RegistryLeasePath -ExpectedKind File
    Test-NervFullStackExistingOptionalPath -StateRoot $PathSet.StateRoot -Path $PathSet.ManifestPath -ExpectedKind File
    Test-NervFullStackExistingOptionalPath -StateRoot $PathSet.StateRoot -Path $PathSet.GuardianDirectory -ExpectedKind Directory
    Test-NervFullStackExistingOptionalPath -StateRoot $PathSet.StateRoot -Path $PathSet.GuardianRequestPath -ExpectedKind File
    Test-NervFullStackExistingOptionalPath -StateRoot $PathSet.StateRoot -Path $PathSet.GuardianAckPath -ExpectedKind File
    if ($null -ne $PathSet.PublicationTempDirectory) {
        Test-NervFullStackExistingOptionalPath -StateRoot $PathSet.StateRoot -Path $PathSet.PublicationTempDirectory -ExpectedKind Directory
    }
}

function Assert-NervFullStackAuthorityIdentity {
    param(
        [Parameter(Mandatory)]
        [object] $PathSet,

        [Parameter(Mandatory)]
        [object] $ExpectedIdentity
    )

    $trustedAuthority = Test-NervFullStackTrustedPathGraph `
        -StateRoot $PathSet.StateRoot `
        -CandidatePath $PathSet.AuthorityPath `
        -ExpectedKind File
    $currentAuthorityProof = Open-NervFullStackVerifiedPathHandle -TrustedPath $trustedAuthority -Access Read
    try {
        if (-not [string]::Equals($currentAuthorityProof.Status, 'Verified', [StringComparison]::Ordinal) -or
            -not [string]::Equals($currentAuthorityProof.Provider, $ExpectedIdentity.Provider, [StringComparison]::Ordinal) -or
            -not [string]::Equals($currentAuthorityProof.Identity.Key, $ExpectedIdentity.Key, [StringComparison]::Ordinal)) {
            throw 'authority:identity-mismatch'
        }
    }
    finally {
        if ($null -ne $currentAuthorityProof.Handle) {
            $currentAuthorityProof.Handle.Dispose()
        }
    }
}

function Invoke-WithNervFullStackSessionVerifiedLease {
    param(
        [Parameter(Mandatory)]
        [object] $VerifiedSession,

        [Parameter(Mandatory)]
        [scriptblock] $ScriptBlock
    )

    if (-not $VerifiedSession.Verified) {
        throw 'authority:verified-session-required'
    }
    $pathSetParameters = @{
        StateRoot = [string] $VerifiedSession.StateRoot
        SessionId = [string] $VerifiedSession.SessionId
    }
    if ($null -ne $VerifiedSession.CreationNonce) {
        $pathSetParameters.CreationNonce = [string] $VerifiedSession.CreationNonce
    }
    $pathSet = Get-NervFullStackControlPathSet @pathSetParameters

    Assert-NervFullStackSessionPathGraph -PathSet $pathSet

    # Authority must be reopened and compared before the session lock path is
    # validated or acquired. Possession of .session.lock is never authority.
    Assert-NervFullStackAuthorityIdentity -PathSet $pathSet -ExpectedIdentity $VerifiedSession.AuthorityIdentity

    $trustedLock = Test-NervFullStackTrustedPathGraph `
        -StateRoot $pathSet.StateRoot `
        -CandidatePath $pathSet.SessionLeasePath `
        -ExpectedKind File `
        -AllowMissingLeaf
    $leaseHandle = Open-NervFullStackLeaseHandle -TrustedLockPath $trustedLock
    try {
        Assert-NervFullStackSessionPathGraph -PathSet $pathSet
        Assert-NervFullStackAuthorityIdentity -PathSet $pathSet -ExpectedIdentity $VerifiedSession.AuthorityIdentity
        [void] (Test-NervFullStackTrustedPathGraph -StateRoot $pathSet.StateRoot -CandidatePath $pathSet.SessionLeasePath -ExpectedKind File)
    }
    catch {
        $leaseHandle.Dispose()
        throw
    }

    return Invoke-NervFullStackLeaseBody -LeaseHandle $leaseHandle -ScriptBlock $ScriptBlock
}

function Assert-NervFullStackExternalActionAllowed {
    param(
        [Parameter(Mandatory)]
        [string] $Operation
    )

    if ($script:NervFullStackHeldLeaseCount -gt 0) {
        throw "lease:external-action-forbidden '$Operation'"
    }
}
