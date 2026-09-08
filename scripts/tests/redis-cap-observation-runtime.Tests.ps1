# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Runs a disposable testhost and the dedicated observer on Linux
#   Writes:
#     - Owned temporary test project, manifest, TRX and observation files
#     - artifacts/script-logs/**
#   Cleanup:
#     - Stops owned observer and test processes and removes owned temporary files in finally
#   Requires:
#     - Linux, PowerShell 7, .NET 10, prlimit, timeout, redis-cli and dotnet-counters

param([Parameter(Mandatory)] [string]$CountersPath)
$ErrorActionPreference = 'Stop'
if (-not $IsLinux) { throw 'This regression requires Linux; it does not certify a hosted lane.' }
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv2127-runtime-$([Guid]::NewGuid().ToString('N'))"
$observer = $null
$test = $null
function Assert-Observation([bool]$Condition, [string]$Message) { if (-not $Condition) { throw $Message } }
try {
    [IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
    $project = Join-Path $fixtureRoot 'Probe.csproj'
    [IO.File]::WriteAllText($project, '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net10.0</TargetFramework><IsTestProject>true</IsTestProject></PropertyGroup><ItemGroup><PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1"/><PackageReference Include="xunit" Version="2.9.3"/><PackageReference Include="xunit.runner.visualstudio" Version="3.1.4"/></ItemGroup></Project>')
    [IO.File]::WriteAllText((Join-Path $fixtureRoot 'Probe.cs'), 'public class Probe { [Xunit.Fact] public async System.Threading.Tasks.Task Observed() { System.Console.WriteLine("processorCount=" + System.Environment.ProcessorCount); await System.Threading.Tasks.Task.Delay(10000); } }')
    $manifest = Join-Path $fixtureRoot 'manifest.json'
    $noisyTool = Join-Path $fixtureRoot 'noisy-counters'
    [IO.File]::WriteAllText($noisyTool, @'
#!/bin/sh
while [ "$#" -gt 0 ]; do
  if [ "$1" = "--output" ]; then shift; output="$1"; fi
  shift
done
dd if=/dev/zero of="$output" bs=1024 count=2 2>/dev/null
'@)
    [IO.File]::SetUnixFileMode($noisyTool, [IO.UnixFileMode]::UserRead -bor [IO.UnixFileMode]::UserWrite -bor [IO.UnixFileMode]::UserExecute)
    @{ members = @(@{ id = 'probe'; status = 'active'; project = $project; filter = 'FullyQualifiedName=Probe.Observed' }) } | ConvertTo-Json -Depth 4 | Set-Content $manifest
    Invoke-DotNetOutput -Arguments @('build', $project, '--configuration', 'Release') -WorkingDirectory $fixtureRoot -TimeoutSeconds 120 -Name 'observation-probe-build' | Out-Null
    foreach ($scenario in @('natural', 'early-exit', 'size-limit', 'stop', 'deadline')) {
        $output = Join-Path $fixtureRoot $scenario
        $results = Join-Path $fixtureRoot "$scenario-results"
        $tool = if ($scenario -ceq 'early-exit') { '/usr/bin/false' } elseif ($scenario -ceq 'size-limit') { $noisyTool } else { $CountersPath }
        $limit = if ($scenario -ceq 'size-limit') { 1024 } else { 8388608 }
        $duration = if ($scenario -ceq 'deadline') { '3' } else { '30' }
        $observer = Start-ManagedBackgroundProcess -Command 'pwsh' -Arguments @('-NoProfile', '-File', (Join-Path $repoRoot 'scripts/observe-redis-cap-lane.ps1'), '-LaneProcessId', [string]$PID, '-ResultsDirectory', $results, '-OutputDirectory', $output, '-CountersPath', $tool, '-ManifestPath', $manifest, '-DurationSeconds', $duration, '-MaxFileBytes', [string]$limit) -WorkingDirectory $repoRoot -Name "observation-$scenario"
        $test = Start-ManagedBackgroundProcess -Command 'dotnet' -Arguments @('test', $project, '--configuration', 'Release', '--no-build', '--filter', 'FullyQualifiedName=Probe.Observed', '--results-directory', (Join-Path $results 'probe'), '--logger', 'trx') -WorkingDirectory $fixtureRoot -Name "observation-test-$scenario"
        if ($scenario -ceq 'stop') {
            $wait = [Diagnostics.Stopwatch]::StartNew()
            $started = $false
            while (-not $started -and $wait.Elapsed.TotalSeconds -lt 15) {
                $records = Join-Path $output 'observations.jsonl'
                $started = (Test-Path -LiteralPath $records) -and [IO.File]::ReadAllText($records).Contains('"kind":"collector-started"', [StringComparison]::Ordinal)
                if (-not $started) { Start-Sleep -Milliseconds 100 }
            }
            Assert-Observation $started 'Cancellation regression must first observe an owned collector process.'
            Assert-Observation (-not $test.Process.HasExited) 'Stop regression must stop observation while the actual testhost is still running.'
        }
        else {
            Assert-Observation ($test.Process.WaitForExit(20000)) 'Probe testhost must exit inside its own test budget.'
            Assert-Observation ($test.Process.ExitCode -eq 0) 'Observation must not alter the probe test result.'
        }
        [IO.File]::WriteAllText((Join-Path $output 'stop'), 'stop')
        Assert-Observation ($observer.Process.WaitForExit(15000)) 'Observer must finish after the stop marker.'
        $state = Get-Content (Join-Path $output 'status.json') -Raw | ConvertFrom-Json
        Write-Host ($state | ConvertTo-Json -Compress)
        Assert-Observation ($state.observedTesthosts -eq 1 -and $state.remainingCollectors -eq 0) 'Exactly one actual execution testhost must be observed and all collectors reaped.'
        if ($scenario -ceq 'stop') {
            Assert-Observation (-not $test.Process.HasExited) 'Observer cleanup must not stop the testhost.'
            Assert-Observation ($test.Process.WaitForExit(15000) -and $test.Process.ExitCode -eq 0) 'The testhost must retain its original successful exit after observation stops.'
        }
        if ($scenario -ceq 'early-exit') { Assert-Observation ($state.collectorFailures -gt 0) 'Early tool exits must be reported separately from the passing test.' }
        elseif ($scenario -ceq 'size-limit') {
            Assert-Observation (@(Get-ChildItem $output -Filter '*.csv' | Where-Object Length -gt 1024).Count -eq 0) 'Kernel file-size limit must bound every collector output.'
            Assert-Observation (@(Get-ChildItem $output -Filter '*.csv' | Where-Object Length -eq 1024).Count -gt 0) 'The size-limit regression must actually reach the kernel cap.'
        }
        elseif ($scenario -ceq 'deadline') { Assert-Observation ($state.outcome -ceq 'deadline') 'The observer must stop at its own deadline without a caller stop marker.' }
        elseif ($scenario -ceq 'natural') {
            $cpu = @(Get-ChildItem $output -Filter 'cpu-*.csv' | Import-Csv | Where-Object { $_.'Counter Type' -ceq 'Metric' })
            $threads = @(Get-ChildItem $output -Filter 'clr-*.csv' | Import-Csv | Where-Object { $_.'Counter Name' -ceq 'ThreadPool Thread Count' -and $_.'Counter Type' -ceq 'Metric' })
            Assert-Observation ($cpu.Count -gt 0 -and $threads.Count -gt 0) 'Actual testhost must yield ProcessorCount and absolute ThreadPool metrics.'
        }
        [void]$observer.Stop.Invoke('Observer regression complete'); $observer = $null
        [void]$test.Stop.Invoke('Probe regression complete'); $test = $null
        Write-Host "Redis/CAP observer runtime scenario passed: $scenario"
    }
}
finally {
    if ($null -ne $observer) { [void]$observer.Stop.Invoke('Observer regression finally') }
    if ($null -ne $test) { [void]$test.Stop.Invoke('Probe regression finally') }
    Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
}
