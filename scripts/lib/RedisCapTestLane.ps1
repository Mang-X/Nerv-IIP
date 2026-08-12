# Script-Governance:
#   Category: library
#   SideEffects:
#     - Reads a Redis/CAP lane manifest and VSTest TRX files supplied by the caller
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

function New-NervRedisCapMemberIdentity {
    param(
        [Parameter(Mandatory)] [string] $MemberId,
        [Parameter(Mandatory)] [string] $CapVersionPrefix,
        [Parameter(Mandatory)] [string] $DatabaseSuffix
    )

    if ($DatabaseSuffix -cnotmatch '_(?<attempt>[1-9][0-9]*)$') {
        throw "Redis/CAP database suffix '$DatabaseSuffix' must end with an explicit positive run attempt."
    }

    $attemptToken = "a$($Matches.attempt)"
    $identityInput = "$MemberId|$DatabaseSuffix"
    $identityBytes = [Text.Encoding]::UTF8.GetBytes($identityInput)
    $hashBytes = [Security.Cryptography.SHA256]::HashData($identityBytes)
    $identityHash = [Convert]::ToHexString($hashBytes).ToLowerInvariant()
    $hashToken = $identityHash.Substring(0, 8)
    $prefixLength = 20 - $attemptToken.Length - $hashToken.Length - 2
    if ($prefixLength -lt 1) { throw "Redis/CAP run attempt '$($Matches.attempt)' cannot fit the 20-character CAP version contract." }
    $capPrefix = $CapVersionPrefix.Substring(0, [Math]::Min($CapVersionPrefix.Length, $prefixLength))

    return [pscustomobject]@{
        capVersion = "$capPrefix-$attemptToken-$hashToken"
        redisNamespace = "nerv:n688:$($identityHash.Substring(0, 16)):"
        runAttempt = [int]$Matches.attempt
    }
}

function Get-NervRedisCapNamespaceKeys {
    param(
        [Parameter(Mandatory)] [string] $Namespace,
        [Parameter(Mandatory)] [scriptblock] $EnumerateKeys
    )

    if ($Namespace -cnotmatch '^nerv:n688:[a-z0-9-]+:$') {
        throw "Redis/CAP namespace '$Namespace' is not canonical."
    }

    $keys = @(& $EnumerateKeys $Namespace | ForEach-Object { [string]$_ })
    foreach ($key in $keys) {
        if (-not $key.StartsWith($Namespace, [StringComparison]::Ordinal)) {
            throw "Redis/CAP namespace enumeration returned foreign key '$key' for '$Namespace'."
        }
    }
    return @($keys | Sort-Object -Unique)
}

function New-NervRedisCapNamespaceClaim {
    param(
        [Parameter(Mandatory)] [string] $Namespace,
        [Parameter(Mandatory)] [scriptblock] $EnumerateKeys
    )

    $existingKeys = @(Get-NervRedisCapNamespaceKeys -Namespace $Namespace -EnumerateKeys $EnumerateKeys)
    if ($existingKeys.Count -ne 0) {
        throw "Redis/CAP namespace '$Namespace' is not empty and cannot be claimed."
    }

    return [pscustomobject]@{ namespace = $Namespace }
}

function Remove-NervRedisCapNamespace {
    param(
        [Parameter(Mandatory)] [psobject] $Claim,
        [Parameter(Mandatory)] [scriptblock] $EnumerateKeys,
        [Parameter(Mandatory)] [scriptblock] $RemoveKey
    )

    $namespace = [string]$Claim.namespace
    $ownedKeys = @(Get-NervRedisCapNamespaceKeys -Namespace $namespace -EnumerateKeys $EnumerateKeys)
    foreach ($key in $ownedKeys) { & $RemoveKey $key }

    $remainingKeys = @(Get-NervRedisCapNamespaceKeys -Namespace $namespace -EnumerateKeys $EnumerateKeys)
    if ($remainingKeys.Count -ne 0) {
        throw "Redis/CAP namespace '$namespace' cleanup left $($remainingKeys.Count) key(s)."
    }
}

