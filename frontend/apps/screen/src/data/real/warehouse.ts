// 仓储物流真实取数适配（MAN-467 · 档1样板，照抄 MAN-466 设备屏三段式）：把 business-console
// WMS 作业域 facade（与 Console WMS 域同源，均走 /api/business-console/v1/wms/**）适配进大屏
// 既有 WarehouseBoard / WarehouseOpsTick 契约 —— 契约不变、页面零改动（除轮询/诚实标注）。
//
// 覆盖（档1，六个分页 list facade 齐全）：入库/出库单（文档级进度）、上架/拣货/盘点任务
//   （积压/龄期/超时按真实 createdAtUtc 计算）、WCS 指令（状态分布 + 失败榜，按真实时间戳龄期）。
// 诚实占位（不臆造）：
//   - 入库/出库 facade 仅文档级（单号/状态/createdAtUtc），无行级数量 → linesDone/linesTotal
//     镜像单据数（口径为「单」而非「行」，页面 real 模式相应标注）；小时流量按订单到达时刻聚合。
//   - 出库无客户维度 → customers 置 0（页面 real 模式隐藏「客户 N 家」），latestShipment 取最新单号。
//   - 库存水位/低库存预警半屏仍缺 Inventory 读面聚合 → 不接入（待 #570），页面维持占位标注。
import {
  listBusinessConsoleWmsCountExecutions,
  listBusinessConsoleWmsInboundOrders,
  listBusinessConsoleWmsOutboundOrders,
  listBusinessConsoleWmsPickingTasks,
  listBusinessConsoleWmsPutawayTasks,
  listBusinessConsoleWmsWcsTasks,
  type BusinessConsoleWmsCountExecutionItem,
  type BusinessConsoleWmsInboundOrderItem,
  type BusinessConsoleWmsOutboundOrderItem,
  type BusinessConsoleWmsWarehouseTaskItem,
  type BusinessConsoleWmsWcsTaskItem,
} from '@nerv-iip/api-client'
import type {
  CycleCountBoard,
  InboundProgress,
  OutboundProgress,
  OverdueTaskRow,
  WarehouseBoard,
  WarehouseKpis,
  WarehouseOpsTick,
  WcsAdapterCell,
  WcsAdapterKind,
  WcsBoard,
  WcsFailureRow,
  WhTaskGroup,
  WhTaskKind,
  WhTaskRow,
} from '@/data/contracts/warehouse'
import { getScreenSession, hasScreenSession } from '@/data/session'

// 超时阈值（分钟）——与契约「龄期 > 45min」及 mock OVERDUE_MIN 同口径。
const OVERDUE_MIN = 45
// 单批拉取上限：WMS list 按 CreatedAtUtc 降序（newest-first），小厂当日作业量足够；
// 命中上限时告警（不静默截断，见 roster ROSTER_MAX 同款诚实约束）。
const LIST_TAKE = 200

const KIND_LABELS: Record<WhTaskKind, string> = { putaway: '上架', pick: '拣货', count: '盘点' }

// —— WMS 领域状态（镜像后端枚举名，序列化为字符串）——
// InboundOrderStatus: Open/Completed/InventoryPostingFailed/PendingQualityCheck
// OutboundOrderStatus: Open/Completed/InventoryPostingFailed/Cancelled
// WarehouseTaskStatus（上架/拣货）: Open/Completed/Cancelled
// CountExecutionStatus: Open/Completed
// WcsTaskStatus: Dispatched/Completed/Failed/Cancelled
function eq(status: string | null | undefined, name: string): boolean {
  return (status ?? '').trim().toLowerCase() === name.toLowerCase()
}

