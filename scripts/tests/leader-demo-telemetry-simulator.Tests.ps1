# Script-Governance:
#   Category: check
#   SideEffects:
#     - Exercises deterministic leader-demo telemetry simulation functions with injected actions
#   Writes:
#     - Temporary test data only
#   Cleanup:
#     - Removes temporary test data
#   Requires:
#     - PowerShell 7

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $repoRoot 'scripts/lib/LeaderDemoTelemetrySimulator.ps1')

function Assert-True {
    param([bool] $Condition, [string] $Message)
    if (-not $Condition) { throw $Message }
}

function Assert-Equal {
    param($Expected, $Actual, [string] $Message)
    if ($Expected -is [array] -or $Actual -is [array]) {
        $expectedJson = ConvertTo-Json @($Expected) -Compress
        $actualJson = ConvertTo-Json @($Actual) -Compress
        if ($expectedJson -cne $actualJson) {
            throw "$Message Expected '$expectedJson', got '$actualJson'."
        }
        return
    }

    if ($Expected -cne $Actual) {
        throw "$Message Expected '$Expected', got '$Actual'."
    }
}

$scenarioStart = [DateTimeOffset]::Parse('2026-07-24T00:00:00Z')
$sessionStartedAt = $scenarioStart.AddMinutes(-2)
$subMillisecondTimeline = @(
    New-NervLeaderDemoTelemetryTimeline `
        -RunId 'precision-001' `
        -ScenarioStartUtc ([DateTimeOffset]::Parse('2026-07-24T00:00:00.1234567Z')) `
        -DurationSeconds 8 `
        -SampleIntervalSeconds 2 `
        -DegradingAtSeconds 2 `
        -AlarmAtSeconds 4 `
        -RecoveredAtSeconds 6
)
Assert-Equal (
    [DateTimeOffset]::Parse('2026-07-24T00:00:00.123Z')
) (
    [DateTimeOffset]::Parse($subMillisecondTimeline[0].BucketStartUtc)
) 'Scenario timestamps must be truncated to Unix milliseconds for database-stable replay.'

$timeline = @(
    New-NervLeaderDemoTelemetryTimeline `
        -RunId 'rehearsal-001' `
        -ScenarioStartUtc $scenarioStart `
        -DurationSeconds 24 `
        -SampleIntervalSeconds 2 `
        -DegradingAtSeconds 6 `
        -AlarmAtSeconds 12 `
        -RecoveredAtSeconds 18
)

