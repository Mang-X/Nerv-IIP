// 产线监控 mock 聚合（MAN-316）：真实业务画像前置 ——
// ① 产线状态从设备画像**真实归并**（buildEquipmentOverview 同源：设备屏卷绕机
//    报警 ⇔ 产线屏电芯线红灯），断线设备计入失联角标（防假绿）；
// ② 当班产量按**标准节拍反推**（非拍数字），良品/报废/返修与计划勾稽；
// ③ 班次剩余按真实时钟推算；④ 横幅只在有事时存在（异常是例外）。
// 🟠 产量/节拍/达成待 #570 真实端点，接入后由 fetchers/line.ts 单点切换。
import type { DeviceCell } from '@/data/contracts/equipment'
import type { AndonCall, CurrentWo, LineBoard, LineState, LineSummaryCard } from '@/data/contracts/line'
import { buildEquipmentOverview, paramSeriesFor } from './equipment'
import { clock, jitter, seq } from './fixtures'
import { LINES, WORKSHOPS } from './masterdata'

// 线长名池（按产线序稳定取名） 🟡
const LINE_LEADERS = ['王强', '李敏', '周斌', '刘洋', '陈静', '赵磊', '杨帆', '徐娜', '孙鹏', '高翔', '马丽']

function clamp(n: number, lo: number, hi: number): number {
  return Math.min(hi, Math.max(lo, n))
}

// —— 线型工艺档案：标准节拍（s/件）+ 工序路线 + 在制产品（真实业务画像）——
interface LineProfile {
  taktSec: number
  steps: string[]
  product: string
  doingIdx: number
}
const LINE_PROFILES: Record<string, LineProfile> = {
  'LN-STAMP-1': { taktSec: 9, steps: ['上料', '冲压成形', '在线检测', '码垛'], product: 'Model C 左侧围外板', doingIdx: 1 },
  'LN-STAMP-2': { taktSec: 11, steps: ['上料', '冲压成形', '在线检测', '码垛'], product: '发动机舱盖内板', doingIdx: 1 },
  'LN-STAMP-3': { taktSec: 8, steps: ['落料', '冲压成形', '在线检测', '码垛'], product: '车门内板', doingIdx: 1 },
  'LN-WELD-1': { taktSec: 66, steps: ['装夹定位', '机器人焊接', '涂胶', '下件检测'], product: 'Model C 白车身总成', doingIdx: 1 },
  'LN-WELD-2': { taktSec: 72, steps: ['装夹定位', '激光焊接', '下件检测'], product: '后地板总成', doingIdx: 1 },
  'LN-WELD-3': { taktSec: 69, steps: ['装夹定位', '螺柱焊', '机器人焊接', '视觉检测'], product: '前纵梁总成', doingIdx: 2 },
  'LN-PAINT-1': { taktSec: 95, steps: ['前处理', '电泳', '中涂', '面漆', '流平烘干'], product: 'Model C 车身涂装', doingIdx: 2 },
  'LN-PAINT-2': { taktSec: 102, steps: ['遮蔽', '面漆喷涂', '烘干', '抛光'], product: '双色车顶面漆', doingIdx: 1 },
  'LN-ASSY-1': { taktSec: 78, steps: ['内饰装配', '底盘合装', '油液加注', '下线检测'], product: 'Model C 整车装配', doingIdx: 1 },
  'LN-ASSY-2': { taktSec: 84, steps: ['内饰装配', '风挡涂胶', '四轮定位', '下线检测'], product: 'Model C 整车装配', doingIdx: 0 },
  'LN-ASSY-3': { taktSec: 80, steps: ['内饰装配', '玻璃安装', '注油', '路试'], product: 'Model D 整车装配', doingIdx: 1 },
  'LN-BAT-1': { taktSec: 13, steps: ['极片上料', '卷绕', '注液', '化成', '分容'], product: 'LFP-280Ah 电芯', doingIdx: 1 },
  'LN-BAT-2': { taktSec: 48, steps: ['模组上件', '堆叠', '气密检测', 'EOL 测试'], product: '标准电池包 PACK-96s', doingIdx: 1 },
  'LN-INJ-1': { taktSec: 35, steps: ['原料干燥', '注塑成形', '取件', '去毛边'], product: '前保险杠骨架', doingIdx: 1 },
  'LN-INJ-2': { taktSec: 40, steps: ['混料', '注塑成形', '取件', '检验'], product: '仪表板本体', doingIdx: 1 },
  'LN-MACH-1': { taktSec: 210, steps: ['粗加工', '精加工', '清洗', '三坐标检测'], product: '电机壳体 EM-3', doingIdx: 1 },
}
const DEFAULT_LINE_PROFILE: LineProfile = { taktSec: 60, steps: ['上料', '加工', '检测'], product: '通用件', doingIdx: 1 }

