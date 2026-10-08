# Script-Governance:
#   Category: check
#   SideEffects:
#     - Drives the PostgreSQL runner with controlled leaf commands, without a database
#   Writes:
#     - Owned temporary command, manifest, call and TRX fixtures
#     - artifacts/script-logs/**
#   Cleanup:
#     - Removes owned temporary fixtures and restores environment in finally
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv-postgres-build-$([Guid]::NewGuid().ToString('N'))"
function Assert-Contract([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
$leaf = @'
$ErrorActionPreference = 'Stop'
[IO.File]::AppendAllText($env:NERV_BUILD_CALLS, ((@{ command=$env:NERV_BUILD_COMMAND; arguments=@($args) } | ConvertTo-Json -Compress) + "`n"))
$mode=$env:NERV_BUILD_MODE
if ([string]::Equals([string]$env:NERV_BUILD_COMMAND, 'psql', [StringComparison]::Ordinal)) {
    if ($args[-1].Contains('server_version', [StringComparison]::Ordinal)) { '18.6' }
    if ([string]::Equals([string]$mode, 'cleanup', [StringComparison]::Ordinal) -and $args[-1].StartsWith('DROP DATABASE', [StringComparison]::Ordinal)) { exit 37 }
    exit 0
}
$configurationIndex=[Array]::IndexOf([object[]]$args,'--configuration')
if ($configurationIndex -lt 0 -or -not ([string]::Equals([string]$args[$configurationIndex+1], 'Release', [StringComparison]::Ordinal))) { throw 'Expected Release configuration.' }
if ([string]::Equals([string]$args[0], 'build', [StringComparison]::Ordinal)) {
    if ([string]::Equals([string]$mode, 'build-failure', [StringComparison]::Ordinal)) { exit 31 }
    if (-not ([string]::Equals([string]$mode, 'missing-release', [StringComparison]::Ordinal)) -and -not ([string]::Equals([string]$mode, 'debug-only', [StringComparison]::Ordinal))) { [IO.File]::WriteAllText($env:NERV_BUILD_READY, 'Release') }
    exit 0
}
if ([Array]::IndexOf([object[]]$args,'--no-build') -lt 0) { throw 'Consumer attempted another build.' }
if (-not (Test-Path $env:NERV_BUILD_READY)) { throw 'Missing current Release output.' }
$filterIndex=[Array]::IndexOf([object[]]$args,'--filter')
$identity=([string]$args[$filterIndex+1]).Substring('FullyQualifiedName='.Length)
if ([Array]::IndexOf([object[]]$args,'--list-tests') -ge 0) {
    if ([string]::Equals([string]$mode, 'discovery', [StringComparison]::Ordinal) -and [string]::Equals([string]$identity, 'Fixture.A.One', [StringComparison]::Ordinal)) { exit 32 }
    if ([string]::Equals([string]$mode, 'missing-identity', [StringComparison]::Ordinal) -and [string]::Equals([string]$identity, 'Fixture.A.One', [StringComparison]::Ordinal)) { exit 0 }
    $identity
    exit 0
}
if ([string]::Equals([string]$mode, 'missing-trx', [StringComparison]::Ordinal)) { exit 0 }
$resultIndex=[Array]::IndexOf([object[]]$args,'--results-directory')
$resultDirectory=$args[$resultIndex+1]
[IO.Directory]::CreateDirectory($resultDirectory) | Out-Null
if ([string]::Equals([string]$mode, 'wrong-identity', [StringComparison]::Ordinal)) { $identity='Fixture.Wrong.Test' }
$separator=$identity.LastIndexOf('.', [StringComparison]::Ordinal)
$outcome=if ([string]::Equals([string]$mode, 'skip', [StringComparison]::Ordinal)) { 'NotExecuted' } else { 'Passed' }
$trx="<TestRun><Results><UnitTestResult testId=`"1`" outcome=`"$outcome`" /></Results><TestDefinitions><UnitTest id=`"1`"><TestMethod className=`"$($identity.Substring(0,$separator))`" name=`"$($identity.Substring($separator+1))`" /></UnitTest></TestDefinitions></TestRun>"
[IO.File]::WriteAllText((Join-Path $resultDirectory 'fixture.trx'),$trx)
'@
$saved=@{}
foreach ($key in @('PATH','NERV_IIP_TEST_POSTGRES','NERV_BUILD_CALLS','NERV_BUILD_MODE','NERV_BUILD_COMMAND','NERV_BUILD_READY')) { $saved[$key]=[Environment]::GetEnvironmentVariable($key) }
try {
    [IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
    foreach ($command in @('dotnet','psql')) {
        if ($IsWindows) {
            [IO.File]::WriteAllText((Join-Path $fixtureRoot "$command.ps1"),$leaf)
            [IO.File]::WriteAllText((Join-Path $fixtureRoot "$command.cmd"),"@echo off`r`nset NERV_BUILD_COMMAND=$command`r`npwsh -NoProfile -File `"%~dp0$command.ps1`" %*`r`nexit /b %ERRORLEVEL%`r`n")
        } else {
            $path=Join-Path $fixtureRoot $command
            [IO.File]::WriteAllText($path,"#!/usr/bin/env pwsh`n`$env:NERV_BUILD_COMMAND='$command'`n"+$leaf)
            [IO.File]::SetUnixFileMode($path,([IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite -bor [IO.UnixFileMode]::UserExecute))
        }
    }
    $env:PATH="$fixtureRoot$([IO.Path]::PathSeparator)$($saved.PATH)"
    $env:NERV_IIP_TEST_POSTGRES='Host=fixture;Port=5432;Database=postgres;Username=fixture;Password=fixture'
    $members=@(Get-Content (Join-Path $repoRoot 'scripts/postgres-test-lane.json') -Raw | ConvertFrom-Json).members
    $projects=@($members[0].project,$members[1].project)
    $fixtureMembers=@(
        foreach ($index in 0..2) {
            $identity=@('Fixture.A.One','Fixture.A.Two','Fixture.B.One')[$index]
            [ordered]@{ id="fixture-$index"; service='fixture'; tier='core'; status='active'; databaseOwnership='runner'; project=$projects[[int]($index -eq 2)]; filter="FullyQualifiedName=$identity"; databasePrefix="nerv_fixture_$index"; diagnosticSchemas=@('fixture'); expectedTestIdentities=@($identity) }
        }
    )
    $manifest=Join-Path $fixtureRoot 'manifest.json'
    @{ schemaVersion=1; members=$fixtureMembers } | ConvertTo-Json -Depth 10 | Set-Content $manifest
    $env:NERV_IIP_TEST_POSTGRES=$null
    $missingEnvironmentRejected=$false
    try {
        & (Join-Path $repoRoot 'scripts/run-postgres-test-lane.ps1') -MemberId fixture-0 -DatabaseSuffix fixture -ResultsDirectory (Join-Path $fixtureRoot 'missing-env-results') -SummaryPath (Join-Path $fixtureRoot 'missing-env-summary.json') -ManifestPath $manifest
    } catch { $missingEnvironmentRejected=$_.Exception.Message.Contains('Set NERV_IIP_TEST_POSTGRES', [StringComparison]::Ordinal) }
    Assert-Contract $missingEnvironmentRejected 'Missing PostgreSQL environment must fail before leaf commands.'
    $env:NERV_IIP_TEST_POSTGRES='Host=fixture;Port=5432;Database=postgres;Username=fixture;Password=fixture'
    foreach ($mode in @('control','all-active','build-failure','missing-release','debug-only','discovery','missing-identity','missing-trx','wrong-identity','skip','cleanup')) {
        $env:NERV_BUILD_MODE=$mode
        $env:NERV_BUILD_CALLS=Join-Path $fixtureRoot "$mode-calls.jsonl"
        $env:NERV_BUILD_READY=Join-Path $fixtureRoot "$mode-ready"
        $results=Join-Path $fixtureRoot "$mode-results"
        $summaryPath=Join-Path $fixtureRoot "$mode-summary.json"
        # 上一 invocation 的 TRX 不得掩盖本次 build/discovery 失败。
        [IO.Directory]::CreateDirectory((Join-Path $results 'fixture-0')) | Out-Null
        [IO.File]::WriteAllText((Join-Path $results 'fixture-0/old.trx'),'<TestRun><Results><UnitTestResult testId="1" outcome="Passed" /></Results><TestDefinitions><UnitTest id="1"><TestMethod className="Fixture.A" name="One" /></UnitTest></TestDefinitions></TestRun>')
        if ([string]::Equals([string]$mode, 'build-failure', [StringComparison]::Ordinal)) { [IO.File]::WriteAllText($env:NERV_BUILD_READY,'Release') }
        $failed=$false
        try {
            if ([string]::Equals([string]$mode, 'all-active', [StringComparison]::Ordinal)) {
                & (Join-Path $repoRoot 'scripts/run-postgres-test-lane.ps1') -AllActiveMembers -DatabaseSuffix fixture -ResultsDirectory $results -SummaryPath $summaryPath -ManifestPath $manifest
            } else {
                & (Join-Path $repoRoot 'scripts/run-postgres-test-lane.ps1') -MemberId fixture-0,fixture-1 -DatabaseSuffix fixture -ResultsDirectory $results -SummaryPath $summaryPath -ManifestPath $manifest
            }
        } catch { $failed=$true; $failureMessage=$_.Exception.Message }
        $calls=@(Get-Content $env:NERV_BUILD_CALLS | ForEach-Object { $_ | ConvertFrom-Json })
        $builds=@($calls | Where-Object { [string]::Equals([string]$_.arguments[0], 'build', [StringComparison]::Ordinal) })
        $discoveries=@($calls | Where-Object { [Array]::IndexOf([object[]]$_.arguments,'--list-tests') -ge 0 })
        $executions=@($calls | Where-Object { [Array]::IndexOf([object[]]$_.arguments,'--results-directory') -ge 0 })
        $summary=Get-Content $summaryPath -Raw | ConvertFrom-Json
        $expectedBuilds=if ([string]::Equals([string]$mode, 'all-active', [StringComparison]::Ordinal)) { 2 } else { 1 }
        Assert-Contract ($builds.Count -eq $expectedBuilds) "$mode expected $expectedBuilds Release builds but observed $($builds.Count)."
        if ([string]::Equals([string]$mode, 'control', [StringComparison]::Ordinal) -or [string]::Equals([string]$mode, 'all-active', [StringComparison]::Ordinal)) {
            $count=if ([string]::Equals([string]$mode, 'all-active', [StringComparison]::Ordinal)) { 3 } else { 2 }
            Assert-Contract (-not $failed -and $discoveries.Count -eq $count -and $executions.Count -eq $count -and $summary.passed -eq $count -and [string]::Equals([string]$summary.cleanup, 'passed', [StringComparison]::Ordinal)) "$mode must retain per-member discovery, execution and successful evidence."
            Assert-Contract ([string]::Equals((@($summary.selectedMemberIds) -join '|'), ((0..($count-1) | ForEach-Object { "fixture-$_" }) -join '|'), [StringComparison]::Ordinal)) 'Selection order changed.'
        } else {
            Assert-Contract $failed "$mode must propagate failure."
            Assert-Contract (@($summary.members | Where-Object { -not ([string]::Equals([string]$_.outcome, 'passed', [StringComparison]::Ordinal)) }).Count -gt 0) "$mode cannot report successful members only."
            if ([string]::Equals([string]$mode, 'build-failure', [StringComparison]::Ordinal)) { Assert-Contract ($discoveries.Count -eq 0 -and $executions.Count -eq 0 -and $summary.passed -eq 0) 'Failed build must not consume old outputs or TRX.' }
            if ([string]::Equals([string]$mode, 'missing-release', [StringComparison]::Ordinal) -or [string]::Equals([string]$mode, 'debug-only', [StringComparison]::Ordinal)) { Assert-Contract ($executions.Count -eq 0) 'Missing Release outputs must prevent execution.' }
            if ([string]::Equals([string]$mode, 'discovery', [StringComparison]::Ordinal) -or [string]::Equals([string]$mode, 'missing-identity', [StringComparison]::Ordinal)) { Assert-Contract ($executions.Count -eq 1 -and [string]::Equals([string]$summary.members[1].outcome, 'passed', [StringComparison]::Ordinal)) 'Discovery failure must preserve the other member isolation.' }
        }
        if ([string]::Equals([string]$mode, 'build-failure', [StringComparison]::Ordinal)) { Assert-Contract ($failureMessage.Contains('31', [StringComparison]::Ordinal)) 'Build failure must preserve its native exit code.' }
        if ([string]::Equals([string]$mode, 'cleanup', [StringComparison]::Ordinal)) { Assert-Contract ($failureMessage.Contains('37', [StringComparison]::Ordinal)) 'Cleanup failure must preserve its native exit code.' }
        $creates=@($calls | Where-Object { [string]::Equals([string]$_.command, 'psql', [StringComparison]::Ordinal) -and $_.arguments[-1].StartsWith('CREATE DATABASE', [StringComparison]::Ordinal) })
        $drops=@($calls | Where-Object { [string]::Equals([string]$_.command, 'psql', [StringComparison]::Ordinal) -and $_.arguments[-1].StartsWith('DROP DATABASE', [StringComparison]::Ordinal) })
        Assert-Contract ($creates.Count -eq $drops.Count) "$mode must attempt cleanup for every owned member database."
        if (-not ([string]::Equals([string]$mode, 'cleanup', [StringComparison]::Ordinal))) { Assert-Contract ([string]::Equals([string]$summary.cleanup, 'passed', [StringComparison]::Ordinal)) "$mode must preserve successful cleanup even on preparation/evidence failure." }
        else { Assert-Contract ([string]::Equals([string]$summary.cleanup, 'failed', [StringComparison]::Ordinal)) 'Cleanup failure cannot produce a successful aggregate.' }
        foreach ($execution in $executions) {
            $filterIndex=[Array]::IndexOf([object[]]$execution.arguments,'--filter')
            $resultIndex=[Array]::IndexOf([object[]]$execution.arguments,'--results-directory')
            $memberIndex=[Array]::IndexOf([string[]]@('FullyQualifiedName=Fixture.A.One','FullyQualifiedName=Fixture.A.Two','FullyQualifiedName=Fixture.B.One'), [string]$execution.arguments[$filterIndex+1])
            Assert-Contract ($memberIndex -ge 0 -and [string]::Equals([string]$execution.arguments[$resultIndex+1], (Join-Path $results "fixture-$memberIndex"), [StringComparison]::Ordinal)) 'Each filter must execute in its own results directory.'
        }
        Write-Host "PASS $mode builds=$($builds.Count) discovery=$($discoveries.Count) execution=$($executions.Count)"
    }
} finally {
    foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key,$saved[$key]) }
    if (Test-Path $fixtureRoot) { Remove-Item $fixtureRoot -Recurse -Force }
}
