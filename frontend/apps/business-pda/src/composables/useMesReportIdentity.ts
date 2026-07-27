import type {
  BusinessConsoleMesOperationTaskRow,
  BusinessConsoleMesWorkOrderDetailResponse,
  BusinessConsoleMesWorkOrderItem,
} from '@nerv-iip/api-client'
import { computed, watch, type Ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'

type WorkOrder = BusinessConsoleMesWorkOrderItem
type Task = BusinessConsoleMesOperationTaskRow

interface TaskFilters {
  workOrderId?: string
}

interface UseMesReportIdentityOptions {
  workOrders: Readonly<Ref<WorkOrder[]>>
  workOrdersPending: Readonly<Ref<boolean>>
  workOrderDetail: Readonly<Ref<BusinessConsoleMesWorkOrderDetailResponse | null | undefined>>
  workOrderDetailPending: Readonly<Ref<boolean>>
  workOrderDetailError: Readonly<Ref<unknown>>
  operationTasks: Readonly<Ref<Task[]>>
  tasksPending: Readonly<Ref<boolean>>
  taskFilters: TaskFilters
  cancelPendingTasks: () => void
}

function queryId(value: unknown) {
  return typeof value === 'string' ? value.trim() : ''
}

export function useMesReportIdentity(options: UseMesReportIdentityOptions) {
  const route = useRoute()
  const router = useRouter()

  const requestedWorkOrderId = computed(() => queryId(route.query.workOrderId))
  const requestedOperationTaskId = computed(() => queryId(route.query.operationTaskId))

  const selectedWorkOrder = computed<WorkOrder | null>(() => {
    const workOrderId = requestedWorkOrderId.value
    if (!workOrderId) return null
    if (options.workOrderDetailError.value) return null
    const detail = options.workOrderDetail.value
    if (detail?.workOrderId === workOrderId) {
      return {
        workOrderId: detail.workOrderId,
        skuId: detail.skuId,
        productionVersionId: detail.productionVersionId,
        quantity: detail.quantity,
        status: detail.status as WorkOrder['status'],
        operationTasks: detail.operationTasks,
      }
    }
    return null
  })

  watch(
    () => selectedWorkOrder.value?.workOrderId,
    (workOrderId) => {
      if (options.taskFilters.workOrderId === workOrderId) return
      options.cancelPendingTasks()
      options.taskFilters.workOrderId = workOrderId
    },
    { immediate: true },
  )

  const visibleOperationTasks = computed(() => {
    const workOrderId = selectedWorkOrder.value?.workOrderId
    const detail = options.workOrderDetail.value
    if (
      !workOrderId ||
      options.workOrderDetailPending.value ||
      options.tasksPending.value ||
      options.taskFilters.workOrderId !== workOrderId
    ) {
      return []
    }
    const exactTasks =
      detail?.workOrderId === workOrderId && detail.operationTasks ? detail.operationTasks : []
    return exactTasks.filter(
      (task) => task.workOrderId === workOrderId && Boolean(task.operationTaskId),
    )
  })

  const selectedTask = computed<Task | null>(() => {
    const operationTaskId = requestedOperationTaskId.value
    if (!operationTaskId) return null
    return (
      visibleOperationTasks.value.find((task) => task.operationTaskId === operationTaskId) ?? null
    )
  })

  const pair = computed(() => {
    const workOrderId = selectedWorkOrder.value?.workOrderId
    const operationTaskId = selectedTask.value?.operationTaskId
    if (!workOrderId || !operationTaskId || selectedTask.value?.workOrderId !== workOrderId) {
      return null
    }
    return { workOrderId, operationTaskId }
  })

  const routeIssue = computed(() => {
    const workOrderId = requestedWorkOrderId.value
    const operationTaskId = requestedOperationTaskId.value
    if (operationTaskId && !workOrderId) {
      return '报工链接缺少工单 ID，已阻止报工。'
    }
    if (workOrderId && options.workOrderDetailError.value) {
      return `工单 ${workOrderId} 详情加载失败，已阻止报工，请重试。`
    }
    if (workOrderId && !options.workOrderDetailPending.value && !selectedWorkOrder.value) {
      return `未找到工单 ${workOrderId}，已阻止报工。`
    }
    if (
      workOrderId &&
      operationTaskId &&
      selectedWorkOrder.value &&
      !options.tasksPending.value &&
      !selectedTask.value
    ) {
      const taskWithSameId = options.operationTasks.value.find(
        (task) => task.operationTaskId === operationTaskId,
      )
      if (taskWithSameId?.workOrderId && taskWithSameId.workOrderId !== workOrderId) {
        return `工序任务 ${operationTaskId} 不属于工单 ${workOrderId}，已阻止报工。`
      }
      return `未找到工单 ${workOrderId} 下的工序任务 ${operationTaskId}，已阻止报工。`
    }
    return null
  })

  function identityQuery(workOrderId?: string, operationTaskId?: string) {
    const query = { ...route.query }
    delete query.workOrderId
    delete query.operationTaskId
    if (workOrderId) query.workOrderId = workOrderId
    if (operationTaskId) query.operationTaskId = operationTaskId
    return query
  }

  function chooseWorkOrder(workOrder: WorkOrder) {
    if (!workOrder.workOrderId) return Promise.resolve()
    return router.replace({ query: identityQuery(workOrder.workOrderId) })
  }

  function chooseTask(task: Task) {
    const workOrderId = selectedWorkOrder.value?.workOrderId
    if (
      !workOrderId ||
      !task.operationTaskId ||
      task.workOrderId !== workOrderId ||
      !visibleOperationTasks.value.some(
        (visibleTask) =>
          visibleTask.workOrderId === workOrderId &&
          visibleTask.operationTaskId === task.operationTaskId,
      )
    ) {
      return Promise.resolve()
    }
    return router.replace({
      query: identityQuery(workOrderId, task.operationTaskId),
    })
  }

  function clearTask() {
    return router.replace({
      query: identityQuery(selectedWorkOrder.value?.workOrderId),
    })
  }

  function clearIdentity() {
    return router.replace({ query: identityQuery() })
  }

  return {
    selectedWorkOrder,
    selectedTask,
    visibleOperationTasks,
    pair,
    routeIssue,
    chooseWorkOrder,
    chooseTask,
    clearTask,
    clearIdentity,
  }
}
