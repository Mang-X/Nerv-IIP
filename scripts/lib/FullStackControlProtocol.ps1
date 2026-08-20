# Script-Governance:
#   Category: library
#   SideEffects:
#     - None
#   Writes:
#     - None
#   Cleanup:
#     - No process or external resource ownership
#   Requires:
#     - PowerShell 7

Set-StrictMode -Version Latest

# The values below are the protocol identifiers frozen by Spec r2. A dictionary
# with an Ordinal comparer is used for domains as well as members so a caller
# cannot turn a protocol identifier into a culture- or case-insensitive lookup.
$script:NervFullStackProtocolVocabulary = [System.Collections.Generic.Dictionary[string, string[]]]::new([StringComparer]::Ordinal)
$script:NervFullStackProtocolValueSets = [System.Collections.Generic.Dictionary[string, System.Collections.Generic.HashSet[string]]]::new([StringComparer]::Ordinal)

function Add-NervFullStackProtocolVocabularyDomain {
    param(
        [Parameter(Mandatory)]
        [string] $Domain,

        [Parameter(Mandatory)]
        [string[]] $Values
    )

    $members = [string[]] $Values.Clone()
    $set = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($value in $members) {
        [void] $set.Add($value)
    }

    $script:NervFullStackProtocolVocabulary.Add($Domain, $members)
    $script:NervFullStackProtocolValueSets.Add($Domain, $set)
}

Add-NervFullStackProtocolVocabularyDomain -Domain 'generation' -Values @(
    'v0',
    'v1',
    'v2',
    'invalid'
)
Add-NervFullStackProtocolVocabularyDomain -Domain 'activation' -Values @(
    'GateOff',
    'ActiveV2',
    'InvalidMarker'
)
Add-NervFullStackProtocolVocabularyDomain -Domain 'compatibility' -Values @(
    'legacy-stopped',
    'legacy-active-blocked',
    'prototype-v1-untrusted',
    'v2'
)
Add-NervFullStackProtocolVocabularyDomain -Domain 'recordKind' -Values @(
    'fullstack-protocol-mode',
    'fullstack-session-authority',
    'request',
    'ack'
)
Add-NervFullStackProtocolVocabularyDomain -Domain 'publicationBoundary' -Values @(
    'not-published',
    'temp-publication-residue',
    'final-authority-only-init-incomplete',
    'manifest-init-incomplete',
    'published-unprobed',
    'toolchain-probe-incomplete',
    'published-unstarted',
    'published-starting-uncertain'
)

# This is an injected test seam, not a persisted protocol state. In particular,
# it is intentionally absent from publicationBoundary.
Add-NervFullStackProtocolVocabularyDomain -Domain 'crashSeam' -Values @('test-only')

Add-NervFullStackProtocolVocabularyDomain -Domain 'guardianDisposition' -Values @(
    'Absent-before-request',
    'Absent-after-request-before-ack',
    'Ack+Absent',
    'Ack+Active',
    'Mismatched',
    'Unknown'
)
Add-NervFullStackProtocolVocabularyDomain -Domain 'guardianRegistrationState' -Values @(
    'Registered',
    'NotRegistered',
    'NonV2NotApplicable'
)
Add-NervFullStackProtocolVocabularyDomain -Domain 'resultDisposition' -Values @(
    'ReadOnlyLegacyStopped',
    'BlockedLegacyActive',
    'BlockedPrototypeV1',
    'AlreadyInProgress',
    'CleanupBlocked',
    'CleanupFailed',
    'Stopped'
)
Add-NervFullStackProtocolVocabularyDomain -Domain 'stage' -Values @(
    'guardian',
    'aspire',
    'authoritative-process',
    'grammar-fallback',
    'docker'
)
Add-NervFullStackProtocolVocabularyDomain -Domain 'stageStatus' -Values @(
    'not-attempted',
    'passed',
    'failed',
    'blocked'
)

function Get-NervFullStackProtocolVocabulary {
    [OutputType([pscustomobject])]
    param()

    $copy = [ordered]@{}
    foreach ($entry in $script:NervFullStackProtocolVocabulary.GetEnumerator()) {
        $copy[$entry.Key] = [string[]] $entry.Value.Clone()
    }

    return [pscustomobject] $copy
}

function Test-NervFullStackProtocolValue {
    [OutputType([bool])]
    param(
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string] $Domain,

        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string] $Value
    )

    if (-not $script:NervFullStackProtocolValueSets.ContainsKey($Domain)) {
        return $false
    }

    return $script:NervFullStackProtocolValueSets[$Domain].Contains($Value)
}

function Assert-NervFullStackProtocolValue {
    param(
        [Parameter(Mandatory)]
        [string] $Domain,

        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string] $Value
    )

    if (-not (Test-NervFullStackProtocolValue -Domain $Domain -Value $Value)) {
        throw "Invalid Nerv-IIP full-stack protocol value '$Domain=$Value'."
    }
}

function New-NervFullStackProtocolObservation {
    [OutputType([pscustomobject])]
    param(
        [Parameter(Mandatory)]
        [string] $Generation,

        [Parameter(Mandatory)]
        [string] $Activation,

        [Parameter(Mandatory)]
        [string] $Compatibility,

        [Parameter(Mandatory)]
        [string] $RecordKind,

        [AllowNull()]
        [string] $PublicationBoundary
    )

    Assert-NervFullStackProtocolValue -Domain 'generation' -Value $Generation
    Assert-NervFullStackProtocolValue -Domain 'activation' -Value $Activation
    Assert-NervFullStackProtocolValue -Domain 'compatibility' -Value $Compatibility
    Assert-NervFullStackProtocolValue -Domain 'recordKind' -Value $RecordKind

    $publicationBoundaryValue = $null
    if ($PSBoundParameters.ContainsKey('PublicationBoundary') -and $null -ne $PublicationBoundary) {
        Assert-NervFullStackProtocolValue -Domain 'publicationBoundary' -Value $PublicationBoundary
        $publicationBoundaryValue = $PublicationBoundary
    }

    return [pscustomobject][ordered]@{
        generation = $Generation
        activation = $Activation
        compatibility = $Compatibility
        recordKind = $RecordKind
        publicationBoundary = $publicationBoundaryValue
    }
}
