# Script-Governance:
#   Category: check
#   SideEffects:
#     - Exercises the Redis/CAP observation baseline recomputation against owned synthetic artifacts
#   Writes:
#     - Owned temporary observation fixtures
#   Cleanup:
#     - Removes owned temporary observation fixtures in finally
#   Requires:
#     - PowerShell 7

# 这份测试守的是「度量脚本不得静默给 0」：一个把缺数据读成 0 的基线脚本比没有基线更坏，
# 它会把「没采到」呈现成「指标很好」。因此除 CONTROL 外的每一格都是隔离的畸形/空产物，
# 每格只破坏一个位点，并断言脚本抛错而不是返回读数。

$ErrorActionPreference = 'Stop'
$script:measureScript = (Resolve-Path (Join-Path $PSScriptRoot '../measure-redis-cap-observation-baseline.ps1')).Path

function Assert-Baseline([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

function New-BaselineCounterCsv {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [hashtable] $Series,
        [Parameter(Mandatory)] [string[]] $Timestamps
    )
    $lines = [Collections.Generic.List[string]]::new()
    $lines.Add('Timestamp,Provider,Counter Name,Counter Type,Mean/Increment')
    for ($index = 0; $index -lt $Timestamps.Length; $index++) {
        foreach ($counterName in $Series.Keys) {
            $lines.Add(('{0},System.Runtime,"{1}",Metric,{2}' -f $Timestamps[$index], $counterName, ([double[]]$Series[$counterName])[$index]))
        }
    }
    [IO.File]::WriteAllLines($Path, $lines, [Text.UTF8Encoding]::new($false))
}

function New-BaselinePhaseLine {
    param(
        [Parameter(Mandatory)] [string] $FixtureId,
        [Parameter(Mandatory)] [int] $ConsumerId,
        [Parameter(Mandatory)] [string] $Phase,
        [Parameter(Mandatory)] [long] $Timestamp,
        [Parameter(Mandatory)] [string] $Utc,
        [Parameter(Mandatory)] [int] $ProcessId
    )
    return ('{{"source":"mes_asset_unavailable_subscription","fixtureId":"{0}","consumerId":{1},"sequence":1,"phase":"{2}","timestamp":{3},"timestampFrequency":1000000000,"utc":"{4}","processId":{5},"runtime":".NET 10.0.12","architecture":"X64","processorCount":4}}' -f $FixtureId, $ConsumerId, $Phase, $Timestamp, $Utc, $ProcessId)
}

# CONTROL 夹具：目标 testhost pid 4242，另有一台诱饵 testhost pid 99。
# 诱饵带一次 60s 的 inner_subscribe 与完全不同的计数器值 —— 只要成员解析或 processId 过滤
# 被拿掉，窗口与读数就会变，CONTROL 断言随即失败。
function New-BaselineArtifact {
    param(
        [Parameter(Mandatory)] [string] $Root,
        [Parameter(Mandatory)] [string] $ArtifactName
    )
    $observation = Join-Path (Join-Path $Root $ArtifactName) 'observation'
    [IO.Directory]::CreateDirectory($observation) | Out-Null

    [IO.File]::WriteAllLines((Join-Path $observation 'observations.jsonl'), @(
            '{"kind":"testhost","utc":"2026-09-08T05:59:50Z","memberId":"quality-rework-receipt-redis-cap","processId":99}',
            '{"kind":"testhost","utc":"2026-09-08T05:59:55Z","memberId":"mes-asset-unavailable-redis-cap","processId":4242}',
            '{"kind":"redis","utc":"2026-09-08T05:59:56Z","values":{}}'
        ), [Text.UTF8Encoding]::new($false))

    [IO.File]::WriteAllLines((Join-Path $observation 'fixture-phases.jsonl'), @(
            (New-BaselinePhaseLine -FixtureId ('a' * 32) -ConsumerId 2 -Phase 'inner_subscribe_started' -Timestamp 1000000000 -Utc '2026-09-08T06:00:00.0000000+00:00' -ProcessId 4242),
            (New-BaselinePhaseLine -FixtureId ('a' * 32) -ConsumerId 2 -Phase 'inner_subscribe_succeeded' -Timestamp 11000000000 -Utc '2026-09-08T06:00:10.0000000+00:00' -ProcessId 4242),
            # 次长这次落在 CSV 覆盖范围内：否则「取最长」被改成「取最短」时，
            # 会被脚本自己的「窗口内无计数器行」守卫兜住，window 断言就量不到东西。
            (New-BaselinePhaseLine -FixtureId ('b' * 32) -ConsumerId 2 -Phase 'inner_subscribe_started' -Timestamp 13000000000 -Utc '2026-09-08T06:00:12.0000000+00:00' -ProcessId 4242),
            (New-BaselinePhaseLine -FixtureId ('b' * 32) -ConsumerId 2 -Phase 'inner_subscribe_succeeded' -Timestamp 13040000000 -Utc '2026-09-08T06:00:12.0400000+00:00' -ProcessId 4242),
            (New-BaselinePhaseLine -FixtureId ('c' * 32) -ConsumerId 5 -Phase 'inner_subscribe_started' -Timestamp 1000000000 -Utc '2026-09-08T06:00:00.0000000+00:00' -ProcessId 99),
            (New-BaselinePhaseLine -FixtureId ('c' * 32) -ConsumerId 5 -Phase 'inner_subscribe_failed' -Timestamp 61000000000 -Utc '2026-09-08T06:01:00.0000000+00:00' -ProcessId 99)
        ), [Text.UTF8Encoding]::new($false))

    $timestamps = @(0..14 | ForEach-Object { '09/08/2026 06:00:{0:d2}' -f $_ })
    New-BaselineCounterCsv -Path (Join-Path $observation 'clr-4242.csv') -Timestamps $timestamps -Series @{
        'ThreadPool Thread Count'                             = @(18, 18, 18, 18, 18, 18, 18, 18, 18, 18, 18, 30, 31, 32, 33)
        'ThreadPool Queue Length'                             = @(0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 1, 1, 1, 1)
        'ThreadPool Completed Work Item Count (Count / 1 sec)' = @(3, 3, 3, 3, 3, 7, 3, 3, 3, 3, 3, 100, 100, 100, 100)
        'Monitor Lock Contention Count (Count / 1 sec)'        = @(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 50, 50, 50, 50)
        'CPU Usage (%)'                                       = @(1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 1.5, 20, 20, 20, 20)
    }
    New-BaselineCounterCsv -Path (Join-Path $observation 'clr-99.csv') -Timestamps $timestamps -Series @{
        'ThreadPool Thread Count'                             = @(1..15 | ForEach-Object { 77 })
        'ThreadPool Queue Length'                             = @(1..15 | ForEach-Object { 88 })
        'ThreadPool Completed Work Item Count (Count / 1 sec)' = @(1..15 | ForEach-Object { 99 })
        'Monitor Lock Contention Count (Count / 1 sec)'        = @(1..15 | ForEach-Object { 66 })
        'CPU Usage (%)'                                       = @(1..15 | ForEach-Object { 55 })
    }

    [IO.File]::WriteAllText((Join-Path $observation 'status.json'), '{"outcome":"stopped","counterTimestampTimeZone":"Etc/UTC","observedTesthosts":2}', [Text.UTF8Encoding]::new($false))
    return $observation
}

function Write-BaselineManifest {
    param([Parameter(Mandatory)] [string] $Path, [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Samples)
    [IO.File]::WriteAllText($Path, (@{ memberId = 'mes-asset-unavailable-redis-cap'; samples = $Samples } | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
}

# 只断言「抛了错」鉴别力太弱：相邻同型守卫会互相兜住，删掉任何一条都还有别的守卫抛错。
# 因此每格同时钉住失败原因里的具体位点。
function Invoke-BaselineExpectingFailure {
    param([Parameter(Mandatory)] [string] $Cell, [Parameter(Mandatory)] [string] $ManifestPath, [Parameter(Mandatory)] [string] $Root, [Parameter(Mandatory)] [string] $ExpectedCause)
    $result = $null
    try { $result = & $script:measureScript -ArtifactRoot $Root -SampleManifestPath $ManifestPath -Quiet }
    catch {
        $message = [string]$_.Exception.Message
        if (-not $message.Contains($ExpectedCause, [StringComparison]::Ordinal)) {
            throw "$Cell must report '$ExpectedCause' as the innermost verifiable cause, but reported: $message"
        }
        return $message
    }
    throw "$Cell must fail closed, but the baseline returned $($result.samples.Count) sample(s) instead of reporting missing data."
}

$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) "nerv3349-$([Guid]::NewGuid().ToString('N'))"
try {
    [IO.Directory]::CreateDirectory($fixtureRoot) | Out-Null
    $root = Join-Path $fixtureRoot 'artifacts'
    [IO.Directory]::CreateDirectory($root) | Out-Null
    $redObservation = New-BaselineArtifact -Root $root -ArtifactName 'redis-cap-dependency-summary-111-1'
    [void](New-BaselineArtifact -Root $root -ArtifactName 'redis-cap-dependency-summary-222-1')

    $controlSamples = @(
        @{ runId = '111'; runAttempt = 1; laneOutcome = 'failure'; event = 'push'; headBranch = 'main'; artifactName = 'redis-cap-dependency-summary-111-1' },
        @{ runId = '222'; runAttempt = 1; laneOutcome = 'success'; event = 'pull_request'; headBranch = 'topic'; artifactName = 'redis-cap-dependency-summary-222-1' }
    )
    $manifestPath = Join-Path $fixtureRoot 'samples.json'
    Write-BaselineManifest -Path $manifestPath -Samples $controlSamples

    # CONTROL：完整产物必须复算出逐样本读数，且红绿两侧各自成组。
    $control = & $script:measureScript -ArtifactRoot $root -SampleManifestPath $manifestPath -Quiet
    Assert-Baseline ($control.samples.Count -eq 2) 'CONTROL must measure both declared samples.'
    $red = $control.samples[0]
    Assert-Baseline ([string]::Equals([string]$red.laneColour, 'red', [StringComparison]::Ordinal)) 'A failing lane outcome must be reported as red.'
    Assert-Baseline ([string]::Equals([string]$control.samples[1].laneColour, 'green', [StringComparison]::Ordinal)) 'A succeeding lane outcome must be reported as green.'
    Assert-Baseline ($red.testhostProcessId -eq 4242) 'The target member testhost must be resolved from observations.jsonl, not the decoy member.'
    Assert-Baseline ($red.innerSubscribeSeconds.Count -eq 2) 'Only the target testhost episodes may be measured; the decoy 60s episode must be excluded.'
    Assert-Baseline ($red.window.seconds -eq 10) 'The window must be the longest target-testhost inner_subscribe episode.'
    Assert-Baseline ($red.innerSubscribeSeconds[0].seconds -eq 10 -and $red.innerSubscribeSeconds[1].seconds -eq 0.04) 'Episodes must be reported longest first with their real durations.'
    Assert-Baseline ($red.windowCounterInterpretable) 'A 10s window has enough 1Hz counter rows to be interpretable.'
    Assert-Baseline ($red.inWindow.threadPoolThreadCount.samples -eq 11) 'The window must select exactly the counter rows inside [started, terminal].'
    Assert-Baseline ($red.inWindow.threadPoolThreadCount.median -eq 18 -and $red.outsideWindow.threadPoolThreadCount.max -eq 33) 'In-window and outside-window thread counts must stay separated.'
    Assert-Baseline ($red.inWindow.threadPoolQueueLength.max -eq 10 -and $red.inWindow.threadPoolQueueLength.median -eq 5) 'Queue length statistics must come from the target CSV.'
    Assert-Baseline ($red.inWindow.threadPoolCompletedItemsRate.min -eq 3 -and $red.outsideWindow.threadPoolCompletedItemsRate.min -eq 100) 'The completed-items trough must be the in-window minimum.'
    Assert-Baseline ($red.inWindow.monitorLockContentionRate.max -eq 0) 'Monitor lock contention must be reported as measured, including when it is zero inside the window.'
    Assert-Baseline ($red.inWindow.cpuUsagePercent.max -eq 1.5) 'CPU usage must come from the target CSV rather than the decoy CSV.'
    Assert-Baseline ($control.aggregates.red.samples -eq 1 -and $control.aggregates.green.samples -eq 1) 'Aggregates must keep red and green arms separate.'
    Assert-Baseline ($control.aggregates.red.counterInterpretableSamples -eq 1) 'Interpretable-window samples must be counted separately from all samples.'
    Assert-Baseline ($control.valueDomain.Count -ge 6) 'The value domain declaration must travel with the readings.'

    # 隔离格：每格只破坏一个位点，其余产物保持 CONTROL 形状。
    $cells = [ordered]@{
        'empty-fixture-phases'         = @{ cause = 'no complete inner_subscribe episode'; damage = { [IO.File]::WriteAllText((Join-Path $redObservation 'fixture-phases.jsonl'), '') } }
        'started-without-terminal'     = @{ cause = 'no complete inner_subscribe episode'; damage = { [IO.File]::WriteAllLines((Join-Path $redObservation 'fixture-phases.jsonl'), @((New-BaselinePhaseLine -FixtureId ('a' * 32) -ConsumerId 2 -Phase 'inner_subscribe_started' -Timestamp 1000000000 -Utc '2026-09-08T06:00:00.0000000+00:00' -ProcessId 4242)), [Text.UTF8Encoding]::new($false)) } }
        'missing-fixture-phases'       = @{ cause = 'no fixture-phases.jsonl'; damage = { Remove-Item -LiteralPath (Join-Path $redObservation 'fixture-phases.jsonl') -Force } }
        'malformed-phase-line'         = @{ cause = 'unparsable fixture-phases.jsonl'; damage = { [IO.File]::WriteAllText((Join-Path $redObservation 'fixture-phases.jsonl'), "{not json`n") } }
        'missing-counter-csv'          = @{ cause = 'no CLR counter file'; damage = { Remove-Item -LiteralPath (Join-Path $redObservation 'clr-4242.csv') -Force } }
        'header-only-counter-csv'      = @{ cause = 'has no rows'; damage = { [IO.File]::WriteAllText((Join-Path $redObservation 'clr-4242.csv'), "Timestamp,Provider,Counter Name,Counter Type,Mean/Increment`n") } }
        'counter-absent-from-csv'      = @{ cause = "no 'ThreadPool Queue Length' samples"; damage = {
                $kept = @([IO.File]::ReadAllLines((Join-Path $redObservation 'clr-4242.csv')) | Where-Object { -not $_.Contains('ThreadPool Queue Length', [StringComparison]::Ordinal) })
                [IO.File]::WriteAllLines((Join-Path $redObservation 'clr-4242.csv'), $kept, [Text.UTF8Encoding]::new($false))
            }
        }
        'unparsable-counter-timestamp' = @{ cause = 'unparsable timestamp'; damage = {
                $rewritten = @([IO.File]::ReadAllLines((Join-Path $redObservation 'clr-4242.csv')) | ForEach-Object { $_.Replace('09/08/2026 06:00:05', 'not-a-timestamp') })
                [IO.File]::WriteAllLines((Join-Path $redObservation 'clr-4242.csv'), $rewritten, [Text.UTF8Encoding]::new($false))
            }
        }
        'counter-rows-outside-window'  = @{ cause = 'inner_subscribe window'; damage = {
                $rewritten = @([IO.File]::ReadAllLines((Join-Path $redObservation 'clr-4242.csv')) | ForEach-Object { $_.Replace('06:00:', '07:00:') })
                [IO.File]::WriteAllLines((Join-Path $redObservation 'clr-4242.csv'), $rewritten, [Text.UTF8Encoding]::new($false))
            }
        }
        'target-member-absent'         = @{ cause = 'no observed testhost'; damage = { [IO.File]::WriteAllLines((Join-Path $redObservation 'observations.jsonl'), @('{"kind":"testhost","utc":"2026-09-08T05:59:50Z","memberId":"quality-rework-receipt-redis-cap","processId":99}'), [Text.UTF8Encoding]::new($false)) } }
        'missing-observations'         = @{ cause = 'no observations.jsonl'; damage = { Remove-Item -LiteralPath (Join-Path $redObservation 'observations.jsonl') -Force } }
        'non-utc-counter-timezone'     = @{ cause = 'America/Los_Angeles'; damage = { [IO.File]::WriteAllText((Join-Path $redObservation 'status.json'), '{"counterTimestampTimeZone":"America/Los_Angeles"}', [Text.UTF8Encoding]::new($false)) } }
        'missing-status'               = @{ cause = 'no observation status.json'; damage = { Remove-Item -LiteralPath (Join-Path $redObservation 'status.json') -Force } }
        'missing-observation-directory' = @{ cause = 'no observation directory'; damage = { Remove-Item -LiteralPath $redObservation -Recurse -Force } }
    }
    foreach ($cell in $cells.Keys) {
        $backup = Join-Path $fixtureRoot ('backup-' + $cell)
        Copy-Item -LiteralPath $redObservation -Destination $backup -Recurse -Force
        try {
            & $cells[$cell].damage
            [void](Invoke-BaselineExpectingFailure -Cell $cell -ManifestPath $manifestPath -Root $root -ExpectedCause $cells[$cell].cause)
        }
        finally {
            if (Test-Path -LiteralPath $redObservation) { Remove-Item -LiteralPath $redObservation -Recurse -Force }
            Copy-Item -LiteralPath $backup -Destination $redObservation -Recurse -Force
            Remove-Item -LiteralPath $backup -Recurse -Force
        }
    }

    # 恢复后必须仍然复算得出同一读数：上面每格都只是临时破坏。
    $restored = & $script:measureScript -ArtifactRoot $root -SampleManifestPath $manifestPath -Quiet
    Assert-Baseline ($restored.samples[0].window.seconds -eq 10 -and $restored.samples[0].inWindow.threadPoolQueueLength.max -eq 10) 'Restoring the fixture must reproduce the CONTROL reading.'

    # manifest 与根目录侧的 fail-closed 格。
    $emptyManifest = Join-Path $fixtureRoot 'empty.json'
    Write-BaselineManifest -Path $emptyManifest -Samples @()
    [void](Invoke-BaselineExpectingFailure -Cell 'empty-manifest' -ManifestPath $emptyManifest -Root $root -ExpectedCause 'declares no samples')

    $brokenManifest = Join-Path $fixtureRoot 'broken.json'
    [IO.File]::WriteAllText($brokenManifest, '{ not json')
    [void](Invoke-BaselineExpectingFailure -Cell 'malformed-manifest' -ManifestPath $brokenManifest -Root $root -ExpectedCause 'is not valid JSON')

    $unknownOutcomeManifest = Join-Path $fixtureRoot 'outcome.json'
    Write-BaselineManifest -Path $unknownOutcomeManifest -Samples @(@{ runId = '111'; runAttempt = 1; laneOutcome = 'cancelled'; event = 'push'; headBranch = 'main'; artifactName = 'redis-cap-dependency-summary-111-1' })
    [void](Invoke-BaselineExpectingFailure -Cell 'unmeasurable-lane-outcome' -ManifestPath $unknownOutcomeManifest -Root $root -ExpectedCause 'unsupported laneOutcome')

    $missingArtifactManifest = Join-Path $fixtureRoot 'missing.json'
    Write-BaselineManifest -Path $missingArtifactManifest -Samples @(@{ runId = '333'; runAttempt = 1; laneOutcome = 'failure'; event = 'push'; headBranch = 'main'; artifactName = 'redis-cap-dependency-summary-333-1' })
    [void](Invoke-BaselineExpectingFailure -Cell 'missing-artifact' -ManifestPath $missingArtifactManifest -Root $root -ExpectedCause 'no observation directory')

    [void](Invoke-BaselineExpectingFailure -Cell 'missing-artifact-root' -ManifestPath $manifestPath -Root (Join-Path $fixtureRoot 'no-such-root') -ExpectedCause 'artifact root')

    $absentManifest = Join-Path $fixtureRoot 'no-such-manifest.json'
    [void](Invoke-BaselineExpectingFailure -Cell 'missing-manifest' -ManifestPath $absentManifest -Root $root -ExpectedCause 'sample manifest')

    # 仓库登记的样本 manifest 必须真的两侧都有样本，且只取自然 run。
    $committed = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../redis-cap-observation-baseline-samples.json') -Raw | ConvertFrom-Json
    $committedSamples = @($committed.samples)
    Assert-Baseline ($committedSamples.Count -gt 0) 'The committed sample manifest must declare samples.'
    Assert-Baseline (@($committedSamples | Where-Object { [string]::Equals([string]$_.laneOutcome, 'failure', [StringComparison]::Ordinal) }).Count -gt 0) 'The committed sample manifest must keep red samples.'
    Assert-Baseline (@($committedSamples | Where-Object { [string]::Equals([string]$_.laneOutcome, 'success', [StringComparison]::Ordinal) }).Count -gt 0) 'The committed sample manifest must keep green samples.'
    Assert-Baseline (@($committedSamples | Where-Object { [int]$_.runAttempt -ne 1 }).Count -eq 0) 'Reruns bias the arm toward green; only natural first attempts may be registered.'
    foreach ($sample in $committedSamples) {
        Assert-Baseline ([string]::Equals([string]$sample.artifactName, ('redis-cap-dependency-summary-{0}-{1}' -f [string]$sample.runId, [int]$sample.runAttempt), [StringComparison]::Ordinal)) 'Each registered sample must name the artifact its run and attempt actually produced.'
    }
}
finally { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force -ErrorAction SilentlyContinue }
Write-Host 'Redis/CAP observation baseline recomputation and fail-closed contracts passed.'