function pad2(n: number): string {
  return String(n).padStart(2, '0')
}
/** ISO → 本地 HH:mm；空/非法为「—」。 */
function clockOf(iso: string | null | undefined): string {
  if (!iso) return '—'
  const t = Date.parse(iso)
  if (!Number.isFinite(t)) return '—'
  const d = new Date(t)
  return `${pad2(d.getHours())}:${pad2(d.getMinutes())}`
}
/** 龄期（分钟）= now − iso；空/非法为 0。 */
function minutesSince(iso: string | null | undefined, nowMs: number): number {
  if (!iso) return 0
  const t = Date.parse(iso)
  if (!Number.isFinite(t)) return 0
  return Math.max(0, Math.round((nowMs - t) / 60_000))
}
/** iso 是否落在本地「今天」（自本地零点起）。 */
function isToday(iso: string | null | undefined, nowMs: number): boolean {
  if (!iso) return false
  const t = Date.parse(iso)
  if (!Number.isFinite(t)) return false
  const start = new Date(nowMs)
  start.setHours(0, 0, 0, 0)
  return t >= start.getTime()
}

/** 近 12h 每小时计数（按 iso 落桶；桶尾整点为标签），Σ = 落在 12h 窗内的条目数。 */
function hourlyBy<T>(
  items: T[],
  nowMs: number,
  getIso: (item: T) => string | null | undefined,
): { hourly: number[]; hourLabels: string[] } {
  const hourly = new Array(12).fill(0) as number[]
  const hourLabels: string[] = []
  const windowStart = nowMs - 12 * 3_600_000
  for (let i = 0; i < 12; i++) {
    const end = new Date(windowStart + (i + 1) * 3_600_000)
    hourLabels.push(`${pad2(end.getHours())}:00`)
  }
  for (const item of items) {
    const iso = getIso(item)
    if (!iso) continue
    const t = Date.parse(iso)
    if (!Number.isFinite(t)) continue
    const idx = Math.floor((t - windowStart) / 3_600_000)
    if (idx >= 0 && idx < 12) hourly[idx]++
  }
  return { hourly, hourLabels }
}

function warnIfTruncated(label: string, len: number): void {
  if (len >= LIST_TAKE) {
    console.warn(
      `[screen] WMS ${label} 返回条数达单批上限 ${LIST_TAKE}，看板可能未覆盖全部作业（小厂样板足够，超出等 #570 汇总端点）。`,
    )
  }
}

// —— WMS 分页 list 取数（SDK throwOnError:true → { data: { success, data: { items } } }）——
function pageItems<T>(items: T[] | null | undefined, label: string): T[] {
  const list = items ?? []
  warnIfTruncated(label, list.length)
  return list
}

/** 六个 WMS list facade 共用 query：org/env + 单批上限。 */
function listQuery() {
  const { organizationId, environmentId } = getScreenSession()
  return { organizationId, environmentId, skip: 0, take: LIST_TAKE }
}

// —— 上架 / 拣货任务（WarehouseTask：Open/Completed/Cancelled）——
function taskRowOf(
  t: BusinessConsoleWmsWarehouseTaskItem,
  kind: WhTaskKind,
  nowMs: number,
): WhTaskRow {
  const ageMin = minutesSince(t.createdAtUtc, nowMs)
  return {
    id: t.taskNo?.trim() || t.warehouseTaskId?.trim() || '—',
    kind,
    sku: t.skuCode?.trim() || '—',
    qty: t.plannedQuantity ?? 0,
    unit: t.uomCode?.trim() || '',
    from: t.fromLocationCode?.trim() || '—',
    to: t.toLocationCode?.trim() || undefined,
    ref: t.sourceOrderNo?.trim() || undefined,
    createdAt: clockOf(t.createdAtUtc),
    ageMin,
    overdue: ageMin > OVERDUE_MIN,
  }
}