const STATE_LABELS: Record<LineState, string> = {
  run: '正常作业',
  attention: '需关注',
  alarm: '设备报警',
}

/** 状态归并（纯函数）：任一设备报警 → 红；停机/待机 → 黄；否则绿。断线不改灯，走失联角标。 */
export function composeLineState(devices: Pick<DeviceCell, 'state'>[]): LineState {
  if (devices.some((d) => d.state === 'alarm')) return 'alarm'
  if (devices.some((d) => d.state === 'down' || d.state === 'idle')) return 'attention'
  return 'run'
}

/** 当班（早班 08:00–20:00 / 夜班 20:00–08:00），按真实时钟推算已过/剩余。 */
export function shiftNow(now = new Date()): {
  name: string
  range: string
  remainingMin: number
  elapsedMin: number
} {
  const minOfDay = now.getHours() * 60 + now.getMinutes()
  const day = minOfDay >= 480 && minOfDay < 1200
  const elapsed = day ? minOfDay - 480 : minOfDay >= 1200 ? minOfDay - 1200 : minOfDay + 240
  return {
    name: day ? '早班' : '夜班',
    range: day ? '08:00–20:00' : '20:00–08:00',
    remainingMin: 720 - elapsed,
    elapsedMin: elapsed,
  }
}

/** 单线核心指标（选择器卡与单线屏共用，保证两处数字一致的口径） */
function lineMetrics(lineId: string, state: LineState, elapsedMin: number) {
  const p = LINE_PROFILES[lineId] ?? DEFAULT_LINE_PROFILE
  // 达成率：报警线明显掉、关注线小掉（🟠 待 #570）
  const achievement =
    state === 'alarm'
      ? clamp(jitter(78, 6), 68, 88)
      : state === 'attention'
        ? clamp(jitter(88, 5), 80, 95)
        : clamp(jitter(96, 4), 90, 100)
  // 节拍：落后为正（红），报警线显著落后
  const deviationPct =
    state === 'alarm'
      ? clamp(jitter(18, 6), 10, 28)
      : state === 'attention'
        ? clamp(jitter(8, 5), 2, 15)
        : clamp(jitter(0, 8), -6, 6)
  const actualSec = +(p.taktSec * (1 + deviationPct / 100)).toFixed(1)
  // 当班计划按标准节拍反推（单流简化）
  const plan = Math.max(1, Math.floor((elapsedMin * 60) / p.taktSec))
  const total = Math.round((plan * achievement) / 100)
  const scrap = clamp(Math.round(total * 0.008) + clamp(jitter(1, 2), 0, 2), 0, total)
  const rework = clamp(clamp(jitter(2, 3), 0, 4), 0, total - scrap)
  const good = total - scrap - rework
  return { profile: p, achievement, deviationPct, actualSec, plan, good, scrap, rework }
}

/** 小时产量趋势（近 12h）：围绕节拍产能波动，报警线尾部明显走低（卡与单线屏共用口径）。 */
function hourlyOf(taktSec: number, state: LineState): number[] {
  const perHour = Math.round(3600 / taktSec)
  return Array.from({ length: 12 }, (_, i) => {
    const base = state === 'alarm' && i >= 10 ? perHour * 0.4 : perHour
    return Math.max(0, Math.round(base + ((Math.random() - 0.5) * perHour) / 4))
  })
}

/** 近 12 小时的整点标签（趋势图悬停用） */
function hourLabelsNow(now = new Date()): string[] {
  const h = now.getHours()
  return Array.from({ length: 12 }, (_, i) => `${String((h - 11 + i + 24) % 24).padStart(2, '0')}:00`)
}

/** 该线一句话异常（卡片用；有事才有） */
function lineAlert(devices: DeviceCell[]): string | undefined {
  const alarm = devices.find((d) => d.state === 'alarm')
  if (alarm) return `${alarm.name} ${alarm.block ?? '报警'}`
  const down = devices.find((d) => d.state === 'down')
  if (down) return `${down.name} 停机待修`
  const idle = devices.find((d) => d.state === 'idle' && d.block)
  if (idle) return idle.block
  return undefined
}

