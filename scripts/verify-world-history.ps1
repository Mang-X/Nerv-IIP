# Script-Governance:
#   Category: verify
#   SideEffects:
#     - Runs the world-history seed engine's own consistency validators against real PostgreSQL
#     - Creates and drops throwaway databases on the configured PostgreSQL instance
#   Writes:
#     - JSON and Markdown evidence under artifacts/world-history
#   Cleanup:
#     - Runs in the foreground and creates no background process
#     - Throwaway databases are dropped by the test harness on success or handled failure
#   Requires:
#     - PowerShell 7
#     - .NET 10 SDK
#     - A reachable PostgreSQL instance (default: the infra/docker-compose.dev.yml dev instance)

<#
.SYNOPSIS
《工厂世界观设定集》L1 背景历史引擎的一致性校验证据脚本。

.DESCRIPTION
引擎自带的一致性校验器（六个服务各一份 `WorldHistoryConsistencyValidator`）是 **fail-closed**
的：seed 结束前必跑，任何一条对账不成立就抛异常让 seed 失败。本脚本通过
NERV_IIP_TEST_POSTGRES 门控的真机测试跑一遍全量生成 + 校验，并把耗时、单据量与 20 单抽样
全链引用落成可归档证据。

覆盖的对账（设定集 §7 末尾）：
- 一期 ERP：订单 → 发货 → 应收 → 凭证 → 收款 的数量与金额链；已收款订单必有凭证且金额平；
- 一期 MES：工单 → 工序任务 → 报工 → 完工入库 的数量链；
- 二期 质量：检验任务 ↔ 报工数量对账；NCR 比例与处置分布；报废量 ∈ 一期投料放大量；
- 二期 库存：现存量 = 期初 + 入 − 出；hold 施加/释放成对；
- 二期 仓储：收货/上架/拣货/出库单据与作业任务数量一致、均达终态；
- 二期 条码标签：扫码记录 ↔ 源单据（单号存在 + 时间戳不早于源单据）、标签值符合规则；
- 全链时间戳落在 [2026-01-05, 今天] 且单调、不落周日；
- 状态分布落在设定集比例的抽样容差内。

脚本另外做**跨域抽样 20 单全链对账**（#1826）：六个服务在各自的真库里查同一批抽样序号下
自己拥有的单据，脚本把六份输出按 (序号, link) 拼起来核对「该有的有没有、不该有的有没有多、
数量金额时间戳对不对得上」。这是「抽样 20 单跨域全链可追」的机器化落地形式；
对账口径、容差取值与合法缺失的判据见 `scripts/lib/WorldHistoryCrossDomain.ps1`。

本轮之前这一段是**纯字符串代数**：脚本按号段推出单据号打进证据表，存在与否留给 reviewer
逐个 grep，因此设定集 §7 承诺的那条从未被机器验证过。

.PARAMETER Scale
生成缩放比例，只作证据标注。缩放比例由被调用的 Postgres 测试自己固定（全量用例 1.0、
缩放用例 0.1），本参数不下传，因此跨域抽样对账始终针对全量那一跑。

.PARAMETER PostgresConnectionString
PostgreSQL 连接串。缺省读环境变量 NERV_IIP_TEST_POSTGRES，再缺省用本地 dev compose 实例。

.EXAMPLE
scripts/verify-world-history.ps1

.EXAMPLE
scripts/verify-world-history.ps1 -Scale 0.1
#>

