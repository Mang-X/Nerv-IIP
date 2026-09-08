# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Observes only descendant execution testhosts of the supplied Redis/CAP lane process on Linux
#     - Starts bounded dotnet-counters collectors and reads the CI Redis service statistics
#   Writes:
#     - Caller-owned observation directory
#     - artifacts/script-logs/**
#   Cleanup:
#     - Stops every owned counters process in finally; never stops testhosts or service containers
#   Requires:
#     - PowerShell 7 on Linux
#     - dotnet-counters 10.0.731102, timeout, prlimit and redis-cli

[CmdletBinding()]
param(
    [Parameter(Mandatory)] [int]$LaneProcessId,
    [Parameter(Mandatory)] [string]$ResultsDirectory,
    [Parameter(Mandatory)] [string]$OutputDirectory,
    [Parameter(Mandatory)] [string]$CountersPath,
    [string]$ManifestPath = (Join-Path $PSScriptRoot 'redis-cap-test-lane.json'),
    [ValidateRange(1, 1150)] [int]$DurationSeconds = 1150,
    [ValidateRange(1024, 8388608)] [long]$MaxFileBytes = 8388608,
    [switch]$Disabled
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/ScriptAutomation.ps1')
. (Join-Path $PSScriptRoot 'lib/RedisCapObservation.ps1')
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
[IO.Directory]::CreateDirectory($OutputDirectory) | Out-Null
$OutputDirectory = (Resolve-Path $OutputDirectory).Path
$clock = [Diagnostics.Stopwatch]::StartNew()
$collectors = [Collections.Generic.List[object]]::new()
$seen = [Collections.Generic.HashSet[int]]::new()
$recordsPath = Join-Path $OutputDirectory 'observations.jsonl'
$status = [ordered]@{ outcome = 'running'; startedUtc = [DateTimeOffset]::UtcNow.ToString('O'); counterTimestampTimeZone = [TimeZoneInfo]::Local.Id; durationSeconds = $DurationSeconds; maxFileBytes = $MaxFileBytes; observerProcessId = $PID; observedTesthosts = 0; stoppedCollectors = 0; collectorFailures = 0; remainingCollectors = $null; failureType = $null }

function Write-Observation {
    param([object]$Record)
    $written = Write-RedisCapObservationRecord -Path $recordsPath -MaxBytes 8388608 -Record $Record
    if (-not $written) { $status.outcome = 'size-limit' }
    return $written
}

function Read-ProcessTable {
    $table = @{}
    foreach ($directory in [IO.Directory]::EnumerateDirectories('/proc')) {
        $processNumber = 0
        if (-not [int]::TryParse([IO.Path]::GetFileName($directory), [ref]$processNumber)) { continue }
        try {
            $stat = [IO.File]::ReadAllText((Join-Path $directory 'stat'))
            $tail = $stat.Substring($stat.LastIndexOf(')') + 2).Split(' ')
            $arguments = [IO.File]::ReadAllText((Join-Path $directory 'cmdline')).Split([char]0, [StringSplitOptions]::RemoveEmptyEntries)
            $table[$processNumber] = [pscustomobject]@{ parent = [int]$tail[1]; startTicks = $tail[19]; arguments = $arguments }
        }
        catch [IO.IOException] { } # /proc entries disappear when processes exit.
        catch [UnauthorizedAccessException] { }
    }
    return $table
}

try {
    if ($Disabled) { $status.outcome = 'disabled'; return }
    if (-not $IsLinux) { $status.outcome = 'unavailable-platform'; return }
    if (-not (Test-Path -LiteralPath $CountersPath -PathType Leaf)) { $status.outcome = 'unavailable-tool'; return }
    foreach ($requiredTool in @('redis-cli', 'prlimit', 'timeout')) {
        if ($null -eq (Get-Command $requiredTool -CommandType Application -ErrorAction SilentlyContinue)) { $status.outcome = 'unavailable-tool'; return }
    }
    $members = @((Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json).members | Where-Object { [string]::Equals([string]$_.status, 'active', [StringComparison]::Ordinal) })
    $parentStart = (Get-Process -Id $LaneProcessId).StartTime.ToUniversalTime()
    $nextRedis = 0.0
    while ($clock.Elapsed.TotalSeconds -lt $DurationSeconds -and -not (Test-Path -LiteralPath (Join-Path $OutputDirectory 'stop'))) {
        $parent = Get-Process -Id $LaneProcessId -ErrorAction SilentlyContinue
        if ($null -eq $parent -or $parent.StartTime.ToUniversalTime() -ne $parentStart) { $status.outcome = 'parent-exited'; break }
        $table = Read-ProcessTable
        foreach ($processNumber in @($table.Keys)) {
            if ($seen.Contains($processNumber)) { continue }
            $identity = Resolve-RedisCapObservedTesthost -ProcessId $processNumber -Processes $table -LaneProcessId $LaneProcessId -Members $members -ResultsDirectory $ResultsDirectory
            if ($null -eq $identity) { continue }
            [void]$seen.Add($processNumber)
            $utc = [DateTimeOffset]::UtcNow.ToString('O')
            try {
                $maps = [IO.File]::ReadAllText("/proc/$processNumber/maps")
                $runtime = [regex]::Match($maps, '/shared/Microsoft\.NETCore\.App/(?<version>[0-9.]+)/libcoreclr\.so').Groups['version'].Value
                $processStatus = [IO.File]::ReadAllText("/proc/$processNumber/status")
                $affinity = [regex]::Match($processStatus, '(?m)^Cpus_allowed_list:\s*(?<value>[0-9,\-]+)').Groups['value'].Value
                $cgroup = [IO.File]::ReadAllLines("/proc/$processNumber/cgroup") | Where-Object { $_.StartsWith('0::', [StringComparison]::Ordinal) } | Select-Object -First 1
                $cpuMax = $null
                if ($null -ne $cgroup) {
                    $cpuMaxPath = Join-Path '/sys/fs/cgroup' ($cgroup.Substring(3).TrimStart('/') + '/cpu.max')
                    if (Test-Path -LiteralPath $cpuMaxPath) { $cpuMax = [IO.File]::ReadAllText($cpuMaxPath).Trim() }
                }
                $record = [ordered]@{ kind = 'testhost'; utc = $utc; memberId = $identity.memberId; processId = $processNumber; executionProcessId = $identity.executionProcessId; processStartTicks = $table[$processNumber].startTicks; runtime = if ($runtime) { $runtime } else { $null }; architecture = $null; cpuAffinity = if ($affinity) { $affinity } else { $null }; cgroupCpuMax = $cpuMax; processorCountSource = "cpu-$processNumber.csv: dotnet.process.cpu.count Metric gauge"; attachBeforeUtc = $utc; preAttachBaseline = 'unknown' }
                $executable = [IO.File]::OpenRead("/proc/$processNumber/exe")
                try {
                    $header = [byte[]]::new(20)
                    if ($executable.Read($header, 0, 20) -eq 20 -and $header[0] -eq 127 -and $header[1] -eq 69) {
                        $machine = [BitConverter]::ToUInt16($header, 18)
                        $record.architecture = switch ($machine) { 62 { 'X64' } 183 { 'Arm64' } default { $null } }
                    }
                }
                finally { $executable.Dispose() }
                if (-not (Write-Observation $record)) { break }
                $remaining = [Math]::Max(1, [int]($DurationSeconds - $clock.Elapsed.TotalSeconds))
                # The tool forbids mixing legacy EventCounters and Metrics of the same provider in one session.
                $counterSets = @(
                    @('clr', 'EventCounters\System.Runtime[cpu-usage,threadpool-thread-count,threadpool-queue-length,threadpool-completed-items-count,monitor-lock-contention-count,time-in-gc]'),
                    @('cpu', 'System.Runtime[dotnet.process.cpu.count]')
                )
                foreach ($counterSet in $counterSets) {
                    $csvPath = Join-Path $OutputDirectory "$($counterSet[0])-$processNumber.csv"
                    $handle = Start-ManagedBackgroundProcess -Command 'prlimit' -Arguments @("--fsize=$MaxFileBytes", '--', 'timeout', '--signal=INT', '--kill-after=2', [string]$remaining, $CountersPath, 'collect', '--process-id', [string]$processNumber, '--refresh-interval', '1', '--duration', ([TimeSpan]::FromSeconds($remaining).ToString('dd\:hh\:mm\:ss')), '--counters', $counterSet[1], '--format', 'csv', '--output', $csvPath, '--resume-runtime:false') -WorkingDirectory $repoRoot -Name "redis-cap-observer-$($counterSet[0])-$processNumber"
                    $collectors.Add([pscustomobject]@{ handle = $handle; targetId = $processNumber; memberId = $identity.memberId; exitedRecorded = $false; csvPath = $csvPath })
                    [void](Write-Observation @{ kind = 'collector-started'; utc = [DateTimeOffset]::UtcNow.ToString('O'); processId = $processNumber; collectorProcessId = $handle.ProcessId; file = [IO.Path]::GetFileName($csvPath); memberId = $identity.memberId })
                }
                $status.observedTesthosts++
            }
            catch { $status.collectorFailures++; [void](Write-Observation @{ kind = 'attach-unavailable'; utc = $utc; processId = $processNumber; memberId = $identity.memberId; failureType = $_.Exception.GetType().Name }) }
        }
        foreach ($collector in $collectors) {
            if (-not $collector.exitedRecorded -and $collector.handle.Process.HasExited) {
                $collector.exitedRecorded = $true
                if ($collector.handle.Process.ExitCode -ne 0) { $status.collectorFailures++ }
                [void](Write-Observation @{ kind = 'collector-exited'; utc = [DateTimeOffset]::UtcNow.ToString('O'); processId = $collector.targetId; collectorProcessId = $collector.handle.ProcessId; file = [IO.Path]::GetFileName($collector.csvPath); memberId = $collector.memberId; exitCode = $collector.handle.Process.ExitCode; targetStillPresent = $table.ContainsKey($collector.targetId); bytes = if (Test-Path -LiteralPath $collector.csvPath) { (Get-Item -LiteralPath $collector.csvPath).Length } else { $null } })
            }
        }
        if ($status.outcome -ceq 'size-limit') { break }
        if ($clock.Elapsed.TotalSeconds -ge $nextRedis) {
            $started = $clock.Elapsed.TotalSeconds
            try {
                # INFO's named sections contain numeric statistics, not stream values or command arguments.
                $result = Invoke-NativeCommandOutput -Command 'redis-cli' -Arguments @('--raw', '-h', '127.0.0.1', '-p', '6379', 'INFO', 'cpu', 'clients', 'commandstats', 'latencystats') -WorkingDirectory $repoRoot -TimeoutSeconds 2 -Name 'redis-cap-observation-redis'
                $values = ConvertFrom-RedisCapInfo -Info $result.Stdout
                [void](Write-Observation @{ kind = 'redis'; utc = [DateTimeOffset]::UtcNow.ToString('O'); elapsedSeconds = $clock.Elapsed.TotalSeconds; collectionSeconds = $clock.Elapsed.TotalSeconds - $started; values = $values })
            }
            catch { [void](Write-Observation @{ kind = 'redis-unavailable'; utc = [DateTimeOffset]::UtcNow.ToString('O'); failureType = $_.Exception.GetType().Name }) }
            $nextRedis = $clock.Elapsed.TotalSeconds + 2
        }
        Start-Sleep -Milliseconds 250
    }
    if ($status.outcome -ceq 'running') { $status.outcome = if ($clock.Elapsed.TotalSeconds -ge $DurationSeconds) { 'deadline' } else { 'stopped' } }
}
catch { $status.outcome = 'failed'; $status.failureType = $_.Exception.GetType().Name }
finally {
    if ($status.outcome -ceq 'running') { $status.outcome = 'interrupted' }
    $remainingCollectors = 0
    foreach ($collector in $collectors) {
        $collectorId = $collector.handle.ProcessId
        try {
            if (-not $collector.handle.Process.HasExited) {
                # timeout forwards SIGINT to the owned tool, allowing its buffered CSV to be written.
                try {
                    Invoke-NativeCommandOutput -Command '/bin/kill' -Arguments @('-INT', [string]$collectorId) -WorkingDirectory $repoRoot -TimeoutSeconds 1 -Name 'redis-cap-observer-flush' | Out-Null
                    [void]$collector.handle.Process.WaitForExit(1000)
                }
                catch { $status.collectorFailures++ }
            }
            [void]$collector.handle.Stop.Invoke('Redis/CAP observation finally')
            $status.stoppedCollectors++
        }
        catch { $status.outcome = 'cleanup-failed' }
        if ($null -ne (Get-Process -Id $collectorId -ErrorAction SilentlyContinue)) { $remainingCollectors++ }
    }
    $status.remainingCollectors = $remainingCollectors
    $self = [Diagnostics.Process]::GetCurrentProcess()
    $status.observerCpuSeconds = $self.TotalProcessorTime.TotalSeconds
    $status.observerFinalWorkingSetBytes = $self.WorkingSet64
    $self.Dispose()
    $status.finishedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    $status.elapsedSeconds = $clock.Elapsed.TotalSeconds
    [IO.File]::WriteAllText((Join-Path $OutputDirectory 'status.json'), ($status | ConvertTo-Json -Depth 5), [Text.UTF8Encoding]::new($false))
}
