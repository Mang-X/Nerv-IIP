import type { DraftFeedback, DraftTaskFeedback, ScheduleModel } from './types'

const minute = 60_000
const key = (orderId: string, operationId: string) => JSON.stringify([orderId, operationId])
type Interval = { start: number; end: number }
type Occupancy = Interval & { resourceId?: string; workCenterId: string; taskId?: string }

/** 浏览器建议，不改后端冲突，也不替代重预览/发布校验。 */
export function evaluateDraft(model: ScheduleModel): DraftFeedback {
  const tasks = model.tasks.filter((task) => task.type === 'operation' && !task.blockKind)
  const feedback: DraftFeedback = {
    tasks: Object.fromEntries(tasks.map((task) => [task.id, { issues: [] }])),
  }
  const context = model.validationContext
  if (!context) {
    for (const task of tasks)
      feedback.tasks[task.id]!.issues.push({
        kind: 'unknown',
        message: '方案未记录校验依据，日历、占用、前序和交期暂无法核对。',
      })
    return feedback
  }
  const taskByKey = new Map(tasks.map((task) => [key(task.orderId, task.operationId), task]))
  const operations = new Map(
    context.operations.map((operation) => [
      key(operation.orderId, operation.operationId),
      operation,
    ]),
  )
  const resources = new Map(context.resources.map((resource) => [resource.resourceId, resource]))
  const calendars = new Map(
    (model.calendars ?? []).map((calendar) => [
      calendar.calendarId,
      mergeWindows(calendar.shiftWindows.map(interval)),
    ]),
  )
  // 可见工序按草案占用；冻结 reservation 只保留草案外的占用，避免旧时间/资源和重复计数。
  const reservations = context.fixedReservations.filter(
    (reservation) => !taskByKey.has(key(reservation.orderId, reservation.operationId)),
  )
  const occupancies: Occupancy[] = reservations.map((reservation) => ({
    ...interval(reservation),
    resourceId: reservation.resourceId,
    workCenterId: reservation.workCenterId,
  }))
  const earliestEnd = new Map<string, number>()
  // APS setup 仅在同资源已有生产段结束时占用；外部占用不充当生产前序。
  for (const reservation of reservations) {
    if (
      reservation.resourceId &&
      operations.has(key(reservation.orderId, reservation.operationId))
    ) {
      rememberEnd(earliestEnd, reservation.resourceId, parseUtc(reservation.endUtc))
    }
  }
  let incompleteOccupancy = false
  for (const task of [...tasks].sort(
    (a, b) =>
      parseUtc(a.startUtc) - parseUtc(b.startUtc) ||
      (a.resourceId ?? '').localeCompare(b.resourceId ?? '') ||
      a.operationId.localeCompare(b.operationId),
  )) {
    const result = feedback.tasks[task.id]!
    const start = parseUtc(task.startUtc)
    const end = parseUtc(task.endUtc)
    if (!Number.isFinite(start) || !Number.isFinite(end) || end <= start) {
      result.issues.push({ kind: 'invalidTime', message: '请填写有效起止时间，结束必须晚于开始。' })
      incompleteOccupancy = true
      continue
    }
    const operation = operations.get(key(task.orderId, task.operationId))
    if (!operation) {
      result.issues.push({ kind: 'unknown', message: '方案未记录该工序的校验依据。' })
      incompleteOccupancy = true
      continue
    }
    if (operation.dueUtc) {
      const deltaMinutes = (end - parseUtc(operation.dueUtc)) / minute
      result.due = {
        dueUtc: operation.dueUtc,
        deltaMinutes,
        status: deltaMinutes < 0 ? 'early' : deltaMinutes > 0 ? 'late' : 'onTime',
      }
    }
    for (const predecessorId of operation.predecessorOperationIds) {
      const predecessor = taskByKey.get(key(task.orderId, predecessorId))
      if (!predecessor) {
        result.issues.push({
          kind: 'predecessorUnscheduled',
          message: `前序工序 ${predecessorId} 尚未排程，请先安排前序。`,
        })
      } else if (!Number.isFinite(parseUtc(predecessor.endUtc))) {
        result.issues.push({
          kind: 'unknown',
          message: `前序工序 ${predecessorId} 的结束时间尚未填写完整。`,
        })
      } else if (start < parseUtc(predecessor.endUtc)) {
        result.issues.push({
          kind: 'predecessor',
          message: `前序倒置：开始早于前序工序 ${predecessorId} 结束。`,
        })
      }
    }
    const resource = resources.get(task.resourceId ?? '')
    if (
      !resource ||
      resource.capacityUnits < 1 ||
      resource.utilizationRate <= 0 ||
      operation.setupMinutes === undefined
    ) {
      result.issues.push({
        kind: 'unknown',
        message: '方案未记录所选资源的容量、利用率或工序准备时间，日历与占用暂无法核对。',
      })
      incompleteOccupancy = true
      continue
    }
    const segments = task.segments?.length ? task.segments : [task]
    segments.forEach((segment, index) => {
      const production = interval(segment)
      const previousEnd = earliestEnd.get(resource.resourceId)
      occupancies.push({
        taskId: task.id,
        resourceId: resource.resourceId,
        workCenterId: resource.workCenterId,
        start:
          production.start -
          (index === 0 &&
          !operation.isFixed &&
          previousEnd !== undefined &&
          previousEnd <= production.start
            ? operation.setupMinutes! * minute
            : 0),
        end:
          operation.isFixed || resource.utilizationRate === 1
            ? production.end
            : production.start +
              Math.ceil((production.end - production.start) / minute / resource.utilizationRate) *
                minute,
      })
      rememberEnd(earliestEnd, resource.resourceId, production.end)
    })
  }
  const horizon = interval(context.horizon)
  for (const occupancy of occupancies) {
    if (!occupancy.taskId) continue
    const result = feedback.tasks[occupancy.taskId]!
    const resource = resources.get(occupancy.resourceId ?? '')
    const windows = resource ? calendars.get(resource.calendarId) : undefined
    if (!windows || !Number.isFinite(horizon.start) || !Number.isFinite(horizon.end)) {
      result.issues.push({
        kind: 'unknown',
        message: '方案未记录所选资源的班次日历或排程窗口，日历暂无法核对。',
      })
    } else if (
      occupancy.start < horizon.start ||
      occupancy.end > horizon.end ||
      !windows.some((window) => window.start <= occupancy.start && window.end >= occupancy.end)
    ) {
      result.issues.push({
        kind: 'calendar',
        message: '日历外：生产及准备、利用率保留占用跨越停产空隙或排程窗口。',
      })
    }
    for (const block of model.tasks.filter((task) => task.blockKind)) {
      if (
        ((block.resourceId && block.resourceId === occupancy.resourceId) ||
          (block.workCenterId && block.workCenterId === occupancy.workCenterId)) &&
        overlaps(occupancy, interval(block))
      ) {
        result.issues.push({
          kind: 'capacity',
          message: `占用冲突：与${block.text}时段重叠。`,
          startUtc: block.startUtc,
          endUtc: block.endUtc,
        })
      }
    }
  }
  if (incompleteOccupancy) {
    for (const task of tasks) {
      const result = feedback.tasks[task.id]!
      if (!result.issues.some((issue) => issue.kind === 'invalidTime' || issue.kind === 'unknown'))
        result.issues.push({
          kind: 'unknown',
          message: '草案有工序时间或占用依据未完整，占用反馈尚不完整。',
        })
    }
  }
  const centerCapacities = new Map<string, number>()
  for (const resource of context.resources)
    centerCapacities.set(
      resource.workCenterId,
      (centerCapacities.get(resource.workCenterId) ?? 0) + resource.capacityUnits,
    )
  for (const [resourceId, resource] of resources)
    reportCapacity(
      occupancies.filter((x) => x.resourceId === resourceId),
      resource.capacityUnits,
      'resource',
      feedback.tasks,
    )
  for (const [centerId, capacity] of centerCapacities)
    reportCapacity(
      occupancies.filter((x) => x.workCenterId === centerId),
      capacity,
      'workCenter',
      feedback.tasks,
    )
  return feedback
}

