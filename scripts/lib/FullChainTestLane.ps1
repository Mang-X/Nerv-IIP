# Script-Governance:
#   Category: library
#   SideEffects:
#     - Reads a FullChain lane manifest and VSTest TRX files supplied by the caller
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

function Import-NervFullChainTestLaneManifest {
    param(
        [Parameter(Mandatory)] [string] $ManifestPath,
        [Parameter(Mandatory)] [string] $RepositoryRoot
    )

    $manifest = Get-Content -LiteralPath (Resolve-Path $ManifestPath) -Raw | ConvertFrom-Json -Depth 20
    if ([int]$manifest.schemaVersion -ne 1) { throw "Unsupported FullChain lane manifest schemaVersion '$($manifest.schemaVersion)'." }
    $members = @($manifest.members)
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $identities = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $allowedKinds = [Collections.Generic.HashSet[string]]::new([string[]]@('fullstack', 'script', 'dotnet'), [StringComparer]::Ordinal)
    foreach ($member in $members) {
        $id = [string]$member.id
        if ($id -cnotmatch '^[a-z0-9]+(?:-[a-z0-9]+)*$' -or -not $ids.Add($id)) { throw "FullChain lane member id '$id' must be unique and canonical." }
        if (-not [string]::Equals([string]$member.tier, 'core', [StringComparison]::Ordinal) -or -not [string]::Equals([string]$member.status, 'active', [StringComparison]::Ordinal)) { throw "FullChain lane member '$id' must be active/core." }
        $project = [string]$member.project
        if ($project -cnotmatch '^backend/.+\.csproj$' -or -not (Test-Path -LiteralPath (Join-Path $RepositoryRoot $project) -PathType Leaf)) { throw "FullChain lane member '$id' project is missing or outside backend." }
        if ([string]::IsNullOrWhiteSpace([string]$member.filter) -or -not ([string]$member.filter).StartsWith('FullyQualifiedName=', [StringComparison]::Ordinal)) { throw "FullChain lane member '$id' must declare one exact FullyQualifiedName filter." }
        $memberIdentities = @($member.expectedTestIdentities | ForEach-Object { [string]$_ })
        if ($memberIdentities.Count -ne 1 -or [string]::IsNullOrWhiteSpace($memberIdentities[0]) -or -not $identities.Add($memberIdentities[0])) { throw "FullChain lane member '$id' must freeze exactly one globally unique identity." }
        if (-not [string]::Equals([string]$member.filter, "FullyQualifiedName=$($memberIdentities[0])", [StringComparison]::Ordinal)) { throw "FullChain lane member '$id' filter must equal its frozen identity." }
        $schemas = @($member.diagnosticSchemas | ForEach-Object { [string]$_ })
        $schemaSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        if ($schemas.Count -eq 0 -or @($schemas | Where-Object { $_ -cnotmatch '^[a-z][a-z0-9_]{0,62}$' -or -not $schemaSet.Add($_) }).Count -gt 0) { throw "FullChain lane member '$id' must declare unique canonical diagnosticSchemas." }
        $kind = [string]$member.entrypoint.kind
        if (-not $allowedKinds.Contains($kind)) { throw "FullChain lane member '$id' has invalid entrypoint kind '$kind'." }
        if ($kind -eq 'fullstack' -and [string]$member.entrypoint.scenario -cnotmatch '^man-[0-9]+$') { throw "FullChain lane member '$id' has an invalid fullstack scenario." }
        if ($kind -eq 'script') {
            $path = [string]$member.entrypoint.path
            if ($path -cnotmatch '^scripts/.+\.ps1$' -or -not (Test-Path -LiteralPath (Join-Path $RepositoryRoot $path) -PathType Leaf)) { throw "FullChain lane member '$id' script entrypoint is missing." }
        }
        if (-not [bool]$member.dependencies.postgres) { throw "FullChain lane member '$id' must require PostgreSQL." }
    }
    if ($members.Count -ne 5) { throw "FullChain lane manifest must contain exactly 5 active/core members; observed $($members.Count)." }
    return [pscustomobject]@{ schemaVersion = 1; members = $members }
}

