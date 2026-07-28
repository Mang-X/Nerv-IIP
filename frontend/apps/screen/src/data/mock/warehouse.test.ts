import { describe, expect, it } from 'vitest'
import { buildWarehouseBoard, buildWarehouseOpsTick, OVERDUE_MIN, workFrac } from './warehouse'

/** 2026-07-06（日期 %3=0 → 过账无失败）与 07-07（有 1 单失败）两个确定性基准日。 */
const at = (h: number, m = 0) => new Date(2026, 6, 6, h, m)
const at7 = (h: number, m = 0) => new Date(2026, 6, 7, h, m)

describe('workFrac（仓库作业窗 08:00–20:00 真实时钟）', () => {
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

  it('单据量级符合设定集 §7（采购 480 张 / 销售 3200 单摊到 174 个工作日）', () => {
    const b = buildWarehouseBoard(at(14, 0))
    // 入库行 ≈30（收货 9 + 完工入库 21）；出库行 ≈109（发货 25 + 车间领料 84）
    expect(b.inbound.linesTotal).toBeGreaterThanOrEqual(28)
    expect(b.inbound.linesTotal).toBeLessThanOrEqual(40)
    expect(b.outbound.linesTotal).toBeGreaterThanOrEqual(100)
    expect(b.outbound.linesTotal).toBeLessThanOrEqual(125)
  })

  it('近 12h 流量 = 完成量逐小时差分（Σ 精确勾稽、非负、标签整点）', () => {
    const b = buildWarehouseBoard(at(14, 0))
    for (const flow of [b.inbound, b.outbound]) {
      expect(flow.hourly).toHaveLength(12)
      expect(flow.failedHourly).toHaveLength(12)
      expect(flow.hourLabels).toHaveLength(12)
      for (const v of flow.hourly) expect(v).toBeGreaterThanOrEqual(0)
      for (const v of flow.failedHourly) expect(v).toBeGreaterThanOrEqual(0)
      for (const l of flow.hourLabels) expect(l).toMatch(/^\d{2}:00$/)
      // 12h 窗覆盖今日全部已过工时（02:00–14:00）→ Σ = 当前完成行
      expect(flow.hourly.reduce((n, v) => n + v, 0)).toBe(flow.linesDone)
      expect(flow.failedHourly.reduce((n, v) => n + v, 0)).toBe(flow.failedDocs)
    }
  })

  it('mock 失败按确定性创建时段落桶，不伪造成全部刚刚发生', () => {
    const failedHourly = buildWarehouseBoard(at7(14, 0)).inbound.failedHourly
    expect(failedHourly.slice(0, -1).some((count) => count > 0)).toBe(true)
    expect(failedHourly.at(-1)).toBe(0)
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

  it('列表规模贴合本厂体量：拣货 12–17 / 上架 6–9 / 盘点 3–5', () => {
    expect(b.pick.rows.length).toBeGreaterThanOrEqual(12)
    expect(b.pick.rows.length).toBeLessThanOrEqual(17)
    expect(b.putaway.rows.length).toBeGreaterThanOrEqual(6)
    expect(b.putaway.rows.length).toBeLessThanOrEqual(9)
    expect(b.count.rows.length).toBeGreaterThanOrEqual(3)
    expect(b.count.rows.length).toBeLessThanOrEqual(5)
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

  it('超时是例外：全板 5–7 条且与超时榜一致（榜按龄期降序、TOP5 内）', () => {
    const total = b.pick.overdue + b.putaway.overdue + b.count.overdue
    expect(total).toBeGreaterThanOrEqual(5)
    expect(total).toBeLessThanOrEqual(7)
    expect(b.overdueTop.length).toBe(Math.min(5, total))
    for (const r of b.overdueTop) expect(r.ageMin).toBeGreaterThan(OVERDUE_MIN)
    for (let i = 1; i < b.overdueTop.length; i++) {
      expect(b.overdueTop[i - 1].ageMin).toBeGreaterThanOrEqual(b.overdueTop[i].ageMin)
    }
    // 大多数任务正常龄期（不把屏填满异常）
    const all = b.pick.rows.length + b.putaway.rows.length + b.count.rows.length
    expect(total / all).toBeLessThan(0.3)
  })

  it('单号与来源单据全部走设定集 §9 号段（发货出库单 / 工单 / 入库单）', () => {
    for (const r of b.pick.rows) {
      expect(r.id).toMatch(/^PK-\d{4}$/)
      expect(r.ref).toMatch(/^(OB-DO-2026-\d{5}|WO-2026-\d{5})$/)
      expect(r.from).toBeTruthy()
      expect(r.to).toBeTruthy()
    }
    expect(b.pick.rows.some((r) => r.ref?.startsWith('OB-DO-'))).toBe(true)
    expect(b.pick.rows.some((r) => r.ref?.startsWith('WO-2026-'))).toBe(true)
    for (const r of b.putaway.rows) {
      expect(r.id).toMatch(/^PT-\d{4}$/)
      // 入库单 = `IB-{采购订单}`（与 WMS WorldHistoryPhase2Spec.InboundOrderNo 同式）
      expect(r.ref).toMatch(/^IB-PO-2026-\d{4}$/)
      expect(r.from).toMatch(/^RCV-\d{2}$/)
      expect(r.to).toBeTruthy()
    }
    for (const r of b.count.rows) {
      expect(r.id).toMatch(/^CC-\d{2}$/)
      expect(r.to).toBeUndefined()
      expect(r.ref).toBeUndefined()
    }
  })

  it('物料是减振器物料（L0 §4 SKU），不是整车厂的电芯/线束', () => {
    const rows = [...b.pick.rows, ...b.putaway.rows, ...b.count.rows]
    const forbidden = ['电芯', 'PACK', '线束', 'BMS', '车门', '座椅', '保险杠']
    for (const r of rows) {
      for (const word of forbidden) expect(r.sku).not.toContain(word)
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

  it('只列本厂真实存在的 4 类自动化设备；每适配器 total = queued+running+completed+failed', () => {
    const b = buildWarehouseBoard(at(14, 0))
    expect(b.wcs.adapters).toHaveLength(4)
    expect(b.wcs.adapters.map((a) => a.kind).sort()).toEqual(
      ['agv', 'conveyor', 'hoist', 'stacker'].sort(),
    )
    for (const a of b.wcs.adapters) {
      expect(a.total).toBe(a.queued + a.running + a.completed + a.failed)
    }
    for (const key of ['queued', 'running', 'completed'] as const) {
      expect(b.wcs.counts[key]).toBe(b.wcs.adapters.reduce((n, a) => n + a[key], 0))
    }
  })

  it('失败榜常驻 2 条（午后含提升机第 3 条）、指令号走 WCS-{仓储任务号} 形', () => {
    const noon = buildWarehouseBoard(at(14, 0))
    expect(noon.wcs.failures).toHaveLength(3)
    const morning = buildWarehouseBoard(at(9, 30))
    expect(morning.wcs.failures).toHaveLength(2)
    for (const x of noon.wcs.failures) {
      expect(x.cmd).toMatch(/^WCS-WT-[A-Z]{2}-[A-Z]{2}-2026-\d{4}-\d{2}$/)
      expect(x.retries).toBeGreaterThanOrEqual(1)
      expect(x.sinceMin).toBeGreaterThan(0)
      expect(x.firstAt).toMatch(/^\d{2}:\d{2}$/)
      expect(x.error).toBeTruthy()
    }
  })
})

describe('过账失败空态（0–1 单，按日期轮换）', () => {
  it('7/6 无失败（空态语义）；7/7 恰 1 单且带入库单号', () => {
    const clean = buildWarehouseBoard(at(14, 0))
    expect(clean.inbound.postFailedDocs).toBe(0)
    expect(clean.inbound.postFailedDoc).toBeUndefined()
    const dirty = buildWarehouseBoard(at7(14, 0))
    expect(dirty.inbound.postFailedDocs).toBe(1)
    expect(dirty.inbound.postFailedDoc).toMatch(/^IB-PO-2026-\d{4}$/)
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

  it('跨日差异：种子含日期，不同日画像不同', () => {
    const d6 = buildWarehouseBoard(at(14, 0))
    const d7 = buildWarehouseBoard(at7(14, 0))
    const sig = (x: typeof d6) =>
      [x.inbound.linesTotal, x.outbound.linesTotal, x.pick.rows.length, x.pick.rows[0]?.id].join(
        '|',
      )
    expect(sig(d7)).not.toBe(sig(d6))
  })
})