[CmdletBinding()]
param(
    [ValidateRange(0.01, 1.0)]
    [double] $Scale = 1.0,

    [string] $PostgresConnectionString,

    [ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9._-]{0,47}$')]
    [string] $RunId
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')
. (Join-Path $repoRoot 'scripts/lib/WorldHistoryCrossDomain.ps1')

if (-not $RunId) {
    $RunId = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss')
}

if (-not $PostgresConnectionString) {
    $PostgresConnectionString = $env:NERV_IIP_TEST_POSTGRES
}

if (-not $PostgresConnectionString) {
    $PostgresConnectionString = 'Host=localhost;Port=15432;Username=nerv;Password=nerv;Database=nerv_iip'
    Write-Host "No connection string supplied; falling back to the local dev PostgreSQL instance." -ForegroundColor Yellow
}

$artifactRoot = Join-Path $repoRoot "artifacts/world-history/$RunId"
New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null

$env:NERV_IIP_TEST_POSTGRES = $PostgresConnectionString

$targets = @(
    [ordered]@{
        Name    = 'erp'
        Project = 'backend/services/Business/Erp/tests/Nerv.IIP.Business.Erp.Web.Tests/Nerv.IIP.Business.Erp.Web.Tests.csproj'
        Filter  = 'FullyQualifiedName~WorldHistorySeedPostgresTests'
        Prefix  = 'erp-world-history'
    },
    [ordered]@{
        Name    = 'mes'
        Project = 'backend/services/Business/Mes/tests/Nerv.IIP.Business.Mes.Web.Tests/Nerv.IIP.Business.Mes.Web.Tests.csproj'
        Filter  = 'FullyQualifiedName~WorldHistorySeedPostgresTests'
        Prefix  = 'mes-world-history'
    },
    [ordered]@{
        Name    = 'quality'
        Project = 'backend/services/Business/Quality/tests/Nerv.IIP.Business.Quality.Web.Tests/Nerv.IIP.Business.Quality.Web.Tests.csproj'
        Filter  = 'FullyQualifiedName~WorldHistoryQualitySeedPostgresTests'
        Prefix  = 'quality-world-history'
    },
    [ordered]@{
        Name    = 'inventory'
        Project = 'backend/services/Business/Inventory/tests/Nerv.IIP.Business.Inventory.Web.Tests/Nerv.IIP.Business.Inventory.Web.Tests.csproj'
        Filter  = 'FullyQualifiedName~WorldHistoryInventorySeedPostgresTests'
        Prefix  = 'inventory-world-history'
    },
    [ordered]@{
        Name    = 'wms'
        Project = 'backend/services/Business/Wms/tests/Nerv.IIP.Business.Wms.Web.Tests/Nerv.IIP.Business.Wms.Web.Tests.csproj'
        Filter  = 'FullyQualifiedName~WorldHistoryWmsSeedPostgresTests'
        Prefix  = 'wms-world-history'
    },
    [ordered]@{
        Name    = 'barcode-label'
        Project = 'backend/services/Business/BarcodeLabel/tests/Nerv.IIP.Business.BarcodeLabel.Web.Tests/Nerv.IIP.Business.BarcodeLabel.Web.Tests.csproj'
        Filter  = 'FullyQualifiedName~WorldHistoryLabelSeedPostgresTests'
        Prefix  = 'label-world-history'
    }
)

$summary = [ordered]@{
    runId                 = $RunId
    startedAtUtc          = (Get-Date).ToUniversalTime().ToString('o')
    requestedScale        = $Scale
    goLiveDate            = '2026-01-05'
    services              = [ordered]@{}
    consistencyValidator  = 'fail-closed: seed throws WorldHistoryConsistencyException on any unbalanced chain'
}

$failed = $false
$probes = @()

foreach ($target in $targets) {
    Write-Host "Running $($target.Name) world-history consistency proof..." -ForegroundColor Cyan
    $logPath = Join-Path $artifactRoot "$($target.Name)-consistency.log"

    $arguments = @(
        'test'
        (Join-Path $repoRoot $target.Project)
        '--filter'
        $target.Filter
        '--logger'
        'console;verbosity=detailed'
        '--nologo'
    )

    # Invoke-DotNetOutput 在非零退出码时抛异常，异常消息里带完整输出；这里按证据脚本的
    # 「记录失败而不中断另一侧」语义接住它，最后再统一决定退出码。
    $succeeded = $true
    $exitCode = 0
    $stdout = ''
    try {
        $result = Invoke-DotNetOutput -Arguments $arguments -WorkingDirectory $repoRoot -TimeoutSeconds 1800
        $stdout = "$($result.Stdout)`n$($result.Stderr)"
        $exitCode = $result.ExitCode
    }
    catch {
        $succeeded = $false
        $stdout = "$($_.Exception.Message)"
        $exitCode = if ($_.Exception.Data['ExitCode']) { [int] $_.Exception.Data['ExitCode'] } else { 1 }
    }

    $stdout | Set-Content -Path $logPath -Encoding utf8

    $outputLines = $stdout -split "`r?`n"
    $probes += ConvertFrom-NervWorldHistoryProbeOutput `
        -Service $target.Name `
        -Prefix $target.Prefix `
        -Lines $outputLines

    $metrics = [ordered]@{}
    $samples = @()
    foreach ($line in $outputLines) {
        $trimmed = "$line".Trim()
        if ($trimmed -match "^$($target.Prefix)-sample:\s*(.+)$") {
            $samples += $Matches[1]
            continue
        }
        if ($trimmed -match "^$($target.Prefix)-([a-z0-9-]+)=(.+)$") {
            $metrics[$Matches[1]] = $Matches[2]
        }
    }

    if (-not $succeeded) { $failed = $true }

    $summary.services[$target.Name] = [ordered]@{
        succeeded = $succeeded
        exitCode  = $exitCode
        metrics   = $metrics
        sample    = $samples
        logFile   = "$($target.Name)-consistency.log"
    }

    if ($succeeded) {
        Write-Host "  $($target.Name): consistency validator passed ($($samples.Count) sampled chains)." -ForegroundColor Green
    }
    else {
        Write-Host "  $($target.Name): FAILED (exit $exitCode). See $logPath" -ForegroundColor Red
    }
}

# 跨域抽样 20 单全链对账（#1826）。
#
# 六个服务已经在各自的真库里查过这 20 个订单序号下自己拥有的单据，并按
# Nerv.IIP.Testing.CrossServiceSampleProbe 的格式把「该不该有 / 实际有没有 / 数量 / 金额 /
# 时间戳」输出成证据行。这里把六份输出拼起来核对——「对方那一行是否真的存在」是任何单侧
# 校验器都看不见的那一面，只有在这里才成立。
#
# 合法缺失（废弃单没有工单与下游单据、打印批次按 900 张预算抽样）由各侧自己的 spec 判定
# 并写在 expected 列上，不会被当成红；判错了则会在 expectation-drift 这一类里暴露出来。
$crossDomain = Get-NervWorldHistoryCrossDomainReport -Probes @($probes)
$summary.crossDomain = $crossDomain

# -Scale 不下传给被调用的测试（缩放比例由测试自己固定），因此 summary 里把「请求的」
# 和「实际生成的」分开写：顶层只留 requestedScale，实际值取各侧探针上报的基准。
# 原先顶层那个 `scale` 与 crossDomain.scale 同名不同义，-Scale 0.1 跑出来两个数字会打架。
$summary.effectiveScale = $crossDomain.scale
$summary.scaleNote = '-Scale 只作证据标注，不下传给被调用的 Postgres 测试；effectiveScale 取各服务探针上报的基准值。'

if (-not $crossDomain.succeeded) {
    $failed = $true
    Write-Host ''
    Write-Host "Cross-domain sample reconciliation FAILED with $($crossDomain.findings.Count) finding(s):" -ForegroundColor Red
    foreach ($finding in $crossDomain.findings) {
        Write-Host "  [$($finding.category)] #$($finding.index) $($finding.link): $($finding.detail)" -ForegroundColor Red
    }
}
else {
    Write-Host ''
    Write-Host ("Cross-domain sample reconciliation passed: {0} 张单实查，{1} 张确认存在，{2} 张按规则本就不该存在。" -f `
        $crossDomain.documentsChecked, $crossDomain.confirmed, $crossDomain.legitimatelyAbsent) -ForegroundColor Green
}

