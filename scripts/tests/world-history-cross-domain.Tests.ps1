#!/usr/bin/env pwsh
# Script-Governance:
#   Category: check
#   SideEffects:
#     - Runs the world-history cross-domain reconciliation library against in-memory fixtures
#   Writes:
#     - None
#   Cleanup:
#     - No process or external resource ownership
#   Requires:
#     - PowerShell 7

<#
    scripts/lib/WorldHistoryCrossDomain.ps1 的契约测试（#1826）。

    这份文件的存在理由是**鉴别力**：对账器只要少查一类行、把容差放宽一个量级，
    或者把「见证行」当成「实查行」，真机跑出来照样全绿。因此每一条对账规则
    都在这里配一组变异 fixture——把绿 fixture 改一处，断言它必须变红。
#>

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
. (Join-Path $repoRoot 'scripts/lib/OrdinalString.ps1')
. (Join-Path $repoRoot 'scripts/lib/WorldHistoryCrossDomain.ps1')

$script:Failures = New-Object System.Collections.Generic.List[string]

function Assert-Contract([bool] $Condition, [string] $Message) {
    if (-not $Condition) { $script:Failures.Add($Message) }
}

function Assert-Categories([object] $Report, [string[]] $Expected, [string] $Case) {
    # 发现类别是标识符，去重与排序必须走序数比较器：Sort-Object -Unique 会按 culture 折叠
    # 可忽略字符，两个不同的类别名可能被折成一个（scripts/lib/OrdinalComparisonContract.ps1）。
    $ordinal = [StringComparer]::Ordinal
    $actual = Get-NervStringsSorted -Values @($Report.findings | ForEach-Object { [string] $_.category }) -Comparer $ordinal -Unique
    $expectedSorted = Get-NervStringsSorted -Values @($Expected) -Comparer $ordinal -Unique
    $actualText = [string]::Join(',', @($actual))
    $expectedText = [string]::Join(',', @($expectedSorted))
    Assert-Contract ([string]::Equals($actualText, $expectedText, [StringComparison]::Ordinal)) `
        "$Case：期望发现类别 [$expectedText]，实际 [$actualText]。"
}

# ---------------------------------------------------------------------------
# 抽样序号算术：与 Nerv.IIP.Testing.CrossServiceSampleProbe.SampleIndexes 同一份实现。
# 3283 是 2026-07-26 全量的真实订单数，这一串是真机跑出来的那一串。
# ---------------------------------------------------------------------------
$fullScale = Get-NervWorldHistorySampleIndex -TotalOrders 3283 -SampleSize 20
Assert-Contract ([string]::Equals(
        [string]::Join(',', @($fullScale)),
        '1,165,329,493,657,821,985,1150,1314,1478,1642,1806,1970,2134,2299,2463,2627,2791,2955,3119',
        [StringComparison]::Ordinal)) `
    '全量抽样序号与真机实测不一致。'
Assert-Contract (@(Get-NervWorldHistorySampleIndex -TotalOrders 0).Count -eq 0) '零订单时不应产生抽样序号。'
Assert-Contract (@(Get-NervWorldHistorySampleIndex -TotalOrders 3 -SampleSize 20).Count -eq 3) `
    '订单数少于抽样规模时应退化为逐单全查。'
Assert-Contract ([string]::Equals(
        [string]::Join(',', @(Get-NervWorldHistorySampleIndex -TotalOrders 3 -SampleSize 20)),
        '1,2,3',
        [StringComparison]::Ordinal)) `
    '退化抽样必须覆盖全部序号。'

# ---------------------------------------------------------------------------
# 容差：口径写在库里，这里逐值钉住——放宽一个量级必须改到这一行。
# ---------------------------------------------------------------------------
$tolerance = Get-NervWorldHistoryCrossDomainTolerance
Assert-Contract ($tolerance.Quantity -eq [decimal] '0.0000005') '数量容差不是半个 numeric(18,6) 步长。'
Assert-Contract ($tolerance.Amount -eq [decimal] '0.0000005') '金额容差不是半个 numeric(18,6) 步长。'
Assert-Contract ($tolerance.TimestampTicks -eq 10) '时间戳容差不是 1 微秒（10 tick）。'

# ---------------------------------------------------------------------------
# 绿 fixture：一张走完全链的订单（序号 1），六个服务各出自己那几行。
# 每条对账规则的变异用例都从这份 fixture 改一处得到。
# ---------------------------------------------------------------------------
$fgMoment = '2026-01-15T08:00:00.0000000Z'
$shipMoment = '2026-01-19T01:21:00.0000000Z'

function New-ProbeLines {
    param(
        [Parameter(Mandatory)] [string] $Prefix,
        [Parameter(Mandatory)] [string[]] $Rows,
        [string] $Indexes = '1',
        [int] $TotalOrders = 1,
        [string] $AsOfDate = '2026-07-26',
        [string] $Scale = '1'
    )

    $lines = @("$Prefix-crossdomain-basis: asOfDate=$AsOfDate;scale=$Scale;totalOrders=$TotalOrders;sampleSize=$(@($Indexes.Split(',')).Count);indexes=$Indexes")
    foreach ($row in $Rows) {
        $lines += "$Prefix-crossdomain: $row"
    }
    return $lines
}

function New-GreenFixture {
    return [ordered]@{
        erp = New-ProbeLines -Prefix 'erp-world-history' -Rows @(
            'index=1;link=sales-order;kind=erp-sales-order;no=SO-2026-00001;expected=true;exists=true;quantity=80;amount=24000;timestamp=2026-01-05T07:19:00.0000000Z',
            'index=1;link=work-order;kind=erp-work-order-witness;no=WO-2026-00001;expected=true;exists=-;quantity=-;amount=-;timestamp=-',
            'index=1;link=finished-goods-receipt;kind=erp-finished-goods-receipt-witness;no=WO-2026-00001;expected=true;exists=-;quantity=80;amount=-;timestamp=-',
            'index=1;link=delivery-order;kind=erp-delivery-order;no=DO-2026-00001;expected=true;exists=true;quantity=80;amount=-;timestamp=2026-01-19T00:02:00.0000000Z',
            'index=1;link=shipment;kind=erp-shipment;no=DO-2026-00001;expected=true;exists=true;quantity=80;amount=-;timestamp=2026-01-19T07:29:00.0000000Z',
            'index=1;link=receivable;kind=erp-receivable;no=AR-2026-00001;expected=true;exists=true;quantity=-;amount=24000;timestamp=2026-01-19T07:29:00.0000000Z',
            'index=1;link=outbound-inspection;kind=erp-outbound-inspection-witness;no=DO-2026-00001;expected=true;exists=-;quantity=80;amount=-;timestamp=-')
        mes = New-ProbeLines -Prefix 'mes-world-history' -Rows @(
            'index=1;link=sales-order;kind=mes-sales-order-witness;no=SO-2026-00001;expected=true;exists=-;quantity=80;amount=24000;timestamp=-',
            'index=1;link=work-order;kind=mes-work-order;no=WO-2026-00001;expected=true;exists=true;quantity=82;amount=-;timestamp=2026-01-05T12:23:00.0000000Z',
            'index=1;link=operation-inspection;kind=mes-final-operation-task;no=WO-2026-00001-OP-70;expected=true;exists=true;quantity=82;amount=-;timestamp=2026-01-08T04:17:00.0000000Z',
            "index=1;link=finished-goods-receipt;kind=mes-finished-goods-receipt;no=FGR-WO-2026-00001;expected=true;exists=true;quantity=80;amount=-;timestamp=2026-01-15T12:30:00.0000000Z",
            'index=1;link=delivery-order;kind=mes-delivery-order-witness;no=DO-2026-00001;expected=true;exists=-;quantity=-;amount=-;timestamp=-',
            'index=1;link=shipment;kind=mes-shipment-witness;no=DO-2026-00001;expected=true;exists=-;quantity=80;amount=-;timestamp=-')
        quality = New-ProbeLines -Prefix 'quality-world-history' -Rows @(
            'index=1;link=operation-inspection;kind=quality-operation-inspection;no=WO-2026-00001;expected=true;exists=true;quantity=82;amount=-;timestamp=2026-01-15T09:37:00.0000000Z',
            'index=1;link=outbound-inspection;kind=quality-outbound-inspection;no=DO-2026-00001;expected=true;exists=true;quantity=80;amount=-;timestamp=2026-01-19T12:38:00.0000000Z',
            'index=1;link=sales-order;kind=quality-sales-order-witness;no=SO-2026-00001;expected=true;exists=-;quantity=80;amount=-;timestamp=-')
        inventory = New-ProbeLines -Prefix 'inventory-world-history' -Rows @(
            "index=1;link=finished-goods-receipt;kind=inventory-finished-goods-movement;no=INV-WO-2026-00001;expected=true;exists=true;quantity=80;amount=-;timestamp=$fgMoment",
            "index=1;link=shipment;kind=inventory-delivery-movement;no=DO-2026-00001:delivery-out;expected=true;exists=true;quantity=80;amount=-;timestamp=$shipMoment",
            'index=1;link=sales-order;kind=inventory-sales-order-witness;no=SO-2026-00001;expected=true;exists=-;quantity=80;amount=-;timestamp=-',
            'index=1;link=work-order;kind=inventory-work-order-witness;no=WO-2026-00001;expected=true;exists=-;quantity=-;amount=-;timestamp=-')
        wms = New-ProbeLines -Prefix 'wms-world-history' -Rows @(
            "index=1;link=finished-goods-receipt;kind=wms-finished-goods-inbound;no=IB-FGR-WO-2026-00001;expected=true;exists=true;quantity=80;amount=-;timestamp=$fgMoment",
            "index=1;link=shipment;kind=wms-delivery-outbound;no=OB-DO-2026-00001;expected=true;exists=true;quantity=80;amount=-;timestamp=$shipMoment",
            'index=1;link=sales-order;kind=wms-sales-order-witness;no=SO-2026-00001;expected=true;exists=-;quantity=80;amount=-;timestamp=-',
            'index=1;link=work-order;kind=wms-work-order-witness;no=WO-2026-00001;expected=true;exists=-;quantity=-;amount=-;timestamp=-')
        label = New-ProbeLines -Prefix 'label-world-history' -Rows @(
            'index=1;link=lot-print-batch;kind=label-lot-print-batch;no=PB-FGR-WO-2026-00001-TPL-WB-LOT-001;expected=true;exists=true;quantity=-;amount=-;timestamp=2026-01-15T07:00:00.0000000Z',
            'index=1;link=carton-print-batch;kind=label-carton-print-batch;no=PB-DO-2026-00001-TPL-WB-CTN-001;expected=true;exists=true;quantity=-;amount=-;timestamp=2026-01-19T14:40:00.0000000Z',
            'index=1;link=finished-goods-receipt;kind=label-finished-goods-receipt-witness;no=FGR-WO-2026-00001;expected=true;exists=-;quantity=80;amount=-;timestamp=-',
            'index=1;link=shipment;kind=label-shipment-witness;no=DO-2026-00001;expected=true;exists=-;quantity=80;amount=-;timestamp=-')
    }
}

$script:ServicePrefix = [ordered]@{
    erp       = 'erp-world-history'
    mes       = 'mes-world-history'
    quality   = 'quality-world-history'
    inventory = 'inventory-world-history'
    wms       = 'wms-world-history'
    label     = 'label-world-history'
}

function Get-Report([object] $Fixture) {
    $probes = @()
    foreach ($service in $script:ServicePrefix.Keys) {
        $probes += ConvertFrom-NervWorldHistoryProbeOutput `
            -Service $service `
            -Prefix ([string] $script:ServicePrefix[$service]) `
            -Lines @($Fixture[$service])
    }
    return Get-NervWorldHistoryCrossDomainReport -Probes $probes
}

function Get-MutatedReport {
    param(
        [Parameter(Mandatory)] [string] $Service,
        [Parameter(Mandatory)] [string] $Match,
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $Replacement
    )

    $fixture = New-GreenFixture
    $mutated = @()
    $hit = $false
    foreach ($line in $fixture[$Service]) {
        if ($line.Contains($Match, [StringComparison]::Ordinal)) {
            $hit = $true
            if (-not [string]::IsNullOrEmpty($Replacement)) {
                $mutated += $line.Replace($Match, $Replacement)
            }
        }
        else {
            $mutated += $line
        }
    }

    Assert-Contract $hit "变异用例没有命中任何行：'$Match'（fixture 已漂移，变异等于没做）。"
    $fixture[$Service] = $mutated
    return (Get-Report -Fixture $fixture)
}

# 绿基线：真机全量跑出来的那一组关系，必须零发现。
$green = Get-Report -Fixture (New-GreenFixture)
Assert-Categories -Report $green -Expected @() -Case '绿基线'
Assert-Contract $green.succeeded '绿基线应判通过。'
Assert-Contract ($green.documentsChecked -eq 15) "绿基线应实查 15 张单，实际 $($green.documentsChecked)。"
Assert-Contract ($green.confirmed -eq 15) "绿基线应 15 张全部确认存在，实际 $($green.confirmed)。"
Assert-Contract ($green.legitimatelyAbsent -eq 0) '绿基线不该有合法缺失。'

# ---------------------------------------------------------------------------
# 多序号 fixture：单序号 fixture 证明不了「跨 index 不串台」——
# 参照行的选取（quantityRows[0] / amountRows[0] / 时间戳组）都是在同一个 index 内取的，
# 一旦分组漏掉 index 这一维，序号 1 的 80 件会和序号 2 的 120 件互相比对。
# ---------------------------------------------------------------------------
function New-TwoIndexGreenFixture {
    $single = New-GreenFixture
    $fixture = [ordered]@{}
    foreach ($service in @($single.Keys)) {
        $lines = New-Object System.Collections.Generic.List[string]
        foreach ($line in $single[$service]) {
            if ($line.Contains('-crossdomain-basis: ', [StringComparison]::Ordinal)) {
                $lines.Add($line.Replace('totalOrders=1;sampleSize=1;indexes=1', 'totalOrders=2;sampleSize=2;indexes=1,2'))
                continue
            }

            $lines.Add($line)
            # 序号 2 刻意用另一组数量/金额：分组漏掉 index 这一维时必然报 quantity/amount-mismatch。
            $lines.Add($line.Replace('index=1;', 'index=2;').Replace('00001', '00002').
                Replace('quantity=80;', 'quantity=120;').Replace('quantity=82;', 'quantity=123;').
                Replace('amount=24000;', 'amount=36000;'))
        }
        $fixture[$service] = $lines.ToArray()
    }
    return $fixture
}

$twoIndexGreen = Get-Report -Fixture (New-TwoIndexGreenFixture)
Assert-Categories -Report $twoIndexGreen -Expected @() -Case '两序号绿基线（不得跨序号串台）'
Assert-Contract ($twoIndexGreen.documentsChecked -eq 30) `
    "两序号绿基线应实查 30 张单，实际 $($twoIndexGreen.documentsChecked)。"
Assert-Contract (@($twoIndexGreen.indexes).Count -eq 2) '两序号绿基线应报告 2 个抽样序号。'

# 只把序号 2 的出库单打掉：发现必须只落在序号 2 上，序号 1 不得被牵连。
$secondIndexFixture = New-TwoIndexGreenFixture
$secondIndexFixture['wms'] = @($secondIndexFixture['wms'] | ForEach-Object {
    $_.Replace('index=2;link=shipment;kind=wms-delivery-outbound;no=OB-DO-2026-00002;expected=true;exists=true',
               'index=2;link=shipment;kind=wms-delivery-outbound;no=OB-DO-2026-00002;expected=true;exists=false')
})
$secondIndexReport = Get-Report -Fixture $secondIndexFixture
Assert-Contract (@($secondIndexReport.findings).Count -gt 0) '打掉序号 2 的出库单必须报红。'
Assert-Contract (@($secondIndexReport.findings | Where-Object { $_.index -ne 2 }).Count -eq 0) `
    '打掉序号 2 的出库单不得在序号 1 上产生任何发现。'

# 变异 1：仓储少造一张出库单（本票缺口举的正是这个例子）。
# 连带报 orphan-document 是对的：成品箱贴挂在这张发货事实上，源单据没了它就成了孤儿。
Assert-Categories -Case '仓储少造出库单' -Expected @('document-missing', 'orphan-document') -Report (Get-MutatedReport `
    -Service 'wms' -Match 'kind=wms-delivery-outbound;no=OB-DO-2026-00001;expected=true;exists=true' `
    -Replacement 'kind=wms-delivery-outbound;no=OB-DO-2026-00001;expected=true;exists=false')

# 变异 2：库存多造一笔本不该有的成品入账。
Assert-Categories -Case '库存多造成品入账' -Expected @('expectation-drift', 'document-unexpected') -Report (Get-MutatedReport `
    -Service 'inventory' -Match 'kind=inventory-finished-goods-movement;no=INV-WO-2026-00001;expected=true' `
    -Replacement 'kind=inventory-finished-goods-movement;no=INV-WO-2026-00001;expected=false')

# 变异 3：数量漂移（MES 完工入库记 79，其余各侧记 80）。
Assert-Categories -Case '完工入库数量漂移' -Expected @('quantity-mismatch') -Report (Get-MutatedReport `
    -Service 'mes' -Match 'kind=mes-finished-goods-receipt;no=FGR-WO-2026-00001;expected=true;exists=true;quantity=80' `
    -Replacement 'kind=mes-finished-goods-receipt;no=FGR-WO-2026-00001;expected=true;exists=true;quantity=79')

# 变异 4：数量的容差边界。列是 numeric(18,6)，最小可表示步长 1e-6：
# 差 5e-7（存不进这一列，只可能是舍入残差）判绿，差 1e-6（真实差异）必须判红。
Assert-Categories -Case '数量差 5e-7（容差内）' -Expected @() -Report (Get-MutatedReport `
    -Service 'mes' -Match 'kind=mes-finished-goods-receipt;no=FGR-WO-2026-00001;expected=true;exists=true;quantity=80;' `
    -Replacement 'kind=mes-finished-goods-receipt;no=FGR-WO-2026-00001;expected=true;exists=true;quantity=80.0000005;')
Assert-Categories -Case '数量差 1e-6（容差外）' -Expected @('quantity-mismatch') -Report (Get-MutatedReport `
    -Service 'mes' -Match 'kind=mes-finished-goods-receipt;no=FGR-WO-2026-00001;expected=true;exists=true;quantity=80;' `
    -Replacement 'kind=mes-finished-goods-receipt;no=FGR-WO-2026-00001;expected=true;exists=true;quantity=80.000001;')

# 变异 5：金额的容差边界，同一口径。
# 本轮之前容差是一分且闭区间，「差一分」被判绿——这条用例现在把它钉成红。
Assert-Categories -Case '金额差 5e-7（容差内）' -Expected @() -Report (Get-MutatedReport `
    -Service 'mes' -Match 'kind=mes-sales-order-witness;no=SO-2026-00001;expected=true;exists=-;quantity=80;amount=24000' `
    -Replacement 'kind=mes-sales-order-witness;no=SO-2026-00001;expected=true;exists=-;quantity=80;amount=24000.0000005')
Assert-Categories -Case '金额差 1e-6（容差外）' -Expected @('amount-mismatch') -Report (Get-MutatedReport `
    -Service 'mes' -Match 'kind=mes-sales-order-witness;no=SO-2026-00001;expected=true;exists=-;quantity=80;amount=24000' `
    -Replacement 'kind=mes-sales-order-witness;no=SO-2026-00001;expected=true;exists=-;quantity=80;amount=24000.000001')
Assert-Categories -Case '金额差一分（旧容差会放行）' -Expected @('amount-mismatch') -Report (Get-MutatedReport `
    -Service 'mes' -Match 'kind=mes-sales-order-witness;no=SO-2026-00001;expected=true;exists=-;quantity=80;amount=24000' `
    -Replacement 'kind=mes-sales-order-witness;no=SO-2026-00001;expected=true;exists=-;quantity=80;amount=24000.01')

# 变异 6：时间戳。库存与仓储的成品入库时刻按共享 spec 是同一个表达式：
# 差 1 微秒（往返截断的上界）仍绿，差 1 毫秒必须红。
Assert-Categories -Case '成品入库时刻差 1 微秒' -Expected @() -Report (Get-MutatedReport `
    -Service 'wms' -Match "kind=wms-finished-goods-inbound;no=IB-FGR-WO-2026-00001;expected=true;exists=true;quantity=80;amount=-;timestamp=$fgMoment" `
    -Replacement 'kind=wms-finished-goods-inbound;no=IB-FGR-WO-2026-00001;expected=true;exists=true;quantity=80;amount=-;timestamp=2026-01-15T08:00:00.0000010Z')
Assert-Categories -Case '成品入库时刻差 1 毫秒' -Expected @('timestamp-mismatch') -Report (Get-MutatedReport `
    -Service 'wms' -Match "kind=wms-finished-goods-inbound;no=IB-FGR-WO-2026-00001;expected=true;exists=true;quantity=80;amount=-;timestamp=$fgMoment" `
    -Replacement 'kind=wms-finished-goods-inbound;no=IB-FGR-WO-2026-00001;expected=true;exists=true;quantity=80;amount=-;timestamp=2026-01-15T08:00:00.0010000Z')

# 变异 7：整类证据行消失——探针漏发时必须报 row-missing，而不是因为「没有可比的行」静默转绿。
Assert-Categories -Case '仓储漏发入库证据行' -Expected @('row-missing') -Report (Get-MutatedReport `
    -Service 'wms' -Match 'kind=wms-finished-goods-inbound' -Replacement '')

# 变异 8：孤儿打印批次——标签抽中的批次，其源完工入库单在 MES 库里不存在。
Assert-Categories -Case '孤儿批次标签' -Expected @('document-missing', 'orphan-document') -Report (Get-MutatedReport `
    -Service 'mes' -Match 'kind=mes-finished-goods-receipt;no=FGR-WO-2026-00001;expected=true;exists=true' `
    -Replacement 'kind=mes-finished-goods-receipt;no=FGR-WO-2026-00001;expected=true;exists=false')

# 变异 9：六侧不在对同一批单——asOfDate 漂移。
Assert-Categories -Case '基准 asOfDate 漂移' -Expected @('basis-drift') -Report (Get-MutatedReport `
    -Service 'quality' -Match 'asOfDate=2026-07-26' -Replacement 'asOfDate=2026-07-25')

# 变异 10：某一侧取错抽样序号。
$driftFixture = New-GreenFixture
$driftFixture['label'] = @($driftFixture['label'] | ForEach-Object {
    $_.Replace('totalOrders=1;sampleSize=1;indexes=1', 'totalOrders=1;sampleSize=1;indexes=2')
})
Assert-Categories -Case '抽样序号漂移' -Expected @('sample-index-drift') -Report (Get-Report -Fixture $driftFixture)

# 变异 11：某一侧完全没跑。
$missingFixture = New-GreenFixture
$missingFixture['quality'] = @()
$missingReport = Get-Report -Fixture $missingFixture
Assert-Contract (@($missingReport.findings | Where-Object {
        [string]::Equals([string] $_.category, 'probe-missing', [StringComparison]::Ordinal)
    }).Count -eq 1) '某一侧完全没跑时必须报 probe-missing。'
Assert-Contract (-not $missingReport.succeeded) '缺一侧探针时不得判通过。'

# 合法缺失不得被判红：废弃单没有工单及其下游单据。
$cancelledFixture = New-GreenFixture
foreach ($service in @($cancelledFixture.Keys)) {
    $cancelledFixture[$service] = @($cancelledFixture[$service] | ForEach-Object {
        $line = $_
        foreach ($link in @('work-order', 'operation-inspection', 'finished-goods-receipt', 'delivery-order', 'shipment', 'receivable', 'outbound-inspection', 'lot-print-batch', 'carton-print-batch')) {
            if ($line.Contains("link=$link;", [StringComparison]::Ordinal)) {
                $line = $line.Replace('expected=true;exists=true', 'expected=false;exists=false').Replace('expected=true;exists=-', 'expected=false;exists=-')
            }
        }
        $line
    })
}
$cancelledReport = Get-Report -Fixture $cancelledFixture
Assert-Categories -Report $cancelledReport -Expected @() -Case '废弃单合法缺失'
Assert-Contract ($cancelledReport.legitimatelyAbsent -eq 14) `
    "废弃单应有 14 张按规则本就不该存在，实际 $($cancelledReport.legitimatelyAbsent)。"

if ($script:Failures.Count -gt 0) {
    foreach ($failure in $script:Failures) {
        Write-Host "FAILED: $failure" -ForegroundColor Red
    }
    exit 1
}

Write-Host 'World-history cross-domain reconciliation contract tests passed.' -ForegroundColor Green
