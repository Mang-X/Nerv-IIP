import {
  getBusinessConsoleMesWorkOrderDetailQueryOptions,
  getBusinessConsoleMesWorkOrderDetailQueryKey,
  listBusinessConsoleMesWorkOrdersQueryOptions,
  type BusinessConsoleMesWorkOrderItem,
  type BusinessConsoleMesWorkOrderDetailResponse,
  type BusinessConsoleMesWorkOrderListEnvelope,
} from '@nerv-iip/api-client'
import { useListResponseState, useScopeBoundListResponse } from '@nerv-iip/business-core'
import { useQuery, useQueryCache } from '@pinia/colada'
import { computed, reactive, watch, type Ref } from 'vue'
import {
  MES_WORK_ORDERS_READ_PERMISSION,
  bindAuthScope,
  defaultFilters,
  envelopeItems,
  envelopeTotal,
  hasScope,
  isNonBlankString,
  isRecord,
  scopeQuery,
  toListQuery,
} from './mesShared'
import { useMesPrincipalWorkScope } from './useMesWorkScope'

function isWorkOrderOperationTask(value: unknown, requestedWorkOrderId: string) {
  if (!isRecord(value)) return false
  return (
    isNonBlankString(value.operationTaskId) &&
    value.workOrderId === requestedWorkOrderId &&
    isNonBlankString(value.status) &&
    typeof value.operationSequence === 'number' &&
    Number.isInteger(value.operationSequence) &&
    isNonBlankString(value.workCenterId) &&
    isNonBlankString(value.qualityStatus)
  )
}

function isWorkOrderDetail(value: unknown, requestedWorkOrderId: string) {
  if (!isRecord(value)) return false
  return (
    value.workOrderId === requestedWorkOrderId &&
    isNonBlankString(value.skuId) &&
    typeof value.quantity === 'number' &&
    Number.isFinite(value.quantity) &&
    isNonBlankString(value.status) &&
    isNonBlankString(value.readinessStatus) &&
    Array.isArray(value.blockingReasons) &&
    value.blockingReasons.every((reason) => typeof reason === 'string') &&
    Array.isArray(value.operationTasks) &&
    value.operationTasks.every((task) => isWorkOrderOperationTask(task, requestedWorkOrderId))
  )
}

function bindWorkOrderDetailResponse(value: unknown, requestedWorkOrderId: string) {
  if (value === undefined) return undefined
  if (isRecord(value) && value.success === true && isRecord(value.data)) {
    const responseWorkOrderId = value.data.workOrderId
    if (isNonBlankString(responseWorkOrderId) && responseWorkOrderId !== requestedWorkOrderId) {
      return undefined
    }
    if (isWorkOrderDetail(value.data, requestedWorkOrderId)) return value
  }
  const message =
    isRecord(value) && typeof value.message === 'string' && value.message.trim()
      ? value.message.trim()
      : '工单详情响应无效，请重试。'
  return { success: false, message }
}

export function useMesWorkOrders() {
  const filters = defaultFilters()
  const workOrderReadScope = useMesPrincipalWorkScope(filters, MES_WORK_ORDERS_READ_PERMISSION)
  const scopeReady = computed(() => hasScope(filters) && workOrderReadScope.scopeReady.value)
  const workOrdersIdentity = computed(() => {
    const selectedScope = workOrderReadScope.selectedScope.value
    return [
      workOrderReadScope.principalIdentity.value,
      filters.organizationId.trim(),
      filters.environmentId.trim(),
      selectedScope?.kind ?? '',
      selectedScope?.id ?? '',
    ].join(':')
  })

  const workOrdersQuery = useQuery(() => {
    const selectedScope = workOrderReadScope.selectedScope.value
    const options = listBusinessConsoleMesWorkOrdersQueryOptions({
      query: {
        ...toListQuery(filters),
        ...(selectedScope ? { scopeKind: selectedScope.kind, scopeId: selectedScope.id } : {}),
      },
    })
    return {
      ...options,
      key: [...options.key, `principal-scope:${workOrdersIdentity.value}`],
      enabled: scopeReady.value,
    }
  })
  const currentResponse = useScopeBoundListResponse(
    () => workOrdersQuery.data.value,
    workOrdersIdentity,
    scopeReady,
  )
  const { hasSuccessfulResponse, hasFailedResponse } = useListResponseState(
    currentResponse,
    scopeReady,
    workOrdersQuery.isLoading,
  )

  return {
    filters,
    workOrders: computed<BusinessConsoleMesWorkOrderItem[]>(() =>
      envelopeItems<BusinessConsoleMesWorkOrderItem, BusinessConsoleMesWorkOrderListEnvelope>(
        currentResponse.value,
      ),
    ),
    total: computed(() => envelopeTotal(currentResponse.value)),
    pending: workOrdersQuery.isLoading,
    error: workOrdersQuery.error,
    workOrderReadScope: workOrderReadScope.selectedScope,
    workOrderReadScopeMessage: workOrderReadScope.scopeMessage,
    workOrderReadScopePending: workOrderReadScope.scopePending,
    workOrderReadScopeReady: workOrderReadScope.scopeReady,
    workOrderReadScopeUnavailable: workOrderReadScope.scopeUnavailable,
    hasSuccessfulResponse,
    hasFailedResponse,
    refresh: () => (scopeReady.value ? workOrdersQuery.refetch() : Promise.resolve()),
  }
}

