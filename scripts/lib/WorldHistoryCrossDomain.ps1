# Script-Governance:
#   Category: library
#   SideEffects:
#     - Pure parsing and reconciliation over strings supplied by the caller
#   Writes:
#     - None
#   Cleanup:
#     - No process or external resource ownership
#   Requires:
#     - PowerShell 7

<#
    L1 背景历史「跨域抽样 20 单」的对账口径（#1826）。

    以前这一段是**纯字符串代数**：脚本按号段推出 20 组单据号打进证据表，
    「这些单在各库里到底存不存在」留给 reviewer 逐个 grep。也就是说设定集 §7
    承诺的「抽样 20 单全链人工可追」从来没有被机器验证过。

    现在六个服务各自在自己的真库里查同一批抽样序号下自己拥有的单据，
    按 Nerv.IIP.Testing.CrossServiceSampleProbe 的格式输出证据行，本库把六份输出
    按 (序号, link) 拼起来对账。一个 link 就是一次物理业务事实：

      * **owner 行**（`exists` 有值）：该服务拥有这张单，并真的查了库；
      * **witness 行**（`exists=-`）：该服务不拥有这张单，只按自己那份共享 spec
        声明「它应该存在、数量应该是多少」。

    「对方那一行是否真的存在」正是任何单侧校验器都看不见的那一面：
    witness 说该有、owner 查出来没有，就是本库要报的红。
#>

Set-StrictMode -Version Latest

<#
    容差口径（本票验收第 3 条）。

    先说实测的列精度，因为容差只有相对它才有意义。探针读到的每一列——ERP 的
    `SalesOrder.TotalAmount` / `SalesOrderLine.OrderedQuantity` / `DeliveryOrderLine.ShippedQuantity` /
    `AccountReceivable.Amount`，MES 的 `WorkOrder.Quantity` / `OperationTask.PlannedQuantity` /
    `FinishedGoodsReceiptRequest.Quantity`，质量的 `InspectionTask.Quantity`，库存的
    `StockMovement.Quantity`，仓储的 `InboundOrderLine.ReceivedQuantity` /
    `OutboundOrderLine.IssuedQuantity`——在各服务的 ModelSnapshot 里都是 **`numeric(18,6)`**
    （逐列查过，不是按 decimal 默认精度推的）。

    Quantity = Amount = 0.0000005
      `numeric(18,6)` 的最小可表示步长是 1e-6，两侧的值又都由同一份确定性 decimal 算术推出、
      往返不损失精度，所以**任何在这一列里可表示的差异（≥ 1e-6）都是真实差异，必须判红**。
      容差取半个最小步长：比较用闭区间（`-le`），于是差 1e-6 判红、差 5e-7 判绿——
      后者在这一列里根本无法被存下来，只可能是舍入残差。
      本轮之前这两个值是 0.0001 / 0.01，论证写的是「金额列是 decimal(18,2)，一分是最小可表示差异」
      —— 前提错了（实为 6 位标度），后果是**恰好差一分的真实金额差异会被闭区间放行**。

    TimestampTicks = 10（= 1 微秒）
      PostgreSQL 的 timestamptz 精度是**微秒**，.NET 的 DateTimeOffset 是 100ns（tick）。
      同一个时刻写进库再读回来最多损失 9 个 tick，因此「同一时刻」的判据取 1 微秒。
      放宽到秒级会让「差了半秒」的真实漂移变成绿灯，收紧到 0 会让往返截断变成假红。
      这一条与数量/金额不同：微秒截断是**真实存在**的表示误差，确实需要吸收。
#>
function Get-NervWorldHistoryCrossDomainTolerance {
    return [ordered]@{
        Quantity       = [decimal] '0.0000005'
        Amount         = [decimal] '0.0000005'
        TimestampTicks = [long] 10
    }
}

<#
    抽样序号：与 Nerv.IIP.Testing.CrossServiceSampleProbe.SampleIndexes 同一算术。

    脚本用它复算一遍，与六侧上报的序号逐个比对——任一侧取错序号，
    六份输出就不是在说同一批单，后面所有对账都失去意义。
