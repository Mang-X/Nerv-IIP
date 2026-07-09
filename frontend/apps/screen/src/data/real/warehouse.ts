// 仓储物流真实取数适配（MAN-467 · 档1样板，照抄 MAN-466 设备屏三段式）：把 business-console
// WMS 作业域 facade（与 Console WMS 域同源，均走 /api/business-console/v1/wms/**）适配进大屏
// 既有 WarehouseBoard / WarehouseOpsTick 契约 —— 契约不变、页面零改动（除轮询/诚实标注）。
//
// **完整聚合口径（两轮 review 修正后）**：后端六个 WMS list 均按 CreatedAtUtc（WCS 按
// DispatchedAtUtc）降序、无日期过滤，只取第一页会漏数。按「事实语义」选过滤 + 翻页到闭包完整：
//   - 当前状态（day-independent，翻页取尽）：积压 `status:'Open'`；WCS **当前失败** `status:'Failed'`、
//     在链 `status:'Dispatched'`。⚠️ 不用 `failed:true`——后端语义是 `FailedAtUtc != null`，WCS 重试后
//     状态回到 Dispatched 但 FailedAtUtc 保留，用它会把已重试在链任务算进失败榜且与在链**双计**。
//   - **今日完成**（真实跨日吞吐，review 修正）：任务/盘点/WCS 元素带 completedAtUtc → 查
//     `status:'Completed'` 按 completedAtUtc 落今日过滤;为界定扫描量,按排序键(createdAtUtc / WCS
//     dispatchedAtUtc)回溯 lookback 天早停(覆盖真实完工时延,含昨日创建今日完成;>lookback 天前创建
//     今日才完工=异常长尾,穷尽吞吐待 #570 后端汇总/完工日期端点)。
//   - 入/出库单 facade **无 completedAtUtc**（仅 createdAtUtc）→ 只能按**当日到货 cohort**（当日创建
//     单据中已完成的）呈现进度,无法做完工日期维度吞吐(待 #570)。
// 诚实占位（不臆造）：入/出库仅文档级(无行级数量)→ linesDone/linesTotal 镜像单据数、页面 real 标注
//   「单」;出库无客户维度→customers 0;WCS 无排队态→queued 0;库存水位/低库存半屏待 #570 不接入。
//   会话空/请求失败即 throw → stale。
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
// 翻页参数：每页 100，安全上限 50 页（5000 条/查询）——真实小厂单查询远小于此，仅防 runaway。
const PAGE_TAKE = 100
const MAX_PAGES = 50
// 今日完成扫描的回溯窗（界定 status:'Completed' 扫描量，覆盖真实完工时延）：
// 任务/盘点可能跨日完工 → 7 天；WCS 秒级 intra-day 作业 → 2 天足够覆盖跨零点边界。
const LOOKBACK_TASK_MS = 7 * 24 * 3_600_000
const LOOKBACK_WCS_MS = 2 * 24 * 3_600_000

const KIND_LABELS: Record<WhTaskKind, string> = { putaway: '上架', pick: '拣货', count: '盘点' }

