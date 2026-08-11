# Script-Governance:
#   Category: generate
#   SideEffects:
#     - Assigns queued MES dispatch tasks to the four PDA demo workers through public BusinessGateway HTTP
#     - Starts a small number of operation tasks as the demo operators (real worker JWT, real domain gates)
#     - Creates demo-segment WMS inbound/outbound orders, putaway and picking tasks (IB-PR-DEMO-* / OB-SO-DEMO-*)
#   Writes:
#     - Business facts in the target demo session's databases via public facades only (no direct DB writes)
#   Cleanup:
#     - Idempotent by stable keys and demo-segment document numbers; safe to re-run after demo reset
#   Requires:
#     - PowerShell 7
#     - A running demo session (PlatformGateway + BusinessGateway reachable)
#     - NERV_IIP_LEADER_DEMO_ADMIN_PASSWORD in the current process
#     - NERV_IIP_LEADER_DEMO_WORKER_PASSWORD in the current process (optional; enables the in-progress task starts)

<#
.SYNOPSIS
PDA 领导演示现场态造数：给演示工人派活（MES 派工 + WMS 未完成任务）。

.DESCRIPTION
demo reset 会清掉现场态（L2）数据；本脚本在 L1 世界观种子之上，用**公开 BusinessGateway API**
重灌 PDA 走查所需的现场工作量（设定集 §9 号段，稳定幂等键，可重复执行）：

- emp010（user-emp-010，机加早班操作工）：机加工作中心的 Queued 派工任务补齐至目标数，并以其本人
  身份开工其中若干条（真实 JWT，前序/齐套等领域门禁生效，开不了的如实跳过）。
- emp012（user-emp-012，装配早班操作工）：装配工作中心的 Queued 派工任务补齐至目标数（装配序
  通常被前序机加工序阻塞，保持"待开工"是真实状态）。
- emp049（库管）：IB-PR-DEMO-01..04 收货单（含一单待检行）、前两单的上架任务、
  OB-SO-DEMO-01..03 出库单与行级钉批次的拣货任务（按库存可用批次动态选择，避开 FEFO 拆分限制）。
- emp034（检验员）：待检任务由 L1 历史引擎提供，本脚本不造。

密码只从当前进程环境变量读取（同 leader-demo 惯例），不落文件不进参数。
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^https?://')]
    [string] $BusinessGatewayUrl,

    [Parameter(Mandatory)]
    [ValidatePattern('^https?://')]
    [string] $PlatformGatewayUrl,

    [string] $OrganizationId = 'org-001',

    [string] $EnvironmentId = 'env-dev',

    [ValidateRange(1, 30)]
    [int] $DispatchTargetPerOperator = 10,

    [ValidateRange(0, 5)]
    [int] $InProgressTargetPerOperator = 2
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $repoRoot 'scripts/lib/ScriptAutomation.ps1')

$adminPassword = [Environment]::GetEnvironmentVariable('NERV_IIP_LEADER_DEMO_ADMIN_PASSWORD', 'Process')
if ([string]::IsNullOrWhiteSpace($adminPassword)) {
    throw 'NERV_IIP_LEADER_DEMO_ADMIN_PASSWORD must be set in the current process.'
}
$workerPassword = [Environment]::GetEnvironmentVariable('NERV_IIP_LEADER_DEMO_WORKER_PASSWORD', 'Process')

$businessBase = $BusinessGatewayUrl.TrimEnd('/') + '/api/business-console/v1'
$platformBase = $PlatformGatewayUrl.TrimEnd('/') + '/api/console/v1'
$scopeQuery = "organizationId=$OrganizationId&environmentId=$EnvironmentId"

function Get-NervDemoAccessToken {
    param(
        [Parameter(Mandatory)] [string] $LoginName,
        [Parameter(Mandatory)] [string] $Password
    )
    $body = @{ loginName = $LoginName; password = $Password } | ConvertTo-Json
    $response = Invoke-RestMethod -Method Post -Uri "$platformBase/auth/login" -ContentType 'application/json' -Body $body
    if (-not $response.success) {
        throw "Login failed for $LoginName."
    }
    return [string] $response.data.accessToken
}

function Invoke-NervDemoGet {
    param(
        [Parameter(Mandatory)] [string] $Token,
        [Parameter(Mandatory)] [string] $PathAndQuery
    )
    return Invoke-RestMethod -Method Get -Uri "$businessBase$PathAndQuery" -Headers @{ Authorization = "Bearer $Token" }
}

