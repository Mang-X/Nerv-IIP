$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$verifyPath = Join-Path $repoRoot 'scripts/verify-connector-health-disconnect.ps1'
$simulatorPath = Join-Path $repoRoot 'scripts/support/modbus-tcp-simulator.ps1'
$appHostPath = Join-Path $repoRoot 'infra/aspire/Nerv.IIP.AppHost/Program.cs'

function Assert-Contract([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

Assert-Contract (Test-Path -LiteralPath $verifyPath -PathType Leaf) 'Connector disconnect verify script is missing.'
Assert-Contract (Test-Path -LiteralPath $simulatorPath -PathType Leaf) 'Loopback Modbus simulator script is missing.'

$verify = Get-Content -LiteralPath $verifyPath -Raw
$simulator = Get-Content -LiteralPath $simulatorPath -Raw
$appHost = Get-Content -LiteralPath $appHostPath -Raw

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
Assert-Contract ($verify.Contains('diagnostics.json')) 'Verify script must persist stage diagnostics independently of success evidence.'
Assert-Contract ($verify.Contains('currentStage')) 'Diagnostics must identify the current acceptance stage.'
Assert-Contract ($verify.Contains('lastRequestError')) 'Diagnostics must preserve the last health or coverage request error.'
Assert-Contract ($verify.Contains('Write-AcceptanceDiagnostics')) 'Verify script must update diagnostics while it runs and when it fails.'
Assert-Contract ($verify.Contains("status = 'failed'")) 'Verify script must persist an explicit failed terminal status.'
Assert-Contract (-not $verify.Contains('catch { }')) 'Verify script must not silently swallow request or state-observation failures.'
Assert-Contract ($verify.Contains('Start-DetachedManagedProcess')) 'Full-stack start must be detached so Aspire descendants cannot hold the verification pipe open.'
Assert-Contract ($verify.Contains('Wait-FullStackSessionRunning')) 'Verify script must wait on the governed session manifest instead of start-process EOF.'
Assert-Contract ($verify.Contains('Test-NervProcessIdentity')) 'Detached start cleanup must verify the exact process identity.'
Assert-Contract ($verify.Contains('function Stop-FullStackStartProcess')) 'Verify script must own explicit detached-wrapper cleanup.'
Assert-Contract (
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

Write-Host 'Connector health disconnect verify-script contract tests passed.'
