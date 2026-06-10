import type { ScheduleAssignmentContract, SchedulePlanContract } from '@nerv-iip/api-client'
import type {
  ConflictReason,
  ConflictSeverity,
  ChangeType,
  PlanStatus,
  ScheduleChange,
  ScheduleConflict,
  ScheduleLink,
  ScheduleModel,
  ScheduleTask,
  UnscheduledItem,
} from './types'

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

  const allStarts = operations.map((o) => o.startUtc).filter(Boolean).sort()
  const allEnds = operations.map((o) => o.endUtc).filter(Boolean).sort()

  return {
    tasks: [...orderNodes, ...operations],
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
    changes,
    horizon: { startUtc: allStarts[0] ?? '', endUtc: allEnds[allEnds.length - 1] ?? '' },
    meta: {
      planId: plan.planId ?? '',
      status: (plan.status ?? 'preview') as PlanStatus,
      algorithmVersion: plan.algorithmVersion ?? '',
    },
  }
}

/** 锁定的工序 → assignment 契约,供重预览回传(order 分组父节点不回传)。 */
export function toLockedAssignments(model: ScheduleModel): ScheduleAssignmentContract[] {
  return model.tasks
    .filter((t) => t.type === 'operation' && t.locked)
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