function Invoke-NervDemoPost {
    param(
        [Parameter(Mandatory)] [string] $Token,
        [Parameter(Mandatory)] [string] $PathAndQuery,
        [Parameter(Mandatory)] [object] $Body,
        # 已知的业务性拒绝（幂等重放冲突/领域门禁）不视为脚本失败；返回 $null。
        [switch] $AllowBusinessReject
    )
    $json = $Body | ConvertTo-Json -Depth 8
    try {
        return Invoke-RestMethod -Method Post -Uri "$businessBase$PathAndQuery" -Headers @{ Authorization = "Bearer $Token" } -ContentType 'application/json' -Body $json
    }
    catch {
        $status = $null
        if ($_.Exception.PSObject.Properties['Response'] -and $_.Exception.Response) {
            $status = [int] $_.Exception.Response.StatusCode
        }
        if ($AllowBusinessReject -and $status -eq 400) {
            return $null
        }
        throw
    }
}

$summary = [ordered]@{}
$adminToken = Get-NervDemoAccessToken -LoginName 'admin' -Password $adminPassword
Write-Host "已登录 admin（$PlatformGatewayUrl）。"

# ---------- 1) MES 派工：两名操作工补齐至目标数 ----------
$operators = @(
    [pscustomobject]@{ LoginName = 'emp010'; UserId = 'user-emp-010'; WorkCenters = @('WC-ROD-01', 'WC-ROD-02', 'WC-TUB-01', 'WC-GRD-01') },
    [pscustomobject]@{ LoginName = 'emp012'; UserId = 'user-emp-012'; WorkCenters = @('WC-FA-01', 'WC-FA-02', 'WC-RA-01', 'WC-VA-01') }
)

foreach ($operator in $operators) {
    $userId = $operator.UserId
    $openCount = 0
    foreach ($status in @('Queued', 'InProgress', 'Paused')) {
        $page = Invoke-NervDemoGet -Token $adminToken -PathAndQuery "/mes/dispatch-tasks?$scopeQuery&assignedUserId=$userId&status=$status&take=1"
        $openCount += [int] $page.data.total
    }

    $assigned = 0
    foreach ($workCenter in $operator.WorkCenters) {
        if (($openCount + $assigned) -ge $DispatchTargetPerOperator) { break }
        $queued = Invoke-NervDemoGet -Token $adminToken -PathAndQuery "/mes/dispatch-tasks?$scopeQuery&status=Queued&workCenterId=$workCenter&take=30"
        foreach ($task in @($queued.data.items)) {
            if (($openCount + $assigned) -ge $DispatchTargetPerOperator) { break }
            if (-not [string]::IsNullOrWhiteSpace([string] $task.assignedUserId)) { continue }
            $taskId = [string] $task.operationTaskId
            $body = @{ assignedUserId = $userId; idempotencyKey = "pda-demo-assign-$taskId-$userId" }
            $result = Invoke-NervDemoPost -Token $adminToken -PathAndQuery "/mes/dispatch-tasks/$taskId/assign?$scopeQuery" -Body $body -AllowBusinessReject
            if ($null -ne $result) { $assigned++ }
        }
    }

    # 以工人本人身份开工若干条（真实领域门禁：前序未完/齐套缺失会 400，如实跳过）。
    $started = 0
    if (-not [string]::IsNullOrWhiteSpace($workerPassword) -and $InProgressTargetPerOperator -gt 0) {
        $workerToken = Get-NervDemoAccessToken -LoginName $operator.LoginName -Password $workerPassword
        $inProgress = Invoke-NervDemoGet -Token $adminToken -PathAndQuery "/mes/dispatch-tasks?$scopeQuery&assignedUserId=$userId&status=InProgress&take=1"
        $started = [int] $inProgress.data.total
        if ($started -lt $InProgressTargetPerOperator) {
            $mine = Invoke-NervDemoGet -Token $adminToken -PathAndQuery "/mes/dispatch-tasks?$scopeQuery&assignedUserId=$userId&status=Queued&take=50"
            foreach ($task in @($mine.data.items)) {
                if ($started -ge $InProgressTargetPerOperator) { break }
                $taskId = [string] $task.operationTaskId
                $body = @{ idempotencyKey = "pda-demo-start-$taskId" }
                $result = Invoke-NervDemoPost -Token $workerToken -PathAndQuery "/mes/operation-tasks/$taskId/start?$scopeQuery" -Body $body -AllowBusinessReject
                if ($null -ne $result) { $started++ }
            }
        }
    }

    $summary["dispatch:$($operator.LoginName)"] = "新派 $assigned（原有在办 $openCount），进行中 $started"
}

