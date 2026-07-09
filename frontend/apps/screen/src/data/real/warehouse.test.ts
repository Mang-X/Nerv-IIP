// 真实取数适配勾稽测试（MAN-467）：打 business-console WMS facade 契约桩，验证
// facade 响应 → 大屏 WarehouseBoard/WarehouseOpsTick 契约的映射（文档级出入库进度、
// 任务积压/龄期/超时按真实 createdAtUtc、盘点差异、WCS 状态分布 + 失败榜按真实时间戳、
// 诚实占位：入库行镜像单据数 / 出库无客户 / WCS 无排队态、会话守卫、tick 子集一致）。
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

vi.mock('@nerv-iip/api-client', () => ({
  listBusinessConsoleWmsInboundOrders: vi.fn(),
  listBusinessConsoleWmsOutboundOrders: vi.fn(),
  listBusinessConsoleWmsPutawayTasks: vi.fn(),
  listBusinessConsoleWmsPickingTasks: vi.fn(),
  listBusinessConsoleWmsCountExecutions: vi.fn(),
  listBusinessConsoleWmsWcsTasks: vi.fn(),
}))

import * as api from '@nerv-iip/api-client'
import { setScreenSession } from '@/data/session'
import { fetchRealWarehouseBoard, fetchRealWarehouseOpsTick } from './warehouse'

/** SDK（throwOnError:true）返回 { data: envelope }，envelope = { success, data: { items } }。 */
function ok(items: unknown[]) {
  return { data: { success: true, data: { items } } } as never
}

// 固定系统时钟，令 Date.now()/龄期/「今日」判定确定；单据时刻按「距 now N 分钟」构造。
const NOW = new Date('2026-07-09T12:00:00Z').getTime()
const iso = (minAgo: number) => new Date(NOW - minAgo * 60_000).toISOString()

beforeEach(() => {
  vi.clearAllMocks()
  vi.useFakeTimers()
  vi.setSystemTime(NOW)
  setScreenSession({ organizationId: 'org-001', environmentId: 'env-dev' })
})
afterEach(() => {
  vi.useRealTimers()
})

