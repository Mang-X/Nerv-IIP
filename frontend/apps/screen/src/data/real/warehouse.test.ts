// 真实取数适配勾稽测试（MAN-467，含 review 修正的完整闭包聚合）：打 business-console WMS
// facade 契约桩，验证 facade 响应 → 大屏 WarehouseBoard/WarehouseOpsTick 契约的映射，重点覆盖：
//   - 完整覆盖（非首页近似）：open 积压跨第二页不漏；未恢复 WCS 失败经 `failed:true` 隔离取尽；
//     当日窗口按时刻降序翻页、命中昨日即早停（不误计跨日单据）。
//   - 文档级出入库进度、盘点差异、WCS 状态分布、诚实占位、会话守卫、tick 子集。
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

// 固定系统时钟；单据时刻按「距 now N 分钟」；昨日项落在本地零点前 1min（任何 TZ 都属昨天）。
const NOW = new Date('2026-07-09T12:00:00Z').getTime()
const iso = (minAgo: number) => new Date(NOW - minAgo * 60_000).toISOString()
const localMidnight = (() => {
  const d = new Date(NOW)
  d.setHours(0, 0, 0, 0)
  return d.getTime()
})()
const isoYesterday = new Date(localMidnight - 60_000).toISOString()

/** 模拟后端 list：按 query.status / query.failed 过滤，按 skip/take 分页（rows 须 newest-first）。 */
function makeList<T extends { status?: string }>(rows: T[]) {
  return (opts: { query: Record<string, unknown> }) => {
    const q = opts.query
    let r = rows
    if (typeof q.status === 'string') r = r.filter((x) => x.status === q.status)
    if (q.failed === true) r = r.filter((x) => x.status === 'Failed')
    const skip = (q.skip as number) ?? 0
    const take = (q.take as number) ?? r.length
    return Promise.resolve(ok(r.slice(skip, skip + take)))
  }
}

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
  // 入库单：当日 3 单（完成1/在途1/过账失败1）+ 昨日 1 单（早停应排除）。
  vi.mocked(api.listBusinessConsoleWmsInboundOrders).mockImplementation(
    makeList([
      {
        inboundOrderId: 'IN-a',
        inboundOrderNo: 'ASN-260702',
        status: 'Open',
        createdAtUtc: iso(30),
      },
      {
        inboundOrderId: 'IN-b',
        inboundOrderNo: 'ASN-260703',
        status: 'InventoryPostingFailed',
        createdAtUtc: iso(60),
      },
      {
        inboundOrderId: 'IN-c',
        inboundOrderNo: 'ASN-260701',
        status: 'Completed',
        createdAtUtc: iso(120),
      },
      {
        inboundOrderId: 'IN-yst',
        inboundOrderNo: 'ASN-260699',
        status: 'Completed',
        createdAtUtc: isoYesterday,
      },
    ]) as never,
  )
  // 出库单：在途 1（较新）/ 已发运 1（较旧），均今日。
  vi.mocked(api.listBusinessConsoleWmsOutboundOrders).mockImplementation(
    makeList([
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
    ]) as never,
  )
  // 上架任务：**120 个 Open（跨第二页）**——末位是最老的超时单（200min），验证 open 积压不漏页；
  //   另加 1 今日完成 + 1 取消。newest-first：前 119 个 10min、第 120 个 200min（最老在尾）。
  const putawayOpen = Array.from({ length: 120 }, (_, i) => ({
    warehouseTaskId: `PT-${i}`,
    taskNo: `PT-${1000 + i}`,
    skuCode: i === 119 ? '陈年超时料' : 'BMS 主控板',
    uomCode: '件',
    fromLocationCode: 'RCV-01',
    toLocationCode: 'A1-07-02',
    sourceOrderNo: 'ASN-260701',
    plannedQuantity: 8,
    status: 'Open',
    createdAtUtc: i === 119 ? iso(200) : iso(10),
  }))
  vi.mocked(api.listBusinessConsoleWmsPutawayTasks).mockImplementation(
    makeList([
      ...putawayOpen,
      {
        warehouseTaskId: 'PT-done',
        taskNo: 'PT-9',
        skuCode: '高压连接器',
        uomCode: '盒',
        status: 'Completed',
        createdAtUtc: iso(120),
        completedAtUtc: iso(15),
      },
      { warehouseTaskId: 'PT-cancel', taskNo: 'PT-8', status: 'Cancelled', createdAtUtc: iso(40) },
    ]) as never,
  )
  // 拣货任务：超时 Open（90min，带 SO 来源）+ 今日完成。
  vi.mocked(api.listBusinessConsoleWmsPickingTasks).mockImplementation(
    makeList([
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
    ]) as never,
  )
  // 盘点：Open（50min，超时）/ 今日完成有差异 / 今日完成无差异。
  vi.mocked(api.listBusinessConsoleWmsCountExecutions).mockImplementation(
    makeList([
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
    ]) as never,
  )
  // WCS：5 在链(agv) + 1 今日完成(agv) + 1 失败堆垛机(重试3,12min) + 1 失败agv(重试1)。
  const wcsDispatched = Array.from({ length: 5 }, (_, i) => ({
    wcsTaskId: `W-d${i}`,
    adapterType: 'agv',
    externalTaskId: `WCS-8810${i}`,
    status: 'Dispatched',
    attemptCount: 0,
    dispatchedAtUtc: iso(5 + i),
  }))
  vi.mocked(api.listBusinessConsoleWmsWcsTasks).mockImplementation(
    makeList([
      ...wcsDispatched,
      {
        wcsTaskId: 'W-done',
        adapterType: 'agv',
        externalTaskId: 'WCS-88199',
        status: 'Completed',
        attemptCount: 0,
        dispatchedAtUtc: iso(30),
        completedAtUtc: iso(28),
      },
      {
        wcsTaskId: 'W-f1',
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
        wcsTaskId: 'W-f2',
        adapterType: 'agv',
        externalTaskId: 'WCS-88103',
        status: 'Failed',
        attemptCount: 1,
        failureMessage: '路径阻挡',
        dispatchedAtUtc: iso(8),
        failedAtUtc: iso(6),
      },
    ]) as never,
  )
}

