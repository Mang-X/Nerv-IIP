// 工厂总览 mock 聚合（MAN-314）：车间矩阵从 masterdata 真实汇总，健康度按集中规则合成；
// 数量类字段轮询微抖、健康度驱动项（告警/超期/停机画像）保持稳定，避免大屏状态色闪跳。
// 🟠 达成率/告警/停机等待 #570 真实聚合端点，接入后由 fetchers/factory.ts 单点切换。
import type {
  FactoryOverview,
  FeedItem,
  WorkshopCell,
  WorkshopHealth,
} from '@/data/contracts/factory'
import { clock, jitter, seq } from './fixtures'
import { linesByWorkshop, workshopsByFactory } from './masterdata'

function clamp(n: number, lo: number, hi: number): number {
  return Math.min(hi, Math.max(lo, n))
}

// —— 健康度合成（spec §二）：阈值集中一处，逐屏确认时可调 ——
export const HEALTH_RULES = {
  /** 达成率低于该值转黄 */
  rateYellowBelow: 80,
} as const

/** 红 = 未恢复 critical 告警 或 超期工单；黄 = Open 停机 或 达成率过低；否则绿。 */
export function composeHealth(
  w: Pick<WorkshopCell, 'critAlarms' | 'overdue' | 'openDowntime' | 'rate'>,
): WorkshopHealth {
  if (w.critAlarms > 0 || w.overdue > 0) return 'red'
  if (w.openDowntime > 0 || w.rate < HEALTH_RULES.rateYellowBelow) return 'yellow'
  return 'green'
}

const HEALTH_ORDER: Record<WorkshopHealth, number> = { red: 0, yellow: 1, green: 2 }

// 每车间演示画像（稳定，不随轮询跳变）；未列出的车间用 DEFAULT
interface Profile {
  overdue: number
  critAlarms: number
  openDowntime: number
  rateBase: number
  stateLabel: string
}
const PROFILES: Record<string, Profile> = {
  'WS-STAMP': { overdue: 0, critAlarms: 0, openDowntime: 0, rateBase: 96, stateLabel: '运行中' },
  'WS-WELD': { overdue: 0, critAlarms: 0, openDowntime: 0, rateBase: 94, stateLabel: '运行中' },
  'WS-PAINT': { overdue: 0, critAlarms: 0, openDowntime: 1, rateBase: 81, stateLabel: '换型待机' },
  'WS-ASSY': { overdue: 1, critAlarms: 0, openDowntime: 0, rateBase: 92, stateLabel: '超期风险' },
  'WS-BATTERY': { overdue: 0, critAlarms: 1, openDowntime: 1, rateBase: 74, stateLabel: '设备报警' },
  'WS-INJECT': { overdue: 0, critAlarms: 0, openDowntime: 0, rateBase: 90, stateLabel: '运行中' },
  'WS-MACH': { overdue: 0, critAlarms: 0, openDowntime: 1, rateBase: 84, stateLabel: '停机处理中' },
}
const DEFAULT_PROFILE: Profile = { overdue: 0, critAlarms: 0, openDowntime: 0, rateBase: 92, stateLabel: '运行中' }

