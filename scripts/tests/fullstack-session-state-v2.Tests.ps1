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

# Governance/PublicContract: this read-only classifier may call only the frozen
# set of parsing, trusted-read, and protocol-observation commands below.  AST
# command nodes are used so call operators, native executables, Start-Process,
# Aspire/Docker CLIs, or a new destructive helper cannot hide behind spelling.
$classifierTokens = $null
$classifierParseErrors = $null
$classifierAst = [System.Management.Automation.Language.Parser]::ParseFile(
    $a4Library,
    [ref] $classifierTokens,
    [ref] $classifierParseErrors
)
Assert-True ($classifierParseErrors.Count -eq 0) 'The A4 classifier must parse before its external-call governance contract is evaluated.'
$allowedClassifierCommands = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($commandName in @(
    'Assert-NervFullStackProtocolValue',
    'ConvertFrom-Json',
    'Get-NervFullStackClassifierArrayCount',
    'Get-NervFullStackClassifierProperty',
    'Get-NervFullStackClassifierSessionReadback',
    'Get-NervFullStackControlPathSet',
    'Get-NervFullStackNormalizedFullPath',
    'Get-NervFullStackPathComparison',
    'Join-Path',
    'Open-NervFullStackVerifiedPathHandle',
    'Read-NervFullStackClassifierJsonRecord',
    'Read-NervFullStackOpenedRecordBytes',
    'Read-NervFullStackVerifiedRecord',
    'Set-StrictMode',
    'Test-NervFullStackClassifierActivationMarker',
    'Test-NervFullStackClassifierExactString',
    'Test-NervFullStackClassifierIdentityArray',
    'Test-NervFullStackClassifierIdentityRecord',
    'Test-NervFullStackClassifierIntegerValue',
    'Test-NervFullStackClassifierPattern',
    'Test-NervFullStackClassifierPositiveInteger',
    'Test-NervFullStackClassifierTimestamp',
    'Test-NervFullStackClassifierV2Readback',
    'Test-NervFullStackProtocolValue',
    'Test-NervFullStackTrustedPathGraph'
)) {
    [void] $allowedClassifierCommands.Add($commandName)
}
$unapprovedClassifierInvocations = [System.Collections.Generic.List[string]]::new()
$classifierCommandAsts = @($classifierAst.FindAll({
    param($node)
    return $node -is [System.Management.Automation.Language.CommandAst]
}, $true))
foreach ($commandAst in $classifierCommandAsts) {
    if ($commandAst.InvocationOperator -eq [System.Management.Automation.Language.TokenKind]::Dot) {
        continue
    }

    $commandName = $commandAst.GetCommandName()
    if ($commandAst.InvocationOperator -ne [System.Management.Automation.Language.TokenKind]::Unknown -or
        [string]::IsNullOrWhiteSpace($commandName) -or
        -not $allowedClassifierCommands.Contains($commandName)) {
        $unapprovedClassifierInvocations.Add($commandAst.Extent.Text)
    }
}
Assert-True ($unapprovedClassifierInvocations.Count -eq 0) "The A4 classifier must not invoke external commands, Start-Process, Aspire, Docker, or destructive process helpers. Observed: $($unapprovedClassifierInvocations -join ' | ')"

