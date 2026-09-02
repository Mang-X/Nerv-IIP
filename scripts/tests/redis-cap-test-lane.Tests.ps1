# Script-Governance:
#   Category: check
#   SideEffects:
#     - Validates Redis/CAP lane manifest, runner, TRX and summary contracts with temporary fixtures and fake dependency commands
#   Writes:
#     - Temporary command, manifest, TRX and summary fixtures under the operating-system temp directory
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

function New-FakeRedisCapRunnerCommands {
    param([Parameter(Mandatory)] [string] $Directory)

    [IO.Directory]::CreateDirectory($Directory) | Out-Null
    $commandScript = @'
$commandName = if ([string]::IsNullOrWhiteSpace($env:NERV_FAKE_COMMAND_NAME)) {
    [IO.Path]::GetFileNameWithoutExtension($PSCommandPath)
}
else {
    $env:NERV_FAKE_COMMAND_NAME
}

switch ($commandName) {
    'psql' {
        if (@($args | Where-Object { $_.Contains('server_version', [StringComparison]::Ordinal) }).Count -gt 0) {
            Write-Output '18.6'
        }
        exit 0
    }
    'redis-cli' {
        if (@($args | Where-Object { [string]::Equals([string]$_, 'PING', [StringComparison]::Ordinal) }).Count -gt 0) {
            Write-Output 'PONG'
        }
        elseif (@($args | Where-Object { [string]::Equals([string]$_, 'INFO', [StringComparison]::Ordinal) }).Count -gt 0) {
            Write-Output 'redis_version:8.10.1'
        }
        exit 0
    }
    'dotnet' {
        $identities = @(
            'Nerv.IIP.Business.DemandPlanning.Web.Tests.ErpSalesOrderDemandConsumerTests.Redis_cap_fallback_scan_converges_changed_v2_after_immediate_retries_fail'
            'Nerv.IIP.Business.DemandPlanning.Web.Tests.ErpSalesOrderDemandConsumerTests.Redis_cap_transport_converges_duplicate_out_of_order_change_and_cancel_in_postgres'
        )
        if (@($args | Where-Object { [string]::Equals([string]$_, '--list-tests', [StringComparison]::Ordinal) }).Count -gt 0) {
            $identities | Write-Output
            exit 0
        }

        $resultsDirectoryIndex = [Array]::IndexOf([object[]]$args, '--results-directory')
        if ($resultsDirectoryIndex -lt 0 -or $resultsDirectoryIndex + 1 -ge $args.Count) { throw 'Fake dotnet execution requires --results-directory.' }
        $resultsDirectory = [string]$args[$resultsDirectoryIndex + 1]
        [IO.Directory]::CreateDirectory($resultsDirectory) | Out-Null
        $trx = '<?xml version="1.0"?><TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010"><Results><UnitTestResult testId="test-0" testName="Redis_cap_fallback_scan_converges_changed_v2_after_immediate_retries_fail" outcome="Passed" /><UnitTestResult testId="test-1" testName="Redis_cap_transport_converges_duplicate_out_of_order_change_and_cancel_in_postgres" outcome="Passed" /></Results><TestDefinitions><UnitTest id="test-0"><TestMethod className="Nerv.IIP.Business.DemandPlanning.Web.Tests.ErpSalesOrderDemandConsumerTests" name="Redis_cap_fallback_scan_converges_changed_v2_after_immediate_retries_fail" /></UnitTest><UnitTest id="test-1"><TestMethod className="Nerv.IIP.Business.DemandPlanning.Web.Tests.ErpSalesOrderDemandConsumerTests" name="Redis_cap_transport_converges_duplicate_out_of_order_change_and_cancel_in_postgres" /></UnitTest></TestDefinitions></TestRun>'
        [IO.File]::WriteAllText((Join-Path $resultsDirectory 'fake.trx'), $trx, [Text.UTF8Encoding]::new($false))
        exit 0
    }
    default { throw "Unexpected fake Redis/CAP runner command '$commandName'." }
}
'@

    if ($IsWindows) {
        $commandScriptPath = Join-Path $Directory 'fake-redis-cap-command.ps1'
        [IO.File]::WriteAllText($commandScriptPath, $commandScript, [Text.UTF8Encoding]::new($false))
        foreach ($commandName in @('dotnet', 'psql', 'redis-cli')) {
            $wrapper = "@echo off`r`nset NERV_FAKE_COMMAND_NAME=$commandName`r`npwsh -NoProfile -File `"%~dp0fake-redis-cap-command.ps1`" %*`r`nexit /b %ERRORLEVEL%`r`n"
            [IO.File]::WriteAllText((Join-Path $Directory "$commandName.cmd"), $wrapper, [Text.UTF8Encoding]::new($false))
        }
        return
    }

    $unixMode = [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite -bor [IO.UnixFileMode]::UserExecute
    foreach ($commandName in @('dotnet', 'psql', 'redis-cli')) {
        $commandPath = Join-Path $Directory $commandName
        [IO.File]::WriteAllText($commandPath, ("#!/usr/bin/env pwsh`n`$env:NERV_FAKE_COMMAND_NAME = '$commandName'`n" + $commandScript), [Text.UTF8Encoding]::new($false))
        [IO.File]::SetUnixFileMode($commandPath, $unixMode)
    }
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

    $erpMember = Import-NervRedisCapTestLaneMember -ManifestPath $manifestPath -MemberId 'erp-operation-labor-redis-cap' -RepositoryRoot $repoRoot
    $erpIdentity = 'Nerv.IIP.Business.Erp.Web.Tests.OperationLaborSettlementRedisCapTransportTests.Redis_cap_transport_converges_settle_void_redelivery_and_out_of_order_revisions_in_postgres'
    Assert-Contract ([string]::Equals([string]$erpMember.service, 'Erp', [StringComparison]::Ordinal)) 'The ERP labor-cost Redis/CAP member must be owned by ERP.'
    Assert-Contract ([string]::Equals([string]$erpMember.tier, 'core', [StringComparison]::Ordinal) -and [string]::Equals([string]$erpMember.status, 'active', [StringComparison]::Ordinal)) 'The ERP labor-cost Redis/CAP member must be active/core.'
    Assert-Contract ([string]::Equals([string]$erpMember.project, 'backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/Nerv.IIP.Business.Erp.Web.Tests.csproj', [StringComparison]::Ordinal)) 'The ERP labor-cost Redis/CAP member must target the ERP Web test project.'
    Assert-Contract ([string]::Equals((@($erpMember.expectedTestIdentities) -join "`n"), $erpIdentity, [StringComparison]::Ordinal)) 'The ERP labor-cost Redis/CAP member must freeze its transport identity.'
    Assert-Contract ([string]::Equals((@($erpMember.diagnosticSchemas) -join '|'), 'erp|cap', [StringComparison]::Ordinal)) 'The ERP labor-cost Redis/CAP member must restrict diagnostics to ERP and CAP schemas.'

    $qualityMember = Import-NervRedisCapTestLaneMember -ManifestPath $manifestPath -MemberId 'quality-rework-receipt-redis-cap' -RepositoryRoot $repoRoot
    $qualityIdentity = 'Nerv.IIP.Business.Quality.Web.Tests.QualityReworkReceiptRedisCapTransportTests.Redis_cap_transport_recovers_replayed_receipt_and_preserves_the_first_system_binding_in_postgres'
    Assert-Contract ([string]::Equals([string]$qualityMember.service, 'Quality', [StringComparison]::Ordinal)) 'The Quality rework-receipt Redis/CAP member must be owned by Quality.'
    Assert-Contract ([string]::Equals([string]$qualityMember.tier, 'core', [StringComparison]::Ordinal) -and [string]::Equals([string]$qualityMember.status, 'active', [StringComparison]::Ordinal)) 'The Quality rework-receipt Redis/CAP member must be active/core.'
    Assert-Contract ([string]::Equals([string]$qualityMember.project, 'backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Web.Tests/Nerv.IIP.Business.Quality.Web.Tests.csproj', [StringComparison]::Ordinal)) 'The Quality rework-receipt Redis/CAP member must target the Quality Web test project.'
    Assert-Contract ([string]::Equals((@($qualityMember.expectedTestIdentities) -join "`n"), $qualityIdentity, [StringComparison]::Ordinal)) 'The Quality rework-receipt Redis/CAP member must freeze its transport identity.'
    Assert-Contract ([string]::Equals((@($qualityMember.diagnosticSchemas) -join '|'), 'quality|cap', [StringComparison]::Ordinal)) 'The Quality rework-receipt Redis/CAP member must restrict diagnostics to Quality and CAP schemas.'

    $activeManifest = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json -Depth 20
    $activeSentinel = $activeManifest.members[0].PSObject.Copy()
    $activeSentinel.id = 'active-sentinel-redis-cap'
    $activeManifest.members = @($activeManifest.members) + @($activeSentinel)
    $activeManifestPath = Join-Path $fixtureRoot 'active-sentinel.json'
    [IO.File]::WriteAllText($activeManifestPath, (($activeManifest | ConvertTo-Json -Depth 20) + "`n"), [Text.UTF8Encoding]::new($false))
    $activeMembers = @(Import-NervRedisCapTestLaneMembers -ManifestPath $activeManifestPath -RepositoryRoot $repoRoot)
    Assert-Contract ([string]::Equals((@($activeMembers.id) -join '|'), 'demandplanning-sales-order-redis-cap|erp-operation-labor-redis-cap|quality-rework-receipt-redis-cap|active-sentinel-redis-cap', [StringComparison]::Ordinal)) 'All-active resolution must include a newly registered active member in manifest order without a caller-owned member list.'

    $activeManifest.members[-1].status = 'deferred'
    $deferredSentinelPath = Join-Path $fixtureRoot 'deferred-sentinel.json'
    [IO.File]::WriteAllText($deferredSentinelPath, (($activeManifest | ConvertTo-Json -Depth 20) + "`n"), [Text.UTF8Encoding]::new($false))
    $membersWithoutDeferred = @(Import-NervRedisCapTestLaneMembers -ManifestPath $deferredSentinelPath -RepositoryRoot $repoRoot)
    Assert-Contract (-not [Linq.Enumerable]::Contains([string[]]@($membersWithoutDeferred.id), 'active-sentinel-redis-cap', [StringComparer]::Ordinal)) 'All-active resolution must exclude a member after its manifest status becomes deferred.'

    foreach ($manifestMember in $activeManifest.members) { $manifestMember.status = 'deferred' }
    $zeroActiveManifestPath = Join-Path $fixtureRoot 'zero-active.json'
    [IO.File]::WriteAllText($zeroActiveManifestPath, (($activeManifest | ConvertTo-Json -Depth 20) + "`n"), [Text.UTF8Encoding]::new($false))
    $zeroActiveRejected = $false
    try {
        & (Join-Path $repoRoot 'scripts/run-redis-cap-test-lane.ps1') `
            -AllActiveMembers `
            -ManifestPath $zeroActiveManifestPath `
            -DatabaseSuffix '3026_1' `
            -ResultsDirectory (Join-Path $fixtureRoot 'zero-active-results') `
            -SummaryPath (Join-Path $fixtureRoot 'zero-active-summary.json')
    }
    catch { $zeroActiveRejected = $_.Exception.Message.Contains('does not contain any active members', [StringComparison]::Ordinal) }
    Assert-Contract $zeroActiveRejected 'All-active execution must reject an empty active set before dependency readiness is evaluated.'

    $savedTestPostgres = [Environment]::GetEnvironmentVariable('NERV_IIP_TEST_POSTGRES')
    $savedTestRedis = [Environment]::GetEnvironmentVariable('NERV_IIP_TEST_REDIS')
    try {
        [Environment]::SetEnvironmentVariable('NERV_IIP_TEST_POSTGRES', $null)
        [Environment]::SetEnvironmentVariable('NERV_IIP_TEST_REDIS', $null)
        $focusedMemberReachedReadiness = $false
        try {
            & (Join-Path $repoRoot 'scripts/run-redis-cap-test-lane.ps1') `
                -MemberId 'demandplanning-sales-order-redis-cap' `
                -DatabaseSuffix '3026_1' `
                -ResultsDirectory (Join-Path $fixtureRoot 'focused-results') `
                -SummaryPath (Join-Path $fixtureRoot 'focused-summary.json')
        }
        catch { $focusedMemberReachedReadiness = $_.Exception.Message.Contains('Set NERV_IIP_TEST_POSTGRES', [StringComparison]::Ordinal) }
        Assert-Contract $focusedMemberReachedReadiness 'The explicit -MemberId entrypoint must remain available for local focused execution.'
    }
    finally {
        [Environment]::SetEnvironmentVariable('NERV_IIP_TEST_POSTGRES', $savedTestPostgres)
        [Environment]::SetEnvironmentVariable('NERV_IIP_TEST_REDIS', $savedTestRedis)
    }

    $mixedSelectionRejected = $false
    try {
        & (Join-Path $repoRoot 'scripts/run-redis-cap-test-lane.ps1') `
            -MemberId 'demandplanning-sales-order-redis-cap' `
            -AllActiveMembers `
            -DatabaseSuffix '3026_1' `
            -ResultsDirectory (Join-Path $fixtureRoot 'mixed-results') `
            -SummaryPath (Join-Path $fixtureRoot 'mixed-summary.json')
    }
    catch { $mixedSelectionRejected = $_.Exception.Message.Contains('Parameter set cannot be resolved', [StringComparison]::Ordinal) }
    Assert-Contract $mixedSelectionRejected '-MemberId and -AllActiveMembers must be mutually exclusive runner entrypoints.'

    $runnerSentinel = $activeSentinel.PSObject.Copy()
    $runnerSentinel.status = 'active'
    $runnerSentinelManifest = [pscustomobject]@{
        schemaVersion = 1
        members = @($runnerSentinel)
    }
    $runnerSentinelManifestPath = Join-Path $fixtureRoot 'runner-active-sentinel.json'
    [IO.File]::WriteAllText($runnerSentinelManifestPath, (($runnerSentinelManifest | ConvertTo-Json -Depth 20) + "`n"), [Text.UTF8Encoding]::new($false))
    $fakeCommandDirectory = Join-Path $fixtureRoot 'fake-runner-commands'
    New-FakeRedisCapRunnerCommands -Directory $fakeCommandDirectory
    $runnerResultsDirectory = Join-Path $fixtureRoot 'runner-active-results'
    $runnerSummaryPath = Join-Path $fixtureRoot 'runner-active-summary.json'
    $savedPath = [Environment]::GetEnvironmentVariable('PATH')
    $savedTestPostgres = [Environment]::GetEnvironmentVariable('NERV_IIP_TEST_POSTGRES')
    $savedTestRedis = [Environment]::GetEnvironmentVariable('NERV_IIP_TEST_REDIS')
    try {
        [Environment]::SetEnvironmentVariable('PATH', "$fakeCommandDirectory$([IO.Path]::PathSeparator)$savedPath")
        [Environment]::SetEnvironmentVariable('NERV_IIP_TEST_POSTGRES', 'Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=fake')
        [Environment]::SetEnvironmentVariable('NERV_IIP_TEST_REDIS', 'localhost:6379')
        & (Join-Path $repoRoot 'scripts/run-redis-cap-test-lane.ps1') `
            -AllActiveMembers `
            -ManifestPath $runnerSentinelManifestPath `
            -DatabaseSuffix '3026_1' `
            -ResultsDirectory $runnerResultsDirectory `
            -SummaryPath $runnerSummaryPath
    }
    finally {
        [Environment]::SetEnvironmentVariable('PATH', $savedPath)
        [Environment]::SetEnvironmentVariable('NERV_IIP_TEST_POSTGRES', $savedTestPostgres)
        [Environment]::SetEnvironmentVariable('NERV_IIP_TEST_REDIS', $savedTestRedis)
    }
    $runnerSummary = [IO.File]::ReadAllText($runnerSummaryPath) | ConvertFrom-Json -Depth 20
    Assert-Contract ([string]::Equals((@($runnerSummary.selectedMemberIds) -join '|'), 'active-sentinel-redis-cap', [StringComparison]::Ordinal)) 'The runner must expose the active sentinel as its actual selected member set.'
    Assert-Contract ($runnerSummary.members.Count -eq 1 -and [string]::Equals([string]$runnerSummary.members[0].memberId, 'active-sentinel-redis-cap', [StringComparison]::Ordinal)) 'The runner must execute and summarize the member returned by all-active manifest resolution.'
    Assert-Contract ($runnerSummary.expected -eq 2 -and $runnerSummary.discovered -eq 2 -and $runnerSummary.passed -eq 2 -and $runnerSummary.failed -eq 0 -and $runnerSummary.skipped -eq 0 -and [string]::Equals([string]$runnerSummary.cleanup, 'passed', [StringComparison]::Ordinal)) 'The runner sentinel fixture must close discovery, execution and cleanup for the resolved member.'
    Remove-Item -LiteralPath $runnerResultsDirectory -Recurse -Force

    $runnerContent = [IO.File]::ReadAllText((Join-Path $repoRoot 'scripts/run-redis-cap-test-lane.ps1'))
    Assert-Contract (-not $runnerContent.Contains('}.GetNewClosure()', [StringComparison]::Ordinal)) 'Runner callbacks must retain the runner script session state so hosted PowerShell can resolve Get-RedisKeys and Invoke-RedisCli.'

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
    Assert-Contract ([Linq.Enumerable]::Contains($redisKeys, 'preexisting:shared-stream', [StringComparer]::Ordinal)) 'Namespace cleanup must preserve a pre-existing shared stream outside the claimed namespace.'
    Assert-Contract ([Linq.Enumerable]::Contains($redisKeys, 'concurrent:other-run-stream', [StringComparer]::Ordinal)) 'Namespace cleanup must preserve a key concurrently created by another run.'
    Assert-Contract (-not [Linq.Enumerable]::Contains($redisKeys, "$($attempt1Identity.redisNamespace)SalesOrderChangedIntegrationEvent", [StringComparer]::Ordinal)) 'Namespace cleanup must remove the stream created inside the claimed namespace.'

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

# Keep the destructive stale-namespace cleanup contract under the established Redis/CAP
# governance entry without adding a second workflow surface.
& (Join-Path $PSScriptRoot 'redis-test-namespace-cleanup.Tests.ps1')