describe('fetchRealWarehouseBoard', () => {
  beforeEach(stubDefaults)

  it('入库/出库文档级进度：当日窗口早停排除昨日、完成/失败按状态、行镜像单据、pct 勾稽', async () => {
    const b = await fetchRealWarehouseBoard('F01')
    expect(b.factoryId).toBe('F01')
    // 昨日单被早停排除 → 当日 3 单
    expect(b.inbound.docsTotal).toBe(3)
    expect(b.inbound.docsDone).toBe(1)
    expect(b.inbound.postFailedDocs).toBe(1)
    expect(b.inbound.postFailedDoc).toBe('ASN-260703')
    expect(b.inbound.linesDone).toBe(b.inbound.docsDone) // 行镜像单据（诚实占位）
    expect(b.inbound.linesTotal).toBe(b.inbound.docsTotal)
    expect(b.inbound.pct).toBe(33) // 1/3
    expect(b.outbound.docsTotal).toBe(2)
    expect(b.outbound.customers).toBe(0)
    expect(b.outbound.latestShipment).toBe('发运单 SO-9801')
  })

  it('open 积压跨第二页完整覆盖（120 个 Open），末页最老超时单不漏、按龄期降序', async () => {
    const b = await fetchRealWarehouseBoard('F01')
    // 120 个 Open 分两页(100+20)全部纳入
    expect(b.putaway.backlog).toBe(120)
    expect(b.putaway.rows).toHaveLength(120)
    expect(b.putaway.doneToday).toBe(1)
    // 末页那个 200min 老单排到最前（龄期降序），且被判超时——不因翻页漏掉
    expect(b.putaway.rows[0].sku).toBe('陈年超时料')
    expect(b.putaway.rows[0].overdue).toBe(true)
    expect(b.putaway.overdue).toBe(1)
    // 请求了第二页
    const calls = vi.mocked(api.listBusinessConsoleWmsPutawayTasks).mock.calls
    expect(
      calls.some(
        (c) =>
          (c[0] as { query: { status?: string; skip?: number } }).query.status === 'Open' &&
          (c[0] as { query: { skip?: number } }).query.skip === 100,
      ),
    ).toBe(true)
  })

  it('WCS 失败经 failed:true 隔离取尽（不被在链指令挤到次页而漏），失败榜按重试降序', async () => {
    const b = await fetchRealWarehouseBoard('F01')
    expect(b.wcs.failures).toHaveLength(2)
    expect(b.wcs.failures[0].retries).toBe(3) // 堆垛机在前
    expect(b.wcs.failures[0].kind).toBe('stacker')
    expect(b.wcs.failures[0].error).toBe('取货超时 · 货叉未到位')
    expect(b.wcs.failures[0].sinceMin).toBe(12) // 真实 failedAtUtc 龄期
    // 用 failed:true 查询而非首页全量
    const calls = vi.mocked(api.listBusinessConsoleWmsWcsTasks).mock.calls
    expect(calls.some((c) => (c[0] as { query: { failed?: boolean } }).query.failed === true)).toBe(
      true,
    )
    // 状态分布：无排队态、在链取尽=5、失败=2
    expect(b.wcs.counts.queued).toBe(0)
    expect(b.wcs.counts.running).toBe(5)
    expect(b.wcs.counts.failed).toBe(2)
    const agv = b.wcs.adapters.find((a) => a.kind === 'agv')!
    expect(agv.running).toBe(5)
    expect(agv.failed).toBe(1)
    expect(b.wcs.adapters.some((a) => a.kind === 'stacker')).toBe(true)
  })

  it('盘点：Open→未盘、今日完成→已盘、差异=非零差额、planned=已盘+未盘', async () => {
    const b = await fetchRealWarehouseBoard('F01')
    expect(b.count.rows).toHaveLength(1)
    expect(b.count.counted).toBe(2)
    expect(b.count.variance).toBe(1)
    expect(b.count.planned).toBe(3)
    expect(b.count.rows[0].overdue).toBe(true)
  })

  it('KPI 勾稽、超时榜跨类合并按龄期降序', async () => {
    const b = await fetchRealWarehouseBoard('F01')
    expect(b.kpis.pickBacklog).toBe(b.pick.backlog)
    expect(b.kpis.putawayBacklog).toBe(b.putaway.backlog)
    expect(b.kpis.wcsFailed).toBe(b.wcs.failures.length)
    expect(b.kpis.countVariance).toBe(b.count.variance)
    expect(b.kpis.throughputLines).toBe(b.inbound.linesDone + b.outbound.linesDone)
    // 拣货90 / 上架200 / 盘点50 → 降序，上架陈年单居首
    expect(b.overdueTop[0].ageMin).toBe(200)
    for (let i = 1; i < b.overdueTop.length; i++) {
      expect(b.overdueTop[i - 1].ageMin).toBeGreaterThanOrEqual(b.overdueTop[i].ageMin)
    }
  })

  it('空数据：全 0 不崩、无失败、无超时', async () => {
    for (const fn of [
      api.listBusinessConsoleWmsInboundOrders,
      api.listBusinessConsoleWmsOutboundOrders,
      api.listBusinessConsoleWmsPutawayTasks,
      api.listBusinessConsoleWmsPickingTasks,
      api.listBusinessConsoleWmsCountExecutions,
      api.listBusinessConsoleWmsWcsTasks,
    ]) {
      vi.mocked(fn).mockImplementation(makeList([]) as never)
    }
    const b = await fetchRealWarehouseBoard('F01')
    expect(b.inbound.docsTotal).toBe(0)
    expect(b.inbound.pct).toBe(0)
    expect(b.putaway.backlog).toBe(0)
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
    const tick = await fetchRealWarehouseOpsTick('F01')
    expect(tick.pick.backlog).toBe(b.pick.backlog)
    expect(tick.putaway.backlog).toBe(b.putaway.backlog)
    expect(tick.count.rows).toEqual(b.count.rows)
    expect(tick.wcs.failures.length).toBe(b.wcs.failures.length)
    expect(tick.overdueTop).toEqual(b.overdueTop)
  })

  it('高频 tick 只刷作业子集，不打出入库单端点', async () => {
    await fetchRealWarehouseOpsTick('F01')
    expect(api.listBusinessConsoleWmsInboundOrders).not.toHaveBeenCalled()
    expect(api.listBusinessConsoleWmsOutboundOrders).not.toHaveBeenCalled()
    expect(api.listBusinessConsoleWmsPutawayTasks).toHaveBeenCalled()
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