function stubDefaults() {
  // 入库单（文档级）：完成 1 / 在途 1 / 过账失败 1（当日）。
  vi.mocked(api.listBusinessConsoleWmsInboundOrders).mockResolvedValue(
    ok([
      {
        inboundOrderId: 'IN-1',
        inboundOrderNo: 'ASN-260703',
        status: 'InventoryPostingFailed',
        createdAtUtc: iso(30),
      },
      {
        inboundOrderId: 'IN-2',
        inboundOrderNo: 'ASN-260702',
        status: 'Open',
        createdAtUtc: iso(60),
      },
      {
        inboundOrderId: 'IN-3',
        inboundOrderNo: 'ASN-260701',
        status: 'Completed',
        createdAtUtc: iso(120),
      },
    ]),
  )
  // 出库单：在途 1（较新）/ 已发运 1（较旧）——newest-first。
  vi.mocked(api.listBusinessConsoleWmsOutboundOrders).mockResolvedValue(
    ok([
      {
        outboundOrderId: 'OUT-2',
        outboundOrderNo: 'SO-9802',
        status: 'Open',
        createdAtUtc: iso(20),
      },
      {
        outboundOrderId: 'OUT-1',
        outboundOrderNo: 'SO-9801',
        status: 'Completed',
        createdAtUtc: iso(90),
      },
    ]),
  )
  // 上架任务：超时 Open（60min）/ 正常 Open（10min）/ 今日完成 / 取消（忽略）。
  vi.mocked(api.listBusinessConsoleWmsPutawayTasks).mockResolvedValue(
    ok([
      {
        warehouseTaskId: 'PT-late',
        taskNo: 'PT-0420',
        taskType: 'Putaway',
        skuCode: 'PACK 下箱体',
        uomCode: '件',
        fromLocationCode: 'RCV-01',
        toLocationCode: 'A2-03-14',
        sourceOrderNo: 'ASN-260701',
        plannedQuantity: 12,
        status: 'Open',
        createdAtUtc: iso(60),
      },
      {
        warehouseTaskId: 'PT-ok',
        taskNo: 'PT-0421',
        skuCode: 'BMS 主控板',
        uomCode: '件',
        fromLocationCode: 'RCV-02',
        toLocationCode: 'B1-12-05',
        plannedQuantity: 8,
        status: 'Open',
        createdAtUtc: iso(10),
      },
      {
        warehouseTaskId: 'PT-done',
        taskNo: 'PT-0419',
        skuCode: '高压连接器',
        uomCode: '盒',
        status: 'Completed',
        createdAtUtc: iso(200),
        completedAtUtc: iso(15),
      },
      {
        warehouseTaskId: 'PT-cancel',
        taskNo: 'PT-0418',
        status: 'Cancelled',
        createdAtUtc: iso(40),
      },
    ]),
  )
  // 拣货任务：超时 Open（90min，带 SO 来源）/ 今日完成。
  vi.mocked(api.listBusinessConsoleWmsPickingTasks).mockResolvedValue(
    ok([
      {
        warehouseTaskId: 'PK-late',
        taskNo: 'PK-0880',
        skuCode: '电芯极片卷料',
        uomCode: '卷',
        fromLocationCode: 'P-A-07',
        toLocationCode: 'SHIP-01',
        sourceOrderNo: 'SO-9801',
        plannedQuantity: 6,
        status: 'Open',
        createdAtUtc: iso(90),
      },
      {
        warehouseTaskId: 'PK-done',
        taskNo: 'PK-0879',
        skuCode: '门内饰板总成',
        uomCode: '件',
        status: 'Completed',
        createdAtUtc: iso(180),
        completedAtUtc: iso(20),
      },
    ]),
  )
  // 盘点：Open（50min，超时）/ 今日完成有差异 / 今日完成无差异。
  vi.mocked(api.listBusinessConsoleWmsCountExecutions).mockResolvedValue(
    ok([
      {
        countExecutionId: 'CC-1',
        countNo: 'CC-12',
        skuCode: '轮速传感器',
        uomCode: '盒',
        locationCode: 'C2-05-08',
        expectedQuantity: 240,
        status: 'Open',
        createdAtUtc: iso(50),
      },
      {
        countExecutionId: 'CC-2',
        countNo: 'CC-13',
        skuCode: '电池模组端板',
        uomCode: '件',
        locationCode: 'D1-09-03',
        expectedQuantity: 120,
        countedQuantity: 117,
        varianceQuantity: -3,
        status: 'Completed',
        createdAtUtc: iso(70),
        completedAtUtc: iso(10),
      },
      {
        countExecutionId: 'CC-3',
        countNo: 'CC-14',
        skuCode: '冷却液管路',
        uomCode: '件',
        locationCode: 'A1-07-02',
        expectedQuantity: 80,
        countedQuantity: 80,
        varianceQuantity: 0,
        status: 'Completed',
        createdAtUtc: iso(80),
        completedAtUtc: iso(25),
      },
    ]),
  )
  // WCS：AGV 在链 + AGV 完成 + 堆垛机失败（12min，重试3）+ AGV 失败（重试1）。
  vi.mocked(api.listBusinessConsoleWmsWcsTasks).mockResolvedValue(
    ok([
      {
        wcsTaskId: 'W-1',
        adapterType: 'agv',
        externalTaskId: 'WCS-88101',
        status: 'Dispatched',
        attemptCount: 0,
        dispatchedAtUtc: iso(5),
      },
      {
        wcsTaskId: 'W-2',
        adapterType: 'agv',
        externalTaskId: 'WCS-88102',
        status: 'Completed',
        attemptCount: 0,
        dispatchedAtUtc: iso(30),
        completedAtUtc: iso(28),
      },
      {
        wcsTaskId: 'W-3',
        adapterType: 'Stacker',
        externalTaskId: 'WCS-88200',
        status: 'Failed',
        attemptCount: 3,
        failureCode: 'FORK_TIMEOUT',
        failureMessage: '取货超时 · 货叉未到位',
        dispatchedAtUtc: iso(20),
        failedAtUtc: iso(12),
      },
      {
        wcsTaskId: 'W-4',
        adapterType: 'agv',
        externalTaskId: 'WCS-88103',
        status: 'Failed',
        attemptCount: 1,
        failureMessage: '路径阻挡',
        dispatchedAtUtc: iso(8),
        failedAtUtc: iso(6),
      },
    ]),
  )
}

