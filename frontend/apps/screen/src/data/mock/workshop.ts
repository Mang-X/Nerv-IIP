// 车间总览 mock 聚合（MAN-315）：车间主任「当班作战室」——
// ① 产线区**直接复用** buildLineCards（与产线/设备屏同源：活塞杆一线红灯 ⇔ DEV-CNC-03 报警），
//    车间产量/计划/达成 = Σ 本车间产线卡，数字精确同源（勾稽单测锁定）——
//    注意这是**车间内工序产出合计**，全厂「成品下线」另有末道口径，见 mock/factory.ts；
// ② 事件流从设备画像归并（急停/待修/换型/失联），计划保养不进异常流 ——
//    异常是例外，正常车间事件区为空（空态 = 健康）；
// ③ 齐套与产线屏 kitting 同口径（前减装配二线缺料，需求量 = 该线当前工单计划数同式）；
// ④ 人员区诚实口径：只给班组花名册/班次/交接遗留/技能覆盖（平台真实能力），
//    **不造**考勤在岗/人效（数据缺口，见 spec 人员维度铁律）。
// 🟠 车间维度聚合当前无真实端点（#570），接入后由 fetchers/workshop.ts 单点切换。
import type { LineState, LineSummaryCard } from '@/data/contracts/line'
import type {
  CrewInfo,
  Daily30,
  LineOee,
  NcrItem,
  ShiftCurve,
  ShortageItem,
  WoAlert,
  WorkshopBoard,
  WorkshopEvent,
  WorkshopOee,
} from '@/data/contracts/workshop'
import { buildEquipmentOverview } from './equipment'
import { clock, jitter } from './fixtures'
import { buildLineCards } from './line'
import { DEFAULT_FACTORY_ID, linesByWorkshop, workshopsByFactory } from './masterdata'
import { buildQualityBoard } from './quality'
import { shiftNow, teamOf, woOf } from './world'

function clamp(n: number, lo: number, hi: number): number {
  return Math.min(hi, Math.max(lo, n))
}
const p2 = (n: number) => String(n).padStart(2, '0')
const fmtDur = (min: number) =>
  min >= 60 ? `${Math.floor(min / 60)}h ${p2(min % 60)}m` : `${min}m`

const WS_STATE_LABELS: Record<LineState, string> = {
  run: '运行正常',
  attention: '需关注',
  alarm: '设备报警',
}

/** 车间态归并（纯函数）：任一线红 → 红；任一线黄 → 黄；否则绿。 */
export function composeWorkshopState(lines: Pick<LineSummaryCard, 'state'>[]): LineState {
  if (lines.some((l) => l.state === 'alarm')) return 'alarm'
  if (lines.some((l) => l.state === 'attention')) return 'attention'
  return 'run'
}

// —— 班组花名册（✅ 平台真实能力：设定集 §5 的 6 个**车间级**班组，组长为 L0 EMP-004..009）——
// 技能覆盖率基线：操作工每人 1–3 项带等级/取证日期（§5），车间间略有差异。
const SKILL_BASE: Record<string, number> = { 'WS-01': 94, 'WS-02': 92, 'WS-03': 90 }

// 班次交接遗留（✅ 平台有交接记录，设定集 §9 `HO-2026-#####`；
// 叙事与设备画像勾稽：上一班已留意的隐患当班应验）
const HANDOVER: Record<string, { issues: number; note: string }> = {
  'WS-01': { issues: 1, note: '上一班交接：DEV-CNC-03 主轴间歇异响，需重点观察振动值' },
  'WS-03': { issues: 1, note: '上一班交接：DEV-CTG-02 循环泵底部渗液，已报设备部跟进' },
}

