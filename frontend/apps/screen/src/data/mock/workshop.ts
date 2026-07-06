// 车间总览 mock 聚合（MAN-315）：车间主任「当班作战室」——
// ① 产线区**直接复用** buildLineCards（与产线/设备屏同源：电芯线红灯 ⇔ 卷绕机 1# 报警），
//    车间产量/计划/达成 = Σ 本车间产线卡，数字精确同源（勾稽单测锁定）；
// ② 事件流从设备画像归并（急停/待修/换型/失联），计划保养不进异常流 ——
//    异常是例外，正常车间事件区为空（空态 = 健康）；
// ③ 齐套与产线屏 kitting 同口径（LN-ASSY-2 缺料，需求量 = 该线当前工单计划数同式）；
// ④ 人员区诚实口径：只给班组花名册/班次/交接遗留/技能覆盖（平台真实能力），
//    **不造**考勤在岗/人效（数据缺口，见 spec 人员维度铁律）。
// 🟠 车间维度聚合当前无真实端点（#570），接入后由 fetchers/workshop.ts 单点切换。
import type { LineState, LineSummaryCard } from '@/data/contracts/line'
import type {
  CrewInfo,
  NcrItem,
  ShiftCurve,
  ShortageItem,
  WoAlert,
  WorkshopBoard,
  WorkshopEvent,
} from '@/data/contracts/workshop'
import { buildEquipmentOverview } from './equipment'
import { clock, jitter, seq } from './fixtures'
import { buildLineCards, shiftNow } from './line'
import { linesByWorkshop, workshopsByFactory } from './masterdata'

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

// —— 班组花名册（✅ 平台真实能力：计划班组/组长/班次；人名为演示数据）——
const CREW_PROFILES: Record<string, { teamName: string; leader: string; skillBase: number }> = {
  'WS-STAMP': { teamName: '冲压一班', leader: '韩志刚', skillBase: 96 },
  'WS-WELD': { teamName: '焊装二班', leader: '郑卫东', skillBase: 94 },
  'WS-PAINT': { teamName: '涂装一班', leader: '罗成', skillBase: 92 },
  'WS-ASSY': { teamName: '总装三班', leader: '何建波', skillBase: 95 },
  'WS-BATTERY': { teamName: '电池一班', leader: '崔明亮', skillBase: 91 },
  'WS-INJECT': { teamName: '注塑一班', leader: '唐国栋', skillBase: 93 },
  'WS-MACH': { teamName: '机加一班', leader: '沈永康', skillBase: 97 },
}
const DEFAULT_CREW = { teamName: '生产一班', leader: '张伟', skillBase: 92 }

// 班次交接遗留（✅ 平台有交接记录；叙事与设备画像勾稽：夜班已留意的隐患当班应验）
const HANDOVER: Record<string, { issues: number; note: string }> = {
  'WS-BATTERY': { issues: 1, note: '夜班交接：卷绕机 1# 间歇异响，需重点观察' },
  'WS-ASSY': { issues: 1, note: '夜班交接：合装举升机液压渗油，已报保全跟进' },
}

// —— 线边缺料画像：与 mock/line.ts buildLineBoard 的 kitting 口径一致（仅 LN-ASSY-2 short）；
//    需求量按该线当前工单计划数派生（与产线屏 qtyPlan 同式，跨屏对得上）——
const KITTING_SHORT_LINES = new Set(['LN-ASSY-2'])
interface ShortageSpec {
  material: string
  code: string
  /** 缺口占当前工单计划数比例 */
  shortPct: number
  etaMin?: number
  etaText?: string
}
const SHORTAGE_SPECS: Record<string, ShortageSpec[]> = {
  'LN-ASSY-2': [
    { material: '线束总成', code: 'MAT-30512', shortPct: 0.12, etaMin: 85 },
    { material: '门内饰板（左前）', code: 'MAT-30587', shortPct: 0.08, etaMin: 45 },
    { material: '风挡玻璃总成', code: 'MAT-30443', shortPct: 0.04, etaText: '在途 · 待入库' },
  ],
}

// NCR 待办（✅ 平台有 NCR；异常是例外：仅报警/缺料叙事车间有待办）
const NCR_PROFILES: Record<string, NcrItem[]> = {
  'WS-BATTERY': [{ code: 'NCR-0871', lineName: '电芯线', text: '极片对齐度超差', status: '待处置' }],
  'WS-ASSY': [{ code: 'NCR-0864', lineName: '总装二线', text: '风挡密封条压伤', status: '返修中' }],
}

// 未恢复预警（与设备屏 ALARM_POOL 同一叙事：文本/线别/时距一致）
const WARN_POOL: Record<string, { lineName: string; text: string; minsAgo: number; status: string }[]> = {
  'WS-WELD': [{ lineName: '焊装一线', text: '焊接机器人 R02 伺服过载预警', minsAgo: 108, status: '已确认 · 待处理' }],
  'WS-BATTERY': [{ lineName: '电芯线', text: '注液机 注液量偏差预警', minsAgo: 137, status: '观察中' }],
  'WS-MACH': [{ lineName: '机加线', text: '加工中心 M01 刀具寿命预警', minsAgo: 240, status: '计划换刀' }],
}

// 维修责任人（与设备屏 REPAIR_POOL 同源：卷绕机→张建国、举升机→刘志远）
const REPAIR_ASSIGNEE: Record<string, string> = {
  '卷绕机 1#': '张建国',
  '合装举升机': '刘志远',
}