#>
function Get-NervWorldHistorySampleIndex {
    param(
        [Parameter(Mandatory)] [int] $TotalOrders,
        [int] $SampleSize = 20
    )

    if ($TotalOrders -lt 0) { throw 'TotalOrders must not be negative.' }
    if ($SampleSize -le 0) { throw 'SampleSize must be positive.' }
    if ($TotalOrders -eq 0) { return @() }

    $effective = [Math]::Min($SampleSize, $TotalOrders)
    $indexes = New-Object System.Collections.Generic.List[int]
    for ($slot = 0; $slot -lt $effective; $slot++) {
        $indexes.Add(1 + [int]([Math]::Floor(([long] $slot * $TotalOrders) / $effective)))
    }

    return $indexes.ToArray()
}

<#
    对账契约：每个 link 下**必须**出现的 kind，以及时间戳必须逐 tick 相等的那几组。

    RequiredKinds 是 fail-closed 的核心：探针少发一整类行时，若不逐 kind 点名，
    对账会因为「没有可比的两行」而静默转绿。
#>
function Get-NervWorldHistoryCrossDomainContract {
    return @(
        [ordered]@{
            Link          = 'sales-order'
            RequiredKinds = @(
                'erp-sales-order',
                'mes-sales-order-witness',
                'quality-sales-order-witness',
                'inventory-sales-order-witness',
                'wms-sales-order-witness')
            TimestampEqualityKinds = @()
            DependsOnLink = $null
        },
        [ordered]@{
            Link          = 'work-order'
            RequiredKinds = @(
                'mes-work-order',
                'erp-work-order-witness',
                'inventory-work-order-witness',
                'wms-work-order-witness')
            TimestampEqualityKinds = @()
            DependsOnLink = $null
        },
        [ordered]@{
            Link          = 'operation-inspection'
            RequiredKinds = @('mes-final-operation-task', 'quality-operation-inspection')
            TimestampEqualityKinds = @()
            DependsOnLink = $null
        },
        [ordered]@{
            Link          = 'finished-goods-receipt'
            RequiredKinds = @(
                'mes-finished-goods-receipt',
                'inventory-finished-goods-movement',
                'wms-finished-goods-inbound',
                'erp-finished-goods-receipt-witness',
                'label-finished-goods-receipt-witness')
            # 库存的成品入账时刻与仓储入库单的建单时刻按二期共享 spec 是**同一个表达式**
            # （Later(MomentOn(完工日, FGR 号, 'stock-fg-receipt'), 末次领料 + 45 分钟)），
            # 因此它们必须逐 tick 相等；MES 的入库过账时刻是另一条推导，只参与顺序检查。
            TimestampEqualityKinds = @('inventory-finished-goods-movement', 'wms-finished-goods-inbound')
            DependsOnLink = $null
        },
        [ordered]@{
            Link          = 'delivery-order'
            RequiredKinds = @('erp-delivery-order', 'mes-delivery-order-witness')
            TimestampEqualityKinds = @()
            DependsOnLink = $null
        },
        [ordered]@{
            Link          = 'shipment'
            RequiredKinds = @(
                'erp-shipment',
                'inventory-delivery-movement',
                'wms-delivery-outbound',
                'mes-shipment-witness',
                'label-shipment-witness')
            # 同上：出库流水的过账时刻与出库单的建单时刻是同一个表达式。
            TimestampEqualityKinds = @('inventory-delivery-movement', 'wms-delivery-outbound')
            DependsOnLink = $null
        },
        [ordered]@{
            Link          = 'receivable'
            RequiredKinds = @('erp-receivable')
            TimestampEqualityKinds = @()
            DependsOnLink = $null
        },
        [ordered]@{
            Link          = 'outbound-inspection'
            RequiredKinds = @('quality-outbound-inspection', 'erp-outbound-inspection-witness')
            TimestampEqualityKinds = @()
            DependsOnLink = $null
        },
        [ordered]@{
            Link          = 'lot-print-batch'
            RequiredKinds = @('label-lot-print-batch')
            TimestampEqualityKinds = @()
            # 打印批次按 900 张预算抽样，未被抽中的完工入库单合法地没有批次；
            # 但被抽中的那些必须挂在真实存在的完工入库上，否则就是孤儿批次。
            DependsOnLink = 'finished-goods-receipt'
        },
        [ordered]@{
            Link          = 'carton-print-batch'
            RequiredKinds = @('label-carton-print-batch')
            TimestampEqualityKinds = @()
            DependsOnLink = 'shipment'
        }
    )
}

