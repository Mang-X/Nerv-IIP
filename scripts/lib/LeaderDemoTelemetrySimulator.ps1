# Script-Governance:
#   Category: library
#   SideEffects:
#     - Issues leader-demo telemetry requests through a caller-injected HTTP action
#   Writes:
#     - None; callers own every output path
#   Cleanup:
#     - Owns no process; pacing and cancellation are caller-injected actions
#   Requires:
#     - PowerShell 7

Set-StrictMode -Version Latest

. (Join-Path $PSScriptRoot 'OrdinalString.ps1')

function Get-NervLeaderDemoTelemetryProfile {
    param(
        [Parameter(Mandatory)] [int] $ElapsedSeconds,
        [Parameter(Mandatory)] [int] $DegradingAtSeconds,
        [Parameter(Mandatory)] [int] $AlarmAtSeconds,
        [Parameter(Mandatory)] [int] $RecoveredAtSeconds
    )

    if ($ElapsedSeconds -ge $RecoveredAtSeconds) { return 'recovered' }
    if ($ElapsedSeconds -ge $AlarmAtSeconds) { return 'alarm' }
    if ($ElapsedSeconds -ge $DegradingAtSeconds) { return 'degrading' }
    return 'normal'
}

function Get-NervLeaderDemoTelemetryValue {
    param(
        [Parameter(Mandatory)] [ValidateSet('vibration', 'temperature')] [string] $TagKey,
        [Parameter(Mandatory)] [ValidateSet('normal', 'degrading', 'alarm', 'recovered')] [string] $Phase,
        [Parameter(Mandatory)] [int] $Index,
        [Parameter(Mandatory)] [int] $ElapsedSeconds,
        [Parameter(Mandatory)] [int] $DegradingAtSeconds,
        [Parameter(Mandatory)] [int] $AlarmAtSeconds
    )

    $wave = [Math]::Sin($Index * 0.7)
    if ([string]::Equals([string]($TagKey), [string]('temperature'), [StringComparison]::Ordinal)) {
        $value = if ([string]::Equals([string]($Phase), [string]('normal'), [StringComparison]::OrdinalIgnoreCase)) { 42.0 + $wave }
elseif ([string]::Equals([string]($Phase), [string]('degrading'), [StringComparison]::OrdinalIgnoreCase)) {
                $duration = [Math]::Max(1, $AlarmAtSeconds - $DegradingAtSeconds)
                $progress = [Math]::Min(1.0, [Math]::Max(0.0, ($ElapsedSeconds - $DegradingAtSeconds) / $duration))
                46.0 + (10.0 * $progress) + ($wave * 0.5)
            }
elseif ([string]::Equals([string]($Phase), [string]('alarm'), [StringComparison]::OrdinalIgnoreCase)) { 62.0 + ($wave * 1.5) }
elseif ([string]::Equals([string]($Phase), [string]('recovered'), [StringComparison]::OrdinalIgnoreCase)) { 44.0 + ($wave * 0.6) }
    }
    else {
        $value = if ([string]::Equals([string]($Phase), [string]('normal'), [StringComparison]::OrdinalIgnoreCase)) { 2.2 + ($wave * 0.15) }
elseif ([string]::Equals([string]($Phase), [string]('degrading'), [StringComparison]::OrdinalIgnoreCase)) {
                $duration = [Math]::Max(1, $AlarmAtSeconds - $DegradingAtSeconds)
                $progress = [Math]::Min(0.99, [Math]::Max(0.0, ($ElapsedSeconds - $DegradingAtSeconds) / $duration))
                3.4 + (4.4 * $progress) + ($wave * 0.05)
            }
elseif ([string]::Equals([string]($Phase), [string]('alarm'), [StringComparison]::OrdinalIgnoreCase)) { 8.6 + ($wave * 0.2) }
elseif ([string]::Equals([string]($Phase), [string]('recovered'), [StringComparison]::OrdinalIgnoreCase)) { 2.8 + ($wave * 0.1) }
    }

    return [decimal]::Round([decimal]$value, 3, [MidpointRounding]::AwayFromZero)
}

