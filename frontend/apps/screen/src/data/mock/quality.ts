// 质量看板 mock 聚合（MAN-319）：质量健康度 + 待办闭环，真实业务画像前置 ——
// ① 与产线屏**同一个故事**：电芯线（LN-BAT-1）卷绕机 1# 报警 ⇔ 本屏帕累托 TOP1/2
//    为电芯缺陷、龄期最长的超期 NCR 挂在电芯线当前工单 WO-1951（工单号与 mock/line
//    的 currentWo 同源推导）、过程检积压电芯线偏多；其余产线质量健康（异常是例外）；
// ② 勾稽自洽：批次合格率 = Σ合格批/Σ判定批、不良率（件口径）= Σ不良件/Σ检验件、
//    帕累托 Σ占比 ≤ 100 且降序、超期 NCR 数 = 龄期 > SLA 行数、三层合格率与检验
//    积压同源自同一组 InspectionLayer；
// ③ 龄期/时钟真实：NCR 龄期为 now 相对偏移；30 天趋势含周日检验量低谷。
// 🟠 待 #570：Quality 零聚合 API，多数 KPI 前端先行；⚠️ 缺陷码 Quality(reason_code)
// 与 MES(defect_code) 口径不统一 —— mock 用统一语义名，真实接入时需映射归一。
import {
  DEFECT_RED_LINE_PCT,
  type DefectTrend12h,
  type DefectTrend30,
  type InspectionLayer,
  NCR_SLA_HOURS,
  type NcrDisposition,
  type NcrRow,
  type NcrStatus,
  type ParetoItem,
  type QualityBoard,
  type QualityKpis,
} from '@/data/contracts/quality'
import { seq } from './fixtures'
import { LINES, WORKSHOPS } from './masterdata'

export { DEFECT_RED_LINE_PCT, NCR_SLA_HOURS }

const STATUS_LABELS: Record<NcrStatus, string> = {
  review: '待评审',
  disposing: '处置中',
  verify: '待验证',
}

function clamp(n: number, lo: number, hi: number): number {
  return Math.min(hi, Math.max(lo, n))
}
/** 浮点抖动（fixtures.jitter 为整数口径，比率类用这个） */
function jf(base: number, amp: number): number {
  return base + (Math.random() - 0.5) * amp
}
function round1(n: number): number {
  return Math.round(n * 10) / 10
}
function round2(n: number): number {
  return Math.round(n * 100) / 100
}

/** 产线当前工单号 —— 与 mock/line 的 currentWo 同一推导（seq('WO', 1940 + 产线序）），
 *  保证质量屏 NCR 挂的工单与产线屏正在生产的工单一致（电芯线 = WO-1951）。 */
export function woOf(lineId: string): string {
  return seq('WO', 1940 + Math.max(0, LINES.findIndex((l) => l.id === lineId)))
}
function lineNameOf(lineId: string): string {
  return LINES.find((l) => l.id === lineId)?.name ?? lineId
}

// —— 每工厂质量画像（确定性种子；动态量仅受控抖动，勾稽由构造保证）——

interface LayerSeed {
  key: InspectionLayer['key']
  label: string
  code: string
  lotsDone: number
  lotsPassed: number
  lotsDue: number
  carryOver: number
  oldestHours: number
  backlogTop?: { name: string; count: number }
  failedTop?: { name: string; count: number }
  pieceInspected: number
  pieceDefects: number
}

interface NcrSeed {
  n: number
  lineId?: string
  supplier?: string
  /** 来料行的检验单号（source_document） */
  iqcN?: number
  defect: string
  qty: number
  ageHours: number
  status: NcrStatus
  disposition?: NcrDisposition
  product?: string
}

interface ParetoSeed {
  defect: string
  lineId: string
  base: number
  amp: number
  lo: number
  hi: number
}

