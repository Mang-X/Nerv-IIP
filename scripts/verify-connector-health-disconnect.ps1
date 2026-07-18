# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Starts a loopback Modbus simulator and one isolated full-stack session
#   Writes:
#     - artifacts/script-logs/connector-health-disconnect/<timestamp>/evidence.json
#     - Managed full-stack session manifests and diagnostics
#   Cleanup:
#     - Stops the simulator and exact full-stack session in finally
#   Requires:
#     - PowerShell 7
#     - Aspire CLI 13.4.x
#     - Docker

[CmdletBinding()]
param(
    [ValidateRange(1, 10)] [int] $Runs = 1,
    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')
. (Join-Path $repoRoot 'scripts/lib/FullStackSessionState.ps1')
. (Join-Path $repoRoot 'scripts/lib/FullStackSessionRuntime.ps1')

$DisconnectDeadlineMilliseconds = 10000
$organizationId = 'org-001'
$environmentId = 'env-dev'
$connectorId = 'modbus-acceptance'
$sampledTagKey = 'acceptance.sampled'
$neverSampledTagKey = 'acceptance.never-sampled'
$timestamp = [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssfffZ')
$artifactRoot = Join-Path $repoRoot "artifacts/script-logs/connector-health-disconnect/$timestamp"
[System.IO.Directory]::CreateDirectory($artifactRoot) | Out-Null
$evidencePath = Join-Path $artifactRoot 'evidence.json'
$diagnosticsPath = Join-Path $artifactRoot 'diagnostics.json'
$simulatorScript = Join-Path $repoRoot 'scripts/support/modbus-tcp-simulator.ps1'
$fullStackScript = Join-Path $repoRoot 'scripts/fullstack-session.ps1'
$sessionId = New-NervFullStackSessionId -WorktreeRoot $repoRoot
$adminPassword = New-NervFullStackSecretValue -Bytes 24
$simulator = $null
$manifest = $null
$fullStackStartIdentity = $null
$fullStackStartStdoutPath = Join-Path $artifactRoot 'start.stdout.log'
$fullStackStartStderrPath = Join-Path $artifactRoot 'start.stderr.log'
$evidenceRuns = [System.Collections.Generic.List[object]]::new()
$script:currentStage = 'initializing'
$script:lastRequestError = $null
$script:lastHealth = $null
$script:lastCoverage = $null
$script:disconnectStartUtc = $null

function Write-AcceptanceDiagnostics([string] $Status = 'running', [object] $Failure = $null) {
    try {
        [ordered]@{
            status = $Status
            currentStage = $script:currentStage
            observedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
            sessionId = $sessionId
            connectorId = $connectorId
            disconnectStartUtc = if ($null -eq $script:disconnectStartUtc) { $null } else { $script:disconnectStartUtc.ToString('O') }
            lastRequestError = $script:lastRequestError
            lastHealth = $script:lastHealth
            lastCoverage = $script:lastCoverage
            failure = $Failure
        } | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $diagnosticsPath -Encoding utf8
    }
    catch {
        Write-Diagnostic -Level WARN -Message "Could not persist connector acceptance diagnostics: $($_.Exception.Message)"
    }
}

function Set-AcceptanceStage([string] $Stage) {
    $script:currentStage = $Stage
    Write-AcceptanceDiagnostics
    Write-Diagnostic -Level INFO -Message "Connector health acceptance stage: $Stage"
}

function Get-FullStackStartErrorTail {
    if (-not (Test-Path -LiteralPath $fullStackStartStderrPath -PathType Leaf)) { return '<no stderr>' }
    $stderr = Protect-NervFullStackDiagnosticText `
        -Text (Get-Content -LiteralPath $fullStackStartStderrPath -Raw) `
        -SensitiveValues @($adminPassword)
    if ($stderr.Length -gt 2000) { return $stderr.Substring($stderr.Length - 2000) }
    return $stderr
}

function Wait-FullStackSessionRunning([int] $TimeoutSeconds = 900) {
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTimeOffset]::UtcNow -lt $deadline) {
        $manifestPath = Get-NervFullStackManifestPath -SessionId $sessionId
        if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
            $candidate = Read-NervFullStackManifest -SessionId $sessionId
            if ("$($candidate.state)" -in @('Failed', 'CleanupFailed')) {
                throw "Full-stack session '$sessionId' entered startup terminal state '$($candidate.state)': $($candidate.failure.message)"
            }
            if ("$($candidate.state)" -eq 'Running') { return $candidate }
        }
        if ($null -ne $fullStackStartIdentity -and
            -not (Test-NervProcessIdentity -ProcessId $fullStackStartIdentity.Pid -ProcessStartTimeUtc $fullStackStartIdentity.ProcessStartTimeUtc)) {
            throw "Full-stack start wrapper exited before session '$sessionId' reached Running. $(Get-FullStackStartErrorTail)"
        }
        Start-Sleep -Seconds 2
    }
    throw "Timed out waiting for full-stack session '$sessionId' to reach Running. $(Get-FullStackStartErrorTail)"
}