describe('fetchRealWarehouseBoard', () => {
  beforeEach(stubDefaults)

  it('入库/出库文档级进度：完成/失败按状态计数、行口径镜像单据、pct 勾稽', async () => {
    const b = await fetchRealWarehouseBoard('F01')
    expect(b.factoryId).toBe('F01')
    // 入库：3 单当日，完成 1，过账失败 1
    expect(b.inbound.docsTotal).toBe(3)
    expect(b.inbound.docsDone).toBe(1)
    expect(b.inbound.postFailedDocs).toBe(1)
    expect(b.inbound.postFailedDoc).toBe('ASN-260703')
    // facade 无行级 → 行镜像单据数（诚实占位）
    expect(b.inbound.linesDone).toBe(b.inbound.docsDone)
    expect(b.inbound.linesTotal).toBe(b.inbound.docsTotal)
    expect(b.inbound.pct).toBe(33) // 1/3
    // 出库：2 单，完成 1，最近发运取最新 Completed 单号，无客户维度
    expect(b.outbound.docsTotal).toBe(2)
    expect(b.outbound.docsDone).toBe(1)
    expect(b.outbound.customers).toBe(0)
    expect(b.outbound.latestShipment).toBe('发运单 SO-9801')
  })

  it('近 12h 流量按 createdAtUtc 落桶：长度 12、Σ = 当日窗内单据数', async () => {
    const b = await fetchRealWarehouseBoard('F01')
    expect(b.inbound.hourly).toHaveLength(12)
    expect(b.inbound.hourLabels).toHaveLength(12)
    for (const l of b.inbound.hourLabels) expect(l).toMatch(/^\d{2}:00$/)
    // 3 单入库均在近 12h 内（30/60/120min 前）→ Σ = 3
    expect(b.inbound.hourly.reduce((n, v) => n + v, 0)).toBe(3)
  })

  it('上架/拣货任务：Open→积压行、Completed(今日)→今日完成、超时按龄期>45min、按龄期降序', async () => {
    const b = await fetchRealWarehouseBoard('F01')
    // 上架：2 Open（backlog=2）、1 完成、Cancelled 忽略
    expect(b.putaway.backlog).toBe(2)
    expect(b.putaway.doneToday).toBe(1)
    expect(b.putaway.overdue).toBe(1) // 仅 60min 那条超时
    expect(b.putaway.rows.map((r) => r.id)).toEqual(['PT-0420', 'PT-0421']) // 龄期降序
    const late = b.putaway.rows[0]
    expect(late.overdue).toBe(true)
    expect(late.sku).toBe('PACK 下箱体')
    expect(late.from).toBe('RCV-01')
    expect(late.to).toBe('A2-03-14')
    expect(late.ref).toBe('ASN-260701')
    expect(late.createdAt).toMatch(/^\d{2}:\d{2}$/)
    // 拣货：1 Open（超时）、1 完成
    expect(b.pick.backlog).toBe(1)
    expect(b.pick.doneToday).toBe(1)
    expect(b.pick.overdue).toBe(1)
    expect(b.pick.rows[0].ref).toBe('SO-9801')
  })

  it('盘点：Open→未盘行、Completed(今日)→已盘、差异=有非零差额、planned=已盘+未盘', async () => {
    const b = await fetchRealWarehouseBoard('F01')
    expect(b.count.rows).toHaveLength(1) // 1 Open
    expect(b.count.counted).toBe(2) // 2 今日完成
    expect(b.count.variance).toBe(1) // 仅 CC-13 有差额 (-3)
    expect(b.count.planned).toBe(3) // 2 已盘 + 1 未盘
    expect(b.count.rows[0].overdue).toBe(true) // 50min > 45
    expect(b.count.rows[0].to).toBeUndefined()
    expect(b.count.rows[0].ref).toBeUndefined()
  })

  it('WCS：按 adapterType 归并、无排队态 queued=0、失败榜按真实时间戳龄期 + 重试降序', async () => {
    const b = await fetchRealWarehouseBoard('F01')
    // 归并：agv（在链1/完成1/失败1）+ stacker（失败1）
    expect(b.wcs.adapters).toHaveLength(2)
    const agv = b.wcs.adapters.find((a) => a.kind === 'agv')!
    expect(agv.running).toBe(1)
    expect(agv.completed).toBe(1)
    expect(agv.failed).toBe(1)
    expect(agv.total).toBe(3)
    expect(b.wcs.adapters.some((a) => a.kind === 'stacker')).toBe(true)
    // 状态分布：无 queued 态（诚实）
    expect(b.wcs.counts.queued).toBe(0)
    expect(b.wcs.counts.running).toBe(1)
    expect(b.wcs.counts.completed).toBe(1)
    expect(b.wcs.counts.failed).toBe(2)
    // 失败榜：2 条，重试降序（3 在前），龄期取 failedAtUtc
    expect(b.wcs.failures).toHaveLength(2)
    expect(b.wcs.failures[0].retries).toBe(3)
    expect(b.wcs.failures[0].kind).toBe('stacker')
    expect(b.wcs.failures[0].error).toBe('取货超时 · 货叉未到位')
    expect(b.wcs.failures[0].sinceMin).toBe(12)
    expect(b.wcs.failures[0].cmd).toBe('WCS-88200')
    expect(b.wcs.failures[0].firstAt).toMatch(/^\d{2}:\d{2}$/)
  })

  it('KPI 与明细勾稽、超时榜跨类合并按龄期降序', async () => {
    const b = await fetchRealWarehouseBoard('F01')
    expect(b.kpis.inboundPct).toBe(b.inbound.pct)
    expect(b.kpis.outboundPct).toBe(b.outbound.pct)
    expect(b.kpis.pickBacklog).toBe(b.pick.backlog)
    expect(b.kpis.putawayBacklog).toBe(b.putaway.backlog)
    expect(b.kpis.wcsFailed).toBe(b.wcs.failures.length)
    expect(b.kpis.countVariance).toBe(b.count.variance)
    expect(b.kpis.throughputLines).toBe(b.inbound.linesDone + b.outbound.linesDone)
    // 超时榜：拣货90 / 上架60 / 盘点50 → 3 条降序
    expect(b.overdueTop.map((r) => r.ageMin)).toEqual([90, 60, 50])
    expect(b.overdueTop[0].kindLabel).toBe('拣货')
    for (let i = 1; i < b.overdueTop.length; i++) {
      expect(b.overdueTop[i - 1].ageMin).toBeGreaterThanOrEqual(b.overdueTop[i].ageMin)
    }
  })

  it('空数据：全 0 不崩、无失败、无超时', async () => {
    vi.mocked(api.listBusinessConsoleWmsInboundOrders).mockResolvedValue(ok([]))
    vi.mocked(api.listBusinessConsoleWmsOutboundOrders).mockResolvedValue(ok([]))
    vi.mocked(api.listBusinessConsoleWmsPutawayTasks).mockResolvedValue(ok([]))
    vi.mocked(api.listBusinessConsoleWmsPickingTasks).mockResolvedValue(ok([]))
    vi.mocked(api.listBusinessConsoleWmsCountExecutions).mockResolvedValue(ok([]))
    vi.mocked(api.listBusinessConsoleWmsWcsTasks).mockResolvedValue(ok([]))
    const b = await fetchRealWarehouseBoard('F01')
    expect(b.inbound.docsTotal).toBe(0)
    expect(b.inbound.pct).toBe(0)
    expect(b.pick.backlog).toBe(0)
    expect(b.wcs.failures).toHaveLength(0)
    expect(b.wcs.adapters).toHaveLength(0)
    expect(b.overdueTop).toHaveLength(0)
    expect(b.kpis.throughputLines).toBe(0)
  })
})