$summary.completedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
$summary.succeeded = -not $failed

$jsonPath = Join-Path $artifactRoot 'world-history-consistency.json'
$summary | ConvertTo-Json -Depth 8 | Set-Content -Path $jsonPath -Encoding utf8

$markdown = New-Object System.Text.StringBuilder
[void]$markdown.AppendLine('# L1 背景历史一致性校验证据')
[void]$markdown.AppendLine()
[void]$markdown.AppendLine("- Run: ``$RunId``")
[void]$markdown.AppendLine("- Scale（``-Scale`` 参数，仅标注、不下传）: ``$Scale``")
[void]$markdown.AppendLine("- 实际生成缩放（各侧探针上报）: ``$($crossDomain.scale)``")
[void]$markdown.AppendLine("- 结论: " + $(if ($failed) { '**失败**' } else { '**通过**' }))
[void]$markdown.AppendLine()

foreach ($name in $summary.services.Keys) {
    $service = $summary.services[$name]
    [void]$markdown.AppendLine("## $($name.ToUpperInvariant())")
    [void]$markdown.AppendLine()
    [void]$markdown.AppendLine('| 指标 | 数值 |')
    [void]$markdown.AppendLine('| --- | --- |')
    foreach ($key in $service.metrics.Keys) {
        [void]$markdown.AppendLine("| $key | $($service.metrics[$key]) |")
    }
    [void]$markdown.AppendLine()
    if ($service.sample.Count -gt 0) {
        [void]$markdown.AppendLine('### 抽样全链引用（人工可追）')
        [void]$markdown.AppendLine()
        [void]$markdown.AppendLine('```text')
        foreach ($line in $service.sample) {
            [void]$markdown.AppendLine($line)
        }
        [void]$markdown.AppendLine('```')
        [void]$markdown.AppendLine()
    }
}

