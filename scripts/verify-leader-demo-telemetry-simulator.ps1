# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Publishes simulated vibration, temperature, and device-state facts to the exact current leader-demo session through public BusinessGateway HTTP
#     - Optionally probes and publishes historical telemetry through the same public facade
#   Writes:
#     - Redacted JSON and Markdown evidence under artifacts/leader-demo
#   Cleanup:
#     - Runs in the foreground and creates no background process
#     - Finalizes evidence on success or handled failure
#   Requires:
#     - PowerShell 7
#     - A current healthy leader-demo session created by .\nerv.ps1 demo start or reset
#     - NERV_IIP_LEADER_DEMO_ADMIN_PASSWORD in the current process

[CmdletBinding()]
param(
    [ValidateRange(1, 120)]
    [int] $DurationMinutes = 10,

    [ValidateRange(1, 60)]
    [int] $SampleIntervalSeconds = 2,

    [ValidateRange(0.1, 119)]
    [double] $DegradingAtMinutes = 2,

    [ValidateRange(0.2, 119)]
    [double] $AlarmAtMinutes = 5,

    [ValidateRange(0.3, 119)]
    [double] $RecoveredAtMinutes = 8,

    [switch] $HistoricalBackfill,

    [ValidateRange(1, 168)]
    [int] $HistoricalHours = 24,

    [ValidateRange(1, 1440)]
    [int] $HistoricalIntervalMinutes = 15,

    [ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9._-]{0,47}$')]
    [string] $RunId,

    [Nullable[DateTimeOffset]] $ScenarioStartUtc
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')
. (Join-Path $repoRoot 'scripts/lib/FullStackSessionRuntime.ps1')
. (Join-Path $repoRoot 'scripts/lib/LeaderDemoTelemetrySimulator.ps1')

$password = $null
$accessToken = $null
$sessionId = 'unresolved'
$effectiveRunId = $RunId
$evidenceRoot = Join-Path $repoRoot 'artifacts/leader-demo'
$startedAtUtc = [DateTimeOffset]::UtcNow
$simulation = $null
$failureMessage = $null

try {
    $password = Get-NervLeaderDemoAdminPassword
    $ownedSession = Resolve-NervLeaderDemoOwnedSession `
        -StateRoot (Get-NervFullStackStateRoot) `
        -ExpectedWorktreeRoot $repoRoot
    $manifest = $ownedSession.Manifest
    $sessionId = "$($ownedSession.SessionId)"
    if ("$(Get-NervObjectPropertyValue -InputObject $manifest -Name 'state')" -cne 'Running') {
        throw "Leader-demo session '$sessionId' is not Running."
    }
    if ("$(Get-NervObjectPropertyValue -InputObject $manifest -Name 'messagingProvider')" -cne 'Redis') {
        throw "Leader-demo session '$sessionId' is not using the required Redis messaging profile."
    }

    $endpoints = Get-NervObjectPropertyValue -InputObject $manifest -Name 'endpoints'
    $gatewayUrl = "$(Get-NervObjectPropertyValue -InputObject $endpoints -Name 'gateway')".TrimEnd('/')
    $businessGatewayUrl = "$(Get-NervObjectPropertyValue -InputObject $endpoints -Name 'business-gateway')".TrimEnd('/')
    if ([string]::IsNullOrWhiteSpace($gatewayUrl) -or [string]::IsNullOrWhiteSpace($businessGatewayUrl)) {
        throw "Leader-demo session '$sessionId' has no public Gateway endpoint set."
    }

    $loginResponse = Invoke-RestMethod `
        -Method Post `
        -Uri "$gatewayUrl/api/console/v1/auth/login" `
        -Body (@{ loginName = 'admin'; password = $password } | ConvertTo-Json -Compress) `
        -ContentType 'application/json' `
        -TimeoutSec 30
    $loginData = Get-NervLeaderDemoResponseData -Response $loginResponse
    $accessToken = "$(Get-NervObjectPropertyValue -InputObject $loginData -Name 'accessToken')"
    if ([string]::IsNullOrWhiteSpace($accessToken)) {
        throw 'Platform Gateway login returned no access token.'
    }
    $headers = @{ Authorization = "Bearer $accessToken" }
    $httpAction = {
        param($Method, $Path, $Body)
        $uri = "$businessGatewayUrl$Path"
        if ($Method -ceq 'POST') {
            return Invoke-RestMethod `
                -Method Post `
                -Uri $uri `
                -Headers $headers `
                -Body ($Body | ConvertTo-Json -Depth 20 -Compress) `
                -ContentType 'application/json' `
                -TimeoutSec 30
        }
        return Invoke-RestMethod -Method Get -Uri $uri -Headers $headers -TimeoutSec 30
    }

    $effectiveStart = if ($null -ne $ScenarioStartUtc) {
        ([DateTimeOffset]$ScenarioStartUtc).ToUniversalTime()
    }
    else {
        [DateTimeOffset]::UtcNow
    }
    if ([string]::IsNullOrWhiteSpace($effectiveRunId)) {
        $effectiveRunId = "$sessionId-$($effectiveStart.UtcDateTime.ToString('yyyyMMddHHmmss'))"
    }
    $durationSeconds = $DurationMinutes * 60
    $degradingAtSeconds = [int][Math]::Round($DegradingAtMinutes * 60)
    $alarmAtSeconds = [int][Math]::Round($AlarmAtMinutes * 60)
    $recoveredAtSeconds = [int][Math]::Round($RecoveredAtMinutes * 60)
    if (-not ($degradingAtSeconds -lt $alarmAtSeconds -and
        $alarmAtSeconds -lt $recoveredAtSeconds -and
        $recoveredAtSeconds -lt $durationSeconds)) {
        throw 'Profile transition minutes must be strictly ordered and all occur before DurationMinutes ends.'
    }

    Write-Diagnostic -Level INFO -Message "Starting foreground leader-demo telemetry run '$effectiveRunId' for exact session '$sessionId'."
    $simulationParameters = @{
        OrganizationId = 'org-001'
        EnvironmentId = 'env-dev'
        DeviceAssetId = 'DEV-CNC-DEMO'
        RunId = $effectiveRunId
        ScenarioStartUtc = $effectiveStart
        DurationSeconds = $durationSeconds
        SampleIntervalSeconds = $SampleIntervalSeconds
        DegradingAtSeconds = $degradingAtSeconds
        AlarmAtSeconds = $alarmAtSeconds
        RecoveredAtSeconds = $recoveredAtSeconds
        HistoricalHours = $HistoricalHours
        HistoricalIntervalMinutes = $HistoricalIntervalMinutes
        HttpAction = $httpAction
    }
    if ($HistoricalBackfill) { $simulationParameters['HistoricalBackfill'] = $true }
    $simulation = Invoke-NervLeaderDemoTelemetrySimulator @simulationParameters
}
catch {
    $failureMessage = Protect-ScriptAutomationText -Text "$($_.Exception.Message)"
    $simulation = [pscustomobject][ordered]@{
        Result = 'failed'
        RunId = $effectiveRunId
        HistoricalBackfill = [pscustomobject]@{ Mode = 'not-completed'; PublishedCount = 0 }
        Live = [pscustomobject]@{ PublishedPointCount = 0 }
        Replay = [pscustomobject]@{ IdentityStable = $false }
        History = [pscustomobject]@{ ItemCount = 0 }
        Alarm = [pscustomobject]@{ Status = $null }
        Failure = $failureMessage
    }
}
finally {
    if ([string]::IsNullOrWhiteSpace($effectiveRunId)) { $effectiveRunId = 'unresolved' }
    $commit = try {
        (Invoke-NativeCommandOutput `
            -Command 'git' `
            -Arguments @('rev-parse', 'HEAD') `
            -WorkingDirectory $repoRoot `
            -TimeoutSeconds 30 `
            -Name 'leader-demo-telemetry-commit').Stdout.Trim()
    }
    catch {
        'unavailable'
    }
    $evidence = [pscustomobject][ordered]@{
        schemaVersion = 1
        issue = 'MAN-601/#1086'
        sessionId = $sessionId
        commit = $commit
        startedAtUtc = $startedAtUtc.ToString('O')
        completedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        publicWritePath = '/api/business-console/v1/telemetry/samples'
        backgroundProcessesCreated = 0
        simulation = $simulation
    }
    $paths = Write-NervLeaderDemoTelemetryEvidence `
        -Evidence $evidence `
        -EvidenceRoot $evidenceRoot `
        -SessionId $sessionId `
        -RunId $effectiveRunId `
        -SensitiveValues @($password, $accessToken)
    Write-Diagnostic -Level INFO -Message "Leader-demo telemetry evidence: $($paths.JsonPath)"
}

if ($null -ne $failureMessage) {
    Write-Error $failureMessage
    exit 1
}

if ($simulation.Result -cne 'completed') {
    Write-Error "Leader-demo telemetry simulator ended with result '$($simulation.Result)'."
    exit 2
}
if (-not $simulation.Replay.IdentityStable) {
    Write-Error 'Leader-demo telemetry replay returned different fact identities.'
    exit 3
}
if (-not $simulation.Alarm.Found -or $simulation.Alarm.Status -cne 'cleared') {
    Write-Error 'Leader-demo telemetry alarm lifecycle was not observed as cleared after recovery.'
    exit 4
}

Write-Diagnostic -Level INFO -Message "Leader-demo telemetry run '$effectiveRunId' completed without creating a background process."
exit 0