# ---------- 2) WMS 收货单 + 上架任务 ----------
function Add-NervDemoInboundLine {
    param(
        [string] $LineNo, [string] $SkuCode, [string] $UomCode, [decimal] $Quantity, [string] $QualityStatus = 'unrestricted'
    )
    return @{
        lineNo = $LineNo; skuCode = $SkuCode; uomCode = $UomCode; receivedQuantity = $Quantity
        stagingLocationCode = 'WH-WB-STG-01'; qualityStatus = $QualityStatus; ownerType = 'company'
        lotNo = "LOT-DEMO-$SkuCode"
    }
}

$inboundPlan = @(
    [pscustomobject]@{ No = 'IB-PR-DEMO-01'; Source = 'PO-DEMO-01'; Putaway = $true; Lines = @(
        (Add-NervDemoInboundLine '10' 'RM-TUB-04' 'kg' 800), (Add-NervDemoInboundLine '20' 'RM-SPR-02' 'pcs' 1200)) },
    [pscustomobject]@{ No = 'IB-PR-DEMO-02'; Source = 'PO-DEMO-02'; Putaway = $true; Lines = @(
        (Add-NervDemoInboundLine '10' 'RM-ROD-01' 'kg' 600)) },
    [pscustomobject]@{ No = 'IB-PR-DEMO-03'; Source = 'PO-DEMO-03'; Putaway = $false; Lines = @(
        (Add-NervDemoInboundLine '10' 'RM-SEAL-01' 'pcs' 2000 'quality')) },
    [pscustomobject]@{ No = 'IB-PR-DEMO-04'; Source = 'PO-DEMO-04'; Putaway = $false; Lines = @(
        (Add-NervDemoInboundLine '10' 'RM-SPR-02' 'pcs' 1500), (Add-NervDemoInboundLine '20' 'RM-TUB-04' 'kg' 400)) }
)

$inboundCreated = 0
$putawayCreated = 0
foreach ($order in $inboundPlan) {
    $existing = Invoke-NervDemoGet -Token $adminToken -PathAndQuery "/wms/inbound-orders?$scopeQuery&keyword=$($order.No)&take=1"
    $orderId = $null
    if ([int] $existing.data.total -gt 0) {
        $orderId = [string] $existing.data.items[0].inboundOrderId
    }
    else {
        $body = @{
            organizationId = $OrganizationId; environmentId = $EnvironmentId
            inboundOrderNo = $order.No; sourceDocumentType = 'purchase-receipt'; sourceDocumentId = $order.Source
            siteCode = 'SITE-001'; lines = $order.Lines
        }
        $created = Invoke-NervDemoPost -Token $adminToken -PathAndQuery '/wms/inbound-orders' -Body $body
        $orderId = [string] $created.data.inboundOrderId
        $inboundCreated++
    }

    if ($order.Putaway -and $orderId) {
        $tasks = Invoke-NervDemoGet -Token $adminToken -PathAndQuery "/wms/putaway-tasks?$scopeQuery&keyword=WT-$($order.No)&take=10"
        if ([int] $tasks.data.total -eq 0) {
            $index = 0
            foreach ($line in $order.Lines) {
                $index++
                $taskNo = 'WT-{0}-{1:D2}' -f $order.No, $index
                $body = @{
                    taskNo = $taskNo; lineNo = $line.lineNo
                    fromLocationCode = 'WH-WB-STG-01'; toLocationCode = 'WH-WB-RM-01'; quantity = $line.receivedQuantity
                }
                $result = Invoke-NervDemoPost -Token $adminToken -PathAndQuery "/wms/inbound-orders/$orderId/putaway-tasks?$scopeQuery" -Body $body -AllowBusinessReject
                if ($null -ne $result) { $putawayCreated++ }
            }
        }
    }
}
$summary['wms:inbound'] = "新建收货单 $inboundCreated / 计划 $($inboundPlan.Count)，新建上架任务 $putawayCreated"

