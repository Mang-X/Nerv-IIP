$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')
$verifyPath = Join-Path $repoRoot 'scripts/verify-connector-health-disconnect.ps1'
$simulatorPath = Join-Path $repoRoot 'scripts/support/modbus-tcp-simulator.ps1'
$appHostPath = Join-Path $repoRoot 'infra/aspire/Nerv.IIP.AppHost/Program.cs'
$connectorHostProgramPath = Join-Path $repoRoot 'connector-hosts/src/Nerv.IIP.ConnectorHost.Host/Program.cs'

function Assert-Contract([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

Assert-Contract (Test-Path -LiteralPath $verifyPath -PathType Leaf) 'Connector disconnect verify script is missing.'
Assert-Contract (Test-Path -LiteralPath $simulatorPath -PathType Leaf) 'Loopback Modbus simulator script is missing.'

$verify = Get-Content -LiteralPath $verifyPath -Raw
$simulator = Get-Content -LiteralPath $simulatorPath -Raw
$appHost = Get-Content -LiteralPath $appHostPath -Raw
$connectorHostProgram = Get-Content -LiteralPath $connectorHostProgramPath -Raw

Assert-Contract ($verify.Contains("scripts/lib/ScriptAutomation.ps1")) 'Verify script must dot-source ScriptAutomation.ps1.'
Assert-Contract ($verify.Contains("scripts/lib/FullStackSessionState.ps1")) 'Verify script must use the governed full-stack session state.'
Assert-Contract ($verify.Contains('Start-ManagedBackgroundProcess')) 'Verify script must start the simulator with Start-ManagedBackgroundProcess.'
Assert-Contract ($verify.Contains('[System.Diagnostics.Stopwatch]::StartNew()')) 'Disconnect deadline must use a monotonic Stopwatch.'
Assert-Contract ($verify.Contains('DisconnectDeadlineMilliseconds = 10000')) 'Disconnect deadline must remain fixed at 10 seconds.'
Assert-Contract ($verify.Contains('finally')) 'Verify script must clean the simulator and full-stack session in finally.'
Assert-Contract ($verify.Contains('disconnectStartUtc')) 'Evidence must include disconnectStartUtc.'
Assert-Contract ($verify.Contains('connectionObservedAtUtc')) 'Evidence must include connectionObservedAtUtc.'
Assert-Contract ($verify.Contains('gatewayObservedAtUtc')) 'Evidence must include gatewayObservedAtUtc.'
Assert-Contract ($verify.Contains('elapsedMilliseconds')) 'Evidence must include elapsedMilliseconds.'
Assert-Contract ($verify.Contains('lastHeartbeatAtUtc')) 'Evidence must include lastHeartbeatAtUtc.'
Assert-Contract ($verify.Contains('recoveryObservedAtUtc')) 'Evidence must include a recovery timestamp.'
Assert-Contract ($verify.Contains('neverSampled')) 'Evidence must prove a configured but never-sampled mapping.'
Assert-Contract ($verify.Contains('disconnectedSinceUtc')) 'Each disconnect observation must include the explicit disconnected-since timestamp.'
Assert-Contract ($verify.Contains('maximumElapsedMilliseconds')) 'Root evidence must summarize the maximum elapsed disconnect time.'
Assert-Contract ($verify.Contains('$disconnectedSinceUtc -ge $disconnectStartUtc')) 'Disconnect time must not precede the simulator disconnect.'
Assert-Contract ($verify.Contains('$disconnectedSinceUtc -le $gatewayObservedAtUtc')) 'Disconnect time must not follow the Gateway observation.'
Assert-Contract ($verify.IndexOf('$gatewayObservedAtUtc = [DateTimeOffset]::UtcNow', [StringComparison]::Ordinal) -ge 0) 'Gateway observation must be captured after the lost state is observed.'
Assert-Contract ($verify.Contains('Measure-Object -Property elapsedMilliseconds -Maximum')) 'Maximum elapsed evidence must be computed across completed runs.'
Assert-Contract ($verify.Contains('diagnostics.json')) 'Verify script must persist stage diagnostics independently of success evidence.'
Assert-Contract ($verify.Contains('currentStage')) 'Diagnostics must identify the current acceptance stage.'
Assert-Contract ($verify.Contains('lastRequestError')) 'Diagnostics must preserve the last health or coverage request error.'
Assert-Contract ($verify.Contains('Write-AcceptanceDiagnostics')) 'Verify script must update diagnostics while it runs and when it fails.'
Assert-Contract ($verify.Contains("status = 'failed'")) 'Verify script must persist an explicit failed terminal status.'
Assert-Contract ($verify.Contains('Protect-NervFullStackDiagnosticText')) 'Diagnostic exceptions must redact governed and run-specific sensitive values.'
Assert-Contract (-not $verify.Contains('catch { }')) 'Verify script must not silently swallow request or state-observation failures.'
Assert-Contract ($verify.Contains('Start-DetachedManagedProcess')) 'Full-stack start must be detached so Aspire descendants cannot hold the verification pipe open.'
Assert-Contract ($verify.Contains('Wait-FullStackSessionRunning')) 'Verify script must wait on the governed session manifest instead of start-process EOF.'
Assert-Contract ($verify.Contains('Test-NervProcessIdentity')) 'Detached start cleanup must verify the exact process identity.'
Assert-Contract ($verify.Contains('function Stop-FullStackStartProcess')) 'Verify script must own explicit detached-wrapper cleanup.'
Assert-Contract (
    $verify.LastIndexOf('try { Stop-FullStackStartProcess', [StringComparison]::Ordinal) -ge 0 -and
    $verify.LastIndexOf('$manifestPath = Get-NervFullStackManifestPath', [StringComparison]::Ordinal) -ge 0 -and
    $verify.LastIndexOf('try { Stop-FullStackStartProcess', [StringComparison]::Ordinal) -lt
        $verify.LastIndexOf('$manifestPath = Get-NervFullStackManifestPath', [StringComparison]::Ordinal)
) 'Finally must stop the exact detached start wrapper before stopping the governed full-stack session.'
Assert-Contract ($verify.Contains('start.stdout.log') -and $verify.Contains('start.stderr.log')) 'Detached full-stack start must preserve stdout and stderr artifacts.'
Assert-Contract ($verify.Contains("'Failed', 'CleanupFailed'")) 'Manifest wait must fail explicitly when startup enters a terminal failure state.'

foreach ($forbidden in @('dotnet ', 'docker ', 'pnpm ', 'pwsh ', 'Start-Process')) {
    Assert-Contract (-not $verify.Contains($forbidden, [StringComparison]::OrdinalIgnoreCase)) "Verify script contains forbidden direct command '$forbidden'."
}

Assert-Contract ($simulator.Contains('TcpListener')) 'Simulator must bind a real loopback TCP listener.'
Assert-Contract ($simulator.Contains('127.0.0.1')) 'Simulator must bind loopback only.'
Assert-Contract ($simulator.Contains('ready')) 'Simulator must publish ready JSON.'
Assert-Contract ($simulator.Contains('ConvertTo-Json')) 'Simulator ready record must be JSON.'
Assert-Contract ($simulator.Contains('StopRequested')) 'Simulator must support a governed stop request.'
Assert-Contract ($simulator.Contains('.Stop()')) 'Simulator must stop its listener so the same port can be rebound.'

Assert-Contract ($appHost.Contains('ConnectorHealthAcceptance:Enabled')) 'AppHost must have an explicit acceptance-only opt-in.'
Assert-Contract ($appHost.Contains('Platform__IndustrialTelemetryBaseUrl')) 'Acceptance wiring must inject the IndustrialTelemetry URL into Connector Host.'
Assert-Contract ($appHost.Contains('InternalService__BearerToken')) 'Acceptance wiring must inject the session internal token into Connector Host.'
Assert-Contract ($appHost.Contains('Modbus__Registers__0__TagKey')) 'Acceptance wiring must configure the sampled Modbus mapping.'
Assert-Contract ($appHost.Contains('Modbus__Registers__1__TagKey')) 'Acceptance wiring must configure the never-sampled Modbus mapping.'
Assert-Contract ($appHost.Contains('.WithEnvironment("Modbus__Registers__1__DataType", "Float32")')) 'Never-sampled mapping must bind as Float32.'
Assert-Contract ($appHost.Contains('.WithEnvironment("Modbus__Registers__1__RegisterCount", "2")')) 'Never-sampled Float32 mapping must bind exactly two registers.'
Assert-Contract (-not $appHost.Contains('.WithEnvironment("Modbus__Registers__1__BucketSeconds", "3600")')) 'Never-sampled proof must not depend on an hour-long open bucket.'
Assert-Contract ($connectorHostProgram.Contains('section.GetValue<ushort>("RegisterCount", 1)')) 'Connector Host must bind the configured Modbus register count.'
Assert-Contract ($connectorHostProgram.Contains('section["DataType"]')) 'Connector Host must bind the configured Modbus data type.'

function Wait-ReadyRecord([string] $Path, [int] $TimeoutSeconds = 10) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        if (Test-Path -LiteralPath $Path -PathType Leaf) {
            $ready = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
            if ("$($ready.state)" -eq 'ready') { return $ready }
        }
        Start-Sleep -Milliseconds 25
    }
    throw "Timed out waiting for simulator ready record '$Path'."
}

