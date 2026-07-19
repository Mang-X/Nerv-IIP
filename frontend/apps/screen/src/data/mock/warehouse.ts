// 仓储物流 mock 聚合（MAN-318）：真实作业画像前置 ——
// ① 当日出入库进度按**真实时钟工作窗**（08:00–20:00）单调推进（非拍数字），
//    行/单口径勾稽，近 12h 流量 = 完成量的逐小时差分（Σ 与进度精确勾稽）；
// ② 任务池按「日期 × 工厂」种子确定性生成（同日稳定、跨日变化），创建时刻按
//    15min 波次锚定 —— 龄期随真实时钟增长而**超时集合全天稳定**（异常是例外：
//    拣货 2 + 上架 1 恒 3 条超时，其余任务均正常龄期）；
// ③ WCS 失败榜与状态分布 / 按适配器聚合逐格勾稽（午后多一条提升机失败）；
// ④ 收货过账失败 0–1 单（按日期轮换，空态语义可达）。
// 🟠 全部指标待 #570 真实端点（WMS 分页 list），接入后由 fetchers/warehouse.ts 单点切换。
// ℹ️ 任务行无逐行时序序列且规模 ≤ 50，全量返回；如后续行内加趋势，再按 line.ts
//    的 visibleIds seam 引入（虚拟滚动预留位）。
import type {
  CycleCountBoard,
  InboundProgress,
  OutboundProgress,
  OverdueTaskRow,
  WarehouseBoard,
  WarehouseOpsTick,
  WcsAdapterCell,
  WcsAdapterKind,
  WcsBoard,
  WcsFailureRow,
  WhTaskGroup,
  WhTaskKind,
  WhTaskRow,
} from '@/data/contracts/warehouse'
import { jitter, seq } from './fixtures'

function clamp(n: number, lo: number, hi: number): number {
  return Math.min(hi, Math.max(lo, n))
}

/** 距 now minsAgo 分钟的 HH:mm（now 可注入 —— 测试确定性；语义同 fixtures.clock）。 */
function hhmmAgo(now: Date, minsAgo: number): string {
  const d = new Date(now.getTime() - minsAgo * 60_000)
  const p = (x: number) => String(x).padStart(2, '0')
  return `${p(d.getHours())}:${p(d.getMinutes())}`
}

/** 「日期 × 工厂」确定性种子：同日稳定（轮询不跳变）、跨日/跨厂变化。 */
function daySeed(now: Date, factoryId: string): number {
  let h = now.getFullYear() * 372 + (now.getMonth() + 1) * 31 + now.getDate()
  for (let i = 0; i < factoryId.length; i++) {
    h = Math.imul(h ^ factoryId.charCodeAt(i), 2654435761) >>> 0
  }
  return h >>> 0
}

/** 仓库工作窗 08:00–20:00 的进度分数；08:00 前为 0，20:00 封板为 1。 */
export function workFrac(now = new Date()): number {
  const m = Math.min(now.getHours() * 60 + now.getMinutes(), 1200)
  return clamp((m - 480) / 720, 0, 1)
}

// —— 完成量曲线（收货偏上午、拣配偏下午 —— 真实仓库节奏）——
const IN_CURVE = (f: number) => f ** 0.92 * 0.94
const OUT_CURVE = (f: number) => Math.max(0, f - 0.04) ** 1.08 * 0.96

/** 单调完成量：total × curve(frac) × 缓波（幅 1.5% / 频 1.7 rad/h —— 波幅压到
 *  远小于每小时基线增量，小时**差分**曲线由 curve 斜率主导（上午收货峰 / 下午
 *  拣配峰的真实节奏），而非被正弦项造出「深谷冲高」假波动；基线增速恒大于
 *  波动斜率，当日进度只增不减；20:00 封板后冻结）。 */
function doneAt(total: number, at: Date, curve: (f: number) => number, phase: number): number {
  const f = workFrac(at)
  if (f <= 0) return 0
  const hours = Math.min(at.getHours() + at.getMinutes() / 60, 20)
  const wave = 1 + 0.015 * Math.sin(hours * 1.7 + phase)
  return clamp(Math.floor(total * curve(f) * wave), 0, total)
}