interface QualityProfile {
  layers: LayerSeed[]
  ncrs: NcrSeed[]
  pareto: ParetoSeed[]
  /** 帕累托长尾（TOP5 以外的其余缺陷件数，保证 Σ占比 < 100） */
  paretoTail: number
  /** 近 12h 每小时不良率基线；hotFrom 起为红线拉升段（工厂无事故则为 12 = 无热段） */
  hourly: { bases: number[]; hotFrom: number; calm: [number, number]; hot: [number, number] }
  trend30: { rateBase: number; rateAmp: number; rateClamp: [number, number]; ramp: number[]; lotsBase: number }
  /** 让步接收 NCR 之外的在途条件放行单数 */
  extraRelease: number
}

const PROFILES: Record<string, QualityProfile> = {
  // F01 华东智造基地：整体健康（批合格率 97%+），电芯线是唯一显著异常源 ——
  // 不良率（件）刚越红线、帕累托 TOP1/2 电芯缺陷、超期 NCR 2 条（异常是例外）
  F01: {
    layers: [
      { key: 'iqc', label: '来料检', code: 'IQC', lotsDone: 60, lotsPassed: 59, lotsDue: 64, carryOver: 1, oldestHours: 6, pieceInspected: 5200, pieceDefects: 38 },
      { key: 'ipqc', label: '过程检', code: 'IPQC', lotsDone: 70, lotsPassed: 67, lotsDue: 78, carryOver: 6, oldestHours: 36, backlogTop: { name: '电芯线', count: 8 }, failedTop: { name: '电芯线', count: 3 }, pieceInspected: 7800, pieceDefects: 190 },
      { key: 'fqc', label: '成品检', code: 'FQC', lotsDone: 62, lotsPassed: 61, lotsDue: 66, carryOver: 2, oldestHours: 7, pieceInspected: 3800, pieceDefects: 30 },
    ],
    ncrs: [
      // 龄期最长 + 超期：电芯线当前工单（与产线屏 WO-1951 / LFP-280Ah 电芯同源）
      { n: 41, lineId: 'LN-BAT-1', defect: '极片对齐度超差', qty: 120, ageHours: 62, status: 'disposing', disposition: '返工', product: 'LFP-280Ah 电芯' },
      { n: 43, supplier: '宁华新材', iqcN: 2291, defect: '隔膜厚度偏差', qty: 2400, ageHours: 51, status: 'disposing', disposition: '退供' },
      { n: 44, lineId: 'LN-WELD-3', defect: '螺柱焊漏焊', qty: 7, ageHours: 40, status: 'disposing', disposition: '返工' },
      { n: 45, lineId: 'LN-STAMP-1', defect: '板料表面划伤', qty: 18, ageHours: 34, status: 'verify' },
      { n: 46, lineId: 'LN-ASSY-1', defect: '密封圈压伤', qty: 9, ageHours: 26, status: 'verify' },
      { n: 47, lineId: 'LN-WELD-1', defect: '焊点虚焊', qty: 14, ageHours: 21, status: 'disposing', disposition: '返工' },
      { n: 48, lineId: 'LN-ASSY-2', defect: '内饰面板色差', qty: 60, ageHours: 17, status: 'disposing', disposition: '让步接收' },
      { n: 49, supplier: '宁华新材', iqcN: 2307, defect: '电解液含水量超标', qty: 1, ageHours: 12, status: 'review' },
      // 今晨卷绕机报警的直接回声：新开 NCR 仍在待评审
      { n: 50, lineId: 'LN-BAT-1', defect: '卷绕张力不良', qty: 86, ageHours: 3, status: 'review', product: 'LFP-280Ah 电芯' },
      { n: 51, lineId: 'LN-BAT-2', defect: '气密测试不合格', qty: 3, ageHours: 2, status: 'review' },
      { n: 52, lineId: 'LN-PAINT-2', defect: '面漆橘皮', qty: 6, ageHours: 1, status: 'review' },
    ],
    pareto: [
      // clamp 区间互不重叠 → 抖动后仍严格降序
      { defect: '极片对齐度超差', lineId: 'LN-BAT-1', base: 46, amp: 4, lo: 44, hi: 48 },
      { defect: '卷绕张力不良', lineId: 'LN-BAT-1', base: 31, amp: 4, lo: 29, hi: 33 },
      { defect: '焊点虚焊', lineId: 'LN-WELD-1', base: 12, amp: 2, lo: 11, hi: 13 },
      { defect: '面漆橘皮', lineId: 'LN-PAINT-2', base: 9, amp: 2, lo: 8, hi: 10 },
      { defect: '密封圈压伤', lineId: 'LN-ASSY-1', base: 6, amp: 2, lo: 5, hi: 7 },
    ],
    paretoTail: 34,
    // 近 3h 电芯线缺陷拉升越红线（与产线屏卷绕机报警时段呼应），此前平稳在线下
    hourly: { bases: [1.12, 1.2, 1.08, 1.24, 1.3, 1.18, 1.34, 1.28, 1.22, 1.92, 2.18, 2.42], hotFrom: 9, calm: [0.9, 1.45], hot: [1.7, 2.6] },
    trend30: { rateBase: 1.08, rateAmp: 0.2, rateClamp: [0.85, 1.38], ramp: [1.24, 1.38], lotsBase: 188 },
    extraRelease: 2,
  },
  // F02 华南制造中心：无事故，全绿基线（异常是例外的对照组）
  F02: {
    layers: [
      { key: 'iqc', label: '来料检', code: 'IQC', lotsDone: 24, lotsPassed: 24, lotsDue: 26, carryOver: 0, oldestHours: 4, pieceInspected: 2600, pieceDefects: 12 },
      { key: 'ipqc', label: '过程检', code: 'IPQC', lotsDone: 30, lotsPassed: 29, lotsDue: 33, carryOver: 1, oldestHours: 9, failedTop: { name: '注塑一线', count: 1 }, pieceInspected: 3400, pieceDefects: 26 },
      { key: 'fqc', label: '成品检', code: 'FQC', lotsDone: 22, lotsPassed: 22, lotsDue: 24, carryOver: 0, oldestHours: 3, pieceInspected: 1800, pieceDefects: 9 },
    ],
    ncrs: [
      { n: 55, lineId: 'LN-INJ-1', defect: '缩痕超标', qty: 42, ageHours: 30, status: 'disposing', disposition: '报废' },
      { n: 56, lineId: 'LN-INJ-2', defect: '仪表板表面划痕', qty: 60, ageHours: 15, status: 'disposing', disposition: '让步接收' },
      { n: 57, supplier: '东旭精密', iqcN: 1183, defect: '支架孔位偏移', qty: 300, ageHours: 9, status: 'review' },
      { n: 58, lineId: 'LN-MACH-1', defect: '孔径超差', qty: 5, ageHours: 6, status: 'review' },
    ],
    pareto: [
      { defect: '缩痕超标', lineId: 'LN-INJ-1', base: 9, amp: 2, lo: 8, hi: 10 },
      { defect: '仪表板表面划痕', lineId: 'LN-INJ-2', base: 6, amp: 2, lo: 5, hi: 7 },
      { defect: '孔径超差', lineId: 'LN-MACH-1', base: 4, amp: 0, lo: 4, hi: 4 },
      { defect: '浇口毛边残留', lineId: 'LN-INJ-1', base: 3, amp: 0, lo: 3, hi: 3 },
      { defect: '端面平面度超差', lineId: 'LN-MACH-1', base: 2, amp: 0, lo: 2, hi: 2 },
    ],
    paretoTail: 9,
    hourly: { bases: [0.55, 0.6, 0.5, 0.62, 0.58, 0.66, 0.6, 0.55, 0.64, 0.6, 0.58, 0.62], hotFrom: 12, calm: [0.4, 0.85], hot: [0.4, 0.85] },
    trend30: { rateBase: 0.6, rateAmp: 0.16, rateClamp: [0.42, 0.82], ramp: [], lotsBase: 66 },
    extraRelease: 1,
  },
}

