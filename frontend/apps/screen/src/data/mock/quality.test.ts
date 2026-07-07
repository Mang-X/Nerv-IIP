import { describe, expect, it } from 'vitest'
import { buildQualityBoard, DEFECT_RED_LINE_PCT, NCR_SLA_HOURS, woOf } from './quality'

const round1 = (n: number) => Math.round(n * 10) / 10
const round2 = (n: number) => Math.round(n * 100) / 100

describe('buildQualityBoard（F01 · 勾稽自洽）', () => {
  const b = buildQualityBoard('F01')

  it('批次合格率 = Σ合格批/Σ判定批；不良率（件口径）= Σ不良件/Σ检验件，两口径同源互补', () => {
    const done = b.layers.reduce((n, l) => n + l.lotsDone, 0)
    const passed = b.layers.reduce((n, l) => n + l.lotsPassed, 0)
    expect(b.kpis.batchTotal).toBe(done)
    expect(b.kpis.batchPassed).toBe(passed)
    expect(b.kpis.batchPassRate).toBe(round1((passed / done) * 100))
    const insp = b.layers.reduce((n, l) => n + l.pieceInspected, 0)
    const def = b.layers.reduce((n, l) => n + l.pieceDefects, 0)
    expect(b.kpis.defectRatePct).toBe(round2((def / insp) * 100))
    // 每层：批合格率 / 件不良率均由该层明细推导
    for (const l of b.layers) {
      expect(l.passRate).toBe(round1((l.lotsPassed / l.lotsDone) * 100))
      expect(l.pieceDefectPct).toBe(round2((l.pieceDefects / l.pieceInspected) * 100))
      expect(l.lotsPassed).toBeLessThanOrEqual(l.lotsDone)
      expect(l.lotsDone).toBeLessThanOrEqual(l.lotsDue + l.carryOver)
    }
  })

  it('检验积压 = 应检−已判+结转，KPI 与三层同源；最老龄期取层最大', () => {
    for (const l of b.layers) expect(l.backlog).toBe(l.lotsDue - l.lotsDone + l.carryOver)
    expect(b.kpis.inspectionBacklog).toBe(b.layers.reduce((n, l) => n + l.backlog, 0))
    expect(b.kpis.backlogOldestHours).toBe(Math.max(...b.layers.map((l) => l.oldestHours)))
  })

  it('NCR：超期数 = 龄期 > SLA 的行数且与 KPI 一致；MRB/条件放行与行状态勾稽', () => {
    expect(b.kpis.openNcr).toBe(b.ncrs.length)
    const overdue = b.ncrs.filter((r) => r.ageHours > NCR_SLA_HOURS)
    expect(b.kpis.overdueNcr).toBe(overdue.length)
    for (const r of b.ncrs) expect(r.overdue).toBe(r.ageHours > NCR_SLA_HOURS)
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

  it('帕累托：TOP5 严格降序、Σ占比 ≤ 100、占比按同一分母重算一致', () => {
    expect(b.pareto).toHaveLength(5)
    for (let i = 1; i < b.pareto.length; i++) {
      expect(b.pareto[i - 1].count).toBeGreaterThan(b.pareto[i].count)
      expect(b.pareto[i - 1].pct).toBeGreaterThanOrEqual(b.pareto[i].pct)
    }
    const sumCount = b.pareto.reduce((n, p) => n + p.count, 0)
    expect(b.paretoTotal).toBeGreaterThanOrEqual(sumCount)
    expect(b.pareto.reduce((n, p) => n + p.pct, 0)).toBeLessThanOrEqual(100)
    for (const p of b.pareto) expect(p.pct).toBe(round1((p.count / b.paretoTotal) * 100))
  })

  it('趋势（过程检口径）：12h 尾段越过程管控限（今晨事故）、此前在限内；30 天末点 = 当日 KPI、周日检验量低谷', () => {
    const ipqcLimit = b.layers.find((l) => l.key === 'ipqc')!.limitPct
    expect(b.trend12h.ratePct).toHaveLength(12)
    expect(b.trend12h.labels).toHaveLength(12)
    for (const l of b.trend12h.labels) expect(l).toMatch(/^\d{2}:00$/)
    expect(b.trend12h.ratePct.at(-1)!).toBeGreaterThan(ipqcLimit)
    for (const v of b.trend12h.ratePct.slice(0, 9)) expect(v).toBeLessThan(ipqcLimit)

    expect(b.trend30.ratePct).toHaveLength(30)
    expect(b.trend30.lots).toHaveLength(30)
    expect(b.trend30.labels).toHaveLength(30)
    for (const l of b.trend30.labels) expect(l).toMatch(/^\d{1,2}\/\d{1,2}$/)
    // 今日收盘点与 KPI 严格勾稽
    expect(b.trend30.ratePct.at(-1)).toBe(b.kpis.defectRatePct)
    expect(b.trend30.lots.at(-1)).toBe(b.kpis.batchTotal)
    // 周日低谷：30 天内必有判定批次 < 峰值一半的日子（工厂周日减产）
    const peak = Math.max(...b.trend30.lots)
    expect(b.trend30.lots.some((v) => v < peak * 0.5)).toBe(true)
    for (const v of b.trend30.ratePct) {
      expect(v).toBeGreaterThan(0)
      expect(v).toBeLessThan(3)
    }
  })
})

describe('buildQualityBoard（与产线屏同一个故事：电芯线卷绕机报警）', () => {
  const b = buildQualityBoard('F01')

  it('NCR 挂电芯线当前工单 WO-1951（与 mock/line currentWo 同源推导）+ 产品 LFP-280Ah 电芯', () => {
    expect(woOf('LN-BAT-1')).toBe('WO-1951')
    const batRows = b.ncrs.filter((r) => r.lineId === 'LN-BAT-1')
    expect(batRows.length).toBeGreaterThanOrEqual(1)
    for (const r of batRows) {
      expect(r.sourceDoc).toBe('WO-1951')
      expect(r.product).toBe('LFP-280Ah 电芯')
    }
    // 龄期最长（置顶）且超期红标的正是电芯线那张单
    expect(b.ncrs[0].lineId).toBe('LN-BAT-1')
    expect(b.ncrs[0].overdue).toBe(true)
    // 今晨报警的回声：还有一张低龄期电芯线 NCR 待评审（卷绕张力）
    expect(batRows.some((r) => r.status === 'review' && r.ageHours <= 6)).toBe(true)
  })

  it('帕累托 TOP1/TOP2 为电芯缺陷且来源 = 电芯线', () => {
    expect(b.pareto[0].defect).toBe('极片对齐度超差')
    expect(b.pareto[0].lineName).toBe('电芯线')
    expect(b.pareto[1].lineName).toBe('电芯线')
  })

  it('分层 30 天件不良率：三层各 30 点、末点 = 当日件不良率勾稽、过程检尾部酝酿抬升', () => {
    for (const l of b.layers) {
      expect(l.trend30).toHaveLength(30)
      expect(l.trend30.at(-1)).toBe(l.pieceDefectPct)
      for (const v of l.trend30) expect(v).toBeGreaterThan(0)
    }
    // F01 过程检（电芯事故酝酿）：尾 4 点均值明显高于前段基线；来料/成品平稳
    const ipqc = b.layers.find((l) => l.key === 'ipqc')!
    const head = ipqc.trend30.slice(0, 20)
    const tail = ipqc.trend30.slice(-4)
    const avg = (a: number[]) => a.reduce((n, v) => n + v, 0) / a.length
    expect(avg(tail)).toBeGreaterThan(avg(head) + 0.3)
    const iqc = b.layers.find((l) => l.key === 'iqc')!
    expect(Math.abs(avg(iqc.trend30.slice(-4)) - avg(iqc.trend30.slice(0, 20)))).toBeLessThan(0.3)
  })

  it('过程检层承压：积压最多（电芯线占大头）、批合格率最低、件不良率最高', () => {
    const ipqc = b.layers.find((l) => l.key === 'ipqc')!
    for (const l of b.layers) {
      if (l.key === 'ipqc') continue
      expect(ipqc.backlog).toBeGreaterThan(l.backlog)
      expect(ipqc.passRate).toBeLessThan(l.passRate)
      expect(ipqc.pieceDefectPct).toBeGreaterThan(l.pieceDefectPct)
    }
    expect(ipqc.backlogTop?.name).toBe('电芯线')
    expect(ipqc.backlogTop!.count).toBeGreaterThan(ipqc.backlog / 2)
    // 今日未过批次全记在电芯线（异常是例外）
    expect(ipqc.failedTop?.name).toBe('电芯线')
    expect(ipqc.failedTop?.count).toBe(ipqc.lotsDone - ipqc.lotsPassed)
  })

  it('异常是例外 + 分层管控限：仅过程检小幅越限（事故层），来料/成品在各自限内', () => {
    expect(b.kpis.overdueNcr).toBeLessThanOrEqual(3)
    expect(b.kpis.batchPassRate).toBeGreaterThanOrEqual(97)
    for (const l of b.layers) expect(l.limitPct).toBeGreaterThan(0)
    const iqc = b.layers.find((l) => l.key === 'iqc')!
    const ipqcL = b.layers.find((l) => l.key === 'ipqc')!
    const fqc = b.layers.find((l) => l.key === 'fqc')!
    expect(iqc.passRate).toBeGreaterThanOrEqual(98)
    expect(fqc.passRate).toBeGreaterThanOrEqual(98)
    // 管控口径：每层对照自己的管控限（全厂一条红线不成立）
    expect(iqc.pieceDefectPct).toBeLessThan(iqc.limitPct)
    expect(fqc.pieceDefectPct).toBeLessThan(fqc.limitPct)
    expect(ipqcL.pieceDefectPct).toBeGreaterThan(ipqcL.limitPct)
    expect(ipqcL.pieceDefectPct).toBeLessThan(ipqcL.limitPct + 0.5)
    // 全厂汇总仍作参考口径保留
    expect(b.kpis.defectRatePct).toBeGreaterThan(DEFECT_RED_LINE_PCT)
  })
})

describe('buildQualityBoard（对照与 scope）', () => {
  it('F02 无事故基线：零超期、各层均在管控限内、批合格率 ≥ 98（异常是例外的对照组）', () => {
    const b = buildQualityBoard('F02')
    expect(b.kpis.overdueNcr).toBe(0)
    for (const l of b.layers) expect(l.pieceDefectPct).toBeLessThan(l.limitPct)
    expect(b.kpis.batchPassRate).toBeGreaterThanOrEqual(98)
    expect(b.kpis.openNcr).toBeGreaterThanOrEqual(3)
    const f02Limit = b.layers.find((l) => l.key === 'ipqc')!.limitPct
    for (const v of b.trend12h.ratePct) expect(v).toBeLessThan(f02Limit)
    // KPI 仍与行/层数据勾稽
    expect(b.kpis.openNcr).toBe(b.ncrs.length)
    expect(b.kpis.inspectionBacklog).toBe(b.layers.reduce((n, l) => n + l.backlog, 0))
  })

  it('scope 收窄（电池车间）：NCR/帕累托只剩电芯域，来料单据隐藏，KPI 随行重算', () => {
    const b = buildQualityBoard('F01', ['WS-BATTERY'])
    expect(b.ncrs.length).toBeGreaterThanOrEqual(1)
    for (const r of b.ncrs) {
      expect(r.sourceType).toBe('line')
      expect(['电芯线', '电芯二线', '模组线', 'PACK 线', 'PACK 二线']).toContain(r.source)
    }
    expect(b.kpis.openNcr).toBe(b.ncrs.length)
    expect(b.kpis.overdueNcr).toBe(b.ncrs.filter((r) => r.overdue).length)
    expect(b.kpis.mrbPending).toBe(b.ncrs.filter((r) => r.status === 'review').length)
    for (const p of b.pareto) expect(p.lineName).toBe('电芯线')
    expect(b.pareto.reduce((n, x) => n + x.pct, 0)).toBeLessThanOrEqual(100)
  })
})