function Import-NervRedisCapTestLaneMember {
    param(
        [Parameter(Mandatory)] [string] $ManifestPath,
        [Parameter(Mandatory)] [string] $MemberId,
        [Parameter(Mandatory)] [string] $RepositoryRoot
    )

    $manifest = Get-Content -LiteralPath (Resolve-Path $ManifestPath) -Raw | ConvertFrom-Json -Depth 20
    if ([int]$manifest.schemaVersion -ne 1) { throw "Unsupported Redis/CAP lane manifest schemaVersion '$($manifest.schemaVersion)'." }
    $members = @($manifest.members)
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($member in $members) {
        $id = [string]$member.id
        if ($id -cnotmatch '^[a-z0-9]+(?:-[a-z0-9]+)*$' -or -not $ids.Add($id)) { throw "Redis/CAP lane member id '$id' must be unique and canonical." }
        $tiers = [Collections.Generic.HashSet[string]]::new([string[]]@('core', 'extended'), [StringComparer]::Ordinal)
        $statuses = [Collections.Generic.HashSet[string]]::new([string[]]@('active', 'deferred', 'blocked'), [StringComparer]::Ordinal)
        if (-not $tiers.Contains([string]$member.tier)) { throw "Redis/CAP lane member '$id' has an invalid tier." }
        if (-not $statuses.Contains([string]$member.status)) { throw "Redis/CAP lane member '$id' has an invalid status." }
        if ([string]$member.databasePrefix -cnotmatch '^[a-z][a-z0-9_]{0,39}$') { throw "Redis/CAP lane member '$id' has an invalid databasePrefix." }
        if ([string]$member.capVersionPrefix -cnotmatch '^[a-z][a-z0-9-]{0,11}$') { throw "Redis/CAP lane member '$id' has an invalid capVersionPrefix." }
        $diagnosticSchemas = @($member.diagnosticSchemas | ForEach-Object { [string]$_ })
        $diagnosticSchemaSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        if ($diagnosticSchemas.Count -eq 0 -or @($diagnosticSchemas | Where-Object { $_ -cnotmatch '^[a-z][a-z0-9_]{0,62}$' -or -not $diagnosticSchemaSet.Add($_) }).Count -gt 0) { throw "Redis/CAP lane member '$id' must declare unique canonical diagnosticSchemas." }
        $project = [string]$member.project
        if ($project -cnotmatch '^backend/.+\.csproj$' -or -not (Test-Path -LiteralPath (Join-Path $RepositoryRoot $project) -PathType Leaf)) { throw "Redis/CAP lane member '$id' project is missing or outside backend." }
        if ([string]::IsNullOrWhiteSpace([string]$member.filter)) { throw "Redis/CAP lane member '$id' must declare an exact test filter." }
        $identities = @($member.expectedTestIdentities | ForEach-Object { [string]$_ })
        $identitySet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        if ($identities.Count -eq 0 -or @($identities | Where-Object { [string]::IsNullOrWhiteSpace($_) -or -not $identitySet.Add($_) }).Count -gt 0) { throw "Redis/CAP lane member '$id' must freeze a non-empty unique test identity set." }
    }

    $matches = @($members | Where-Object { [string]::Equals([string]$_.id, $MemberId, [StringComparison]::Ordinal) })
    if ($matches.Count -ne 1) { throw "Redis/CAP lane member '$MemberId' must resolve exactly once." }
    if (-not [string]::Equals([string]$matches[0].status, 'active', [StringComparison]::Ordinal)) { throw "Redis/CAP lane member '$MemberId' is not active." }
    return $matches[0]
}

