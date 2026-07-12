// 设备监控真实取数适配（MAN-466 · 档1样板）：把 business-console 设备域 facade
// （与 Console 设备域同源，均走 /api/business-console/v1/**）适配进大屏既有
// EquipmentOverview / DeviceDetail 契约 —— 契约不变、页面零改动。
//
// 覆盖（档1，现有明细 facade 完全够）：设备清单/状态+新鲜度、活动报警(#686 ack)、
//   维修工单、PM 计划、点检台账、单机 MTBF/MTTR。
// 诚实占位（不臆造）：格上/趋势实时参数（historian 待 #570/#689）→ 空；
//   聚合 MTBF/MTTR 无单一端点 → null（单机值见设备详情）；OEE 性能率/质量率 → #738。
import {
  getBusinessConsoleEquipmentDevice,
  getBusinessConsoleEquipmentOverview,
  listBusinessConsoleEquipmentAlarms,
  listBusinessConsoleMaintenanceInspections,
  listBusinessConsoleMaintenancePlans,
  listBusinessConsoleMaintenanceWorkOrders,
  queryBusinessConsoleMaintenanceAssetReliability,
  queryBusinessConsoleTelemetryOee,
  type BusinessConsoleMaintenanceInspectionItem,
  type BusinessConsoleMaintenancePlanItem,
  type BusinessConsoleMaintenanceWorkOrderItem,
  type BusinessConsoleTelemetryAlarmEventItem,
} from '@nerv-iip/api-client'
import type {
  DeviceCell,
  DeviceDetail,
  DeviceParamsTick,
  DeviceState,
  EquipmentOverview,
  InspectionRow,
  OpenAlarmRow,
  PmTask,
  Reliability,
  RepairOrder,
  RepairStage,
  StateCounts,
} from '@/data/contracts/equipment'
import {
  type DeviceRoster,
  getScreenSession,
  hasScreenSession,
  resolveDeviceRoster,
} from '@/data/session'

const STATE_LABELS: Record<DeviceState, string> = {
  run: '运行',
  idle: '待机',
  down: '停机',
  alarm: '报警',
  offline: '断线',
}

// —— 设备状态归一（镜像后端 EquipmentRuntimeDeviceStates 分类，映射到大屏五态）——
const PRODUCTIVE = new Set([
  'running',
  'run',
  'operating',
  'active',
  'producing',
  'machining',
  'in-production',
  '运行',
  '运行中',
  '加工',
  '生产中',
])
const LOADING = new Set([
  'available',
  'idle',
  'ready',
  'standby',
  'waiting',
  '就绪',
  '空闲',
  '待机',
])
const PLANNED_DOWN = new Set([
  'planned-down',
  'planned-stop',
  'planned-maintenance',
  'maintenance-window',
  'scheduled-maintenance',
  'planned-outage',
  'pm',
  '计划停机',
  '计划维护',
  '预防维护',
])
const UNAVAILABLE = new Set([
  'stopped',
  'stop',
  'down',
  'faulted',
  'fault',
  'error',
  'unavailable',
  'breakdown',
  'unplanned-down',
  'emergency-stop',
  'offline',
  '停止',
  '停机',
  '故障',
  '离线',
])

function normalizeState(state: string | null | undefined): string {
  if (!state) return ''
  let n = state
    .trim()
    .toLowerCase()
    .replace(/[_\s]+/g, '-')
  while (n.includes('--')) n = n.replace('--', '-')
  return n
}

interface MappedState {
  state: DeviceState
  planned: boolean
  unknown: boolean
}
/**
 * 断线优先（IsSourceFresh=false → offline，防假绿）；有活动报警 → alarm；否则按后端
 * 状态分类映射。未知且在线 → idle（中性待机：不假绿、不误报停机）。
 */