export function buildFactoryOverview(
  factoryId = 'F01',
  workshopIds: string[] | 'all' = 'all',
): FactoryOverview {
  const factoryWorkshops = workshopsByFactory(factoryId)
  const visible =
    workshopIds === 'all' ? factoryWorkshops : factoryWorkshops.filter((w) => workshopIds.includes(w.id))

  const cells: WorkshopCell[] = visible.map((w) => {
    const p = PROFILES[w.id] ?? DEFAULT_PROFILE
    const lines = linesByWorkshop(w.id)
    const rate = clamp(jitter(p.rateBase, 3), 55, 100)
    const planQty = Math.max(400, lines.length * jitter(1900, 260))
    const actualQty = Math.round((planQty * rate) / 100)
    const cell = {
      id: w.id,
      name: w.name,
      manager: w.managerName,
      stateLabel: p.stateLabel,
      wip: clamp(jitter(lines.length * 3, 3), 1, 24),
      planQty,
      actualQty,
      rate,
      overdue: p.overdue,
      critAlarms: p.critAlarms,
      openDowntime: p.openDowntime,
    }
    return { ...cell, health: composeHealth(cell) }
  })
  // 红卡置顶（同健康度保持 masterdata 顺序）
  cells.sort((a, b) => HEALTH_ORDER[a.health] - HEALTH_ORDER[b.health])

  const planSum = cells.reduce((n, c) => n + c.planQty, 0)
  const actualSum = cells.reduce((n, c) => n + c.actualQty, 0)
  const criticalAlarms = cells.reduce((n, c) => n + c.critAlarms, 0)
  const kpis = {
    achievement: planSum > 0 ? clamp(Math.round((actualSum / planSum) * 100), 0, 100) : 0, // 🟠 待 #570
    wipOrders: cells.reduce((n, c) => n + c.wip, 0),
    riskOrders: cells.reduce((n, c) => n + c.overdue, 0) + clamp(jitter(1, 2), 0, 2), // 超期 + 临期风险 🟠
    openAlarms: criticalAlarms + clamp(jitter(3, 2), 1, 6),
    criticalAlarms,
    openDowntime: cells.reduce((n, c) => n + c.openDowntime, 0),
    openNcr: clamp(jitter(1, 2), 0, 3),
  }

  const avail = +(80 + Math.random() * 5).toFixed(1)
  const oee = [
    { label: '可用率', value: avail },
    { label: '性能率', value: jitter(92, 4) }, // 🟠 占位值，无真实端点，待 #570
    { label: '良品率', value: +(97 + Math.random() * 2).toFixed(1) }, // 🟠 占位值，无真实端点，待 #570
  ]

  // —— 实时流：从可见车间生成，scope 收窄后流内容跟着收窄 ——
  const alarms: FeedItem[] = []
  const downtimes: FeedItem[] = []
  let ai = 0
  let di = 0
  for (const c of cells) {
    const lines = linesByWorkshop(c.id)
    const lineName = lines[0]?.name ?? c.name
    if (c.critAlarms > 0) {
      alarms.push({
        id: seq('AL', 2400 - ai),
        level: 'critical',
        text: `${c.name} ${lineName}主机 急停触发`,
        time: clock(jitter(4 + ai * 6, 3)),
      })
      ai++
    }
    if (c.overdue > 0) {
      alarms.push({
        id: seq('AL', 2400 - ai),
        level: 'warning',
        text: `${c.name} ${seq('WO', 1930 + ai)} 交付超期`,
        time: clock(jitter(9 + ai * 6, 4)),
      })
      ai++
    }
    if (c.openDowntime > 0) {
      downtimes.push({
        id: seq('DT', 860 - di),
        level: c.critAlarms > 0 ? 'critical' : 'warning',
        text: `${c.name} ${lineName} ${c.critAlarms > 0 ? '故障停机' : '换型停机'} ${jitter(24, 14)} min`,
        time: clock(jitter(12 + di * 9, 5)),
      })
      di++
    }
  }
  // 常规提醒流（每车间轮转），保证流有内容且全部真实命名
  for (const [i, c] of cells.entries()) {
    const lines = linesByWorkshop(c.id)
    const lineName = lines[i % Math.max(1, lines.length)]?.name ?? c.name
    alarms.push({
      id: seq('AL', 2380 - i),
      level: i % 3 === 0 ? 'warning' : 'info',
      text:
        i % 3 === 0
          ? `${c.name} ${lineName} 节拍低于目标`
          : i % 3 === 1
            ? `${c.name} 物料齐套校验通过`
            : `${c.name} ${lineName} 完工上报 ${seq('WO', 1900 + i)}`,
      time: clock(jitter(16 + i * 8, 5)),
    })
    if (downtimes.length < 4) {
      downtimes.push({
        id: seq('DT', 840 - i),
        level: 'info',
        text: `${c.name} ${lineName} 计划保养完成 ${jitter(18, 8)} min`,
        time: clock(jitter(40 + i * 12, 8)),
      })
    }
  }

  return { factoryId, kpis, workshops: cells, oee, alarms, downtimes }
}
