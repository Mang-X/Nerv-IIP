// 产线监控 mock 聚合（MAN-316）：真实业务画像前置 ——
// ① 产线状态从设备画像**真实归并**（buildEquipmentOverview 同源：设备屏 DEV-CNC-03
//    振动超限 ⇔ 产线屏活塞杆一线红灯），断线设备计入失联角标（防假绿）；
// ② 当班产量按**标准节拍反推**（节拍表见 mock/world.ts，附完整规模推导）；
// ③ 班次剩余按真实时钟推算（早班 08:00–16:00 / 中班 16:00–24:00，设定集 §1 双班制）；
// ④ 横幅只在有事时存在（异常是例外）。
// 🟠 产量/节拍/达成待 #570 真实端点，接入后由 fetchers/line.ts 单点切换。
import type { DeviceCell } from '@/data/contracts/equipment'
import type {
  AndonCall,
  CurrentWo,
  LineBoard,
  LineState,
  LineSummaryCard,
} from '@/data/contracts/line'
import { buildEquipmentOverview, paramSeriesFor } from './equipment'
import { clock, jitter } from './fixtures'
import { DEFAULT_FACTORY_ID, LINES, WORKSHOPS } from './masterdata'
import { lineProfileOf, shiftNow, teamOf, woOf } from './world'

export { shiftNow }

function clamp(n: number, lo: number, hi: number): number {
  return Math.min(hi, Math.max(lo, n))
}

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

/** 单线核心指标（选择器卡与单线屏共用，保证两处数字一致的口径） */
function lineMetrics(lineId: string, state: LineState, elapsedMin: number) {
  const p = lineProfileOf(lineId)
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
  // 报废率：设定集 §7 全厂不合格 2.3%，其中报废处置占 15% → 件报废率 ≈0.35%
  const scrap = clamp(Math.round(total * 0.0035) + clamp(jitter(0, 2), 0, 1), 0, total)
  // 返工占不合格 60% → ≈1.4%
  const rework = clamp(Math.round(total * 0.014), 0, total - scrap)
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
  return Array.from(
    { length: 12 },
    (_, i) => `${String((h - 11 + i + 24) % 24).padStart(2, '0')}:00`,
  )
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
  factoryId = DEFAULT_FACTORY_ID,
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
      workshopName: WORKSHOPS.find((w) => w.id === line.workshopId)?.shortName ?? line.workshopId,
      state,
      stateLabel: STATE_LABELS[state],
      offlineDevices: devices.filter((d) => d.state === 'offline').length,
      achievement: m.achievement,
      taktDeviationPct: m.deviationPct,
      output: { good: m.good, plan: m.plan },
      deviceDots: devices.map((d) => d.state),
      // 视野外不生成趋势序列（渲染才需要），空数组
      hourly: want && !want.has(line.id) ? [] : hourlyOf(m.profile.taktSec, state),
      currentWo: woOf(line.id),
      alert: lineAlert(devices),
    })
  }
  const rank: Record<LineState, number> = { alarm: 0, attention: 1, run: 2 }
  return cards.sort((a, b) => rank[a.state] - rank[b.state])
}

