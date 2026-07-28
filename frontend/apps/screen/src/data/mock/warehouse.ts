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
import { DEFAULT_FACTORY_ID } from './masterdata'
import { woOf } from './world'

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

// —— 任务池素材：物料名/编码/单位逐条取自 L0 §4 的 84 SKU（RM- 原料 / SF- 半成品 /
//    FG- 成品 / PK- 包材），库位编码为仓储侧自有编码 ——
const SKUS: { code: string; name: string; unit: string; lo: number; hi: number }[] = [
  { code: 'RM-BAR-01', name: '45# 钢棒料 φ22', unit: 'kg', lo: 200, hi: 900 },
  { code: 'RM-TUB-02', name: '精密钢管 φ50×2.0', unit: 'kg', lo: 180, hi: 800 },
  { code: 'RM-SPR-01', name: '悬架弹簧 轿车前（首选供应商）', unit: '件', lo: 60, hi: 320 },
  { code: 'RM-SPR-03', name: '悬架弹簧 SUV 前（首选供应商）', unit: '件', lo: 60, hi: 280 },
  { code: 'RM-SEL-02', name: '油封 φ22 骨架式', unit: '件', lo: 100, hi: 600 },
  { code: 'RM-SEL-04', name: '油封 φ25 双唇式', unit: '件', lo: 100, hi: 600 },
  { code: 'RM-OIL-01', name: '减振器专用油 10#', unit: 'L', lo: 40, hi: 200 },
  { code: 'RM-ACC-01', name: '连接环 上安装环', unit: '件', lo: 120, hi: 640 },
  { code: 'RM-ACC-05', name: '防尘罩 长款', unit: '件', lo: 120, hi: 600 },
  { code: 'RM-ACC-09', name: '紧固件 M10 高强螺栓', unit: '件', lo: 200, hi: 900 },
  { code: 'SF-ROD-03', name: '活塞杆 φ22×420', unit: '件', lo: 80, hi: 400 },
  { code: 'SF-TUB-03', name: '缸筒 φ50×300', unit: '件', lo: 80, hi: 400 },
  { code: 'SF-VLV-02', name: '阀系组件 标准型', unit: '件', lo: 80, hi: 400 },
  { code: 'FG-QJ-P1-L', name: 'P1 平台前滑柱总成（左）', unit: '件', lo: 40, hi: 240 },
  { code: 'FG-HJ-S2-R', name: 'S2 平台后减振器总成（右）', unit: '件', lo: 40, hi: 240 },
  { code: 'PK-BOX-02', name: '纸箱 中号（6 件装）', unit: '个', lo: 30, hi: 160 },
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
/** 可发运物料：成品 + 成品包材（发运出库只会出这些）。 */
const SHIPPABLE_SKUS = SKUS.filter((x) => /^(FG|PK)-/.test(x.code))
/** 可配送到线边的物料：原料 + 半成品（线边领料只会领这些）。 */
const LINESIDE_SKUS = SKUS.filter((x) => /^(RM|SF)-/.test(x.code))

const LINESIDE_LOCS = [
  '线边-前减装配一线',
  '线边-后减装配一线',
  '线边-阀系预装线',
  '线边-活塞杆一线',
]
/** 线边配送拣货挂的工单所属产线（与 LINESIDE_LOCS 逐位对应，跨屏工单号对得上）。 */
const LINE_IDS = ['LINE-WB-FA-01', 'LINE-WB-RA-01', 'LINE-WB-VA-01', 'LINE-WB-ROD-01']
// 客户取 L0 §6 的 8 家（含 MAN-519 固定案例客户 CUST-DEMO-001）
const CUSTOMERS = [
  '长三角整车一厂',
  '长三角整车二厂',
  '比德新能源',
  '皖江 Tier1 汽车系统',
  '华东汽车零部件采购中心',
  '路航售后连锁',
]

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
    // 拣货物料按去向选池：发运只可能拣成品/包材，线边配送只可能拣原料/半成品。
    // 把「S2 平台后减振器总成」拣去阀系预装线边是物流上不可能的事，一眼假。
    const toShipPick = kind === 'pick' && (s + i) % 5 < 3
    const pool = kind !== 'pick' ? SKUS : toShipPick ? SHIPPABLE_SKUS : LINESIDE_SKUS
    const sku = pool[(s + i * 7) % pool.length]
    const age = ageOf(now, slots[i] ?? 0)
    let id: string
    let from: string
    let to: string | undefined
    let ref: string | undefined
    let qty: number
    if (kind === 'pick') {
      id = seq('PK', 880 + (s % 40) + i)
      const toShip = toShipPick // 60% 发运拣货 / 40% 线边配送（关联 MES 工单）
      const fromPool = (s + i) % 3 === 0 ? PICK_FACES : STORAGE_LOCS
      from = fromPool[(s + i * 3) % fromPool.length]
      to = toShip
        ? SHIP_LOCS[(s + i) % SHIP_LOCS.length]
        : LINESIDE_LOCS[(s + i) % LINESIDE_LOCS.length]
      // 发运拣货挂发货出库单（设定集 §9 `OB-DO-2026-#####`），线边配送挂 MES 工单
      ref = toShip
        ? `OB-DO-2026-${String(1180 + (s % 60) + i).padStart(5, '0')}`
        : woOf(LINE_IDS[(s + i) % LINE_IDS.length])
      qty = sku.lo + ((s * 13 + i * 29) % (sku.hi - sku.lo + 1))
    } else if (kind === 'putaway') {
      id = seq('PT', 420 + (s % 30) + i)
      from = RCV_LOCS[(s + i) % RCV_LOCS.length]
      to = STORAGE_LOCS[(s + i * 3) % STORAGE_LOCS.length]
      // 收货上架挂入库单 `IB-{采购订单}`（WMS WorldHistoryPhase2Spec.InboundOrderNo 同式）
      ref = `IB-${seq('PO-2026', 430 + (s % 40) + i, 4)}`
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

// —— WCS 适配器画像（AdapterType 语义；share 为指令量占比）——
// 宁沪减振的自动化仓储只有：成品立库堆垛机、线边配送 AGV、包装下线接驳输送线、
// 立库层间提升机。分拣机/四向穿梭车这类电商仓设备本厂没有，不列（列了就是假设备）。
const ADAPTER_DEFS: {
  kind: WcsAdapterKind
  label: string
  share: number
  run: number
  queue: number
}[] = [
  { kind: 'agv', label: '线边配送 AGV', share: 0.42, run: 4, queue: 5 },
  { kind: 'stacker', label: '成品立库堆垛机', share: 0.3, run: 2, queue: 3 },
  { kind: 'conveyor', label: '包装下线输送线', share: 0.2, run: 2, queue: 2 },
  { kind: 'hoist', label: '立库提升机', share: 0.08, run: 1, queue: 1 },
]

/** WCS 失败池：常驻 2 条（堆垛机取货超时 / AGV 路径阻挡），午后高峰多 1 条提升机
 *  —— 异常是例外。指令号走设定集 §9 的 `WCS-{仓储任务号}` 形（派生自作业任务号）。 */
function buildFailures(now: Date, s: number): WcsFailureRow[] {
  const rows: WcsFailureRow[] = [
    {
      cmd: `WCS-WT-OB-PK-2026-${String(1180 + (s % 90)).padStart(4, '0')}-01`,
      kind: 'stacker',
      adapter: '成品立库 2 巷道堆垛机',
      error: '取货超时 · 货叉未到位',
      retries: 3,
      sinceMin: 12,
      firstAt: hhmmAgo(now, 12),
    },
    {
      cmd: `WCS-WT-IS-LN-2026-${String(2260 + (s % 70)).padStart(4, '0')}-01`,
      kind: 'agv',
      adapter: 'AGV-03（装配车间线边）',
      error: '路径阻挡 · 等待人工移障',
      retries: 1,
      sinceMin: 6,
      firstAt: hhmmAgo(now, 6),
    },
  ]
  const h = now.getHours()
  if (h >= 13 && h < 18) {
    rows.push({
      cmd: `WCS-WT-IB-PR-2026-${String(1040 + (s % 50)).padStart(4, '0')}-01`,
      kind: 'hoist',
      adapter: '立库提升机 1#',
      error: '层间光电信号异常 · 自动重试中',
      retries: 2,
      sinceMin: 23,
      firstAt: hhmmAgo(now, 23),
    })
  }
  return rows.sort((a, b) => b.retries - a.retries)
}

/** /warehouse 仓储物流大屏（纯函数；now 可注入测试确定性）。 */
export function buildWarehouseBoard(
  now = new Date(),
  factoryId = DEFAULT_FACTORY_ID,
): WarehouseBoard {
  const s = daySeed(now, factoryId)
  const f = workFrac(now)

  // —— 当日入库：行数为主口径，单据完成滞后于行（收完最后一行才关单）——
  // 规模推导（设定集 §7）：采购订单 480 张 / 174 工作日 ≈ 2.8 张/日 → 收货行 ≈ 9；
  // 加上完工入库（工单 3600 / 174 ≈ 21 张/日）→ 当日入库行 ≈ 30。
  const inLinesTotal = 28 + (s % 9)
  const inDocsTotal = 6 + (s % 3)
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
    postFailedDoc: postFailedDocs > 0 ? `IB-${seq('PO-2026', 425 + (s % 9), 4)}` : undefined,
  }

  // —— 当日出库：已拣配行 / 应发行 ——
  // 规模推导（设定集 §7）：销售订单 3200 / 174 ≈ 18 张/日 → 发货行 ≈ 25；
  // 加上车间领料（21 张工单 × 约 4 个组件）≈ 84 行 → 当日出库行 ≈ 109。
  const outLinesTotal = 104 + (s % 17)
  const outDocsTotal = 17 + (s % 5)
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
        ? `${CUSTOMERS[s % CUSTOMERS.length]} · ${seq('SO-2026', 3140 + (s % 60), 5)}`
        : undefined,
  }

  // —— 作业任务（守恒：今日创建 = Open 积压 + 今日完成）——
  // 拣货完成 ⇔ SO 已拣配行（同一事实的两个视图，跨面板勾稽）
  const pickRows = taskRows('pick', 12 + (s % 6), s, now)
  const pick: WhTaskGroup = {
    kind: 'pick',
    backlog: pickRows.length,
    doneToday: outLinesDone,
    createdToday: pickRows.length + outLinesDone,
    overdue: pickRows.filter((r) => r.overdue).length,
    rows: pickRows,
  }
  // 上架完成 ≈ 收货行的 86%（部分收货直送线边，不产生上架任务）
  const putawayRows = taskRows('putaway', 6 + (s % 4), s, now)
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
  const countRows = taskRows('count', 3 + (s % 3), s, now)
  const counted = 5 + Math.floor(f * 6)
  const count: CycleCountBoard = {
    planned: counted + countRows.length,
    counted,
    variance: Math.min(2 + (s % 2), counted),
    overdue: countRows.filter((r) => r.overdue).length,
    rows: countRows,
  }

  // —— WCS：失败榜为事实源，适配器聚合/状态分布由其推导（逐格勾稽）——
  const failures = buildFailures(now, s)
  // WCS 指令量按本厂规模（成品立库 + 线边 AGV 配送）≈260 条/日，不是整车厂的上千条
  const dailyCap = 240 + (s % 60)
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
export function buildWarehouseOpsTick(
  now = new Date(),
  factoryId = DEFAULT_FACTORY_ID,
): WarehouseOpsTick {
  const b = buildWarehouseBoard(now, factoryId)
  return { pick: b.pick, putaway: b.putaway, count: b.count, wcs: b.wcs, overdueTop: b.overdueTop }
}
