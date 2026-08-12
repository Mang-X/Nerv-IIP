# Script-Governance:
#   Category: check
#   SideEffects:
#     - Validates Redis/CAP lane manifest, TRX and summary contracts with temporary fixtures
#   Writes:
#     - Temporary TRX fixtures under the operating-system temp directory
#   Cleanup:
#     - Removes owned temporary fixtures in finally
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $repoRoot 'scripts/lib/RedisCapTestLane.ps1')

$manifestPath = Join-Path $repoRoot 'scripts/redis-cap-test-lane.json'
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-redis-cap-lane-$([Guid]::NewGuid().ToString('N'))"

function Assert-Contract([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function New-RedisCapTrx {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string[]] $Identities,
        [string] $Outcome = 'Passed'
    )

    $definitions = [Collections.Generic.List[string]]::new()
    $results = [Collections.Generic.List[string]]::new()
    for ($index = 0; $index -lt $Identities.Count; $index++) {
        $identity = $Identities[$index]
        $separatorIndex = $identity.LastIndexOf('.', [StringComparison]::Ordinal)
        $class = $identity.Substring(0, $separatorIndex)
        $method = $identity.Substring($separatorIndex + 1)
        $id = "test-$index"
        $definitions.Add("<UnitTest id=`"$id`"><TestMethod className=`"$class`" name=`"$method`" /></UnitTest>")
        $results.Add("<UnitTestResult testId=`"$id`" testName=`"$method`" outcome=`"$Outcome`" />")
    }
    $trx = "<?xml version=`"1.0`"?><TestRun xmlns=`"http://microsoft.com/schemas/VisualStudio/TeamTest/2010`"><Results>$($results -join '')</Results><TestDefinitions>$($definitions -join '')</TestDefinitions></TestRun>"
    [IO.File]::WriteAllText($Path, $trx, [Text.UTF8Encoding]::new($false))
}