function Get-NervFullChainTrxResult {
    param(
        [Parameter(Mandatory)] [string] $ResultsDirectory,
        [Parameter(Mandatory)] [string[]] $ExpectedTestIdentities,
        [switch] $AllowInvalid
    )

    $trxFiles = @(Get-ChildItem -LiteralPath $ResultsDirectory -Filter '*.trx' -File -Recurse)
    if ($trxFiles.Count -ne 1) { throw "FullChain lane member must produce exactly one TRX file; observed $($trxFiles.Count)." }
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
    $identitiesMatch = [string]::Equals(($observed -join "`n"), ($expected -join "`n"), [StringComparison]::Ordinal)
    $passed = @($results | Where-Object { [string]::Equals([string]$_.outcome, 'Passed', [StringComparison]::Ordinal) }).Count
    $failed = @($results | Where-Object { [string]::Equals([string]$_.outcome, 'Failed', [StringComparison]::Ordinal) }).Count
    $skipped = $results.Count - $passed - $failed
    $valid = $identitiesMatch -and $passed -eq $expected.Count -and $failed -eq 0 -and $skipped -eq 0
    $result = [pscustomobject]@{ total = $results.Count; passed = $passed; failed = $failed; skipped = $skipped; identities = @($observed); identitiesMatch = $identitiesMatch; valid = $valid }
    if (-not $AllowInvalid -and -not $identitiesMatch) { throw 'FullChain lane TRX identities do not equal the frozen member identities.' }
    if (-not $AllowInvalid -and -not $valid) { throw "FullChain lane requires $($expected.Count) passed, 0 failed and 0 skipped; observed $passed passed, $failed failed and $skipped skipped." }
    return $result
}

function Assert-NervFullChainTestLaneSummary {
    param(
        [Parameter(Mandatory)] [string[]] $SelectedMemberIds,
        [Parameter(Mandatory)] [object[]] $MemberSummaries
    )

    if ($MemberSummaries.Count -ne $SelectedMemberIds.Count) { throw "FullChain lane selected $($SelectedMemberIds.Count) members but summarized $($MemberSummaries.Count)." }
    for ($index = 0; $index -lt $SelectedMemberIds.Count; $index++) {
        $id = $SelectedMemberIds[$index]
        $member = $MemberSummaries[$index]
        if (-not [string]::Equals([string]$member.memberId, $id, [StringComparison]::Ordinal)) { throw "FullChain lane member at index $index must be '$id' but was '$($member.memberId)'." }
        if (-not [string]::Equals([string]$member.outcome, 'passed', [StringComparison]::Ordinal)) { throw "FullChain lane member '$id' has outcome '$($member.outcome)'." }
        if (-not [string]::Equals([string]$member.cleanup, 'passed', [StringComparison]::Ordinal)) { throw "FullChain lane member '$id' has cleanup '$($member.cleanup)'." }
        if (-not [string]::Equals([string]$member.dependencyEvidence, 'passed', [StringComparison]::Ordinal)) { throw "FullChain lane member '$id' has dependency evidence '$($member.dependencyEvidence)'." }
        if ([string]::IsNullOrWhiteSpace([string]$member.diagnosticEvidence)) { throw "FullChain lane member '$id' has no diagnostic evidence status." }
        if ([int]$member.expected -ne 1 -or [int]$member.discovered -ne 1) { throw "FullChain lane member '$id' expected 1 test but discovered $($member.discovered)." }
        if ([int]$member.passed -ne 1 -or [int]$member.failed -ne 0 -or [int]$member.skipped -ne 0) { throw "FullChain lane member '$id' expected 1 passed, 0 failed and 0 skipped; observed $($member.passed) passed, $($member.failed) failed and $($member.skipped) skipped." }
    }
}