/** 近 12h 每小时完成行数 = 完成量的逐小时差分（Σ 与当前完成量精确勾稽）。 */
function hourlyOf(
  total: number,
  now: Date,
  curve: (f: number) => number,
  phase: number,
): { hourly: number[]; hourLabels: string[] } {
  const hourly: number[] = []
  const hourLabels: string[] = []
  for (let i = 11; i >= 0; i--) {
    const end = new Date(now.getTime() - i * 3_600_000)
    const start = new Date(end.getTime() - 3_600_000)
    hourly.push(Math.max(0, doneAt(total, end, curve, phase) - doneAt(total, start, curve, phase)))
    hourLabels.push(`${String(end.getHours()).padStart(2, '0')}:00`)
  }
  return { hourly, hourLabels }
}

/** mock 没有单据时间戳；用稳定的模拟创建小时分散失败，避免伪造成全部刚刚发生。 */
function failedHourlyOf(count: number, seed: number): number[] {
  const hourly = new Array(12).fill(0) as number[]
  const bucketOrder = [8, 4, 10, 6, 2, 9, 5, 1, 7, 3, 0]
  for (let index = 0; index < count; index++) {
    const bucket = bucketOrder[(seed + index) % bucketOrder.length] ?? 8
    hourly[bucket]++
  }
  return hourly
}

// —— 任务池素材（汽车 / 电池制造物料 + 真实库位编码）——
const SKUS: { name: string; unit: string; lo: number; hi: number }[] = [
  { name: '电芯极片卷料', unit: '卷', lo: 6, hi: 24 },
  { name: 'PACK 下箱体', unit: '件', lo: 4, hi: 16 },
  { name: '门内饰板总成', unit: '件', lo: 8, hi: 32 },
  { name: 'M8 高强螺栓', unit: '箱', lo: 4, hi: 20 },
  { name: '前舱线束总成', unit: '套', lo: 6, hi: 18 },
  { name: 'BMS 主控板', unit: '件', lo: 10, hi: 40 },
  { name: '高压连接器', unit: '盒', lo: 8, hi: 36 },
  { name: '电机定子铁芯', unit: '件', lo: 6, hi: 24 },
  { name: '制动卡钳总成', unit: '件', lo: 8, hi: 28 },
  { name: '座椅滑轨组件', unit: '套', lo: 10, hi: 30 },
  { name: '车门密封条', unit: '卷', lo: 12, hi: 48 },
  { name: '冷却液管路', unit: '件', lo: 8, hi: 30 },
  { name: '铝合金防撞梁', unit: '件', lo: 4, hi: 12 },
  { name: '轮速传感器', unit: '盒', lo: 10, hi: 40 },
  { name: '电池模组端板', unit: '件', lo: 12, hi: 48 },
  { name: '隔音棉-前围', unit: '包', lo: 6, hi: 20 },
]
const STORAGE_LOCS = [
  'A2-03-14',
  'A1-07-02',
  'B1-12-05',
  'B3-02-11',
  'C2-05-08',
  'D1-09-03',
  '立库 L1-08-2',
  '立库 L2-03-4',
  '立库 L1-15-3',
  '立库 L3-06-1',
]
const PICK_FACES = ['P-A-07', 'P-B-03', 'P-C-12', 'P-A-11']
const RCV_LOCS = ['RCV-01', 'RCV-02', 'RCV-03']
const SHIP_LOCS = ['SHIP-01', 'SHIP-02', 'SHIP-03']
const LINESIDE_LOCS = ['线边-总装一线', '线边-电芯线', '线边-PACK 线', '线边-焊装一线']
const CUSTOMERS = ['蔚然汽车', '星驰新能源', '临港储能', '迅驰车业', '宏远重工']

/** 超时阈值（分钟）。 */
export const OVERDUE_MIN = 45
/** 任务创建按 15min 波次锚定（龄期随真实时钟增长、超时集合稳定）。 */
const WAVE_MIN = 15