/** /line/[id] 单线大屏；scope 外或不存在的线返回 null。 */
export function buildLineBoard(
  lineId: string,
  factoryId = DEFAULT_FACTORY_ID,
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
    ? {
        level: 'alarm' as const,
        text: `${alarmDev.name} ${alarmDev.block ?? '设备报警'}`,
        since: clock(jitter(26, 6)),
      }
    : downDev
      ? {
          level: 'downtime' as const,
          text: `${downDev.name} ${downDev.block ?? '停机'}`,
          since: clock(jitter(96, 8)),
        }
      : undefined

  const hourly = hourlyOf(m.profile.taktSec, state)
  const planPerHour = Math.round(3600 / m.profile.taktSec)

  // 近 30 天日产量（双班 16h 产能为基准；周日停产 —— 设定集 §1 标准日历）
  const daily30 = (() => {
    const dayCap = planPerHour * 16
    const output: number[] = []
    const plan: number[] = []
    const labels: string[] = []
    const today = new Date()
    for (let i = 29; i >= 0; i--) {
      const d = new Date(today.getFullYear(), today.getMonth(), today.getDate() - i)
      labels.push(`${d.getMonth() + 1}/${d.getDate()}`)
      // 周日停产保养：不排产，只留少量保养工时（设定集 §1）
      const sunday = d.getDay() === 0
      const dayPlan = sunday ? 0 : dayCap
      plan.push(dayPlan)
      output.push(sunday ? 0 : Math.max(0, Math.round(dayPlan * (0.86 + Math.random() * 0.12))))
    }
    return { output, plan, labels }
  })()

  // 一次合格率 FPY：良品 / 完工（勾稽口径）
  const total = m.good + m.scrap + m.rework
  const fpy = total > 0 ? Math.round((m.good / total) * 1000) / 10 : 100

  // 当班停机统计：报警线多、关注线少、正常线偶发（异常是例外）
  // 停机时长必须封顶在**已过班时长**内：班初（如 16:10）报警时，26 min 的未恢复
  // 时长里有一部分落在上一班，算进当班会让可用率跌到个位数（看着像坏了）。
  const dtCap = Math.max(1, Math.floor(elapsed * 0.6))
  const downtime =
    state === 'alarm'
      ? {
          count: clamp(jitter(2, 1), 1, 3),
          totalMin: Math.min(dtCap, clamp(jitter(32, 10), 18, 55)),
        }
      : state === 'attention'
        ? { count: 1, totalMin: Math.min(dtCap, clamp(jitter(14, 6), 6, 25)) }
        : { count: 0, totalMin: 0 }

  // 当班班组（设定集 §5：**班组是车间级**，6 班组 = 3 车间 × 早/中班；线长这个岗位
  // 在宁沪减振并不存在，所以这里给的是该车间当班班组长）。
  // 在岗人数：全厂 19 名操作工分 6 组，小厂本就一人多机 —— 线级数字是「巡检覆盖」
  // 口径（同一人可覆盖同车间多条自动线），故 Σ 线级 ≥ 车间在册，不做强制配平。
  const team = teamOf(line.workshopId, shift.name)
  const crew = {
    leader: team?.leader ?? WORKSHOPS.find((w) => w.id === line.workshopId)?.managerName ?? '—',
    operators: clamp(Math.ceil(devices.length / 3), 1, 4),
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

  // 安灯呼叫：报警/停机线才有记录（闭环 待 MAN-322）；响应人取 L0 §5 维修技师
  const doingStation = `${m.profile.steps[m.profile.keyIdx]}工位`
  const andon: AndonCall[] = alarmDev
    ? [
        {
          time: clock(jitter(26, 6)),
          station: doingStation,
          type: '设备类',
          response: '张红梅',
          state: '响应中',
        },
      ]
    : downDev
      ? [
          {
            time: clock(jitter(96, 8)),
            station: doingStation,
            type: '维修类',
            response: '刘秀英',
            state: '响应中',
          },
        ]
      : []

  // 工序流分布（流水线语义）：各工序同时在产，累计完成沿流向递减 ——
  // 末道 = 工单完成数（下线口径），逆流向逐段加上段间在制；
  // 关键工序位：报警线停摆（红）、关注线节拍瓶颈（黄），正常线不制造假瓶颈
  const stations = (() => {
    const names = m.profile.steps
    const out: { name: string; done: number; state: 'run' | 'bottleneck' | 'blocked' }[] = []
    let acc = m.good
    for (let i = names.length - 1; i >= 0; i--) {
      out.unshift({
        name: names[i],
        done: acc,
        state:
          i === m.profile.keyIdx
            ? state === 'alarm'
              ? 'blocked'
              : state === 'attention'
                ? 'bottleneck'
                : 'run'
            : 'run',
      })
      // 段间在制：约 1%–2.5% 的量滞留在工序间
      acc += Math.max(2, Math.round(m.good * (0.01 + jitter(8, 6) / 1000)))
    }
    return out
  })()

  const wo: CurrentWo = {
    code: woOf(line.id),
    product: m.profile.product,
    qtyPlan: Math.ceil(m.plan / 100) * 100,
    qtyDone: m.good,
    // 在制 WIP = 首道完成 − 末道完成（工序间滞留总量，与工序流勾稽）
    wip: stations[0].done - m.good,
    dueInMin: clamp(jitter(300, 150), 60, 600),
    stations,
    // 🟡 线边齐套（单工单）：前减装配二线弹簧二供切换期缺料，与车间屏同口径
    kitting: lineId === 'LINE-WB-FA-02' ? 'short' : 'ok',
  }

  return {
    lineId,
    lineName: line.name,
    workshopName: WORKSHOPS.find((w) => w.id === line.workshopId)?.shortName ?? line.workshopId,
    state,
    stateLabel: STATE_LABELS[state],
    offlineDevices: devices.filter((d) => d.state === 'offline').length,
    banner,
    shift,
    crew,
    output: {
      good: m.good,
      scrap: m.scrap,
      rework: m.rework,
      plan: m.plan,
      achievement: m.achievement,
    },
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
      params: paramSeriesFor(d.code, d.state),
    })),
  }
}