# Governance/PublicContract: CommandAst does not include .NET member calls.  Freeze
# the classifier's necessary pure/read-only members plus collection bookkeeping and
# verified-handle disposal so process launch and destructive static members fail
# closed.  This proves the checked-in classifier has no unapproved member-call
# entry point; it is not evidence from a real Aspire/AppHost/process/Docker run.
$allowedClassifierMemberInvocations = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($memberInvocation in @(
    'Static|[DateTimeOffset]|TryParseExact',
    'Static|[regex]|IsMatch',
    'Static|[string]|Equals',
    'Static|[string]|IsNullOrWhiteSpace',
    'Static|[System.Collections.Generic.List[string]]|new',
    'Static|[System.IO.Directory]|EnumerateDirectories',
    'Static|[System.IO.Directory]|Exists',
    'Static|[System.IO.File]|Exists',
    'Static|[System.IO.File]|GetAttributes',
    'Static|[System.IO.Path]|GetFileName',
    'Static|[System.Text.UTF8Encoding]|new',
    'Instance|$name|StartsWith',
    'Instance|$name|Substring',
    'Instance|$proof.Handle|Dispose',
    'Instance|$PSBoundParameters|ContainsKey',
    'Instance|$rawBytes|Clone',
    'Instance|$requiredExactStrings|GetEnumerator',
    'Instance|$tempDirectories|Add',
    'Instance|$tempDirectories|ToArray',
    'Instance|$warnings|Add',
    'Instance|$warnings|ToArray',
    'Instance|[System.Text.UTF8Encoding]::new($false, $true)|GetString'
)) {
    [void] $allowedClassifierMemberInvocations.Add($memberInvocation)
}
$unapprovedClassifierMemberInvocations = [System.Collections.Generic.List[string]]::new()
$classifierMemberInvocationAsts = @($classifierAst.FindAll({
    param($node)
    return $node -is [System.Management.Automation.Language.InvokeMemberExpressionAst]
}, $true))
foreach ($memberInvocationAst in $classifierMemberInvocationAsts) {
    $invocationKind = if ($memberInvocationAst.Static) { 'Static' } else { 'Instance' }
    $invocationIdentity = '{0}|{1}|{2}' -f @(
        $invocationKind,
        $memberInvocationAst.Expression.Extent.Text,
        $memberInvocationAst.Member.Extent.Text
    )
    if (-not $allowedClassifierMemberInvocations.Contains($invocationIdentity)) {
        $unapprovedClassifierMemberInvocations.Add($memberInvocationAst.Extent.Text)
    }
}
Assert-True ($unapprovedClassifierMemberInvocations.Count -eq 0) "The A4 classifier must not invoke unapproved .NET members, including process launch or destructive filesystem members. Observed: $($unapprovedClassifierMemberInvocations -join ' | ')"

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
    $gateOff = Assert-A4ObservationReadOnly -Root $v0StoppedRoot -Action {
        Get-NervFullStackProtocolActivationObservation -StateRoot $v0StoppedRoot
    } -Message 'Reading an absent activation marker must not create marker residue.'
    Assert-True ([string]::Equals($gateOff.Activation, 'GateOff', [StringComparison]::Ordinal)) 'An absent marker must mean GateOff.'
    $v0StoppedDisposition = Get-NervFullStackCompatibilityDisposition -GenerationObservation $v0Stopped -ActivationObservation $gateOff
    Assert-True ([string]::Equals($v0StoppedDisposition.Disposition, 'ReadOnlyLegacyStopped', [StringComparison]::Ordinal)) 'Stopped v0 must be read-only idempotent.'

    $v0ActiveRoot = New-A4FixtureRoot -Name 'v0-active'
    $a4Roots.Add($v0ActiveRoot)
    Write-A4Record -Path (Join-Path $v0ActiveRoot "fullstack-sessions/$v0SessionId.json") -Record ([ordered]@{
        schemaVersion = 1; sessionId = $v0SessionId; state = 'Running'; worktreeRoot = $v0ActiveRoot
    })
    Write-A4ActivationMarker -Root $v0ActiveRoot -Valid $true
    $v0Active = Assert-A4ObservationReadOnly -Root $v0ActiveRoot -Action {
        Get-NervFullStackProtocolGenerationObservation -StateRoot $v0ActiveRoot -SessionId $v0SessionId
    } -Message 'Reading an active v0 fixture must not write, migrate, or delete files.'
    $activeMarker = Assert-A4ObservationReadOnly -Root $v0ActiveRoot -Action {
        Get-NervFullStackProtocolActivationObservation -StateRoot $v0ActiveRoot
    } -Message 'Reading a valid marker beside active v0 state must not change either record.'
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
    $v2 = Assert-A4ObservationReadOnly -Root $validV2Root -Action {
        Get-NervFullStackProtocolGenerationObservation -StateRoot $validV2Root -SessionId $v2SessionId
    } -Message 'Reading a legal v2 fixture must not change authority or manifest records.'
    Assert-True ([string]::Equals($v2.Generation, 'v2', [StringComparison]::Ordinal)) 'Matching authority and manifest must be classified as v2 independently of activation.'
    $v2GateOff = Assert-A4ObservationReadOnly -Root $validV2Root -Action {
        Get-NervFullStackProtocolActivationObservation -StateRoot $validV2Root
    } -Message 'Reading an absent marker beside legal v2 state must not create marker residue.'
    $v2Disposition = Get-NervFullStackCompatibilityDisposition -GenerationObservation $v2 -ActivationObservation $v2GateOff
    Assert-True ([string]::Equals($v2Disposition.Disposition, 'v2', [StringComparison]::Ordinal)) 'A legal v2 generation remains v2 while activation is reported separately.'
    Assert-True ([string]::Equals($v2Disposition.Activation, 'GateOff', [StringComparison]::Ordinal)) 'Compatibility must preserve GateOff rather than infer activation from v2 generation.'

    $invalidMarkerRoot = New-A4FixtureRoot -Name 'invalid-marker'
    $a4Roots.Add($invalidMarkerRoot)
    Write-A4ActivationMarker -Root $invalidMarkerRoot -Valid $false
    $invalidMarker = Assert-A4ObservationReadOnly -Root $invalidMarkerRoot -Action {
        Get-NervFullStackProtocolActivationObservation -StateRoot $invalidMarkerRoot
    } -Message 'Reading an invalid marker must not rewrite or delete it.'
    Assert-True ([string]::Equals($invalidMarker.Activation, 'InvalidMarker', [StringComparison]::Ordinal)) 'A field-mismatched marker must fail closed as InvalidMarker.'

    $markerDirectoryRoot = New-A4FixtureRoot -Name 'marker-directory'
    $a4Roots.Add($markerDirectoryRoot)
    [void] [System.IO.Directory]::CreateDirectory((Join-Path $markerDirectoryRoot 'fullstack-sessions/.protocol-mode.json'))
    $markerDirectory = Assert-A4ObservationReadOnly -Root $markerDirectoryRoot -Action {
        Get-NervFullStackProtocolActivationObservation -StateRoot $markerDirectoryRoot
    } -Message 'Reading a directory-shaped marker residue must not rewrite or delete it.'
    Assert-True ([string]::Equals($markerDirectory.Activation, 'InvalidMarker', [StringComparison]::Ordinal)) 'A directory occupying the marker path must fail closed as InvalidMarker rather than GateOff.'
    Assert-True ($markerDirectory.Warnings.Count -gt 0) 'A directory-shaped marker residue must produce a visible warning.'

    $markerLinkRoot = New-A4FixtureRoot -Name 'marker-link'
    $a4Roots.Add($markerLinkRoot)
    Write-A4ActivationMarker -Root $markerLinkRoot -Valid $true
    $markerLinkPath = Join-Path $markerLinkRoot 'fullstack-sessions/.protocol-mode.json'
    $markerLinkTarget = Join-Path $markerLinkRoot 'fullstack-sessions/protocol-mode-target.json'
    [System.IO.File]::Move($markerLinkPath, $markerLinkTarget)
    [void] [System.IO.File]::CreateSymbolicLink($markerLinkPath, $markerLinkTarget)
    $markerLink = Assert-A4ObservationReadOnly -Root $markerLinkRoot -Action {
        Get-NervFullStackProtocolActivationObservation -StateRoot $markerLinkRoot
    } -Message 'Reading a linked marker residue must not follow, rewrite, or delete it.'
    Assert-True ([string]::Equals($markerLink.Activation, 'InvalidMarker', [StringComparison]::Ordinal)) 'A symlink/reparse marker must remain fail closed as InvalidMarker.'
    Assert-True ($markerLink.Warnings.Count -gt 0) 'A symlink/reparse marker must produce a visible warning.'

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
    Publish-A4V2Session -Root $boundary7Root -SessionId $snapshotOldSession -CreationNonce $v2Nonce -ToolchainProbeIdentities @($probeIdentity)
    Publish-A4V2Session -Root $boundary7Root -SessionId $snapshotNewSession -CreationNonce $v2Nonce -ToolchainSnapshotComplete $true
    $boundaryFixtures.Add([pscustomobject]@{ Number = 7; Root = $boundary7Root; Sessions = @(
        [pscustomobject]@{ SessionId = $snapshotOldSession; Expected = 'toolchain-probe-incomplete' },
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

# F1a frozen member: authority-publication-and-residue.
$member = 'authority-publication-and-residue'
Write-Host "Running $member"

$a5Library = Join-Path $repoRoot 'scripts/lib/FullStackAuthorityPublication.ps1'
if (Test-Path -LiteralPath $a5Library -PathType Leaf) {
    . $a5Library
}

$expectedA5Commands = @(
    'Publish-NervFullStackInitialV2Session',
    'Register-NervFullStackToolchainProbeIdentity',
    'Complete-NervFullStackToolchainSnapshot',
    'Test-NervFullStackRuntimeStartAllowed'
)
foreach ($commandName in $expectedA5Commands) {
    Assert-True ($null -ne (Get-Command -Name $commandName -CommandType Function -ErrorAction SilentlyContinue)) "A5 interface '$commandName' is missing."
}

$a5Tokens = $null
$a5ParseErrors = $null
$a5Ast = [System.Management.Automation.Language.Parser]::ParseFile($a5Library, [ref] $a5Tokens, [ref] $a5ParseErrors)
Assert-True ($a5ParseErrors.Count -eq 0) 'The A5 library must parse without errors.'
$a5ForbiddenCommands = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($commandName in @(
    'Start-Process', 'Stop-ProcessTree', 'Invoke-Aspire', 'Invoke-DockerCompose',
    'Invoke-NativeCommandWithTimeout', 'dotnet', 'node', 'aspire', 'dcp', 'docker'
)) {
    [void] $a5ForbiddenCommands.Add($commandName)
}
$a5ForbiddenCommandHits = [System.Collections.Generic.List[string]]::new()
foreach ($commandAst in @($a5Ast.FindAll({
            param($node)
            return $node -is [System.Management.Automation.Language.CommandAst]
        }, $true))) {
    $invokedName = $commandAst.GetCommandName()
    if ($null -ne $invokedName -and $a5ForbiddenCommands.Contains($invokedName)) {
        $a5ForbiddenCommandHits.Add($commandAst.Extent.Text)
    }
}
Assert-True ($a5ForbiddenCommandHits.Count -eq 0) "A5 primitives must not start or stop Node/dotnet/Aspire/DCP/Docker/runtime processes. Observed: $($a5ForbiddenCommandHits -join ' | ')"

function New-A5FixtureRoot([string] $Name) {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) "nerv-fullstack-a5-$Name-$([Guid]::NewGuid().ToString('N'))"
    [void] [System.IO.Directory]::CreateDirectory($root)
    return $root
}

function New-A5CreatedByIdentity {
    return [pscustomobject][ordered]@{
        pid = 5152
        processStartTimeUtc = '2026-08-22T00:00:00.0000000Z'
    }
}

function New-A5ProbeIdentity([int] $ProcessId = 5153, [string] $Role = 'toolchain-version-probe') {
    return [pscustomobject][ordered]@{
        pid = $ProcessId
        processStartTimeUtc = '2026-08-22T00:00:01.0000000Z'
        role = $Role
    }
}

function Get-A5RecordProperty([object] $Record, [string] $Name) {
    if ($null -eq $Record) {
        return $null
    }

    return $Record.PSObject.Properties[$Name]
}

function Assert-A5PublicationPair([object] $Publication, [string] $Root, [string] $SessionId) {
    Assert-True $Publication.Verified 'A5 publication must return a verified session capability.'
    Assert-True $Publication.PublicationComplete 'A5 publication must return only after pair revalidation completes.'
    Assert-True ([string]::Equals($Publication.StateRoot, (Get-NervFullStackNormalizedFullPath -Path $Root), (Get-NervFullStackPathComparison))) 'Publication must bind canonical StateRoot.'
    Assert-True ([string]::Equals($Publication.SessionId, $SessionId, [StringComparison]::Ordinal)) 'Publication must bind the exact session ID.'
    Assert-True ([regex]::IsMatch($Publication.CreationNonce, '^[a-f0-9]{32}$', [Text.RegularExpressions.RegexOptions]::CultureInvariant)) 'Publication must create a lowercase 128-bit creation nonce.'
    Assert-True ([string]::Equals($Publication.PathSet.ManifestPath, $Publication.ManifestPath, (Get-NervFullStackPathComparison))) 'Publication output must expose the A2 manifest path.'
    Assert-True (Test-NervFullStackRecordSnapshotEqual -Left $Publication.AuthoritySnapshot -Right $Publication.AuthoritySnapshot) 'Publication must expose a complete authority readback snapshot.'
    Assert-True (Test-NervFullStackRecordSnapshotEqual -Left $Publication.ManifestSnapshot -Right $Publication.ManifestSnapshot) 'Publication must expose a complete manifest readback snapshot.'

    $authority = $Publication.AuthoritySnapshot.Record
    $manifest = $Publication.ManifestSnapshot.Record
    Assert-True ([string]::Equals($authority.sessionId, $SessionId, [StringComparison]::Ordinal)) 'Authority readback must contain the exact session ID.'
    Assert-True ([string]::Equals($authority.creationNonce, $Publication.CreationNonce, [StringComparison]::Ordinal)) 'Authority readback must contain the generated creation nonce.'
    Assert-True ([string]::Equals($manifest.sessionId, $SessionId, [StringComparison]::Ordinal)) 'Manifest readback must contain the exact session ID.'
    Assert-True ([string]::Equals($manifest.creationNonce, $Publication.CreationNonce, [StringComparison]::Ordinal)) 'Manifest readback must contain the authority creation nonce.'
    Assert-True ([int] $manifest.controlProtocolVersion -eq 2 -and [int] $manifest.schemaVersion -eq 2) 'Initial manifest must declare v2 schema and control protocol.'
    Assert-True ([string]::Equals($manifest.state, 'Creating', [StringComparison]::Ordinal)) 'Initial manifest must remain Creating.'
    Assert-True (-not [bool] $manifest.toolchainSnapshotComplete) 'Initial publication must leave the toolchain snapshot incomplete.'
    Assert-True (@($manifest.toolchainProbeIdentities).Count -eq 0) 'Initial publication must not fabricate probe identities.'
    Assert-True (-not [bool] $manifest.runtimeStartAttempted -and @($manifest.runtimeIdentities).Count -eq 0) 'Initial publication must not imply runtime startup.'
}

$a5Roots = [System.Collections.Generic.List[string]]::new()
try {
    $publicationRoot = New-A5FixtureRoot -Name 'publication'
    $a5Roots.Add($publicationRoot)
    $publicationSessionId = 'nerv-a500-000001'
    $publicationBoundaries = [System.Collections.Generic.List[string]]::new()
    $script:NervFullStackAuthorityPublicationCrashAction = {
        param($Boundary, $Context)
        $publicationBoundaries.Add([string] $Boundary)
    }.GetNewClosure()
    try {
        $publication = Publish-NervFullStackInitialV2Session `
            -StateRoot $publicationRoot `
            -SessionId $publicationSessionId `
            -WorktreeRoot $repoRoot `
            -CreatedByIdentity (New-A5CreatedByIdentity)
    }
    finally {
        Remove-Variable -Name NervFullStackAuthorityPublicationCrashAction -Scope Script -ErrorAction SilentlyContinue
    }
    Assert-A5PublicationPair -Publication $publication -Root $publicationRoot -SessionId $publicationSessionId
    Assert-OrdinalSetEqual -Actual @($publicationBoundaries) -Expected @(
        'before-temp-directory-create',
        'after-temp-directory-create-before-lock-create',
        'after-lock-create-before-authority-create',
        'after-temp-authority-readback-before-rename',
        'after-temp-revalidation-before-rename',
        'after-rename-before-final-authority-readback',
        'after-final-authority-readback-before-manifest-create',
        'after-manifest-readback-before-pair-revalidation',
        'after-pair-revalidation-before-return'
    ) -Message 'Publication must expose every reachable ordering boundary exactly once.'
    for ($index = 1; $index -lt $publicationBoundaries.Count; $index++) {
        $previous = $publicationBoundaries[$index - 1]
        $current = $publicationBoundaries[$index]
        $expectedOrder = @(
            'before-temp-directory-create',
            'after-temp-directory-create-before-lock-create',
            'after-lock-create-before-authority-create',
            'after-temp-authority-readback-before-rename',
            'after-temp-revalidation-before-rename',
            'after-rename-before-final-authority-readback',
            'after-final-authority-readback-before-manifest-create',
            'after-manifest-readback-before-pair-revalidation',
            'after-pair-revalidation-before-return'
        )
        Assert-True ([Array]::IndexOf([string[]] $expectedOrder, $previous) -lt [Array]::IndexOf([string[]] $expectedOrder, $current)) "Publication boundary '$previous' must precede '$current'."
    }
    Assert-True ([string]::Equals((Get-NervFullStackPublicationBoundaryObservation -StateRoot $publicationRoot -SessionId $publicationSessionId).Boundary, 'published-unprobed', [StringComparison]::Ordinal)) 'A completed pair before probe registration must classify as published-unprobed.'
    Assert-True (-not (Test-NervFullStackRuntimeStartAllowed -VerifiedSession $publication)) 'Runtime must remain blocked immediately after publication.'

    $authorityBytesBeforeDuplicate = [System.IO.File]::ReadAllBytes($publication.PathSet.AuthorityPath)
    $manifestBytesBeforeDuplicate = [System.IO.File]::ReadAllBytes($publication.PathSet.ManifestPath)
    Assert-ThrowsLike {
        Publish-NervFullStackInitialV2Session `
            -StateRoot $publicationRoot `
            -SessionId $publicationSessionId `
            -WorktreeRoot $repoRoot `
            -CreatedByIdentity (New-A5CreatedByIdentity)
    } 'publication:session-target-exists' 'Repeated publication must reject immutable authority and the existing manifest.'
    Assert-True ([System.Linq.Enumerable]::SequenceEqual([byte[]] $authorityBytesBeforeDuplicate, [byte[]] [System.IO.File]::ReadAllBytes($publication.PathSet.AuthorityPath))) 'Repeated publication must preserve authority bytes.'
    Assert-True ([System.Linq.Enumerable]::SequenceEqual([byte[]] $manifestBytesBeforeDuplicate, [byte[]] [System.IO.File]::ReadAllBytes($publication.PathSet.ManifestPath))) 'Repeated publication must preserve manifest bytes.'

    $emptyFinalRoot = New-A5FixtureRoot -Name 'empty-final'
    $a5Roots.Add($emptyFinalRoot)
    [void] (Initialize-NervFullStackTrustedStateRoot -StateRoot $emptyFinalRoot)
    $emptyFinalPaths = Get-NervFullStackControlPathSet -StateRoot $emptyFinalRoot -SessionId 'nerv-a500-000002'
    [void] [System.IO.Directory]::CreateDirectory($emptyFinalPaths.SessionDirectory)
    Assert-ThrowsLike {
        Publish-NervFullStackInitialV2Session -StateRoot $emptyFinalRoot -SessionId $emptyFinalPaths.SessionId -WorktreeRoot $repoRoot -CreatedByIdentity (New-A5CreatedByIdentity)
    } 'publication:session-target-exists' 'An empty final control directory must fail closed before publication.'
    Assert-True (-not [System.IO.File]::Exists($emptyFinalPaths.ManifestPath)) 'Rejecting an empty final directory must not publish a manifest.'
    Assert-True ([System.IO.Directory]::Exists($emptyFinalPaths.SessionDirectory)) 'Rejecting an existing final target must not clean or replace it.'

    $renameRaceRoot = New-A5FixtureRoot -Name 'rename-target-race'
    $a5Roots.Add($renameRaceRoot)
    $renameRaceSession = 'nerv-a500-000005'
    $script:NervFullStackAuthorityPublicationCrashAction = {
        param($Boundary, $Context)
        if ([string]::Equals($Boundary, 'after-temp-revalidation-before-rename', [StringComparison]::Ordinal)) {
            [void] [System.IO.Directory]::CreateDirectory($Context.FinalDirectory)
            Write-Utf8TestFile -Path (Join-Path $Context.FinalDirectory 'existing-owner.txt') -Content 'must-survive'
        }
    }
    try {
        Assert-ThrowsLike {
            Publish-NervFullStackInitialV2Session -StateRoot $renameRaceRoot -SessionId $renameRaceSession -WorktreeRoot $repoRoot -CreatedByIdentity (New-A5CreatedByIdentity)
        } 'publication:session-target-exists' 'A final target that appears immediately before rename must fail closed instead of replace or merge.'
    }
    finally {
        Remove-Variable -Name NervFullStackAuthorityPublicationCrashAction -Scope Script -ErrorAction SilentlyContinue
    }
    $renameRacePaths = Get-NervFullStackControlPathSet -StateRoot $renameRaceRoot -SessionId $renameRaceSession
    Assert-True ([string]::Equals([System.IO.File]::ReadAllText((Join-Path $renameRacePaths.SessionDirectory 'existing-owner.txt')), 'must-survive', [StringComparison]::Ordinal)) 'A failed rename must preserve every pre-existing final-target byte.'
    Assert-True (-not [System.IO.File]::Exists($renameRacePaths.ManifestPath)) 'A failed rename must not publish a manifest.'

    $existingManifestRoot = New-A5FixtureRoot -Name 'existing-manifest'
    $a5Roots.Add($existingManifestRoot)
    [void] (Initialize-NervFullStackTrustedStateRoot -StateRoot $existingManifestRoot)
    $existingManifestPaths = Get-NervFullStackControlPathSet -StateRoot $existingManifestRoot -SessionId 'nerv-a500-000003'
    Write-Utf8TestFile -Path $existingManifestPaths.ManifestPath -Content '{"existing":true}'
    Assert-ThrowsLike {
        Publish-NervFullStackInitialV2Session -StateRoot $existingManifestRoot -SessionId $existingManifestPaths.SessionId -WorktreeRoot $repoRoot -CreatedByIdentity (New-A5CreatedByIdentity)
    } 'publication:session-target-exists' 'A canonical manifest target must reserve the session ID.'
    Assert-True (-not [System.IO.Directory]::Exists($existingManifestPaths.SessionDirectory)) 'Rejecting an existing manifest must not create a final control directory.'
    Assert-True ([string]::Equals([System.IO.File]::ReadAllText($existingManifestPaths.ManifestPath), '{"existing":true}', [StringComparison]::Ordinal)) 'Rejecting an existing manifest must not overwrite it.'

    Assert-ThrowsLike {
        Publish-NervFullStackInitialV2Session -StateRoot $publicationRoot -SessionId '../escape' -WorktreeRoot $repoRoot -CreatedByIdentity (New-A5CreatedByIdentity)
    } 'path:invalid-session-id' 'Publication must reject a non-canonical session ID.'
    Assert-ThrowsLike {
        Publish-NervFullStackInitialV2Session -StateRoot $publicationRoot -SessionId 'nerv-a500-000004' -WorktreeRoot $repoRoot -CreatedByIdentity ([pscustomobject]@{ pid = 0; processStartTimeUtc = 'bad' })
    } 'publication:invalid-created-by-identity' 'Publication must reject an incomplete creator identity before creating residue.'

    foreach ($tamperField in @('sessionId', 'creationNonce', 'worktreeRoot', 'manifestPath')) {
        $tamperRoot = New-A5FixtureRoot -Name "tamper-$tamperField"
        $a5Roots.Add($tamperRoot)
        $tamperSessionId = if ([string]::Equals($tamperField, 'sessionId', [StringComparison]::Ordinal)) {
            'nerv-a501-000001'
        }
        elseif ([string]::Equals($tamperField, 'creationNonce', [StringComparison]::Ordinal)) {
            'nerv-a501-000002'
        }
        elseif ([string]::Equals($tamperField, 'worktreeRoot', [StringComparison]::Ordinal)) {
            'nerv-a501-000003'
        }
        else {
            'nerv-a501-000004'
        }
        $script:a5TamperField = $tamperField
        $script:NervFullStackAuthorityPublicationCrashAction = {
            param($Boundary, $Context)
            if ([string]::Equals($Boundary, 'after-manifest-readback-before-pair-revalidation', [StringComparison]::Ordinal)) {
                $record = [System.IO.File]::ReadAllText($Context.ManifestPath) | ConvertFrom-Json
                if ([string]::Equals($script:a5TamperField, 'sessionId', [StringComparison]::Ordinal)) {
                    $record.sessionId = 'nerv-dead-000000'
                }
                elseif ([string]::Equals($script:a5TamperField, 'creationNonce', [StringComparison]::Ordinal)) {
                    $record.creationNonce = '00000000000000000000000000000000'
                }
                elseif ([string]::Equals($script:a5TamperField, 'worktreeRoot', [StringComparison]::Ordinal)) {
                    $record.worktreeRoot = (Join-Path $Context.StateRoot 'wrong-worktree')
                }
                else {
                    $authority = [System.IO.File]::ReadAllText($Context.AuthorityPath) | ConvertFrom-Json
                    $authority.manifestPath = (Join-Path $Context.StateRoot 'fullstack-sessions/wrong.json')
                    Write-Utf8TestFile -Path $Context.AuthorityPath -Content ($authority | ConvertTo-Json -Depth 20 -Compress)
                }
                if (-not [string]::Equals($script:a5TamperField, 'manifestPath', [StringComparison]::Ordinal)) {
                    Write-Utf8TestFile -Path $Context.ManifestPath -Content ($record | ConvertTo-Json -Depth 20 -Compress)
                }
            }
        }
        try {
            Assert-ThrowsLike {
                Publish-NervFullStackInitialV2Session -StateRoot $tamperRoot -SessionId $tamperSessionId -WorktreeRoot $repoRoot -CreatedByIdentity (New-A5CreatedByIdentity)
            } 'publication:pair-mismatch' "Pair revalidation must reject a wrong $tamperField."
        }
        finally {
            Remove-Variable -Name NervFullStackAuthorityPublicationCrashAction -Scope Script -ErrorAction SilentlyContinue
            Remove-Variable -Name a5TamperField -Scope Script -ErrorAction SilentlyContinue
        }
    }

    $notPublishedRoot = New-A5FixtureRoot -Name 'seam-not-published'
    $a5Roots.Add($notPublishedRoot)
    $notPublishedSession = 'nerv-a502-000001'
    $script:NervFullStackAuthorityPublicationCrashAction = {
        param($Boundary, $Context)
        if ([string]::Equals($Boundary, 'before-temp-directory-create', [StringComparison]::Ordinal)) {
            throw 'test-only:before-temp'
        }
    }
    try {
        Assert-ThrowsLike {
            Publish-NervFullStackInitialV2Session -StateRoot $notPublishedRoot -SessionId $notPublishedSession -WorktreeRoot $repoRoot -CreatedByIdentity (New-A5CreatedByIdentity)
        } 'test-only:before-temp' 'The pre-temp crash seam must surface.'
    }
    finally {
        Remove-Variable -Name NervFullStackAuthorityPublicationCrashAction -Scope Script -ErrorAction SilentlyContinue
    }
    Assert-True ([string]::Equals((Get-NervFullStackPublicationBoundaryObservation -StateRoot $notPublishedRoot -SessionId $notPublishedSession).Boundary, 'not-published', [StringComparison]::Ordinal)) 'Crash boundary 1 must classify as not-published.'

    foreach ($tempBoundary in @(
            'after-temp-directory-create-before-lock-create',
            'after-lock-create-before-authority-create',
            'after-temp-authority-readback-before-rename',
            'after-temp-revalidation-before-rename'
        )) {
        $tempRoot = New-A5FixtureRoot -Name 'seam-temp'
        $a5Roots.Add($tempRoot)
        $tempSession = 'nerv-a502-000002'
        $script:a5CrashBoundary = $tempBoundary
        $script:NervFullStackAuthorityPublicationCrashAction = {
            param($Boundary, $Context)
            if ([string]::Equals($Boundary, $script:a5CrashBoundary, [StringComparison]::Ordinal)) {
                throw "test-only:$Boundary"
            }
        }
        try {
            Assert-ThrowsLike {
                Publish-NervFullStackInitialV2Session -StateRoot $tempRoot -SessionId $tempSession -WorktreeRoot $repoRoot -CreatedByIdentity (New-A5CreatedByIdentity)
            } "test-only:$tempBoundary" "The temp publication seam '$tempBoundary' must surface."
        }
        finally {
            Remove-Variable -Name NervFullStackAuthorityPublicationCrashAction -Scope Script -ErrorAction SilentlyContinue
            Remove-Variable -Name a5CrashBoundary -Scope Script -ErrorAction SilentlyContinue
        }
        Assert-True ([string]::Equals((Get-NervFullStackPublicationBoundaryObservation -StateRoot $tempRoot -SessionId $tempSession).Boundary, 'temp-publication-residue', [StringComparison]::Ordinal)) "Crash boundary 2 at '$tempBoundary' must classify as temp-publication-residue."
        Assert-True (-not [System.IO.Directory]::Exists((Get-NervFullStackControlPathSet -StateRoot $tempRoot -SessionId $tempSession).SessionDirectory)) 'A temp crash residue must never expose an empty final directory.'
    }

    foreach ($finalAuthorityBoundary in @(
            'after-rename-before-final-authority-readback',
            'after-final-authority-readback-before-manifest-create'
        )) {
        $finalAuthorityRoot = New-A5FixtureRoot -Name 'seam-final-authority'
        $a5Roots.Add($finalAuthorityRoot)
        $finalAuthoritySession = 'nerv-a502-000003'
        $script:a5CrashBoundary = $finalAuthorityBoundary
        $script:NervFullStackAuthorityPublicationCrashAction = {
            param($Boundary, $Context)
            if ([string]::Equals($Boundary, $script:a5CrashBoundary, [StringComparison]::Ordinal)) {
                throw "test-only:$Boundary"
            }
        }
        try {
            Assert-ThrowsLike {
                Publish-NervFullStackInitialV2Session -StateRoot $finalAuthorityRoot -SessionId $finalAuthoritySession -WorktreeRoot $repoRoot -CreatedByIdentity (New-A5CreatedByIdentity)
            } "test-only:$finalAuthorityBoundary" "The final-authority seam '$finalAuthorityBoundary' must surface."
        }
        finally {
            Remove-Variable -Name NervFullStackAuthorityPublicationCrashAction -Scope Script -ErrorAction SilentlyContinue
            Remove-Variable -Name a5CrashBoundary -Scope Script -ErrorAction SilentlyContinue
        }
        Assert-True ([string]::Equals((Get-NervFullStackPublicationBoundaryObservation -StateRoot $finalAuthorityRoot -SessionId $finalAuthoritySession).Boundary, 'final-authority-only-init-incomplete', [StringComparison]::Ordinal)) "Crash boundary 3 at '$finalAuthorityBoundary' must classify as final-authority-only-init-incomplete."
        Assert-True (-not [System.IO.File]::Exists((Get-NervFullStackControlPathSet -StateRoot $finalAuthorityRoot -SessionId $finalAuthoritySession).ManifestPath)) 'A final-authority-only residue must not be repaired with a manifest.'
    }

    $finalAuthorityReadbackRoot = New-A5FixtureRoot -Name 'final-authority-readback-failure'
    $a5Roots.Add($finalAuthorityReadbackRoot)
    $finalAuthorityReadbackSession = 'nerv-a502-000008'
    $script:NervFullStackAuthorityPublicationCrashAction = {
        param($Boundary, $Context)
        if ([string]::Equals($Boundary, 'after-rename-before-final-authority-readback', [StringComparison]::Ordinal)) {
            Write-Utf8TestFile -Path $Context.AuthorityPath -Content '{"kind":"fullstack-session-authority"'
        }
    }
    try {
        Assert-ThrowsLike {
            Publish-NervFullStackInitialV2Session -StateRoot $finalAuthorityReadbackRoot -SessionId $finalAuthorityReadbackSession -WorktreeRoot $repoRoot -CreatedByIdentity (New-A5CreatedByIdentity)
        } 'record:invalid-json' 'Final authority readback failure must stop publication before manifest CreateNew.'
    }
    finally {
        Remove-Variable -Name NervFullStackAuthorityPublicationCrashAction -Scope Script -ErrorAction SilentlyContinue
    }
    $finalAuthorityReadbackPaths = Get-NervFullStackControlPathSet -StateRoot $finalAuthorityReadbackRoot -SessionId $finalAuthorityReadbackSession
    Assert-True (-not [System.IO.File]::Exists($finalAuthorityReadbackPaths.ManifestPath)) 'A final authority readback failure must never leave a manifest-first false v2 publication.'

    $manifestIncompleteRoot = New-A5FixtureRoot -Name 'seam-manifest-incomplete'
    $a5Roots.Add($manifestIncompleteRoot)
    $manifestIncompleteSession = 'nerv-a502-000004'
    $script:NervFullStackAuthorityPublicationCrashAction = {
        param($Boundary, $Context)
        if ([string]::Equals($Boundary, 'after-manifest-readback-before-pair-revalidation', [StringComparison]::Ordinal)) {
            Write-Utf8TestFile -Path $Context.ManifestPath -Content '{"broken":'
            throw 'test-only:manifest-readback'
        }
    }
    try {
        Assert-ThrowsLike {
            Publish-NervFullStackInitialV2Session -StateRoot $manifestIncompleteRoot -SessionId $manifestIncompleteSession -WorktreeRoot $repoRoot -CreatedByIdentity (New-A5CreatedByIdentity)
        } 'test-only:manifest-readback' 'The manifest failure seam must surface.'
    }
    finally {
        Remove-Variable -Name NervFullStackAuthorityPublicationCrashAction -Scope Script -ErrorAction SilentlyContinue
    }
    Assert-True ([string]::Equals((Get-NervFullStackPublicationBoundaryObservation -StateRoot $manifestIncompleteRoot -SessionId $manifestIncompleteSession).Boundary, 'manifest-init-incomplete', [StringComparison]::Ordinal)) 'Crash boundary 4 must classify as manifest-init-incomplete.'

    $publishedUnprobedRoot = New-A5FixtureRoot -Name 'seam-published-unprobed'
    $a5Roots.Add($publishedUnprobedRoot)
    $publishedUnprobedSession = 'nerv-a502-000005'
    $script:NervFullStackAuthorityPublicationCrashAction = {
        param($Boundary, $Context)
        if ([string]::Equals($Boundary, 'after-pair-revalidation-before-return', [StringComparison]::Ordinal)) {
            throw 'test-only:published-unprobed'
        }
    }
    try {
        Assert-ThrowsLike {
            Publish-NervFullStackInitialV2Session -StateRoot $publishedUnprobedRoot -SessionId $publishedUnprobedSession -WorktreeRoot $repoRoot -CreatedByIdentity (New-A5CreatedByIdentity)
        } 'test-only:published-unprobed' 'The publication-to-probe seam must surface.'
    }
    finally {
        Remove-Variable -Name NervFullStackAuthorityPublicationCrashAction -Scope Script -ErrorAction SilentlyContinue
    }
    Assert-True ([string]::Equals((Get-NervFullStackPublicationBoundaryObservation -StateRoot $publishedUnprobedRoot -SessionId $publishedUnprobedSession).Boundary, 'published-unprobed', [StringComparison]::Ordinal)) 'Crash boundary 5 must classify as published-unprobed.'

    $unverifiedSession = [pscustomobject][ordered]@{
        Verified = $publication.Verified
        PublicationComplete = $false
        StateRoot = $publication.StateRoot
        SessionId = $publication.SessionId
        CreationNonce = $publication.CreationNonce
        WorktreeRoot = $publication.WorktreeRoot
        AuthorityIdentity = $publication.AuthorityIdentity
        ManifestSnapshot = $publication.ManifestSnapshot
    }
    Assert-ThrowsLike {
        Register-NervFullStackToolchainProbeIdentity -VerifiedSession $unverifiedSession -ProbeIdentity (New-A5ProbeIdentity)
    } 'publication:verified-complete-session-required' 'Probe registration must reject a session before publication completes.'
    Assert-ThrowsLike {
        Register-NervFullStackToolchainProbeIdentity -VerifiedSession $publication -ProbeIdentity ([pscustomobject]@{ pid = 5153; processStartTimeUtc = 'bad'; role = '' })
    } 'publication:invalid-probe-identity' 'Probe registration must reject an incomplete exact identity.'
    Assert-True (Test-NervFullStackRecordSnapshotEqual -Left $publication.ManifestSnapshot -Right (Read-NervFullStackVerifiedRecord -VerifiedTarget (Test-NervFullStackTrustedPathGraph -StateRoot $publication.StateRoot -CandidatePath $publication.ManifestPath -ExpectedKind File) -RecordKind $publication.ManifestSnapshot.RecordKind)) 'A rejected probe registration must preserve the original complete manifest snapshot.'

    $registered = Register-NervFullStackToolchainProbeIdentity -VerifiedSession $publication -ProbeIdentity (New-A5ProbeIdentity)
    Assert-True $registered.PublicationComplete 'Probe registration must preserve verified publication capability.'
    Assert-True (@($registered.ManifestSnapshot.Record.toolchainProbeIdentities).Count -eq 1) 'Probe registration must persist exactly one identity.'
    Assert-True ([int] $registered.ManifestSnapshot.Record.toolchainProbeIdentities[0].pid -eq 5153) 'Probe registration must preserve the exact PID.'
    $expectedProbeStart = [DateTimeOffset]::ParseExact('2026-08-22T00:00:01.0000000Z', 'O', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind)
    $actualProbeStart = [DateTimeOffset] ([DateTime] $registered.ManifestSnapshot.Record.toolchainProbeIdentities[0].processStartTimeUtc)
    Assert-True ($actualProbeStart.UtcTicks -eq $expectedProbeStart.UtcTicks) 'Probe registration must preserve the normalized process start instant.'
    Assert-True ([string]::Equals($registered.ManifestSnapshot.Record.toolchainProbeIdentities[0].role, 'toolchain-version-probe', [StringComparison]::Ordinal)) 'Probe registration must preserve the exact role.'
    Assert-True (-not [bool] $registered.ManifestSnapshot.Record.toolchainSnapshotComplete) 'Probe registration alone must leave the snapshot incomplete.'
    Assert-True ([string]::Equals((Get-NervFullStackPublicationBoundaryObservation -StateRoot $publicationRoot -SessionId $publicationSessionId).Boundary, 'toolchain-probe-incomplete', [StringComparison]::Ordinal)) 'Crash boundary 6 must classify a registered probe with incomplete snapshot as toolchain-probe-incomplete.'
    Assert-True (-not (Test-NervFullStackRuntimeStartAllowed -VerifiedSession $registered)) 'Runtime must remain blocked while the probe snapshot is incomplete.'

    $preSnapshotBytes = [System.IO.File]::ReadAllBytes($registered.ManifestPath)
    Assert-ThrowsLike {
        Complete-NervFullStackToolchainSnapshot `
            -VerifiedSession $registered `
            -ExpectedSnapshot $publication.ManifestSnapshot `
            -ToolchainSnapshot ([pscustomobject][ordered]@{ node = '22.22.3'; dotnet = '10.0.100'; aspire = '13.4.6'; dcp = '13.4.6' })
    } 'record:cas-conflict' 'Snapshot completion must reject a stale full ExpectedSnapshot.'
    Assert-True ([System.Linq.Enumerable]::SequenceEqual([byte[]] $preSnapshotBytes, [byte[]] [System.IO.File]::ReadAllBytes($registered.ManifestPath))) 'A stale snapshot CAS must preserve the complete old manifest.'
    Assert-ThrowsLike {
        Complete-NervFullStackToolchainSnapshot -VerifiedSession $registered -ExpectedSnapshot $registered.ManifestSnapshot -ToolchainSnapshot ([pscustomobject][ordered]@{})
    } 'publication:invalid-toolchain-snapshot' 'Snapshot completion must reject an empty toolchain snapshot.'

    $script:NervFullStackVerifiedRecordStoreCrashAction = {
        param($Boundary, $Context)
        if ([string]::Equals($Boundary, 'after-temp-readback-before-replace', [StringComparison]::Ordinal)) {
            throw 'test-only:snapshot-before-replace'
        }
    }
    try {
        Assert-ThrowsLike {
            Complete-NervFullStackToolchainSnapshot `
                -VerifiedSession $registered `
                -ExpectedSnapshot $registered.ManifestSnapshot `
                -ToolchainSnapshot ([pscustomobject][ordered]@{ node = '22.22.3'; dotnet = '10.0.100'; aspire = '13.4.6'; dcp = '13.4.6' })
        } 'test-only:snapshot-before-replace' 'The snapshot pre-replace crash seam must surface.'
    }
    finally {
        Remove-Variable -Name NervFullStackVerifiedRecordStoreCrashAction -Scope Script -ErrorAction SilentlyContinue
    }
    Assert-True ([string]::Equals((Get-NervFullStackPublicationBoundaryObservation -StateRoot $publicationRoot -SessionId $publicationSessionId).Boundary, 'toolchain-probe-incomplete', [StringComparison]::Ordinal)) 'Crash boundary 7 before atomic replace must retain the complete old incomplete manifest.'
    Assert-True (-not (Test-NervFullStackRuntimeStartAllowed -VerifiedSession $registered)) 'A pre-replace snapshot crash must keep runtime blocked.'

    $postReplaceRoot = New-A5FixtureRoot -Name 'snapshot-post-replace'
    $a5Roots.Add($postReplaceRoot)
    $postReplacePublication = Publish-NervFullStackInitialV2Session -StateRoot $postReplaceRoot -SessionId 'nerv-a502-000006' -WorktreeRoot $repoRoot -CreatedByIdentity (New-A5CreatedByIdentity)
    $postReplaceRegistered = Register-NervFullStackToolchainProbeIdentity -VerifiedSession $postReplacePublication -ProbeIdentity (New-A5ProbeIdentity -ProcessId 5154)
    $script:NervFullStackVerifiedRecordStoreCrashAction = {
        param($Boundary, $Context)
        if ([string]::Equals($Boundary, 'after-replace-before-final-readback', [StringComparison]::Ordinal)) {
            throw 'test-only:snapshot-after-replace'
        }
    }
    try {
        Assert-ThrowsLike {
            Complete-NervFullStackToolchainSnapshot `
                -VerifiedSession $postReplaceRegistered `
                -ExpectedSnapshot $postReplaceRegistered.ManifestSnapshot `
                -ToolchainSnapshot ([pscustomobject][ordered]@{ node = '22.22.3'; dotnet = '10.0.100'; aspire = '13.4.6'; dcp = '13.4.6' })
        } 'test-only:snapshot-after-replace' 'The snapshot post-replace crash seam must surface.'
    }
    finally {
        Remove-Variable -Name NervFullStackVerifiedRecordStoreCrashAction -Scope Script -ErrorAction SilentlyContinue
    }
    Assert-True ([string]::Equals((Get-NervFullStackPublicationBoundaryObservation -StateRoot $postReplaceRoot -SessionId $postReplacePublication.SessionId).Boundary, 'published-unstarted', [StringComparison]::Ordinal)) 'Crash boundary 7 after atomic replace must expose the complete new snapshot.'
    Assert-True (Test-NervFullStackRuntimeStartAllowed -VerifiedSession $postReplaceRegistered) 'A complete post-replace record must be admitted only from durable readback, even when the caller did not receive success.'

    $completed = Complete-NervFullStackToolchainSnapshot `
        -VerifiedSession $registered `
        -ExpectedSnapshot $registered.ManifestSnapshot `
        -ToolchainSnapshot ([pscustomobject][ordered]@{ node = '22.22.3'; dotnet = '10.0.100'; aspire = '13.4.6'; dcp = '13.4.6' })
    Assert-True ([bool] $completed.ManifestSnapshot.Record.toolchainSnapshotComplete) 'Snapshot completion must return only a complete final readback.'
    Assert-True ([string]::Equals($completed.ManifestSnapshot.Record.toolchainSnapshot.node, '22.22.3', [StringComparison]::Ordinal)) 'Snapshot completion must preserve every toolchain field.'
    Assert-True (@($completed.ManifestSnapshot.Record.toolchainProbeIdentities).Count -eq 1) 'Snapshot completion must atomically preserve registered probe identities.'
    Assert-True ([string]::Equals((Get-NervFullStackPublicationBoundaryObservation -StateRoot $publicationRoot -SessionId $publicationSessionId).Boundary, 'published-unstarted', [StringComparison]::Ordinal)) 'Crash boundary 8 must classify a complete snapshot before runtime as published-unstarted.'
    $runtimeOrDestructiveCallCount = 0
    Assert-True (Test-NervFullStackRuntimeStartAllowed -VerifiedSession $completed) 'Runtime gate must return true only after full snapshot CAS/readback.'
    Assert-True ($runtimeOrDestructiveCallCount -eq 0) 'Gate evaluation must perform zero runtime or destructive calls.'

    $readbackMismatchRoot = New-A5FixtureRoot -Name 'snapshot-readback-mismatch'
    $a5Roots.Add($readbackMismatchRoot)
    $readbackMismatchPublication = Publish-NervFullStackInitialV2Session -StateRoot $readbackMismatchRoot -SessionId 'nerv-a502-000007' -WorktreeRoot $repoRoot -CreatedByIdentity (New-A5CreatedByIdentity)
    $readbackMismatchRegistered = Register-NervFullStackToolchainProbeIdentity -VerifiedSession $readbackMismatchPublication -ProbeIdentity (New-A5ProbeIdentity -ProcessId 5155)
    $script:NervFullStackVerifiedRecordStoreCrashAction = {
        param($Boundary, $Context)
        if ([string]::Equals($Boundary, 'after-replace-before-final-readback', [StringComparison]::Ordinal)) {
            Write-Utf8TestFile -Path $Context.Path -Content '{"schemaVersion":2,"kind":"request","tampered":true}'
        }
    }
    try {
        Assert-ThrowsLike {
            Complete-NervFullStackToolchainSnapshot `
                -VerifiedSession $readbackMismatchRegistered `
                -ExpectedSnapshot $readbackMismatchRegistered.ManifestSnapshot `
                -ToolchainSnapshot ([pscustomobject][ordered]@{ node = '22.22.3'; dotnet = '10.0.100'; aspire = '13.4.6'; dcp = '13.4.6' })
        } 'record:readback-mismatch' 'Snapshot completion must reject final raw bytes/fields/identity readback mismatch.'
    }
    finally {
        Remove-Variable -Name NervFullStackVerifiedRecordStoreCrashAction -Scope Script -ErrorAction SilentlyContinue
    }
    Assert-True (-not (Test-NervFullStackRuntimeStartAllowed -VerifiedSession $readbackMismatchRegistered)) 'A readback mismatch must never admit runtime.'

    $runtimeFixtureRoot = New-A5FixtureRoot -Name 'runtime-old-new-fixtures'
    $a5Roots.Add($runtimeFixtureRoot)
    [void] (Initialize-NervFullStackTrustedStateRoot -StateRoot $runtimeFixtureRoot)
    $runtimeFixtureSession = 'nerv-a503-000001'
    $runtimeFixtureNonce = 'abcdef0123456789abcdef0123456789'
    Publish-A4Authority -Root $runtimeFixtureRoot -SessionId $runtimeFixtureSession -CreationNonce $runtimeFixtureNonce
    $runtimeOldRecord = New-A4V2ManifestRecord `
        -Root $runtimeFixtureRoot `
        -SessionId $runtimeFixtureSession `
        -CreationNonce $runtimeFixtureNonce `
        -ToolchainSnapshotComplete $true `
        -ToolchainProbeIdentities @((New-A5ProbeIdentity -ProcessId 5156)) `
        -RuntimeStartAttempted $true `
        -RuntimeIdentities @()
    $runtimeOldRecord | Add-Member -NotePropertyName toolchainSnapshot -NotePropertyValue ([pscustomobject][ordered]@{ node = '22.22.3'; dotnet = '10.0.100'; aspire = '13.4.6'; dcp = '13.4.6' })
    Write-A4Record -Path (Join-Path $runtimeFixtureRoot "fullstack-sessions/$runtimeFixtureSession.json") -Record $runtimeOldRecord
    Assert-True ([string]::Equals((Get-NervFullStackPublicationBoundaryObservation -StateRoot $runtimeFixtureRoot -SessionId $runtimeFixtureSession).Boundary, 'published-starting-uncertain', [StringComparison]::Ordinal)) 'Crash boundary 9 must be classified from a complete old runtime fixture without launching runtime.'

    $runtimeNewRecord = New-A4V2ManifestRecord `
        -Root $runtimeFixtureRoot `
        -SessionId $runtimeFixtureSession `
        -CreationNonce $runtimeFixtureNonce `
        -ToolchainSnapshotComplete $true `
        -ToolchainProbeIdentities @((New-A5ProbeIdentity -ProcessId 5156)) `
        -RuntimeStartAttempted $true `
        -RuntimeIdentities @([pscustomobject][ordered]@{ pid = 5157; processStartTimeUtc = '2026-08-22T00:00:02.0000000Z'; role = 'fixture-runtime' })
    $runtimeNewRecord | Add-Member -NotePropertyName toolchainSnapshot -NotePropertyValue ([pscustomobject][ordered]@{ node = '22.22.3'; dotnet = '10.0.100'; aspire = '13.4.6'; dcp = '13.4.6' })
    Write-A4Record -Path (Join-Path $runtimeFixtureRoot "fullstack-sessions/$runtimeFixtureSession.json") -Record $runtimeNewRecord
    $runtimeNewGeneration = Get-NervFullStackProtocolGenerationObservation -StateRoot $runtimeFixtureRoot -SessionId $runtimeFixtureSession
    Assert-True ([string]::Equals($runtimeNewGeneration.Generation, 'v2', [StringComparison]::Ordinal)) 'Crash boundary 10 new complete runtime fixture must remain legal v2.'
    Assert-True ($null -eq (Get-NervFullStackPublicationBoundaryObservation -StateRoot $runtimeFixtureRoot -SessionId $runtimeFixtureSession).Boundary) 'Crash boundary 10 must use the full new record without inventing a half-commit publication boundary.'
    Assert-True ($runtimeOrDestructiveCallCount -eq 0) 'Runtime boundary fixtures must execute zero production runtime or destructive calls.'
}
finally {
    Remove-Variable -Name NervFullStackAuthorityPublicationCrashAction -Scope Script -ErrorAction SilentlyContinue
    Remove-Variable -Name NervFullStackVerifiedRecordStoreCrashAction -Scope Script -ErrorAction SilentlyContinue
    Remove-Variable -Name a5CrashBoundary -Scope Script -ErrorAction SilentlyContinue
    Remove-Variable -Name a5TamperField -Scope Script -ErrorAction SilentlyContinue
    foreach ($root in $a5Roots) {
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