// —— 线边缺料画像：与 mock/line.ts buildLineBoard 的 kitting 口径一致（仅前减装配二线 short）；
//    需求量按该线当前工单计划数派生（与产线屏 qtyPlan 同式，跨屏对得上）。
//    物料编码取 L0 §4 原材料段（RM-*），弹簧二供切换是设定集里既有的版本演进故事。
const KITTING_SHORT_LINES = new Set(['LINE-WB-FA-02'])
interface ShortageSpec {
  material: string
  code: string
  /** 缺口占当前工单计划数比例 */
  shortPct: number
  etaMin?: number
  etaText?: string
}
const SHORTAGE_SPECS: Record<string, ShortageSpec[]> = {
  'LINE-WB-FA-02': [
    { material: '悬架弹簧 轿车前（首选供应商）', code: 'RM-SPR-01', shortPct: 0.12, etaMin: 85 },
    { material: '油封 φ22 骨架式', code: 'RM-SEL-02', shortPct: 0.08, etaMin: 45 },
    { material: '防尘罩 带缓冲块', code: 'RM-ACC-06', shortPct: 0.04, etaText: '在途 · 待入库' },
  ],
}

// —— 当班已闭环事件（短停/预警已恢复）：作战室事件流要有当班全貌 ——
// 活跃异常置顶、历史沉底灰显；已恢复短停计入当班停机统计（与 downtime 对账）。
// 无当班异常的车间保持空 —— 空态 = 健康，不为填屏造事件。
const RESOLVED_POOL: Record<
  string,
  {
    lineName: string
    level: WorkshopEvent['level']
    text: string
    status: string
    minsAgo: number
    durMin?: number
  }[]
> = {
  'WS-01': [
    {
      lineName: '缸筒一线',
      level: 'downtime',
      text: 'DEV-CNC-07 立式加工中心 换刀异常短停 7 min',
      status: '已恢复',
      minsAgo: 152,
      durMin: 7,
    },
    {
      lineName: '精磨线',
      level: 'warn',
      text: 'DEV-GRD-01 数控外圆磨床 振动瞬时越限',
      status: '已恢复 · 复归正常',
      minsAgo: 205,
    },
  ],
  'WS-02': [
    {
      lineName: '前减装配一线',
      level: 'downtime',
      text: 'DEV-ASM-02 减振器装配台 送料卡滞短停 5 min',
      status: '已恢复',
      minsAgo: 118,
      durMin: 5,
    },
    {
      lineName: '阀系预装线',
      level: 'warn',
      text: 'DEV-ASM-11 阀系预装台 扭矩复检超差',
      status: '已恢复 · 已重新标定',
      minsAgo: 96,
    },
  ],
  'WS-03': [
    {
      lineName: '性能检测线',
      level: 'warn',
      text: 'DEV-TST-03 电液伺服试验台 阻尼力曲线漂移',
      status: '已恢复 · 标定件复测合格',
      minsAgo: 178,
    },
  ],
}

// 未恢复预警（与设备屏 ALARM_POOL 同一叙事：文本/线别/时距一致）
const WARN_POOL: Record<
  string,
  { lineName: string; text: string; minsAgo: number; status: string }[]
> = {
  'WS-01': [
    {
      lineName: '精磨线',
      text: 'DEV-GRD-02 数控外圆磨床 MK1332 振动接近上限',
      minsAgo: 112,
      status: '已确认 · 待砂轮动平衡',
    },
    {
      lineName: '缸筒一线',
      text: 'DEV-WLD-01 六轴焊接机器人 焊接电流波动偏大',
      minsAgo: 143,
      status: '观察中',
    },
  ],
  'WS-02': [
    {
      lineName: '后减装配二线',
      text: 'DEV-ASM-10 减振器装配台 压装力接近上限',
      minsAgo: 226,
      status: '计划传感器标定',
    },
  ],
  'WS-03': [
    {
      lineName: '包装线',
      text: 'DEV-AUX-07 螺杆空压机 SA-37 气源压力偏低',
      minsAgo: 168,
      status: '待保养 · 空滤芯堵塞',
    },
  ],
}

// 维修责任人（与设备屏 REPAIR_POOL 同源：L0 §5 设备部维修技师 EMP-043..046）
const REPAIR_ASSIGNEE: Record<string, string> = {
  'DEV-CNC-03 数控车床 CK6150': '张红梅',
  'DEV-CTG-02 电泳槽': '刘秀英',
  'DEV-AUX-06 冷冻式干燥机 CD-15': '陈国庆',
}