function ConvertTo-NervWorldHistoryProbeDecimal {
    param([Parameter(Mandatory)] [AllowEmptyString()] [string] $Value)

    if ([string]::Equals($Value, '-', [StringComparison]::Ordinal)) { return $null }
    return [decimal]::Parse($Value, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture)
}

function ConvertTo-NervWorldHistoryProbeBoolean {
    param([Parameter(Mandatory)] [AllowEmptyString()] [string] $Value)

    if ([string]::Equals($Value, '-', [StringComparison]::Ordinal)) { return $null }
    if ([string]::Equals($Value, 'true', [StringComparison]::Ordinal)) { return $true }
    if ([string]::Equals($Value, 'false', [StringComparison]::Ordinal)) { return $false }
    throw "Unrecognised boolean in world-history probe row: '$Value'."
}

function ConvertTo-NervWorldHistoryProbeTimestamp {
    param([Parameter(Mandatory)] [AllowEmptyString()] [string] $Value)

    if ([string]::Equals($Value, '-', [StringComparison]::Ordinal)) { return $null }
    return [DateTimeOffset]::ParseExact(
        $Value,
        'yyyy-MM-ddTHH:mm:ss.fffffffZ',
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::AssumeUniversal -bor [Globalization.DateTimeStyles]::AdjustToUniversal)
}

function ConvertFrom-NervWorldHistoryProbeFields {
    param([Parameter(Mandatory)] [string] $Payload)

    $fields = [ordered]@{}
    foreach ($pair in $Payload.Split(';')) {
        $separator = $pair.IndexOf('=', [StringComparison]::Ordinal)
        if ($separator -lt 1) {
            throw "Malformed world-history probe field: '$pair'."
        }
        $fields[$pair.Substring(0, $separator)] = $pair.Substring($separator + 1)
    }
    return $fields
}

<#
    从一个服务的测试输出里解析出该服务的探针基准与证据行。

    Prefix 是该服务的证据前缀（如 `erp-world-history`）；不带该前缀的行一律忽略，
    因为同一份输出里还混着耗时、单据量与既有的单侧抽样行。
#>
function ConvertFrom-NervWorldHistoryProbeOutput {
    param(
        [Parameter(Mandatory)] [string] $Service,
        [Parameter(Mandatory)] [string] $Prefix,
        [AllowEmptyCollection()] [string[]] $Lines = @()
    )

    $basis = $null
    $rows = New-Object System.Collections.Generic.List[object]
    $rowMarker = "$Prefix-crossdomain: "
    $basisMarker = "$Prefix-crossdomain-basis: "

    foreach ($line in $Lines) {
        $trimmed = "$line".Trim()
        if ($trimmed.StartsWith($basisMarker, [StringComparison]::Ordinal)) {
            $fields = ConvertFrom-NervWorldHistoryProbeFields -Payload $trimmed.Substring($basisMarker.Length)
            $basis = [ordered]@{
                service     = $Service
                asOfDate    = [string] $fields['asOfDate']
                scale       = [string] $fields['scale']
                totalOrders = [int] $fields['totalOrders']
                sampleSize  = [int] $fields['sampleSize']
                indexes     = [int[]] @(([string] $fields['indexes']).Split(',') | ForEach-Object { [int] $_ })
            }
            continue
        }

        if (-not $trimmed.StartsWith($rowMarker, [StringComparison]::Ordinal)) {
            continue
        }

        $fields = ConvertFrom-NervWorldHistoryProbeFields -Payload $trimmed.Substring($rowMarker.Length)
        $rows.Add([ordered]@{
            service   = $Service
            index     = [int] $fields['index']
            link      = [string] $fields['link']
            kind      = [string] $fields['kind']
            no        = [string] $fields['no']
            expected  = ConvertTo-NervWorldHistoryProbeBoolean -Value ([string] $fields['expected'])
            exists    = ConvertTo-NervWorldHistoryProbeBoolean -Value ([string] $fields['exists'])
            quantity  = ConvertTo-NervWorldHistoryProbeDecimal -Value ([string] $fields['quantity'])
            amount    = ConvertTo-NervWorldHistoryProbeDecimal -Value ([string] $fields['amount'])
            timestamp = ConvertTo-NervWorldHistoryProbeTimestamp -Value ([string] $fields['timestamp'])
        })
    }

    return [ordered]@{
        service = $Service
        prefix  = $Prefix
        basis   = $basis
        rows    = $rows.ToArray()
    }
}

