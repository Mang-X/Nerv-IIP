// 工厂总览 mock 聚合（MAN-314）：车间矩阵从 masterdata 真实汇总，健康度按集中规则合成；
// 数量类字段轮询微抖、健康度驱动项（告警/超期/停机画像）保持稳定，避免大屏状态色闪跳。
// 🟠 达成率/告警/停机等待 #570 真实聚合端点，接入后由 fetchers/factory.ts 单点切换。
//
// ⚠️ 产量口径（详见 mock/world.ts 的规模推导）：14 条产线是**串行价值链**，
// 同一个物理件在机加/装配/表面三段各计一次。因此
//   · 车间数字 = 该车间**向下游交付**的件数（车间内部中间件不重复计）；
//   · 全厂「今日成品下线」= 末道表面与包装车间（包装线）的交付数，**不是 Σ 车间**。
// 把三段加总会得到「46 台设备日产上万件」这种整车厂量级的假数字。
import type {
  FactoryOverview,
  FeedItem,
  WorkshopCell,
  WorkshopHealth,
} from '@/data/contracts/factory'
import { clock, jitter, seq } from './fixtures'
import { buildQualityBoard } from './quality'
import { DEFAULT_FACTORY_ID, linesByWorkshop, workshopsByFactory } from './masterdata'
import { dayProgress, FINAL_WORKSHOP_ID, lineProfileOf, shiftPlanOf, woOf } from './world'

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

// 每车间演示画像（稳定，不随轮询跳变）—— **与设备屏 DEVICE_PROFILES 逐条勾稽**，
// 三块屏看到的必须是同一起事：
//   · 机加车间   DEV-CNC-03 振动超限（critical）      → 红
//   · 装配车间   DEV-ASM-05/06 换型待机（计划内停机） → 黄
//   · 表面与包装 DEV-CTG-02 循环泵渗漏停机待修        → 黄
// 一个班次里「一起报警 + 一次换型 + 一台待修」正是正常工厂日的密度：
// 46 台设备同时冒出十几条异常，本身就是假数据的味道。
interface Profile {
  overdue: number
  critAlarms: number
  openDowntime: number
  rateBase: number
  stateLabel: string
  /** 停机流文案（与设备屏同一台设备、同一原因） */
  downtimeText?: string
}
const PROFILES: Record<string, Profile> = {
  'WS-01': { overdue: 0, critAlarms: 1, openDowntime: 0, rateBase: 87, stateLabel: '设备报警' },
  'WS-02': {
    overdue: 0,
    critAlarms: 0,
    openDowntime: 1,
    rateBase: 92,
    stateLabel: '换型作业中',
    downtimeText: '前减装配三线 DEV-ASM-05/06 换型停机（P1→S1）',
  },
  'WS-03': {
    overdue: 0,
    critAlarms: 0,
    openDowntime: 1,
    rateBase: 90,
    stateLabel: '停机处理中',
    downtimeText: '电泳涂装线 DEV-CTG-02 故障停机 · 循环泵机械密封渗漏',
  },
}
const DEFAULT_PROFILE: Profile = {
  overdue: 0,
  critAlarms: 0,
  openDowntime: 0,
  rateBase: 92,
  stateLabel: '运行中',
}

/** 车间**对外交付**的全日计划件数 = Σ 该车间 delivers 产线的双班班计划（world.ts 节拍表）。 */
export function dailyDeliveryPlan(workshopId: string): number {
  return linesByWorkshop(workshopId)
    .filter((l) => lineProfileOf(l.id).delivers)
    .reduce((n, l) => n + shiftPlanOf(l.id) * 2, 0)
}