/** 当班累计曲线：计划匀速、实际带噪声单调爬升。**分线生成、逐点求和** ——
 *  每线独立权重曲线（报警线末段增量掉到 45%，停机拖累在自己的曲线上可见），
 *  车间总曲线 = Σ 各线逐点（构造性勾稽：总末点 = Σ 线末点 = KPI 大数字）。 */
function buildShiftCurve(
  lines: LineSummaryCard[],
  planTotal: number,
  elapsedMin: number,
  startHour: number,
): ShiftCurve {
  const k = Math.max(1, Math.ceil(elapsedMin / 60))
  const ts = Array.from({ length: k + 1 }, (_, i) => Math.min(i * 60, elapsedMin))
  const labels = ts.map((t, i) =>
    i === k ? clock(0) : `${p2((startHour + Math.floor(t / 60)) % 24)}:00`,
  )
  const byLine = lines.map((l) => {
    // 每小时段产出权重：±8% 噪声；报警线最后一段掉到 45%（急停拖累）
    const w: number[] = []
    for (let i = 1; i <= k; i++) {
      let wi = (ts[i] - ts[i - 1]) * (0.92 + Math.random() * 0.16)
      if (l.state === 'alarm' && i === k) wi *= 0.45
      w.push(wi)
    }
    const wSum = w.reduce((a, b) => a + b, 0)
    const data = [0]
    let acc = 0
    for (let i = 0; i < k; i++) {
      acc += w[i]
      data.push(Math.round((l.output.good * acc) / wSum))
    }
    data[k] = l.output.good
    return { lineId: l.id, name: l.name, state: l.state, data }
  })
  const actual = ts.map((_, i) => byLine.reduce((n, bl) => n + bl.data[i], 0))
  const plan = ts.map((t, i) => (i === k ? planTotal : Math.round((planTotal * t) / elapsedMin)))
  return { actual, plan, labels, byLine }
}

/** 近 30 天车间日产量：日计划 = 当班计划节奏 × 16h 有效工时（设定集 §1 双班 8+8），
 *  **周日停产保养不排产**（§1 标准工作日历）；
 *  末点 = 今日截至当前实际（与 KPI output.actual 精确勾稽，「今天还没过完」是真实的）。 */
function buildDaily30(planShift: number, actual: number, elapsedMin: number): Daily30 {
  const dayPlan = Math.max(100, Math.round((planShift / elapsedMin) * 60 * 16))
  const output: number[] = []
  const plan: number[] = []
  const labels: string[] = []
  const today = new Date()
  for (let i = 29; i >= 0; i--) {
    const d = new Date(today.getFullYear(), today.getMonth(), today.getDate() - i)
    labels.push(`${d.getMonth() + 1}/${d.getDate()}`)
    if (i === 0) {
      output.push(actual)
      plan.push(dayPlan)
      continue
    }
    const sunday = d.getDay() === 0
    const p = sunday ? 0 : dayPlan
    plan.push(p)
    output.push(
      sunday
        ? 0
        : clamp(jitter(Math.round(p * 0.96), Math.round(p * 0.06)), Math.round(p * 0.85), p),
    )
  }
  return { output, plan, labels }
}

