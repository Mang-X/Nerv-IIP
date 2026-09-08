# Script-Governance:
#   Category: check
#   SideEffects:
#     - Exercises the dedicated Redis/CAP observation identity and privacy boundaries
#   Writes:
#     - Owned temporary observation fixtures
#   Cleanup:
#     - Removes owned temporary observation fixtures in finally
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../lib/RedisCapObservation.ps1')
function Assert-Observation([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

# Captured from Redis 8.10.1 INFO after an owned XGROUP CREATE fixture command.
$redisInfo = @'
cmdstat_xgroup|create:calls=1,usec=171,usec_per_call=171.00,rejected_calls=0,failed_calls=0
latency_percentiles_usec_xgroup|create:p50=171.007,p99=171.007,p99.9=171.007
cmdstat_xgroup|destroy:calls=1,usec=10
cmdstat_get:calls=1,usec=10
cmdstat_xgroup|create:payload="secret-value"
'@
$redisValues = ConvertFrom-RedisCapInfo -Info $redisInfo
Assert-Observation ($redisValues.Count -eq 2 -and $redisValues['cmdstat_xgroup|create'] -ceq 'calls=1,usec=171,usec_per_call=171.00,rejected_calls=0,failed_calls=0' -and $redisValues['latency_percentiles_usec_xgroup|create'] -ceq 'p50=171.007,p99=171.007,p99.9=171.007') 'Actual XGROUP CREATE statistics and percentiles must survive; other subcommands, commands and command arguments must not.'

# NERV-2127: accepting discovery or another invocation must fail this regression.
$members = @([pscustomobject]@{ id = 'mes'; project = 'backend/mes.csproj'; filter = 'FullyQualifiedName=Example' })
$processes = @{
    100 = [pscustomobject]@{ parent = 1; arguments = @('pwsh', 'lane.ps1') }
    101 = [pscustomobject]@{ parent = 100; arguments = @('dotnet', 'test', 'backend/mes.csproj', '--filter', 'FullyQualifiedName=Example', '--results-directory', '/results/mes') }
    102 = [pscustomobject]@{ parent = 101; arguments = @('dotnet', 'exec', '/sdk/testhost.dll') }
    201 = [pscustomobject]@{ parent = 100; arguments = @('dotnet', 'test', 'backend/mes.csproj', '--list-tests', '--filter', 'FullyQualifiedName=Example', '--results-directory', '/results/mes') }
    202 = [pscustomobject]@{ parent = 201; arguments = @('dotnet', 'exec', '/sdk/testhost.dll') }
    301 = [pscustomobject]@{ parent = 1; arguments = @('dotnet', 'test', 'backend/mes.csproj', '--filter', 'FullyQualifiedName=Example', '--results-directory', '/results/mes') }
    302 = [pscustomobject]@{ parent = 301; arguments = @('dotnet', 'exec', '/sdk/testhost.dll') }
}
$identity = Resolve-RedisCapObservedTesthost -ProcessId 102 -Processes $processes -LaneProcessId 100 -Members $members -ResultsDirectory '/results'
Assert-Observation ($null -ne $identity -and $identity.memberId -ceq 'mes' -and $identity.executionProcessId -eq 101) 'Formal testhost must associate with its execution member.'
Assert-Observation ($null -ne (Resolve-RedisCapObservedTesthost -ProcessId 102 -Processes $processes -LaneProcessId 1 -Members $members -ResultsDirectory '/results')) 'A lane owner that is PID 1 in a container must remain observable.'
foreach ($excluded in @(101, 202, 302)) {
    Assert-Observation ($null -eq (Resolve-RedisCapObservedTesthost -ProcessId $excluded -Processes $processes -LaneProcessId 100 -Members $members -ResultsDirectory '/results')) 'Discovery, launcher and foreign invocation must not be observed.'
}
foreach ($mismatch in @(
    @('dotnet', 'test', 'backend/other.csproj', '--filter', 'FullyQualifiedName=Example', '--results-directory', '/results/mes'),
    @('dotnet', 'test', 'backend/mes.csproj', '--filter', 'FullyQualifiedName=Other', '--results-directory', '/results/mes'),
    @('dotnet', 'test', 'backend/mes.csproj', '--filter', 'FullyQualifiedName=Example', '--results-directory', '/other-results/mes')
)) {
    $processes[401] = [pscustomobject]@{ parent = 100; arguments = $mismatch }
    $processes[402] = [pscustomobject]@{ parent = 401; arguments = @('dotnet', 'exec', '/sdk/testhost.dll') }
    Assert-Observation ($null -eq (Resolve-RedisCapObservedTesthost -ProcessId 402 -Processes $processes -LaneProcessId 100 -Members $members -ResultsDirectory '/results')) 'A same-owner testhost with a different project, filter or result directory must be excluded independently.'
}

$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv2127-$([Guid]::NewGuid().ToString('N'))"
try {
    [IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
    $boundedPath = Join-Path $fixtureRoot 'bounded.jsonl'
    Assert-Observation (Write-RedisCapObservationRecord -Path $boundedPath -MaxBytes 16 -Record @{ n = 1 }) 'A complete record inside the limit must be written.'
    $before = [IO.File]::ReadAllText($boundedPath)
    Assert-Observation (-not (Write-RedisCapObservationRecord -Path $boundedPath -MaxBytes 16 -Record @{ n = 'too large' })) 'Crossing the byte limit must reject the next record.'
    Assert-Observation ([string]::Equals($before, [IO.File]::ReadAllText($boundedPath), [StringComparison]::Ordinal)) 'A size limit must preserve complete existing records without partial append.'
    $phase = '{"source":"mes_asset_unavailable_subscription","fixtureId":"00000000000000000000000000000001","consumerId":1,"sequence":1,"phase":"inner_subscribe_failed","timestamp":123,"timestampFrequency":1000,"utc":"2026-09-08T06:00:00Z","processId":102,"runtime":".NET 10.0.11","architecture":"X64","processorCount":4,"payload":"secret-value"}'
    [IO.File]::WriteAllText((Join-Path $fixtureRoot 'test.trx'), "<TestRun><Output><StdOut>$([Security.SecurityElement]::Escape($phase))</StdOut></Output></TestRun>")
    $outputPath = Join-Path $fixtureRoot 'phases.jsonl'
    $count = Export-RedisCapFixturePhases -ResultsDirectory $fixtureRoot -OutputPath $outputPath
    $retained = [IO.File]::ReadAllText($outputPath)
    Assert-Observation ($count -eq 1 -and ($retained | ConvertFrom-Json).processorCount -eq 4) 'VSTest StdOut phase must preserve the actual process identity.'
    Assert-Observation (-not $retained.Contains('secret', [StringComparison]::Ordinal) -and -not $retained.Contains('payload', [StringComparison]::Ordinal)) 'Unknown phase fields must not enter retained evidence.'
    [IO.File]::WriteAllText((Join-Path $fixtureRoot 'test.trx'), "<TestRun><Output><StdOut>$([Security.SecurityElement]::Escape($phase.Replace('inner_subscribe_failed', 'secret-phase')))</StdOut></Output></TestRun>")
    Assert-Observation ((Export-RedisCapFixturePhases -ResultsDirectory $fixtureRoot -OutputPath $outputPath) -eq 0) 'Unknown phases must not enter retained evidence.'
}
finally { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
Write-Host 'Redis/CAP observation identity and privacy contracts passed.'