function Stop-FullStackStartProcess {
    if ($null -eq $fullStackStartIdentity) { return }
    if (-not (Test-NervProcessIdentity -ProcessId $fullStackStartIdentity.Pid -ProcessStartTimeUtc $fullStackStartIdentity.ProcessStartTimeUtc)) { return }
    $process = Get-Process -Id $fullStackStartIdentity.Pid -ErrorAction SilentlyContinue
    if ($null -eq $process) { return }
    try {
        Stop-Process -Id $fullStackStartIdentity.Pid -Force
        [void] $process.WaitForExit(10000)
    }
    finally {
        $process.Dispose()
    }
}

function Assert-Acceptance([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Wait-ReadyJson([string] $Path, [int] $TimeoutSeconds = 10) {
    $timer = [System.Diagnostics.Stopwatch]::StartNew()
    while ($timer.Elapsed.TotalSeconds -lt $TimeoutSeconds) {
        if (Test-Path -LiteralPath $Path -PathType Leaf) {
            try {
                $ready = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
                if ("$($ready.state)" -eq 'ready' -and [int] $ready.port -gt 0) { return $ready }
            }
            catch {
                $script:lastRequestError = "Simulator ready JSON '$Path' could not be read: $($_.Exception.Message)"
                Write-AcceptanceDiagnostics
            }
        }
        Start-Sleep -Milliseconds 25
    }
    throw "Timed out waiting for Modbus simulator ready JSON '$Path'."
}

function Start-ModbusSimulator([int] $Port, [int] $Generation) {
    $readyPath = Join-Path $artifactRoot "simulator-$Generation.ready.json"
    $stopPath = Join-Path $artifactRoot "simulator-$Generation.stop"
    Remove-Item -LiteralPath $readyPath, $stopPath -Force -ErrorAction SilentlyContinue
    $managed = Start-ManagedBackgroundProcess `
        -Command (Get-Process -Id $PID).Path `
        -Arguments @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $simulatorScript, '-Port', "$Port", '-ReadyPath', $readyPath, '-StopPath', $stopPath) `
        -WorkingDirectory $repoRoot `
        -Name "connector-health-modbus-$Generation" `
        -LogDirectory (Join-Path $artifactRoot "simulator-$Generation-logs")
    try {
        $ready = Wait-ReadyJson -Path $readyPath
        return [pscustomobject]@{ Managed = $managed; Ready = $ready; ReadyPath = $readyPath; StopPath = $stopPath }
    }
    catch {
        [void] $managed.Stop.Invoke('Simulator did not become ready')
        throw
    }
}

function Stop-ModbusSimulator([object] $Record) {
    if ($null -eq $Record) { return }
    try {
        Set-Content -LiteralPath $Record.StopPath -Value 'stop' -Encoding ascii
        [void] $Record.Managed.Process.WaitForExit(5000)
        Assert-Acceptance $Record.Managed.Process.HasExited 'Modbus simulator did not close its socket and listener within five seconds.'
    }
    finally {
        [void] $Record.Managed.Stop.Invoke('Connector health acceptance simulator cleanup')
    }
}

function Invoke-JsonRequest(
    [string] $Method,
    [string] $Uri,
    [hashtable] $Headers = @{},
    [object] $Body = $null) {
    $parameters = @{ Method = $Method; Uri = $Uri; Headers = $Headers; TimeoutSec = 30 }
    if ($null -ne $Body) {
        $parameters.Body = $Body | ConvertTo-Json -Depth 20
        $parameters.ContentType = 'application/json'
    }
    try {
        $result = Invoke-RestMethod @parameters
        $script:lastRequestError = $null
        return $result
    }
    catch {
        $script:lastRequestError = Protect-NervFullStackDiagnosticText `
            -Text "$Method $Uri failed: $($_.Exception.Message)" `
            -SensitiveValues @($adminPassword)
        Write-AcceptanceDiagnostics
        throw
    }
}

function Get-Health([string] $BusinessGatewayUrl, [hashtable] $Headers) {
    $uri = "$BusinessGatewayUrl/api/business-console/v1/telemetry/connectors/$connectorId/collection-health?organizationId=$organizationId&environmentId=$environmentId"
    $script:lastHealth = (Invoke-JsonRequest -Method Get -Uri $uri -Headers $Headers).data
    return $script:lastHealth
}

function Get-Coverage([string] $BusinessGatewayUrl, [hashtable] $Headers) {
    $uri = "$BusinessGatewayUrl/api/business-console/v1/telemetry/connectors/$connectorId/tag-coverage?organizationId=$organizationId&environmentId=$environmentId"
    $script:lastCoverage = (Invoke-JsonRequest -Method Get -Uri $uri -Headers $Headers).data
    return $script:lastCoverage
}

function Wait-InitialState([string] $BusinessGatewayUrl, [hashtable] $Headers) {
    $deadline = [DateTimeOffset]::UtcNow.AddMinutes(2)
    do {
        try {
            $health = Get-Health -BusinessGatewayUrl $BusinessGatewayUrl -Headers $Headers
            $coverage = Get-Coverage -BusinessGatewayUrl $BusinessGatewayUrl -Headers $Headers
            $neverSampled = @($coverage.items | Where-Object { "$($_.tagKey)" -eq $neverSampledTagKey -and $null -eq $_.lastSampleAtUtc })
            if ("$($health.connection.status)" -eq 'alive' -and "$($coverage.manifestStatus)" -eq 'current' -and $neverSampled.Count -eq 1) {
                return [pscustomobject]@{ Health = $health; Coverage = $coverage; NeverSampled = $neverSampled[0] }
            }
        }
        catch {
            Write-Diagnostic -Level WARN -Message "Initial connector state observation failed: $($script:lastRequestError ?? $_.Exception.Message)"
        }
        Start-Sleep -Milliseconds 200
    } while ([DateTimeOffset]::UtcNow -lt $deadline)
    $healthStatus = if ($null -eq $script:lastHealth) { '<unobserved>' } else { "$($script:lastHealth.connection.status)/$($script:lastHealth.status)" }
    $manifestStatus = if ($null -eq $script:lastCoverage) { '<unobserved>' } else { "$($script:lastCoverage.manifestStatus)" }
    throw "Timed out waiting for explicit alive state and current connector tag manifest. Last health=$healthStatus; manifest=$manifestStatus; last request error=$($script:lastRequestError ?? '<none>')."
}

try {
    Set-AcceptanceStage -Stage 'simulator-starting'
    $simulator = Start-ModbusSimulator -Port 0 -Generation 0
    Set-AcceptanceStage -Stage 'simulator-ready'
    $modbusPort = [int] $simulator.Ready.port
    $startArguments = @('start', '-SessionId', $sessionId)
    if ($NoBuild) { $startArguments += '-NoBuild' }
    $detachedStartArguments = @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $fullStackScript
    ) + $startArguments
    Set-AcceptanceStage -Stage 'fullstack-starting'
    $fullStackStartIdentity = Invoke-WithScopedEnvironment -Variables @{
        NERV_IIP_FULLSTACK_ADMIN_PASSWORD = $adminPassword
        ConnectorHealthAcceptance__Enabled = 'true'
        ConnectorHealthAcceptance__ModbusEndpoint = "127.0.0.1:$modbusPort"
    } -ScriptBlock {
        Start-DetachedManagedProcess `
            -Command (Get-Process -Id $PID).Path `
            -Arguments $detachedStartArguments `
            -WorkingDirectory $repoRoot `
            -StdoutPath $fullStackStartStdoutPath `
            -StderrPath $fullStackStartStderrPath
    }
    $manifest = Wait-FullStackSessionRunning
    Set-AcceptanceStage -Stage 'fullstack-running'
    Assert-Acceptance ("$($manifest.state)" -eq 'Running') "Full-stack session '$sessionId' did not reach Running."
    $gatewayUrl = Get-NervFullStackEndpointValue -Manifest $manifest -ResourceName 'gateway'
    $businessGatewayUrl = Get-NervFullStackEndpointValue -Manifest $manifest -ResourceName 'business-gateway'
    Set-AcceptanceStage -Stage 'gateway-login'
    $login = Invoke-JsonRequest -Method Post -Uri "$gatewayUrl/api/console/v1/auth/login" -Body @{ loginName = 'admin'; password = $adminPassword }
    $accessToken = "$($login.data.accessToken)"
    Assert-Acceptance (-not [string]::IsNullOrWhiteSpace($accessToken)) 'Managed full-stack admin login did not return an access token.'
    $headers = @{ Authorization = "Bearer $accessToken" }

    Set-AcceptanceStage -Stage 'initial-state-waiting'
    $initial = Wait-InitialState -BusinessGatewayUrl $businessGatewayUrl -Headers $headers
    Set-AcceptanceStage -Stage 'initial-state-ready'
    for ($run = 1; $run -le $Runs; $run++) {
        $before = Get-Health -BusinessGatewayUrl $businessGatewayUrl -Headers $headers
        Assert-Acceptance ("$($before.connection.status)" -eq 'alive') "Run $run did not start from explicit alive state."
        $previousConnectedSinceUtc = [DateTimeOffset] $before.connection.connectedSinceUtc
        $baselineHeartbeatUtc = [DateTimeOffset] $before.lastHeartbeatAtUtc
        $script:disconnectStartUtc = [DateTimeOffset]::UtcNow
        $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
        Set-AcceptanceStage -Stage "run-$run-disconnecting"
        Stop-ModbusSimulator -Record $simulator
        $simulator = $null

        $lost = $null
        while ($stopwatch.ElapsedMilliseconds -lt $DisconnectDeadlineMilliseconds) {
            try {
                $candidate = Get-Health -BusinessGatewayUrl $businessGatewayUrl -Headers $headers
                $heartbeatUtc = if ($candidate.lastHeartbeatAtUtc) { [DateTimeOffset] $candidate.lastHeartbeatAtUtc } else { [DateTimeOffset]::MinValue }
                if ("$($candidate.connection.status)" -eq 'lost' -and
                    "$($candidate.status)" -eq 'stale' -and
                    "$($candidate.staleReason)" -eq 'offline' -and
                    "$($candidate.offlineReason)" -eq 'field-connection' -and
                    $candidate.connection.disconnectedSinceUtc -and
                    $heartbeatUtc -gt $baselineHeartbeatUtc) {
                    $lost = $candidate
                    break
                }
            }
            catch {
                Write-Diagnostic -Level WARN -Message "Run $run disconnect observation failed: $($script:lastRequestError ?? $_.Exception.Message)"
            }
            Start-Sleep -Milliseconds 100
        }
        $stopwatch.Stop()
        Assert-Acceptance ($null -ne $lost) "Run $run did not expose lost/offline/field-connection with an advancing Host heartbeat before the fixed 10-second deadline."
        Assert-Acceptance ($stopwatch.ElapsedMilliseconds -lt $DisconnectDeadlineMilliseconds) "Run $run exceeded the fixed 10-second disconnect deadline."
        $gatewayObservedAtUtc = [DateTimeOffset]::UtcNow
        $disconnectedSinceUtc = [DateTimeOffset] $lost.connection.disconnectedSinceUtc
        Assert-Acceptance ($disconnectedSinceUtc.UtcTicks -ge $script:disconnectStartUtc.UtcTicks) "Run $run reported disconnectedSinceUtc before the simulator disconnect started (disconnectStartUtc=$($script:disconnectStartUtc.ToString('O')); disconnectedSinceUtc=$($disconnectedSinceUtc.ToString('O')))."
        Assert-Acceptance ($disconnectedSinceUtc -le $gatewayObservedAtUtc) "Run $run reported disconnectedSinceUtc after the Gateway observation."

        Set-AcceptanceStage -Stage "run-$run-restarting"
        $simulator = Start-ModbusSimulator -Port $modbusPort -Generation $run
        Assert-Acceptance ([int] $simulator.Ready.port -eq $modbusPort) "Run $run simulator did not restart on the same port."
        $recoveryDeadline = [DateTimeOffset]::UtcNow.AddSeconds(15)
        $recovered = $null
        do {
            try {
                $candidate = Get-Health -BusinessGatewayUrl $businessGatewayUrl -Headers $headers
                if ("$($candidate.connection.status)" -eq 'alive' -and
                    [DateTimeOffset] $candidate.connection.connectedSinceUtc -gt $previousConnectedSinceUtc) {
                    $recovered = $candidate
                    break
                }
            }
            catch {
                Write-Diagnostic -Level WARN -Message "Run $run recovery observation failed: $($script:lastRequestError ?? $_.Exception.Message)"
            }
            Start-Sleep -Milliseconds 100
        } while ([DateTimeOffset]::UtcNow -lt $recoveryDeadline)
        Assert-Acceptance ($null -ne $recovered) "Run $run did not establish a new alive interval after simulator restart."
        $coverage = Get-Coverage -BusinessGatewayUrl $businessGatewayUrl -Headers $headers
        $neverSampled = @($coverage.items | Where-Object { "$($_.tagKey)" -eq $neverSampledTagKey -and $null -eq $_.lastSampleAtUtc })
        Assert-Acceptance ($neverSampled.Count -eq 1) "Run $run lost the configured never-sampled tag from coverage."
        Assert-Acceptance (@($coverage.items | Where-Object { "$($_.tagKey)" -eq $sampledTagKey -and $null -ne $_.lastSampleAtUtc }).Count -eq 1) "Run $run did not retain the sampled mapping fact."

        $evidenceRuns.Add([ordered]@{
            run = $run
            disconnectStartUtc = $script:disconnectStartUtc.ToString('O')
            connectionObservedAtUtc = ([DateTimeOffset] $lost.connection.observedAtUtc).ToString('O')
            gatewayObservedAtUtc = $gatewayObservedAtUtc.ToString('O')
            elapsedMilliseconds = $stopwatch.ElapsedMilliseconds
            disconnectedSinceUtc = $disconnectedSinceUtc.ToString('O')
            lastHeartbeatAtUtc = ([DateTimeOffset] $lost.lastHeartbeatAtUtc).ToString('O')
            recoveryObservedAtUtc = ([DateTimeOffset] $recovered.connection.observedAtUtc).ToString('O')
            recoveredConnectedSinceUtc = ([DateTimeOffset] $recovered.connection.connectedSinceUtc).ToString('O')
            neverSampled = [ordered]@{
                tagKey = "$($neverSampled[0].tagKey)"
                activationStatus = "$($neverSampled[0].activationStatus)"
                lastSampleAtUtc = $neverSampled[0].lastSampleAtUtc
            }
        })
        $maximumElapsedMilliseconds = @(
            $evidenceRuns |
                ForEach-Object { [long] $_['elapsedMilliseconds'] } |
                Measure-Object -Maximum
        )[0].Maximum
        [ordered]@{
            sessionId = $sessionId
            connectorId = $connectorId
            fixedDeadlineMilliseconds = $DisconnectDeadlineMilliseconds
            maximumElapsedMilliseconds = $maximumElapsedMilliseconds
            runs = $evidenceRuns
        } | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $evidencePath -Encoding utf8
        Set-AcceptanceStage -Stage "run-$run-passed"
    }
}
catch {
    $safeFailureMessage = Protect-NervFullStackDiagnosticText -Text $_.Exception.Message -SensitiveValues @($adminPassword)
    $failure = [ordered]@{
        status = 'failed'
        failedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        exceptionType = $_.Exception.GetType().FullName
        message = $safeFailureMessage
    }
    Write-AcceptanceDiagnostics -Status 'failed' -Failure $failure
    throw
}
finally {
    $adminPassword = $null
    if ($null -ne $simulator) {
        try { Stop-ModbusSimulator -Record $simulator } catch { Write-Diagnostic -Level WARN -Message "Simulator cleanup failed: $($_.Exception.Message)" }
    }
    try { Stop-FullStackStartProcess } catch { Write-Diagnostic -Level WARN -Message "Full-stack start-wrapper cleanup failed: $($_.Exception.Message)" }
    $manifestPath = Get-NervFullStackManifestPath -SessionId $sessionId
    if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
        try {
            Invoke-PwshScript `
                -ScriptPath $fullStackScript `
                -Arguments @('stop', '-SessionId', $sessionId) `
                -WorkingDirectory $repoRoot `
                -TimeoutSeconds 300 `
                -Name 'connector-health-fullstack-stop' | Out-Null
        }
        catch { Write-Diagnostic -Level WARN -Message "Full-stack cleanup failed: $($_.Exception.Message)" }
    }
}

Set-AcceptanceStage -Stage 'completed'
Write-AcceptanceDiagnostics -Status 'passed'
Write-Host "Connector health disconnect acceptance passed for $Runs run(s). Evidence: $evidencePath"