export function useMesWorkOrderDetail(workOrderId: Readonly<Ref<string>>) {
  const scope = bindAuthScope(reactive({ organizationId: '', environmentId: '' }))
  const workOrderReadScope = useMesPrincipalWorkScope(scope, MES_WORK_ORDERS_READ_PERMISSION)
  const queryCache = useQueryCache()
  const detailEnabled = computed(
    () => hasScope(scope) && workOrderReadScope.scopeReady.value && workOrderId.value.trim() !== '',
  )
  const detailScopeIdentity = computed(() => {
    const selectedScope = workOrderReadScope.selectedScope.value
    return [
      workOrderReadScope.principalIdentity.value,
      scope.organizationId.trim(),
      scope.environmentId.trim(),
      selectedScope?.kind ?? '',
      selectedScope?.id ?? '',
    ].join(':')
  })
  const detailIdentityKey = computed(
    () => `${detailScopeIdentity.value}:${workOrderId.value.trim()}`,
  )
  watch(
    () => {
      const selectedScope = workOrderReadScope.selectedScope.value
      return [
        workOrderId.value.trim(),
        scope.organizationId,
        scope.environmentId,
        workOrderReadScope.principalIdentity.value,
        selectedScope?.kind ?? '',
        selectedScope?.id ?? '',
      ] as const
    },
    (_current, previous) => {
      const [
        previousWorkOrderId,
        organizationId,
        environmentId,
        principalIdentity,
        scopeKind,
        scopeId,
      ] = previous ?? []
      if (
        !previousWorkOrderId ||
        !organizationId ||
        !environmentId ||
        !principalIdentity ||
        !scopeKind ||
        !scopeId
      ) {
        return
      }
      const previousScopeIdentity = [
        principalIdentity,
        organizationId.trim(),
        environmentId.trim(),
        scopeKind,
        scopeId,
      ].join(':')
      void queryCache.cancelQueries({
        key: [
          ...getBusinessConsoleMesWorkOrderDetailQueryKey({
            path: { workOrderId: previousWorkOrderId },
            query: { organizationId, environmentId, scopeKind, scopeId },
          }),
          `principal-scope:${previousScopeIdentity}`,
        ],
        exact: true,
      })
    },
    { flush: 'sync' },
  )
  const detailQuery = useQuery(() => {
    const requestedId = workOrderId.value.trim()
    const selectedScope = workOrderReadScope.selectedScope.value
    const options = getBusinessConsoleMesWorkOrderDetailQueryOptions({
      path: { workOrderId: requestedId },
      query: {
        ...scopeQuery(scope),
        ...(selectedScope ? { scopeKind: selectedScope.kind, scopeId: selectedScope.id } : {}),
      },
    })
    return {
      ...options,
      key: [...options.key, `principal-scope:${detailScopeIdentity.value}`],
      enabled: detailEnabled.value,
    }
  })
  const currentResponse = useScopeBoundListResponse(
    () => detailQuery.data.value,
    detailIdentityKey,
    detailEnabled,
  )
  const boundDetailResponse = computed(() =>
    bindWorkOrderDetailResponse(currentResponse.value, workOrderId.value.trim()),
  )
  const { hasSuccessfulResponse, hasFailedResponse } = useListResponseState(
    boundDetailResponse,
    detailEnabled,
    detailQuery.isLoading,
  )
  const error = computed(() => {
    if (detailQuery.error.value) return detailQuery.error.value
    if (!hasFailedResponse.value) return undefined
    const response = boundDetailResponse.value
    const message =
      isRecord(response) && typeof response.message === 'string' && response.message.trim()
        ? response.message.trim()
        : '工单详情响应无效，请重试。'
    return new Error(message)
  })

  return {
    workOrder: computed<BusinessConsoleMesWorkOrderDetailResponse | undefined>(() => {
      const response = boundDetailResponse.value
      return isRecord(response) && response.success === true && isRecord(response.data)
        ? (response.data as BusinessConsoleMesWorkOrderDetailResponse)
        : undefined
    }),
    pending: detailQuery.isLoading,
    error,
    workOrderReadScope: workOrderReadScope.selectedScope,
    workOrderReadScopeMessage: workOrderReadScope.scopeMessage,
    workOrderReadScopePending: workOrderReadScope.scopePending,
    workOrderReadScopeReady: workOrderReadScope.scopeReady,
    hasSuccessfulResponse,
    hasFailedResponse,
    refresh: () => (detailEnabled.value ? detailQuery.refetch() : Promise.resolve()),
  }
}
