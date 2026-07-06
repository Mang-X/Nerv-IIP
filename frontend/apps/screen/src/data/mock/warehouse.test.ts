import { describe, expect, it } from 'vitest'
import { buildWarehouseBoard, buildWarehouseOpsTick, OVERDUE_MIN, workFrac } from './warehouse'

/** 2026-07-06（日期 %3=0 → 过账无失败）与 07-07（有 1 单失败）两个确定性基准日。 */
const at = (h: number, m = 0) => new Date(2026, 6, 6, h, m)
const at7 = (h: number, m = 0) => new Date(2026, 6, 7, h, m)

describe('workFrac（工作窗 08:00–20:00 真实时钟）', () => {
  it('开窗前 0、窗内线性、20:00 封板', () => {
    expect(workFrac(at(7, 59))).toBe(0)
    expect(workFrac(at(8, 0))).toBe(0)
    expect(workFrac(at(14, 0))).toBe(0.5)
    expect(workFrac(at(20, 0))).toBe(1)
    expect(workFrac(at(23, 30))).toBe(1)
  })
})

describe('出入库进度（行/单口径勾稽 + 单调 + 流量差分）', () => {
  it('午后基准：分子 ≤ 分母、pct 口径、KPI 与明细一致、吞吐 = 入出行合计', () => {
    const b = buildWarehouseBoard(at(14, 0))
    for (const flow of [b.inbound, b.outbound]) {
      expect(flow.linesDone).toBeGreaterThan(0)
      expect(flow.linesDone).toBeLessThanOrEqual(flow.linesTotal)
      expect(flow.docsDone).toBeLessThanOrEqual(flow.docsTotal)
      expect(flow.pct).toBe(Math.round((flow.linesDone / flow.linesTotal) * 100))
    }
    expect(b.kpis.inboundPct).toBe(b.inbound.pct)
    expect(b.kpis.outboundPct).toBe(b.outbound.pct)
    expect(b.kpis.throughputLines).toBe(b.inbound.linesDone + b.outbound.linesDone)
    // 收货偏上午 / 拣配偏下午：14:00 时入库进度应领先出库
    expect(b.inbound.pct).toBeGreaterThan(b.outbound.pct)
  })

  it('近 12h 流量 = 完成量逐小时差分（Σ 精确勾稽、非负、标签整点）', () => {
    const b = buildWarehouseBoard(at(14, 0))
    for (const flow of [b.inbound, b.outbound]) {
      expect(flow.hourly).toHaveLength(12)
      expect(flow.hourLabels).toHaveLength(12)
      for (const v of flow.hourly) expect(v).toBeGreaterThanOrEqual(0)
      for (const l of flow.hourLabels) expect(l).toMatch(/^\d{2}:00$/)
      // 12h 窗覆盖今日全部已过工时（02:00–14:00）→ Σ = 当前完成行
      expect(flow.hourly.reduce((n, v) => n + v, 0)).toBe(flow.linesDone)
    }
  })

  it('凌晨未开窗：进度归零不造假；晚间封板后冻结且完成率高', () => {
    const dawn = buildWarehouseBoard(at(4, 0))
    expect(dawn.inbound.linesDone).toBe(0)
    expect(dawn.inbound.docsDone).toBe(0)
    expect(dawn.inbound.pct).toBe(0)
    expect(dawn.outbound.pct).toBe(0)
    const night = buildWarehouseBoard(at(22, 0))
    const closed = buildWarehouseBoard(at(20, 30))
    expect(night.inbound.linesDone).toBe(closed.inbound.linesDone)
    expect(night.outbound.linesDone).toBe(closed.outbound.linesDone)
    expect(night.inbound.pct).toBeGreaterThanOrEqual(85)
    expect(night.outbound.pct).toBeGreaterThanOrEqual(85)
  })

  it('当日进度单调不减（真实时钟推进）', () => {
    const a = buildWarehouseBoard(at(10, 0))
    const b = buildWarehouseBoard(at(14, 0))
    const c = buildWarehouseBoard(at(18, 0))
    expect(b.inbound.linesDone).toBeGreaterThanOrEqual(a.inbound.linesDone)
    expect(c.inbound.linesDone).toBeGreaterThanOrEqual(b.inbound.linesDone)
    expect(c.outbound.linesDone).toBeGreaterThanOrEqual(b.outbound.linesDone)
  })
})

