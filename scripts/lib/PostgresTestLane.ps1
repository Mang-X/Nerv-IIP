# Script-Governance:
#   Category: library
#   SideEffects:
#     - Reads a PostgreSQL lane manifest and VSTest TRX files supplied by the caller
#   Writes:
#     - None
#   Cleanup:
#     - None
#   Requires:
#     - PowerShell 7

function Import-NervPostgresTestLaneMember {
    param(
        [Parameter(Mandatory)] [string] $ManifestPath,
        [Parameter(Mandatory)] [string] $MemberId,
        [Parameter(Mandatory)] [string] $RepositoryRoot
    )

    $manifest = Get-Content -LiteralPath (Resolve-Path $ManifestPath) -Raw | ConvertFrom-Json -Depth 20
    if ([int]$manifest.schemaVersion -ne 1) { throw "Unsupported PostgreSQL lane manifest schemaVersion '$($manifest.schemaVersion)'." }
    $members = @($manifest.members)
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($member in $members) {
        $id = [string]$member.id
        if ($id -cnotmatch '^[a-z0-9]+(?:-[a-z0-9]+)*$' -or -not $ids.Add($id)) { throw "PostgreSQL lane member id '$id' must be unique and canonical." }
        $tiers = [Collections.Generic.HashSet[string]]::new([string[]]@('core', 'extended'), [StringComparer]::Ordinal)
        $statuses = [Collections.Generic.HashSet[string]]::new([string[]]@('active', 'deferred', 'blocked'), [StringComparer]::Ordinal)
        if (-not $tiers.Contains([string]$member.tier)) { throw "PostgreSQL lane member '$id' has an invalid tier." }
        if (-not $statuses.Contains([string]$member.status)) { throw "PostgreSQL lane member '$id' has an invalid status." }
        # NERV-688：成员数据库的归属必须显式声明，因为它决定 lane 能证明什么。
        #   runner     = runner 建成员数据库并注入，失败诊断与清理由 lane 证明；
        #   test-owned = 用例用受治理的 PostgreSqlTestDatabase 自建临时库（自身生命周期由
        #                Nerv.IIP.Testing.PostgreSql.Tests 证明），lane 只证明执行数与冻结身份，
        #                成员数据库在失败时是空的，诊断能力受限。
        $ownerships = [Collections.Generic.HashSet[string]]::new([string[]]@('runner', 'test-owned'), [StringComparer]::Ordinal)
        if (-not $ownerships.Contains([string]$member.databaseOwnership)) { throw "PostgreSQL lane member '$id' must declare databaseOwnership as runner or test-owned." }
        if ([string]$member.databasePrefix -cnotmatch '^[a-z][a-z0-9_]{0,39}$') { throw "PostgreSQL lane member '$id' has an invalid databasePrefix." }
        $diagnosticSchemas = @($member.diagnosticSchemas | ForEach-Object { [string]$_ })
        $diagnosticSchemaSet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        if ($diagnosticSchemas.Count -eq 0 -or @($diagnosticSchemas | Where-Object { $_ -cnotmatch '^[a-z][a-z0-9_]{0,62}$' -or -not $diagnosticSchemaSet.Add($_) }).Count -gt 0) { throw "PostgreSQL lane member '$id' must declare unique canonical diagnosticSchemas." }
        $project = [string]$member.project
        if ($project -cnotmatch '^backend/.+\.csproj$' -or -not (Test-Path -LiteralPath (Join-Path $RepositoryRoot $project) -PathType Leaf)) { throw "PostgreSQL lane member '$id' project is missing or outside backend." }
        $identities = @($member.expectedTestIdentities | ForEach-Object { [string]$_ })
        $identitySet = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        if ($identities.Count -eq 0 -or @($identities | Where-Object { [string]::IsNullOrWhiteSpace($_) -or -not $identitySet.Add($_) }).Count -gt 0) { throw "PostgreSQL lane member '$id' must freeze a non-empty unique test identity set." }
    }

    $matches = @($members | Where-Object { [string]::Equals([string]$_.id, $MemberId, [StringComparison]::Ordinal) })
    if ($matches.Count -ne 1) { throw "PostgreSQL lane member '$MemberId' must resolve exactly once." }
    if (-not [string]::Equals([string]$matches[0].status, 'active', [StringComparison]::Ordinal)) { throw "PostgreSQL lane member '$MemberId' is not active." }
    return $matches[0]
}

function Get-NervPostgresTrxResult {
    param(
        [Parameter(Mandatory)] [string] $ResultsDirectory,
        [Parameter(Mandatory)] [string[]] $ExpectedTestIdentities,
        [switch] $AllowInvalid
    )

    $trxFiles = @(Get-ChildItem -LiteralPath $ResultsDirectory -Filter '*.trx' -File -Recurse)
    if ($trxFiles.Count -ne 1) { throw "PostgreSQL lane must produce exactly one TRX file; observed $($trxFiles.Count)." }
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
    if (-not $AllowInvalid -and -not $identitiesMatch) { throw 'PostgreSQL lane TRX identities do not equal the frozen member identities.' }
    if (-not $AllowInvalid -and -not $valid) { throw "PostgreSQL lane requires $($expected.Count) passed, 0 failed and 0 skipped; observed $passed passed, $failed failed and $skipped skipped." }
    return $result
}

function Assert-NervPostgresTestLaneSummary {
    param(
        [Parameter(Mandatory)] [string[]] $SelectedMemberIds,
        [Parameter(Mandatory)] [object[]] $MemberSummaries
    )

    if ($MemberSummaries.Count -ne $SelectedMemberIds.Count) {
        throw "PostgreSQL lane selected $($SelectedMemberIds.Count) members but summarized $($MemberSummaries.Count)."
    }
    for ($index = 0; $index -lt $SelectedMemberIds.Count; $index++) {
        $selectedMemberId = $SelectedMemberIds[$index]
        $member = $MemberSummaries[$index]
        if (-not [string]::Equals([string]$member.memberId, $selectedMemberId, [StringComparison]::Ordinal)) {
            throw "PostgreSQL lane member at index $index must be '$selectedMemberId' but was '$($member.memberId)'."
        }
        if (-not [string]::Equals([string]$member.outcome, 'passed', [StringComparison]::Ordinal)) {
            throw "PostgreSQL lane member '$selectedMemberId' has outcome '$($member.outcome)'."
        }
        if (-not [string]::Equals([string]$member.cleanup, 'passed', [StringComparison]::Ordinal)) {
            throw "PostgreSQL lane member '$selectedMemberId' has cleanup '$($member.cleanup)'."
        }
        if ([int]$member.expected -le 0 -or [int]$member.discovered -ne [int]$member.expected) {
            throw "PostgreSQL lane member '$selectedMemberId' expected $($member.expected) tests but discovered $($member.discovered)."
        }
        if ([int]$member.passed -ne [int]$member.expected -or [int]$member.failed -ne 0 -or [int]$member.skipped -ne 0) {
            throw "PostgreSQL lane member '$selectedMemberId' expected $($member.expected) passed, 0 failed and 0 skipped; observed $($member.passed) passed, $($member.failed) failed and $($member.skipped) skipped."
        }
    }
}
