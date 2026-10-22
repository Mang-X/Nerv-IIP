# Script-Governance:
#   Category: check, generate
#   SideEffects:
#     - Reads already-downloaded Redis/CAP observation artifacts from the supplied root
#   Writes:
#     - Optional baseline JSON at the exact OutputPath
#   Cleanup:
#     - None; the script owns no process, container or temporary resource
#   Requires:
#     - PowerShell 7

<#
.SYNOPSIS
从既有 `redis-cap-dependency-summary-<runId>-<attempt>` artifact 的 `observation/` 产物复算
Redis/CAP 首轮订阅停顿的连续量基线，并按 run 与红/绿逐样本标注。

.DESCRIPTION
本脚本只读既有产物，不采集、不下载、不改动任何 lane。它的值域边界由 `-OutputPath` 写出的
`valueDomain` 段声明，同时打印到 host。

度量口径（全部相对目标成员 `-MemberId` 的 testhost 进程）：

- `innerSubscribeSeconds`：`observation/fixture-phases.jsonl` 中同一
  `(fixtureId, consumerId)` 的 `inner_subscribe_started` 与终态
  （`inner_subscribe_succeeded` 或 `inner_subscribe_failed`）之间的 Stopwatch 差，
  按记录自带的 `timestampFrequency` 换算为秒。
- **窗口**定义为该样本中**最长**一次 `inner_subscribe` 的 `[started.utc, terminal.utc]`。
- 窗口内/窗口外读数来自 `observation/clr-<pid>.csv`（dotnet-counters，1 秒刷新）。
  CSV 的 `Timestamp` 列没有时区，因此本脚本强制要求 `observation/status.json` 的
  `counterTimestampTimeZone` 为 `Etc/UTC`；不是则直接失败，不做时区猜测。

任何一项缺失、为空或无法解析都会抛错，绝不静默给 0。

.EXAMPLE
./scripts/measure-redis-cap-observation-baseline.ps1 -ArtifactRoot artifacts/redis-cap-observation -OutputPath baseline.json
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ArtifactRoot,
    [string] $SampleManifestPath = (Join-Path $PSScriptRoot 'redis-cap-observation-baseline-samples.json'),
    [string] $MemberId = 'mes-asset-unavailable-redis-cap',
    [string] $OutputPath,
    [switch] $Quiet
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/ScriptAutomation.ps1')

$requiredCounters = [ordered]@{
    threadPoolThreadCount        = 'ThreadPool Thread Count'
    threadPoolQueueLength        = 'ThreadPool Queue Length'
    threadPoolCompletedItemsRate = 'ThreadPool Completed Work Item Count (Count / 1 sec)'
    monitorLockContentionRate    = 'Monitor Lock Contention Count (Count / 1 sec)'
    cpuUsagePercent              = 'CPU Usage (%)'
}

function Get-SampleQuantile {
    param([Parameter(Mandatory)] [double[]] $Values, [Parameter(Mandatory)] [double] $Quantile)
    $sorted = [double[]]$Values
    [Array]::Sort($sorted)
    if ($sorted.Length -eq 1) { return $sorted[0] }
    $position = $Quantile * ($sorted.Length - 1)
    $lower = [Math]::Floor($position)
    $upper = [Math]::Ceiling($position)
    if ($lower -eq $upper) { return $sorted[[int]$lower] }
    return $sorted[[int]$lower] + ($position - $lower) * ($sorted[[int]$upper] - $sorted[[int]$lower])
}

