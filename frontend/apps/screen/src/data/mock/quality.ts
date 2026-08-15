// 质量看板 mock 聚合（MAN-319）：质量健康度 + 待办闭环，真实业务画像前置 ——
// ① 与产线屏**同一个故事**：活塞杆一线（LINE-WB-ROD-01）DEV-CNC-03 振动超限 ⇔
//    本屏帕累托 TOP1/2 为活塞杆振纹/尺寸超差、龄期最长的超期 NCR 挂在该线当前工单
//    （工单号与 mock/line 的 currentWo 同源推导 world.woOf）、过程检积压该线偏多；
//    其余产线质量健康（异常是例外）；
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
import { DEFAULT_FACTORY_ID, LINES, WORKSHOPS } from './masterdata'
import { woOf } from './world'

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

// 产线当前工单号与 mock/line 同源（world.woOf）—— 质量屏 NCR 挂的工单
// 必须与产线屏正在生产的工单是同一张（活塞杆一线 = WO-2026-03421）。
export { woOf }

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
  /** 该层近 30 天件不良率基线 %（trendRamp = 尾部事故酝酿抬升，过程检层） */
  trendBase: number
  trendRamp?: boolean
  /** 该层件不良率管控限 %（分层管控 —— 每层标准不同） */
  limitPct: number
}

interface NcrSeed {
  n: number
  lineId?: string
  supplier?: string
  /** 来料行的来源采购订单号（source_document），设定集 §9 的 PO-2026-#### 段 */
  poN?: number
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
  /** 近 12h **过程检（IPQC）**每小时件不良率基线 —— 管控趋势看事故层，
   *  不看全厂平均（分层管控限见 layers.limitPct）；hotFrom 起为越限拉升段 */
  hourly: { bases: number[]; hotFrom: number; calm: [number, number]; hot: [number, number] }
  trend30: {
    rateBase: number
    rateAmp: number
    rateClamp: [number, number]
    ramp: number[]
    lotsBase: number
  }
  /** 让步接收 NCR 之外的在途条件放行单数 */
  extraRelease: number
}

