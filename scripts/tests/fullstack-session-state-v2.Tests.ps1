# Script-Governance:
#   Category: check
#   SideEffects:
#     - None
#   Writes:
#     - None
#   Cleanup:
#     - No process or external resource ownership
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '../..')

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) {
        throw $Message
    }
}

function Assert-Throws([scriptblock] $Action, [string] $Message) {
    $threw = $false
    try {
        & $Action
    }
    catch {
        $threw = $true
    }

    Assert-True $threw $Message
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