/** /line 选择器：迷你监控卡（红线置顶，其余保持产线原序）。
 *  visibleIds = 视野内产线集：状态/产量等标量对全部产线计算（汇总带需要），
 *  仅**小时趋势序列**（渲染才用的流式数据）对视野内产线生成 —— 视野外停止
 *  产生趋势数据，对齐真实端点按可见行订阅时序序列。 */
export function buildLineCards(
  factoryId = 'F01',
  workshopIds: string[] | 'all' = 'all',
  visibleIds?: string[],
): LineSummaryCard[] {
  const eq = buildEquipmentOverview(factoryId, workshopIds)
  const byLine = new Map<string, DeviceCell[]>()
  for (const d of eq.devices) {
    const arr = byLine.get(d.lineId) ?? []
    arr.push(d)
    byLine.set(d.lineId, arr)
  }
  const want = visibleIds ? new Set(visibleIds) : null
  const { elapsedMin } = shiftNow()
  const cards: LineSummaryCard[] = []
  for (const line of LINES) {
    const devices = byLine.get(line.id)
    if (!devices?.length) continue
    const state = composeLineState(devices)
    const m = lineMetrics(line.id, state, Math.max(30, elapsedMin))
    cards.push({
      id: line.id,
      name: line.name,
      workshopName: WORKSHOPS.find((w) => w.id === line.workshopId)?.name ?? line.workshopId,
      state,
      stateLabel: STATE_LABELS[state],
      offlineDevices: devices.filter((d) => d.state === 'offline').length,
      achievement: m.achievement,
      taktDeviationPct: m.deviationPct,
      output: { good: m.good, plan: m.plan },
      deviceDots: devices.map((d) => d.state),
      // 视野外不生成趋势序列（渲染才需要），空数组
      hourly: want && !want.has(line.id) ? [] : hourlyOf(m.profile.taktSec, state),
      currentWo: seq('WO', 1940 + LINES.indexOf(line)),
      alert: lineAlert(devices),
    })
  }
  const rank: Record<LineState, number> = { alarm: 0, attention: 1, run: 2 }
  return cards.sort((a, b) => rank[a.state] - rank[b.state])
}