function Read-Exactly([System.IO.Stream] $Stream, [int] $Count) {
    $buffer = [byte[]]::new($Count)
    $offset = 0
    while ($offset -lt $Count) {
        $read = $Stream.Read($buffer, $offset, $Count - $offset)
        if ($read -eq 0) { throw 'Simulator closed the Modbus connection before returning a complete frame.' }
        $offset += $read
    }
    return $buffer
}

function Invoke-ModbusRead([System.IO.Stream] $Stream, [byte[]] $Request) {
    $Stream.Write($Request, 0, $Request.Length)
    $Stream.Flush()
    $header = Read-Exactly -Stream $Stream -Count 7
    $length = ([int] $header[4] -shl 8) -bor [int] $header[5]
    $body = Read-Exactly -Stream $Stream -Count ($length - 1)
    return [pscustomobject]@{ Header = $header; Body = $body }
}

$simulatorTestRoot = Join-Path ([System.IO.Path]::GetTempPath()) "nerv-modbus-simulator-$([guid]::NewGuid().ToString('N'))"
$managedProcesses = [System.Collections.Generic.List[object]]::new()
try {
    [System.IO.Directory]::CreateDirectory($simulatorTestRoot) | Out-Null
    $readyPath = Join-Path $simulatorTestRoot 'first.ready.json'
    $stopPath = Join-Path $simulatorTestRoot 'first.stop'
    $first = Start-ManagedBackgroundProcess `
        -Command (Get-Process -Id $PID).Path `
        -Arguments @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $simulatorPath, '-Port', '0', '-ReadyPath', $readyPath, '-StopPath', $stopPath) `
        -WorkingDirectory $repoRoot `
        -Name 'modbus-simulator-contract-first' `
        -LogDirectory (Join-Path $simulatorTestRoot 'first-logs')
    $managedProcesses.Add([pscustomobject]@{ Managed = $first; StopPath = $stopPath })
    $ready = Wait-ReadyRecord -Path $readyPath
    $port = [int] $ready.port
    $client = [System.Net.Sockets.TcpClient]::new()
    try {
        $client.Connect('127.0.0.1', $port)
        $stream = $client.GetStream()
        $normal = Invoke-ModbusRead -Stream $stream -Request ([byte[]] @(0, 1, 0, 0, 0, 6, 1, 3, 0, 0, 0, 1))
        Assert-Contract ($normal.Header[1] -eq 1 -and $normal.Body[0] -eq 3 -and $normal.Body[1] -eq 2) 'Normal mapping response frame is invalid.'
        Assert-Contract (-not ($normal.Body[2] -eq 0x7f -and $normal.Body[3] -eq 0xc0)) 'Normal mapping must return a finite register value.'
        $nan = Invoke-ModbusRead -Stream $stream -Request ([byte[]] @(0, 2, 0, 0, 0, 6, 1, 3, 0, 1, 0, 2))
        Assert-Contract ($nan.Header[1] -eq 2 -and $nan.Body[0] -eq 3 -and $nan.Body[1] -eq 4) 'NaN mapping response frame is invalid.'
        Assert-Contract (@($nan.Body[2..5]) -join ',' -eq '127,192,0,0') 'NaN mapping must return IEEE754 quiet NaN bytes 7F C0 00 00.'
    }
    finally {
        if ($null -ne $client) { $client.Dispose() }
    }
    Set-Content -LiteralPath $stopPath -Value stop -Encoding ascii
    [void] $first.Process.WaitForExit(5000)
    Assert-Contract $first.Process.HasExited 'Simulator must exit after its governed stop marker.'

    $restartReadyPath = Join-Path $simulatorTestRoot 'restart.ready.json'
    $restartStopPath = Join-Path $simulatorTestRoot 'restart.stop'
    $restart = Start-ManagedBackgroundProcess `
        -Command (Get-Process -Id $PID).Path `
        -Arguments @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $simulatorPath, '-Port', "$port", '-ReadyPath', $restartReadyPath, '-StopPath', $restartStopPath) `
        -WorkingDirectory $repoRoot `
        -Name 'modbus-simulator-contract-restart' `
        -LogDirectory (Join-Path $simulatorTestRoot 'restart-logs')
    $managedProcesses.Add([pscustomobject]@{ Managed = $restart; StopPath = $restartStopPath })
    $restartReady = Wait-ReadyRecord -Path $restartReadyPath
    Assert-Contract ([int] $restartReady.port -eq $port) 'Simulator must restart ready on the exact same port.'
}
finally {
    foreach ($record in $managedProcesses) {
        try { Set-Content -LiteralPath $record.StopPath -Value stop -Encoding ascii -ErrorAction SilentlyContinue } catch { Write-Warning $_.Exception.Message }
        [void] $record.Managed.Stop.Invoke('Modbus simulator contract cleanup')
    }
    Remove-Item -LiteralPath $simulatorTestRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host 'Connector health disconnect verify-script contract tests passed.'