function parseUtc(value: string): number {
  return /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d+)?(?:Z|[+-]\d{2}:\d{2})$/.test(value)
    ? Date.parse(value)
    : NaN
}
function interval(value: { startUtc: string; endUtc: string }): Interval {
  return { start: parseUtc(value.startUtc), end: parseUtc(value.endUtc) }
}
function overlaps(a: Interval, b: Interval) {
  return a.start < b.end && a.end > b.start
}
function rememberEnd(ends: Map<string, number>, resourceId: string, end: number) {
  ends.set(resourceId, Math.min(ends.get(resourceId) ?? end, end))
}
function mergeWindows(windows: Interval[]): Interval[] {
  const merged: Interval[] = []
  for (const window of windows.sort((a, b) => a.start - b.start || a.end - b.end)) {
    const last = merged.at(-1)
    if (last && window.start <= last.end) last.end = Math.max(last.end, window.end)
    else merged.push({ ...window })
  }
  return merged
}
function reportCapacity(
  occupancies: Occupancy[],
  capacity: number,
  scope: 'resource' | 'workCenter',
  feedback: Record<string, DraftTaskFeedback>,
) {
  const events = new Map<number, { begin: Occupancy[]; finish: Occupancy[] }>()
  for (const occupancy of occupancies) {
    for (const [time, kind] of [
      [occupancy.start, 'begin'],
      [occupancy.end, 'finish'],
    ] as const) {
      const event = events.get(time) ?? { begin: [], finish: [] }
      event[kind].push(occupancy)
      events.set(time, event)
    }
  }
  const boundaries = [...events.keys()].sort((a, b) => a - b)
  const active = new Set<Occupancy>()
  boundaries.forEach((start, index) => {
    const event = events.get(start)!
    event.finish.forEach((occupancy) => active.delete(occupancy))
    event.begin.forEach((occupancy) => active.add(occupancy))
    const end = boundaries[index + 1]
    if (end === undefined || active.size <= capacity) return
    for (const taskId of new Set(
      [...active].map((x) => x.taskId).filter((id): id is string => !!id),
    )) {
      feedback[taskId]!.issues.push({
        kind: 'capacity',
        scope,
        startUtc: new Date(start).toISOString(),
        endUtc: new Date(end).toISOString(),
        message: `占用冲突：${scope === 'resource' ? '资源' : '工作中心'}同时占用 ${active.size} 道，超过容量 ${capacity}。`,
      })
    }
  })
}