// 宁沪减振一号工厂质量画像（设定集 §7）：
//  · 检验任务 29 周约 7000 → ≈40 批/日，按 IQC 10 / IPQC 20 / FQC 8 分层；
//  · 件不良率 2.3% 是**过程检层**口径（实测 NCR 164 张 / 2.531%），来料与成品检
//    各自 0.7x%，全厂加权 ≈1.4% —— 「全厂一条红线」不成立，管控看分层限；
//  · 唯一显著异常源是活塞杆一线（DEV-CNC-03 振动超限 → 表面振纹），
//    帕累托 TOP1/2 都在这条线上，龄期最长的超期 NCR 挂它的当前工单。
const PROFILES: Record<string, QualityProfile> = {
  'SITE-001': {
    layers: [
      {
        key: 'iqc',
        label: '来料检',
        code: 'IQC',
        lotsDone: 10,
        lotsPassed: 10,
        lotsDue: 12,
        carryOver: 1,
        oldestHours: 6,
        pieceInspected: 1200,
        pieceDefects: 9,
        trendBase: 0.72,
        limitPct: 1.0,
      },
      {
        key: 'ipqc',
        label: '过程检',
        code: 'IPQC',
        lotsDone: 20,
        lotsPassed: 19,
        lotsDue: 26,
        carryOver: 3,
        oldestHours: 34,
        backlogTop: { name: '活塞杆一线', count: 5 },
        failedTop: { name: '活塞杆一线', count: 1 },
        pieceInspected: 3200,
        pieceDefects: 74,
        trendBase: 1.86,
        trendRamp: true,
        limitPct: 2.2,
      },
      {
        key: 'fqc',
        label: '成品检',
        code: 'FQC',
        lotsDone: 8,
        lotsPassed: 8,
        lotsDue: 10,
        carryOver: 1,
        oldestHours: 7,
        pieceInspected: 3200,
        pieceDefects: 24,
        trendBase: 0.76,
        limitPct: 1.0,
      },
    ],
    // 设定集 §9 号段：NCR 走 `NCR-2026-####`（29 周共 164 张，当前在 016x 段）；
    // 来料 NCR 的来源单据引采购订单 `PO-2026-####`（29 周约 480 张）。
    ncrs: [
      {
        n: 158,
        lineId: 'LINE-WB-ROD-01',
        defect: '活塞杆表面振纹',
        qty: 86,
        ageHours: 62,
        status: 'disposing',
        disposition: '返工',
        product: '活塞杆 φ22×420',
      },
      {
        n: 161,
        supplier: '常州恒力弹簧有限公司',
        poN: 442,
        defect: '悬架弹簧（二供）自由高度超差',
        qty: 600,
        ageHours: 53,
        status: 'disposing',
        disposition: '退供',
      },
      {
        n: 159,
        lineId: 'LINE-WB-TUB-01',
        defect: '环缝焊接气孔',
        qty: 14,
        ageHours: 41,
        status: 'verify',
      },
      {
        n: 160,
        lineId: 'LINE-WB-FA-01',
        defect: '气密测试泄漏',
        qty: 9,
        ageHours: 36,
        status: 'disposing',
        disposition: '返工',
      },
      {
        n: 162,
        lineId: 'LINE-WB-CT-01',
        defect: '电泳膜厚不足',
        qty: 22,
        ageHours: 30,
        status: 'disposing',
        disposition: '让步接收',
      },
      {
        n: 163,
        lineId: 'LINE-WB-TS-01',
        defect: '阻尼力曲线超差',
        qty: 7,
        ageHours: 26,
        status: 'verify',
      },
      {
        n: 164,
        lineId: 'LINE-WB-GRD-01',
        defect: '精磨圆度超差',
        qty: 11,
        ageHours: 21,
        status: 'disposing',
        disposition: '返工',
      },
      {
        n: 165,
        supplier: '江阴特钢制品有限公司',
        poN: 448,
        defect: '45# 钢棒料表面裂纹',
        qty: 320,
        ageHours: 17,
        status: 'review',
      },
      {
        n: 166,
        lineId: 'LINE-WB-RA-02',
        defect: '活塞杆压装力超差',
        qty: 5,
        ageHours: 12,
        status: 'review',
      },
      {
        n: 167,
        lineId: 'LINE-WB-VA-01',
        defect: '阀片叠装顺序错',
        qty: 4,
        ageHours: 8,
        status: 'review',
      },
      {
        n: 168,
        supplier: '宁波密封件制造有限公司',
        poN: 451,
        defect: '油封唇口毛刺',
        qty: 180,
        ageHours: 5,
        status: 'review',
      },
      // 今晨 DEV-CNC-03 振动超限的直接回声：新开 NCR 仍在待评审
      {
        n: 169,
        lineId: 'LINE-WB-ROD-01',
        defect: '活塞杆外圆尺寸超差',
        qty: 34,
        ageHours: 3,
        status: 'review',
        product: '活塞杆 φ22×420',
      },
      {
        n: 170,
        lineId: 'LINE-WB-PK-01',
        defect: '成品箱贴错贴',
        qty: 6,
        ageHours: 1,
        status: 'review',
      },
    ],
    pareto: [
      // clamp 区间互不重叠 → 抖动后仍严格降序；Σ TOP5 + 长尾 ≈ 当日 107 件不良
      { defect: '活塞杆表面振纹', lineId: 'LINE-WB-ROD-01', base: 26, amp: 3, lo: 25, hi: 28 },
      { defect: '活塞杆外圆尺寸超差', lineId: 'LINE-WB-ROD-01', base: 18, amp: 3, lo: 17, hi: 20 },
      { defect: '气密测试泄漏', lineId: 'LINE-WB-FA-01', base: 11, amp: 2, lo: 10, hi: 12 },
      { defect: '电泳膜厚不足', lineId: 'LINE-WB-CT-01', base: 8, amp: 2, lo: 7, hi: 9 },
      { defect: '阻尼力曲线超差', lineId: 'LINE-WB-TS-01', base: 5, amp: 2, lo: 4, hi: 6 },
    ],
    paretoTail: 38,
    // 近 3h 活塞杆线缺陷拉升（与设备屏 DEV-CNC-03 振动报警时段呼应），此前平稳在本层限下
    hourly: {
      bases: [1.72, 1.8, 1.68, 1.84, 1.9, 1.78, 1.94, 1.88, 1.82, 2.32, 2.54, 2.72],
      hotFrom: 9,
      calm: [1.5, 2.05],
      hot: [2.2, 2.95],
    },
    trend30: {
      rateBase: 1.18,
      rateAmp: 0.2,
      rateClamp: [0.95, 1.42],
      ramp: [1.3, 1.42],
      lotsBase: 38,
    },
    extraRelease: 1,
  },
}

/** 近 12 小时整点标签（与产线屏趋势同款口径） */
function hourLabels12(now = new Date()): string[] {
  const h = now.getHours()
  return Array.from(
    { length: 12 },
    (_, i) => `${String((h - 11 + i + 24) % 24).padStart(2, '0')}:00`,
  )
}

/** 该层近 30 天件不良率：基线缓波，trendRamp 尾 3 天事故酝酿抬升；
 *  末点 = 当日 pieceDefectPct（与三层区当日数字勾稽）。 */