describe('作业任务（规模 + 守恒 + 超时是例外）', () => {
  const b = buildWarehouseBoard(at(14, 0))

  it('列表规模真实：拣货 15–25 / 上架 8–15 / 盘点 4–8', () => {
    expect(b.pick.rows.length).toBeGreaterThanOrEqual(15)
    expect(b.pick.rows.length).toBeLessThanOrEqual(25)
    expect(b.putaway.rows.length).toBeGreaterThanOrEqual(8)
    expect(b.putaway.rows.length).toBeLessThanOrEqual(15)
    expect(b.count.rows.length).toBeGreaterThanOrEqual(4)
    expect(b.count.rows.length).toBeLessThanOrEqual(8)
  })

  it('任务守恒：今日创建 = Open 积压 + 今日完成；KPI 与分组一致', () => {
    for (const g of [b.pick, b.putaway]) {
      expect(g.backlog).toBe(g.rows.length)
      expect(g.createdToday).toBe(g.backlog + g.doneToday)
      expect(g.overdue).toBe(g.rows.filter((r) => r.overdue).length)
    }
    expect(b.kpis.pickBacklog).toBe(b.pick.backlog)
    expect(b.kpis.putawayBacklog).toBe(b.putaway.backlog)
    // 拣货完成 ⇔ 出库已拣配行（同一事实的两个视图）
    expect(b.pick.doneToday).toBe(b.outbound.linesDone)
  })

  it('龄期真实：createdAt=HH:mm、overdue ⇔ 龄期>45、行按龄期降序', () => {
    for (const g of [b.pick, b.putaway, b.count]) {
      for (const r of g.rows) {
        expect(r.createdAt).toMatch(/^\d{2}:\d{2}$/)
        expect(r.qty).toBeGreaterThan(0)
        expect(r.overdue).toBe(r.ageMin > OVERDUE_MIN)
      }
      for (let i = 1; i < g.rows.length; i++) {
        expect(g.rows[i - 1].ageMin).toBeGreaterThanOrEqual(g.rows[i].ageMin)
      }
    }
  })

  it('超时是例外：全板 2–4 条且与超时榜一致（榜按龄期降序、TOP5 内）', () => {
    const total = b.pick.overdue + b.putaway.overdue + b.count.overdue
    expect(total).toBeGreaterThanOrEqual(2)
    expect(total).toBeLessThanOrEqual(4)
    expect(b.overdueTop.length).toBe(Math.min(5, total))
    for (const r of b.overdueTop) expect(r.ageMin).toBeGreaterThan(OVERDUE_MIN)
    for (let i = 1; i < b.overdueTop.length; i++) {
      expect(b.overdueTop[i - 1].ageMin).toBeGreaterThanOrEqual(b.overdueTop[i].ageMin)
    }
    // 大多数任务正常龄期（不把屏填满异常）
    const all = b.pick.rows.length + b.putaway.rows.length + b.count.rows.length
    expect(total / all).toBeLessThan(0.3)
  })

  it('真实业务编号与流向：PK/PT/CC 单号、SO/WO 来源（工单钩子 194x–196x）、ASN 来源、库位流向', () => {
    for (const r of b.pick.rows) {
      expect(r.id).toMatch(/^PK-\d{4}$/)
      expect(r.ref).toMatch(/^(SO-98\d{2}|WO-19[4-6]\d)$/)
      expect(r.from).toBeTruthy()
      expect(r.to).toBeTruthy()
    }
    expect(b.pick.rows.some((r) => r.ref?.startsWith('SO-'))).toBe(true)
    expect(b.pick.rows.some((r) => r.ref?.startsWith('WO-'))).toBe(true)
    for (const r of b.putaway.rows) {
      expect(r.id).toMatch(/^PT-\d{4}$/)
      expect(r.ref).toMatch(/^ASN-2607\d{2}$/)
      expect(r.from).toMatch(/^RCV-\d{2}$/)
      expect(r.to).toBeTruthy()
    }
    for (const r of b.count.rows) {
      expect(r.id).toMatch(/^CC-\d{2}$/)
      expect(r.to).toBeUndefined()
      expect(r.ref).toBeUndefined()
    }
  })
})