// 龄期档位（分钟）：超时档 ≥ 46、正常档 ≤ 31（+14min 量化余量后仍 ≤45），
// 保证任何时刻超时集合不漂移（拣货 3 / 上架 2 / 盘点 1 = 恒 6 条，繁忙日画像
// 但仍是少数 —— 异常是例外）。档位按**显式波次分组**（组间距 ≥16min > 波次
// 粒度）：WMS 波次放单本就同批同龄，但组间必须拉开 —— 否则相邻档被 15min
// 量化坍缩成一整列同龄期（看着像复制粘贴）。
const PICK_AGE_SLOTS = [
  70, 55, 48, 31, 31, 31, 31, 15, 15, 15, 15, 15, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2, 2,
]
const PUTAWAY_AGE_SLOTS = [58, 50, 30, 30, 30, 13, 13, 13, 13, 13, 4, 4, 4, 4, 4]
const COUNT_AGE_SLOTS = [47, 29, 29, 12, 12, 12, 12, 12]

/** 龄期：任务创建时刻 = (now − 档位) 向下取整到 15min 波次 → 龄期 ∈ [档位, 档位+14]。 */
function ageOf(now: Date, slot: number): number {
  const nowMin = Math.floor(now.getTime() / 60_000)
  const createdMin = Math.floor((nowMin - slot) / WAVE_MIN) * WAVE_MIN
  return nowMin - createdMin
}

const KIND_LABELS: Record<WhTaskKind, string> = { putaway: '上架', pick: '拣货', count: '盘点' }

/** 生成某类 Open 任务行（确定性：同日同厂同分钟内完全稳定），按龄期降序。 */
function taskRows(kind: WhTaskKind, n: number, s: number, now: Date): WhTaskRow[] {
  const slots =
    kind === 'pick' ? PICK_AGE_SLOTS : kind === 'putaway' ? PUTAWAY_AGE_SLOTS : COUNT_AGE_SLOTS
  return Array.from({ length: n }, (_, i) => {
    const sku = SKUS[(s + i * 7) % SKUS.length]
    const age = ageOf(now, slots[i] ?? 0)
    let id: string
    let from: string
    let to: string | undefined
    let ref: string | undefined
    let qty: number
    if (kind === 'pick') {
      id = seq('PK', 880 + (s % 40) + i)
      const toShip = (s + i) % 5 < 3 // 60% 发运拣货 / 40% 线边配送（关联 MES 工单）
      const fromPool = (s + i) % 3 === 0 ? PICK_FACES : STORAGE_LOCS
      from = fromPool[(s + i * 3) % fromPool.length]
      to = toShip
        ? SHIP_LOCS[(s + i) % SHIP_LOCS.length]
        : LINESIDE_LOCS[(s + i) % LINESIDE_LOCS.length]
      ref = toShip ? seq('SO', 9800 + (s % 60) + i) : seq('WO', 1941 + ((s + i) % 20))
      qty = sku.lo + ((s * 13 + i * 29) % (sku.hi - sku.lo + 1))
    } else if (kind === 'putaway') {
      id = seq('PT', 420 + (s % 30) + i)
      from = RCV_LOCS[(s + i) % RCV_LOCS.length]
      to = STORAGE_LOCS[(s + i * 3) % STORAGE_LOCS.length]
      ref = seq('ASN', 260700 + (s % 40) + i, 6)
      qty = sku.lo + ((s * 17 + i * 31) % (sku.hi - sku.lo + 1))
    } else {
      id = seq('CC', 12 + (s % 8) + i, 2)
      from = STORAGE_LOCS[(s + i * 2) % STORAGE_LOCS.length]
      to = undefined
      ref = undefined
      qty = 120 + ((s * 7 + i * 53) % 560) // 账面数量
    }
    return {
      id,
      kind,
      sku: sku.name,
      qty,
      unit: sku.unit,
      from,
      to,
      ref,
      createdAt: hhmmAgo(now, age),
      ageMin: age,
      overdue: age > OVERDUE_MIN,
    }
  })
}

// —— WCS 适配器画像（AdapterType 语义；share 为指令量占比；六类自动化设备）——
const ADAPTER_DEFS: {
  kind: WcsAdapterKind
  label: string
  share: number
  run: number
  queue: number
}[] = [
  { kind: 'stacker', label: '巷道堆垛机', share: 0.26, run: 6, queue: 8 },
  { kind: 'agv', label: 'AGV 调度', share: 0.3, run: 9, queue: 11 },
  { kind: 'shuttle', label: '四向穿梭车', share: 0.16, run: 5, queue: 6 },
  { kind: 'conveyor', label: '输送线', share: 0.14, run: 6, queue: 5 },
  { kind: 'sorter', label: '分拣机', share: 0.09, run: 4, queue: 4 },
  { kind: 'hoist', label: '提升机', share: 0.05, run: 2, queue: 3 },
]

