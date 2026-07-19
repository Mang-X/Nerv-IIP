// 真实取数适配勾稽测试（MAN-467，含两轮 review 修正）：打 business-console WMS facade 契约桩，
// 验证 facade 响应 → 大屏 WarehouseBoard/WarehouseOpsTick 契约的映射，重点覆盖：
//   - 完整覆盖：open 积压跨第二页不漏；当日到货窗口早停排除昨日单。
//   - review① WCS 当前失败用 `status:'Failed'`（重试后回 Dispatched 且 FailedAtUtc 保留的任务不算失败、
//     不与在链双计）。
//   - review② 今日完成走 `status:'Completed'` + completedAtUtc 落今日（捕获昨日创建今日完成）；
//     超回溯窗（>7d 前创建今日才完工）的异常长尾按设计排除。
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

// 固定系统时钟；单据时刻按「距 now N 分钟」；昨日/多日前项相对本地零点构造（任何 TZ 都属过去）。
const NOW = new Date('2026-07-09T12:00:00Z').getTime()
const iso = (minAgo: number) => new Date(NOW - minAgo * 60_000).toISOString()
const localMidnight = (() => {
  const d = new Date(NOW)
  d.setHours(0, 0, 0, 0)
  return d.getTime()
})()
const isoYesterday = new Date(localMidnight - 60_000).toISOString() // 本地零点前 1min → 昨天
const isoDaysAgo = (days: number) => new Date(localMidnight - days * 24 * 3_600_000).toISOString()