/** 近 12 小时整点标签（与产线屏趋势同款口径） */
function hourLabels12(now = new Date()): string[] {
  const h = now.getHours()
  return Array.from({ length: 12 }, (_, i) => `${String((h - 11 + i + 24) % 24).padStart(2, '0')}:00`)
}

function buildLayers(seeds: LayerSeed[]): InspectionLayer[] {
  return seeds.map((s) => ({
    ...s,
    backlog: s.lotsDue - s.lotsDone + s.carryOver,
    passRate: s.lotsDone > 0 ? round1((s.lotsPassed / s.lotsDone) * 100) : 100,
    pieceDefectPct: s.pieceInspected > 0 ? round2((s.pieceDefects / s.pieceInspected) * 100) : 0,
  }))
}

function buildNcrs(seeds: NcrSeed[], workshopIds: string[] | 'all'): NcrRow[] {
  const rows: NcrRow[] = []
  for (const s of seeds) {
    if (s.lineId) {
      const line = LINES.find((l) => l.id === s.lineId)
      if (!line) continue
      if (workshopIds !== 'all' && !workshopIds.includes(line.workshopId)) continue
      rows.push({
        code: seq('NCR-26', s.n, 3),
        sourceType: 'line',
        source: line.name,
        lineId: line.id,
        sourceDoc: woOf(line.id),
        product: s.product,
        defect: s.defect,
        qty: s.qty,
        ageHours: s.ageHours,
        overdue: s.ageHours > NCR_SLA_HOURS,
        status: s.status,
        statusLabel: STATUS_LABELS[s.status],
        disposition: s.disposition,
      })
    } else {
      // 来料 NCR：车间收窄 scope 下不展示（来料属工厂级，真实维度待 #570）
      if (workshopIds !== 'all') continue
      rows.push({
        code: seq('NCR-26', s.n, 3),
        sourceType: 'supplier',
        source: s.supplier ?? '外部供应商',
        sourceDoc: seq('IQC', s.iqcN ?? 2000),
        defect: s.defect,
        qty: s.qty,
        ageHours: s.ageHours,
        overdue: s.ageHours > NCR_SLA_HOURS,
        status: s.status,
        statusLabel: STATUS_LABELS[s.status],
        disposition: s.disposition,
      })
    }
  }
  // 龄期降序：最老（最痛）置顶，超期自然在最前
  return rows.sort((a, b) => b.ageHours - a.ageHours)
}