/** WCS 失败池：常驻 3 条（堆垛机取货超时 / AGV 路径阻挡 / 分拣格口满位），
 *  午后高峰多 1 条提升机 —— 繁忙工厂日画像，仍是少数（异常是例外）。 */
function buildFailures(now: Date, s: number): WcsFailureRow[] {
  const rows: WcsFailureRow[] = [
    {
      cmd: seq('WCS', 88200 + (s % 90), 5),
      kind: 'stacker',
      adapter: '巷道 2 堆垛机',
      error: '取货超时 · 货叉未到位',
      retries: 3,
      sinceMin: 12,
      firstAt: hhmmAgo(now, 12),
    },
    {
      cmd: seq('WCS', 88100 + (s % 70), 5),
      kind: 'agv',
      adapter: 'AGV-07',
      error: '路径阻挡 · 等待人工移障',
      retries: 1,
      sinceMin: 6,
      firstAt: hhmmAgo(now, 6),
    },
    {
      cmd: seq('WCS', 88400 + (s % 60), 5),
      kind: 'sorter',
      adapter: '分拣机 1#',
      error: '格口满位 · 分拣暂停',
      retries: 2,
      sinceMin: 17,
      firstAt: hhmmAgo(now, 17),
    },
  ]
  const h = now.getHours()
  if (h >= 13 && h < 18) {
    rows.push({
      cmd: seq('WCS', 88300 + (s % 50), 5),
      kind: 'hoist',
      adapter: '提升机 2#',
      error: '层间光电信号异常 · 自动重试中',
      retries: 2,
      sinceMin: 23,
      firstAt: hhmmAgo(now, 23),
    })
  }
  return rows.sort((a, b) => b.retries - a.retries)
}