/** 模拟后端 list：按 query.status 过滤，按 skip/take 分页（rows 须按后端排序键 newest-first）。 */
function makeList<T extends { status?: string }>(rows: T[]) {
  return (opts: { query: Record<string, unknown> }) => {
    const q = opts.query
    let r = rows
    if (typeof q.status === 'string') r = r.filter((x) => x.status === q.status)
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
  // 上架：120 Open（跨第二页，末位 200min 最老超时）+ 今日完成 3 类：今日创建今日完成、
  //   **昨日创建今日完成**（review② 应计入）、**10天前创建今日完成**（超 7d 回溯窗，应排除）+ 取消。
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
      // status:'Completed' 按 createdAtUtc 降序：今日 → 昨日 → 10天前。
      {
        warehouseTaskId: 'PT-done',
        taskNo: 'PT-9',
        skuCode: '高压连接器',
        uomCode: '盒',
        status: 'Completed',
        createdAtUtc: iso(120),
        completedAtUtc: iso(15),
      },
      {
        warehouseTaskId: 'PT-cross',
        taskNo: 'PT-8',
        skuCode: '隔夜料',
        uomCode: '件',
        status: 'Completed',
        createdAtUtc: isoYesterday,
        completedAtUtc: iso(30),
      },
      {
        warehouseTaskId: 'PT-stale',
        taskNo: 'PT-7',
        skuCode: '陈年完工',
        uomCode: '件',
        status: 'Completed',
        createdAtUtc: isoDaysAgo(10),
        completedAtUtc: iso(40),
      },
      { warehouseTaskId: 'PT-cancel', taskNo: 'PT-6', status: 'Cancelled', createdAtUtc: iso(40) },
    ]) as never,
  )
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
  // 盘点：Open（50min 超时）+ 今日完成有差异 + **昨日创建今日完成**（应计入 counted）。
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
        createdAtUtc: isoYesterday,
        completedAtUtc: iso(25),
      },
    ]) as never,
  )
  // WCS：当前失败 2（堆垛机重试3、agv重试1）+ 在链 5 agv + **1 已重试回 Dispatched（FailedAtUtc 保留）**
  //   + 今日完成 1 agv。review① 校验重试任务只算在链、不进失败榜。
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
      // 已重试：状态回到 Dispatched，但 FailedAtUtc 仍有值——只应算在链，不进失败榜。
      {
        wcsTaskId: 'W-retried',
        adapterType: 'agv',
        externalTaskId: 'WCS-88150',
        status: 'Dispatched',
        attemptCount: 2,
        failedAtUtc: iso(50),
        dispatchedAtUtc: iso(3),
      },
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

  it('入库/出库当日到货 cohort：早停排除昨日、完成/失败按状态、行镜像单据、pct 勾稽', async () => {
    const b = await fetchRealWarehouseBoard('F01')
    expect(b.inbound.docsTotal).toBe(3) // 昨日单被早停排除
    expect(b.inbound.docsDone).toBe(1)
    expect(b.inbound.postFailedDocs).toBe(1)
    expect(b.inbound.postFailedDoc).toBe('ASN-260703')
    expect(b.inbound.failedDocs).toBe(1)
    expect(b.inbound.failedHourly.reduce((n, v) => n + v, 0)).toBe(1)
    expect(b.inbound.linesDone).toBe(b.inbound.docsDone)
    expect(b.inbound.pct).toBe(33)
    expect(b.outbound.failedDocs).toBe(0)
    expect(b.outbound.failedHourly.reduce((n, v) => n + v, 0)).toBe(0)
    expect(b.outbound.customers).toBe(0)
    expect(b.outbound.latestShipment).toBe('发运单 SO-9801')
  })

  it('近 12h 失败柱只统计窗口内单据，同时保留当日过账失败总数', async () => {
    vi.mocked(api.listBusinessConsoleWmsInboundOrders).mockImplementation(
      makeList([
        {
          inboundOrderId: 'IN-recent-failed',
          inboundOrderNo: 'ASN-recent',
          status: 'InventoryPostingFailed',
          createdAtUtc: iso(60),
        },
        {
          inboundOrderId: 'IN-old-failed',
          inboundOrderNo: 'ASN-old',
          status: 'InventoryPostingFailed',
          createdAtUtc: iso(13 * 60),
        },
      ]) as never,
    )

    const b = await fetchRealWarehouseBoard('F01')

    expect(b.inbound.postFailedDocs).toBe(2)
    expect(b.inbound.failedDocs).toBe(1)
    expect(b.inbound.failedHourly.reduce((sum, count) => sum + count, 0)).toBe(b.inbound.failedDocs)
  })

  it('上午的近 12h 窗口包含昨晚失败，但当日进度与过账失败仍只统计今天', async () => {
    const morning = new Date(NOW)
    morning.setHours(6, 0, 0, 0)
    vi.setSystemTime(morning)
    const oneHourAgo = new Date(morning.getTime() - 60 * 60_000).toISOString()
    const previousNight = new Date(morning)
    previousNight.setDate(previousNight.getDate() - 1)
    previousNight.setHours(23, 0, 0, 0)

    vi.mocked(api.listBusinessConsoleWmsInboundOrders).mockImplementation(
      makeList([
        {
          inboundOrderId: 'IN-today-failed',
          inboundOrderNo: 'ASN-today',
          status: 'InventoryPostingFailed',
          createdAtUtc: oneHourAgo,
        },
        {
          inboundOrderId: 'IN-last-night-failed',
          inboundOrderNo: 'ASN-last-night',
          status: 'InventoryPostingFailed',
          createdAtUtc: previousNight.toISOString(),
        },
      ]) as never,
    )
    vi.mocked(api.listBusinessConsoleWmsOutboundOrders).mockImplementation(
      makeList([
        {
          outboundOrderId: 'OUT-today-failed',
          outboundOrderNo: 'SO-today',
          status: 'InventoryPostingFailed',
          createdAtUtc: oneHourAgo,
        },
        {
          outboundOrderId: 'OUT-last-night-failed',
          outboundOrderNo: 'SO-last-night',
          status: 'InventoryPostingFailed',
          createdAtUtc: previousNight.toISOString(),
        },
      ]) as never,
    )

    const b = await fetchRealWarehouseBoard('F01')

    expect(b.inbound.docsTotal).toBe(1)
    expect(b.inbound.postFailedDocs).toBe(1)
    expect(b.inbound.failedDocs).toBe(2)
    expect(b.inbound.failedHourly.reduce((sum, count) => sum + count, 0)).toBe(2)
    expect(b.outbound.docsTotal).toBe(1)
    expect(b.outbound.failedDocs).toBe(2)
    expect(b.outbound.failedHourly.reduce((sum, count) => sum + count, 0)).toBe(2)
  })

  it('open 积压跨第二页完整覆盖（120 Open），末页最老超时单不漏、按龄期降序', async () => {
    const b = await fetchRealWarehouseBoard('F01')
    expect(b.putaway.backlog).toBe(120)
    expect(b.putaway.rows).toHaveLength(120)
    expect(b.putaway.rows[0].sku).toBe('陈年超时料')
    expect(b.putaway.rows[0].overdue).toBe(true)
    expect(b.putaway.overdue).toBe(1)
    const calls = vi.mocked(api.listBusinessConsoleWmsPutawayTasks).mock.calls
    expect(
      calls.some(
        (c) =>
          (c[0] as { query: { status?: string; skip?: number } }).query.status === 'Open' &&
          (c[0] as { query: { skip?: number } }).query.skip === 100,
      ),
    ).toBe(true)
  })

  it('review②：今日完成走 status:Completed + completedAtUtc，含昨日创建今日完成；超 7d 回溯窗排除', async () => {
    const b = await fetchRealWarehouseBoard('F01')
    // PT-done(今日创建今日完成) + PT-cross(昨日创建今日完成) 计入；PT-stale(10天前创建) 超窗排除。
    expect(b.putaway.doneToday).toBe(2)
    // 用 status:'Completed' 查询（非 created 窗口早停近似）。
    const calls = vi.mocked(api.listBusinessConsoleWmsPutawayTasks).mock.calls
    expect(
      calls.some((c) => (c[0] as { query: { status?: string } }).query.status === 'Completed'),
    ).toBe(true)
    // 盘点已盘同理含昨日创建今日完成。
    expect(b.count.counted).toBe(2)
    expect(b.count.variance).toBe(1)
    expect(b.count.planned).toBe(3) // 2 已盘 + 1 未盘
  })

  it('review①：WCS 当前失败用 status:Failed，已重试回 Dispatched 的旧失败不进失败榜、不与在链双计', async () => {
    const b = await fetchRealWarehouseBoard('F01')
    expect(b.wcs.failures).toHaveLength(2) // 仅 W-f1 / W-f2，不含 W-retried
    expect(b.wcs.failures.some((f) => f.cmd === 'WCS-88150')).toBe(false)
    expect(b.wcs.failures[0].retries).toBe(3)
    expect(b.wcs.failures[0].kind).toBe('stacker')
    expect(b.wcs.failures[0].sinceMin).toBe(12)
    // 已重试任务算在链（Dispatched=5+1=6），不算失败
    expect(b.wcs.counts.running).toBe(6)
    expect(b.wcs.counts.failed).toBe(2)
    expect(b.wcs.counts.queued).toBe(0)
    expect(b.wcs.counts.completed).toBe(1)
    // 用 status:'Failed' 查询（非 failed:true）
    const calls = vi.mocked(api.listBusinessConsoleWmsWcsTasks).mock.calls
    expect(
      calls.some((c) => (c[0] as { query: { status?: string } }).query.status === 'Failed'),
    ).toBe(true)
    expect(
      calls.every((c) => (c[0] as { query: { failed?: boolean } }).query.failed !== true),
    ).toBe(true)
  })

  it('KPI 勾稽、超时榜跨类合并按龄期降序', async () => {
    const b = await fetchRealWarehouseBoard('F01')
    expect(b.kpis.pickBacklog).toBe(b.pick.backlog)
    expect(b.kpis.putawayBacklog).toBe(b.putaway.backlog)
    expect(b.kpis.wcsFailed).toBe(b.wcs.failures.length)
    expect(b.kpis.countVariance).toBe(b.count.variance)
    expect(b.kpis.throughputLines).toBe(b.inbound.linesDone + b.outbound.linesDone)
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
    expect(b.putaway.backlog).toBe(0)
    expect(b.putaway.doneToday).toBe(0)
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
    expect(tick.putaway.doneToday).toBe(b.putaway.doneToday)
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