function mapState(
  currentState: string | null | undefined,
  isSourceFresh: boolean,
  activeAlarmCount: number,
): MappedState {
  if (!isSourceFresh) return { state: 'offline', planned: false, unknown: false }
  if (activeAlarmCount > 0) return { state: 'alarm', planned: false, unknown: false }
  const n = normalizeState(currentState)
  if (PRODUCTIVE.has(n)) return { state: 'run', planned: false, unknown: false }
  if (LOADING.has(n)) return { state: 'idle', planned: false, unknown: false }
  if (PLANNED_DOWN.has(n)) return { state: 'idle', planned: true, unknown: false }
  if (UNAVAILABLE.has(n)) return { state: 'down', planned: false, unknown: false }
  return { state: 'idle', planned: false, unknown: true }
}

// 可用性阻塞原因码 → 中文（与 business-console 设备域同口径）。
const REASON_LABELS: Record<string, string> = {
  'equipment.activeAlarm': '设备报警未解除',
  'equipment.stateUnavailable': '设备状态不可运行',
  'equipment.downtime': '设备停机中',
  'equipment.maintenanceWindow': '维修保养占用',
  'equipment.inspectionRequired': '点检未通过',
  'equipment.sourceStale': '采集数据过期',
  'equipment.tagMappingMissing': '采集点未配置',
  'equipment.noEligibleSubstitute': '无可替代设备',
}
const BLOCK_SEVERITY_RANK: Record<string, number> = { critical: 0, blocked: 1, warning: 2, info: 3 }