/** /warehouse 仓储物流大屏（纯函数；now 可注入测试确定性）。 */
export function buildWarehouseBoard(now = new Date(), factoryId = 'F01'): WarehouseBoard {
  const s = daySeed(now, factoryId)
  const f = workFrac(now)

  // —— 当日入库（ASN）：行数为主口径，单据完成滞后于行（收完最后一行才关单）——
  const inLinesTotal = 170 + (s % 31)
  const inDocsTotal = 21 + (s % 5)
  const inLinesDone = doneAt(inLinesTotal, now, IN_CURVE, 0.6)
  const inDocsDone = Math.min(inDocsTotal, Math.floor(inDocsTotal * IN_CURVE(f) ** 1.15))
  const postFailedDocs = now.getDate() % 3 === 0 ? 0 : 1
  const inbound: InboundProgress = {
    docsDone: inDocsDone,
    docsTotal: inDocsTotal,
    linesDone: inLinesDone,
    linesTotal: inLinesTotal,
    pct: Math.round((inLinesDone / inLinesTotal) * 100),
    failedDocs: postFailedDocs,
    ...hourlyOf(inLinesTotal, now, IN_CURVE, 0.6),
    failedHourly: failedHourlyOf(postFailedDocs, s),
    postFailedDocs,
    postFailedDoc: postFailedDocs > 0 ? seq('ASN', 260690 + (s % 9), 6) : undefined,
  }

  // —— 当日出库（SO）：已拣配行 / 应发行 ——
  const outLinesTotal = 136 + (s % 25)
  const outDocsTotal = 15 + (s % 4)
  const outLinesDone = doneAt(outLinesTotal, now, OUT_CURVE, 2.3)
  const outDocsDone = Math.min(outDocsTotal, Math.floor(outDocsTotal * OUT_CURVE(f) ** 1.15))
  const outbound: OutboundProgress = {
    docsDone: outDocsDone,
    docsTotal: outDocsTotal,
    linesDone: outLinesDone,
    linesTotal: outLinesTotal,
    pct: Math.round((outLinesDone / outLinesTotal) * 100),
    failedDocs: 0,
    ...hourlyOf(outLinesTotal, now, OUT_CURVE, 2.3),
    failedHourly: failedHourlyOf(0, s + 1),
    customers: 5 + (s % 3),
    latestShipment:
      outDocsDone > 0
        ? `${CUSTOMERS[s % CUSTOMERS.length]} · ${seq('SO', 9800 + (s % 60))}`
        : undefined,
  }

  // —— 作业任务（守恒：今日创建 = Open 积压 + 今日完成）——
  // 拣货完成 ⇔ SO 已拣配行（同一事实的两个视图，跨面板勾稽）
  const pickRows = taskRows('pick', 18 + (s % 8), s, now)
  const pick: WhTaskGroup = {
    kind: 'pick',
    backlog: pickRows.length,
    doneToday: outLinesDone,
    createdToday: pickRows.length + outLinesDone,
    overdue: pickRows.filter((r) => r.overdue).length,
    rows: pickRows,
  }
  // 上架完成 ≈ 收货行的 86%（部分收货直送线边，不产生上架任务）
  const putawayRows = taskRows('putaway', 10 + (s % 6), s, now)
  const putawayDone = Math.floor(inLinesDone * 0.86)
  const putaway: WhTaskGroup = {
    kind: 'putaway',
    backlog: putawayRows.length,
    doneToday: putawayDone,
    createdToday: putawayRows.length + putawayDone,
    overdue: putawayRows.filter((r) => r.overdue).length,
    rows: putawayRows,
  }
  // 盘点：库位数口径（planned = 已盘 + 未盘任务），差异 ≤ 已盘
  const countRows = taskRows('count', 5 + (s % 4), s, now)
  const counted = 8 + Math.floor(f * 8)
  const count: CycleCountBoard = {
    planned: counted + countRows.length,
    counted,
    variance: Math.min(2 + (s % 2), counted),
    overdue: countRows.filter((r) => r.overdue).length,
    rows: countRows,
  }

  // —— WCS：失败榜为事实源，适配器聚合/状态分布由其推导（逐格勾稽）——
  const failures = buildFailures(now, s)
  const dailyCap = 900 + (s % 120)
  const adapters: WcsAdapterCell[] = ADAPTER_DEFS.map((d) => {
    const completed = Math.floor(dailyCap * d.share * f)
    const running = f > 0 ? clamp(jitter(d.run, 3), 1, 12) : 0
    const queued = f > 0 ? clamp(jitter(d.queue, 4), 0, 16) : 0
    const failed = failures.filter((x) => x.kind === d.kind).length
    return {
      kind: d.kind,
      label: d.label,
      total: queued + running + completed + failed,
      queued,
      running,
      completed,
      failed,
    }
  })
  const wcs: WcsBoard = {
    adapters,
    counts: {
      queued: adapters.reduce((n, a) => n + a.queued, 0),
      running: adapters.reduce((n, a) => n + a.running, 0),
      completed: adapters.reduce((n, a) => n + a.completed, 0),
      failed: adapters.reduce((n, a) => n + a.failed, 0),
    },
    failures,
  }

  // —— 任务超时榜 TOP5（跨类合并，按龄期降序；超时是少数）——
  const overdueTop: OverdueTaskRow[] = [...pickRows, ...putawayRows, ...countRows]
    .filter((r) => r.overdue)
    .sort((a, b) => b.ageMin - a.ageMin)
    .slice(0, 5)
    .map((r) => ({
      id: r.id,
      kind: r.kind,
      kindLabel: KIND_LABELS[r.kind],
      sku: r.sku,
      ageMin: r.ageMin,
    }))

  return {
    factoryId,
    kpis: {
      inboundPct: inbound.pct,
      outboundPct: outbound.pct,
      pickBacklog: pick.backlog,
      putawayBacklog: putaway.backlog,
      wcsFailed: wcs.failures.length,
      countVariance: count.variance,
      throughputLines: inLinesDone + outLinesDone,
    },
    inbound,
    outbound,
    pick,
    putaway,
    count,
    wcs,
    overdueTop,
  }
}

/** 任务看板 + WCS 高频 tick（3s）：与主数据（5s）同源纯函数推导，口径必然一致。 */
export function buildWarehouseOpsTick(now = new Date(), factoryId = 'F01'): WarehouseOpsTick {
  const b = buildWarehouseBoard(now, factoryId)
  return { pick: b.pick, putaway: b.putaway, count: b.count, wcs: b.wcs, overdueTop: b.overdueTop }
}
