import type { ScheduleAssignmentContract, SchedulePlanContract } from '@nerv-iip/api-client'
import type {
  ConflictReason,
  ConflictSeverity,
  ChangeType,
  PlanStatus,
  ScheduleCalendar,
  ScheduleChange,
  ScheduleConflict,
  ScheduleLink,
  ScheduleModel,
  ScheduleTask,
  UnscheduledItem,
  MaterialRisk,
  EquipmentRisk,
} from './types'
import { BLOCK_LABELS, toBlockKind } from './blocks'

const taskId = (a: ScheduleAssignmentContract): string =>
  a.assignmentId ?? `${a.orderId ?? 'order'}:${a.operationId ?? 'op'}`

const orderNodeId = (orderId: string): string => `order:${orderId}`

/** APS SchedulePlanContract → 引擎无关 ScheduleModel(纯函数)。 */
export function toModel(plan: SchedulePlanContract): ScheduleModel {
  const assignments = plan.assignments ?? []

  const operations: ScheduleTask[] = assignments.map((a) => ({
    id: taskId(a),
    orderId: a.orderId ?? '',
    operationId: a.operationId ?? '',
    operationSequence: a.operationSequence ?? 0,
    parentId: a.orderId ? orderNodeId(a.orderId) : undefined,
    type: 'operation',
    text: a.operationId ?? '',
    resourceId: a.resourceId ?? a.workCenterId ?? undefined,
    workCenterId: a.workCenterId ?? undefined,
    dimensions:
      a.workCenterId || a.resourceId
        ? {
            workCenter: {
              id: (a.workCenterId ?? a.resourceId)!,
              label: (a.workCenterId ?? a.resourceId)!,
            },
          }
        : undefined,
    startUtc: a.startUtc ?? '',
    endUtc: a.endUtc ?? '',
    locked: a.isLocked ?? false,
    hasConflict: false,
    conflictReason: null,
  }))

  // 工单分组父节点(order 视图):start=min(子),end=max(子)。
  const orderIds = [...new Set(operations.map((o) => o.orderId).filter(Boolean))]
  const orderNodes: ScheduleTask[] = orderIds.map((orderId) => {
    const kids = operations.filter((o) => o.orderId === orderId)
    return {
      id: orderNodeId(orderId),
      orderId,
      operationId: '',
      operationSequence: 0,
      type: 'order',
      text: orderId,
      startUtc: kids.reduce((m, k) => (k.startUtc < m ? k.startUtc : m), kids[0]?.startUtc ?? ''),
      endUtc: kids.reduce((m, k) => (k.endUtc > m ? k.endUtc : m), kids[0]?.endUtc ?? ''),
      locked: false,
      hasConflict: false,
      conflictReason: null,
    }
  })

  // 依赖链:同工单按 operationSequence 排序,相邻 finish_to_start。
  const links: ScheduleLink[] = []
  for (const orderId of orderIds) {
    const seq = operations
      .filter((o) => o.orderId === orderId)
      .sort((a, b) => a.operationSequence - b.operationSequence)
    for (let i = 1; i < seq.length; i++) {
      links.push({
        id: `${seq[i - 1].id}->${seq[i].id}`,
        source: seq[i - 1].id,
        target: seq[i].id,
        type: 'finish_to_start',
      })
    }
  }

  const conflicts: ScheduleConflict[] = (plan.conflicts ?? []).map((c) => {
    const t = operations.find((o) => o.orderId === c.orderId && o.operationId === c.operationId)
    return {
      id: c.conflictId ?? '',
      reason: (c.reasonCode ?? 'capacity') as ConflictReason,
      severity: (c.severity ?? 'warning') as ConflictSeverity,
      orderId: c.orderId,
      operationId: c.operationId,
      resourceId: c.resourceId,
      message: c.message ?? '',
      taskId: t?.id,
    }
  })
  // 把冲突标记回对应 task。
  for (const c of conflicts) {
    const t = operations.find((o) => o.id === c.taskId)
    if (t) {
      t.hasConflict = true
      t.conflictReason = c.reason
    }
  }

  // 物料风险（软约束）：这些工序已排入计划，但开工前必须先备料。
  // 齐套是开工门槛不是排产门槛，所以它标在已排工序上，不进 unscheduled。
  const materialRisks: MaterialRisk[] = (plan.materialRisks ?? []).map((r) => ({
    orderId: r.orderId ?? '',
    operationId: r.operationId ?? '',
    reasonCodes: [...(r.reasonCodes ?? [])],
    shortages: (r.shortages ?? []).map((s) => ({
      materialId: s.materialId ?? '',
      materialLotId: s.materialLotId,
      requiredQuantity: s.requiredQuantity ?? 0,
      availableQuantity: s.availableQuantity ?? 0,
      shortageQuantity: s.shortageQuantity ?? 0,
    })),
    message: r.message ?? '',
  }))
  for (const risk of materialRisks) {
    const t = operations.find(
      (o) => o.orderId === risk.orderId && o.operationId === risk.operationId,
    )
    if (t) t.materialRisk = risk
  }

  // 设备数据风险（软约束）：这些工序排在状态未知的设备上（无快照 / 快照过期 / 采集源不可达）。
  // 「不知道」不等于「不可用」，所以它同样标在已排工序上，不进 unscheduled。
  const equipmentRisks: EquipmentRisk[] = (plan.equipmentRisks ?? []).map((r) => ({
    orderId: r.orderId ?? '',
    operationId: r.operationId ?? '',
    resourceId: r.resourceId ?? '',
    reasonCodes: [...(r.reasonCodes ?? [])],
    message: r.message ?? '',
  }))
  for (const risk of equipmentRisks) {
    const t = operations.find(
      (o) => o.orderId === risk.orderId && o.operationId === risk.operationId,
    )
    if (t) t.equipmentRisk = risk
  }

  const unscheduled: UnscheduledItem[] = (plan.unscheduledOperations ?? []).map((u) => ({
    orderId: u.orderId ?? '',
    operationId: u.operationId ?? '',
    reason: (u.reasonCode ?? 'noEligibleResource') as ConflictReason,
    message: u.message ?? '',
  }))

  const changes: ScheduleChange[] = (plan.changeSummary ?? []).map((c) => {
    const t = operations.find((o) => o.orderId === c.orderId && o.operationId === c.operationId)
    return {
      orderId: c.orderId ?? '',
      operationId: c.operationId ?? '',
      changeType: (c.changeType ?? 'preserved') as ChangeType,
      message: c.message ?? '',
      taskId: t?.id,
    }
  })

  const allStarts = operations
    .map((o) => o.startUtc)
    .filter(Boolean)
    .sort()
  const allEnds = operations
    .map((o) => o.endUtc)
    .filter(Boolean)
    .sort()

  // 资源不可用窗口(维护/停机/换线/换型):做成不可拖拽的「资源时间块」任务,
  // 引擎不把它们画成工单条,而是按泳道+时段给单元格上斜纹底纹(见 DhtmlxEngine.blockCells)。
  const blocks: ScheduleTask[] = (plan.blockWindows ?? []).map((w, index) => {
    const kind = toBlockKind(w.kind)
    // 泳道键与工序一致(资源板默认按工作中心铺泳道),否则块会另起一条孤立泳道。
    const laneId = w.workCenterId ?? w.resourceId ?? ''
    return {
      id: `block:${index}:${laneId}`,
      orderId: '',
      operationId: '',
      operationSequence: 0,
      type: 'operation' as const,
      text: BLOCK_LABELS[kind],
      resourceId: w.resourceId ?? undefined,
      workCenterId: w.workCenterId ?? undefined,
      dimensions: laneId ? { workCenter: { id: laneId, label: laneId } } : undefined,
      startUtc: w.startUtc ?? '',
      endUtc: w.endUtc ?? '',
      blockKind: kind,
      locked: true,
      hasConflict: false,
      conflictReason: null,
    }
  })

  const calendars: ScheduleCalendar[] = (plan.calendars ?? []).map((c) => ({
    calendarId: c.calendarId ?? '',
    resourceIds: [...(c.resourceIds ?? [])],
    workCenterIds: [...(c.workCenterIds ?? [])],
    shiftWindows: (c.shiftWindows ?? []).map((w) => ({
      startUtc: w.startUtc ?? '',
      endUtc: w.endUtc ?? '',
      shiftCode: w.shiftCode ?? '',
    })),
  }))

  return {
    tasks: [...orderNodes, ...operations, ...blocks],
    calendars: calendars.length ? calendars : undefined,
    links,
    resources: [...new Set(operations.map((o) => o.resourceId).filter(Boolean) as string[])].map(
      (id) => ({ id, text: id }),
    ),
    loads: (plan.resourceLoads ?? []).map((l) => ({
      resourceId: l.resourceId ?? '',
      windowStartUtc: l.windowStartUtc ?? '',
      windowEndUtc: l.windowEndUtc ?? '',
      assignedMinutes: l.assignedMinutes ?? 0,
      availableMinutes: l.availableMinutes ?? 0,
      utilization: l.utilization ?? 0,
    })),
    conflicts,
    unscheduled,
    materialRisks,
    equipmentRisks,
    changes,
    groupDimensions: operations.some((o) => o.dimensions?.workCenter)
      ? [{ key: 'workCenter', label: '工作中心' }]
      : [],
    horizon: { startUtc: allStarts[0] ?? '', endUtc: allEnds[allEnds.length - 1] ?? '' },
    meta: {
      planId: plan.planId ?? '',
      status: (plan.status ?? 'preview') as PlanStatus,
      algorithmVersion: plan.algorithmVersion ?? '',
    },
  }
}

/**
 * 锁定的工序 → assignment 契约,供重预览回传(order 分组父节点不回传)。
 * 资源时间块虽然也是 locked(不可拖拽),但它不是工序,绝不能当成锁定项回传给排程服务。
 */
export function toLockedAssignments(model: ScheduleModel): ScheduleAssignmentContract[] {
  return model.tasks
    .filter((t) => t.type === 'operation' && t.locked && !t.blockKind)
    .map((t) => ({
      assignmentId: t.id,
      orderId: t.orderId,
      operationId: t.operationId,
      operationSequence: t.operationSequence,
      resourceId: t.resourceId,
      workCenterId: t.workCenterId,
      startUtc: t.startUtc,
      endUtc: t.endUtc,
      isLocked: true,
    }))
}