[void]$markdown.AppendLine('## 跨域抽样 20 单全链对账')
[void]$markdown.AppendLine()
[void]$markdown.AppendLine("- 抽样序号: ``$([string]::Join(', ', @($crossDomain.indexes)))``")
[void]$markdown.AppendLine("- 实查单据: **$($crossDomain.documentsChecked)** 张（确认存在 **$($crossDomain.confirmed)** 张，按规则本就不该存在 **$($crossDomain.legitimatelyAbsent)** 张）")
[void]$markdown.AppendLine("- 容差: 数量 ``$($crossDomain.tolerance.Quantity)``、金额 ``$($crossDomain.tolerance.Amount)``、时间戳 ``$($crossDomain.tolerance.TimestampTicks)`` tick（1 微秒，PostgreSQL timestamptz 的精度）")
[void]$markdown.AppendLine("- 结论: " + $(if ($crossDomain.succeeded) { '**通过**' } else { "**失败（$($crossDomain.findings.Count) 项）**" }))
[void]$markdown.AppendLine()

if (-not $crossDomain.succeeded) {
    [void]$markdown.AppendLine('| 序号 | link | 类别 | 说明 |')
    [void]$markdown.AppendLine('| --- | --- | --- | --- |')
    foreach ($finding in $crossDomain.findings) {
        [void]$markdown.AppendLine("| $($finding.index) | $($finding.link) | $($finding.category) | $($finding.detail) |")
    }
    [void]$markdown.AppendLine()
}

if ($crossDomain.rows.Count -gt 0) {
    [void]$markdown.AppendLine('<details><summary>逐单据明细（应存在 / 实存在 / 数量 / 金额 / 时间戳）</summary>')
    [void]$markdown.AppendLine()
    [void]$markdown.AppendLine('| 序号 | link | 服务 | 类别 | 单据号 | 应存在 | 实存在 | 数量 | 金额 | 时间戳 |')
    [void]$markdown.AppendLine('| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |')
    foreach ($row in $crossDomain.rows) {
        $existsText = if ($null -eq $row.exists) { '见证' } else { [string] $row.exists }
        $quantityText = if ($null -eq $row.quantity) { '-' } else { [string] $row.quantity }
        $amountText = if ($null -eq $row.amount) { '-' } else { [string] $row.amount }
        $timestampText = if ($null -eq $row.timestamp) { '-' } else { $row.timestamp.UtcDateTime.ToString('yyyy-MM-dd HH:mm:ss') }
        [void]$markdown.AppendLine(
            "| $($row.index) | $($row.link) | $($row.service) | $($row.kind) | $($row.no) | $($row.expected) | $existsText | $quantityText | $amountText | $timestampText |")
    }
    [void]$markdown.AppendLine()
    [void]$markdown.AppendLine('</details>')
    [void]$markdown.AppendLine()
}

$markdownPath = Join-Path $artifactRoot 'world-history-consistency.md'
$markdown.ToString() | Set-Content -Path $markdownPath -Encoding utf8

Write-Host ''
Write-Host "Evidence written to $artifactRoot" -ForegroundColor Cyan

if ($failed) {
    throw 'World-history consistency verification failed. Inspect the per-service logs and the cross-domain findings under the artifact directory.'
}

Write-Host 'World-history consistency verification passed.' -ForegroundColor Green