/** 当班累计曲线：计划匀速、实际带噪声单调爬升；报警车间末段增量走低（与线趋势口径一致）。
 *  末点强制等于当班累计（与 KPI 大数字精确勾稽）。 */
function buildShiftCurve(
  actualTotal: number,
  planTotal: number,
  elapsedMin: number,
  startHour: number,
  hasAlarm: boolean,
): ShiftCurve {
  const k = Math.max(1, Math.ceil(elapsedMin / 60))
  const ts = Array.from({ length: k + 1 }, (_, i) => Math.min(i * 60, elapsedMin))
  const labels = ts.map((t, i) =>
    i === k ? clock(0) : `${p2((startHour + Math.floor(t / 60)) % 24)}:00`,
  )
  // 每小时段产出权重：±8% 噪声；报警车间最后一段掉到 45%（停机拖累）
  const w: number[] = []
  for (let i = 1; i <= k; i++) {
    let wi = (ts[i] - ts[i - 1]) * (0.92 + Math.random() * 0.16)
    if (hasAlarm && i === k) wi *= 0.45
    w.push(wi)
  }
  const wSum = w.reduce((a, b) => a + b, 0)
  const actual = [0]
  const plan = [0]
  let acc = 0
  for (let i = 0; i < k; i++) {
    acc += w[i]
    actual.push(Math.round((actualTotal * acc) / wSum))
    plan.push(Math.round((planTotal * ts[i + 1]) / elapsedMin))
  }
  actual[k] = actualTotal
  plan[k] = planTotal
  return { actual, plan, labels }
}

/** /workshop/[id] 车间总览；scope 外或不存在的车间返回 null（越权防护）。 */
export function buildWorkshopBoard(
  workshopId: string,
  factoryId = 'F01',
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
    } else if (d.state === 'idle' && d.block === '换型待机') {
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
  const rank: Record<WorkshopEvent['level'], number> = { alarm: 0, downtime: 1, warn: 2, info: 3 }
  events.sort((a, b) => rank[a.level] - rank[b.level])

  // ④ 当班停机：急停 + 待修 + 换型（按线计 1 次）；计划保养不计（计划内非异常）
  let dtCount = 0
  let dtMin = 0
  for (const d of devs) {
    if (d.state === 'alarm') {
      dtCount += 1
      dtMin += clamp(jitter(30, 8), 18, 45)
    } else if (d.state === 'down') {
      dtCount += 1
      dtMin += clamp(jitter(96, 10), 80, 120)
    }
  }
  for (const _ of changeoverByLine) {
    dtCount += 1
    dtMin += clamp(jitter(38, 10), 25, 55)
  }
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

  // ⑥ 质量：报废/返修沿产线屏 lineMetrics 同族口径（≈0.8% + 少量返修）；FPY = 良品/完工
  let scrap = 0
  let rework = 0
  for (const l of lines) {
    scrap += clamp(Math.round(l.output.good * 0.008) + clamp(jitter(1, 2), 0, 2), 0, l.output.good)
    rework += clamp(jitter(2, 3), 0, 4)
  }
  const done = actual + scrap + rework
  const fpy = done > 0 ? Math.round((actual / done) * 1000) / 10 : 100
  const quality = { scrap, rework, fpy, ncr: NCR_PROFILES[workshopId] ?? [] }

  // ⑦ 工单交付预警（编号走 196x 段，不与产线屏当前工单 194x 冲突；异常是例外）
  const woAlerts: WoAlert[] = []
  if (workshopId === 'WS-ASSY') {
    // 总装一线合装举升机待修 → 后续排队单已超期（与工厂屏「超期风险」叙事一致）
    woAlerts.push({
      code: seq('WO', 1961),
      product: 'Model C 整车装配',
      lineName: '总装一线',
      kind: 'overdue',
      dueText: `已超期 ${fmtDur(clamp(jitter(150, 30), 100, 200))}`,
    })
  }
  if (workshopId === 'WS-BATTERY') {
    // 电芯线急停 → 当前工单临期（引用产线卡同号工单，跨屏对得上）
    const bat = lines.find((l) => l.id === 'LN-BAT-1')
    if (bat?.currentWo) {
      woAlerts.push({
        code: bat.currentWo,
        product: 'LFP-280Ah 电芯',
        lineName: bat.name,
        kind: 'dueSoon',
        dueText: `${fmtDur(clamp(jitter(210, 40), 150, 280))} 后到期`,
      })
    }
  }

  // ⑧ 班组（诚实口径：花名册/技能矩阵/交接 ✅；在岗/人效缺口不展示）
  const crewProfile = CREW_PROFILES[workshopId] ?? DEFAULT_CREW
  const handover = HANDOVER[workshopId]
  const crew: CrewInfo = {
    teamName: crewProfile.teamName,
    leader: crewProfile.leader,
    headcountPlanned: clamp(8 + linesByWorkshop(workshopId).length * 4, 8, 20),
    skillCoverage: clamp(jitter(crewProfile.skillBase, 3), 80, 100),
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
    shiftCurve: buildShiftCurve(
      actual,
      plan,
      elapsed,
      shift.name === '早班' ? 8 : 20,
      lineStates.alarm > 0,
    ),
    devices,
    downtime,
    events,
    kitting,
    quality,
    woAlerts,
    crew,
  }
}
