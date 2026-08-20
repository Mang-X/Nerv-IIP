# Script-Governance:
#   Category: check
#   SideEffects:
#     - Creates and removes isolated OS temporary filesystem fixtures
#     - Starts bounded child PowerShell processes for real lease competition
#   Writes:
#     - Isolated directories under the OS temporary directory
#   Cleanup:
#     - Stops bounded child fixtures and removes their temporary directories
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) {
        throw $Message
    }
}

function Assert-Throws {
    param(
        [Parameter(Mandatory)]
        [scriptblock] $Action,

        [Parameter(Mandatory)]
        [string] $Message
    )

    $threw = $false
    try {
        $null = $Action.Invoke()
    }
    catch {
        $threw = $true
    }

    Assert-True $threw $Message
}

function Assert-ThrowsLike {
    param(
        [Parameter(Mandatory)]
        [scriptblock] $Action,

        [Parameter(Mandatory)]
        [string] $ExpectedMessage,

        [Parameter(Mandatory)]
        [string] $Message
    )

    $actualMessage = $null
    try {
        $null = $Action.Invoke()
    }
    catch {
        $actualMessage = $_.Exception.Message
    }

    Assert-True ($null -ne $actualMessage) $Message
    Assert-True $actualMessage.Contains($ExpectedMessage, [StringComparison]::Ordinal) "$Message Actual: '$actualMessage'."
}

function Write-Utf8TestFile([string] $Path, [string] $Content) {
    [System.IO.File]::WriteAllText($Path, $Content, [System.Text.UTF8Encoding]::new($false))
}

function Start-A2FixtureProcess([string] $Command, [string[]] $Arguments) {
    $argumentExpressions = [System.Collections.Generic.List[string]]::new()
    foreach ($argument in $Arguments) {
        $encodedArgument = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($argument))
        $argumentExpressions.Add("([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('$encodedArgument')))")
    }
    $wrappedCommand = "& {`n$Command`n} $($argumentExpressions -join ' ')"
    $encodedCommand = [Convert]::ToBase64String([System.Text.Encoding]::Unicode.GetBytes($wrappedCommand))
    $name = "fullstack-a2-fixture-$([Guid]::NewGuid().ToString('N'))"

    return Start-ManagedBackgroundProcess `
        -Command (Get-Process -Id $PID).Path `
        -Arguments @('-NoProfile', '-NonInteractive', '-EncodedCommand', $encodedCommand) `
        -WorkingDirectory $repoRoot `
        -Name $name `
        -LogDirectory (Join-Path $a2Root "$name-logs")
}

function Wait-A2FixtureReady([string] $Path, [object] $ManagedProcess, [string] $Name) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
    while (-not [System.IO.File]::Exists($Path) -and -not $ManagedProcess.Process.HasExited -and [DateTimeOffset]::UtcNow -lt $deadline) {
        [System.Threading.Thread]::Sleep(20)
    }

    Assert-True ([System.IO.File]::Exists($Path) -and -not $ManagedProcess.Process.HasExited) "$Name did not acquire its lease and signal readiness."
}

function Get-A2MacOSStatIdentity([string] $Path) {
    $result = Invoke-NativeCommandOutput `
        -Command '/usr/bin/stat' `
        -Arguments @('-f', '%d:%i', $Path) `
        -WorkingDirectory $repoRoot `
        -TimeoutSeconds 5 `
        -Name 'fullstack-a2-macos-stat-identity'
    return $result.Stdout.Trim()
}

function Assert-OrdinalSetEqual(
    [AllowEmptyCollection()] [string[]] $Actual,
    [AllowEmptyCollection()] [string[]] $Expected,
    [string] $Message
) {
    $actualSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $expectedSet = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($value in $Actual) {
        [void] $actualSet.Add($value)
    }
    foreach ($value in $Expected) {
        [void] $expectedSet.Add($value)
    }

    Assert-True ($actualSet.Count -eq $expectedSet.Count) "$Message (different count)"
    foreach ($value in $Expected) {
        Assert-True $actualSet.Contains($value) "$Message (missing '$value')"
    }
}

function Get-CaseMutation([string] $Value) {
    if ($Value -cmatch '[A-Z]') {
        return $Value.ToLowerInvariant()
    }

    return $Value.ToUpperInvariant()
}

# F1a frozen member: generation-activation-classification.
$member = 'generation-activation-classification'
Write-Host "Running $member"