describe('fetchRealWarehouseOpsTick', () => {
  beforeEach(stubDefaults)

  it('tick 子集与主板任务/WCS 口径一致；不请求出入库单', async () => {
    const b = await fetchRealWarehouseBoard('F01')
    vi.clearAllMocks()
    stubDefaults()
    const tick = await fetchRealWarehouseOpsTick('F01')
    expect(tick.pick.backlog).toBe(b.pick.backlog)
    expect(tick.putaway.backlog).toBe(b.putaway.backlog)
    expect(tick.count.rows).toEqual(b.count.rows)
    expect(tick.wcs.failures.length).toBe(b.wcs.failures.length)
    expect(tick.overdueTop).toEqual(b.overdueTop)
    // 高频 tick 只刷作业子集，不打出入库单端点
    expect(api.listBusinessConsoleWmsInboundOrders).not.toHaveBeenCalled()
    expect(api.listBusinessConsoleWmsOutboundOrders).not.toHaveBeenCalled()
  })
})

describe('会话守卫', () => {
  it('无 org/env 上下文 → 抛错（useScreenData 标 stale、不打后端）', async () => {
    setScreenSession({ organizationId: '', environmentId: '' })
    await expect(fetchRealWarehouseBoard('F01')).rejects.toThrow()
    await expect(fetchRealWarehouseOpsTick('F01')).rejects.toThrow()
    expect(api.listBusinessConsoleWmsInboundOrders).not.toHaveBeenCalled()
    expect(api.listBusinessConsoleWmsPutawayTasks).not.toHaveBeenCalled()
  })
})