function Get-NervRedisCapTrxResult {
    param(
        [Parameter(Mandatory)] [string] $ResultsDirectory,
        [Parameter(Mandatory)] [string[]] $ExpectedTestIdentities,
        [switch] $AllowInvalid
    )

    $trxFiles = @(Get-ChildItem -LiteralPath $ResultsDirectory -Filter '*.trx' -File -Recurse)
    if ($trxFiles.Count -ne 1) { throw "Redis/CAP lane must produce exactly one TRX file; observed $($trxFiles.Count)." }
    [xml]$trx = Get-Content -LiteralPath $trxFiles[0].FullName -Raw
    $results = @($trx.SelectNodes("//*[local-name()='UnitTestResult']"))
    $definitions = @($trx.SelectNodes("//*[local-name()='UnitTest']"))
    $definitionById = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    foreach ($definition in $definitions) {
        $method = $definition.SelectSingleNode("./*[local-name()='TestMethod']")
        if ($null -ne $method) { $definitionById[[string]$definition.id] = "$($method.className).$($method.name)" }
    }
    $observed = @($results | ForEach-Object { if ($definitionById.ContainsKey([string]$_.testId)) { $definitionById[[string]$_.testId] } else { [string]$_.testName } })
    $expected = @($ExpectedTestIdentities)
    [Array]::Sort($observed, [StringComparer]::Ordinal)
    [Array]::Sort($expected, [StringComparer]::Ordinal)
    $observedSorted = @($observed)
    $expectedSorted = @($expected)
    $identitiesMatch = [string]::Equals(($observedSorted -join "`n"), ($expectedSorted -join "`n"), [StringComparison]::Ordinal)
    $passed = @($results | Where-Object { [string]::Equals([string]$_.outcome, 'Passed', [StringComparison]::Ordinal) }).Count
    $failed = @($results | Where-Object { [string]::Equals([string]$_.outcome, 'Failed', [StringComparison]::Ordinal) }).Count
    $skipped = $results.Count - $passed - $failed
    $valid = $identitiesMatch -and $passed -eq $expected.Count -and $failed -eq 0 -and $skipped -eq 0
    $result = [pscustomobject]@{ total = $results.Count; passed = $passed; failed = $failed; skipped = $skipped; identities = $observedSorted; identitiesMatch = $identitiesMatch; valid = $valid }
    if (-not $AllowInvalid -and -not $identitiesMatch) { throw 'Redis/CAP lane TRX identities do not equal the frozen member identities.' }
    if (-not $AllowInvalid -and -not $valid) { throw "Redis/CAP lane requires $($expected.Count) passed, 0 failed and 0 skipped; observed $passed passed, $failed failed and $skipped skipped." }
    return $result
}

function Assert-NervRedisCapTestLaneSummary {
    param(
        [Parameter(Mandatory)] [string[]] $SelectedMemberIds,
        [Parameter(Mandatory)] [object[]] $MemberSummaries
    )

    if ($MemberSummaries.Count -ne $SelectedMemberIds.Count) { throw "Redis/CAP lane selected $($SelectedMemberIds.Count) members but summarized $($MemberSummaries.Count)." }
    for ($index = 0; $index -lt $SelectedMemberIds.Count; $index++) {
        $selectedMemberId = $SelectedMemberIds[$index]
        $member = $MemberSummaries[$index]
        if (-not [string]::Equals([string]$member.memberId, $selectedMemberId, [StringComparison]::Ordinal)) { throw "Redis/CAP lane member at index $index must be '$selectedMemberId' but was '$($member.memberId)'." }
        if (-not [string]::Equals([string]$member.outcome, 'passed', [StringComparison]::Ordinal)) { throw "Redis/CAP lane member '$selectedMemberId' has outcome '$($member.outcome)'." }
        if (-not [string]::Equals([string]$member.cleanup, 'passed', [StringComparison]::Ordinal)) { throw "Redis/CAP lane member '$selectedMemberId' has cleanup '$($member.cleanup)'." }
        if ([string]$member.capVersion -cnotmatch '^[a-z][a-z0-9-]*-a[1-9][0-9]*-[0-9a-f]{8}$' -or ([string]$member.capVersion).Length -gt 20) { throw "Redis/CAP lane member '$selectedMemberId' has invalid CAP version '$($member.capVersion)'." }
        if ([string]$member.redisNamespace -cnotmatch '^nerv:n688:[0-9a-f]{16}:$') { throw "Redis/CAP lane member '$selectedMemberId' has invalid Redis namespace '$($member.redisNamespace)'." }
        if ([int]$member.expected -le 0 -or [int]$member.discovered -ne [int]$member.expected) { throw "Redis/CAP lane member '$selectedMemberId' expected $($member.expected) tests but discovered $($member.discovered)." }
        if ([int]$member.passed -ne [int]$member.expected -or [int]$member.failed -ne 0 -or [int]$member.skipped -ne 0) { throw "Redis/CAP lane member '$selectedMemberId' expected $($member.expected) passed, 0 failed and 0 skipped; observed $($member.passed) passed, $($member.failed) failed and $($member.skipped) skipped." }
    }
}