# These literals are copied from Spec r2. They deliberately do not come from the
# implementation under test, so a vocabulary that loses a member cannot regenerate
# its own expected result.
$expectedVocabulary = [ordered]@{
    generation = @('v0', 'v1', 'v2', 'invalid')
    activation = @('GateOff', 'ActiveV2', 'InvalidMarker')
    compatibility = @('legacy-stopped', 'legacy-active-blocked', 'prototype-v1-untrusted', 'v2')
    recordKind = @('fullstack-protocol-mode', 'fullstack-session-authority', 'request', 'ack')
    publicationBoundary = @(
        'not-published',
        'temp-publication-residue',
        'final-authority-only-init-incomplete',
        'manifest-init-incomplete',
        'published-unprobed',
        'toolchain-probe-incomplete',
        'published-unstarted',
        'published-starting-uncertain'
    )
    crashSeam = @('test-only')
    guardianDisposition = @(
        'Absent-before-request',
        'Absent-after-request-before-ack',
        'Ack+Absent',
        'Ack+Active',
        'Mismatched',
        'Unknown'
    )
    guardianRegistrationState = @('Registered', 'NotRegistered', 'NonV2NotApplicable')
    resultDisposition = @(
        'ReadOnlyLegacyStopped',
        'BlockedLegacyActive',
        'BlockedPrototypeV1',
        'AlreadyInProgress',
        'CleanupBlocked',
        'CleanupFailed',
        'Stopped'
    )
    stage = @('guardian', 'aspire', 'authoritative-process', 'grammar-fallback', 'docker')
    stageStatus = @('not-attempted', 'passed', 'failed', 'blocked')
}

. (Join-Path $repoRoot 'scripts/lib/FullStackControlProtocol.ps1')

$vocabulary = Get-NervFullStackProtocolVocabulary
$actualDomains = @($vocabulary.PSObject.Properties.Name)
Assert-OrdinalSetEqual $actualDomains @($expectedVocabulary.Keys) 'Protocol vocabulary domains must be the frozen exact set.'

foreach ($domain in $expectedVocabulary.Keys) {
    $actualValues = @($vocabulary.$domain)
    Assert-OrdinalSetEqual $actualValues $expectedVocabulary[$domain] "Protocol vocabulary '$domain' must be the frozen exact set."

    foreach ($value in $expectedVocabulary[$domain]) {
        Assert-True (Test-NervFullStackProtocolValue -Domain $domain -Value $value) "Frozen value '$domain=$value' must be accepted."
        $mutatedValue = Get-CaseMutation $value
        Assert-True (-not (Test-NervFullStackProtocolValue -Domain $domain -Value $mutatedValue)) "Case mutation '$domain=$mutatedValue' must be rejected ordinally."
    }
}

Assert-True (-not (Test-NervFullStackProtocolValue -Domain 'unknown-domain' -Value 'v2')) 'Unknown protocol vocabulary domains must be rejected.'
Assert-True (Test-NervFullStackProtocolValue -Domain 'crashSeam' -Value 'test-only') 'The test-only crash seam must remain explicitly test-only.'
Assert-True (-not (Test-NervFullStackProtocolValue -Domain 'publicationBoundary' -Value 'test-only')) 'The test-only crash seam must not become a persistent publication boundary.'

$observation = New-NervFullStackProtocolObservation `
    -Generation 'v1' `
    -Activation 'ActiveV2' `
    -Compatibility 'prototype-v1-untrusted' `
    -RecordKind 'fullstack-protocol-mode' `
    -PublicationBoundary 'published-unprobed'

Assert-True ([string]::Equals($observation.Generation, 'v1', [StringComparison]::Ordinal)) 'Generation must be preserved as observed.'
Assert-True ([string]::Equals($observation.Activation, 'ActiveV2', [StringComparison]::Ordinal)) 'Activation must be preserved as observed and not derived from generation.'
Assert-True ([string]::Equals($observation.Compatibility, 'prototype-v1-untrusted', [StringComparison]::Ordinal)) 'Compatibility must be preserved as observed.'
Assert-True ([string]::Equals($observation.RecordKind, 'fullstack-protocol-mode', [StringComparison]::Ordinal)) 'Record kind must be preserved as observed.'
Assert-True ([string]::Equals($observation.PublicationBoundary, 'published-unprobed', [StringComparison]::Ordinal)) 'Publication boundary must be preserved as observed.'

$nullableObservation = New-NervFullStackProtocolObservation `
    -Generation 'v0' `
    -Activation 'GateOff' `
    -Compatibility 'legacy-stopped' `
    -RecordKind 'fullstack-session-authority'
Assert-True ($null -eq $nullableObservation.PublicationBoundary) 'Publication boundary must remain nullable when no crash residue was observed.'

Assert-Throws {
    New-NervFullStackProtocolObservation `
        -Generation 'v2' `
        -Activation 'InvalidMarker' `
        -Compatibility 'v2' `
        -RecordKind 'fullstack-session-authority' `
        -PublicationBoundary 'test-only'
} 'A test-only crash seam must not be accepted as a persistent publication boundary.'