function New-NervWorldHistoryFinding {
    param(
        [Parameter(Mandatory)] [string] $Category,
        [Parameter(Mandatory)] [string] $Detail,
        [int] $Index = 0,
        [string] $Link = ''
    )

    return [ordered]@{
        category = $Category
        index    = $Index
        link     = $Link
        detail   = $Detail
    }
}

function Get-NervWorldHistoryProbeBasisFinding {
    param([Parameter(Mandatory)] [object[]] $Probes)

    $findings = New-Object System.Collections.Generic.List[object]
    $withBasis = @($Probes | Where-Object { $null -ne $_.basis })
    foreach ($probe in $Probes) {
        if ($null -eq $probe.basis) {
            $findings.Add((New-NervWorldHistoryFinding -Category 'probe-missing' `
                -Detail "服务 $($probe.service) 没有输出跨域抽样基准行，无法参与对账。"))
        }
    }

    if ($withBasis.Count -eq 0) {
        return $findings.ToArray()
    }

    $reference = $withBasis[0].basis
    foreach ($probe in $withBasis) {
        $basis = $probe.basis
        foreach ($field in @('asOfDate', 'scale', 'totalOrders', 'sampleSize')) {
            $actual = [string] $basis[$field]
            $expected = [string] $reference[$field]
            if (-not [string]::Equals($actual, $expected, [StringComparison]::Ordinal)) {
                $findings.Add((New-NervWorldHistoryFinding -Category 'basis-drift' `
                    -Detail "服务 $($probe.service) 的 $field=$actual 与 $($reference.service) 的 $expected 不一致：六侧不在对同一批单。"))
            }
        }

        $recomputed = Get-NervWorldHistorySampleIndex -TotalOrders $basis.totalOrders -SampleSize $basis.sampleSize
        $reported = [string]::Join(',', @($basis.indexes))
        $expectedIndexes = [string]::Join(',', @($recomputed))
        if (-not [string]::Equals($reported, $expectedIndexes, [StringComparison]::Ordinal)) {
            $findings.Add((New-NervWorldHistoryFinding -Category 'sample-index-drift' `
                -Detail "服务 $($probe.service) 上报的抽样序号 [$reported] 与脚本复算的 [$expectedIndexes] 不一致。"))
        }
    }

    return $findings.ToArray()
}

function Test-NervWorldHistoryWithinTolerance {
    param(
        [Parameter(Mandatory)] [decimal] $Left,
        [Parameter(Mandatory)] [decimal] $Right,
        [Parameter(Mandatory)] [decimal] $Tolerance
    )

    return [Math]::Abs($Left - $Right) -le $Tolerance
}

function Get-NervWorldHistoryLinkFinding {
    param(
        [Parameter(Mandatory)] [object] $LinkContract,
        [Parameter(Mandatory)] [int] $Index,
        [AllowEmptyCollection()] [object[]] $Rows = @(),
        [Parameter(Mandatory)] [object] $Tolerance
    )

    $findings = New-Object System.Collections.Generic.List[object]
    $link = [string] $LinkContract.Link

    foreach ($requiredKind in $LinkContract.RequiredKinds) {
        $present = @($Rows | Where-Object { [string]::Equals([string] $_.kind, [string] $requiredKind, [StringComparison]::Ordinal) })
        if ($present.Count -eq 0) {
            $findings.Add((New-NervWorldHistoryFinding -Category 'row-missing' -Index $Index -Link $link `
                -Detail "契约要求的证据行 $requiredKind 没有出现：这一侧的探针没有跑或漏发了这一类。"))
        }
    }

    if ($Rows.Count -eq 0) {
        return $findings.ToArray()
    }

    # 1. 各侧对「这张单该不该存在」的判断必须一致——不一致即共享 spec 已经漂移。
    # 布尔去重不走 Sort-Object：这里只要「各侧是不是都一样」，直接比对即可。
    $expectationTrue = $false
    $expectationFalse = $false
    foreach ($row in $Rows) {
        if ($row.expected) { $expectationTrue = $true } else { $expectationFalse = $true }
    }
    if ($expectationTrue -and $expectationFalse) {
        $detail = ($Rows | ForEach-Object { "$($_.kind)=$($_.expected)" }) -join ', '
        $findings.Add((New-NervWorldHistoryFinding -Category 'expectation-drift' -Index $Index -Link $link `
            -Detail "各侧对该单是否应存在的判断不一致：$detail。"))
    }

    # 2. 拥有这张单的一侧：应存在就必须查得到，不应存在就必须查不到。
    foreach ($row in $Rows) {
        if ($null -eq $row.exists) { continue }
        if ($row.expected -and -not $row.exists) {
            $findings.Add((New-NervWorldHistoryFinding -Category 'document-missing' -Index $Index -Link $link `
                -Detail "$($row.kind) 应有 $($row.no)，但 $($row.service) 库里查不到。"))
        }
        elseif (-not $row.expected -and $row.exists) {
            $findings.Add((New-NervWorldHistoryFinding -Category 'document-unexpected' -Index $Index -Link $link `
                -Detail "$($row.kind) 本不该有 $($row.no)，但 $($row.service) 库里查到了。"))
        }
    }

    # 3. 数量与金额：同一件业务事实在各侧必须记同一个数。
    $quantityRows = @($Rows | Where-Object { $null -ne $_.quantity })
    if ($quantityRows.Count -gt 1) {
        $reference = $quantityRows[0]
        foreach ($row in $quantityRows) {
            if (-not (Test-NervWorldHistoryWithinTolerance -Left $row.quantity -Right $reference.quantity -Tolerance $Tolerance.Quantity)) {
                $findings.Add((New-NervWorldHistoryFinding -Category 'quantity-mismatch' -Index $Index -Link $link `
                    -Detail "$($row.kind) 记 $($row.quantity)，$($reference.kind) 记 $($reference.quantity)，超出数量容差 $($Tolerance.Quantity)。"))
            }
        }
    }

    $amountRows = @($Rows | Where-Object { $null -ne $_.amount })
    if ($amountRows.Count -gt 1) {
        $reference = $amountRows[0]
        foreach ($row in $amountRows) {
            if (-not (Test-NervWorldHistoryWithinTolerance -Left $row.amount -Right $reference.amount -Tolerance $Tolerance.Amount)) {
                $findings.Add((New-NervWorldHistoryFinding -Category 'amount-mismatch' -Index $Index -Link $link `
                    -Detail "$($row.kind) 记 $($row.amount)，$($reference.kind) 记 $($reference.amount)，超出金额容差 $($Tolerance.Amount)。"))
            }
        }
    }

    # 4. 时间戳：契约点名的那几行按共享 spec 是同一个表达式，必须落在同一微秒。
    $equalityKinds = @($LinkContract.TimestampEqualityKinds)
    if ($equalityKinds.Count -gt 1) {
        $stamped = @()
        foreach ($row in $Rows) {
            if ($null -eq $row.timestamp) { continue }
            foreach ($kind in $equalityKinds) {
                if ([string]::Equals([string] $row.kind, [string] $kind, [StringComparison]::Ordinal)) {
                    $stamped += $row
                    break
                }
            }
        }

        if ($stamped.Count -gt 1) {
            $reference = $stamped[0]
            foreach ($row in $stamped) {
                $delta = [Math]::Abs(($row.timestamp - $reference.timestamp).Ticks)
                if ($delta -gt $Tolerance.TimestampTicks) {
                    $findings.Add((New-NervWorldHistoryFinding -Category 'timestamp-mismatch' -Index $Index -Link $link `
                        -Detail "$($row.kind) 记 $($row.timestamp.UtcDateTime.ToString('o'))，$($reference.kind) 记 $($reference.timestamp.UtcDateTime.ToString('o'))，相差 $delta tick，超出 $($Tolerance.TimestampTicks) tick 容差。"))
                }
            }
        }
    }

    return $findings.ToArray()
}