function Read-ObservationCounterSeries {
    param([Parameter(Mandatory)] [string] $CsvPath)
    $series = @{}
    foreach ($name in $requiredCounters.Values) { $series[$name] = [Collections.Generic.List[object]]::new() }
    $rows = @(Import-Csv -LiteralPath $CsvPath)
    if ($rows.Count -eq 0) { throw "Redis/CAP observation counter file '$CsvPath' has no rows." }
    foreach ($row in $rows) {
        $counterName = [string]$row.'Counter Name'
        if (-not $series.ContainsKey($counterName)) { continue }
        $stamp = [DateTimeOffset]::MinValue
        if (-not [DateTimeOffset]::TryParseExact([string]$row.Timestamp, 'MM/dd/yyyy HH:mm:ss', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AssumeUniversal -bor [Globalization.DateTimeStyles]::AdjustToUniversal, [ref]$stamp)) {
            throw "Redis/CAP observation counter file '$CsvPath' has an unparsable timestamp '$([string]$row.Timestamp)'."
        }
        $value = 0.0
        if (-not [double]::TryParse([string]$row.'Mean/Increment', [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$value)) {
            throw "Redis/CAP observation counter file '$CsvPath' has an unparsable value for '$counterName'."
        }
        $series[$counterName].Add([pscustomobject]@{ utc = $stamp; value = $value })
    }
    foreach ($name in $requiredCounters.Values) {
        if ($series[$name].Count -eq 0) { throw "Redis/CAP observation counter file '$CsvPath' has no '$name' samples." }
    }
    return $series
}

function Measure-ObservationSample {
    param([Parameter(Mandatory)] [object] $Sample, [Parameter(Mandatory)] [string] $Root, [Parameter(Mandatory)] [string] $Member)

    $artifactName = [string]$Sample.artifactName
    $laneOutcome = [string]$Sample.laneOutcome
    if (-not ([string]::Equals($laneOutcome, 'failure', [StringComparison]::Ordinal) -or [string]::Equals($laneOutcome, 'success', [StringComparison]::Ordinal))) {
        throw "Sample '$artifactName' declares an unsupported laneOutcome '$laneOutcome'; only 'failure' and 'success' are measurable."
    }
    $observationDirectory = Join-Path (Join-Path $Root $artifactName) 'observation'
    if (-not (Test-Path -LiteralPath $observationDirectory -PathType Container)) {
        throw "Sample '$artifactName' has no observation directory at '$observationDirectory'."
    }

    $statusPath = Join-Path $observationDirectory 'status.json'
    if (-not (Test-Path -LiteralPath $statusPath -PathType Leaf)) { throw "Sample '$artifactName' has no observation status.json." }
    $status = Get-Content -LiteralPath $statusPath -Raw | ConvertFrom-Json
    $timeZone = [string]$status.counterTimestampTimeZone
    if (-not [string]::Equals($timeZone, 'Etc/UTC', [StringComparison]::Ordinal)) {
        throw "Sample '$artifactName' recorded counter timestamps in '$timeZone'; this baseline only interprets 'Etc/UTC'."
    }

    $observationsPath = Join-Path $observationDirectory 'observations.jsonl'
    if (-not (Test-Path -LiteralPath $observationsPath -PathType Leaf)) { throw "Sample '$artifactName' has no observations.jsonl." }
    $targetProcessId = $null
    foreach ($line in [IO.File]::ReadLines($observationsPath)) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $record = $null
        try { $record = $line | ConvertFrom-Json } catch { throw "Sample '$artifactName' has an unparsable observations.jsonl line." }
        if ([string]::Equals([string]$record.kind, 'testhost', [StringComparison]::Ordinal) -and [string]::Equals([string]$record.memberId, $Member, [StringComparison]::Ordinal)) {
            $targetProcessId = [int]$record.processId
        }
    }
    if ($null -eq $targetProcessId) { throw "Sample '$artifactName' has no observed testhost for member '$Member'." }

    $phasesPath = Join-Path $observationDirectory 'fixture-phases.jsonl'
    if (-not (Test-Path -LiteralPath $phasesPath -PathType Leaf)) { throw "Sample '$artifactName' has no fixture-phases.jsonl." }
    $groups = [ordered]@{}
    foreach ($line in [IO.File]::ReadLines($phasesPath)) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $record = $null
        try { $record = $line | ConvertFrom-Json } catch { throw "Sample '$artifactName' has an unparsable fixture-phases.jsonl line." }
        if ([int]$record.processId -ne $targetProcessId) { continue }
        $key = '{0}|{1}' -f [string]$record.fixtureId, [int]$record.consumerId
        if ($null -eq $groups[$key]) { $groups[$key] = [ordered]@{} }
        $groups[$key][[string]$record.phase] = $record
    }

    $episodes = [Collections.Generic.List[object]]::new()
    foreach ($key in $groups.Keys) {
        $group = $groups[$key]
        # 用键索引取代 .Contains(<字面量>)：序数契约门禁按 AST 判定，分不清字典键查找与字符串包含。
        if ($null -eq $group['inner_subscribe_started']) { continue }
        $terminalPhase = $null
        foreach ($candidate in @('inner_subscribe_succeeded', 'inner_subscribe_failed')) {
            if ($null -ne $group[$candidate]) { $terminalPhase = $candidate }
        }
        if ($null -eq $terminalPhase) { continue }
        $started = $group['inner_subscribe_started']
        $terminal = $group[$terminalPhase]
        $frequency = [long]$started.timestampFrequency
        if ($frequency -le 0) { throw "Sample '$artifactName' has a non-positive timestampFrequency." }
        $episodes.Add([pscustomobject]@{
                fixtureId     = [string]$started.fixtureId
                consumerId    = [int]$started.consumerId
                terminalPhase = $terminalPhase
                seconds       = ([long]$terminal.timestamp - [long]$started.timestamp) / [double]$frequency
                startedUtc    = [DateTimeOffset]::Parse([string]$started.utc, [Globalization.CultureInfo]::InvariantCulture).ToUniversalTime()
                terminalUtc   = [DateTimeOffset]::Parse([string]$terminal.utc, [Globalization.CultureInfo]::InvariantCulture).ToUniversalTime()
            })
    }
    if ($episodes.Count -eq 0) {
        throw "Sample '$artifactName' has no complete inner_subscribe episode for member '$Member'."
    }

    # 按耗时降序自排：Sort-Object 的键比较是 culture collation，本仓序数契约门禁禁用它，
    # 而这里要排的本来就是 double，不需要任何字符串比较。
    $ordered = [Collections.Generic.List[object]]::new()
    $remaining = [Collections.Generic.List[object]]::new($episodes)
    while ($remaining.Count -gt 0) {
        $best = 0
        for ($index = 1; $index -lt $remaining.Count; $index++) {
            if ($remaining[$index].seconds -gt $remaining[$best].seconds) { $best = $index }
        }
        $ordered.Add($remaining[$best])
        $remaining.RemoveAt($best)
    }
    $window = $ordered[0]

    $csvPath = Join-Path $observationDirectory ('clr-{0}.csv' -f $targetProcessId)
    if (-not (Test-Path -LiteralPath $csvPath -PathType Leaf)) { throw "Sample '$artifactName' has no CLR counter file for process $targetProcessId." }
    $series = Read-ObservationCounterSeries -CsvPath $csvPath

    $inWindow = [ordered]@{}
    $outsideWindow = [ordered]@{}
    foreach ($metric in $requiredCounters.Keys) {
        $counterName = $requiredCounters[$metric]
        $inside = @($series[$counterName] | Where-Object { $_.utc -ge $window.startedUtc -and $_.utc -le $window.terminalUtc })
        $outside = @($series[$counterName] | Where-Object { $_.utc -lt $window.startedUtc -or $_.utc -gt $window.terminalUtc })
        if ($inside.Count -eq 0) {
            throw "Sample '$artifactName' has no '$counterName' sample inside the $([Math]::Round($window.seconds, 3))s inner_subscribe window."
        }
        $insideValues = [double[]]@($inside | ForEach-Object { [double]$_.value })
        $inWindow[$metric] = [ordered]@{
            samples = $inside.Count
            min     = ($insideValues | Measure-Object -Minimum).Minimum
            median  = Get-SampleQuantile -Values $insideValues -Quantile 0.5
            max     = ($insideValues | Measure-Object -Maximum).Maximum
        }
        if ($outside.Count -eq 0) {
            $outsideWindow[$metric] = [ordered]@{ samples = 0; min = $null; median = $null; max = $null }
        }
        else {
            $outsideValues = [double[]]@($outside | ForEach-Object { [double]$_.value })
            $outsideWindow[$metric] = [ordered]@{
                samples = $outside.Count
                min     = ($outsideValues | Measure-Object -Minimum).Minimum
                median  = Get-SampleQuantile -Values $outsideValues -Quantile 0.5
                max     = ($outsideValues | Measure-Object -Maximum).Maximum
            }
        }
    }

    return [ordered]@{
        runId                 = [string]$Sample.runId
        runAttempt            = [int]$Sample.runAttempt
        laneOutcome           = $laneOutcome
        laneColour            = if ([string]::Equals($laneOutcome, 'failure', [StringComparison]::Ordinal)) { 'red' } else { 'green' }
        event                 = [string]$Sample.event
        headBranch            = [string]$Sample.headBranch
        artifactName          = $artifactName
        memberId              = $Member
        testhostProcessId     = $targetProcessId
        innerSubscribeSeconds = @($ordered | ForEach-Object {
                [ordered]@{ fixtureId = $_.fixtureId; consumerId = $_.consumerId; terminalPhase = $_.terminalPhase; seconds = [Math]::Round($_.seconds, 4) }
            })
        window                = [ordered]@{
            fixtureId   = $window.fixtureId
            consumerId  = $window.consumerId
            seconds     = [Math]::Round($window.seconds, 4)
            startedUtc  = $window.startedUtc.ToString('O')
            terminalUtc = $window.terminalUtc.ToString('O')
        }
        windowCounterSamples  = $inWindow.threadPoolThreadCount.samples
        # CSV 刷新间隔 1 秒：窗口短于 3 秒时窗口内统计量由 1-3 行决定，分位数不可解释。
        windowCounterInterpretable = ($window.seconds -ge 3)
        inWindow              = $inWindow
        outsideWindow         = $outsideWindow
    }
}