function New-NervLeaderDemoTelemetryTimeline {
    param(
        [Parameter(Mandatory)] [ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9._-]{0,47}$')] [string] $RunId,
        [Parameter(Mandatory)] [DateTimeOffset] $ScenarioStartUtc,
        [Parameter(Mandatory)] [ValidateRange(8, 86400)] [int] $DurationSeconds,
        [Parameter(Mandatory)] [ValidateRange(1, 60)] [int] $SampleIntervalSeconds,
        [Parameter(Mandatory)] [ValidateRange(1, 86400)] [int] $DegradingAtSeconds,
        [Parameter(Mandatory)] [ValidateRange(2, 86400)] [int] $AlarmAtSeconds,
        [Parameter(Mandatory)] [ValidateRange(3, 86400)] [int] $RecoveredAtSeconds
    )

    if (-not ($DegradingAtSeconds -lt $AlarmAtSeconds -and
        $AlarmAtSeconds -lt $RecoveredAtSeconds -and
        $RecoveredAtSeconds -lt $DurationSeconds)) {
        throw 'Scenario transitions must be strictly ordered and occur before the duration ends.'
    }

    $normalizedStart = [DateTimeOffset]::FromUnixTimeMilliseconds(
        $ScenarioStartUtc.ToUniversalTime().ToUnixTimeMilliseconds()
    )
    for ($index = 0; ($index * $SampleIntervalSeconds) -lt $DurationSeconds; $index++) {
        $elapsedSeconds = $index * $SampleIntervalSeconds
        $bucketStart = $normalizedStart.AddSeconds($elapsedSeconds)
        $bucketEnd = $bucketStart.AddSeconds($SampleIntervalSeconds)
        $profile = Get-NervLeaderDemoTelemetryProfile `
            -ElapsedSeconds $elapsedSeconds `
            -DegradingAtSeconds $DegradingAtSeconds `
            -AlarmAtSeconds $AlarmAtSeconds `
            -RecoveredAtSeconds $RecoveredAtSeconds
        $state = if ([string]::Equals([string]($profile), [string]('alarm'), [StringComparison]::OrdinalIgnoreCase)) { 'unavailable' }
elseif ([string]::Equals([string]($profile), [string]('recovered'), [StringComparison]::OrdinalIgnoreCase)) { 'available' }
else { 'running' }

        [pscustomobject][ordered]@{
            Index = $index
            Profile = $profile
            BucketStartUtc = $bucketStart.ToString('O')
            BucketEndUtc = $bucketEnd.ToString('O')
            Vibration = Get-NervLeaderDemoTelemetryValue `
                -TagKey vibration `
                -Phase $profile `
                -Index $index `
                -ElapsedSeconds $elapsedSeconds `
                -DegradingAtSeconds $DegradingAtSeconds `
                -AlarmAtSeconds $AlarmAtSeconds
            Temperature = Get-NervLeaderDemoTelemetryValue `
                -TagKey temperature `
                -Phase $profile `
                -Index $index `
                -ElapsedSeconds $elapsedSeconds `
                -DegradingAtSeconds $DegradingAtSeconds `
                -AlarmAtSeconds $AlarmAtSeconds
            DeviceState = $state
        }
    }
}