function buildPareto(
  seeds: ParetoSeed[],
  tail: number,
  workshopIds: string[] | 'all',
): { items: ParetoItem[]; total: number } {
  const visible = seeds.filter((s) => {
    if (workshopIds === 'all') return true
    const line = LINES.find((l) => l.id === s.lineId)
    return !!line && workshopIds.includes(line.workshopId)
  })
  const counts = visible.map((s) => clamp(Math.round(jf(s.base, s.amp)), s.lo, s.hi))
  const total = counts.reduce((n, c) => n + c, 0) + tail
  const items = visible.map((s, i) => ({
    defect: s.defect,
    lineName: lineNameOf(s.lineId),
    count: counts[i],
    pct: total > 0 ? round1((counts[i] / total) * 100) : 0,
  }))
  return { items, total }
}

function buildTrend12h(p: QualityProfile): DefectTrend12h {
  const { bases, hotFrom, calm, hot } = p.hourly
  return {
    ratePct: bases.map((b, i) => {
      const [lo, hi] = i >= hotFrom ? hot : calm
      return round2(clamp(jf(b, 0.2), lo, hi))
    }),
    labels: hourLabels12(),
  }
}

function buildTrend30(p: QualityProfile, todayRate: number, todayLots: number): DefectTrend30 {
  const { rateBase, rateAmp, rateClamp, ramp, lotsBase } = p.trend30
  const ratePct: number[] = []
  const lots: number[] = []
  const labels: string[] = []
  const today = new Date()
  for (let i = 29; i >= 0; i--) {
    const d = new Date(today.getFullYear(), today.getMonth(), today.getDate() - i)
    labels.push(`${d.getMonth() + 1}/${d.getDate()}`)
    if (i === 0) {
      // 今日点与 KPI 严格勾稽：收盘即当日不良率 / 当日判定批次
      ratePct.push(todayRate)
      lots.push(todayLots)
      continue
    }
    // 收尾爬坡段（事故酝酿期）；无 ramp 的工厂全程平稳
    const rampIdx = ramp.length - i
    const base = rampIdx >= 0 ? ramp[rampIdx] : rateBase
    const [lo, hi] = rampIdx >= 0 ? [rateBase, 1.48] : rateClamp
    ratePct.push(round2(clamp(jf(base, rateAmp), lo, hi)))
    // 周日检验量低谷（工厂周日减产 → 报检批次骤降），量低≠率异常
    const sunday = d.getDay() === 0
    const dayLots = sunday
      ? clamp(Math.round(jf(lotsBase * 0.3, 8)), Math.round(lotsBase * 0.22), Math.round(lotsBase * 0.38))
      : clamp(Math.round(jf(lotsBase, 20)), Math.round(lotsBase * 0.88), Math.round(lotsBase * 1.12))
    lots.push(dayLots)
  }
  return { ratePct, lots, labels }
}