if (-not (Test-Path -LiteralPath $SampleManifestPath -PathType Leaf)) {
    throw "Redis/CAP observation sample manifest '$SampleManifestPath' does not exist."
}
$manifest = $null
try { $manifest = Get-Content -LiteralPath $SampleManifestPath -Raw | ConvertFrom-Json }
catch { throw "Redis/CAP observation sample manifest '$SampleManifestPath' is not valid JSON." }
$samples = @($manifest.samples)
if ($samples.Count -eq 0) { throw "Redis/CAP observation sample manifest '$SampleManifestPath' declares no samples." }
if (-not (Test-Path -LiteralPath $ArtifactRoot -PathType Container)) {
    throw "Redis/CAP observation artifact root '$ArtifactRoot' does not exist."
}

$measured = @($samples | ForEach-Object { Measure-ObservationSample -Sample $_ -Root $ArtifactRoot -Member $MemberId })

$aggregates = [ordered]@{}
foreach ($colour in @('red', 'green')) {
    $group = @($measured | Where-Object { [string]::Equals([string]$_.laneColour, $colour, [StringComparison]::Ordinal) })
    if ($group.Count -eq 0) {
        $aggregates[$colour] = [ordered]@{ samples = 0 }
        continue
    }
    $longest = [double[]]@($group | ForEach-Object { [double]$_.window.seconds })
    $summary = [ordered]@{
        samples                    = $group.Count
        runIds                     = @($group | ForEach-Object { [string]$_.runId })
        longestInnerSubscribeSeconds = [ordered]@{
            min    = ($longest | Measure-Object -Minimum).Minimum
            p50    = [Math]::Round((Get-SampleQuantile -Values $longest -Quantile 0.5), 4)
            p90    = [Math]::Round((Get-SampleQuantile -Values $longest -Quantile 0.9), 4)
            max    = ($longest | Measure-Object -Maximum).Maximum
        }
    }
    # 计数器统计量只对窗口 >= 3s 的样本聚合；更短的窗口在 1 秒刷新的 CSV 上取不到可解释的分布。
    $interpretable = @($group | Where-Object { $_.windowCounterInterpretable })
    $summary['counterInterpretableSamples'] = $interpretable.Count
    foreach ($metric in $requiredCounters.Keys) {
        if ($interpretable.Count -eq 0) {
            $summary["$metric"] = [ordered]@{ inWindowMaxMin = $null; inWindowMaxMax = $null; inWindowMedianP50 = $null; inWindowMinMin = $null; outsideWindowMedianP50 = $null }
            continue
        }
        $inMax = [double[]]@($interpretable | ForEach-Object { [double]$_.inWindow[$metric].max })
        $inMedian = [double[]]@($interpretable | ForEach-Object { [double]$_.inWindow[$metric].median })
        $inMin = [double[]]@($interpretable | ForEach-Object { [double]$_.inWindow[$metric].min })
        $outMedian = [double[]]@($interpretable | Where-Object { $_.outsideWindow[$metric].samples -gt 0 } | ForEach-Object { [double]$_.outsideWindow[$metric].median })
        $summary["$metric"] = [ordered]@{
            inWindowMaxMin         = ($inMax | Measure-Object -Minimum).Minimum
            inWindowMaxMax         = ($inMax | Measure-Object -Maximum).Maximum
            inWindowMedianP50      = [Math]::Round((Get-SampleQuantile -Values $inMedian -Quantile 0.5), 4)
            inWindowMinMin         = ($inMin | Measure-Object -Minimum).Minimum
            outsideWindowMedianP50 = if ($outMedian.Length -gt 0) { [Math]::Round((Get-SampleQuantile -Values $outMedian -Quantile 0.5), 4) } else { $null }
        }
    }
    $aggregates[$colour] = $summary
}