function layerTrend30(base: number, endPct: number, ramp: boolean): number[] {
  const out: number[] = []
  for (let i = 29; i >= 1; i--) {
    const lift = ramp && i <= 3 ? (4 - i) * 0.24 : 0
    out.push(round2(clamp(jf(base + lift, 0.16), Math.max(0.1, base - 0.22), base + lift + 0.24)))
  }
  out.push(endPct)
  return out
}

function buildLayers(seeds: LayerSeed[]): InspectionLayer[] {
  return seeds.map((s) => {
    const pieceDefectPct =
      s.pieceInspected > 0 ? round2((s.pieceDefects / s.pieceInspected) * 100) : 0
    return {
      ...s,
      backlog: s.lotsDue - s.lotsDone + s.carryOver,
      passRate: s.lotsDone > 0 ? round1((s.lotsPassed / s.lotsDone) * 100) : 100,
      pieceDefectPct,
      trend30: layerTrend30(s.trendBase, pieceDefectPct, s.trendRamp ?? false),
    }
  })
}

function buildNcrs(seeds: NcrSeed[], workshopIds: string[] | 'all'): NcrRow[] {
  const rows: NcrRow[] = []
  for (const s of seeds) {
    if (s.lineId) {
      const line = LINES.find((l) => l.id === s.lineId)
      if (!line) continue
      if (workshopIds !== 'all' && !workshopIds.includes(line.workshopId)) continue
      rows.push({
        code: seq('NCR-2026', s.n, 4),
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
        code: seq('NCR-2026', s.n, 4),
        sourceType: 'supplier',
        source: s.supplier ?? '外部供应商',
        sourceDoc: seq('PO-2026', s.poN ?? 400, 4),
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
  if (tail > 0) {
    items.push({
      defect: '其他',
      lineName: '其余缺陷',
      count: tail,
      pct: total > 0 ? round1((tail / total) * 100) : 0,
    })
  }
  return { items, total }
}

/** 12h 分层结构与 30 天视图一致：主线过程检（事故拉升），来料/成品平稳基线，
 *  全厂 = 按各层当日检验件数**逐点加权**（与三层区件数勾稽，单测锁定）。 */
function buildTrend12h(p: QualityProfile, layers: InspectionLayer[]): DefectTrend12h {
  const { bases, hotFrom, calm, hot } = p.hourly
  const ipqc = bases.map((b, i) => {
    const [lo, hi] = i >= hotFrom ? hot : calm
    return round2(clamp(jf(b, 0.2), lo, hi))
  })
  const seedOf = (key: InspectionLayer['key']) => layers.find((l) => l.key === key)!
  // 平稳层围绕各自当日件不良率小幅波动（无事故段）
  const flat = (base: number) =>
    bases.map(() => round2(clamp(jf(base, 0.14), Math.max(0.08, base - 0.18), base + 0.18)))
  const iqc = flat(seedOf('iqc').pieceDefectPct)
  const fqc = flat(seedOf('fqc').pieceDefectPct)
  const wI = seedOf('iqc').pieceInspected
  const wP = seedOf('ipqc').pieceInspected
  const wF = seedOf('fqc').pieceInspected
  const wSum = wI + wP + wF
  const factory = ipqc.map((v, i) => round2((iqc[i] * wI + v * wP + fqc[i] * wF) / wSum))
  return { ratePct: ipqc, iqc, fqc, factory, labels: hourLabels12() }
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
      ? clamp(
          Math.round(jf(lotsBase * 0.3, 8)),
          Math.round(lotsBase * 0.22),
          Math.round(lotsBase * 0.38),
        )
      : clamp(
          Math.round(jf(lotsBase, 20)),
          Math.round(lotsBase * 0.88),
          Math.round(lotsBase * 1.12),
        )
    lots.push(dayLots)
  }
  return { ratePct, lots, labels }
}

/** 质量看板聚合（纯函数）。workshopIds 收窄仅过滤 NCR/帕累托（检验分层与趋势为
 *  工厂级口径，真实车间维度待 #570）；未知工厂回落一号工厂画像。 */
export function buildQualityBoard(
  factoryId = DEFAULT_FACTORY_ID,
  workshopIds: string[] | 'all' = 'all',
): QualityBoard {
  const p = PROFILES[factoryId] ?? PROFILES[DEFAULT_FACTORY_ID]
  // scope 收窄时仅保留可见车间（供 NCR/帕累托过滤使用）
  const wsScope =
    workshopIds === 'all'
      ? ('all' as const)
      : WORKSHOPS.filter((w) => w.factoryId === factoryId && workshopIds.includes(w.id)).map(
          (w) => w.id,
        )

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
    trend12h: buildTrend12h(p, layers),
  }
}
