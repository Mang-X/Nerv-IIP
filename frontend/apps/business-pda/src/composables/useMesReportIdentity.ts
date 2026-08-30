import type {
  BusinessConsoleMesOperationTaskRow,
  BusinessConsoleMesWorkOrderDetailResponse,
  BusinessConsoleMesWorkOrderItem,
} from '@nerv-iip/api-client'
import { computed, type Ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { hasCompleteReworkAuthority } from '@/composables/mes/mesWorkOrderAuthority'

type WorkOrder = BusinessConsoleMesWorkOrderItem
type Task = BusinessConsoleMesOperationTaskRow

interface UseMesReportIdentityOptions {
  workOrderDetail: Readonly<Ref<BusinessConsoleMesWorkOrderDetailResponse | null | undefined>>
  workOrderDetailPending: Readonly<Ref<boolean>>
  workOrderDetailError: Readonly<Ref<unknown>>
  exactOperationTask: Readonly<Ref<Task | null | undefined>>
  exactOperationTaskPending: Readonly<Ref<boolean>>
  exactOperationTaskError: Readonly<Ref<unknown>>
  exactOperationTaskScopeReady: Readonly<Ref<boolean>>
  exactOperationTaskScopeMessage: Readonly<Ref<string>>
}

function queryId(value: unknown) {
  return typeof value === 'string' ? value.trim() : ''
}

function canReport(task: Task) {
  return (
    hasCompleteReworkAuthority(task) &&
    task.allowedActions?.some((action) => action.trim().toLowerCase() === 'report') === true
  )
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
    if (detail?.workOrderId === workOrderId && hasCompleteReworkAuthority(detail)) {
      return {
        workOrderId: detail.workOrderId,
        skuId: detail.skuId,
        productionVersionId: detail.productionVersionId,
        quantity: detail.quantity,
        status: detail.status as WorkOrder['status'],
        operationTasks: detail.operationTasks,
        workOrderType: detail.workOrderType,
        sourceWorkOrderId: detail.sourceWorkOrderId,
        sourceNcrId: detail.sourceNcrId,
        sourceNcrCode: detail.sourceNcrCode,
      }
    }
    return null
  })

  const workOrderOperationTasks = computed(() => {
    const workOrderId = selectedWorkOrder.value?.workOrderId
    const detail = options.workOrderDetail.value
    if (!workOrderId || options.workOrderDetailPending.value) {
      return []
    }
    const exactTasks =
      detail?.workOrderId === workOrderId && detail.operationTasks ? detail.operationTasks : []
    return exactTasks.filter(
      (task) => task.workOrderId === workOrderId && Boolean(task.operationTaskId),
    )
  })

  const visibleOperationTasks = computed(() => workOrderOperationTasks.value.filter(canReport))

  const selectedTask = computed<Task | null>(() => {
    const operationTaskId = requestedOperationTaskId.value
    const workOrderId = selectedWorkOrder.value?.workOrderId
    if (!operationTaskId || !workOrderId) return null
    const detailTask = workOrderOperationTasks.value.find(
      (task) => task.operationTaskId === operationTaskId,
    )
    if (detailTask) return detailTask
    const exactTask = options.exactOperationTask.value
    return exactTask?.workOrderId === workOrderId && exactTask.operationTaskId === operationTaskId
      ? exactTask
      : null
  })

  const pair = computed(() => {
    const workOrderId = selectedWorkOrder.value?.workOrderId
    const task = selectedTask.value
    const operationTaskId = task?.operationTaskId
    if (!workOrderId || !operationTaskId || task?.workOrderId !== workOrderId || !canReport(task)) {
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
    const detail = options.workOrderDetail.value
    if (
      workOrderId &&
      !options.workOrderDetailPending.value &&
      detail?.workOrderId === workOrderId &&
      !hasCompleteReworkAuthority(detail)
    ) {
      return `工单 ${workOrderId} 的返工来源信息不完整，已阻止报工，请刷新后重试。`
    }
    if (workOrderId && !options.workOrderDetailPending.value && !selectedWorkOrder.value) {
      return `未找到工单 ${workOrderId}，已阻止报工。`
    }
    if (workOrderId && operationTaskId && selectedWorkOrder.value && selectedTask.value) {
      if (!hasCompleteReworkAuthority(selectedTask.value)) {
        return `工序任务 ${operationTaskId} 的返工来源信息不完整，已阻止报工，请刷新后重试。`
      }
      if (!canReport(selectedTask.value)) {
        return `工序任务 ${operationTaskId} 当前不可报工，服务端未开放 report 动作。`
      }
    }
    if (workOrderId && operationTaskId && selectedWorkOrder.value && !selectedTask.value) {
      if (!options.exactOperationTaskScopeReady.value) {
        const scopeMessage =
          options.exactOperationTaskScopeMessage.value || '报工任务读取范围尚未就绪。'
        return `报工任务读取范围未就绪：${scopeMessage}`
      }
      if (options.exactOperationTaskError.value) {
        return `工单 ${workOrderId} 下的工序任务 ${operationTaskId} 精确查询失败，已阻止报工，请重试。`
      }
      if (options.exactOperationTaskPending.value) return null
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