Assert-Throws {
    New-NervFullStackProtocolObservation `
        -Generation 'V2' `
        -Activation 'GateOff' `
        -Compatibility 'v2' `
        -RecordKind 'fullstack-session-authority'
} 'Protocol observation values must be validated with Ordinal semantics.'

Write-Host "Full-stack v2 protocol tests passed: $member"

# F1a frozen member: verified-session-cas-and-leases (A2 portion).
$member = 'verified-session-cas-and-leases'
Write-Host "Running $member"

$a2Library = Join-Path $repoRoot 'scripts/lib/FullStackControlFileSystem.ps1'
if (Test-Path -LiteralPath $a2Library -PathType Leaf) {
    . $a2Library
}

$expectedA2Commands = @(
    'Get-NervFullStackControlPathSet',
    'Initialize-NervFullStackTrustedStateRoot',
    'Test-NervFullStackTrustedPathGraph',
    'Open-NervFullStackVerifiedPathHandle',
    'New-NervFullStackVerifiedSessionCapability',
    'Invoke-WithNervFullStackRegistryLease',
    'Invoke-WithNervFullStackSessionVerifiedLease',
    'Assert-NervFullStackExternalActionAllowed'
)
foreach ($commandName in $expectedA2Commands) {
    Assert-True ($null -ne (Get-Command -Name $commandName -CommandType Function -ErrorAction SilentlyContinue)) "A2 interface '$commandName' is missing."
}

$a2Root = Join-Path ([System.IO.Path]::GetTempPath()) "nerv-fullstack-a2-$([Guid]::NewGuid().ToString('N'))"
$stateRoot = Join-Path $a2Root 'state'
$outsideRoot = Join-Path $a2Root 'outside'
$adjacentRoot = "$stateRoot-adjacent"
$sessionId = 'nerv-abcd-123456'
$creationNonce = '0123456789abcdef0123456789abcdef'
[void] [System.IO.Directory]::CreateDirectory($a2Root)
[void] [System.IO.Directory]::CreateDirectory($outsideRoot)
[void] [System.IO.Directory]::CreateDirectory($adjacentRoot)