try {
    [IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
    $member = Import-NervRedisCapTestLaneMember -ManifestPath $manifestPath -MemberId 'demandplanning-sales-order-redis-cap' -RepositoryRoot $repoRoot
    $expectedIdentities = @(
        'Nerv.IIP.Business.DemandPlanning.Web.Tests.ErpSalesOrderDemandConsumerTests.Redis_cap_fallback_scan_converges_changed_v2_after_immediate_retries_fail',
        'Nerv.IIP.Business.DemandPlanning.Web.Tests.ErpSalesOrderDemandConsumerTests.Redis_cap_transport_converges_duplicate_out_of_order_change_and_cancel_in_postgres'
    )
    Assert-Contract ([string]::Equals([string]$member.service, 'DemandPlanning', [StringComparison]::Ordinal)) 'The Redis/CAP pilot must register DemandPlanning as its owning service.'
    Assert-Contract ([string]::Equals([string]$member.tier, 'core', [StringComparison]::Ordinal) -and [string]::Equals([string]$member.status, 'active', [StringComparison]::Ordinal)) 'The Redis/CAP pilot must be active/core.'
    Assert-Contract ([string]::Equals([string]$member.project, 'backend/services/Business/DemandPlanning/tests/Nerv.IIP.Business.DemandPlanning.Web.Tests/Nerv.IIP.Business.DemandPlanning.Web.Tests.csproj', [StringComparison]::Ordinal)) 'The Redis/CAP pilot must target the DemandPlanning Web test project.'
    Assert-Contract ([string]::Equals((@($member.expectedTestIdentities) -join "`n"), ($expectedIdentities -join "`n"), [StringComparison]::Ordinal)) 'The Redis/CAP pilot must freeze exactly the two transport identities in ordinal order.'
    Assert-Contract ([string]::Equals((@($member.diagnosticSchemas) -join '|'), 'demand_planning|cap', [StringComparison]::Ordinal)) 'The Redis/CAP pilot must restrict PostgreSQL diagnostics to the production demand_planning and CAP schemas.'

    $attempt1Identity = New-NervRedisCapMemberIdentity `
        -MemberId 'demandplanning-sales-order-redis-cap' `
        -CapVersionPrefix 'n688-dp' `
        -DatabaseSuffix '31617004968_1'
    $attempt2Identity = New-NervRedisCapMemberIdentity `
        -MemberId 'demandplanning-sales-order-redis-cap' `
        -CapVersionPrefix 'n688-dp' `
        -DatabaseSuffix '31617004968_2'
    Assert-Contract ([string]::Equals([string]$attempt1Identity.capVersion, 'n688-dp-a1-4a87964a', [StringComparison]::Ordinal)) 'Attempt 1 CAP version must retain the explicit attempt and a hash of the complete member/run/attempt input.'
    Assert-Contract ([string]::Equals([string]$attempt2Identity.capVersion, 'n688-dp-a2-0e3b4c73', [StringComparison]::Ordinal)) 'Attempt 2 CAP version must retain the explicit attempt and a hash of the complete member/run/attempt input.'
    Assert-Contract ($attempt1Identity.capVersion.Length -le 20 -and $attempt2Identity.capVersion.Length -le 20) 'Derived CAP versions must remain within CAP group version limits.'
    Assert-Contract (-not [string]::Equals([string]$attempt1Identity.capVersion, [string]$attempt2Identity.capVersion, [StringComparison]::Ordinal)) 'Rerun attempts for one real run id must not collide in CAP version.'
    Assert-Contract ([string]::Equals([string]$attempt1Identity.redisNamespace, 'nerv:n688:4a87964a1f645a47:', [StringComparison]::Ordinal)) 'Attempt 1 Redis namespace must be the frozen digest-derived member/run/attempt namespace.'
    Assert-Contract ([string]::Equals([string]$attempt2Identity.redisNamespace, 'nerv:n688:0e3b4c733bec65b3:', [StringComparison]::Ordinal)) 'Attempt 2 Redis namespace must be independently derived from the complete input.'

    $redisKeys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    [void]$redisKeys.Add('preexisting:shared-stream')
    $enumerateNamespace = {
        param([string] $namespace)
        return @($redisKeys | Where-Object { $_.StartsWith($namespace, [StringComparison]::Ordinal) })
    }.GetNewClosure()
    $removeNamespaceKey = {
        param([string] $key)
        if (-not $redisKeys.Remove($key)) { throw "Key '$key' was not present for removal." }
    }.GetNewClosure()
    $claim = New-NervRedisCapNamespaceClaim -Namespace $attempt1Identity.redisNamespace -EnumerateKeys $enumerateNamespace
    [void]$redisKeys.Add('concurrent:other-run-stream')
    [void]$redisKeys.Add("$($attempt1Identity.redisNamespace)SalesOrderChangedIntegrationEvent")
    Remove-NervRedisCapNamespace -Claim $claim -EnumerateKeys $enumerateNamespace -RemoveKey $removeNamespaceKey
    Assert-Contract ($redisKeys.Contains('preexisting:shared-stream')) 'Namespace cleanup must preserve a pre-existing shared stream outside the claimed namespace.'
    Assert-Contract ($redisKeys.Contains('concurrent:other-run-stream')) 'Namespace cleanup must preserve a key concurrently created by another run.'
    Assert-Contract (-not $redisKeys.Contains("$($attempt1Identity.redisNamespace)SalesOrderChangedIntegrationEvent")) 'Namespace cleanup must remove the stream created inside the claimed namespace.'

    [void]$redisKeys.Add("$($attempt2Identity.redisNamespace)existing-stream")
    $existingNamespaceRejected = $false
    try { New-NervRedisCapNamespaceClaim -Namespace $attempt2Identity.redisNamespace -EnumerateKeys $enumerateNamespace | Out-Null }
    catch { $existingNamespaceRejected = $_.Exception.Message.Contains('is not empty', [StringComparison]::Ordinal) }
    Assert-Contract $existingNamespaceRejected 'A pre-existing stream in the exact namespace must reject the claim rather than being appended to or deleted.'

    $beforeScanRejected = $false
    try { New-NervRedisCapNamespaceClaim -Namespace 'nerv:n688:before-failure:' -EnumerateKeys { throw 'before scan failed' } | Out-Null }
    catch { $beforeScanRejected = $_.Exception.Message.Contains('before scan failed', [StringComparison]::Ordinal) }
    Assert-Contract $beforeScanRejected 'A namespace claim scan failure must fail closed.'

    $afterScanState = [pscustomobject]@{ calls = 0 }
    $afterScan = {
        param([string] $namespace)
        $afterScanState.calls++
        if ($afterScanState.calls -eq 1) { return @() }
        throw 'after scan failed'
    }.GetNewClosure()
    $afterScanClaim = New-NervRedisCapNamespaceClaim -Namespace 'nerv:n688:after-failure:' -EnumerateKeys $afterScan
    $afterScanRejected = $false
    try { Remove-NervRedisCapNamespace -Claim $afterScanClaim -EnumerateKeys $afterScan -RemoveKey { param([string] $key) } }
    catch { $afterScanRejected = $_.Exception.Message.Contains('after scan failed', [StringComparison]::Ordinal) }
    Assert-Contract $afterScanRejected 'A post-execution namespace enumeration failure must make cleanup fail.'

    $verificationKeys = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $verificationState = [pscustomobject]@{ calls = 0 }
    $verificationScan = {
        param([string] $namespace)
        $verificationState.calls++
        if ($verificationState.calls -eq 1) { return @() }
        if ($verificationState.calls -eq 2) { return @($verificationKeys) }
        throw 'cleanup verification scan failed'
    }.GetNewClosure()
    $verificationClaim = New-NervRedisCapNamespaceClaim -Namespace 'nerv:n688:verify-failure:' -EnumerateKeys $verificationScan
    [void]$verificationKeys.Add('nerv:n688:verify-failure:stream')
    $verificationRejected = $false
    try {
        Remove-NervRedisCapNamespace -Claim $verificationClaim -EnumerateKeys $verificationScan -RemoveKey {
            param([string] $key)
            [void]$verificationKeys.Remove($key)
        }.GetNewClosure()
    }
    catch { $verificationRejected = $_.Exception.Message.Contains('cleanup verification scan failed', [StringComparison]::Ordinal) }
    Assert-Contract $verificationRejected 'A cleanup verification scan failure must fail closed even after owned keys were removed.'

    $manifest = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json -Depth 20
    $manifest.members[0].expectedTestIdentities = @($manifest.members[0].expectedTestIdentities | Select-Object -First 1)
    $missingIdentityPath = Join-Path $fixtureRoot 'missing-identity.json'
    [IO.File]::WriteAllText($missingIdentityPath, (($manifest | ConvertTo-Json -Depth 20) + "`n"), [Text.UTF8Encoding]::new($false))
    $missingIdentity = Import-NervRedisCapTestLaneMember -ManifestPath $missingIdentityPath -MemberId 'demandplanning-sales-order-redis-cap' -RepositoryRoot $repoRoot
    Assert-Contract (@($missingIdentity.expectedTestIdentities).Count -eq 1) 'The missing-identity fixture must remove one governed test.'
    Assert-Contract (-not [string]::Equals((@($missingIdentity.expectedTestIdentities) -join "`n"), ($expectedIdentities -join "`n"), [StringComparison]::Ordinal)) 'Removing a frozen Redis/CAP identity must fail the pilot contract.'

    $trxPath = Join-Path $fixtureRoot 'redis-cap.trx'
    New-RedisCapTrx -Path $trxPath -Identities $expectedIdentities
    $trxResult = Get-NervRedisCapTrxResult -ResultsDirectory $fixtureRoot -ExpectedTestIdentities $expectedIdentities
    Assert-Contract ($trxResult.total -eq 2 -and $trxResult.passed -eq 2 -and $trxResult.failed -eq 0 -and $trxResult.skipped -eq 0) 'Two passed frozen identities must satisfy the Redis/CAP TRX contract.'

    New-RedisCapTrx -Path $trxPath -Identities $expectedIdentities -Outcome 'NotExecuted'
    $skippedResult = Get-NervRedisCapTrxResult -ResultsDirectory $fixtureRoot -ExpectedTestIdentities $expectedIdentities -AllowInvalid
    Assert-Contract (-not $skippedResult.valid -and $skippedResult.skipped -eq 2) 'All skipped Redis/CAP tests must remain visible and invalid.'
    $skipRejected = $false
    try { Get-NervRedisCapTrxResult -ResultsDirectory $fixtureRoot -ExpectedTestIdentities $expectedIdentities | Out-Null }
    catch { $skipRejected = $_.Exception.Message.Contains('0 failed and 0 skipped', [StringComparison]::Ordinal) }
    Assert-Contract $skipRejected 'The strict Redis/CAP TRX contract must reject all-skipped execution.'

    New-RedisCapTrx -Path $trxPath -Identities @($expectedIdentities[0])
    $missingTrxIdentity = Get-NervRedisCapTrxResult -ResultsDirectory $fixtureRoot -ExpectedTestIdentities $expectedIdentities -AllowInvalid
    Assert-Contract (-not $missingTrxIdentity.identitiesMatch) 'A partial Redis/CAP TRX must not satisfy the frozen identity set.'

    $passedSummary = [pscustomobject]@{
        memberId = 'demandplanning-sales-order-redis-cap'
        capVersion = $attempt1Identity.capVersion
        redisNamespace = $attempt1Identity.redisNamespace
        outcome = 'passed'
        cleanup = 'passed'
        expected = 2
        discovered = 2
        passed = 2
        failed = 0
        skipped = 0
    }
    Assert-NervRedisCapTestLaneSummary -SelectedMemberIds @('demandplanning-sales-order-redis-cap') -MemberSummaries @($passedSummary)
    $cleanupRejected = $false
    try {
        Assert-NervRedisCapTestLaneSummary -SelectedMemberIds @('demandplanning-sales-order-redis-cap') -MemberSummaries @(
            [pscustomobject]@{
                memberId = 'demandplanning-sales-order-redis-cap'
                capVersion = $attempt1Identity.capVersion
                redisNamespace = $attempt1Identity.redisNamespace
                outcome = 'passed'
                cleanup = 'failed'
                expected = 2
                discovered = 2
                passed = 2
                failed = 0
                skipped = 0
            })
    }
    catch { $cleanupRejected = $_.Exception.Message.Contains("cleanup 'failed'", [StringComparison]::Ordinal) }
    Assert-Contract $cleanupRejected 'Redis/CAP cleanup failure must make the lane summary contract fail.'
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
}

Write-Output 'Redis/CAP test lane contract tests passed.'
