import { describe, expect, it } from 'vitest'
import { DEFAULT_FACTORY_ID } from './masterdata'
import { buildQualityBoard, DEFECT_RED_LINE_PCT, NCR_SLA_HOURS, woOf } from './quality'

const round1 = (n: number) => Math.round(n * 10) / 10
const round2 = (n: number) => Math.round(n * 100) / 100

describe('buildQualityBoard（勾稽自洽）', () => {
  const b = buildQualityBoard()

  it('批次合格率 = Σ合格批/Σ判定批；不良率（件口径）= Σ不良件/Σ检验件，两口径同源互补', () => {
    const done = b.layers.reduce((n, l) => n + l.lotsDone, 0)
    const passed = b.layers.reduce((n, l) => n + l.lotsPassed, 0)
    expect(b.kpis.batchTotal).toBe(done)
    expect(b.kpis.batchPassed).toBe(passed)
    expect(b.kpis.batchPassRate).toBe(round1((passed / done) * 100))
    const insp = b.layers.reduce((n, l) => n + l.pieceInspected, 0)
    const def = b.layers.reduce((n, l) => n + l.pieceDefects, 0)
    expect(b.kpis.defectRatePct).toBe(round2((def / insp) * 100))
    for (const l of b.layers) {
      expect(l.passRate).toBe(round1((l.lotsPassed / l.lotsDone) * 100))
      expect(l.pieceDefectPct).toBe(round2((l.pieceDefects / l.pieceInspected) * 100))
      expect(l.lotsPassed).toBeLessThanOrEqual(l.lotsDone)
      expect(l.lotsDone).toBeLessThanOrEqual(l.lotsDue + l.carryOver)
    }
  })

  it('检验规模符合设定集 §7（29 周约 7000 批 → ≈40 批/日）', () => {
    expect(b.kpis.batchTotal).toBeGreaterThanOrEqual(30)
    expect(b.kpis.batchTotal).toBeLessThanOrEqual(50)
  })

  it('检验积压 = 应检−已判+结转，KPI 与三层同源；最老龄期取层最大', () => {
    for (const l of b.layers) expect(l.backlog).toBe(l.lotsDue - l.lotsDone + l.carryOver)
    expect(b.kpis.inspectionBacklog).toBe(b.layers.reduce((n, l) => n + l.backlog, 0))
    expect(b.kpis.backlogOldestHours).toBe(Math.max(...b.layers.map((l) => l.oldestHours)))
  })

  it('NCR：超期数 = 龄期 > SLA 的行数且与 KPI 一致；MRB/条件放行与行状态勾稽；号段为 NCR-2026-####', () => {
    expect(b.kpis.openNcr).toBe(b.ncrs.length)
    const overdue = b.ncrs.filter((r) => r.ageHours > NCR_SLA_HOURS)
    expect(b.kpis.overdueNcr).toBe(overdue.length)
    for (const r of b.ncrs) {
      expect(r.overdue).toBe(r.ageHours > NCR_SLA_HOURS)
      expect(r.code).toMatch(/^NCR-2026-\d{4}$/)
      // 来料 NCR 的来源单据引采购订单段（设定集 §9）
      if (r.sourceType === 'supplier') expect(r.sourceDoc).toMatch(/^PO-2026-\d{4}$/)
      else expect(r.sourceDoc).toMatch(/^WO-2026-\d{5}$/)
    }
    expect(b.kpis.mrbPending).toBe(b.ncrs.filter((r) => r.status === 'review').length)
    const concession = b.ncrs.filter((r) => r.disposition === '让步接收').length
    expect(b.kpis.conditionalRelease).toBeGreaterThanOrEqual(concession)
    // 处置方式只出现在「处置中」；龄期降序（最痛置顶）
    for (const r of b.ncrs) {
      if (r.status !== 'disposing') expect(r.disposition).toBeUndefined()
      else expect(r.disposition).toBeTruthy()
    }
    for (let i = 1; i < b.ncrs.length; i++)
      expect(b.ncrs[i - 1].ageHours).toBeGreaterThanOrEqual(b.ncrs[i].ageHours)
  })

  it('帕累托：TOP5 + 其他按窗口长尾聚合，数量守恒且占比按同一分母重算', () => {
    expect(b.pareto).toHaveLength(6)
    expect(b.pareto.at(-1)?.defect).toBe('其他')
    expect(b.pareto.at(-1)?.lineName).toBe('其余缺陷')
    for (let i = 1; i < b.pareto.length - 1; i++) {
      expect(b.pareto[i - 1].count).toBeGreaterThan(b.pareto[i].count)
      expect(b.pareto[i - 1].pct).toBeGreaterThanOrEqual(b.pareto[i].pct)
    }
    const sumCount = b.pareto.reduce((n, p) => n + p.count, 0)
    expect(b.paretoTotal).toBe(sumCount)
    expect(b.pareto.reduce((n, p) => n + p.pct, 0)).toBeCloseTo(100, 0)
    for (const p of b.pareto) expect(p.pct).toBe(round1((p.count / b.paretoTotal) * 100))
  })

  it('趋势（过程检口径）：12h 尾段越过程管控限（今晨事故）、此前在限内；30 天末点 = 当日 KPI、周日检验量低谷', () => {
    const ipqcLimit = b.layers.find((l) => l.key === 'ipqc')!.limitPct
    expect(b.trend12h.ratePct).toHaveLength(12)
    expect(b.trend12h.labels).toHaveLength(12)
    for (const l of b.trend12h.labels) expect(l).toMatch(/^\d{2}:00$/)
    expect(b.trend12h.ratePct.at(-1)!).toBeGreaterThan(ipqcLimit)
    for (const v of b.trend12h.ratePct.slice(0, 9)) expect(v).toBeLessThan(ipqcLimit)
    // 12h 分层结构与 30 天一致：三层等长；全厂 = 各层按当日检验件数逐点加权（勾稽）
    expect(b.trend12h.iqc).toHaveLength(12)
    expect(b.trend12h.fqc).toHaveLength(12)
    expect(b.trend12h.factory).toHaveLength(12)
    const wI = b.layers.find((l) => l.key === 'iqc')!.pieceInspected
    const wP = b.layers.find((l) => l.key === 'ipqc')!.pieceInspected
    const wF = b.layers.find((l) => l.key === 'fqc')!.pieceInspected
    for (let i = 0; i < 12; i++) {
      const exp =
        Math.round(
          ((b.trend12h.iqc[i] * wI + b.trend12h.ratePct[i] * wP + b.trend12h.fqc[i] * wF) /
            (wI + wP + wF)) *
            100,
        ) / 100
      expect(b.trend12h.factory[i]).toBe(exp)
    }
    for (const v of b.trend12h.iqc)
      expect(v).toBeLessThan(b.layers.find((l) => l.key === 'iqc')!.limitPct)
    for (const v of b.trend12h.fqc)
      expect(v).toBeLessThan(b.layers.find((l) => l.key === 'fqc')!.limitPct)

    expect(b.trend30.ratePct).toHaveLength(30)
    expect(b.trend30.lots).toHaveLength(30)
    expect(b.trend30.labels).toHaveLength(30)
    for (const l of b.trend30.labels) expect(l).toMatch(/^\d{1,2}\/\d{1,2}$/)
    // 今日收盘点与 KPI 严格勾稽
    expect(b.trend30.ratePct.at(-1)).toBe(b.kpis.defectRatePct)
    expect(b.trend30.lots.at(-1)).toBe(b.kpis.batchTotal)
    // 周日低谷：设定集 §1 周日停产 → 报检批次骤降
    const peak = Math.max(...b.trend30.lots)
    expect(b.trend30.lots.some((v) => v < peak * 0.5)).toBe(true)
    for (const v of b.trend30.ratePct) {
      expect(v).toBeGreaterThan(0)
      expect(v).toBeLessThan(3)
    }
  })
})