function taskGroupOf(
  items: BusinessConsoleWmsWarehouseTaskItem[],
  kind: WhTaskKind,
  nowMs: number,
): WhTaskGroup {
  const open = items.filter((t) => eq(t.status, 'Open'))
  const rows = open.map((t) => taskRowOf(t, kind, nowMs)).sort((a, b) => b.ageMin - a.ageMin)
  const doneToday = items.filter(
    (t) => eq(t.status, 'Completed') && isToday(t.completedAtUtc, nowMs),
  ).length
  const createdToday = items.filter(
    (t) => !eq(t.status, 'Cancelled') && isToday(t.createdAtUtc, nowMs),
  ).length
  return {
    kind,
    backlog: rows.length,
    doneToday,
    createdToday,
    overdue: rows.filter((r) => r.overdue).length,
    rows,
  }
}

// —— 盘点执行（CountExecution：Open/Completed）——
function countRowOf(c: BusinessConsoleWmsCountExecutionItem, nowMs: number): WhTaskRow {
  const ageMin = minutesSince(c.createdAtUtc, nowMs)
  return {
    id: c.countNo?.trim() || c.countExecutionId?.trim() || '—',
    kind: 'count',
    sku: c.skuCode?.trim() || '—',
    // 盘点行数量口径为账面数量（expectedQuantity）。
    qty: c.expectedQuantity ?? 0,
    unit: c.uomCode?.trim() || '',
    from: c.locationCode?.trim() || '—',
    to: undefined,
    ref: undefined,
    createdAt: clockOf(c.createdAtUtc),
    ageMin,
    overdue: ageMin > OVERDUE_MIN,
  }
}

function countBoardOf(
  items: BusinessConsoleWmsCountExecutionItem[],
  nowMs: number,
): CycleCountBoard {
  const open = items.filter((c) => eq(c.status, 'Open'))
  const rows = open.map((c) => countRowOf(c, nowMs)).sort((a, b) => b.ageMin - a.ageMin)
  const completedToday = items.filter(
    (c) => eq(c.status, 'Completed') && isToday(c.completedAtUtc, nowMs),
  )
  const counted = completedToday.length
  const variance = completedToday.filter((c) => (c.varianceQuantity ?? 0) !== 0).length
  return {
    // 库位数口径：planned = 已盘（今日完成）+ 未盘（Open 任务）。
    planned: counted + rows.length,
    counted,
    variance,
    overdue: rows.filter((r) => r.overdue).length,
    rows,
  }
}

// —— WCS 指令（Dispatched/Completed/Failed/Cancelled；adapterType 自由字符串）——
const WCS_KIND_LABELS: Record<WcsAdapterKind, string> = {
  stacker: '巷道堆垛机',
  agv: 'AGV 调度',
  shuttle: '四向穿梭车',
  conveyor: '输送线',
  sorter: '分拣机',
  hoist: '提升机',
}
/** adapterType 自由字符串 → 六类语义；未知归入输送线（通用搬运，不臆造具体设备）。 */
function normalizeAdapter(adapterType: string | null | undefined): WcsAdapterKind {
  const s = (adapterType ?? '').toLowerCase()
  if (/stack|堆垛|asrs|crane/.test(s)) return 'stacker'
  if (/agv|amr|forklift|叉车/.test(s)) return 'agv'
  if (/shuttle|穿梭|四向/.test(s)) return 'shuttle'
  if (/sort|分拣|divert/.test(s)) return 'sorter'
  if (/hoist|提升|elevat|lift/.test(s)) return 'hoist'
  if (/conveyor|输送|belt|roller/.test(s)) return 'conveyor'
  return 'conveyor'
}

function wcsFailureOf(t: BusinessConsoleWmsWcsTaskItem, nowMs: number): WcsFailureRow {
  const kind = normalizeAdapter(t.adapterType)
  const failedIso = t.failedAtUtc ?? t.dispatchedAtUtc
  return {
    cmd: t.externalTaskId?.trim() || t.wcsTaskId?.trim() || '—',
    kind,
    adapter: t.adapterType?.trim() || WCS_KIND_LABELS[kind],
    error: t.failureMessage?.trim() || t.failureCode?.trim() || '设备异常',
    retries: t.attemptCount ?? 0,
    sinceMin: minutesSince(failedIso, nowMs),
    firstAt: clockOf(failedIso),
  }
}