<#
    跨链接依赖：打印批次是抽样产生的，被抽中的那些必须挂在真实存在的源单据上。

    这条规则单靠标签域自己无法成立——它看不见完工入库单和发货单在不在。
#>
function Get-NervWorldHistoryDependencyFinding {
    param(
        [Parameter(Mandatory)] [object] $LinkContract,
        [Parameter(Mandatory)] [int] $Index,
        [AllowEmptyCollection()] [object[]] $Rows = @(),
        [AllowEmptyCollection()] [object[]] $DependencyRows = @()
    )

    $findings = New-Object System.Collections.Generic.List[object]
    $link = [string] $LinkContract.Link
    $dependsOn = [string] $LinkContract.DependsOnLink
    if ([string]::IsNullOrEmpty($dependsOn)) {
        return $findings.ToArray()
    }

    foreach ($row in $Rows) {
        if (-not $row.expected) { continue }
        foreach ($dependency in $DependencyRows) {
            if ($null -eq $dependency.exists) { continue }
            if (-not $dependency.exists) {
                $findings.Add((New-NervWorldHistoryFinding -Category 'orphan-document' -Index $Index -Link $link `
                    -Detail "$($row.kind) 抽中了 $($row.no)，但它的源单据 $($dependency.kind) $($dependency.no) 在 $($dependency.service) 库里不存在。"))
            }
        }
    }

    return $findings.ToArray()
}

<#
    把六个服务的探针输出对成一份跨域抽样对账报告。

    Probes 是 ConvertFrom-NervWorldHistoryProbeOutput 的产物数组。
    返回结果里的 succeeded 为 $false 即代表抽样对账失败——脚本据此报红。
#>
function Get-NervWorldHistoryCrossDomainReport {
    param([AllowEmptyCollection()] [object[]] $Probes = @())

    $tolerance = Get-NervWorldHistoryCrossDomainTolerance
    $contract = Get-NervWorldHistoryCrossDomainContract
    $findings = New-Object System.Collections.Generic.List[object]
    foreach ($finding in (Get-NervWorldHistoryProbeBasisFinding -Probes $Probes)) {
        $findings.Add($finding)
    }

    $rows = New-Object System.Collections.Generic.List[object]
    foreach ($probe in $Probes) {
        foreach ($row in $probe.rows) {
            $rows.Add($row)
        }
    }

    $withBasis = @($Probes | Where-Object { $null -ne $_.basis })
    # 注意：`$x = if (...) { @(...) }` 在单元素时会被管道拆包成标量，
    # 因此这里显式赋值而不是让 if 表达式的值流出来。
    $indexes = @()
    if ($withBasis.Count -gt 0) { $indexes = [int[]] @($withBasis[0].basis.indexes) }

    $confirmed = 0
    $legitimatelyAbsent = 0
    $ownedChecked = 0

    foreach ($index in $indexes) {
        $indexRows = @($rows | Where-Object { $_.index -eq $index })
        foreach ($linkContract in $contract) {
            $link = [string] $linkContract.Link
            $linkRows = @($indexRows | Where-Object { [string]::Equals([string] $_.link, $link, [StringComparison]::Ordinal) })
            foreach ($finding in (Get-NervWorldHistoryLinkFinding -LinkContract $linkContract -Index $index -Rows $linkRows -Tolerance $tolerance)) {
                $findings.Add($finding)
            }

            $dependsOn = [string] $linkContract.DependsOnLink
            if (-not [string]::IsNullOrEmpty($dependsOn)) {
                $dependencyRows = @($indexRows | Where-Object { [string]::Equals([string] $_.link, $dependsOn, [StringComparison]::Ordinal) })
                foreach ($finding in (Get-NervWorldHistoryDependencyFinding -LinkContract $linkContract -Index $index -Rows $linkRows -DependencyRows $dependencyRows)) {
                    $findings.Add($finding)
                }
            }

            foreach ($row in $linkRows) {
                if ($null -eq $row.exists) { continue }
                $ownedChecked++
                if ($row.expected -and $row.exists) { $confirmed++ }
                elseif (-not $row.expected -and -not $row.exists) { $legitimatelyAbsent++ }
            }
        }
    }

    $basis = if ($withBasis.Count -gt 0) { $withBasis[0].basis } else { $null }
    return [ordered]@{
        succeeded          = ($findings.Count -eq 0)
        asOfDate           = if ($null -ne $basis) { [string] $basis.asOfDate } else { '' }
        scale              = if ($null -ne $basis) { [string] $basis.scale } else { '' }
        totalOrders        = if ($null -ne $basis) { [int] $basis.totalOrders } else { 0 }
        sampleSize         = @($indexes).Count
        indexes            = @($indexes)
        services           = @($Probes | ForEach-Object { [string] $_.service })
        documentsChecked   = $ownedChecked
        confirmed          = $confirmed
        legitimatelyAbsent = $legitimatelyAbsent
        tolerance          = $tolerance
        findings           = $findings.ToArray()
        rows               = $rows.ToArray()
    }
}