export function buildFactoryOverview(
  factoryId = DEFAULT_FACTORY_ID,
  workshopIds: string[] | 'all' = 'all',
): FactoryOverview {
  const factoryWorkshops = workshopsByFactory(factoryId)
  const visible =
    workshopIds === 'all'
      ? factoryWorkshops
      : factoryWorkshops.filter((w) => workshopIds.includes(w.id))
  const progress = Math.max(0.05, dayProgress())

  const cells: WorkshopCell[] = visible.map((w) => {
    const p = PROFILES[w.id] ?? DEFAULT_PROFILE
    const lines = linesByWorkshop(w.id)
    const rate = clamp(jitter(p.rateBase, 3), 55, 100)
    // 截至当前应完成的交付计划 = 全日交付计划 × 当日排产进度
    const planQty = Math.max(1, Math.round(dailyDeliveryPlan(w.id) * progress))
    const actualQty = Math.round((planQty * rate) / 100)
    const cell = {
      id: w.id,
      name: w.name,
      manager: w.managerName,
      stateLabel: p.stateLabel,
      // 在制工单：设定集 §7 约 3600 张 / 174 工作日 ≈ 21 张/日在产，按产线数摊
      wip: clamp(jitter(lines.length + 2, 2), 1, 12),
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

  // 全厂产量取**末道车间**（成品下线口径）；末道不在 scope 内时退回可见车间合计
  // 并且此时可见范围本来就不是「全厂」，加总不会造成跨段重复计数的误读。
  const finalCell = cells.find((c) => c.id === FINAL_WORKSHOP_ID)
  const actualSum = finalCell?.actualQty ?? cells.reduce((n, c) => n + c.actualQty, 0)
  const planSum = finalCell?.planQty ?? cells.reduce((n, c) => n + c.planQty, 0)
  const criticalAlarms = cells.reduce((n, c) => n + c.critAlarms, 0)
  const kpis = {
    achievement: planSum > 0 ? clamp(Math.round((actualSum / planSum) * 100), 0, 100) : 0, // 🟠 待 #570
    todayOutput: actualSum, // 🟠 待 #570
    todayPlan: planSum, // 🟠 待 #570
    wipOrders: cells.reduce((n, c) => n + c.wip, 0),
    riskOrders: cells.reduce((n, c) => n + c.overdue, 0) + clamp(jitter(1, 2), 0, 2), // 超期 + 临期风险 🟠
    openAlarms: criticalAlarms + clamp(jitter(3, 2), 1, 5),
    criticalAlarms,
    openDowntime: cells.reduce((n, c) => n + c.openDowntime, 0),
    // 与质量屏同源（跨屏同一事实同一数据源）——「待处置 NCR」两屏必须是同一个数
    openNcr: buildQualityBoard(factoryId, workshopIds).kpis.openNcr,
  }

  // OEE 三因子：良品率取设定集 §7 的不合格率 2.3% 反推（≈97.7%），
  // 可用率/性能率仍为占位（🟠 无真实端点，待 #570）。
  const oee = [
    { label: '可用率', value: +(87 + Math.random() * 4).toFixed(1) }, // 🟠 占位值
    { label: '性能率', value: +(92 + Math.random() * 4).toFixed(1) }, // 🟠 占位值
    { label: '良品率', value: +(97.7 + (Math.random() - 0.5) * 0.6).toFixed(1) },
  ]

  // —— 实时流：从可见车间生成，scope 收窄后流内容跟着收窄 ——
  // 车间名用短名（远视距可读），产线/工单/维修单号全部走设定集 §9 号段。
  const alarms: FeedItem[] = []
  const downtimes: FeedItem[] = []
  let ai = 0
  let di = 0
  for (const c of cells) {
    const ws = visible.find((w) => w.id === c.id)!
    const lines = linesByWorkshop(c.id)
    const lineName = lines[0]?.name ?? ws.shortName
    if (c.critAlarms > 0) {
      alarms.push({
        id: seq('AL', 2400 - ai),
        level: 'critical',
        text: `${ws.shortName} 活塞杆一线 DEV-CNC-03 振动超限 6.9 mm/s`,
        time: clock(jitter(4 + ai * 6, 3)),
      })
      ai++
    }
    if (c.overdue > 0) {
      alarms.push({
        id: seq('AL', 2400 - ai),
        level: 'warning',
        text: `${ws.shortName} ${woOf(lines[0]?.id ?? '')} 交付超期`,
        time: clock(jitter(9 + ai * 6, 4)),
      })
      ai++
    }
    const dtText = (PROFILES[c.id] ?? DEFAULT_PROFILE).downtimeText
    if (c.openDowntime > 0 && dtText) {
      downtimes.push({
        id: seq('DT-2026', 812 - di),
        level: 'warning',
        text: `${ws.shortName} ${dtText} ${jitter(72, 20)} min`,
        time: clock(jitter(84, 12)),
      })
      di++
    }
  }
  // 常规流（每车间两轮），保证两条流都溢出可滚且全部真实命名
  for (const [i, c] of cells.entries()) {
    const ws = visible.find((w) => w.id === c.id)!
    const lines = linesByWorkshop(c.id)
    const lineName = lines[0]?.name ?? ws.shortName
    const line1 = lines[i % Math.max(1, lines.length)]
    const line2 = lines[(i + 1) % Math.max(1, lines.length)]
    alarms.push(
      {
        id: seq('AL', 2380 - i),
        level: i % 3 === 0 ? 'warning' : 'info',
        text:
          i % 3 === 0
            ? `${ws.shortName} ${line1?.name ?? ''} 节拍低于目标`
            : i % 3 === 1
              ? `${ws.shortName} 物料齐套校验通过`
              : `${ws.shortName} ${line1?.name ?? ''} 完工上报 ${woOf(line1?.id ?? '')}`,
        time: clock(jitter(16 + i * 8, 5)),
      },
      {
        id: seq('AL', 2360 - i),
        level: i % 4 === 0 ? 'warning' : 'info',
        text:
          i % 4 === 0
            ? `${ws.shortName} ${line2?.name ?? ''} 首检超时提醒`
            : `${ws.shortName} ${line2?.name ?? ''} 质检放行 ${woOf(line2?.id ?? '')}`,
        time: clock(jitter(52 + i * 9, 6)),
      },
      {
        id: seq('AL', 2340 - i),
        level: 'info',
        text: `${ws.shortName} ${lineName} 首件确认合格`,
        time: clock(jitter(128 + i * 11, 8)),
      },
    )
    downtimes.push(
      {
        id: seq('DT-2026', 806 - i),
        level: 'info',
        text: `${ws.shortName} ${line1?.name ?? ''} 计划保养完成 ${jitter(24, 8)} min`,
        time: clock(jitter(40 + i * 12, 8)),
      },
      {
        id: seq('DT-2026', 800 - i),
        level: 'info',
        text: `${ws.shortName} ${line2?.name ?? ''} 换型停机记录 ${jitter(18, 6)} min`,
        time: clock(jitter(96 + i * 14, 10)),
      },
      {
        id: seq('DT-2026', 794 - i),
        level: 'info',
        text: `${ws.shortName} ${lineName} 首件调机停机 ${jitter(12, 6)} min`,
        time: clock(jitter(168 + i * 16, 12)),
      },
    )
  }

  return { factoryId, kpis, workshops: cells, oee, alarms, downtimes }
}