function isSevereAlarm(severity: string | null | undefined): boolean {
  const s = (severity ?? '').toLowerCase()
  return /crit|high|sev|fatal|emerg|danger|alarm/.test(s)
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
function minutesSince(iso: string | null | undefined, nowMs: number): number {
  if (!iso) return 0
  const t = Date.parse(iso)
  if (!Number.isFinite(t)) return 0
  return Math.max(0, Math.round((nowMs - t) / 60_000))
}
function dateOf(iso: string | null | undefined): string {
  if (!iso) return '—'
  const t = Date.parse(iso)
  if (!Number.isFinite(t)) return iso
  const d = new Date(t)
  return `${d.getFullYear()}-${pad2(d.getMonth() + 1)}-${pad2(d.getDate())}`
}

function nameOf(roster: DeviceRoster, deviceAssetId: string | null | undefined): string {
  if (!deviceAssetId) return '—'
  return roster.byId.get(deviceAssetId)?.name ?? deviceAssetId
}

function alarmRow(
  a: BusinessConsoleTelemetryAlarmEventItem,
  roster: DeviceRoster,
  woByAlarm: Map<string, string>,
): OpenAlarmRow {
  const acked = Boolean(a.acknowledgedAtUtc)
  const cleared = Boolean(a.clearedAtUtc)
  const shelved = Boolean(a.shelvedAtUtc)
  const escalated = Boolean(a.escalatedAtUtc)
  // status 文案：'已恢复…' 前缀被页面用于过滤（合并事件流不显已恢复项），须保持。
  const status = cleared
    ? '已恢复待确认'
    : shelved
      ? '已搁置'
      : acked
        ? '已确认 · 处理中'
        : '未恢复'
  return {
    time: clockOf(a.raisedAtUtc),
    line: nameOf(roster, a.deviceAssetId),
    level: isSevereAlarm(a.severity) ? 'sev' : 'gen',
    name: a.alarmCode?.trim() || '设备报警',
    wo: (a.alarmEventId && woByAlarm.get(a.alarmEventId)) || '—',
    status,
    acked,
    ackBy: a.acknowledgedBy ?? undefined,
    escalated,
  }
}

function isCompletedWorkOrder(status: string | null | undefined): boolean {
  const s = (status ?? '').toLowerCase()
  return s.includes('complete') || s === '1'
}

function repairRow(
  w: BusinessConsoleMaintenanceWorkOrderItem,
  roster: DeviceRoster,
  nowMs: number,
): RepairOrder {
  const completed = isCompletedWorkOrder(w.status)
  const hasTechnician = Boolean(w.assignedTechnicianUserId)
  const stage: RepairStage = completed ? '已关闭' : hasTechnician ? '维修中' : '已派工'
  const elapsedMin = minutesSince(w.openedAtUtc, nowMs)
  const est = w.estimatedLaborMinutes ?? null
  const overdue = !completed && est !== null && elapsedMin > est
  return {
    wo: w.workOrderId?.trim() || '—',
    device: nameOf(roster, w.deviceAssetId),
    issue: w.sourceAlarmId ? '报警触发维修' : '计划维修',
    stage,
    reportedAt: clockOf(w.openedAtUtc),
    elapsedMin: Math.max(1, elapsedMin),
    etaText: est !== null ? `预计 ${est} min` : '—',
    overdue,
    // 工单状态仅 Open/Completed，facade 无独立"待确认"标记 → 诚实置 false。
    awaitingConfirm: false,
    assignee: w.assignedTechnicianUserId?.trim() || '未指派',
  }
}

function pmRow(p: BusinessConsoleMaintenancePlanItem, roster: DeviceRoster): PmTask {
  const dueParts = [dateOf(p.startsOn), p.interval?.trim()].filter(Boolean)
  return {
    device: nameOf(roster, p.deviceAssetId),
    task: p.planCode?.trim() || '保养计划',
    due: dueParts.join(' · ') || '—',
    // 计划列表无到期实例判定 → 诚实标 due（不臆造 overdue/done）。
    state: 'due',
  }
}

function isPassResult(result: string | null | undefined): boolean {
  const s = (result ?? '').toLowerCase()
  return /pass|ok|accept|合格|通过|正常/.test(s)
}

function inspectionRow(
  i: BusinessConsoleMaintenanceInspectionItem,
  roster: DeviceRoster,
  deviceByWorkOrder: Map<string, string>,
  deviceByPlan: Map<string, string>,
): InspectionRow {
  const deviceAssetId =
    (i.workOrderId && deviceByWorkOrder.get(i.workOrderId)) ||
    (i.planId && deviceByPlan.get(i.planId)) ||
    ''
  return {
    time: clockOf(i.inspectedAtUtc),
    device: deviceAssetId ? nameOf(roster, deviceAssetId) : '—',
    item: i.measurements?.[0]?.characteristicCode?.trim() || '点检',
    by: i.inspector?.trim() || '—',
    result: isPassResult(i.result) ? '合格' : '异常',
  }
}

function deviceCellOf(
  roster: DeviceRoster,
  deviceAssetId: string,
  currentState: string | null | undefined,
  isSourceFresh: boolean,
  activeAlarmCount: number,
  blockByDevice: Map<string, string>,
): DeviceCell {
  const entry = roster.byId.get(deviceAssetId)
  const { state, planned, unknown } = mapState(currentState, isSourceFresh, activeAlarmCount)
  let block = blockByDevice.get(deviceAssetId)
  if (!block && planned) block = '计划保养中'
  if (!block && unknown && currentState) block = `状态未归类 · ${currentState}`
  return {
    id: deviceAssetId,
    // 展示用 master-data code（可与 join 键 deviceAssetId 不等）。
    code: entry?.code ?? deviceAssetId,
    name: entry?.name ?? deviceAssetId,
    lineId: entry?.lineCode ?? '',
    lineName: entry?.lineName ?? '—',
    workshopId: entry?.workshopCode ?? '',
    workshopName: entry?.workshopName ?? '—',
    state,
    stateLabel: STATE_LABELS[state],
    block,
    sourceFresh: isSourceFresh,
    // 格上实时参数走 historian（#570/#689），真实模式诚实留空。
    params: [],
  }
}

/** 全景取数：设备清单+状态、活动报警(#686)、维修/PM/点检、聚合可靠性。 */
export async function fetchRealEquipmentOverview(factoryId = 'F01'): Promise<EquipmentOverview> {
  if (!hasScreenSession()) {
    throw new Error('大屏会话上下文未就绪（organizationId/environmentId 为空）')
  }
  const nowMs = Date.now()
  const { organizationId, environmentId } = getScreenSession()
  const roster = await resolveDeviceRoster(nowMs)
  const contextQuery = { organizationId, environmentId }
  const listQuery = { organizationId, environmentId, skip: 0, take: 100 }

  const [overviewRes, alarmsRes, woRes, planRes, inspRes] = await Promise.all([
    roster.ids.length > 0
      ? getBusinessConsoleEquipmentOverview({
          throwOnError: true,
          query: { ...contextQuery, deviceAssetIds: roster.ids.join(',') },
        })
      : Promise.resolve(null),
    listBusinessConsoleEquipmentAlarms({ throwOnError: true, query: contextQuery }),
    listBusinessConsoleMaintenanceWorkOrders({ throwOnError: true, query: listQuery }),
    listBusinessConsoleMaintenancePlans({ throwOnError: true, query: listQuery }),
    listBusinessConsoleMaintenanceInspections({ throwOnError: true, query: listQuery }),
  ])

  const summaries = overviewRes?.data?.data?.devices ?? []
  const activeBlocks = overviewRes?.data?.data?.activeBlocks ?? []
  const alarmItems = alarmsRes?.data?.data?.items ?? []
  const woItems = woRes?.data?.data?.items ?? []
  const planItems = planRes?.data?.data?.items ?? []
  const inspItems = inspRes?.data?.data?.items ?? []

  // 每设备取最高严重度的活动阻塞作 block 文案。
  const blockByDevice = new Map<string, string>()
  for (const b of [...activeBlocks].sort(
    (x, y) =>
      (BLOCK_SEVERITY_RANK[x.severity ?? 'info'] ?? 9) -
      (BLOCK_SEVERITY_RANK[y.severity ?? 'info'] ?? 9),
  )) {
    const id = b.deviceAssetId
    if (!id || blockByDevice.has(id)) continue
    blockByDevice.set(id, REASON_LABELS[b.reasonCode ?? ''] ?? b.reasonCode ?? '受限')
  }

  const devices: DeviceCell[] = summaries.map((d) =>
    deviceCellOf(
      roster,
      d.deviceAssetId ?? '',
      d.currentState,
      d.isSourceFresh ?? false,
      d.activeAlarmCount ?? 0,
      blockByDevice,
    ),
  )

  const counts: StateCounts = { run: 0, idle: 0, down: 0, alarm: 0, offline: 0 }
  for (const d of devices) counts[d.state]++

  // 报警→工单联动：维修单 sourceAlarmId/relatedAlarmId 反查工单号。
  const woByAlarm = new Map<string, string>()
  for (const w of woItems) {
    if (!w.workOrderId) continue
    for (const aid of [w.sourceAlarmId, w.relatedAlarmId]) {
      if (aid && !woByAlarm.has(aid)) woByAlarm.set(aid, w.workOrderId)
    }
  }
  const alarms: OpenAlarmRow[] = alarmItems.map((a) => alarmRow(a, roster, woByAlarm))

  const repairs: RepairOrder[] = woItems.map((w) => repairRow(w, roster, nowMs))

  // 点检→设备联动：经工单/计划反查 deviceAssetId（点检项本身不带设备号）。
  const deviceByWorkOrder = new Map<string, string>()
  for (const w of woItems)
    if (w.workOrderId && w.deviceAssetId) deviceByWorkOrder.set(w.workOrderId, w.deviceAssetId)
  const deviceByPlan = new Map<string, string>()
  for (const p of planItems)
    if (p.planId && p.deviceAssetId) deviceByPlan.set(p.planId, p.deviceAssetId)

  const pmTasks: PmTask[] = planItems.map((p) => pmRow(p, roster))
  const inspections: InspectionRow[] = inspItems.map((i) =>
    inspectionRow(i, roster, deviceByWorkOrder, deviceByPlan),
  )

  // 聚合可靠性：运行设备占比仅作瞬时运行画像，不冒充 OEE 可用率；
  // 聚合 MTBF/MTTR 无单一端点 → null（单机值见设备详情）；故障=报警+停机台数，修复=进行中维修单数。
  const total = devices.length
  const reliability: Reliability = {
    availability: total > 0 ? Math.round((counts.run / total) * 100) : 0,
    mtbfHours: null,
    mttrMinutes: null,
    failures: counts.alarm + counts.down,
    repairs: repairs.filter((r) => r.stage !== '已关闭').length,
  }

  return { factoryId, counts, devices, alarms, repairs, reliability, pmTasks, inspections }
}

/** 参数快刷：真实模式无 historian（#570/#689），不产出演示参数流。 */
export async function fetchRealDeviceParamsTick(): Promise<DeviceParamsTick> {
  return {}
}

/** 设备详情：单设备状态+新鲜度、单机 MTBF/MTTR（真实 reliability 端点）、该设备维修/PM/点检。 */
export async function fetchRealDeviceDetail(deviceId: string): Promise<DeviceDetail | null> {
  if (!hasScreenSession()) {
    throw new Error('大屏会话上下文未就绪（organizationId/environmentId 为空）')
  }
  const nowMs = Date.now()
  const { organizationId, environmentId } = getScreenSession()
  const roster = await resolveDeviceRoster(nowMs)
  const contextQuery = { organizationId, environmentId }
  const listQuery = { organizationId, environmentId, skip: 0, take: 100 }
  const windowEndUtc = new Date(nowMs).toISOString()
  const windowStartUtc = new Date(nowMs - 30 * 24 * 60 * 60_000).toISOString()

  const [devRes, relRes, woRes, planRes, inspRes, oeeRes] = await Promise.all([
    getBusinessConsoleEquipmentDevice({
      throwOnError: true,
      path: { deviceAssetId: deviceId },
      query: contextQuery,
    }),
    queryBusinessConsoleMaintenanceAssetReliability({
      throwOnError: true,
      path: { deviceAssetId: deviceId },
      query: { ...contextQuery, windowStartUtc, windowEndUtc },
    }),
    listBusinessConsoleMaintenanceWorkOrders({ throwOnError: true, query: listQuery }),
    listBusinessConsoleMaintenancePlans({ throwOnError: true, query: listQuery }),
    listBusinessConsoleMaintenanceInspections({ throwOnError: true, query: listQuery }),
    queryBusinessConsoleTelemetryOee({
      throwOnError: true,
      query: { ...contextQuery, deviceAssetId: deviceId, windowStartUtc, windowEndUtc },
    }),
  ])

  const detail = devRes?.data
  if (!detail?.success) return null
  const currentState = detail.data?.currentState
  const device = deviceCellOf(
    roster,
    deviceId,
    currentState?.currentState,
    currentState?.isSourceFresh ?? false,
    currentState?.activeAlarms?.length ?? 0,
    new Map(),
  )

  const woItems = (woRes?.data?.data?.items ?? []).filter((w) => w.deviceAssetId === deviceId)
  const planItems = (planRes?.data?.data?.items ?? []).filter((p) => p.deviceAssetId === deviceId)

  const deviceByWorkOrder = new Map<string, string>()
  for (const w of woItems) if (w.workOrderId) deviceByWorkOrder.set(w.workOrderId, deviceId)
  const deviceByPlan = new Map<string, string>()
  for (const p of planItems) if (p.planId) deviceByPlan.set(p.planId, deviceId)
  const inspItems = (inspRes?.data?.data?.items ?? []).filter(
    (i) =>
      (i.workOrderId && deviceByWorkOrder.has(i.workOrderId)) ||
      (i.planId && deviceByPlan.has(i.planId)),
  )

  const reliability = relRes?.data?.data
  const oee = oeeRes?.data?.data
  const entry = roster.byId.get(deviceId)
  return {
    device,
    workCenterName: entry?.workCenterCode || '—',
    // facade 不返回设备负责人 → 诚实「—」。
    managerName: '—',
    params: [],
    repairs: woItems.map((w) => repairRow(w, roster, nowMs)),
    pmTasks: planItems.map((p) => pmRow(p, roster)),
    inspections: inspItems.map((i) => inspectionRow(i, roster, deviceByWorkOrder, deviceByPlan)),
    mtbfHours: reliability?.mtbfHours ?? null,
    mttrMinutes: reliability?.mttrMinutes ?? null,
    oee: {
      availability: oee?.availabilityRate ?? null,
      performance: oee?.performanceRate ?? null,
      quality: oee?.qualityRate ?? null,
      rate: oee?.oeeRate ?? null,
      isDegraded: oee?.isDegraded ?? true,
      degradedReasons: oee?.degradedReasons ?? ['oee-facts-unavailable'],
    },
  }
}
