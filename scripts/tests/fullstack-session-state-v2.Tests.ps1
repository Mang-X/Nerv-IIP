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

$a4Library = Join-Path $repoRoot 'scripts/lib/FullStackProtocolClassifier.ps1'
if (Test-Path -LiteralPath $a4Library -PathType Leaf) {
    . $a4Library
}

$expectedA4Commands = @(
    'Get-NervFullStackProtocolGenerationObservation',
    'Get-NervFullStackProtocolActivationObservation',
    'Get-NervFullStackCompatibilityDisposition',
    'Get-NervFullStackPublicationBoundaryObservation'
)
$missingA4Commands = @($expectedA4Commands | Where-Object {
    $null -eq (Get-Command -Name $_ -CommandType Function -ErrorAction SilentlyContinue)
})
Assert-True ($missingA4Commands.Count -eq 0) "A4 interfaces are missing: $($missingA4Commands -join ', ')."

function New-A4FixtureRoot([string] $Name) {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) "nerv-fullstack-a4-$Name-$([Guid]::NewGuid().ToString('N'))"
    [void] [System.IO.Directory]::CreateDirectory((Join-Path $root 'fullstack-sessions'))
    [void] [System.IO.Directory]::CreateDirectory((Join-Path $root 'fullstack-controls'))
    return $root
}

function New-A4AuthorityRecord([string] $Root, [string] $SessionId, [string] $CreationNonce) {
    $manifestPath = Join-Path $Root "fullstack-sessions/$SessionId.json"
    return [ordered]@{
        schemaVersion = 2
        kind = 'fullstack-session-authority'
        sessionId = $SessionId
        creationNonce = $CreationNonce
        worktreeRoot = $Root
        manifestPath = $manifestPath
        createdBy = [ordered]@{
            pid = 4242
            processStartTimeUtc = '2026-08-18T00:00:00.0000000Z'
        }
        createdAtUtc = '2026-08-18T00:00:00.0000000Z'
    }
}

function New-A4V2ManifestRecord {
    param(
        [string] $Root,
        [string] $SessionId,
        [string] $CreationNonce,
        [bool] $ToolchainSnapshotComplete = $false,
        [object[]] $ToolchainProbeIdentities = @(),
        [bool] $RuntimeStartAttempted = $false,
        [object[]] $RuntimeIdentities = @()
    )

    return [ordered]@{
        schemaVersion = 2
        kind = 'fullstack-session-authority'
        controlProtocolVersion = 2
        sessionId = $SessionId
        creationNonce = $CreationNonce
        worktreeRoot = $Root
        state = 'Creating'
        toolchainSnapshotComplete = $ToolchainSnapshotComplete
        toolchainProbeIdentities = @($ToolchainProbeIdentities)
        runtimeStartAttempted = $RuntimeStartAttempted
        runtimeIdentities = @($RuntimeIdentities)
    }
}

function Write-A4Record([string] $Path, [object] $Record) {
    [void] [System.IO.Directory]::CreateDirectory((Split-Path -Parent $Path))
    Write-Utf8TestFile -Path $Path -Content ($Record | ConvertTo-Json -Depth 20 -Compress)
}

function Publish-A4Authority([string] $Root, [string] $SessionId, [string] $CreationNonce) {
    $sessionDirectory = Join-Path $Root "fullstack-controls/$SessionId"
    [void] [System.IO.Directory]::CreateDirectory($sessionDirectory)
    Write-Utf8TestFile -Path (Join-Path $sessionDirectory '.session.lock') -Content ''
    Write-A4Record `
        -Path (Join-Path $sessionDirectory 'authority.json') `
        -Record (New-A4AuthorityRecord -Root $Root -SessionId $SessionId -CreationNonce $CreationNonce)
}

