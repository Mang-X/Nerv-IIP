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
    Assert-Contract ([string]::Equals((@($member.diagnosticSchemas) -join '|'), 'business_demand_planning|cap', [StringComparison]::Ordinal)) 'The Redis/CAP pilot must restrict PostgreSQL diagnostics to business_demand_planning and cap.'

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
