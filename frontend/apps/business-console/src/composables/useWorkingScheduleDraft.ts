import type {
  BusinessConsoleSchedulePlan,
  BusinessConsoleSchedulingLockedAssignment,
  BusinessConsoleSchedulingPlanImpact,
} from '@nerv-iip/api-client'
import {
  toModel,
  type ScheduleModel,
  type ScheduleTask,
  type TaskDragPayload,
} from '@nerv-iip/scheduling'
import { computed, shallowRef, type MaybeRefOrGetter, toValue } from 'vue'

export interface WorkingScheduleOrder {
  workOrderId: string
  priority: number
  isRush: boolean
  included: boolean
}

export interface WorkingSchedulePendingOperation {
  id: string
  taskId?: string
  orderId: string
  operationId: string
  source: 'unscheduled' | 'invalidated' | 'removed'
  reasonCode?: string
  message: string
  canRestore: boolean
  task?: ScheduleTask
}

interface DraftSnapshot {
  orders: WorkingScheduleOrder[]
  model?: ScheduleModel
  pendingOperations: WorkingSchedulePendingOperation[]
}

export function useWorkingScheduleDraft(readOnly: MaybeRefOrGetter<boolean> = false) {
  const orders = shallowRef<WorkingScheduleOrder[]>([])
  const model = shallowRef<ScheduleModel>()
  const baselineTasks = shallowRef(new Map<string, ScheduleTask>())
  const baselineLinks = shallowRef<ScheduleModel['links']>([])
  const pendingOperations = shallowRef<WorkingSchedulePendingOperation[]>([])
  const history = shallowRef<DraftSnapshot[]>([])
  const future = shallowRef<DraftSnapshot[]>([])

  function assertMutable() {
    if (toValue(readOnly)) throw new Error('Working schedule draft is read-only.')
  }

  function snapshot(): DraftSnapshot {
    return structuredClone({
      orders: orders.value,
      model: model.value,
      pendingOperations: pendingOperations.value,
    })
  }

  function mutate(change: () => void) {
    assertMutable()
    history.value = [...history.value, snapshot()]
    future.value = []
    change()
  }

  function setOrders(candidates: Array<{ workOrderId?: string; priority?: number }>) {
    const existing = new Map(orders.value.map((order) => [order.workOrderId, order]))
    orders.value = candidates
      .filter((candidate): candidate is { workOrderId: string; priority?: number } =>
        Boolean(candidate.workOrderId?.trim()),
      )
      .map(
        (candidate) =>
          existing.get(candidate.workOrderId) ?? {
            workOrderId: candidate.workOrderId,
            priority: candidate.priority ?? 100,
            isRush: false,
            included: false,
          },
      )
  }

  function setIncluded(workOrderIds: string[], included: boolean) {
    mutate(() => {
      const selected = new Set(workOrderIds)
      orders.value = orders.value.map((order) =>
        selected.has(order.workOrderId) ? { ...order, included } : order,
      )
    })
  }

  function updateOrder(
    workOrderId: string,
    patch: Partial<Pick<WorkingScheduleOrder, 'priority' | 'isRush'>>,
  ) {
    mutate(() => {
      orders.value = orders.value.map((order) =>
        order.workOrderId === workOrderId ? { ...order, ...patch } : order,
      )
    })
  }

  function loadPlan(
    plan: BusinessConsoleSchedulePlan,
    impact?: BusinessConsoleSchedulingPlanImpact,
  ) {
    history.value = []
    future.value = []
    const nextModel = toModel(plan)
    model.value = nextModel
    baselineTasks.value = new Map(nextModel.tasks.map((task) => [task.id, structuredClone(task)]))
    baselineLinks.value = structuredClone(nextModel.links)
    pendingOperations.value = buildPendingOperations(nextModel, impact)
  }

  function updateTask(
    taskId: string,
    patch: Partial<Pick<ScheduleTask, 'resourceId' | 'startUtc' | 'endUtc'>>,
  ) {
    const task = model.value?.tasks.find((candidate) => candidate.id === taskId)
    if (!task || task.type !== 'operation' || task.locked) return
    mutate(() => {
      if (!model.value) return
      const tasks = model.value.tasks.map((task) =>
        task.id === taskId ? { ...task, ...patch } : task,
      )
      model.value = { ...model.value, tasks: recomputeOrderNodes(tasks) }
    })
  }

  function moveTask(payload: TaskDragPayload) {
    updateTask(payload.taskId, {
      resourceId: payload.resourceId,
      startUtc: payload.startUtc,
      endUtc: payload.endUtc,
    })
  }

  function setLocked(taskId: string, locked: boolean) {
    const task = model.value?.tasks.find((candidate) => candidate.id === taskId)
    if (!task || task.type !== 'operation' || task.locked === locked) return
    mutate(() => {
      if (!model.value) return
      model.value = {
        ...model.value,
        tasks: model.value.tasks.map((task) => (task.id === taskId ? { ...task, locked } : task)),
      }
    })
  }

  function lockModifiedTasks() {
    const modified = new Set(modifiedUnlockedTaskIds.value)
    if (modified.size === 0) return
    mutate(() => {
      if (!model.value) return
      model.value = {
        ...model.value,
        tasks: model.value.tasks.map((task) =>
          modified.has(task.id) ? { ...task, locked: true } : task,
        ),
      }
    })
  }

  function moveTaskToPending(taskId: string) {
    const task = model.value?.tasks.find((candidate) => candidate.id === taskId)
    if (!task || task.type !== 'operation' || task.locked) return
    mutate(() => {
      if (!model.value) return
      const related = pendingOperations.value.filter(
        (item) => item.orderId === task.orderId && item.operationId === task.operationId,
      )
      pendingOperations.value = [
        ...pendingOperations.value.filter(
          (item) => item.orderId !== task.orderId || item.operationId !== task.operationId,
        ),
        {
          id: `removed:${task.id}`,
          taskId: task.id,
          orderId: task.orderId,
          operationId: task.operationId,
          source: 'removed',
          reasonCode: related[0]?.reasonCode,
          message:
            related
              .map((item) => item.message)
              .filter(Boolean)
              .join('；') || '规划员移回待排',
          canRestore: true,
          task: structuredClone(task),
        },
      ]
      const tasks = model.value.tasks.filter((candidate) => candidate.id !== taskId)
      model.value = {
        ...model.value,
        tasks: recomputeOrderNodes(tasks),
        links: visibleBaselineLinks(tasks, baselineLinks.value),
      }
    })
  }

  function restorePendingTask(taskId: string) {
    const pending = pendingOperations.value.find(
      (item) => item.taskId === taskId && item.source === 'removed' && item.task,
    )
    if (!pending?.task || !model.value) return
    mutate(() => {
      if (!model.value || !pending.task) return
      const parent = pending.task.parentId
        ? baselineTasks.value.get(pending.task.parentId)
        : undefined
      const tasks = [
        ...model.value.tasks,
        ...(parent && !model.value.tasks.some((task) => task.id === parent.id)
          ? [structuredClone(parent)]
          : []),
        structuredClone(pending.task),
      ]
      model.value = {
        ...model.value,
        tasks: recomputeOrderNodes(tasks),
        links: visibleBaselineLinks(tasks, baselineLinks.value),
      }
      pendingOperations.value = pendingOperations.value.filter((item) => item.id !== pending.id)
    })
  }

  function restore(target: DraftSnapshot) {
    orders.value = structuredClone(target.orders)
    model.value = structuredClone(target.model)
    pendingOperations.value = structuredClone(target.pendingOperations)
  }

  function undo() {
    assertMutable()
    const previous = history.value.at(-1)
    if (!previous) return
    future.value = [snapshot(), ...future.value]
    history.value = history.value.slice(0, -1)
    restore(previous)
  }

  function redo() {
    assertMutable()
    const next = future.value[0]
    if (!next) return
    history.value = [...history.value, snapshot()]
    future.value = future.value.slice(1)
    restore(next)
  }

  const includedOrders = computed(() => orders.value.filter((order) => order.included))
  const modifiedUnlockedTaskIds = computed(() =>
    (model.value?.tasks ?? [])
      .filter((task) => {
        if (task.type !== 'operation' || task.locked) return false
        const baseline = baselineTasks.value.get(task.id)
        return (
          baseline !== undefined &&
          (baseline.resourceId !== task.resourceId ||
            baseline.startUtc !== task.startUtc ||
            baseline.endUtc !== task.endUtc)
        )
      })
      .map((task) => task.id),
  )
  const lockedAssignments = computed<BusinessConsoleSchedulingLockedAssignment[]>(() =>
    (model.value?.tasks ?? [])
      .filter((task) => task.type === 'operation' && task.locked)
      .map((task) => ({
        assignmentId: task.id,
        orderId: task.orderId,
        operationId: task.operationId,
        operationSequence: task.operationSequence,
        resourceId: task.resourceId,
        workCenterId: task.workCenterId,
        startUtc: task.startUtc,
        endUtc: task.endUtc,
        lockReasonCode: 'planner-draft-lock',
      })),
  )

  return {
    canRedo: computed(() => future.value.length > 0),
    canUndo: computed(() => history.value.length > 0),
    includedOrders,
    loadPlan,
    lockModifiedTasks,
    lockedAssignments,
    model,
    modifiedUnlockedTaskIds,
    moveTask,
    moveTaskToPending,
    orders,
    pendingOperations,
    redo,
    restorePendingTask,
    setIncluded,
    setLocked,
    setOrders,
    undo,
    updateOrder,
    updateTask,
  }
}