Assert-Equal `
    @('normal', 'degrading', 'alarm', 'recovered') `
    @($timeline | Select-Object -ExpandProperty Profile -Unique) `
    'The deterministic timeline must traverse the governed profile order.'
Assert-True (
    [decimal](($timeline | Where-Object Profile -eq 'degrading')[-1].Vibration) -lt [decimal]8
) 'The degrading profile must approach but remain below the vibration alarm threshold.'
Assert-True (
    [decimal](($timeline | Where-Object Profile -eq 'alarm')[0].Vibration) -gt [decimal]8
) 'The alarm profile must cross the vibration threshold.'
Assert-True (
    [decimal](($timeline | Where-Object Profile -eq 'recovered')[0].Vibration) -lt [decimal]7.7
) 'The recovered profile must fall below the rule deadband.'

$repeatedTimeline = @(
    New-NervLeaderDemoTelemetryTimeline `
        -RunId 'rehearsal-001' `
        -ScenarioStartUtc $scenarioStart `
        -DurationSeconds 24 `
        -SampleIntervalSeconds 2 `
        -DegradingAtSeconds 6 `
        -AlarmAtSeconds 12 `
        -RecoveredAtSeconds 18
)
Assert-Equal `
    ($timeline | ConvertTo-Json -Depth 10 -Compress) `
    ($repeatedTimeline | ConvertTo-Json -Depth 10 -Compress) `
    'Identical run inputs must produce byte-equivalent timeline facts.'

$degrading = $timeline | Where-Object Profile -eq 'degrading' | Select-Object -First 1
$vibrationBody = New-NervLeaderDemoTelemetrySampleBody `
    -OrganizationId 'org-001' `
    -EnvironmentId 'env-dev' `
    -DeviceAssetId 'DEV-CNC-DEMO' `
    -RunId 'rehearsal-001' `
    -Point $degrading `
    -TagKey 'vibration'
$temperatureBody = New-NervLeaderDemoTelemetrySampleBody `
    -OrganizationId 'org-001' `
    -EnvironmentId 'env-dev' `
    -DeviceAssetId 'DEV-CNC-DEMO' `
    -RunId 'rehearsal-001' `
    -Point $degrading `
    -TagKey 'temperature'

Assert-Equal 'leader-demo-simulator' $vibrationBody.sourceSystem 'Samples must use the governed source system.'
Assert-Equal 'business-gateway' $vibrationBody.sourceConnector 'Samples must declare the public facade connector.'
Assert-True ($vibrationBody.sourceSequence -match '^leader-demo:rehearsal-001:degrading:\d{6}:vibration$') 'Vibration source sequences must be stable and scoped.'
Assert-True ($temperatureBody.sourceSequence -match '^leader-demo:rehearsal-001:degrading:\d{6}:temperature$') 'Temperature source sequences must be stable and scoped.'
Assert-Equal 'running' $vibrationBody.deviceState 'Degrading must keep the device in a productive state.'
Assert-True ($null -eq $temperatureBody.deviceState) 'Only the vibration request may carry the device-state fact for a timeline point.'
Assert-Equal 2 ([int]([DateTimeOffset]::Parse($vibrationBody.bucketEndUtc) - [DateTimeOffset]::Parse($vibrationBody.bucketStartUtc)).TotalSeconds) 'Every live bucket must be exactly two seconds.'

$script:acceptedRequests = [System.Collections.Generic.List[object]]::new()
$script:acceptedPostPacingCount = 0
$script:acceptedHistoricalPacingCount = 0
$acceptedHttpAction = {
    param($Method, $Path, $Body)
    $script:acceptedRequests.Add([pscustomobject]@{ Method = $Method; Path = $Path; Body = $Body })
    if ($Method -ceq 'POST') {
        return [pscustomobject]@{
            data = [pscustomobject]@{
                telemetrySummaryId = "summary:$($Body.sourceSequence)"
                deviceStateSnapshotId = if ($null -ne $Body.deviceState) { "state:$($Body.sourceSequence)" } else { $null }
            }
        }
    }
    if ($Path -like '*/history*') {
        return [pscustomobject]@{
            data = [pscustomobject]@{
                items = @(
                    [pscustomobject]@{
                        itemType = 'sample'
                        deviceAssetId = 'DEV-CNC-DEMO'
                        tagKey = 'vibration'
                        value = '8.7'
                        occurredAtUtc = '2026-07-24T00:00:06Z'
                    }
                )
            }
        }
    }
    return [pscustomobject]@{
        data = [pscustomobject]@{
            items = @(
                [pscustomobject]@{
                    alarmCode = 'VIBRATION-HIGH'
                    externalAlarmId = 'ALARM-DEMO-001:2026-07-24T00:00:04.0000000+00:00'
                    status = 'cleared'
                    raisedAtUtc = '2026-07-24T00:00:04Z'
                    clearedAtUtc = '2026-07-24T00:00:08Z'
                }
            )
        }
    }
}

$acceptedEvidence = Invoke-NervLeaderDemoTelemetrySimulator `
    -OrganizationId 'org-001' `
    -EnvironmentId 'env-dev' `
    -DeviceAssetId 'DEV-CNC-DEMO' `
    -RunId 'accepted-001' `
    -SessionStartedAtUtc $sessionStartedAt `
    -ScenarioStartUtc $scenarioStart `
    -DurationSeconds 10 `
    -SampleIntervalSeconds 2 `
    -DegradingAtSeconds 2 `
    -AlarmAtSeconds 4 `
    -RecoveredAtSeconds 6 `
    -HistoricalBackfill `
    -HistoricalHours 24 `
    -HistoricalIntervalMinutes 360 `
    -HttpAction $acceptedHttpAction `
    -DelayAction { param($Seconds) } `
    -PostRequestPacingAction { $script:acceptedPostPacingCount++ } `
    -HistoricalPostRequestPacingAction { $script:acceptedHistoricalPacingCount++ }

Assert-Equal 'accepted' $acceptedEvidence.HistoricalBackfill.Mode 'An accepted late sample must keep the full historical backfill mode.'
Assert-True ($acceptedEvidence.HistoricalBackfill.PublishedCount -ge 8) 'The accepted 24-hour shape must publish both governed tags oldest-to-newest.'
Assert-True $acceptedEvidence.Replay.IdentityStable 'The replay probe must return the same persisted identities.'
Assert-True ($acceptedEvidence.History.ItemCount -ge 1) 'The public history verification must observe run data.'
Assert-Equal 'cleared' $acceptedEvidence.Alarm.Status 'The alarm verification must observe the recovered lifecycle.'
Assert-True (
    @($script:acceptedRequests | Where-Object Method -eq 'POST' | Where-Object Path -ne '/api/business-console/v1/telemetry/samples').Count -eq 0
) 'All simulator fact writes must use only the public telemetry sample facade.'
Assert-Equal (
    @($script:acceptedRequests | Where-Object Method -eq 'POST').Count
) $script:acceptedPostPacingCount 'The replay pacing hook must run exactly once after every public POST.'
Assert-Equal (
    @($script:acceptedRequests | Where-Object {
        $_.Method -ceq 'POST' -and "$($_.Body.sourceSequence)" -match ':(history-probe|history):'
    }).Count
) $script:acceptedHistoricalPacingCount 'The historical pacing hook must run exactly once after every historical public POST.'
Assert-True (
    @($script:acceptedRequests | Where-Object Path -like '*/telemetry/devices/DEV-CNC-DEMO/history*').Count -eq 1
) 'The simulator must verify history through the public device history facade.'
Assert-True (
    @($script:acceptedRequests | Where-Object Path -like '/api/business-console/v1/telemetry/alarms*').Count -eq 1
) 'The simulator must verify alarms through the public alarm facade.'

$script:fallbackRequests = [System.Collections.Generic.List[object]]::new()
$fallbackHttpAction = {
    param($Method, $Path, $Body)
    $script:fallbackRequests.Add([pscustomobject]@{ Method = $Method; Path = $Path; Body = $Body })
    if ($Method -ceq 'POST' -and "$($Body.sourceSequence)" -like '*:history-probe:*') {
        $exception = [System.InvalidOperationException]::new('historical timestamp rejected by public facade')
        $exception.Data['HttpStatusCode'] = 400
        throw $exception
    }
    if ($Method -ceq 'POST') {
        return [pscustomobject]@{
            data = [pscustomobject]@{
                telemetrySummaryId = "summary:$($Body.sourceSequence)"
                deviceStateSnapshotId = if ($null -ne $Body.deviceState) { "state:$($Body.sourceSequence)" } else { $null }
            }
        }
    }
    if ($Path -like '*/history*') {
        return [pscustomobject]@{ data = [pscustomobject]@{ items = @() } }
    }
    return [pscustomobject]@{ data = [pscustomobject]@{ items = @() } }
}

$fallbackEvidence = Invoke-NervLeaderDemoTelemetrySimulator `
    -OrganizationId 'org-001' `
    -EnvironmentId 'env-dev' `
    -DeviceAssetId 'DEV-CNC-DEMO' `
    -RunId 'fallback-001' `
    -SessionStartedAtUtc $sessionStartedAt `
    -ScenarioStartUtc $scenarioStart `
    -DurationSeconds 10 `
    -SampleIntervalSeconds 2 `
    -DegradingAtSeconds 2 `
    -AlarmAtSeconds 4 `
    -RecoveredAtSeconds 6 `
    -HistoricalBackfill `
    -HistoricalHours 24 `
    -HistoricalIntervalMinutes 360 `
    -HttpAction $fallbackHttpAction `
    -DelayAction { param($Seconds) }

Assert-Equal 'rejected-fallback' $fallbackEvidence.HistoricalBackfill.Mode 'A rejected late sample must select the declared session-window fallback.'
Assert-True ($fallbackEvidence.HistoricalBackfill.Reason -like '*historical timestamp rejected*') 'The fallback evidence must retain a sanitized rejection reason.'
Assert-True (
    @($script:fallbackRequests | Where-Object { $_.Method -ceq 'POST' -and "$($_.Body.sourceSequence)" -like '*:session-backfill:*' }).Count -gt 0
) 'The rejected historical probe must be followed by explicit session-window samples.'
$fallbackBodies = @($script:fallbackRequests | Where-Object {
    $_.Method -ceq 'POST' -and "$($_.Body.sourceSequence)" -like '*:session-backfill:*'
} | Select-Object -ExpandProperty Body)
Assert-Equal $sessionStartedAt ([DateTimeOffset]::Parse($fallbackEvidence.HistoricalBackfill.WindowStartUtc)) 'Fallback must begin at the real session boundary when it is newer than the five-minute lookback.'
Assert-True (
    @($fallbackBodies | Where-Object { [DateTimeOffset]::Parse($_.bucketStartUtc) -lt $sessionStartedAt }).Count -eq 0
) 'Fallback must never publish a timestamp from before the exact leader-demo session.'

$script:invalidWindowRequests = 0
$invalidWindowRejected = $false
try {
    Invoke-NervLeaderDemoTelemetrySimulator `
        -OrganizationId 'org-001' `
        -EnvironmentId 'env-dev' `
        -DeviceAssetId 'DEV-CNC-DEMO' `
        -RunId 'invalid-window-001' `
        -SessionStartedAtUtc $scenarioStart.AddSeconds(1) `
        -ScenarioStartUtc $scenarioStart `
        -DurationSeconds 10 `
        -SampleIntervalSeconds 2 `
        -DegradingAtSeconds 2 `
        -AlarmAtSeconds 4 `
        -RecoveredAtSeconds 6 `
        -HistoricalBackfill `
        -HistoricalHours 24 `
        -HistoricalIntervalMinutes 360 `
        -HttpAction {
            param($Method, $Path, $Body)
            $script:invalidWindowRequests++
            return & $acceptedHttpAction $Method $Path $Body
        } `
        -DelayAction { param($Seconds) } | Out-Null
}
catch {
    $invalidWindowRejected = $_.Exception.Message.Contains('session start')
}
Assert-True $invalidWindowRejected 'A scenario before the exact session boundary must be rejected.'
Assert-Equal 0 $script:invalidWindowRequests 'An invalid session/scenario window must fail before publishing the historical probe.'

$script:transportRequests = [System.Collections.Generic.List[object]]::new()
$transportHttpAction = {
    param($Method, $Path, $Body)
    $script:transportRequests.Add([pscustomobject]@{ Method = $Method; Path = $Path; Body = $Body })
    if ($Method -ceq 'POST' -and "$($Body.sourceSequence)" -like '*:history-probe:*') {
        throw 'connection reset during historical probe'
    }
    return & $acceptedHttpAction $Method $Path $Body
}
$transportFailurePropagated = $false
try {
    Invoke-NervLeaderDemoTelemetrySimulator `
        -OrganizationId 'org-001' `
        -EnvironmentId 'env-dev' `
        -DeviceAssetId 'DEV-CNC-DEMO' `
        -RunId 'transport-001' `
        -SessionStartedAtUtc $sessionStartedAt `
        -ScenarioStartUtc $scenarioStart `
        -DurationSeconds 10 `
        -SampleIntervalSeconds 2 `
        -DegradingAtSeconds 2 `
        -AlarmAtSeconds 4 `
        -RecoveredAtSeconds 6 `
        -HistoricalBackfill `
        -HistoricalHours 24 `
        -HistoricalIntervalMinutes 360 `
        -HttpAction $transportHttpAction `
        -DelayAction { param($Seconds) } | Out-Null
}
catch {
    $transportFailurePropagated = $_.Exception.Message.Contains('connection reset')
}
Assert-True $transportFailurePropagated 'Transport failures must fail the run instead of being mislabeled as historical timestamp rejection.'
Assert-True (
    @($script:transportRequests | Where-Object {
        $_.Method -ceq 'POST' -and "$($_.Body.sourceSequence)" -like '*:session-backfill:*'
    }).Count -eq 0
) 'Transport failures must not publish fallback facts.'

$scenarioContract = New-NervLeaderDemoTelemetryScenarioContract `
    -DurationSeconds 600 `
    -SampleIntervalSeconds 2 `
    -DegradingAtSeconds 120 `
    -AlarmAtSeconds 300 `
    -RecoveredAtSeconds 480 `
    -HistoricalBackfill `
    -HistoricalHours 24 `
    -HistoricalIntervalMinutes 15
$baselineEvidence = [pscustomobject]@{
    executionMode = 'real-time'
    simulation = [pscustomobject]@{
        Result = 'completed'
        RunId = 'replay-contract-001'
        ScenarioStartUtc = $scenarioStart.ToString('O')
        Scenario = $scenarioContract
    }
}
$replayEvidenceRoot = Join-Path ([System.IO.Path]::GetTempPath()) "nerv-replay-contract-$([guid]::NewGuid().ToString('N'))"
try {
    $replaySessionDirectory = Join-Path $replayEvidenceRoot 'nerv-test-replay'
    [System.IO.Directory]::CreateDirectory($replaySessionDirectory) | Out-Null
    [System.IO.File]::WriteAllText(
        (Join-Path $replaySessionDirectory 'telemetry-simulator-replay-contract-001-baseline.json'),
        ($baselineEvidence | ConvertTo-Json -Depth 20),
        [System.Text.UTF8Encoding]::new($false)
    )
    $resolvedBaseline = Get-NervLeaderDemoTelemetryReplayBaseline `
        -EvidenceRoot $replayEvidenceRoot `
        -SessionId 'nerv-test-replay' `
        -RunId 'replay-contract-001' `
        -ScenarioStartUtc $scenarioStart
    Assert-Equal 'replay-contract-001' $resolvedBaseline.simulation.RunId 'Replay must resolve the completed real-time evidence for the exact session, run, and start instant.'
}
finally {
    if (Test-Path -LiteralPath $replayEvidenceRoot) {
        Remove-Item -LiteralPath $replayEvidenceRoot -Recurse -Force
    }
}
Assert-NervLeaderDemoTelemetryReplayContract `
    -BaselineEvidence $baselineEvidence `
    -RunId 'replay-contract-001' `
    -ScenarioStartUtc $scenarioStart `
    -ScenarioContract $scenarioContract
$replayMismatchRejected = $false
try {
    $mismatchedContract = New-NervLeaderDemoTelemetryScenarioContract `
        -DurationSeconds 1200 `
        -SampleIntervalSeconds 2 `
        -DegradingAtSeconds 120 `
        -AlarmAtSeconds 300 `
        -RecoveredAtSeconds 480 `
        -HistoricalBackfill `
        -HistoricalHours 24 `
        -HistoricalIntervalMinutes 15
    Assert-NervLeaderDemoTelemetryReplayContract `
        -BaselineEvidence $baselineEvidence `
        -RunId 'replay-contract-001' `
        -ScenarioStartUtc $scenarioStart `
        -ScenarioContract $mismatchedContract
}
catch {
    $replayMismatchRejected = $_.Exception.Message.Contains('DurationSeconds')
}
Assert-True $replayMismatchRejected 'Replay must reject any timeline parameter that differs from the completed real-time evidence.'

$script:cancellationChecks = 0
$cancelEvidence = Invoke-NervLeaderDemoTelemetrySimulator `
    -OrganizationId 'org-001' `
    -EnvironmentId 'env-dev' `
    -DeviceAssetId 'DEV-CNC-DEMO' `
    -RunId 'cancel-001' `
    -SessionStartedAtUtc $sessionStartedAt `
    -ScenarioStartUtc $scenarioStart `
    -DurationSeconds 10 `
    -SampleIntervalSeconds 2 `
    -DegradingAtSeconds 2 `
    -AlarmAtSeconds 4 `
    -RecoveredAtSeconds 6 `
    -HttpAction $acceptedHttpAction `
    -DelayAction { param($Seconds) } `
    -CancellationCheckAction {
        $script:cancellationChecks++
        return $script:cancellationChecks -ge 2
    }

Assert-Equal 'stopped' $cancelEvidence.Result 'Cancellation must stop the foreground loop cleanly.'
Assert-True ($cancelEvidence.Live.PublishedPointCount -lt 5) 'Cancellation must stop before the complete timeline is published.'

Write-Host 'Leader-demo telemetry simulator tests passed.'