/** 质量看板聚合（纯函数）。workshopIds 收窄仅过滤 NCR/帕累托（检验分层与趋势为
 *  工厂级口径，真实车间维度待 #570）；未知工厂回落 F01 画像。 */
export function buildQualityBoard(
  factoryId = 'F01',
  workshopIds: string[] | 'all' = 'all',
): QualityBoard {
  const p = PROFILES[factoryId] ?? PROFILES.F01
  // scope 收窄时仅保留可见车间（供 NCR/帕累托过滤使用）
  const wsScope =
    workshopIds === 'all'
      ? ('all' as const)
      : WORKSHOPS.filter((w) => w.factoryId === factoryId && workshopIds.includes(w.id)).map((w) => w.id)

  const layers = buildLayers(p.layers)
  const ncrs = buildNcrs(p.ncrs, wsScope)
  const { items: pareto, total: paretoTotal } = buildPareto(p.pareto, p.paretoTail, wsScope)

  // —— KPI 全部从明细推导（勾稽由构造保证，单测锁死）——
  const batchTotal = layers.reduce((n, l) => n + l.lotsDone, 0)
  const batchPassed = layers.reduce((n, l) => n + l.lotsPassed, 0)
  const pieceInspected = layers.reduce((n, l) => n + l.pieceInspected, 0)
  const pieceDefects = layers.reduce((n, l) => n + l.pieceDefects, 0)
  const defectRatePct = pieceInspected > 0 ? round2((pieceDefects / pieceInspected) * 100) : 0
  const concession = ncrs.filter((r) => r.disposition === '让步接收').length

  const kpis: QualityKpis = {
    batchPassRate: batchTotal > 0 ? round1((batchPassed / batchTotal) * 100) : 100,
    batchPassed,
    batchTotal,
    defectRatePct,
    redLinePct: DEFECT_RED_LINE_PCT,
    openNcr: ncrs.length,
    overdueNcr: ncrs.filter((r) => r.overdue).length,
    inspectionBacklog: layers.reduce((n, l) => n + l.backlog, 0),
    backlogOldestHours: Math.max(0, ...layers.map((l) => l.oldestHours)),
    conditionalRelease: concession + (wsScope === 'all' ? p.extraRelease : 0),
    mrbPending: ncrs.filter((r) => r.status === 'review').length,
  }

  return {
    factoryId,
    kpis,
    ncrs,
    pareto,
    paretoTotal,
    layers,
    trend30: buildTrend30(p, defectRatePct, batchTotal),
    trend12h: buildTrend12h(p),
  }
}