function New-NervLeaderDemoTelemetrySampleBody {
    param(
        [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $OrganizationId,
        [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $EnvironmentId,
        [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $DeviceAssetId,
        [Parameter(Mandatory)] [ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9._-]{0,47}$')] [string] $RunId,
        [Parameter(Mandatory)] [object] $Point,
        [Parameter(Mandatory)] [ValidateSet('vibration', 'temperature')] [string] $TagKey,
        [string] $SourcePhase
    )

    $sourcePhase = if ([string]::IsNullOrWhiteSpace($SourcePhase)) { "$($Point.Profile)" } else { $SourcePhase }
    $value = if ([string]::Equals([string]($TagKey), [string]('vibration'), [StringComparison]::Ordinal)) { [decimal]$Point.Vibration } else { [decimal]$Point.Temperature }
    $sourceSequence = "leader-demo:$RunId`:$sourcePhase`:$(([int]$Point.Index).ToString('000000'))`:$TagKey"
    $deviceState = if ([string]::Equals([string]($TagKey), [string]('vibration'), [StringComparison]::Ordinal)) { "$($Point.DeviceState)" } else { $null }
    $stateOccurredAtUtc = if ([string]::Equals([string]($TagKey), [string]('vibration'), [StringComparison]::Ordinal)) { "$($Point.BucketEndUtc)" } else { $null }

    return [pscustomobject][ordered]@{
        organizationId = $OrganizationId
        environmentId = $EnvironmentId
        deviceAssetId = $DeviceAssetId
        tagKey = $TagKey
        bucketStartUtc = "$($Point.BucketStartUtc)"
        bucketEndUtc = "$($Point.BucketEndUtc)"
        sampleCount = 1
        minValue = $value
        maxValue = $value
        averageValue = $value
        sourceSequence = $sourceSequence
        sourceSystem = 'leader-demo-simulator'
        sourceConnector = 'business-gateway'
        firstValue = $value
        lastValue = $value
        deviceState = $deviceState
        stateOccurredAtUtc = $stateOccurredAtUtc
    }
}

function Get-NervLeaderDemoPropertyValue {
    param(
        [AllowNull()] [object] $InputObject,
        [Parameter(Mandatory)] [string] $Name
    )

    if ($null -eq $InputObject) { return $null }
    if ($InputObject -is [System.Collections.IDictionary]) { return $InputObject[$Name] }
    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Get-NervLeaderDemoHttpStatusCode {
    param([Parameter(Mandatory)] [object] $ErrorRecord)

    $exception = Get-NervLeaderDemoPropertyValue -InputObject $ErrorRecord -Name 'Exception'
    if ($null -eq $exception) { $exception = $ErrorRecord }
    $response = Get-NervLeaderDemoPropertyValue -InputObject $exception -Name 'Response'
    $statusCode = Get-NervLeaderDemoPropertyValue -InputObject $response -Name 'StatusCode'
    if ($null -eq $statusCode -and $exception.Data -is [System.Collections.IDictionary]) {
        $statusCode = $exception.Data['HttpStatusCode']
    }
    if ($null -eq $statusCode) { return $null }
    return [int]$statusCode
}

function Invoke-NervLeaderDemoTelemetryRequest {
    param(
        [Parameter(Mandatory)] [ValidateSet('GET', 'POST')] [string] $Method,
        [Parameter(Mandatory)] [string] $Path,
        [AllowNull()] [object] $Body,
        [Parameter(Mandatory)] [scriptblock] $HttpAction
    )

    return & $HttpAction $Method $Path $Body
}

function New-NervLeaderDemoHistoricalPoint {
    param(
        [Parameter(Mandatory)] [int] $Index,
        [Parameter(Mandatory)] [DateTimeOffset] $BucketStartUtc,
        [Parameter(Mandatory)] [int] $BucketSeconds,
        [Parameter(Mandatory)] [decimal] $Vibration,
        [Parameter(Mandatory)] [decimal] $Temperature
    )

    return [pscustomobject][ordered]@{
        Index = $Index
        Profile = 'normal'
        BucketStartUtc = $BucketStartUtc.ToUniversalTime().ToString('O')
        BucketEndUtc = $BucketStartUtc.ToUniversalTime().AddSeconds($BucketSeconds).ToString('O')
        Vibration = $Vibration
        Temperature = $Temperature
        DeviceState = 'running'
    }
}

function Remove-NervLeaderDemoHistoricalDeviceState {
    param([Parameter(Mandatory)] [object] $Body)

    $Body.deviceState = $null
    $Body.stateOccurredAtUtc = $null
    return $Body
}

function Test-NervLeaderDemoHistoricalSampleAcceptance {
    param(
        [Parameter(Mandatory)] [string] $OrganizationId,
        [Parameter(Mandatory)] [string] $EnvironmentId,
        [Parameter(Mandatory)] [string] $DeviceAssetId,
        [Parameter(Mandatory)] [string] $RunId,
        [Parameter(Mandatory)] [DateTimeOffset] $ScenarioStartUtc,
        [Parameter(Mandatory)] [int] $SampleIntervalSeconds,
        [Parameter(Mandatory)] [scriptblock] $HttpAction
    )

    $probePoint = New-NervLeaderDemoHistoricalPoint `
        -Index 0 `
        -BucketStartUtc $ScenarioStartUtc.ToUniversalTime().AddHours(-24) `
        -BucketSeconds $SampleIntervalSeconds `
        -Vibration ([decimal]2.1) `
        -Temperature ([decimal]41.5)
    $probeBody = New-NervLeaderDemoTelemetrySampleBody `
        -OrganizationId $OrganizationId `
        -EnvironmentId $EnvironmentId `
        -DeviceAssetId $DeviceAssetId `
        -RunId $RunId `
        -Point $probePoint `
        -TagKey vibration `
        -SourcePhase 'history-probe'
    $probeBody = Remove-NervLeaderDemoHistoricalDeviceState -Body $probeBody

    try {
        $response = Invoke-NervLeaderDemoTelemetryRequest `
            -Method POST `
            -Path '/api/business-console/v1/telemetry/samples' `
            -Body $probeBody `
            -HttpAction $HttpAction
        return [pscustomobject]@{
            Accepted = $true
            Reason = $null
            Response = $response
            SourceSequence = $probeBody.sourceSequence
        }
    }
    catch {
        $statusCode = Get-NervLeaderDemoHttpStatusCode -ErrorRecord $_
        if ($statusCode -notin @(400, 422)) {
            throw
        }
        return [pscustomobject]@{
            Accepted = $false
            Reason = "$($_.Exception.Message)"
            HttpStatusCode = $statusCode
            Response = $null
            SourceSequence = $probeBody.sourceSequence
        }
    }
}

function Invoke-NervLeaderDemoHistoricalBackfill {
    param(
        [Parameter(Mandatory)] [string] $OrganizationId,
        [Parameter(Mandatory)] [string] $EnvironmentId,
        [Parameter(Mandatory)] [string] $DeviceAssetId,
        [Parameter(Mandatory)] [string] $RunId,
        [Parameter(Mandatory)] [DateTimeOffset] $SessionStartedAtUtc,
        [Parameter(Mandatory)] [DateTimeOffset] $ScenarioStartUtc,
        [Parameter(Mandatory)] [ValidateRange(1, 168)] [int] $HistoricalHours,
        [Parameter(Mandatory)] [ValidateRange(1, 1440)] [int] $HistoricalIntervalMinutes,
        [Parameter(Mandatory)] [ValidateRange(1, 60)] [int] $SampleIntervalSeconds,
        [Parameter(Mandatory)] [scriptblock] $HttpAction
    )

    $sessionStart = $SessionStartedAtUtc.ToUniversalTime()
    $scenarioStart = $ScenarioStartUtc.ToUniversalTime()
    if ($sessionStart -gt $scenarioStart) {
        throw 'Leader-demo session start cannot be later than the telemetry scenario start.'
    }
    $probe = Test-NervLeaderDemoHistoricalSampleAcceptance `
        -OrganizationId $OrganizationId `
        -EnvironmentId $EnvironmentId `
        -DeviceAssetId $DeviceAssetId `
        -RunId $RunId `
        -ScenarioStartUtc $ScenarioStartUtc `
        -SampleIntervalSeconds $SampleIntervalSeconds `
        -HttpAction $HttpAction

    $mode = if ($probe.Accepted) { 'accepted' } else { 'rejected-fallback' }
    $phase = if ($probe.Accepted) { 'history' } else { 'session-backfill' }
    $windowStart = if ($probe.Accepted) {
        $scenarioStart.AddHours(-$HistoricalHours)
    }
    else {
        $candidate = $scenarioStart.AddMinutes(-5)
        if ($candidate -lt $sessionStart) { $sessionStart } else { $candidate }
    }
    $stepSeconds = if ($probe.Accepted) {
        $HistoricalIntervalMinutes * 60
    }
    else {
        60
    }
    $publishedCount = if ($probe.Accepted) { 1 } else { 0 }
    $index = 1
    for ($cursor = $windowStart; $cursor -lt $scenarioStart; $cursor = $cursor.AddSeconds($stepSeconds)) {
        $wave = [Math]::Sin($index * 0.6)
        $point = New-NervLeaderDemoHistoricalPoint `
            -Index $index `
            -BucketStartUtc $cursor `
            -BucketSeconds $SampleIntervalSeconds `
            -Vibration ([decimal]::Round([decimal](2.2 + ($wave * 0.12)), 3)) `
            -Temperature ([decimal]::Round([decimal](42.0 + ($wave * 0.8)), 3))
        foreach ($tagKey in @('vibration', 'temperature')) {
            $body = New-NervLeaderDemoTelemetrySampleBody `
                -OrganizationId $OrganizationId `
                -EnvironmentId $EnvironmentId `
                -DeviceAssetId $DeviceAssetId `
                -RunId $RunId `
                -Point $point `
                -TagKey $tagKey `
                -SourcePhase $phase
            $body = Remove-NervLeaderDemoHistoricalDeviceState -Body $body
            Invoke-NervLeaderDemoTelemetryRequest `
                -Method POST `
                -Path '/api/business-console/v1/telemetry/samples' `
                -Body $body `
                -HttpAction $HttpAction | Out-Null
            $publishedCount++
        }
        $index++
    }

    return [pscustomobject][ordered]@{
        Mode = $mode
        Reason = $probe.Reason
        ProbeSourceSequence = $probe.SourceSequence
        WindowStartUtc = $windowStart.ToString('O')
        WindowEndUtc = $scenarioStart.ToString('O')
        PublishedCount = $publishedCount
    }
}

function New-NervLeaderDemoTelemetryScenarioContract {
    param(
        [Parameter(Mandatory)] [int] $DurationSeconds,
        [Parameter(Mandatory)] [int] $SampleIntervalSeconds,
        [Parameter(Mandatory)] [int] $DegradingAtSeconds,
        [Parameter(Mandatory)] [int] $AlarmAtSeconds,
        [Parameter(Mandatory)] [int] $RecoveredAtSeconds,
        [switch] $HistoricalBackfill,
        [Parameter(Mandatory)] [int] $HistoricalHours,
        [Parameter(Mandatory)] [int] $HistoricalIntervalMinutes
    )

    return [pscustomobject][ordered]@{
        DurationSeconds = $DurationSeconds
        SampleIntervalSeconds = $SampleIntervalSeconds
        DegradingAtSeconds = $DegradingAtSeconds
        AlarmAtSeconds = $AlarmAtSeconds
        RecoveredAtSeconds = $RecoveredAtSeconds
        HistoricalBackfill = [bool]$HistoricalBackfill
        HistoricalHours = $HistoricalHours
        HistoricalIntervalMinutes = $HistoricalIntervalMinutes
    }
}

function Get-NervLeaderDemoTelemetryReplayBaseline {
    param(
        [Parameter(Mandatory)] [string] $EvidenceRoot,
        [Parameter(Mandatory)] [string] $SessionId,
        [Parameter(Mandatory)] [string] $RunId,
        [Parameter(Mandatory)] [DateTimeOffset] $ScenarioStartUtc
    )

    $directory = Join-Path ([System.IO.Path]::GetFullPath($EvidenceRoot)) $SessionId
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) {
        throw "Replay requires completed real-time evidence for session '$SessionId' and run '$RunId'."
    }
    $normalizedStart = [DateTimeOffset]::FromUnixTimeMilliseconds(
        $ScenarioStartUtc.ToUniversalTime().ToUnixTimeMilliseconds()
    )
    foreach ($file in @(Get-NervItemsSorted -Items @(Get-ChildItem -LiteralPath $directory -Filter 'telemetry-simulator-*.json' -File) -Comparison { param($left, $right) if ($right.LastWriteTimeUtc -gt $left.LastWriteTimeUtc) { 1 } elseif ($right.LastWriteTimeUtc -lt $left.LastWriteTimeUtc) { -1 } else { 0 } })) {
        try {
            $evidence = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json -Depth 50
            $simulation = Get-NervLeaderDemoPropertyValue -InputObject $evidence -Name 'simulation'
            $candidateStart = [DateTimeOffset]::Parse(
                "$(Get-NervLeaderDemoPropertyValue -InputObject $simulation -Name 'ScenarioStartUtc')"
            )
            if (
                [string]::Equals([string]("$(Get-NervLeaderDemoPropertyValue -InputObject $evidence -Name 'executionMode')"), [string]('real-time'), [StringComparison]::Ordinal) -and
                [string]::Equals([string]("$(Get-NervLeaderDemoPropertyValue -InputObject $simulation -Name 'Result')"), [string]('completed'), [StringComparison]::Ordinal) -and
                [string]::Equals([string]("$(Get-NervLeaderDemoPropertyValue -InputObject $simulation -Name 'RunId')"), [string]($RunId), [StringComparison]::Ordinal) -and
                $candidateStart -eq $normalizedStart
            ) {
                return $evidence
            }
        }
        catch {
            continue
        }
    }
    throw "Replay requires completed real-time evidence for exact session '$SessionId', run '$RunId', and ScenarioStartUtc '$($normalizedStart.ToString('O'))'."
}

function Assert-NervLeaderDemoTelemetryReplayContract {
    param(
        [Parameter(Mandatory)] [object] $BaselineEvidence,
        [Parameter(Mandatory)] [string] $RunId,
        [Parameter(Mandatory)] [DateTimeOffset] $ScenarioStartUtc,
        [Parameter(Mandatory)] [object] $ScenarioContract
    )

    $simulation = Get-NervLeaderDemoPropertyValue -InputObject $BaselineEvidence -Name 'simulation'
    if (
        (-not [string]::Equals([string]("$(Get-NervLeaderDemoPropertyValue -InputObject $BaselineEvidence -Name 'executionMode')"), [string]('real-time'), [StringComparison]::Ordinal)) -or
        (-not [string]::Equals([string]("$(Get-NervLeaderDemoPropertyValue -InputObject $simulation -Name 'Result')"), [string]('completed'), [StringComparison]::Ordinal))
    ) {
        throw 'Replay baseline must be completed real-time evidence.'
    }
    if ((-not [string]::Equals([string]("$(Get-NervLeaderDemoPropertyValue -InputObject $simulation -Name 'RunId')"), [string]($RunId), [StringComparison]::Ordinal))) {
        throw 'Replay RunId does not match the completed real-time evidence.'
    }
    $expectedStart = [DateTimeOffset]::Parse(
        "$(Get-NervLeaderDemoPropertyValue -InputObject $simulation -Name 'ScenarioStartUtc')"
    )
    $actualStart = [DateTimeOffset]::FromUnixTimeMilliseconds(
        $ScenarioStartUtc.ToUniversalTime().ToUnixTimeMilliseconds()
    )
    if ($expectedStart -ne $actualStart) {
        throw 'Replay ScenarioStartUtc does not match the completed real-time evidence.'
    }
    $expectedContract = Get-NervLeaderDemoPropertyValue -InputObject $simulation -Name 'Scenario'
    if ($null -eq $expectedContract) {
        throw 'Replay baseline does not contain the complete scenario contract.'
    }
    foreach ($field in @(
        'DurationSeconds',
        'SampleIntervalSeconds',
        'DegradingAtSeconds',
        'AlarmAtSeconds',
        'RecoveredAtSeconds',
        'HistoricalBackfill',
        'HistoricalHours',
        'HistoricalIntervalMinutes'
    )) {
        $expected = Get-NervLeaderDemoPropertyValue -InputObject $expectedContract -Name $field
        $actual = Get-NervLeaderDemoPropertyValue -InputObject $ScenarioContract -Name $field
        if ((-not [string]::Equals([string]("$expected"), [string]("$actual"), [StringComparison]::Ordinal))) {
            throw "Replay scenario field '$field' differs from completed real-time evidence (expected '$expected', actual '$actual')."
        }
    }
}

function Get-NervLeaderDemoTelemetryIdentity {
    param([AllowNull()] [object] $Response)

    $data = Get-NervLeaderDemoPropertyValue -InputObject $Response -Name 'data'
    return [pscustomobject][ordered]@{
        TelemetrySummaryId = "$(Get-NervLeaderDemoPropertyValue -InputObject $data -Name 'telemetrySummaryId')"
        DeviceStateSnapshotId = "$(Get-NervLeaderDemoPropertyValue -InputObject $data -Name 'deviceStateSnapshotId')"
    }
}

function Test-NervLeaderDemoTelemetryReplay {
    param(
        [Parameter(Mandatory)] [object] $Body,
        [Parameter(Mandatory)] [object] $FirstResponse,
        [Parameter(Mandatory)] [scriptblock] $HttpAction
    )

    $replayResponse = Invoke-NervLeaderDemoTelemetryRequest `
        -Method POST `
        -Path '/api/business-console/v1/telemetry/samples' `
        -Body $Body `
        -HttpAction $HttpAction
    $first = Get-NervLeaderDemoTelemetryIdentity -Response $FirstResponse
    $replay = Get-NervLeaderDemoTelemetryIdentity -Response $replayResponse
    return [pscustomobject][ordered]@{
        IdentityStable = (
            [string]::Equals([string]($first.TelemetrySummaryId), [string]($replay.TelemetrySummaryId), [StringComparison]::Ordinal) -and
            [string]::Equals([string]($first.DeviceStateSnapshotId), [string]($replay.DeviceStateSnapshotId), [StringComparison]::Ordinal)
        )
        SourceSequence = "$($Body.sourceSequence)"
        TelemetrySummaryId = $first.TelemetrySummaryId
        DeviceStateSnapshotId = $first.DeviceStateSnapshotId
    }
}

function Get-NervLeaderDemoTelemetryHistoryEvidence {
    param(
        [Parameter(Mandatory)] [string] $OrganizationId,
        [Parameter(Mandatory)] [string] $EnvironmentId,
        [Parameter(Mandatory)] [string] $DeviceAssetId,
        [Parameter(Mandatory)] [DateTimeOffset] $FromUtc,
        [Parameter(Mandatory)] [DateTimeOffset] $ToUtc,
        [Parameter(Mandatory)] [scriptblock] $HttpAction
    )

    $path = "/api/business-console/v1/telemetry/devices/$([Uri]::EscapeDataString($DeviceAssetId))/history" +
        "?organizationId=$([Uri]::EscapeDataString($OrganizationId))" +
        "&environmentId=$([Uri]::EscapeDataString($EnvironmentId))" +
        "&fromUtc=$([Uri]::EscapeDataString($FromUtc.ToUniversalTime().ToString('O')))" +
        "&toUtc=$([Uri]::EscapeDataString($ToUtc.ToUniversalTime().ToString('O')))"
    $response = Invoke-NervLeaderDemoTelemetryRequest -Method GET -Path $path -Body $null -HttpAction $HttpAction
    $data = Get-NervLeaderDemoPropertyValue -InputObject $response -Name 'data'
    $items = @(Get-NervLeaderDemoPropertyValue -InputObject $data -Name 'items')
    return [pscustomobject][ordered]@{
        ItemCount = $items.Count
        FirstOccurredAtUtc = if ($items.Count -gt 0) { "$(Get-NervLeaderDemoPropertyValue -InputObject $items[0] -Name 'occurredAtUtc')" } else { $null }
        LastOccurredAtUtc = if ($items.Count -gt 0) { "$(Get-NervLeaderDemoPropertyValue -InputObject $items[-1] -Name 'occurredAtUtc')" } else { $null }
    }
}

function Get-NervLeaderDemoTelemetryAlarmEvidence {
    param(
        [Parameter(Mandatory)] [string] $OrganizationId,
        [Parameter(Mandatory)] [string] $EnvironmentId,
        [Parameter(Mandatory)] [string] $DeviceAssetId,
        [Parameter(Mandatory)] [scriptblock] $HttpAction
    )

    $path = '/api/business-console/v1/telemetry/alarms' +
        "?organizationId=$([Uri]::EscapeDataString($OrganizationId))" +
        "&environmentId=$([Uri]::EscapeDataString($EnvironmentId))" +
        "&deviceAssetId=$([Uri]::EscapeDataString($DeviceAssetId))&skip=0&take=100"
    $response = Invoke-NervLeaderDemoTelemetryRequest -Method GET -Path $path -Body $null -HttpAction $HttpAction
    $data = Get-NervLeaderDemoPropertyValue -InputObject $response -Name 'data'
    $items = @(Get-NervLeaderDemoPropertyValue -InputObject $data -Name 'items')
    $alarm = @(Get-NervItemsSortedByString -Items @($items | Where-Object {
        [string]::Equals([string]("$(Get-NervLeaderDemoPropertyValue -InputObject $_ -Name 'alarmCode')"), [string]('VIBRATION-HIGH'), [StringComparison]::Ordinal) -or
        "$(Get-NervLeaderDemoPropertyValue -InputObject $_ -Name 'externalAlarmId')" -like 'ALARM-DEMO-001:*'
    }) -KeySelector { param($row) "$(Get-NervLeaderDemoPropertyValue -InputObject $row -Name 'raisedAtUtc')" } -Comparer ([StringComparer]::Ordinal) | Select-Object -Last 1)
    if ($alarm.Count -eq 0) {
        return [pscustomobject][ordered]@{
            Found = $false
            Status = $null
            RaisedAtUtc = $null
            ClearedAtUtc = $null
        }
    }

    return [pscustomobject][ordered]@{
        Found = $true
        Status = "$(Get-NervLeaderDemoPropertyValue -InputObject $alarm[0] -Name 'status')"
        RaisedAtUtc = "$(Get-NervLeaderDemoPropertyValue -InputObject $alarm[0] -Name 'raisedAtUtc')"
        ClearedAtUtc = "$(Get-NervLeaderDemoPropertyValue -InputObject $alarm[0] -Name 'clearedAtUtc')"
    }
}

function Invoke-NervLeaderDemoTelemetrySimulator {
    param(
        [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $OrganizationId,
        [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $EnvironmentId,
        [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $DeviceAssetId,
        [Parameter(Mandatory)] [ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9._-]{0,47}$')] [string] $RunId,
        [Parameter(Mandatory)] [DateTimeOffset] $SessionStartedAtUtc,
        [Parameter(Mandatory)] [DateTimeOffset] $ScenarioStartUtc,
        [Parameter(Mandatory)] [ValidateRange(8, 86400)] [int] $DurationSeconds,
        [Parameter(Mandatory)] [ValidateRange(1, 60)] [int] $SampleIntervalSeconds,
        [Parameter(Mandatory)] [ValidateRange(1, 86400)] [int] $DegradingAtSeconds,
        [Parameter(Mandatory)] [ValidateRange(2, 86400)] [int] $AlarmAtSeconds,
        [Parameter(Mandatory)] [ValidateRange(3, 86400)] [int] $RecoveredAtSeconds,
        [switch] $HistoricalBackfill,
        [ValidateRange(1, 168)] [int] $HistoricalHours = 24,
        [ValidateRange(1, 1440)] [int] $HistoricalIntervalMinutes = 15,
        [Parameter(Mandatory)] [scriptblock] $HttpAction,
        [scriptblock] $DelayAction = { param($Seconds) Start-Sleep -Seconds $Seconds },
        [scriptblock] $PostRequestPacingAction = {},
        [scriptblock] $HistoricalPostRequestPacingAction = {},
        [scriptblock] $CancellationCheckAction = { return $false }
    )

    $ScenarioStartUtc = [DateTimeOffset]::FromUnixTimeMilliseconds(
        $ScenarioStartUtc.ToUniversalTime().ToUnixTimeMilliseconds()
    )
    $timeline = @(
        New-NervLeaderDemoTelemetryTimeline `
            -RunId $RunId `
            -ScenarioStartUtc $ScenarioStartUtc `
            -DurationSeconds $DurationSeconds `
            -SampleIntervalSeconds $SampleIntervalSeconds `
            -DegradingAtSeconds $DegradingAtSeconds `
            -AlarmAtSeconds $AlarmAtSeconds `
            -RecoveredAtSeconds $RecoveredAtSeconds
    )
    $effectiveHttpAction = {
        param($Method, $Path, $Body)
        try {
            return & $HttpAction $Method $Path $Body
        }
        finally {
            if ([string]::Equals([string]($Method), [string]('POST'), [StringComparison]::Ordinal)) {
                & $PostRequestPacingAction
            }
        }
    }.GetNewClosure()
    $historicalHttpAction = {
        param($Method, $Path, $Body)
        try {
            return & $effectiveHttpAction $Method $Path $Body
        }
        finally {
            if ([string]::Equals([string]($Method), [string]('POST'), [StringComparison]::Ordinal)) {
                & $HistoricalPostRequestPacingAction
            }
        }
    }.GetNewClosure()
    $backfill = if ($HistoricalBackfill) {
        Invoke-NervLeaderDemoHistoricalBackfill `
            -OrganizationId $OrganizationId `
            -EnvironmentId $EnvironmentId `
            -DeviceAssetId $DeviceAssetId `
            -RunId $RunId `
            -SessionStartedAtUtc $SessionStartedAtUtc `
            -ScenarioStartUtc $ScenarioStartUtc `
            -HistoricalHours $HistoricalHours `
            -HistoricalIntervalMinutes $HistoricalIntervalMinutes `
            -SampleIntervalSeconds $SampleIntervalSeconds `
            -HttpAction $historicalHttpAction
    }
    else {
        [pscustomobject][ordered]@{
            Mode = 'disabled'
            Reason = $null
            ProbeSourceSequence = $null
            WindowStartUtc = $null
            WindowEndUtc = $null
            PublishedCount = 0
        }
    }

    $result = 'completed'
    $publishedPointCount = 0
    $publishedRequestCount = 0
    $replay = $null
    foreach ($point in $timeline) {
        if (& $CancellationCheckAction) {
            $result = 'stopped'
            break
        }

        foreach ($tagKey in @('vibration', 'temperature')) {
            $body = New-NervLeaderDemoTelemetrySampleBody `
                -OrganizationId $OrganizationId `
                -EnvironmentId $EnvironmentId `
                -DeviceAssetId $DeviceAssetId `
                -RunId $RunId `
                -Point $point `
                -TagKey $tagKey
            $response = Invoke-NervLeaderDemoTelemetryRequest `
                -Method POST `
                -Path '/api/business-console/v1/telemetry/samples' `
                -Body $body `
                -HttpAction $effectiveHttpAction
            $publishedRequestCount++
            if ($null -eq $replay -and [string]::Equals([string]($tagKey), [string]('vibration'), [StringComparison]::Ordinal)) {
                $replay = Test-NervLeaderDemoTelemetryReplay -Body $body -FirstResponse $response -HttpAction $effectiveHttpAction
            }
        }
        $publishedPointCount++
        if ($publishedPointCount -lt $timeline.Count) {
            & $DelayAction $SampleIntervalSeconds
        }
    }

    $history = if ([string]::Equals([string]($result), [string]('completed'), [StringComparison]::Ordinal)) {
        Get-NervLeaderDemoTelemetryHistoryEvidence `
            -OrganizationId $OrganizationId `
            -EnvironmentId $EnvironmentId `
            -DeviceAssetId $DeviceAssetId `
            -FromUtc $(if ($HistoricalBackfill) { [DateTimeOffset]::Parse($backfill.WindowStartUtc) } else { $ScenarioStartUtc }) `
            -ToUtc $ScenarioStartUtc.ToUniversalTime().AddSeconds($DurationSeconds + $SampleIntervalSeconds) `
            -HttpAction $effectiveHttpAction
    }
    else {
        [pscustomobject][ordered]@{ ItemCount = 0; FirstOccurredAtUtc = $null; LastOccurredAtUtc = $null }
    }
    $alarm = if ([string]::Equals([string]($result), [string]('completed'), [StringComparison]::Ordinal)) {
        Get-NervLeaderDemoTelemetryAlarmEvidence `
            -OrganizationId $OrganizationId `
            -EnvironmentId $EnvironmentId `
            -DeviceAssetId $DeviceAssetId `
            -HttpAction $effectiveHttpAction
    }
    else {
        [pscustomobject][ordered]@{ Found = $false; Status = $null; RaisedAtUtc = $null; ClearedAtUtc = $null }
    }

    return [pscustomobject][ordered]@{
        SchemaVersion = 1
        Result = $result
        RunId = $RunId
        OrganizationId = $OrganizationId
        EnvironmentId = $EnvironmentId
        DeviceAssetId = $DeviceAssetId
        ScenarioStartUtc = $ScenarioStartUtc.ToUniversalTime().ToString('O')
        ScenarioEndUtc = $ScenarioStartUtc.ToUniversalTime().AddSeconds($DurationSeconds).ToString('O')
        Scenario = New-NervLeaderDemoTelemetryScenarioContract `
            -DurationSeconds $DurationSeconds `
            -SampleIntervalSeconds $SampleIntervalSeconds `
            -DegradingAtSeconds $DegradingAtSeconds `
            -AlarmAtSeconds $AlarmAtSeconds `
            -RecoveredAtSeconds $RecoveredAtSeconds `
            -HistoricalBackfill:$HistoricalBackfill `
            -HistoricalHours $HistoricalHours `
            -HistoricalIntervalMinutes $HistoricalIntervalMinutes
        HistoricalBackfill = $backfill
        Live = [pscustomobject][ordered]@{
            PlannedPointCount = $timeline.Count
            PublishedPointCount = $publishedPointCount
            PublishedRequestCount = $publishedRequestCount
            SampleIntervalSeconds = $SampleIntervalSeconds
            Profiles = @(Get-NervStringsUniqueInOrder -Values @($timeline | ForEach-Object { [string]$_.Profile }) -Comparer ([StringComparer]::Ordinal))
            VibrationMinimum = ($timeline | Measure-Object -Property Vibration -Minimum).Minimum
            VibrationMaximum = ($timeline | Measure-Object -Property Vibration -Maximum).Maximum
        }
        Replay = if ($null -ne $replay) { $replay } else {
            [pscustomobject][ordered]@{
                IdentityStable = $false
                SourceSequence = $null
                TelemetrySummaryId = $null
                DeviceStateSnapshotId = $null
            }
        }
        History = $history
        Alarm = $alarm
    }
}

function Write-NervLeaderDemoTelemetryEvidence {
    param(
        [Parameter(Mandatory)] [object] $Evidence,
        [Parameter(Mandatory)] [string] $EvidenceRoot,
        [Parameter(Mandatory)] [string] $SessionId,
        [Parameter(Mandatory)] [string] $RunId,
        [string[]] $SensitiveValues = @()
    )

    $directory = Join-Path ([System.IO.Path]::GetFullPath($EvidenceRoot)) $SessionId
    [void][System.IO.Directory]::CreateDirectory($directory)
    $timestamp = [DateTimeOffset]::UtcNow.UtcDateTime.ToString('yyyyMMddTHHmmssfffZ')
    $safeRunId = $RunId -replace '[^a-zA-Z0-9._-]', '-'
    $jsonPath = Join-Path $directory "telemetry-simulator-$safeRunId-$timestamp.json"
    $markdownPath = Join-Path $directory "telemetry-simulator-$safeRunId-$timestamp.md"
    $json = $Evidence | ConvertTo-Json -Depth 50
    foreach ($sensitiveValue in $SensitiveValues) {
        if (-not [string]::IsNullOrEmpty($sensitiveValue)) {
            $json = $json.Replace($sensitiveValue, '<redacted>')
        }
    }
    [System.IO.File]::WriteAllText($jsonPath, $json, [System.Text.UTF8Encoding]::new($false))

    $simulation = Get-NervLeaderDemoPropertyValue -InputObject $Evidence -Name 'simulation'
    $backfill = Get-NervLeaderDemoPropertyValue -InputObject $simulation -Name 'HistoricalBackfill'
    $live = Get-NervLeaderDemoPropertyValue -InputObject $simulation -Name 'Live'
    $replay = Get-NervLeaderDemoPropertyValue -InputObject $simulation -Name 'Replay'
    $history = Get-NervLeaderDemoPropertyValue -InputObject $simulation -Name 'History'
    $alarm = Get-NervLeaderDemoPropertyValue -InputObject $simulation -Name 'Alarm'
    $markdown = @"
# Leader-demo telemetry simulator evidence

- Session: ``$SessionId``
- Run: ``$RunId``
- Result: ``$(Get-NervLeaderDemoPropertyValue -InputObject $simulation -Name 'Result')``
- Historical backfill: ``$(Get-NervLeaderDemoPropertyValue -InputObject $backfill -Name 'Mode')``
- Backfill published requests: ``$(Get-NervLeaderDemoPropertyValue -InputObject $backfill -Name 'PublishedCount')``
- Live published points: ``$(Get-NervLeaderDemoPropertyValue -InputObject $live -Name 'PublishedPointCount')``
- Replay identity stable: ``$(Get-NervLeaderDemoPropertyValue -InputObject $replay -Name 'IdentityStable')``
- Public history items: ``$(Get-NervLeaderDemoPropertyValue -InputObject $history -Name 'ItemCount')``
- Alarm status: ``$(Get-NervLeaderDemoPropertyValue -InputObject $alarm -Name 'Status')``
- Background processes created: ``0``
"@
    foreach ($sensitiveValue in $SensitiveValues) {
        if (-not [string]::IsNullOrEmpty($sensitiveValue)) {
            $markdown = $markdown.Replace($sensitiveValue, '<redacted>')
        }
    }
    [System.IO.File]::WriteAllText($markdownPath, $markdown, [System.Text.UTF8Encoding]::new($false))

    return [pscustomobject]@{
        JsonPath = $jsonPath
        MarkdownPath = $markdownPath
    }
}