# ---------- 3) WMS 出库单 + 行级钉批次拣货任务（避开 FEFO 拆分） ----------
$fgCandidates = @('FG-QJ-M1-L', 'FG-HJ-M1-R', 'FG-QJ-P1-L', 'FG-HJ-S1-L', 'FG-QJ-P2-L', 'FG-HJ-P1-R')
$fgLots = @()
foreach ($sku in $fgCandidates) {
    $availability = Invoke-NervDemoGet -Token $adminToken -PathAndQuery "/inventory/availability?$scopeQuery&skuCode=$sku&uomCode=pcs&siteCode=SITE-001"
    $bestLot = Get-NervItemsSorted -Items @(@($availability.data.items) |
        Where-Object { $_.availableQuantity -gt 1 }) -Comparison { param($left, $right) if ([decimal]$right.availableQuantity -gt [decimal]$left.availableQuantity) { 1 } elseif ([decimal]$right.availableQuantity -lt [decimal]$left.availableQuantity) { -1 } else { 0 } } |
        Select-Object -First 1
    if ($null -ne $bestLot) {
        $fgLots += [pscustomobject]@{ Sku = $sku; LotNo = [string] $bestLot.lotNo; Location = [string] $bestLot.locationCode; Quantity = [math]::Max(1, [int] $bestLot.availableQuantity - 1) }
    }
}

$outboundCreated = 0
$pickingCreated = 0
$groupIndex = 0
for ($offset = 0; $offset + 1 -lt $fgLots.Count -and $groupIndex -lt 3; $offset += 2) {
    $groupIndex++
    $orderNo = 'OB-SO-DEMO-{0:D2}' -f $groupIndex
    $group = @($fgLots[$offset], $fgLots[$offset + 1])

    $existing = Invoke-NervDemoGet -Token $adminToken -PathAndQuery "/wms/outbound-orders?$scopeQuery&keyword=$orderNo&take=1"
    $orderId = $null
    if ([int] $existing.data.total -gt 0) {
        $orderId = [string] $existing.data.items[0].outboundOrderId
    }
    else {
        $lines = @()
        $lineIndex = 0
        foreach ($entry in $group) {
            $lineIndex++
            $lines += @{
                lineNo = [string] ($lineIndex * 10); skuCode = $entry.Sku; uomCode = 'pcs'
                requestedQuantity = $entry.Quantity; pickLocationCode = $entry.Location; lotNo = $entry.LotNo
                qualityStatus = 'unrestricted'; ownerType = 'company'
            }
        }
        # 词表与 WmsSourceDocumentTypes.DeliveryOrder 保持一致（写错字面量时链路不会报错，只会静默丢失应收）。
        # 注意：本脚本的 sourceDocumentId 是 SO-DEMO-2xx 而非 DO-2026-#####，本就不进 ERP 应收链。
        $body = @{
            organizationId = $OrganizationId; environmentId = $EnvironmentId
            outboundOrderNo = $orderNo; sourceDocumentType = 'erp-delivery-order'; sourceDocumentId = "SO-DEMO-2$('{0:D2}' -f $groupIndex)"
            siteCode = 'SITE-001'; lines = $lines
        }
        $created = Invoke-NervDemoPost -Token $adminToken -PathAndQuery '/wms/outbound-orders' -Body $body
        $orderId = [string] $created.data.outboundOrderId
        $outboundCreated++
    }

    $tasks = Invoke-NervDemoGet -Token $adminToken -PathAndQuery "/wms/picking-tasks?$scopeQuery&keyword=WT-$orderNo&take=10"
    if ($orderId -and [int] $tasks.data.total -eq 0) {
        $lineIndex = 0
        foreach ($entry in $group) {
            $lineIndex++
            $taskNo = 'WT-{0}-{1:D2}' -f $orderNo, $lineIndex
            $body = @{
                taskNo = $taskNo; lineNo = [string] ($lineIndex * 10)
                fromLocationCode = $entry.Location; toLocationCode = 'WH-WB-SHIP-01'; quantity = $entry.Quantity
            }
            $result = Invoke-NervDemoPost -Token $adminToken -PathAndQuery "/wms/outbound-orders/$orderId/picking-tasks?$scopeQuery" -Body $body -AllowBusinessReject
            if ($null -ne $result) { $pickingCreated++ }
        }
    }
}
$summary['wms:outbound'] = "新建出库单 $outboundCreated / 3，新建拣货任务 $pickingCreated"

# ---------- 4) 汇总 ----------
Write-Host ''
Write-Host '现场态造数完成：'
foreach ($key in $summary.Keys) {
    Write-Host ('  {0} -> {1}' -f $key, $summary[$key])
}
foreach ($status in @('Open')) {
    foreach ($face in @('wms/inbound-orders', 'wms/putaway-tasks', 'wms/outbound-orders', 'wms/picking-tasks')) {
        $page = Invoke-NervDemoGet -Token $adminToken -PathAndQuery "/$face`?$scopeQuery&status=$status&take=1"
        Write-Host ('  {0} [{1}] -> {2}' -f $face, $status, $page.data.total)
    }
}
Write-Host '检验员待检任务由 L1 历史引擎提供，未在本脚本范围。'