describe('buildQualityBoard（与产线屏同一个故事：活塞杆一线 DEV-CNC-03 振动超限）', () => {
  const b = buildQualityBoard()

  it('NCR 挂活塞杆一线当前工单（与 mock/line currentWo 同源推导）+ 产品取 L0 SKU 名', () => {
    expect(woOf('LINE-WB-ROD-01')).toBe('WO-2026-03421')
    const rodRows = b.ncrs.filter((r) => r.lineId === 'LINE-WB-ROD-01')
    expect(rodRows.length).toBeGreaterThanOrEqual(1)
    for (const r of rodRows) {
      expect(r.sourceDoc).toBe('WO-2026-03421')
      expect(r.product).toBe('活塞杆 φ22×420')
    }
    // 龄期最长（置顶）且超期红标的正是活塞杆一线那张单
    expect(b.ncrs[0].lineId).toBe('LINE-WB-ROD-01')
    expect(b.ncrs[0].overdue).toBe(true)
    // 今晨报警的回声：还有一张低龄期活塞杆 NCR 待评审
    expect(rodRows.some((r) => r.status === 'review' && r.ageHours <= 6)).toBe(true)
  })

  it('帕累托 TOP1/TOP2 为活塞杆缺陷且来源 = 活塞杆一线', () => {
    expect(b.pareto[0].defect).toBe('活塞杆表面振纹')
    expect(b.pareto[0].lineName).toBe('活塞杆一线')
    expect(b.pareto[1].lineName).toBe('活塞杆一线')
  })

  it('来料 NCR 的供应商取 L0 §6 十家供应商', () => {
    const suppliers = b.ncrs.filter((r) => r.sourceType === 'supplier').map((r) => r.source)
    expect(suppliers.length).toBeGreaterThanOrEqual(2)
    for (const s of suppliers) expect(s).toMatch(/有限公司$/)
  })

  it('分层 30 天件不良率：三层各 30 点、末点 = 当日件不良率勾稽、过程检尾部酝酿抬升', () => {
    for (const l of b.layers) {
      expect(l.trend30).toHaveLength(30)
      expect(l.trend30.at(-1)).toBe(l.pieceDefectPct)
      for (const v of l.trend30) expect(v).toBeGreaterThan(0)
    }
    const ipqc = b.layers.find((l) => l.key === 'ipqc')!
    const head = ipqc.trend30.slice(0, 20)
    const tail = ipqc.trend30.slice(-4)
    const avg = (a: number[]) => a.reduce((n, v) => n + v, 0) / a.length
    expect(avg(tail)).toBeGreaterThan(avg(head) + 0.3)
    const iqc = b.layers.find((l) => l.key === 'iqc')!
    expect(Math.abs(avg(iqc.trend30.slice(-4)) - avg(iqc.trend30.slice(0, 20)))).toBeLessThan(0.3)
  })

  it('过程检层承压：积压最多（活塞杆一线占大头）、批合格率最低、件不良率最高', () => {
    const ipqc = b.layers.find((l) => l.key === 'ipqc')!
    for (const l of b.layers) {
      if (l.key === 'ipqc') continue
      expect(ipqc.backlog).toBeGreaterThan(l.backlog)
      expect(ipqc.passRate).toBeLessThan(l.passRate)
      expect(ipqc.pieceDefectPct).toBeGreaterThan(l.pieceDefectPct)
    }
    expect(ipqc.backlogTop?.name).toBe('活塞杆一线')
    expect(ipqc.backlogTop!.count).toBeGreaterThan(ipqc.backlog / 2)
    // 今日未过批次全记在活塞杆一线（异常是例外）
    expect(ipqc.failedTop?.name).toBe('活塞杆一线')
    expect(ipqc.failedTop?.count).toBe(ipqc.lotsDone - ipqc.lotsPassed)
  })

  it('分层管控限：仅过程检小幅越限（事故层）；全厂加权仍在参考线内（一条红线不成立）', () => {
    expect(b.kpis.overdueNcr).toBeLessThanOrEqual(3)
    expect(b.kpis.batchPassRate).toBeGreaterThanOrEqual(97)
    for (const l of b.layers) expect(l.limitPct).toBeGreaterThan(0)
    const iqc = b.layers.find((l) => l.key === 'iqc')!
    const ipqcL = b.layers.find((l) => l.key === 'ipqc')!
    const fqc = b.layers.find((l) => l.key === 'fqc')!
    expect(iqc.passRate).toBeGreaterThanOrEqual(98)
    expect(fqc.passRate).toBeGreaterThanOrEqual(98)
    expect(iqc.pieceDefectPct).toBeLessThan(iqc.limitPct)
    expect(fqc.pieceDefectPct).toBeLessThan(fqc.limitPct)
    // 过程检层 ≈2.3%（设定集 §7 的不合格率口径）已越本层 2.2% 管控限
    expect(ipqcL.pieceDefectPct).toBeGreaterThan(ipqcL.limitPct)
    expect(ipqcL.pieceDefectPct).toBeLessThan(ipqcL.limitPct + 0.5)
    // 全厂加权（三种检验总体口径不同）低于参考线 —— 管控必须看分层，不看全厂平均
    expect(b.kpis.defectRatePct).toBeLessThan(DEFECT_RED_LINE_PCT)
  })
})