function Publish-A4V2Session {
    param(
        [string] $Root,
        [string] $SessionId,
        [string] $CreationNonce,
        [bool] $ToolchainSnapshotComplete = $false,
        [object[]] $ToolchainProbeIdentities = @(),
        [bool] $RuntimeStartAttempted = $false,
        [object[]] $RuntimeIdentities = @()
    )

    Publish-A4Authority -Root $Root -SessionId $SessionId -CreationNonce $CreationNonce
    Write-A4Record `
        -Path (Join-Path $Root "fullstack-sessions/$SessionId.json") `
        -Record (New-A4V2ManifestRecord `
            -Root $Root `
            -SessionId $SessionId `
            -CreationNonce $CreationNonce `
            -ToolchainSnapshotComplete $ToolchainSnapshotComplete `
            -ToolchainProbeIdentities $ToolchainProbeIdentities `
            -RuntimeStartAttempted $RuntimeStartAttempted `
            -RuntimeIdentities $RuntimeIdentities)
}

function Write-A4ActivationMarker([string] $Root, [bool] $Valid) {
    $marker = if ($Valid) {
        [ordered]@{
            schemaVersion = 2
            kind = 'fullstack-protocol-mode'
            controlProtocolVersion = 2
            stateRoot = $Root
            e1CapabilityVersion = 'e1-v1'
            e3CapabilityVersion = 'e3-v1'
            activatedFromHeadSha = ('a' * 40)
            f1FrozenManifestHash = ('b' * 64)
            f1EvidenceHash = ('c' * 64)
            activationNonce = '0123456789abcdef0123456789abcdef'
            activatedAtUtc = '2026-08-18T00:00:00.0000000Z'
        }
    }
    else {
        [ordered]@{
            schemaVersion = 2
            kind = 'fullstack-protocol-mode'
            controlProtocolVersion = 2
            stateRoot = (Join-Path $Root 'wrong-root')
        }
    }
    Write-A4Record -Path (Join-Path $Root 'fullstack-sessions/.protocol-mode.json') -Record $marker
}

function Get-A4TreeFingerprint([string] $Root) {
    $rows = [System.Collections.Generic.List[string]]::new()
    foreach ($path in [System.IO.Directory]::EnumerateFileSystemEntries($Root, '*', [System.IO.SearchOption]::AllDirectories)) {
        $relativePath = [System.IO.Path]::GetRelativePath($Root, $path)
        if ([System.IO.Directory]::Exists($path)) {
            $rows.Add("D|$relativePath")
        }
        else {
            $bytes = [System.IO.File]::ReadAllBytes($path)
            $hash = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bytes))
            $rows.Add("F|$relativePath|$hash")
        }
    }
    $rows.Sort([StringComparer]::Ordinal)
    return ($rows -join "`n")
}

function Assert-A4ObservationReadOnly([string] $Root, [scriptblock] $Action, [string] $Message) {
    $before = Get-A4TreeFingerprint -Root $Root
    $results = @($Action.Invoke())
    $after = Get-A4TreeFingerprint -Root $Root
    Assert-True ([string]::Equals($before, $after, [StringComparison]::Ordinal)) $Message
    Assert-True ($results.Count -eq 1) "$Message The read must return exactly one observation."
    return $results[0]
}

$a4Roots = [System.Collections.Generic.List[string]]::new()
try {
    $v0StoppedRoot = New-A4FixtureRoot -Name 'v0-stopped'
    $a4Roots.Add($v0StoppedRoot)
    $v0SessionId = 'nerv-a400-000001'
    Write-A4Record -Path (Join-Path $v0StoppedRoot "fullstack-sessions/$v0SessionId.json") -Record ([ordered]@{
        schemaVersion = 1; sessionId = $v0SessionId; state = 'Stopped'; worktreeRoot = $v0StoppedRoot
    })
    [System.IO.Directory]::Delete((Join-Path $v0StoppedRoot 'fullstack-controls'))
    $v0Stopped = Assert-A4ObservationReadOnly -Root $v0StoppedRoot -Action {
        Get-NervFullStackProtocolGenerationObservation -StateRoot $v0StoppedRoot -SessionId $v0SessionId
    } -Message 'Reading a stopped v0 fixture must not write, migrate, or delete files.'
    Assert-True ([string]::Equals($v0Stopped.Generation, 'v0', [StringComparison]::Ordinal)) 'A canonical legacy manifest must remain v0.'
    Assert-True ([string]::Equals($v0Stopped.State, 'Stopped', [StringComparison]::Ordinal)) 'The v0 state must be observed ordinally.'
    $gateOff = Get-NervFullStackProtocolActivationObservation -StateRoot $v0StoppedRoot
    Assert-True ([string]::Equals($gateOff.Activation, 'GateOff', [StringComparison]::Ordinal)) 'An absent marker must mean GateOff.'
    $v0StoppedDisposition = Get-NervFullStackCompatibilityDisposition -GenerationObservation $v0Stopped -ActivationObservation $gateOff
    Assert-True ([string]::Equals($v0StoppedDisposition.Disposition, 'ReadOnlyLegacyStopped', [StringComparison]::Ordinal)) 'Stopped v0 must be read-only idempotent.'

    $v0ActiveRoot = New-A4FixtureRoot -Name 'v0-active'
    $a4Roots.Add($v0ActiveRoot)
    Write-A4Record -Path (Join-Path $v0ActiveRoot "fullstack-sessions/$v0SessionId.json") -Record ([ordered]@{
        schemaVersion = 1; sessionId = $v0SessionId; state = 'Running'; worktreeRoot = $v0ActiveRoot
    })
    Write-A4ActivationMarker -Root $v0ActiveRoot -Valid $true
    $v0Active = Get-NervFullStackProtocolGenerationObservation -StateRoot $v0ActiveRoot -SessionId $v0SessionId
    $activeMarker = Get-NervFullStackProtocolActivationObservation -StateRoot $v0ActiveRoot
    Assert-True ([string]::Equals($v0Active.Generation, 'v0', [StringComparison]::Ordinal)) 'A valid marker must not upgrade a v0 record to v2 generation.'
    Assert-True ([string]::Equals($activeMarker.Activation, 'ActiveV2', [StringComparison]::Ordinal)) 'A complete marker must be observed independently as ActiveV2.'
    $v0ActiveDisposition = Get-NervFullStackCompatibilityDisposition -GenerationObservation $v0Active -ActivationObservation $activeMarker
    Assert-True ([string]::Equals($v0ActiveDisposition.Disposition, 'BlockedLegacyActive', [StringComparison]::Ordinal)) 'An active v0 record must fail closed even when activation is ActiveV2.'

    $v1Root = New-A4FixtureRoot -Name 'v1-flat'
    $a4Roots.Add($v1Root)
    Write-A4Record -Path (Join-Path $v1Root "fullstack-sessions/$v0SessionId.json") -Record ([ordered]@{
        schemaVersion = 1; sessionId = $v0SessionId; state = 'Running'; worktreeRoot = $v1Root
    })
    Write-Utf8TestFile -Path (Join-Path $v1Root "fullstack-sessions/$v0SessionId.authority") -Content '{"creationNonce":"0123456789abcdef0123456789abcdef"}'
    $v1 = Assert-A4ObservationReadOnly -Root $v1Root -Action {
        Get-NervFullStackProtocolGenerationObservation -StateRoot $v1Root -SessionId $v0SessionId
    } -Message 'Reading a flat v1 sidecar must not adopt, migrate, or delete it.'
    Assert-True ([string]::Equals($v1.Generation, 'v1', [StringComparison]::Ordinal)) 'A flat v1 sidecar must remain unsupported prototype state.'
    $v1Disposition = Get-NervFullStackCompatibilityDisposition -GenerationObservation $v1 -ActivationObservation (Get-NervFullStackProtocolActivationObservation -StateRoot $v1Root)
    Assert-True ([string]::Equals($v1Disposition.Disposition, 'BlockedPrototypeV1', [StringComparison]::Ordinal)) 'A flat v1 prototype must be blocked without promotion.'

    $validV2Root = New-A4FixtureRoot -Name 'valid-v2'
    $a4Roots.Add($validV2Root)
    $v2SessionId = 'nerv-a400-000002'
    $v2Nonce = '0123456789abcdef0123456789abcdef'
    Publish-A4V2Session -Root $validV2Root -SessionId $v2SessionId -CreationNonce $v2Nonce
    $v2 = Get-NervFullStackProtocolGenerationObservation -StateRoot $validV2Root -SessionId $v2SessionId
    Assert-True ([string]::Equals($v2.Generation, 'v2', [StringComparison]::Ordinal)) 'Matching authority and manifest must be classified as v2 independently of activation.'
    $v2Disposition = Get-NervFullStackCompatibilityDisposition -GenerationObservation $v2 -ActivationObservation (Get-NervFullStackProtocolActivationObservation -StateRoot $validV2Root)
    Assert-True ([string]::Equals($v2Disposition.Disposition, 'v2', [StringComparison]::Ordinal)) 'A legal v2 generation remains v2 while activation is reported separately.'
    Assert-True ([string]::Equals($v2Disposition.Activation, 'GateOff', [StringComparison]::Ordinal)) 'Compatibility must preserve GateOff rather than infer activation from v2 generation.'

    $invalidMarkerRoot = New-A4FixtureRoot -Name 'invalid-marker'
    $a4Roots.Add($invalidMarkerRoot)
    Write-A4ActivationMarker -Root $invalidMarkerRoot -Valid $false
    $invalidMarker = Assert-A4ObservationReadOnly -Root $invalidMarkerRoot -Action {
        Get-NervFullStackProtocolActivationObservation -StateRoot $invalidMarkerRoot
    } -Message 'Reading an invalid marker must not rewrite or delete it.'
    Assert-True ([string]::Equals($invalidMarker.Activation, 'InvalidMarker', [StringComparison]::Ordinal)) 'A field-mismatched marker must fail closed as InvalidMarker.'

    $boundaryFixtures = [System.Collections.Generic.List[object]]::new()

    $boundary1Root = New-A4FixtureRoot -Name 'boundary-01'
    $a4Roots.Add($boundary1Root)
    $boundaryFixtures.Add([pscustomobject]@{ Number = 1; Root = $boundary1Root; Sessions = @(
        [pscustomobject]@{ SessionId = $v2SessionId; Expected = 'not-published' }
    ) })

    $boundary2Root = New-A4FixtureRoot -Name 'boundary-02'
    $a4Roots.Add($boundary2Root)
    $tempDirectory = Join-Path $boundary2Root "fullstack-controls/.tmp-$v2SessionId-$v2Nonce"
    [void] [System.IO.Directory]::CreateDirectory($tempDirectory)
    Write-Utf8TestFile -Path (Join-Path $tempDirectory 'authority.json') -Content '{"kind":"fullstack-session-authority"'
    $boundaryFixtures.Add([pscustomobject]@{ Number = 2; Root = $boundary2Root; Sessions = @(
        [pscustomobject]@{ SessionId = $v2SessionId; Expected = 'temp-publication-residue' }
    ) })

    $boundary3Root = New-A4FixtureRoot -Name 'boundary-03'
    $a4Roots.Add($boundary3Root)
    Publish-A4Authority -Root $boundary3Root -SessionId $v2SessionId -CreationNonce $v2Nonce
    $boundaryFixtures.Add([pscustomobject]@{ Number = 3; Root = $boundary3Root; Sessions = @(
        [pscustomobject]@{ SessionId = $v2SessionId; Expected = 'final-authority-only-init-incomplete' }
    ) })

    $boundary4Root = New-A4FixtureRoot -Name 'boundary-04'
    $a4Roots.Add($boundary4Root)
    Publish-A4Authority -Root $boundary4Root -SessionId $v2SessionId -CreationNonce $v2Nonce
    Write-Utf8TestFile -Path (Join-Path $boundary4Root "fullstack-sessions/$v2SessionId.json") -Content '{"toolchainSnapshotComplete":true'
    $boundaryFixtures.Add([pscustomobject]@{ Number = 4; Root = $boundary4Root; Sessions = @(
        [pscustomobject]@{ SessionId = $v2SessionId; Expected = 'manifest-init-incomplete' }
    ) })

    $boundary5Root = New-A4FixtureRoot -Name 'boundary-05'
    $a4Roots.Add($boundary5Root)
    Publish-A4V2Session -Root $boundary5Root -SessionId $v2SessionId -CreationNonce $v2Nonce
    $boundaryFixtures.Add([pscustomobject]@{ Number = 5; Root = $boundary5Root; Sessions = @(
        [pscustomobject]@{ SessionId = $v2SessionId; Expected = 'published-unprobed' }
    ) })

    $probeIdentity = [pscustomobject]@{ pid = 4243; processStartTimeUtc = '2026-08-18T00:00:01.0000000Z'; role = 'dotnet-version-probe' }
    $boundary6Root = New-A4FixtureRoot -Name 'boundary-06'
    $a4Roots.Add($boundary6Root)
    Publish-A4V2Session -Root $boundary6Root -SessionId $v2SessionId -CreationNonce $v2Nonce -ToolchainProbeIdentities @($probeIdentity)
    $boundaryFixtures.Add([pscustomobject]@{ Number = 6; Root = $boundary6Root; Sessions = @(
        [pscustomobject]@{ SessionId = $v2SessionId; Expected = 'toolchain-probe-incomplete' }
    ) })

    $boundary7Root = New-A4FixtureRoot -Name 'boundary-07'
    $a4Roots.Add($boundary7Root)
    $snapshotOldSession = 'nerv-a400-000007'
    $snapshotNewSession = 'nerv-a400-000017'
    Publish-A4V2Session -Root $boundary7Root -SessionId $snapshotOldSession -CreationNonce $v2Nonce
    Publish-A4V2Session -Root $boundary7Root -SessionId $snapshotNewSession -CreationNonce $v2Nonce -ToolchainSnapshotComplete $true
    $boundaryFixtures.Add([pscustomobject]@{ Number = 7; Root = $boundary7Root; Sessions = @(
        [pscustomobject]@{ SessionId = $snapshotOldSession; Expected = 'published-unprobed' },
        [pscustomobject]@{ SessionId = $snapshotNewSession; Expected = 'published-unstarted' }
    ) })

    $boundary8Root = New-A4FixtureRoot -Name 'boundary-08'
    $a4Roots.Add($boundary8Root)
    Publish-A4V2Session -Root $boundary8Root -SessionId $v2SessionId -CreationNonce $v2Nonce -ToolchainSnapshotComplete $true
    $boundaryFixtures.Add([pscustomobject]@{ Number = 8; Root = $boundary8Root; Sessions = @(
        [pscustomobject]@{ SessionId = $v2SessionId; Expected = 'published-unstarted' }
    ) })

    $boundary9Root = New-A4FixtureRoot -Name 'boundary-09'
    $a4Roots.Add($boundary9Root)
    Publish-A4V2Session -Root $boundary9Root -SessionId $v2SessionId -CreationNonce $v2Nonce -ToolchainSnapshotComplete $true -RuntimeStartAttempted $true
    $boundaryFixtures.Add([pscustomobject]@{ Number = 9; Root = $boundary9Root; Sessions = @(
        [pscustomobject]@{ SessionId = $v2SessionId; Expected = 'published-starting-uncertain' }
    ) })

    $runtimeIdentity = [pscustomobject]@{ pid = 4244; processStartTimeUtc = '2026-08-18T00:00:02.0000000Z'; role = 'apphost' }
    $boundary10Root = New-A4FixtureRoot -Name 'boundary-10'
    $a4Roots.Add($boundary10Root)
    $runtimeOldSession = 'nerv-a400-000010'
    $runtimeNewSession = 'nerv-a400-000020'
    Publish-A4V2Session -Root $boundary10Root -SessionId $runtimeOldSession -CreationNonce $v2Nonce -ToolchainSnapshotComplete $true -RuntimeStartAttempted $true
    Publish-A4V2Session -Root $boundary10Root -SessionId $runtimeNewSession -CreationNonce $v2Nonce -ToolchainSnapshotComplete $true -RuntimeStartAttempted $true -RuntimeIdentities @($runtimeIdentity)
    $boundaryFixtures.Add([pscustomobject]@{ Number = 10; Root = $boundary10Root; Sessions = @(
        [pscustomobject]@{ SessionId = $runtimeOldSession; Expected = 'published-starting-uncertain' },
        [pscustomobject]@{ SessionId = $runtimeNewSession; Expected = $null }
    ) })

    Assert-True ($boundaryFixtures.Count -eq 10) 'Spec r2 section 5.3 must have ten independent OS temporary fixtures.'
    foreach ($fixture in $boundaryFixtures) {
        foreach ($session in $fixture.Sessions) {
            $boundary = Assert-A4ObservationReadOnly -Root $fixture.Root -Action {
                Get-NervFullStackPublicationBoundaryObservation -StateRoot $fixture.Root -SessionId $session.SessionId
            } -Message "Crash boundary $($fixture.Number) classification must not write, migrate, delete, cleanup, or start external resources."
            Assert-True ([string]::Equals([string] $boundary.Boundary, [string] $session.Expected, [StringComparison]::Ordinal)) "Crash boundary $($fixture.Number) must classify only the complete readback; expected '$($session.Expected)', actual '$($boundary.Boundary)'."
            Assert-True ($boundary.WriteCount -eq 0 -and $boundary.MigrationCount -eq 0 -and $boundary.DeleteCount -eq 0) "Crash boundary $($fixture.Number) must report zero filesystem side effects."
            Assert-True ($boundary.AspireCallCount -eq 0 -and $boundary.ProcessCallCount -eq 0 -and $boundary.DockerCallCount -eq 0) "Crash boundary $($fixture.Number) must report zero external calls."
        }
    }
}
finally {
    foreach ($root in $a4Roots) {
        if ([System.IO.Directory]::Exists($root)) {
            [System.IO.Directory]::Delete($root, $true)
        }
    }
}

Write-Host "Full-stack v2 protocol tests passed: $member"

# F1a frozen member: verified-session-cas-and-leases (A3 portion).
$member = 'verified-session-cas-and-leases'
Write-Host "Running $member (A3 portion)"
$a2Library = Join-Path $repoRoot 'scripts/lib/FullStackControlFileSystem.ps1'
. $a2Library
$a3Library = Join-Path $repoRoot 'scripts/lib/FullStackVerifiedRecordStore.ps1'
if (Test-Path -LiteralPath $a3Library -PathType Leaf) {
    . $a3Library
}

$expectedA3Commands = @(
    'New-NervFullStackVerifiedRecord',
    'Read-NervFullStackVerifiedRecord',
    'Update-NervFullStackVerifiedRecordCas',
    'Test-NervFullStackRecordSnapshotEqual'
)
foreach ($commandName in $expectedA3Commands) {
    Assert-True ($null -ne (Get-Command -Name $commandName -CommandType Function -ErrorAction SilentlyContinue)) "A3 interface '$commandName' is missing."
}

function Get-A3SnapshotText([object] $Snapshot) {
    return [System.Text.Encoding]::UTF8.GetString([byte[]] $Snapshot.RawBytes)
}

function Copy-A3Snapshot {
    param(
        [Parameter(Mandatory)]
        [object] $Snapshot,

        [byte[]] $RawBytes = $Snapshot.RawBytes,

        [object] $Record = $Snapshot.Record,

        [object] $Identity = $Snapshot.Identity
    )

    return [pscustomobject][ordered]@{
        Verified = $Snapshot.Verified
        StateRoot = $Snapshot.StateRoot
        CandidatePath = $Snapshot.CandidatePath
        CanonicalPath = $Snapshot.CanonicalPath
        RecordKind = $Snapshot.RecordKind
        RawBytes = [byte[]] $RawBytes.Clone()
        Record = $Record
        Identity = $Identity
    }
}

function Start-A3FixtureProcess([string] $Command, [string[]] $Arguments, [string] $FixtureRoot) {
    $argumentExpressions = [System.Collections.Generic.List[string]]::new()
    foreach ($argument in $Arguments) {
        $encodedArgument = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($argument))
        $argumentExpressions.Add("([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('$encodedArgument')))")
    }
    $wrappedCommand = "& {`n$Command`n} $($argumentExpressions -join ' ')"
    $encodedCommand = [Convert]::ToBase64String([System.Text.Encoding]::Unicode.GetBytes($wrappedCommand))
    $name = "fullstack-a3-fixture-$([Guid]::NewGuid().ToString('N'))"

    return Start-ManagedBackgroundProcess `
        -Command (Get-Process -Id $PID).Path `
        -Arguments @('-NoProfile', '-NonInteractive', '-EncodedCommand', $encodedCommand) `
        -WorkingDirectory $repoRoot `
        -Name $name `
        -LogDirectory (Join-Path $FixtureRoot "$name-logs")
}

$a3Root = Join-Path ([System.IO.Path]::GetTempPath()) "nerv-fullstack-a3-$([Guid]::NewGuid().ToString('N'))"
$a3StateRoot = Join-Path $a3Root 'state'
$a3SessionId = 'nerv-cafe-123456'
$a3CreationNonce = 'fedcba9876543210fedcba9876543210'
[void] [System.IO.Directory]::CreateDirectory($a3Root)

try {
    [void] (Initialize-NervFullStackTrustedStateRoot -StateRoot $a3StateRoot)
    $a3Paths = Get-NervFullStackControlPathSet `
        -StateRoot $a3StateRoot `
        -SessionId $a3SessionId `
        -CreationNonce $a3CreationNonce
    [void] [System.IO.Directory]::CreateDirectory($a3Paths.SessionDirectory)
    [void] [System.IO.Directory]::CreateDirectory($a3Paths.GuardianDirectory)

    $authorityTarget = Test-NervFullStackTrustedPathGraph `
        -StateRoot $a3StateRoot `
        -CandidatePath $a3Paths.AuthorityPath `
        -ExpectedKind File `
        -AllowMissingLeaf
    $authorityRecord = [pscustomobject][ordered]@{
        schemaVersion = 2
        kind = 'fullstack-session-authority'
        sessionId = $a3SessionId
        creationNonce = $a3CreationNonce
        displayName = '仓储会话-α'
    }
    $authoritySnapshot = New-NervFullStackVerifiedRecord `
        -VerifiedTarget $authorityTarget `
        -RecordKind 'fullstack-session-authority' `
        -Record $authorityRecord
    $expectedAuthorityJson = "{`"schemaVersion`":2,`"kind`":`"fullstack-session-authority`",`"sessionId`":`"$a3SessionId`",`"creationNonce`":`"$a3CreationNonce`",`"displayName`":`"仓储会话-α`"}"
    Assert-True ([string]::Equals((Get-A3SnapshotText $authoritySnapshot), $expectedAuthorityJson, [StringComparison]::Ordinal)) 'CreateNew must persist the exact UTF-8 bytes without a BOM.'
    Assert-True ([string]::Equals($authoritySnapshot.Record.displayName, '仓储会话-α', [StringComparison]::Ordinal)) 'UTF-8 fields must survive deserialization readback.'

    $authorityBytesBeforeDuplicate = [System.IO.File]::ReadAllBytes($a3Paths.AuthorityPath)
    Assert-ThrowsLike {
        New-NervFullStackVerifiedRecord `
            -VerifiedTarget $authorityTarget `
            -RecordKind 'fullstack-session-authority' `
            -Record ([pscustomobject][ordered]@{ schemaVersion = 2; kind = 'fullstack-session-authority'; sessionId = 'replacement' })
    } 'record:target-exists' 'A duplicate CreateNew must fail instead of overwriting authority.'
    Assert-True ([System.Linq.Enumerable]::SequenceEqual([byte[]] $authorityBytesBeforeDuplicate, [byte[]] [System.IO.File]::ReadAllBytes($a3Paths.AuthorityPath))) 'A rejected duplicate create must preserve authority bytes.'

    $authorityExistingTarget = Test-NervFullStackTrustedPathGraph `
        -StateRoot $a3StateRoot `
        -CandidatePath $a3Paths.AuthorityPath `
        -ExpectedKind File
    $authorityReadback = Read-NervFullStackVerifiedRecord `
        -VerifiedTarget $authorityExistingTarget `
        -RecordKind 'fullstack-session-authority'
    Assert-True (Test-NervFullStackRecordSnapshotEqual -Left $authoritySnapshot -Right $authorityReadback) 'CreateNew and independent readback snapshots must bind the same bytes, fields, and opened identity.'

    $fieldMismatchRecord = [pscustomobject][ordered]@{
        schemaVersion = 2
        kind = 'fullstack-session-authority'
        sessionId = 'nerv-dead-000000'
        creationNonce = $a3CreationNonce
        displayName = '仓储会话-α'
    }
    $fieldMismatch = Copy-A3Snapshot -Snapshot $authorityReadback -Record $fieldMismatchRecord
    Assert-True (-not (Test-NervFullStackRecordSnapshotEqual -Left $authorityReadback -Right $fieldMismatch)) 'Snapshot equality must reject changed deserialized fields even when raw bytes and identity match.'

    $byteMismatch = Copy-A3Snapshot -Snapshot $authorityReadback -RawBytes ([System.Text.Encoding]::UTF8.GetBytes(" $expectedAuthorityJson"))
    Assert-True (-not (Test-NervFullStackRecordSnapshotEqual -Left $authorityReadback -Right $byteMismatch)) 'Snapshot equality must reject changed raw bytes even when deserialized fields and identity match.'

    $identityMismatchValue = [pscustomobject][ordered]@{
        Provider = $authorityReadback.Identity.Provider
        Key = "$($authorityReadback.Identity.Key)-replacement"
        Device = $authorityReadback.Identity.Device
        Inode = $authorityReadback.Identity.Inode
        Kind = $authorityReadback.Identity.Kind
    }
    $identityMismatch = Copy-A3Snapshot -Snapshot $authorityReadback -Identity $identityMismatchValue
    Assert-True (-not (Test-NervFullStackRecordSnapshotEqual -Left $authorityReadback -Right $identityMismatch)) 'Snapshot equality must reject a different opened-object identity even when bytes and fields match.'

    $missingRequiredFields = [pscustomobject][ordered]@{ schemaVersion = 2 }
    $invalidLeft = Copy-A3Snapshot -Snapshot $authorityReadback -Record $missingRequiredFields
    $invalidRight = Copy-A3Snapshot -Snapshot $authorityReadback -Record $missingRequiredFields
    Assert-True (-not (Test-NervFullStackRecordSnapshotEqual -Left $invalidLeft -Right $invalidRight)) 'Snapshot equality must fail closed when both snapshots omit required fields.'

    $corruptPath = Join-Path $a3Paths.GuardianDirectory 'corrupt.json'
    Write-Utf8TestFile -Path $corruptPath -Content '{"kind":"request"'
    $corruptTarget = Test-NervFullStackTrustedPathGraph -StateRoot $a3StateRoot -CandidatePath $corruptPath -ExpectedKind File
    Assert-ThrowsLike {
        Read-NervFullStackVerifiedRecord -VerifiedTarget $corruptTarget -RecordKind 'request'
    } 'record:invalid-json' 'Damaged JSON must fail closed after raw-byte readback.'

    $wrongKindPath = Join-Path $a3Paths.GuardianDirectory 'wrong-kind.json'
    Write-Utf8TestFile -Path $wrongKindPath -Content '{"schemaVersion":2,"kind":"ack"}'
    $wrongKindTarget = Test-NervFullStackTrustedPathGraph -StateRoot $a3StateRoot -CandidatePath $wrongKindPath -ExpectedKind File
    Assert-ThrowsLike {
        Read-NervFullStackVerifiedRecord -VerifiedTarget $wrongKindTarget -RecordKind 'request'
    } 'record:field-mismatch' 'A deserialized kind that differs from RecordKind must fail closed.'

    $readbackTamperPath = Join-Path $a3Paths.GuardianDirectory 'create-readback.json'
    $readbackTamperTarget = Test-NervFullStackTrustedPathGraph -StateRoot $a3StateRoot -CandidatePath $readbackTamperPath -ExpectedKind File -AllowMissingLeaf
    $script:NervFullStackVerifiedRecordStoreCrashAction = {
        param($Boundary, $Context)
        if ([string]::Equals($Boundary, 'after-create-flush-before-readback', [StringComparison]::Ordinal)) {
            Write-Utf8TestFile -Path $Context.Path -Content '{"schemaVersion":2,"kind":"request","tampered":true}'
        }
    }
    try {
        Assert-ThrowsLike {
            New-NervFullStackVerifiedRecord `
                -VerifiedTarget $readbackTamperTarget `
                -RecordKind 'request' `
                -Record ([pscustomobject][ordered]@{ schemaVersion = 2; kind = 'request'; value = 'expected' })
        } 'record:readback-mismatch' 'CreateNew must compare durable readback bytes and fields instead of trusting the write call.'
    }
    finally {
        Remove-Variable -Name NervFullStackVerifiedRecordStoreCrashAction -Scope Script -ErrorAction SilentlyContinue
    }

    $authorityProof = Open-NervFullStackVerifiedPathHandle -TrustedPath $authorityExistingTarget -Access Read
    try {
        $a3VerifiedSession = New-NervFullStackVerifiedSessionCapability -PathSet $a3Paths -AuthorityProof $authorityProof
    }
    finally {
        if ($null -ne $authorityProof.Handle) { $authorityProof.Handle.Dispose() }
    }

    Assert-ThrowsLike {
        Update-NervFullStackVerifiedRecordCas `
            -VerifiedSession $a3VerifiedSession `
            -ExpectedSnapshot $authorityReadback `
            -NextRecord $authorityRecord
    } 'record:authority-immutable' 'CAS must never replace authority.json.'

    $requestTarget = Test-NervFullStackTrustedPathGraph `
        -StateRoot $a3StateRoot `
        -CandidatePath $a3Paths.GuardianRequestPath `
        -ExpectedKind File `
        -AllowMissingLeaf
    $requestRecordV1 = [pscustomobject][ordered]@{ schemaVersion = 2; kind = 'request'; sessionId = $a3SessionId; attempt = 1; writer = 'initial' }
    $requestSnapshotV1 = New-NervFullStackVerifiedRecord -VerifiedTarget $requestTarget -RecordKind 'request' -Record $requestRecordV1
    $requestRecordV2 = [pscustomobject][ordered]@{ schemaVersion = 2; kind = 'request'; sessionId = $a3SessionId; attempt = 2; writer = 'winner' }
    $requestSnapshotV2 = Update-NervFullStackVerifiedRecordCas `
        -VerifiedSession $a3VerifiedSession `
        -ExpectedSnapshot $requestSnapshotV1 `
        -NextRecord $requestRecordV2
    Assert-True ([string]::Equals($requestSnapshotV2.Record.writer, 'winner', [StringComparison]::Ordinal)) 'A successful CAS must return the final readback snapshot.'

    $bytesAfterWinningCas = [System.IO.File]::ReadAllBytes($a3Paths.GuardianRequestPath)
    Assert-ThrowsLike {
        Update-NervFullStackVerifiedRecordCas `
            -VerifiedSession $a3VerifiedSession `
            -ExpectedSnapshot $requestSnapshotV1 `
            -NextRecord ([pscustomobject][ordered]@{ schemaVersion = 2; kind = 'request'; sessionId = $a3SessionId; attempt = 3; writer = 'stale' })
    } 'record:cas-conflict' 'A stale CAS must fail before replace.'
    Assert-True ([System.Linq.Enumerable]::SequenceEqual([byte[]] $bytesAfterWinningCas, [byte[]] [System.IO.File]::ReadAllBytes($a3Paths.GuardianRequestPath))) 'A stale CAS loser must not change final bytes.'

    $sameFieldsSnapshot = Read-NervFullStackVerifiedRecord `
        -VerifiedTarget (Test-NervFullStackTrustedPathGraph -StateRoot $a3StateRoot -CandidatePath $a3Paths.GuardianRequestPath -ExpectedKind File) `
        -RecordKind 'request'
    $sameFieldsDifferentBytes = "{ `"schemaVersion`": 2, `"kind`": `"request`", `"sessionId`": `"$a3SessionId`", `"attempt`": 2, `"writer`": `"winner`" }"
    Write-Utf8TestFile -Path $a3Paths.GuardianRequestPath -Content $sameFieldsDifferentBytes
    Assert-ThrowsLike {
        Update-NervFullStackVerifiedRecordCas `
            -VerifiedSession $a3VerifiedSession `
            -ExpectedSnapshot $sameFieldsSnapshot `
            -NextRecord ([pscustomobject][ordered]@{ schemaVersion = 2; kind = 'request'; sessionId = $a3SessionId; attempt = 3; writer = 'must-not-replace' })
    } 'record:cas-conflict' 'CAS must compare original bytes, not only deserialized fields.'
    Assert-True ([string]::Equals([System.IO.File]::ReadAllText($a3Paths.GuardianRequestPath), $sameFieldsDifferentBytes, [StringComparison]::Ordinal)) 'A raw-byte CAS conflict must preserve the conflicting bytes.'

    $identitySnapshot = Read-NervFullStackVerifiedRecord `
        -VerifiedTarget (Test-NervFullStackTrustedPathGraph -StateRoot $a3StateRoot -CandidatePath $a3Paths.GuardianRequestPath -ExpectedKind File) `
        -RecordKind 'request'
    $identityReplacement = Join-Path $a3Paths.GuardianDirectory 'identity-replacement.json'
    [System.IO.File]::WriteAllBytes($identityReplacement, [byte[]] $identitySnapshot.RawBytes)
    [System.IO.File]::Move($identityReplacement, $a3Paths.GuardianRequestPath, $true)
    Assert-ThrowsLike {
        Update-NervFullStackVerifiedRecordCas `
            -VerifiedSession $a3VerifiedSession `
            -ExpectedSnapshot $identitySnapshot `
            -NextRecord ([pscustomobject][ordered]@{ schemaVersion = 2; kind = 'request'; sessionId = $a3SessionId; attempt = 3; writer = 'must-not-replace' })
    } 'record:cas-conflict' 'CAS must compare opened-object identity even when bytes and fields are unchanged.'

    $crashSnapshot = Read-NervFullStackVerifiedRecord `
        -VerifiedTarget (Test-NervFullStackTrustedPathGraph -StateRoot $a3StateRoot -CandidatePath $a3Paths.GuardianRequestPath -ExpectedKind File) `
        -RecordKind 'request'
    $bytesBeforePreReplaceCrash = [System.IO.File]::ReadAllBytes($a3Paths.GuardianRequestPath)
    $script:NervFullStackVerifiedRecordStoreCrashAction = {
        param($Boundary, $Context)
        if ([string]::Equals($Boundary, 'after-temp-readback-before-replace', [StringComparison]::Ordinal)) {
            throw 'test-only:before-replace-crash'
        }
    }
    try {
        Assert-ThrowsLike {
            Update-NervFullStackVerifiedRecordCas `
                -VerifiedSession $a3VerifiedSession `
                -ExpectedSnapshot $crashSnapshot `
                -NextRecord ([pscustomobject][ordered]@{ schemaVersion = 2; kind = 'request'; sessionId = $a3SessionId; attempt = 4; writer = 'pre-replace-crash' })
        } 'test-only:before-replace-crash' 'A crash before atomic replace must surface without reporting success.'
    }
    finally {
        Remove-Variable -Name NervFullStackVerifiedRecordStoreCrashAction -Scope Script -ErrorAction SilentlyContinue
    }
    Assert-True ([System.Linq.Enumerable]::SequenceEqual([byte[]] $bytesBeforePreReplaceCrash, [byte[]] [System.IO.File]::ReadAllBytes($a3Paths.GuardianRequestPath))) 'A pre-replace crash must preserve the old complete bytes.'

    $postReplaceSnapshot = Read-NervFullStackVerifiedRecord `
        -VerifiedTarget (Test-NervFullStackTrustedPathGraph -StateRoot $a3StateRoot -CandidatePath $a3Paths.GuardianRequestPath -ExpectedKind File) `
        -RecordKind 'request'
    $postReplaceRecord = [pscustomobject][ordered]@{ schemaVersion = 2; kind = 'request'; sessionId = $a3SessionId; attempt = 5; writer = 'post-replace-crash' }
    $script:NervFullStackVerifiedRecordStoreCrashAction = {
        param($Boundary, $Context)
        if ([string]::Equals($Boundary, 'after-replace-before-final-readback', [StringComparison]::Ordinal)) {
            throw 'test-only:after-replace-crash'
        }
    }
    try {
        Assert-ThrowsLike {
            Update-NervFullStackVerifiedRecordCas `
                -VerifiedSession $a3VerifiedSession `
                -ExpectedSnapshot $postReplaceSnapshot `
                -NextRecord $postReplaceRecord
        } 'test-only:after-replace-crash' 'A crash after atomic replace must surface without fabricating a half-committed snapshot.'
    }
    finally {
        Remove-Variable -Name NervFullStackVerifiedRecordStoreCrashAction -Scope Script -ErrorAction SilentlyContinue
    }
    $postCrashReadback = Read-NervFullStackVerifiedRecord `
        -VerifiedTarget (Test-NervFullStackTrustedPathGraph -StateRoot $a3StateRoot -CandidatePath $a3Paths.GuardianRequestPath -ExpectedKind File) `
        -RecordKind 'request'
    Assert-True ([string]::Equals($postCrashReadback.Record.writer, 'post-replace-crash', [StringComparison]::Ordinal)) 'A post-replace crash must expose the complete new record on the next read.'

    $finalReadbackSnapshot = $postCrashReadback
    $script:NervFullStackVerifiedRecordStoreCrashAction = {
        param($Boundary, $Context)
        if ([string]::Equals($Boundary, 'after-replace-before-final-readback', [StringComparison]::Ordinal)) {
            Write-Utf8TestFile -Path $Context.Path -Content '{"schemaVersion":2,"kind":"request","tampered":true}'
        }
    }
    try {
        Assert-ThrowsLike {
            Update-NervFullStackVerifiedRecordCas `
                -VerifiedSession $a3VerifiedSession `
                -ExpectedSnapshot $finalReadbackSnapshot `
                -NextRecord ([pscustomobject][ordered]@{ schemaVersion = 2; kind = 'request'; sessionId = $a3SessionId; attempt = 6; writer = 'must-read-final' })
        } 'record:readback-mismatch' 'CAS must verify final readback instead of trusting atomic replace.'
    }
    finally {
        Remove-Variable -Name NervFullStackVerifiedRecordStoreCrashAction -Scope Script -ErrorAction SilentlyContinue
    }

    $concurrentStartRecord = [pscustomobject][ordered]@{ schemaVersion = 2; kind = 'request'; sessionId = $a3SessionId; attempt = 10; writer = 'concurrent-start' }
    $currentConcurrentTarget = Test-NervFullStackTrustedPathGraph -StateRoot $a3StateRoot -CandidatePath $a3Paths.GuardianRequestPath -ExpectedKind File
    $currentConcurrentSnapshot = Read-NervFullStackVerifiedRecord -VerifiedTarget $currentConcurrentTarget -RecordKind 'request'
    $currentConcurrentSnapshot = Update-NervFullStackVerifiedRecordCas `
        -VerifiedSession $a3VerifiedSession `
        -ExpectedSnapshot $currentConcurrentSnapshot `
        -NextRecord $concurrentStartRecord

    $concurrentCommand = @'
param($FileSystemLibrary, $RecordLibrary, $Root, $SessionId, $CreationNonce, $Writer, $Ready, $Go, $BarrierRoot, $Result)
$ErrorActionPreference = 'Stop'
. $FileSystemLibrary
. $RecordLibrary
$paths = Get-NervFullStackControlPathSet -StateRoot $Root -SessionId $SessionId -CreationNonce $CreationNonce
$authorityTarget = Test-NervFullStackTrustedPathGraph -StateRoot $Root -CandidatePath $paths.AuthorityPath -ExpectedKind File
$authorityProof = Open-NervFullStackVerifiedPathHandle -TrustedPath $authorityTarget -Access Read
try {
    $session = New-NervFullStackVerifiedSessionCapability -PathSet $paths -AuthorityProof $authorityProof
}
finally {
    if ($null -ne $authorityProof.Handle) { $authorityProof.Handle.Dispose() }
}
$target = Test-NervFullStackTrustedPathGraph -StateRoot $Root -CandidatePath $paths.GuardianRequestPath -ExpectedKind File
$snapshot = Read-NervFullStackVerifiedRecord -VerifiedTarget $target -RecordKind 'request'
[System.IO.File]::WriteAllText($Ready, 'ready')
$deadline = [DateTimeOffset]::UtcNow.AddSeconds(10)
while (-not [System.IO.File]::Exists($Go) -and [DateTimeOffset]::UtcNow -lt $deadline) {
    [System.Threading.Thread]::Sleep(10)
}
$outcome = [ordered]@{ writer = $Writer; succeeded = $false; error = $null }
try {
    $script:NervFullStackVerifiedRecordStoreCrashAction = {
        param($Boundary, $Context)
        if ([string]::Equals($Boundary, 'after-cas-recheck-before-replace', [StringComparison]::Ordinal)) {
            [System.IO.File]::WriteAllText("$BarrierRoot-$Writer.ready", 'ready')
            $barrierDeadline = [DateTimeOffset]::UtcNow.AddSeconds(2)
            while ([System.IO.Directory]::GetFiles((Split-Path -Parent $BarrierRoot), "$(Split-Path -Leaf $BarrierRoot)-*.ready").Count -lt 2 -and [DateTimeOffset]::UtcNow -lt $barrierDeadline) {
                [System.Threading.Thread]::Sleep(10)
            }
        }
    }
    [void] (Update-NervFullStackVerifiedRecordCas `
        -VerifiedSession $session `
        -ExpectedSnapshot $snapshot `
        -NextRecord ([pscustomobject][ordered]@{ schemaVersion = 2; kind = 'request'; sessionId = $SessionId; attempt = 11; writer = $Writer }))
    $outcome.succeeded = $true
}
catch {
    $outcome.error = $_.Exception.Message
}
[System.IO.File]::WriteAllText($Result, ($outcome | ConvertTo-Json -Compress))
'@
    $concurrentGo = Join-Path $a3Root 'concurrent.go'
    $concurrentReadyA = Join-Path $a3Root 'concurrent-a.ready'
    $concurrentReadyB = Join-Path $a3Root 'concurrent-b.ready'
    $concurrentBarrier = Join-Path $a3Root 'concurrent-cas-barrier'
    $concurrentResultA = Join-Path $a3Root 'concurrent-a.json'
    $concurrentResultB = Join-Path $a3Root 'concurrent-b.json'
    $concurrentA = Start-A3FixtureProcess -Command $concurrentCommand -Arguments @($a2Library, $a3Library, $a3StateRoot, $a3SessionId, $a3CreationNonce, 'writer-a', $concurrentReadyA, $concurrentGo, $concurrentBarrier, $concurrentResultA) -FixtureRoot $a3Root
    $concurrentB = Start-A3FixtureProcess -Command $concurrentCommand -Arguments @($a2Library, $a3Library, $a3StateRoot, $a3SessionId, $a3CreationNonce, 'writer-b', $concurrentReadyB, $concurrentGo, $concurrentBarrier, $concurrentResultB) -FixtureRoot $a3Root
    try {
        Wait-A2FixtureReady -Path $concurrentReadyA -ManagedProcess $concurrentA -Name 'A3 concurrent writer A'
        Wait-A2FixtureReady -Path $concurrentReadyB -ManagedProcess $concurrentB -Name 'A3 concurrent writer B'
        Write-Utf8TestFile -Path $concurrentGo -Content 'go'
        Assert-True $concurrentA.Process.WaitForExit(15000) 'Concurrent writer A must finish in bounded time.'
        Assert-True $concurrentB.Process.WaitForExit(15000) 'Concurrent writer B must finish in bounded time.'
        Assert-True ($concurrentA.Process.ExitCode -eq 0 -and $concurrentB.Process.ExitCode -eq 0) 'Concurrent writer fixtures must report their outcomes successfully.'
    }
    finally {
        $concurrentA.Stop.Invoke('A3 concurrent writer A cleanup')
        $concurrentB.Stop.Invoke('A3 concurrent writer B cleanup')
    }

    $concurrentOutcomes = @(
        ([System.IO.File]::ReadAllText($concurrentResultA) | ConvertFrom-Json)
        ([System.IO.File]::ReadAllText($concurrentResultB) | ConvertFrom-Json)
    )
    $successfulOutcomes = @($concurrentOutcomes | Where-Object { $_.succeeded })
    $failedOutcomes = @($concurrentOutcomes | Where-Object { -not $_.succeeded })
    Assert-True ($successfulOutcomes.Count -eq 1 -and $failedOutcomes.Count -eq 1) 'Two real concurrent CAS writers must produce exactly one success and one failure.'
    Assert-True ($failedOutcomes[0].error.Contains('record:cas-conflict', [StringComparison]::Ordinal) -or $failedOutcomes[0].error.Contains('lease:unavailable', [StringComparison]::Ordinal)) 'The concurrent loser must fail at the lease or CAS boundary.'
    $concurrentFinal = Read-NervFullStackVerifiedRecord `
        -VerifiedTarget (Test-NervFullStackTrustedPathGraph -StateRoot $a3StateRoot -CandidatePath $a3Paths.GuardianRequestPath -ExpectedKind File) `
        -RecordKind 'request'
    Assert-True ([string]::Equals($concurrentFinal.Record.writer, $successfulOutcomes[0].writer, [StringComparison]::Ordinal)) 'The final bytes must belong to the sole successful concurrent writer.'
}
finally {
    Remove-Variable -Name NervFullStackVerifiedRecordStoreCrashAction -Scope Script -ErrorAction SilentlyContinue
    if ([System.IO.Directory]::Exists($a3Root)) {
        [System.IO.Directory]::Delete($a3Root, $true)
    }
}

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
