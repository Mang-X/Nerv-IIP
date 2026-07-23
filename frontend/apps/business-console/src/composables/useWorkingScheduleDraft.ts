import type {
  BusinessConsoleSchedulePlan,
  BusinessConsoleSchedulingLockedAssignment,
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

interface DraftSnapshot {
  orders: WorkingScheduleOrder[]
  model?: ScheduleModel
}

export function useWorkingScheduleDraft(readOnly: MaybeRefOrGetter<boolean> = false) {
  const orders = shallowRef<WorkingScheduleOrder[]>([])
  const model = shallowRef<ScheduleModel>()
  const history = shallowRef<DraftSnapshot[]>([])
  const future = shallowRef<DraftSnapshot[]>([])

  function assertMutable() {
    if (toValue(readOnly)) throw new Error('Working schedule draft is read-only.')
  }

  function snapshot(): DraftSnapshot {
    return structuredClone({ orders: orders.value, model: model.value })
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

  function loadPlan(plan: BusinessConsoleSchedulePlan) {
    history.value = []
    future.value = []
    model.value = toModel(plan)
  }

  function updateTask(
    taskId: string,
    patch: Partial<Pick<ScheduleTask, 'resourceId' | 'startUtc' | 'endUtc'>>,
  ) {
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
    mutate(() => {
      if (!model.value) return
      model.value = {
        ...model.value,
        tasks: model.value.tasks.map((task) => (task.id === taskId ? { ...task, locked } : task)),
      }
    })
  }

  function restore(target: DraftSnapshot) {
    orders.value = structuredClone(target.orders)
    model.value = structuredClone(target.model)
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
    lockedAssignments,
    model,
    moveTask,
    orders,
    redo,
    setIncluded,
    setLocked,
    setOrders,
    undo,
    updateOrder,
    updateTask,
  }
}

function recomputeOrderNodes(tasks: ScheduleTask[]) {
  return tasks.map((task) => {
    if (task.type !== 'order') return task
    const children = tasks.filter(
      (child) => child.type === 'operation' && child.orderId === task.orderId,
    )
    if (children.length === 0) return task
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