describe('buildQualityBoard（scope 与回落）', () => {
  it('未知工厂回落一号工厂画像（单基地世界观）', () => {
    expect(buildQualityBoard('NOPE').kpis.openNcr).toBe(buildQualityBoard().kpis.openNcr)
  })

  it('scope 收窄（机加车间）：NCR/帕累托只剩机加域，来料单据隐藏，KPI 随行重算', () => {
    const b = buildQualityBoard(DEFAULT_FACTORY_ID, ['WS-01'])
    const machLines = ['活塞杆一线', '活塞杆二线', '缸筒一线', '缸筒二线', '精磨线']
    expect(b.ncrs.length).toBeGreaterThanOrEqual(1)
    for (const r of b.ncrs) {
      expect(r.sourceType).toBe('line')
      expect(machLines).toContain(r.source)
    }
    expect(b.kpis.openNcr).toBe(b.ncrs.length)
    expect(b.kpis.overdueNcr).toBe(b.ncrs.filter((r) => r.overdue).length)
    expect(b.kpis.mrbPending).toBe(b.ncrs.filter((r) => r.status === 'review').length)
    for (const p of b.pareto.slice(0, -1)) expect(machLines).toContain(p.lineName)
    expect(b.pareto.at(-1)?.lineName).toBe('其余缺陷')
    expect(b.pareto.reduce((n, x) => n + x.pct, 0)).toBeCloseTo(100, 0)
  })
})