/** /workshop/[id] 车间总览；scope 外或不存在的车间返回 null（越权防护）。 */
export function buildWorkshopBoard(
  workshopId: string,
  factoryId = DEFAULT_FACTORY_ID,
  workshopIds: string[] | 'all' = 'all',
): WorkshopBoard | null {
  const ws = workshopsByFactory(factoryId).find((w) => w.id === workshopId)
  if (!ws) return null
  if (workshopIds !== 'all' && !workshopIds.includes(workshopId)) return null

  // ① 产线卡同源：buildLineCards 全量后过滤本车间（保持红线置顶的全局排序）
  const lineIds = new Set(linesByWorkshop(workshopId).map((l) => l.id))
  const lines = buildLineCards(factoryId, workshopIds).filter((c) => lineIds.has(c.id))
  if (!lines.length) return null

  const state = composeWorkshopState(lines)
  const shift = shiftNow()
  const elapsed = Math.max(30, shift.elapsedMin) // 与产线卡 plan 推算同一钳位口径

  // ② 车间产出 = Σ 产线卡（精确同源，勾稽单测锁定）
  const actual = lines.reduce((n, l) => n + l.output.good, 0)
  const plan = lines.reduce((n, l) => n + l.output.plan, 0)
  const output = {
    actual,
    plan,
    achievement: plan > 0 ? Math.round((actual / plan) * 100) : 0,
  }
  const lineStates = {
    run: lines.filter((l) => l.state === 'run').length,
    attention: lines.filter((l) => l.state === 'attention').length,
    alarm: lines.filter((l) => l.state === 'alarm').length,
  }

  // ③ 设备画像（与设备屏同源）：计数 + 事件流素材
  const eq = buildEquipmentOverview(factoryId, workshopIds)
  const devs = eq.devices.filter((d) => d.workshopId === workshopId)
  const devices = { total: devs.length, run: 0, idle: 0, down: 0, alarm: 0, offline: 0 }
  for (const d of devs) devices[d.state] += 1

  // 事件流：急停（红）/ 停机待修（黄）/ 换型（按线归并）/ 失联；计划保养不进异常流
  const events: WorkshopEvent[] = []
  const changeoverByLine = new Map<string, string[]>()
  for (const d of devs) {
    if (d.state === 'alarm') {
      const who = REPAIR_ASSIGNEE[d.name]
      events.push({
        id: `EV-${d.id}-alarm`,
        time: clock(jitter(26, 6)),
        level: 'alarm',
        lineName: d.lineName,
        text: `${d.name} ${d.block ?? '设备报警'}`,
        status: who ? `已派工 · ${who}` : `未恢复 ${clamp(jitter(26, 6), 12, 45)} min`,
      })
    } else if (d.state === 'down') {
      const who = REPAIR_ASSIGNEE[d.name]
      events.push({
        id: `EV-${d.id}-down`,
        time: clock(jitter(96, 10)),
        level: 'downtime',
        lineName: d.lineName,
        text: `${d.name} ${d.block ?? '停机待修'}`,
        status: who ? `维修中 · ${who}` : '停机待修',
      })
    } else if (d.state === 'idle' && d.block?.startsWith('换型待机')) {
      const arr = changeoverByLine.get(d.lineName) ?? []
      arr.push(d.name)
      changeoverByLine.set(d.lineName, arr)
    } else if (d.state === 'offline') {
      events.push({
        id: `EV-${d.id}-off`,
        time: clock(jitter(75, 10)),
        level: 'info',
        lineName: d.lineName,
        text: `${d.name} 数据链路失联`,
        status: '采集通道排查中',
      })
    }
  }
  for (const [lineName, names] of changeoverByLine) {
    events.push({
      id: `EV-CO-${lineName}`,
      time: clock(jitter(48, 10)),
      level: 'info',
      lineName,
      text: `${names.join(' / ')} 换型待机`,
      status: '换型作业中',
    })
  }
  for (const [i, wPoolItem] of (WARN_POOL[workshopId] ?? []).entries()) {
    events.push({
      id: `EV-W${i}-${workshopId}`,
      time: clock(wPoolItem.minsAgo + jitter(2, 3)),
      level: 'warn',
      lineName: wPoolItem.lineName,
      text: wPoolItem.text,
      status: wPoolItem.status,
    })
  }

  // ④ 当班停机（含已恢复短停 —— 作战室口径是「当班累计」，不只看未恢复）；
  //    急停 + 待修 + 换型（按线计 1 次）+ 已恢复短停；计划保养不计（计划内非异常）。
  //    按线记账（dtByLine）供线级 OEE 可用率推算。
  let dtCount = 0
  let dtMin = 0
  const dtByLine = new Map<string, number>()
  const addDt = (lineName: string, min: number) => {
    dtCount += 1
    dtMin += min
    dtByLine.set(lineName, (dtByLine.get(lineName) ?? 0) + min)
  }
  for (const d of devs) {
    if (d.state === 'alarm') addDt(d.lineName, clamp(jitter(30, 8), 18, 45))
    else if (d.state === 'down') addDt(d.lineName, clamp(jitter(96, 10), 80, 120))
  }
  for (const [lineName] of changeoverByLine) addDt(lineName, clamp(jitter(38, 10), 25, 55))

  for (const [i, r] of (RESOLVED_POOL[workshopId] ?? []).entries()) {
    events.push({
      id: `EV-R${i}-${workshopId}`,
      time: clock(r.minsAgo + jitter(2, 3)),
      level: r.level,
      lineName: r.lineName,
      text: r.text,
      status: r.status,
      resolved: true,
    })
    if (r.level === 'downtime') addDt(r.lineName, r.durMin ?? 5)
  }
  const rank: Record<WorkshopEvent['level'], number> = { alarm: 0, downtime: 1, warn: 2, info: 3 }
  events.sort(
    (a, b) => rank[a.level] + (a.resolved ? 10 : 0) - (rank[b.level] + (b.resolved ? 10 : 0)),
  )
  const downtime = { count: dtCount, totalMin: dtMin }

  // ⑤ 齐套：与产线屏 kitting 同口径；缺料需求量 = 该线当前工单计划数同式（ceil(plan/100)×100）
  const shortLines = lines.filter((l) => KITTING_SHORT_LINES.has(l.id))
  const shortages: ShortageItem[] = shortLines.flatMap((l) => {
    const qtyPlan = Math.ceil(l.output.plan / 100) * 100
    return (SHORTAGE_SPECS[l.id] ?? []).map((s) => ({
      material: s.material,
      code: s.code,
      lineName: l.name,
      wo: l.currentWo ?? '—',
      requiredQty: qtyPlan,
      shortQty: Math.max(1, Math.round(qtyPlan * s.shortPct)),
      eta: s.etaText ?? `预计 ${clock(-(s.etaMin ?? 60))} 到料`,
    }))
  })
  const kitting = {
    rate: Math.round(((lines.length - shortLines.length) / lines.length) * 100),
    woActive: lines.length,
    woBlocked: shortLines.length + lineStates.alarm,
    shortages,
  }

  // ⑥ 质量：报废/返修沿产线屏 lineMetrics 同族口径（≈0.8% + 少量返修）；FPY = 良品/完工。
  //    NCR 直接从质量屏 mock 过滤本车间产线 —— 单号/缺陷/状态与 /quality 屏严格同一批
  //    （NCR-26-xxx，不再各屏手写一套编号）。
  let scrap = 0
  let rework = 0
  const fpyByLine = new Map<string, number>()
  for (const l of lines) {
    // 报废/返修与产线屏 lineMetrics 同族口径（设定集 §7：不合格 2.3%，报废 15% / 返工 60%）
    const s = clamp(
      Math.round(l.output.good * 0.0035) + clamp(jitter(0, 2), 0, 1),
      0,
      l.output.good,
    )
    const r = clamp(Math.round(l.output.good * 0.014), 0, l.output.good - s)
    scrap += s
    rework += r
    const doneL = l.output.good + s + r
    fpyByLine.set(l.id, doneL > 0 ? Math.round((l.output.good / doneL) * 100) : 100)
  }
  const done = actual + scrap + rework
  const fpy = done > 0 ? Math.round((actual / done) * 1000) / 10 : 100
  const wsLineIds = new Set(linesByWorkshop(workshopId).map((l) => l.id))
  const ncr: NcrItem[] = buildQualityBoard(factoryId)
    .ncrs.filter((r) => r.lineId && wsLineIds.has(r.lineId))
    .map((r) => ({
      code: r.code,
      lineName: r.source,
      text: r.defect,
      status: r.disposition ? `${r.statusLabel} · ${r.disposition}` : r.statusLabel,
    }))
  const quality = { scrap, rework, fpy, ncr }

  // ⑥b 车间效率（spec「设备 & OEE」）：A = 1 − 停机/(班时×线数)、P = 节拍达成
  //    （plan 加权，与产线屏 P=标准节拍/实际节拍同族）、Q = FPY；overall = A×P×Q。
  //    byLine 用各线自己的停机/节拍/FPY —— 报警线 OEE 垫底一眼可见。
  const oeeA = clamp(Math.round((1 - dtMin / (elapsed * lines.length)) * 100), 55, 100)
  const oeeP =
    plan > 0
      ? clamp(
          Math.round(
            lines.reduce(
              (n, l) => n + l.output.plan * (100 / (1 + Math.max(0, l.taktDeviationPct) / 100)),
              0,
            ) / plan,
          ),
          60,
          100,
        )
      : 100
  const oeeQ = Math.round(fpy)
  const byLine: LineOee[] = lines.map((l) => {
    const a = clamp(Math.round((1 - (dtByLine.get(l.name) ?? 0) / elapsed) * 100), 55, 100)
    const p = clamp(Math.round(100 / (1 + Math.max(0, l.taktDeviationPct) / 100)), 60, 100)
    const q = fpyByLine.get(l.id) ?? 98
    return { lineId: l.id, name: l.name, state: l.state, oee: Math.round((a * p * q) / 10000) }
  })
  const oee: WorkshopOee = {
    overall: Math.round((oeeA * oeeP * oeeQ) / 10000),
    availability: oeeA,
    performance: oeeP,
    quality: oeeQ,
    byLine,
  }

  // ⑦ 工单交付预警（引用产线卡同号工单，跨屏对得上；异常是例外）
  const woAlerts: WoAlert[] = []
  if (workshopId === 'WS-01') {
    // 活塞杆一线 DEV-CNC-03 振动超限停摆 → 当前工单临期（与工厂屏红卡同一叙事）
    const rod = lines.find((l) => l.id === 'LINE-WB-ROD-01')
    if (rod?.currentWo) {
      woAlerts.push({
        code: rod.currentWo,
        product: '活塞杆 φ22×420',
        lineName: rod.name,
        kind: 'dueSoon',
        dueText: `${fmtDur(clamp(jitter(210, 40), 150, 280))} 后到期`,
      })
    }
  }
  if (workshopId === 'WS-02') {
    // 前减装配二线弹簧二供切换缺料 → 排队单已超期
    woAlerts.push({
      code: woOf('LINE-WB-FA-02'),
      product: 'P1 平台前滑柱总成（右）',
      lineName: '前减装配二线',
      kind: 'overdue',
      dueText: `已超期 ${fmtDur(clamp(jitter(150, 30), 100, 200))}`,
    })
  }

  // ⑧ 班组（诚实口径：花名册/技能矩阵/交接 ✅；在岗/人效缺口不展示）
  // 设定集 §5：班组绑车间不绑工作中心，6 个班组 = 3 车间 × 早/中班；
  // headcountPlanned = 该班组在册（班组长 1 + 操作工 N），不是考勤在岗。
  const team = teamOf(workshopId, shift.name)
  const handover = HANDOVER[workshopId]
  const crew: CrewInfo = {
    teamName: team?.name ?? `${ws.shortName}班组`,
    leader: team?.leader ?? ws.managerName,
    headcountPlanned: team ? team.operators + 1 : 4,
    skillCoverage: clamp(jitter(SKILL_BASE[workshopId] ?? 92, 3), 80, 100),
    handoverIssues: handover?.issues ?? 0,
    handoverNote: handover?.note,
  }

  return {
    workshopId,
    workshopName: ws.name,
    managerName: ws.managerName,
    state,
    stateLabel: WS_STATE_LABELS[state],
    offlineDevices: devices.offline,
    shift,
    output,
    lines,
    lineStates,
    shiftCurve: buildShiftCurve(lines, plan, elapsed, shift.name.startsWith('早') ? 8 : 16),
    daily30: buildDaily30(plan, actual, elapsed),
    oee,
    devices,
    downtime,
    events,
    kitting,
    quality,
    woAlerts,
    crew,
  }
}