$result = [ordered]@{
    memberId   = $MemberId
    generatedFrom = [ordered]@{
        sampleManifestPath = $SampleManifestPath
        artifactRoot       = $ArtifactRoot
        sampleCount        = $measured.Count
    }
    valueDomain = @(
        '覆盖：目标成员 testhost 的 inner_subscribe 耗时、以及该进程 clr-<pid>.csv 里的 ThreadPool Thread Count / ThreadPool Queue Length / ThreadPool Completed Work Item Count (rate) / Monitor Lock Contention Count (rate) / CPU Usage。',
        '窗口：每个样本取该样本中最长的一次 inner_subscribe 的 [started.utc, terminal.utc]；窗口外读数为同一 CSV 的其余行。CSV 刷新间隔 1 秒，窗口内样本数即窗口秒数。',
        '分辨率：窗口短于 3 秒的样本，其窗口内计数器统计量由 1-3 行 CSV 决定，逐样本 windowCounterInterpretable 为 false；聚合段的计数器统计量已排除这些样本，但逐样本行仍原样保留，引用时必须带上该标志。',
        '不覆盖：SE.Redis 超时异常文本里的 WORKER Busy/Min/Max、POOL QueuedItems、IOCP 与 in:，它们不在任何 artifact 里，只存在于 job 日志文本（#3236 评论 8 报缺未闭合）。ThreadPool Thread Count 与 WORKER Busy 不是同一个量，不得互相代入。',
        '不覆盖：fullstack 射程。full-chain-failure-diagnostics 是 if: failure()，绿侧无产物，该射程不做定量验收（#3236 2026-09-11 裁定）。',
        '不覆盖：Redis 服务端读数（observations.jsonl 的 kind=redis 记录）与非目标成员的 testhost。',
        '红/绿由 manifest 的 laneOutcome 声明，取自 GitHub Actions 的 Redis/CAP Transport Tests job 结论，脚本不从产物反推。'
    )
    samples    = $measured
    aggregates = $aggregates
}