function wcsBoardOf(items: BusinessConsoleWmsWcsTaskItem[], nowMs: number): WcsBoard {
  // 状态分布：模型无 queued 态（Dispatched=在链执行），故 queued 恒 0（诚实，不臆造排队量）。
  const byKind = new Map<WcsAdapterKind, WcsAdapterCell>()
  const cell = (kind: WcsAdapterKind): WcsAdapterCell => {
    let c = byKind.get(kind)
    if (!c) {
      c = {
        kind,
        label: WCS_KIND_LABELS[kind],
        total: 0,
        queued: 0,
        running: 0,
        completed: 0,
        failed: 0,
      }
      byKind.set(kind, c)
    }
    return c
  }
  for (const t of items) {
    const c = cell(normalizeAdapter(t.adapterType))
    if (eq(t.status, 'Dispatched')) c.running++
    else if (eq(t.status, 'Completed')) c.completed++
    else if (eq(t.status, 'Failed')) c.failed++
    // Cancelled 不计入在链/完成/失败分布。
  }
  const adapters = [...byKind.values()]
    .map((c) => ({ ...c, total: c.queued + c.running + c.completed + c.failed }))
    .sort((a, b) => b.total - a.total)
  const counts = {
    queued: 0,
    running: adapters.reduce((n, a) => n + a.running, 0),
    completed: adapters.reduce((n, a) => n + a.completed, 0),
    failed: adapters.reduce((n, a) => n + a.failed, 0),
  }
  const failures = items
    .filter((t) => eq(t.status, 'Failed'))
    .map((t) => wcsFailureOf(t, nowMs))
    .sort((a, b) => b.retries - a.retries || b.sinceMin - a.sinceMin)
  return { adapters, counts, failures }
}

// —— 入库 / 出库（文档级：Open/Completed/InventoryPostingFailed/…）——
function inboundOf(items: BusinessConsoleWmsInboundOrderItem[], nowMs: number): InboundProgress {
  // 当日入库范围 = createdAtUtc 落在今天的收货单（newest-first，今日单据在页首）。
  const today = items.filter((o) => isToday(o.createdAtUtc, nowMs))
  const docsTotal = today.length
  const docsDone = today.filter((o) => eq(o.status, 'Completed')).length
  const failed = today.filter((o) => eq(o.status, 'InventoryPostingFailed'))
  const { hourly, hourLabels } = hourlyBy(today, nowMs, (o) => o.createdAtUtc)
  return {
    docsDone,
    docsTotal,
    // facade 无行级数量 → 行口径镜像单据数（页面 real 模式标注为「单」）。
    linesDone: docsDone,
    linesTotal: docsTotal,
    pct: docsTotal > 0 ? Math.round((docsDone / docsTotal) * 100) : 0,
    hourly,
    hourLabels,
    postFailedDocs: failed.length,
    postFailedDoc: failed[0]?.inboundOrderNo?.trim() || undefined,
  }
}

function outboundOf(items: BusinessConsoleWmsOutboundOrderItem[], nowMs: number): OutboundProgress {
  const today = items.filter((o) => isToday(o.createdAtUtc, nowMs))
  const docsTotal = today.length
  const docsDone = today.filter((o) => eq(o.status, 'Completed')).length
  const { hourly, hourLabels } = hourlyBy(today, nowMs, (o) => o.createdAtUtc)
  // 最近一票发运 = 最新已完成（发运）出库单（newest-first，取首个 Completed）。
  const latest = today.find((o) => eq(o.status, 'Completed'))?.outboundOrderNo?.trim()
  return {
    docsDone,
    docsTotal,
    linesDone: docsDone,
    linesTotal: docsTotal,
    pct: docsTotal > 0 ? Math.round((docsDone / docsTotal) * 100) : 0,
    hourly,
    hourLabels,
    // 出库 facade 无客户维度 → 0（页面 real 模式隐藏「客户 N 家」）。
    customers: 0,
    latestShipment: latest ? `发运单 ${latest}` : undefined,
  }
}

