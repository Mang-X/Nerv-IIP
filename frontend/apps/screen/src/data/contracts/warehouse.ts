// 仓储物流大屏数据契约（MAN-318，spec §五）。
// 一期诚实定位「WMS 作业指挥屏」：出入库进度 / 上架·拣货·盘点积压 / WCS 失败告警。
// Inventory 域对外仅单 SKU 点查（库存余额/流水/预留无读面）→ 一期不做库存资产半屏、
// 不造假库存数。WMS 读面（ASN/SO/putaway/pick/cycle-count/WCS 指令分页 list）齐全。
// ✅ = WMS 分页 list 直接可得；🟡 = 前端按分页数据聚合口径；🟠 = 待 #570 真实端点，
// 接入后仅换 fetchers/warehouse.ts，页面与契约不动。

/** 作业任务类别：上架 / 拣货 / 盘点（WarehouseTask 仅 Open/Completed 两态，无 operator） */
export type WhTaskKind = 'putaway' | 'pick' | 'count'

/** 积压任务行（Open 态）。龄期 🟡 = now − CreatedUtc，前端算；超时阈值 45min。 */
export interface WhTaskRow {
  /** PT-xxxx 上架 / PK-xxxx 拣货 / CC-xx 盘点 ✅ */
  id: string
  kind: WhTaskKind
  /** 物料名 ✅（行上带 SKU 快照） */
  sku: string
  /** 数量（盘点行为账面数量）✅ */
  qty: number
  unit: string
  /** 来源库位（盘点行 = 待盘库位）✅ */
  from: string
  /** 目标库位（盘点任务无流向）✅ */
  to?: string
  /** 需求来源单：拣货 SO-/WO-（线边配送关联 MES 工单）、上架 ASN- ✅ */
  ref?: string
  /** 创建时刻 HH:mm ✅（CreatedUtc） */
  createdAt: string
  /** 龄期（分钟）🟡 前端按 CreatedUtc 推算 */
  ageMin: number
  /** 超时（> 45min）🟡 */
  overdue: boolean
}

/** 上架 / 拣货任务组（守恒：createdToday = backlog + doneToday） */
export interface WhTaskGroup {
  kind: WhTaskKind
  /** 积压 = Open 任务数 = rows.length ✅ */
  backlog: number
  /** 今日完成（Completed 计数）✅ */
  doneToday: number
  /** 今日创建 🟡 = backlog + doneToday（任务守恒口径） */
  createdToday: number
  /** 超时任务数 🟡（rows 中 overdue 计数） */
  overdue: number
  rows: WhTaskRow[]
}

/** 盘点看板：进度按库位数口径（rows = 未盘库位任务，planned = counted + backlog） */
export interface CycleCountBoard {
  /** 计划盘点库位 ✅ */
  planned: number
  /** 已盘库位 ✅ */
  counted: number
  /** 差异库位数 ✅（≤ counted） */
  variance: number
  /** 超时盘点任务数 🟡 */
  overdue: number
  rows: WhTaskRow[]
}

/** 出入库当日进度（行数口径：Σ已收行/Σ应收行、Σ已拣配行/Σ应发行）✅ */
export interface WhFlowProgress {
  /** 单据口径：ASN = 已收完单 / SO = 已发运单 ✅ */
  docsDone: number
  docsTotal: number
  /** 行数口径 ✅（进度主口径） */
  linesDone: number
  linesTotal: number
  /** 行完成率 0–100 🟡 = round(linesDone / linesTotal × 100) */
  pct: number
  /** 近 12h 每小时完成行数 🟡（勾稽：工作窗内 Σ = 行完成量差） */
  hourly: number[]
  hourLabels: string[]
}

export interface InboundProgress extends WhFlowProgress {
  /** 收货过账失败异常单数 ✅（正常日 0–1，异常是例外） */
  postFailedDocs: number
  /** 失败单号（有失败时给出） */
  postFailedDoc?: string
}

export interface OutboundProgress extends WhFlowProgress {
  /** 今日发运客户数 🟡 */
  customers: number
  /** 最近一票发运（客户 · 单号） */
  latestShipment?: string
}

/** WCS 适配器语义（真实 WCS 无设备号，只有 AdapterType） */
export type WcsAdapterKind = 'stacker' | 'agv' | 'conveyor' | 'hoist'

/** 按适配器聚合的指令口径 🟡（total = queued + running + completed + failed） */
export interface WcsAdapterCell {
  kind: WcsAdapterKind
  label: string
  total: number
  queued: number
  running: number
  completed: number
  failed: number
}

/** WCS 失败指令行 ✅（失败态分页直接可得；重试次数在指令上） */
export interface WcsFailureRow {
  /** 指令号 ✅ */
  cmd: string
  kind: WcsAdapterKind
  /** 适配器语义名（巷道 2 堆垛机 / AGV-07 …） */
  adapter: string
  /** 错误语义 ✅ */
  error: string
  /** 重试次数 ✅ */
  retries: number
  /** 持续时长（分钟）🟡 */
  sinceMin: number
  /** 首次失败时刻 HH:mm ✅ */
  firstAt: string
}

export interface WcsBoard {
  adapters: WcsAdapterCell[]
  /** 状态分布 ✅（跨适配器合计，与 adapters 逐格勾稽） */
  counts: { queued: number; running: number; completed: number; failed: number }
  /** 失败告警榜 ✅（与 counts.failed / kpis.wcsFailed 勾稽） */
  failures: WcsFailureRow[]
}

/** 任务超时榜行 🟡（Open 任务按创建时间算龄期，跨上架/拣货/盘点合并 TOP5） */
export interface OverdueTaskRow {
  id: string
  kind: WhTaskKind
  kindLabel: string
  sku: string
  ageMin: number
}

/** 顶部 KPI（全部与明细区勾稽，见各字段来源） */
export interface WarehouseKpis {
  /** = inbound.pct 🟡 */
  inboundPct: number
  /** = outbound.pct 🟡 */
  outboundPct: number
  /** = pick.backlog ✅ */
  pickBacklog: number
  /** = putaway.backlog ✅ */
  putawayBacklog: number
  /** = wcs.failures.length ✅ */
  wcsFailed: number
  /** = count.variance ✅ */
  countVariance: number
  /** 当日吞吐（行）🟡 = 入库已收行 + 出库已拣配行 */
  throughputLines: number
}

/** /warehouse 仓储物流大屏 */
export interface WarehouseBoard {
  factoryId: string
  kpis: WarehouseKpis
  inbound: InboundProgress
  outbound: OutboundProgress
  pick: WhTaskGroup
  putaway: WhTaskGroup
  count: CycleCountBoard
  wcs: WcsBoard
  overdueTop: OverdueTaskRow[]
}

/** 高频 tick（任务看板 + WCS 3s；主数据 5s）——两者同源纯函数推导，口径必然一致 */
export type WarehouseOpsTick = Pick<
  WarehouseBoard,
  'pick' | 'putaway' | 'count' | 'wcs' | 'overdueTop'
>