if (-not $Quiet) {
    Write-Host "Redis/CAP observation baseline — member '$MemberId'，样本 $($measured.Count) 个（红 $($aggregates.red.samples) / 绿 $($aggregates.green.samples)）"
    Write-Host ''
    Write-Host ('{0,-12} {1,-4} {2,-6} {3,-22} {4,10} {5,8} {6,7} {7,7} {8,9} {9,9}' -f 'runId', 'att', 'lane', 'headBranch', 'longest(s)', 'nextEp(s)', 'thrMed', 'quMax', 'compMin', 'monMax')
    foreach ($sample in $measured) {
        $next = if ($sample.innerSubscribeSeconds.Count -gt 1) { $sample.innerSubscribeSeconds[1].seconds } else { $null }
        Write-Host ('{0,-12} {1,-4} {2,-6} {3,-22} {4,10} {5,8} {6,7} {7,7} {8,9} {9,9}' -f `
                $sample.runId, $sample.runAttempt, $sample.laneColour, ([string]$sample.headBranch).PadRight(22).Substring(0, 22), `
                $sample.window.seconds, $next, $sample.inWindow.threadPoolThreadCount.median, $sample.inWindow.threadPoolQueueLength.max, `
                $sample.inWindow.threadPoolCompletedItemsRate.min, $sample.inWindow.monitorLockContentionRate.max)
    }
    Write-Host ''
    foreach ($line in $result.valueDomain) { Write-Host "值域边界：$line" }
}

if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $fullOutputPath = [IO.Path]::GetFullPath($OutputPath)
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($fullOutputPath)) | Out-Null
    [IO.File]::WriteAllText($fullOutputPath, ($result | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
    if (-not $Quiet) { Write-Host "已写出 $fullOutputPath" }
}

return $result