/** /line/[id] 单线大屏；scope 外或不存在的线返回 null。 */
export function buildLineBoard(
  lineId: string,
  factoryId = 'F01',
  workshopIds: string[] | 'all' = 'all',
): LineBoard | null {
  const line = LINES.find((l) => l.id === lineId)
  if (!line) return null
  const eq = buildEquipmentOverview(factoryId, workshopIds)
  const devices = eq.devices.filter((d) => d.lineId === lineId)
  if (!devices.length) return null // scope 外（越权线）

  const state = composeLineState(devices)
  const shift = shiftNow()
  const elapsed = Math.max(30, shift.elapsedMin)
  const m = lineMetrics(lineId, state, elapsed)

  // 横幅：只在报警/停机时存在（异常是例外）
  const alarmDev = devices.find((d) => d.state === 'alarm')
  const downDev = devices.find((d) => d.state === 'down')
  const banner = alarmDev
    ? { level: 'alarm' as const, text: `${alarmDev.name} ${alarmDev.block ?? '设备报警'}`, since: clock(jitter(26, 6)) }
    : downDev
      ? { level: 'downtime' as const, text: `${downDev.name} ${downDev.block ?? '停机'}`, since: clock(jitter(48, 8)) }
      : undefined

  const hourly = hourlyOf(m.profile.taktSec, state)
  const planPerHour = Math.round(3600 / m.profile.taktSec)

  // 近 30 天日产量（两班 20h 产能为基准；周日排产低谷 —— 真实工厂节奏）🟡
  const daily30 = (() => {
    const dayCap = planPerHour * 20
    const output: number[] = []
    const plan: number[] = []
    const labels: string[] = []
    const today = new Date()
    for (let i = 29; i >= 0; i--) {
      const d = new Date(today.getFullYear(), today.getMonth(), today.getDate() - i)
      labels.push(`${d.getMonth() + 1}/${d.getDate()}`)
      const sunday = d.getDay() === 0
      const dayPlan = Math.round(sunday ? dayCap * 0.3 : dayCap)
      plan.push(dayPlan)
      output.push(Math.max(0, Math.round(dayPlan * (0.86 + Math.random() * 0.12))))
    }
    return { output, plan, labels }
  })()

  // 一次合格率 FPY：良品 / 完工（勾稽口径）
  const total = m.good + m.scrap + m.rework
  const fpy = total > 0 ? Math.round((m.good / total) * 1000) / 10 : 100

  // 当班停机统计：报警线多、关注线少、正常线偶发（异常是例外）
  const downtime =
    state === 'alarm'
      ? { count: clamp(jitter(2, 1), 1, 3), totalMin: clamp(jitter(32, 10), 18, 55) }
      : state === 'attention'
        ? { count: 1, totalMin: clamp(jitter(14, 6), 6, 25) }
        : { count: 0, totalMin: 0 }

  // 当班班组：线长（名池稳定取）+ 在岗人数
  const crew = {
    leader: LINE_LEADERS[LINES.indexOf(line) % LINE_LEADERS.length],
    operators: clamp(jitter(devices.length + 2, 3), 4, 14),
  }

  // 产线 OEE（班内推算 🟡 待 #570 校准）：可用率=停机推 / 性能率=节拍推 / 良品率=FPY
  const availability =
    elapsed > 0 ? clamp(Math.round(((elapsed - downtime.totalMin) / elapsed) * 100), 0, 100) : 100
  const performance = clamp(Math.round((m.profile.taktSec / m.actualSec) * 100), 0, 100)
  const oee = {
    overall: Math.round((availability * performance * fpy) / 10000),
    availability,
    performance,
    quality: fpy,
  }

  // 近 24h 每小时 OEE（热力图）：报警线近 3h 低谷、关注线近 4h 走弱
  const hourlyOee = Array.from({ length: 24 }, (_, i) => {
    if (state === 'alarm' && i >= 21) return clamp(jitter(42, 12), 25, 58)
    if (state === 'attention' && i >= 20) return clamp(jitter(68, 8), 55, 78)
    return clamp(jitter(86, 8), 72, 96)
  })

  // 安灯呼叫：报警/停机线才有记录（闭环 待 MAN-322）
  const doingStation = `${m.profile.steps[m.profile.doingIdx]}工位`
  const andon: AndonCall[] = alarmDev
    ? [{ time: clock(jitter(26, 6)), station: doingStation, type: '设备类', response: '张建国', state: '响应中' }]
    : downDev
      ? [{ time: clock(jitter(48, 8)), station: doingStation, type: '维修类', response: '刘志远', state: '响应中' }]
      : []

  const wo: CurrentWo = {
    code: seq('WO', 1940 + LINES.indexOf(line)),
    product: m.profile.product,
    qtyPlan: Math.ceil(m.plan / 100) * 100,
    qtyDone: m.good,
    wip: clamp(jitter(devices.length * 2, 4), 2, 40),
    dueInMin: clamp(jitter(300, 150), 60, 600),
    steps: m.profile.steps.map((name, i) => ({
      name,
      state: i < m.profile.doingIdx ? ('done' as const) : i === m.profile.doingIdx ? ('doing' as const) : ('todo' as const),
    })),
    kitting: lineId === 'LN-ASSY-2' ? 'short' : 'ok', // 🟡 线边齐套（单工单）
  }

  return {
    lineId,
    lineName: line.name,
    workshopName: WORKSHOPS.find((w) => w.id === line.workshopId)?.name ?? line.workshopId,
    state,
    stateLabel: STATE_LABELS[state],
    offlineDevices: devices.filter((d) => d.state === 'offline').length,
    banner,
    shift,
    crew,
    output: { good: m.good, scrap: m.scrap, rework: m.rework, plan: m.plan, achievement: m.achievement },
    fpy,
    downtime,
    takt: { standardSec: m.profile.taktSec, actualSec: m.actualSec, deviationPct: m.deviationPct },
    oee,
    hourlyOee,
    hourly,
    hourLabels: hourLabelsNow(),
    planPerHour,
    daily30,
    wo,
    andon,
    devices: devices.map((d) => ({
      id: d.id,
      name: d.name,
      state: d.state,
      stateLabel: d.stateLabel,
      param: d.params[0] ? `${d.params[0].label} ${d.params[0].value}` : undefined,
      params: paramSeriesFor(d.name, d.state),
    })),
  }
}