try {
    $initializedRoot = Initialize-NervFullStackTrustedStateRoot -StateRoot $stateRoot
    Assert-True $initializedRoot.Verified 'A newly initialized StateRoot must be verified.'
    Assert-True ([System.IO.Directory]::Exists((Join-Path $stateRoot 'fullstack-sessions'))) 'StateRoot initialization must create fullstack-sessions.'
    Assert-True ([System.IO.Directory]::Exists((Join-Path $stateRoot 'fullstack-controls'))) 'StateRoot initialization must create fullstack-controls.'

    $pathSet = Get-NervFullStackControlPathSet -StateRoot $stateRoot -SessionId $sessionId -CreationNonce $creationNonce
    Assert-True ([string]::Equals($pathSet.RegistryLeasePath, (Join-Path $stateRoot 'fullstack-sessions/.sessions.lock'), [StringComparison]::Ordinal)) 'RegistryLease must use the frozen exact path.'
    Assert-True ([string]::Equals($pathSet.SessionLeasePath, (Join-Path $stateRoot "fullstack-controls/$sessionId/.session.lock"), [StringComparison]::Ordinal)) 'SessionVerifiedLease must use the frozen exact path.'
    Assert-True ([string]::Equals($pathSet.AuthorityPath, (Join-Path $stateRoot "fullstack-controls/$sessionId/authority.json"), [StringComparison]::Ordinal)) 'Authority must use the v2 control namespace.'
    Assert-True ([string]::Equals($pathSet.ManifestPath, (Join-Path $stateRoot "fullstack-sessions/$sessionId.json"), [StringComparison]::Ordinal)) 'Manifest must stay in the canonical manifest namespace.'
    Assert-True ([string]::Equals($pathSet.PublicationTempDirectory, (Join-Path $stateRoot "fullstack-controls/.tmp-$sessionId-$creationNonce"), [StringComparison]::Ordinal)) 'Publication temp must bind the exact session and creation nonce.'
    Assert-True ([string]::Equals($pathSet.GuardianRequestPath, (Join-Path $stateRoot "fullstack-controls/$sessionId/guardian/stop.request.json"), [StringComparison]::Ordinal)) 'Guardian request must use the v2 guardian namespace.'
    Assert-True ([string]::Equals($pathSet.GuardianAckPath, (Join-Path $stateRoot "fullstack-controls/$sessionId/guardian/stop.ack.json"), [StringComparison]::Ordinal)) 'Guardian acknowledgement must use the v2 guardian namespace.'
    Assert-Throws { Get-NervFullStackControlPathSet -StateRoot $stateRoot -SessionId '../escape' } 'Invalid session IDs must fail closed.'
    Assert-Throws { Get-NervFullStackControlPathSet -StateRoot $stateRoot -SessionId $sessionId -CreationNonce 'ABCDEF' } 'Creation nonce must be exact lowercase 128-bit hex.'
    Assert-Throws { Initialize-NervFullStackTrustedStateRoot -StateRoot ([System.IO.Path]::GetPathRoot($stateRoot)) } 'A filesystem root must never be accepted as StateRoot.'

    $ordinaryDirectory = Join-Path $stateRoot 'ordinary'
    $ordinaryFile = Join-Path $ordinaryDirectory 'record.json'
    [void] [System.IO.Directory]::CreateDirectory($ordinaryDirectory)
    Write-Utf8TestFile -Path $ordinaryFile -Content '{"same":"bytes"}'
    $trustedDirectory = Test-NervFullStackTrustedPathGraph -StateRoot $stateRoot -CandidatePath $ordinaryDirectory -ExpectedKind Directory
    $trustedFile = Test-NervFullStackTrustedPathGraph -StateRoot $stateRoot -CandidatePath $ordinaryFile -ExpectedKind File
    Assert-True ($trustedDirectory.Verified -and $trustedFile.Verified) 'Ordinary directory and file fixtures must validate.'

    $missingFile = Join-Path $ordinaryDirectory 'missing.json'
    $trustedMissing = Test-NervFullStackTrustedPathGraph -StateRoot $stateRoot -CandidatePath $missingFile -ExpectedKind File -AllowMissingLeaf
    Assert-True ($trustedMissing.Verified -and -not $trustedMissing.Exists) 'AllowMissingLeaf must retain a verified missing leaf under a trusted parent.'
    Assert-Throws { Test-NervFullStackTrustedPathGraph -StateRoot $stateRoot -CandidatePath $missingFile -ExpectedKind File } 'A missing leaf must fail without AllowMissingLeaf.'

    $adjacentFile = Join-Path $adjacentRoot 'record.json'
    Write-Utf8TestFile -Path $adjacentFile -Content '{}'
    Assert-ThrowsLike {
        Test-NervFullStackTrustedPathGraph -StateRoot $stateRoot -CandidatePath $adjacentFile -ExpectedKind File
    } 'path:outside-state-root' 'An adjacent string-prefix path must fail separator-aware containment.'
    $outsideFile = Join-Path $outsideRoot 'record.json'
    Write-Utf8TestFile -Path $outsideFile -Content '{}'
    Assert-ThrowsLike {
        Test-NervFullStackTrustedPathGraph -StateRoot $stateRoot -CandidatePath $outsideFile -ExpectedKind File
    } 'path:outside-state-root' 'An outside path must fail canonical containment.'

    if ($IsMacOS) {
        $linkedParent = Join-Path $stateRoot 'linked-parent'
        $linkedLeaf = Join-Path $stateRoot 'linked-leaf.json'
        [void] [System.IO.Directory]::CreateSymbolicLink($linkedParent, $outsideRoot)
        [void] [System.IO.File]::CreateSymbolicLink($linkedLeaf, $outsideFile)
        Assert-ThrowsLike {
            Test-NervFullStackTrustedPathGraph -StateRoot $stateRoot -CandidatePath (Join-Path $linkedParent 'record.json') -ExpectedKind File
        } 'path:link-or-reparse' 'A symlink parent must fail the trusted graph.'
        Assert-ThrowsLike {
            Test-NervFullStackTrustedPathGraph -StateRoot $stateRoot -CandidatePath $linkedLeaf -ExpectedKind File
        } 'path:link-or-reparse' 'A symlink leaf must fail the trusted graph.'

        $linkedStateRoot = Join-Path $a2Root 'linked-state-root'
        [void] [System.IO.Directory]::CreateSymbolicLink($linkedStateRoot, $stateRoot)
        Assert-ThrowsLike {
            Initialize-NervFullStackTrustedStateRoot -StateRoot $linkedStateRoot
        } 'path:link-or-reparse' 'A symlink StateRoot must fail closed.'

        $registryOutside = Join-Path $outsideRoot 'registry.lock'
        Write-Utf8TestFile -Path $registryOutside -Content 'outside'
        [void] [System.IO.File]::CreateSymbolicLink($pathSet.RegistryLeasePath, $registryOutside)
        Assert-ThrowsLike {
            Invoke-WithNervFullStackRegistryLease -StateRoot $stateRoot -ScriptBlock { throw 'must-not-run' }
        } 'path:link-or-reparse' 'RegistryLease must reject a symlink lock leaf before its body runs.'
        Assert-True ([string]::Equals([System.IO.File]::ReadAllText($registryOutside), 'outside', [StringComparison]::Ordinal)) 'A rejected RegistryLease symlink must not modify its outside target.'
        [System.IO.File]::Delete($pathSet.RegistryLeasePath)
    }

    $sessionDirectory = $pathSet.SessionDirectory
    [void] [System.IO.Directory]::CreateDirectory($sessionDirectory)
    [void] [System.IO.Directory]::CreateDirectory($pathSet.GuardianDirectory)
    Write-Utf8TestFile -Path $pathSet.AuthorityPath -Content '{"authority":1}'
    Write-Utf8TestFile -Path $pathSet.ManifestPath -Content '{"manifest":1}'

    $authorityTrusted = Test-NervFullStackTrustedPathGraph -StateRoot $stateRoot -CandidatePath $pathSet.AuthorityPath -ExpectedKind File
    $authorityProof = Open-NervFullStackVerifiedPathHandle -TrustedPath $authorityTrusted -Access Read
    if ($IsMacOS) {
        Assert-True ([string]::Equals($authorityProof.Status, 'Verified', [StringComparison]::Ordinal)) 'macOS opened-object identity must be verified.'
        Assert-True ([string]::Equals($authorityProof.Provider, 'macOS-fstat-opened-object-v1', [StringComparison]::Ordinal)) 'macOS identity must name the proven fstat opened-object provider.'
        Assert-True (-not [string]::IsNullOrWhiteSpace($authorityProof.Identity.Key)) 'Verified opened-object identity must have a stable device/inode key.'
        Assert-True ([string]::Equals($authorityProof.Identity.Key, (Get-A2MacOSStatIdentity -Path $pathSet.AuthorityPath), [StringComparison]::Ordinal)) 'The opened-handle fstat offsets must agree with the current macOS stat provider.'

        $originalTimestamp = [System.IO.File]::GetLastWriteTimeUtc($pathSet.AuthorityPath)
        $replacementPath = Join-Path $sessionDirectory 'authority.replacement'
        Write-Utf8TestFile -Path $replacementPath -Content '{"authority":2}'
        [System.IO.File]::SetLastWriteTimeUtc($replacementPath, $originalTimestamp)
        [System.IO.File]::Move($replacementPath, $pathSet.AuthorityPath, $true)
        [System.IO.File]::SetLastWriteTimeUtc($pathSet.AuthorityPath, $originalTimestamp)
        $replacementTrusted = Test-NervFullStackTrustedPathGraph -StateRoot $stateRoot -CandidatePath $pathSet.AuthorityPath -ExpectedKind File
        $replacementProof = Open-NervFullStackVerifiedPathHandle -TrustedPath $replacementTrusted -Access Read
        try {
            Assert-True (-not [string]::Equals($authorityProof.Identity.Key, $replacementProof.Identity.Key, [StringComparison]::Ordinal)) 'Same-size/same-mtime replacement must change opened-object identity.'
            $verifiedSession = New-NervFullStackVerifiedSessionCapability -PathSet $pathSet -AuthorityProof $replacementProof
        }
        finally {
            $authorityProof.Handle.Dispose()
            $replacementProof.Handle.Dispose()
        }

        $sessionLockOutside = Join-Path $outsideRoot 'session.lock'
        Write-Utf8TestFile -Path $sessionLockOutside -Content 'outside'
        [void] [System.IO.File]::CreateSymbolicLink($pathSet.SessionLeasePath, $sessionLockOutside)
        Assert-ThrowsLike {
            Invoke-WithNervFullStackSessionVerifiedLease -VerifiedSession $verifiedSession -ScriptBlock { throw 'must-not-run' }
        } 'path:link-or-reparse' 'SessionVerifiedLease must reject a symlink lock leaf.'
        [System.IO.File]::Delete($pathSet.SessionLeasePath)

        $script:destructiveCallCount = 0
        Invoke-WithNervFullStackSessionVerifiedLease -VerifiedSession $verifiedSession -ScriptBlock {
            foreach ($operation in @('guardian', 'Aspire', 'process', 'Docker', 'wait', 'poll', 'drain')) {
                Assert-ThrowsLike {
                    Assert-NervFullStackExternalActionAllowed -Operation $operation
                    $script:destructiveCallCount++
                } 'lease:external-action-forbidden' "External operation '$operation' must fail before its destructive counter increments."
            }
        }
        Assert-True ($script:destructiveCallCount -eq 0) 'Every external action must fail before the destructive counter increments.'
        Assert-NervFullStackExternalActionAllowed -Operation 'guardian'
        Assert-True ([System.IO.File]::Exists($pathSet.SessionLeasePath)) 'SessionVerifiedLease must use a persistent ordinary lock file.'
        Assert-True (([System.IO.FileInfo]::new($pathSet.SessionLeasePath)).Length -eq 0) '.session.lock must not carry JSON or protocol payload.'
        $sessionLockMode = [System.IO.File]::GetUnixFileMode($pathSet.SessionLeasePath)
        Assert-True (($sessionLockMode -band [System.IO.UnixFileMode]::UserRead) -ne 0 -and ($sessionLockMode -band [System.IO.UnixFileMode]::UserWrite) -ne 0) '.session.lock must remain readable and writable by its owner across processes.'

        $registryReady = Join-Path $a2Root 'registry.ready'
        $registryChildCommand = @'
param($Library, $Root, $Ready)
$ErrorActionPreference = 'Stop'
. $Library
Invoke-WithNervFullStackRegistryLease -StateRoot $Root -ScriptBlock {
    [System.IO.File]::WriteAllText($Ready, 'ready')
    [System.Threading.Thread]::SpinWait(150000000)
}
'@
        $registryChild = Start-A2FixtureProcess -Command $registryChildCommand -Arguments @($a2Library, $stateRoot, $registryReady)
        try {
            Wait-A2FixtureReady -Path $registryReady -ManagedProcess $registryChild -Name 'RegistryLease cross-process holder'
            Assert-ThrowsLike {
                Invoke-WithNervFullStackRegistryLease -StateRoot $stateRoot -ScriptBlock { throw 'must-not-run' }
            } 'lease:unavailable' 'A second process must not acquire RegistryLease while the first process owns it.'
            Assert-True $registryChild.Process.WaitForExit(10000) 'RegistryLease holder process must exit in bounded time.'
            Assert-True ($registryChild.Process.ExitCode -eq 0) 'RegistryLease holder process must complete successfully.'
        }
        finally {
            $registryChild.Stop.Invoke('RegistryLease fixture cleanup')
        }
        $script:registryReacquired = 0
        Invoke-WithNervFullStackRegistryLease -StateRoot $stateRoot -ScriptBlock { $script:registryReacquired++ }
        Assert-True ($script:registryReacquired -eq 1) 'RegistryLease must be reacquirable after the holder releases it.'
        Assert-True (([System.IO.FileInfo]::new($pathSet.RegistryLeasePath)).Length -eq 0) '.sessions.lock must not carry JSON or protocol payload.'

        $sessionReady = Join-Path $a2Root 'session.ready'
        $sessionChildCommand = @'
param($Library, $Root, $SessionId, $CreationNonce, $Ready)
$ErrorActionPreference = 'Stop'
. $Library
$paths = Get-NervFullStackControlPathSet -StateRoot $Root -SessionId $SessionId -CreationNonce $CreationNonce
$trusted = Test-NervFullStackTrustedPathGraph -StateRoot $Root -CandidatePath $paths.AuthorityPath -ExpectedKind File
$proof = Open-NervFullStackVerifiedPathHandle -TrustedPath $trusted -Access Read
try {
    $session = New-NervFullStackVerifiedSessionCapability -PathSet $paths -AuthorityProof $proof
}
finally {
    $proof.Handle.Dispose()
}
Invoke-WithNervFullStackSessionVerifiedLease -VerifiedSession $session -ScriptBlock {
    [System.IO.File]::WriteAllText($Ready, 'ready')
    [System.Threading.Thread]::SpinWait(150000000)
}
'@
        $sessionChild = Start-A2FixtureProcess -Command $sessionChildCommand -Arguments @($a2Library, $stateRoot, $sessionId, $creationNonce, $sessionReady)
        try {
            Wait-A2FixtureReady -Path $sessionReady -ManagedProcess $sessionChild -Name 'SessionVerifiedLease cross-process holder'
            Assert-ThrowsLike {
                Invoke-WithNervFullStackSessionVerifiedLease -VerifiedSession $verifiedSession -ScriptBlock { throw 'must-not-run' }
            } 'lease:unavailable' 'A second process must not acquire SessionVerifiedLease while the first process owns it.'
            Assert-True $sessionChild.Process.WaitForExit(10000) 'SessionVerifiedLease holder process must exit in bounded time.'
            Assert-True ($sessionChild.Process.ExitCode -eq 0) 'SessionVerifiedLease holder process must complete successfully.'
        }
        finally {
            $sessionChild.Stop.Invoke('SessionVerifiedLease fixture cleanup')
        }
        $script:sessionReacquired = 0
        Invoke-WithNervFullStackSessionVerifiedLease -VerifiedSession $verifiedSession -ScriptBlock { $script:sessionReacquired++ }
        Assert-True ($script:sessionReacquired -eq 1) 'SessionVerifiedLease must be reacquirable after the holder releases it without reopening its own lock path.'

        $orderRoot = Join-Path $a2Root 'authority-order-state'
        [void] (Initialize-NervFullStackTrustedStateRoot -StateRoot $orderRoot)
        $orderPaths = Get-NervFullStackControlPathSet -StateRoot $orderRoot -SessionId $sessionId -CreationNonce $creationNonce
        [void] [System.IO.Directory]::CreateDirectory($orderPaths.SessionDirectory)
        Write-Utf8TestFile -Path $orderPaths.AuthorityPath -Content '{"authority":1}'
        $orderTrusted = Test-NervFullStackTrustedPathGraph -StateRoot $orderRoot -CandidatePath $orderPaths.AuthorityPath -ExpectedKind File
        $orderProof = Open-NervFullStackVerifiedPathHandle -TrustedPath $orderTrusted -Access Read
        try {
            $orderSession = New-NervFullStackVerifiedSessionCapability -PathSet $orderPaths -AuthorityProof $orderProof
        }
        finally {
            $orderProof.Handle.Dispose()
        }
        $orderReplacement = Join-Path $orderPaths.SessionDirectory 'authority.replacement'
        Write-Utf8TestFile -Path $orderReplacement -Content '{"authority":2}'
        [System.IO.File]::Move($orderReplacement, $orderPaths.AuthorityPath, $true)
        [void] [System.IO.Directory]::CreateDirectory($orderPaths.SessionLeasePath)
        Assert-ThrowsLike {
            Invoke-WithNervFullStackSessionVerifiedLease -VerifiedSession $orderSession -ScriptBlock { throw 'must-not-run' }
        } 'authority:identity-mismatch' 'Authority must be reopened and rejected before SessionVerifiedLease touches an invalid lock target.'
    }
    else {
        try {
            Assert-True ([string]::Equals($authorityProof.Status, 'Unknown', [StringComparison]::Ordinal)) 'An unverified OS provider must return Unknown.'
            Assert-True ([string]::Equals($authorityProof.Reason, 'path:identity-unavailable', [StringComparison]::Ordinal)) 'An unverified OS provider must use the stable identity-unavailable reason.'
            Assert-True ($null -eq $authorityProof.Handle) 'An unverified OS provider must not expose a trusted handle.'
        }
        finally {
            if ($null -ne $authorityProof.Handle) { $authorityProof.Handle.Dispose() }
        }
    }
}
finally {
    if ([System.IO.Directory]::Exists($a2Root)) {
        [System.IO.Directory]::Delete($a2Root, $true)
    }
}

Write-Host "Full-stack v2 protocol tests passed: $member"