// —— WMS 领域状态（镜像后端枚举名，序列化为字符串）——
// InboundOrderStatus: Open/Completed/InventoryPostingFailed/PendingQualityCheck
// OutboundOrderStatus: Open/Completed/InventoryPostingFailed/Cancelled
// WarehouseTaskStatus（上架/拣货）: Open/Completed/Cancelled
// CountExecutionStatus: Open/Completed
// WcsTaskStatus: Dispatched/Completed/Failed/Cancelled（重试 Failed→Dispatched，FailedAtUtc 保留）
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
/** 本地零点（ms）。 */
function startOfLocalDay(nowMs: number): number {
  const d = new Date(nowMs)
  d.setHours(0, 0, 0, 0)
  return d.getTime()
}
/** iso 是否早于 boundary（用于翻页早停：newest-first 命中即穿过窗口）。 */
function tsBefore(iso: string | null | undefined, boundaryMs: number): boolean {
  const t = Date.parse(iso ?? '')
  return Number.isFinite(t) && t < boundaryMs
}
/** iso 是否落在本地「今天」（自本地零点起）。 */
function isToday(iso: string | null | undefined, nowMs: number): boolean {
  const t = Date.parse(iso ?? '')
  return Number.isFinite(t) && t >= startOfLocalDay(nowMs)
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

// —— 翻页取数（newest-first）：stopBefore 命中即早停（穿过窗口边界）；取尽(<take)或达上限即止 ——
// SDK throwOnError:true → { data: { success, data: { items } } }。
type ListEnvelope<T> = { data?: { data?: { items?: T[] | null } | null } | null }
async function pageOf<T>(res: Promise<ListEnvelope<T>>): Promise<T[]> {
  return (await res).data?.data?.items ?? []
}
async function paginate<T>(
  fetchPage: (skip: number, take: number) => Promise<T[]>,
  label: string,
  stopBefore?: (item: T) => boolean,
): Promise<T[]> {
  const acc: T[] = []
  for (let page = 0; page < MAX_PAGES; page++) {
    const items = await fetchPage(page * PAGE_TAKE, PAGE_TAKE)
    for (const item of items) {
      if (stopBefore?.(item)) return acc
      acc.push(item)
    }
    if (items.length < PAGE_TAKE) return acc
  }
  console.warn(
    `[screen] WMS ${label} 翻页达安全上限 ${MAX_PAGES * PAGE_TAKE} 条（异常数据量，可能未覆盖全部；正常小厂不触发）。`,
  )
  return acc
}

/**
 * 今日完成（真实跨日吞吐）：查 `status:'Completed'`，按排序键回溯 lookback 天早停（界定扫描量），
 * 再按 completedAtUtc 落今日过滤。捕获「昨日创建今日完成」；>lookback 天前创建今日才完工的异常长尾
 * 需 #570 后端完工日期端点。
 */
async function fetchCompletedToday<T extends { completedAtUtc?: string | null }>(
  fetchPage: (skip: number, take: number) => Promise<T[]>,
  sortKeyOf: (item: T) => string | null | undefined,
  lookbackMs: number,
  nowMs: number,
  label: string,
): Promise<T[]> {
  const boundary = startOfLocalDay(nowMs) - lookbackMs
  const scanned = await paginate(fetchPage, label, (item) => tsBefore(sortKeyOf(item), boundary))
  return scanned.filter((item) => isToday(item.completedAtUtc, nowMs))
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

/**
 * openItems = 全部 Open（`status:'Open'` 翻页取尽，积压完整）；
 * completedToday = 今日完成（completedAtUtc 落今日，含昨日创建今日完成）。
 */
function taskGroupOf(
  openItems: BusinessConsoleWmsWarehouseTaskItem[],
  completedToday: BusinessConsoleWmsWarehouseTaskItem[],
  kind: WhTaskKind,
  nowMs: number,
): WhTaskGroup {
  const rows = openItems.map((t) => taskRowOf(t, kind, nowMs)).sort((a, b) => b.ageMin - a.ageMin)
  // 今日创建 = 今日创建的 open + 今日创建的今日完成（两集不相交）。
  const createdToday = [...openItems, ...completedToday].filter((t) =>
    isToday(t.createdAtUtc, nowMs),
  ).length
  return {
    kind,
    backlog: rows.length,
    doneToday: completedToday.length,
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
  openItems: BusinessConsoleWmsCountExecutionItem[],
  completedToday: BusinessConsoleWmsCountExecutionItem[],
  nowMs: number,
): CycleCountBoard {
  const rows = openItems.map((c) => countRowOf(c, nowMs)).sort((a, b) => b.ageMin - a.ageMin)
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

/**
 * failedItems = **当前失败** `status:'Failed'`（不含已重试回 Dispatched 的旧失败）；
 * runningItems = 在链 `status:'Dispatched'`（含重试后回到在链的任务）；
 * completedToday = 今日完成（completedAtUtc 落今日）。状态分布无 queued 态（诚实，不臆造排队量）。
 */
function wcsBoardOf(
  failedItems: BusinessConsoleWmsWcsTaskItem[],
  runningItems: BusinessConsoleWmsWcsTaskItem[],
  completedToday: BusinessConsoleWmsWcsTaskItem[],
  nowMs: number,
): WcsBoard {
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
  for (const t of runningItems) cell(normalizeAdapter(t.adapterType)).running++
  for (const t of failedItems) cell(normalizeAdapter(t.adapterType)).failed++
  for (const t of completedToday) cell(normalizeAdapter(t.adapterType)).completed++
  const adapters = [...byKind.values()]
    .map((c) => ({ ...c, total: c.queued + c.running + c.completed + c.failed }))
    .sort((a, b) => b.total - a.total)
  const counts = {
    queued: 0,
    running: adapters.reduce((n, a) => n + a.running, 0),
    completed: adapters.reduce((n, a) => n + a.completed, 0),
    failed: adapters.reduce((n, a) => n + a.failed, 0),
  }
  const failures = failedItems
    .map((t) => wcsFailureOf(t, nowMs))
    .sort((a, b) => b.retries - a.retries || b.sinceMin - a.sinceMin)
  return { adapters, counts, failures }
}

// —— 入库 / 出库（文档级，无 completedAtUtc → 当日到货 cohort：当日创建单中已完成的）——
function inboundOf(
  todayItems: BusinessConsoleWmsInboundOrderItem[],
  nowMs: number,
): InboundProgress {
  const docsTotal = todayItems.length
  const docsDone = todayItems.filter((o) => eq(o.status, 'Completed')).length
  const failed = todayItems.filter((o) => eq(o.status, 'InventoryPostingFailed'))
  const { hourly, hourLabels } = hourlyBy(todayItems, nowMs, (o) => o.createdAtUtc)
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

function outboundOf(
  todayItems: BusinessConsoleWmsOutboundOrderItem[],
  nowMs: number,
): OutboundProgress {
  const docsTotal = todayItems.length
  const docsDone = todayItems.filter((o) => eq(o.status, 'Completed')).length
  const { hourly, hourLabels } = hourlyBy(todayItems, nowMs, (o) => o.createdAtUtc)
  // 最近一票发运 = 最新已完成（发运）出库单（newest-first，取首个 Completed）。
  const latest = todayItems.find((o) => eq(o.status, 'Completed'))?.outboundOrderNo?.trim()
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

function requireSession(): { organizationId: string; environmentId: string } {
  if (!hasScreenSession()) {
    throw new Error('大屏会话上下文未就绪（organizationId/environmentId 为空）')
  }
  const { organizationId, environmentId } = getScreenSession()
  return { organizationId, environmentId }
}

type Ctx = { organizationId: string; environmentId: string }

// —— 各作业域「取尽 open 积压 + 今日完成」翻页闭包 ——
function fetchTaskGroup(
  list: typeof listBusinessConsoleWmsPutawayTasks,
  ctx: Ctx,
  kind: WhTaskKind,
  nowMs: number,
): Promise<WhTaskGroup> {
  const label = KIND_LABELS[kind]
  return Promise.all([
    paginate(
      (skip, take) =>
        pageOf(list({ throwOnError: true, query: { ...ctx, status: 'Open', skip, take } })),
      `${label}(Open)`,
    ),
    fetchCompletedToday(
      (skip, take) =>
        pageOf(list({ throwOnError: true, query: { ...ctx, status: 'Completed', skip, take } })),
      (t) => t.createdAtUtc,
      LOOKBACK_TASK_MS,
      nowMs,
      `${label}(今日完成)`,
    ),
  ]).then(([open, done]) => taskGroupOf(open, done, kind, nowMs))
}

function fetchCountBoard(ctx: Ctx, nowMs: number): Promise<CycleCountBoard> {
  return Promise.all([
    paginate(
      (skip, take) =>
        pageOf(
          listBusinessConsoleWmsCountExecutions({
            throwOnError: true,
            query: { ...ctx, status: 'Open', skip, take },
          }),
        ),
      '盘点(Open)',
    ),
    fetchCompletedToday(
      (skip, take) =>
        pageOf(
          listBusinessConsoleWmsCountExecutions({
            throwOnError: true,
            query: { ...ctx, status: 'Completed', skip, take },
          }),
        ),
      (c) => c.createdAtUtc,
      LOOKBACK_TASK_MS,
      nowMs,
      '盘点(今日完成)',
    ),
  ]).then(([open, done]) => countBoardOf(open, done, nowMs))
}

function fetchWcsBoard(ctx: Ctx, nowMs: number): Promise<WcsBoard> {
  return Promise.all([
    paginate(
      (skip, take) =>
        pageOf(
          listBusinessConsoleWmsWcsTasks({
            throwOnError: true,
            query: { ...ctx, status: 'Failed', skip, take },
          }),
        ),
      'WCS(当前失败)',
    ),
    paginate(
      (skip, take) =>
        pageOf(
          listBusinessConsoleWmsWcsTasks({
            throwOnError: true,
            query: { ...ctx, status: 'Dispatched', skip, take },
          }),
        ),
      'WCS(在链)',
    ),
    fetchCompletedToday(
      (skip, take) =>
        pageOf(
          listBusinessConsoleWmsWcsTasks({
            throwOnError: true,
            query: { ...ctx, status: 'Completed', skip, take },
          }),
        ),
      (w) => w.dispatchedAtUtc,
      LOOKBACK_WCS_MS,
      nowMs,
      'WCS(今日完成)',
    ),
  ]).then(([failed, running, done]) => wcsBoardOf(failed, running, done, nowMs))
}

function fetchInboundToday(ctx: Ctx, nowMs: number): Promise<InboundProgress> {
  return paginate(
    (skip, take) =>
      pageOf(
        listBusinessConsoleWmsInboundOrders({ throwOnError: true, query: { ...ctx, skip, take } }),
      ),
    '入库单(当日)',
    (o) => tsBefore(o.createdAtUtc, startOfLocalDay(nowMs)),
  ).then((items) => inboundOf(items, nowMs))
}

function fetchOutboundToday(ctx: Ctx, nowMs: number): Promise<OutboundProgress> {
  return paginate(
    (skip, take) =>
      pageOf(
        listBusinessConsoleWmsOutboundOrders({ throwOnError: true, query: { ...ctx, skip, take } }),
      ),
    '出库单(当日)',
    (o) => tsBefore(o.createdAtUtc, startOfLocalDay(nowMs)),
  ).then((items) => outboundOf(items, nowMs))
}

/** 主数据全景：入库/出库进度 + 上架/拣货/盘点 + WCS（10s 轮询，完整闭包聚合）。 */
export async function fetchRealWarehouseBoard(factoryId = 'F01'): Promise<WarehouseBoard> {
  const ctx = requireSession()
  const nowMs = Date.now()

  const [inbound, outbound, putaway, pick, count, wcs] = await Promise.all([
    fetchInboundToday(ctx, nowMs),
    fetchOutboundToday(ctx, nowMs),
    fetchTaskGroup(listBusinessConsoleWmsPutawayTasks, ctx, 'putaway', nowMs),
    fetchTaskGroup(listBusinessConsoleWmsPickingTasks, ctx, 'pick', nowMs),
    fetchCountBoard(ctx, nowMs),
    fetchWcsBoard(ctx, nowMs),
  ])
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

/** 任务看板 + WCS 高频 tick（15s 轮询）：只刷作业子集，与主板同源完整闭包口径一致。 */
export async function fetchRealWarehouseOpsTick(_factoryId = 'F01'): Promise<WarehouseOpsTick> {
  const ctx = requireSession()
  const nowMs = Date.now()

  const [putaway, pick, count, wcs] = await Promise.all([
    fetchTaskGroup(listBusinessConsoleWmsPutawayTasks, ctx, 'putaway', nowMs),
    fetchTaskGroup(listBusinessConsoleWmsPickingTasks, ctx, 'pick', nowMs),
    fetchCountBoard(ctx, nowMs),
    fetchWcsBoard(ctx, nowMs),
  ])
  const overdueTop = overdueTopOf([pick, putaway], count)

  return { pick, putaway, count, wcs, overdueTop }
}