describe('盘点（库位数口径）', () => {
  it('planned = 已盘 + 未盘任务；差异 ≤ 已盘；KPI 一致', () => {
    const b = buildWarehouseBoard(at(14, 0))
    expect(b.count.planned).toBe(b.count.counted + b.count.rows.length)
    expect(b.count.counted).toBeLessThanOrEqual(b.count.planned)
    expect(b.count.variance).toBeGreaterThanOrEqual(0)
    expect(b.count.variance).toBeLessThanOrEqual(b.count.counted)
    expect(b.kpis.countVariance).toBe(b.count.variance)
  })
})

describe('WCS（失败榜为事实源，聚合逐格勾稽）', () => {
  it('失败数三方一致：KPI = 失败榜行数 = Σ适配器失败 = 状态分布失败', () => {
    const b = buildWarehouseBoard(at(14, 0))
    const sumFailed = b.wcs.adapters.reduce((n, a) => n + a.failed, 0)
    expect(b.kpis.wcsFailed).toBe(b.wcs.failures.length)
    expect(b.kpis.wcsFailed).toBe(sumFailed)
    expect(b.kpis.wcsFailed).toBe(b.wcs.counts.failed)
  })

  it('每适配器 total = queued+running+completed+failed；状态分布 = 各列合计', () => {
    const b = buildWarehouseBoard(at(14, 0))
    expect(b.wcs.adapters).toHaveLength(4)
    expect(new Set(b.wcs.adapters.map((a) => a.kind)).size).toBe(4)
    for (const a of b.wcs.adapters) {
      expect(a.total).toBe(a.queued + a.running + a.completed + a.failed)
    }
    for (const key of ['queued', 'running', 'completed'] as const) {
      expect(b.wcs.counts[key]).toBe(b.wcs.adapters.reduce((n, a) => n + a[key], 0))
    }
  })

  it('失败榜 1–3 条（午后含提升机第 3 条）、重试次数与时刻齐备', () => {
    const noon = buildWarehouseBoard(at(14, 0))
    expect(noon.wcs.failures).toHaveLength(3)
    const morning = buildWarehouseBoard(at(9, 30))
    expect(morning.wcs.failures).toHaveLength(2)
    for (const x of noon.wcs.failures) {
      expect(x.cmd).toMatch(/^WCS-\d{5}$/)
      expect(x.retries).toBeGreaterThanOrEqual(1)
      expect(x.sinceMin).toBeGreaterThan(0)
      expect(x.firstAt).toMatch(/^\d{2}:\d{2}$/)
      expect(x.error).toBeTruthy()
    }
  })
})

describe('过账失败空态（0–1 单，按日期轮换）', () => {
  it('7/6 无失败（空态语义）；7/7 恰 1 单且带单号', () => {
    const clean = buildWarehouseBoard(at(14, 0))
    expect(clean.inbound.postFailedDocs).toBe(0)
    expect(clean.inbound.postFailedDoc).toBeUndefined()
    const dirty = buildWarehouseBoard(at7(14, 0))
    expect(dirty.inbound.postFailedDocs).toBe(1)
    expect(dirty.inbound.postFailedDoc).toMatch(/^ASN-\d{6}$/)
  })
})

describe('确定性与多频一致（3s tick 与 5s 主数据同源）', () => {
  it('同刻两次调用任务/失败完全一致（轮询不跳变）；tick 子集与主板一致', () => {
    const a = buildWarehouseBoard(at(14, 0))
    const b = buildWarehouseBoard(at(14, 0))
    expect(a.pick.rows).toEqual(b.pick.rows)
    expect(a.putaway.rows).toEqual(b.putaway.rows)
    expect(a.wcs.failures).toEqual(b.wcs.failures)
    expect(a.kpis.pickBacklog).toBe(b.kpis.pickBacklog)
    const tick = buildWarehouseOpsTick(at(14, 0))
    expect(tick.pick.backlog).toBe(a.kpis.pickBacklog)
    expect(tick.putaway.backlog).toBe(a.kpis.putawayBacklog)
    expect(tick.wcs.failures.length).toBe(a.kpis.wcsFailed)
    expect(tick.overdueTop).toEqual(a.overdueTop)
  })

  it('跨厂差异：F02 与 F01 画像不同（种子含工厂）', () => {
    const f1 = buildWarehouseBoard(at(14, 0), 'F01')
    const f2 = buildWarehouseBoard(at(14, 0), 'F02')
    const sig = (x: typeof f1) =>
      [x.inbound.linesTotal, x.outbound.linesTotal, x.pick.rows.length, x.pick.rows[0]?.id].join('|')
    expect(sig(f2)).not.toBe(sig(f1))
  })
})