function overdueTopOf(groups: WhTaskGroup[], count: CycleCountBoard): OverdueTaskRow[] {
  return [...groups.flatMap((g) => g.rows), ...count.rows]
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
}

function requireSession(): void {
  if (!hasScreenSession()) {
    throw new Error('大屏会话上下文未就绪（organizationId/environmentId 为空）')
  }
}

/** 主数据全景：入库/出库进度 + 上架/拣货/盘点 + WCS（10s 轮询）。 */
export async function fetchRealWarehouseBoard(factoryId = 'F01'): Promise<WarehouseBoard> {
  requireSession()
  const nowMs = Date.now()
  const query = listQuery()

  const [inRes, outRes, ptRes, pkRes, ccRes, wcsRes] = await Promise.all([
    listBusinessConsoleWmsInboundOrders({ throwOnError: true, query }),
    listBusinessConsoleWmsOutboundOrders({ throwOnError: true, query }),
    listBusinessConsoleWmsPutawayTasks({ throwOnError: true, query }),
    listBusinessConsoleWmsPickingTasks({ throwOnError: true, query }),
    listBusinessConsoleWmsCountExecutions({ throwOnError: true, query }),
    listBusinessConsoleWmsWcsTasks({ throwOnError: true, query }),
  ])

  const inbound = inboundOf(pageItems(inRes.data?.data?.items, '入库单'), nowMs)
  const outbound = outboundOf(pageItems(outRes.data?.data?.items, '出库单'), nowMs)
  const putaway = taskGroupOf(pageItems(ptRes.data?.data?.items, '上架任务'), 'putaway', nowMs)
  const pick = taskGroupOf(pageItems(pkRes.data?.data?.items, '拣货任务'), 'pick', nowMs)
  const count = countBoardOf(pageItems(ccRes.data?.data?.items, '盘点执行'), nowMs)
  const wcs = wcsBoardOf(pageItems(wcsRes.data?.data?.items, 'WCS 指令'), nowMs)
  const overdueTop = overdueTopOf([pick, putaway], count)

  const kpis: WarehouseKpis = {
    inboundPct: inbound.pct,
    outboundPct: outbound.pct,
    pickBacklog: pick.backlog,
    putawayBacklog: putaway.backlog,
    wcsFailed: wcs.failures.length,
    countVariance: count.variance,
    throughputLines: inbound.linesDone + outbound.linesDone,
  }

  return { factoryId, kpis, inbound, outbound, pick, putaway, count, wcs, overdueTop }
}

/** 任务看板 + WCS 高频 tick（15s 轮询）：只刷作业子集，与主板同源口径一致。 */
export async function fetchRealWarehouseOpsTick(_factoryId = 'F01'): Promise<WarehouseOpsTick> {
  requireSession()
  const nowMs = Date.now()
  const query = listQuery()

  const [ptRes, pkRes, ccRes, wcsRes] = await Promise.all([
    listBusinessConsoleWmsPutawayTasks({ throwOnError: true, query }),
    listBusinessConsoleWmsPickingTasks({ throwOnError: true, query }),
    listBusinessConsoleWmsCountExecutions({ throwOnError: true, query }),
    listBusinessConsoleWmsWcsTasks({ throwOnError: true, query }),
  ])

  const putaway = taskGroupOf(pageItems(ptRes.data?.data?.items, '上架任务'), 'putaway', nowMs)
  const pick = taskGroupOf(pageItems(pkRes.data?.data?.items, '拣货任务'), 'pick', nowMs)
  const count = countBoardOf(pageItems(ccRes.data?.data?.items, '盘点执行'), nowMs)
  const wcs = wcsBoardOf(pageItems(wcsRes.data?.data?.items, 'WCS 指令'), nowMs)
  const overdueTop = overdueTopOf([pick, putaway], count)

  return { pick, putaway, count, wcs, overdueTop }
}
