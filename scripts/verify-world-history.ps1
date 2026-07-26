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

脚本另外输出**跨域抽样 20 单全链引用表**：按号段代数从订单序号推导出该单在六个服务里的
单据号，reviewer 可以逐个 grep 各库核对——这是「抽样 20 单跨域全链人工可追」的落地形式。

.PARAMETER Scale
生成缩放比例。1.0 = 全量（约 3200 单 / 3600 工单），0.1 = 约十分之一的快速验证。

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
    scale                 = $Scale
    goLiveDate            = '2026-01-05'
    services              = [ordered]@{}
    consistencyValidator  = 'fail-closed: seed throws WorldHistoryConsistencyException on any unbalanced chain'
}

$failed = $false

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

    $metrics = [ordered]@{}
    $samples = @()
    foreach ($line in ($stdout -split "`r?`n")) {
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

# 跨域抽样 20 单全链引用：号段是纯代数（设定集 §9），从订单序号即可推出该单在六个服务里的单据号。
# 抽样只挑「一定有工单」的序号做不到——废弃单占 2%，序号本身看不出来——所以这里如实输出推导出的
# 单据号，并注明「废弃单没有工单/发货侧单据」，由 reviewer 按各库实际存在与否核对。
$crossDomainSample = @()
$totalOrders = 0
if ($summary.services.Contains('erp') -and $summary.services['erp'].metrics.Contains('orders')) {
    [void][int]::TryParse($summary.services['erp'].metrics['orders'], [ref] $totalOrders)
}

if ($totalOrders -gt 0) {
    $sampleSize = [Math]::Min(20, $totalOrders)
    for ($slot = 0; $slot -lt $sampleSize; $slot++) {
        $index = 1 + [int](($slot * $totalOrders) / $sampleSize)
        $salesOrderNo = 'SO-2026-{0:D5}' -f $index
        $workOrderNo = 'WO-2026-{0:D5}' -f $index
        $deliveryOrderNo = 'DO-2026-{0:D5}' -f $index
        $crossDomainSample += [ordered]@{
            index        = $index
            erpSalesOrder = $salesOrderNo
            erpQuotation  = 'QUO-2026-{0:D5}' -f $index
            erpDelivery   = $deliveryOrderNo
            erpReceivable = 'AR-2026-{0:D5}' -f $index
            mesWorkOrder  = $workOrderNo
            mesFinalOperationTask = "$workOrderNo-OP-70"
            mesFinishedGoodsReceipt = "FGR-$workOrderNo"
            producedLot   = "LOT-$workOrderNo"
            qualityOperationInspection = "$workOrderNo/70"
            qualityFinalInspection = $deliveryOrderNo
            inventoryFinishedGoodsMovement = "INV-$workOrderNo"
            wmsInbound    = "IB-FGR-$workOrderNo"
            wmsOutbound   = "OB-$deliveryOrderNo"
            labelLotPrintBatch = "PB-FGR-$workOrderNo-TPL-WB-LOT-001"
            labelCartonPrintBatch = "PB-$deliveryOrderNo-TPL-WB-CTN-001"
        }
    }
}

$summary.crossDomainSample = $crossDomainSample
$summary.crossDomainSampleNote = '废弃单（约 2%）没有工单及其下游单据；打印批次按 900 张预算抽样，未被抽中的单据没有打印批次。'

$summary.completedAtUtc = (Get-Date).ToUniversalTime().ToString('o')
$summary.succeeded = -not $failed

$jsonPath = Join-Path $artifactRoot 'world-history-consistency.json'
$summary | ConvertTo-Json -Depth 8 | Set-Content -Path $jsonPath -Encoding utf8

$markdown = New-Object System.Text.StringBuilder
[void]$markdown.AppendLine('# L1 背景历史一致性校验证据')
[void]$markdown.AppendLine()
[void]$markdown.AppendLine("- Run: ``$RunId``")
[void]$markdown.AppendLine("- Scale: ``$Scale``")
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

if ($crossDomainSample.Count -gt 0) {
    [void]$markdown.AppendLine('## 跨域抽样 20 单全链引用（人工可追）')
    [void]$markdown.AppendLine()
    [void]$markdown.AppendLine($summary.crossDomainSampleNote)
    [void]$markdown.AppendLine()
    [void]$markdown.AppendLine('| # | 销售订单 | 工单 | 终检工序 | 完工入库 | 成品批次 | 库存移动 | 仓储入库单 | 发货单 | 仓储出库单 | 批次标签打印 |')
    [void]$markdown.AppendLine('| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |')
    foreach ($row in $crossDomainSample) {
        [void]$markdown.AppendLine(
            "| $($row.index) | $($row.erpSalesOrder) | $($row.mesWorkOrder) | $($row.mesFinalOperationTask) | " +
            "$($row.mesFinishedGoodsReceipt) | $($row.producedLot) | $($row.inventoryFinishedGoodsMovement) | " +
            "$($row.wmsInbound) | $($row.erpDelivery) | $($row.wmsOutbound) | $($row.labelLotPrintBatch) |")
    }
    [void]$markdown.AppendLine()
}

$markdownPath = Join-Path $artifactRoot 'world-history-consistency.md'
$markdown.ToString() | Set-Content -Path $markdownPath -Encoding utf8

Write-Host ''
Write-Host "Evidence written to $artifactRoot" -ForegroundColor Cyan

if ($failed) {
    throw 'World-history consistency verification failed. Inspect the per-service logs under the artifact directory.'
}

Write-Host 'World-history consistency verification passed.' -ForegroundColor Green