function buildPendingOperations(
  model: ScheduleModel,
  impact?: BusinessConsoleSchedulingPlanImpact,
): WorkingSchedulePendingOperation[] {
  const affected = new Set(impact?.affectedOperationIds ?? [])
  const affectedOrders = new Set(impact?.affectedWorkOrderIds ?? [])
  const isAffected = (orderId: string, operationId: string) =>
    Boolean(
      impact?.isInvalidated &&
      affected.has(operationId) &&
      (affectedOrders.size === 0 || affectedOrders.has(orderId)),
    )
  const unscheduled = model.unscheduled.map((item) => {
    const invalidated = isAffected(item.orderId, item.operationId)
    return {
      id: `${invalidated ? 'invalidated' : 'unscheduled'}:${item.orderId}:${item.operationId}`,
      orderId: item.orderId,
      operationId: item.operationId,
      source: invalidated ? ('invalidated' as const) : ('unscheduled' as const),
      reasonCode: invalidated ? (impact?.reasonCode ?? undefined) : item.reason,
      message: invalidated ? `${item.message}；基线失效影响` : item.message,
      canRestore: false,
    }
  })
  if (!impact?.isInvalidated) return unscheduled

  const pendingKeys = new Set(unscheduled.map((item) => `${item.orderId}:${item.operationId}`))
  return [
    ...unscheduled,
    ...model.tasks
      .filter(
        (task) =>
          task.type === 'operation' &&
          affected.has(task.operationId) &&
          (affectedOrders.size === 0 || affectedOrders.has(task.orderId)) &&
          !pendingKeys.has(`${task.orderId}:${task.operationId}`),
      )
      .map((task) => ({
        id: `invalidated:${task.id}`,
        taskId: task.id,
        orderId: task.orderId,
        operationId: task.operationId,
        source: 'invalidated' as const,
        reasonCode: impact.reasonCode ?? undefined,
        message: '基线失效影响；候选方案中已重新计算',
        canRestore: false,
      })),
  ]
}

function visibleBaselineLinks(tasks: ScheduleTask[], baselineLinks: ScheduleModel['links']) {
  const visibleTaskIds = new Set(tasks.map((task) => task.id))
  return baselineLinks
    .filter((link) => visibleTaskIds.has(link.source) && visibleTaskIds.has(link.target))
    .map((link) => structuredClone(link))
}

function recomputeOrderNodes(tasks: ScheduleTask[]) {
  const operations = tasks.filter((task) => task.type === 'operation')
  return tasks
    .filter(
      (task) =>
        task.type !== 'order' || operations.some((operation) => operation.orderId === task.orderId),
    )
    .map((task) => {
      if (task.type !== 'order') return task
      const children = operations.filter((child) => child.orderId === task.orderId)
      return {
        ...task,
        startUtc: children.map((child) => child.startUtc).sort()[0] ?? task.startUtc,
        endUtc:
          children
            .map((child) => child.endUtc)
            .sort()
            .at(-1) ?? task.endUtc,
      }
    })
}
