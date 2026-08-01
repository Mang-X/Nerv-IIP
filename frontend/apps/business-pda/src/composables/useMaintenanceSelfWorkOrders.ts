import {
  getBusinessConsoleMaintenanceWorkOrderQueryOptions,
  listBusinessConsoleDeviceAssetsQueryOptions,
  listBusinessConsoleMaintenanceWorkOrders,
  listBusinessConsoleMaintenanceWorkOrdersQueryOptions,
  type BusinessConsoleMaintenanceWorkOrderItem,
  type BusinessConsoleResourceItem,
  type BusinessConsoleResourceListEnvelope,
} from '@nerv-iip/api-client'
import {
  normalizeMaintenanceWorkOrderStatusFilter,
  type MaintenanceWorkOrderStatusCode,
} from '@nerv-iip/business-core'
import { useQuery } from '@pinia/colada'
import { computed, reactive, toValue, type MaybeRefOrGetter } from 'vue'

import {
  useListFreshness,
  useListResponseState,
  useScopeBoundListResponse,
} from './useListFreshness'
import { useTaskListPagination } from './useTaskListPagination'
import { useAuthStore } from '@/stores/auth'

const PAGE_SIZE = 20
const MAINTENANCE_READ_PERMISSION = 'business.maintenance.work-orders.read'
const DEVICE_READ_PERMISSION = 'business.masterdata.resources.read'

type WorkOrderListEnvelope =
  | {
      success?: boolean
      data?: {
        items?: BusinessConsoleMaintenanceWorkOrderItem[]
        total?: number
      } | null
    }
  | undefined

type WorkOrderDetailEnvelope =
  | { success?: boolean; data?: BusinessConsoleMaintenanceWorkOrderItem | null }
  | undefined

export interface MaintenanceSelfWorkOrderFilters {
  status: '' | MaintenanceWorkOrderStatusCode
  deviceAssetId: string
  keyword: string
}

function normalized(value: string) {
  return value.trim() || undefined
}

function useMaintenanceSelfScope() {
  const auth = useAuthStore()
  const organizationId = computed(() => auth.principal?.organizationId?.trim() ?? '')
  const environmentId = computed(() => auth.principal?.environmentId?.trim() ?? '')
  const principalId = computed(() => auth.principal?.principalId?.trim() ?? '')
  const permissions = computed(() => new Set(auth.principal?.permissionCodes ?? []))
  const canReadMaintenance = computed(() => permissions.value.has(MAINTENANCE_READ_PERMISSION))
  const canReadDevice = computed(() => permissions.value.has(DEVICE_READ_PERMISSION))
  const canRead = computed(() => canReadMaintenance.value && canReadDevice.value)
  const scopeReady = computed(
    () =>
      Boolean(organizationId.value && environmentId.value && principalId.value) && canRead.value,
  )
  const scopeKey = computed(() =>
    scopeReady.value
      ? `${organizationId.value}:${environmentId.value}:self:${principalId.value}`
      : '',
  )
  const queryScope = () => ({
    organizationId: organizationId.value,
    environmentId: environmentId.value,
    scopeKind: 'self',
    scopeId: principalId.value,
  })

  return {
    organizationId,
    environmentId,
    principalId,
    permissions,
    canRead,
    canReadMaintenance,
    canReadDevice,
    scopeReady,
    scopeKey,
    queryScope,
  }
}

export function useMaintenanceSelfWorkOrders() {
  const scope = useMaintenanceSelfScope()
  const filters = reactive<MaintenanceSelfWorkOrderFilters>({
    status: '',
    deviceAssetId: '',
    keyword: '',
  })
  const listQueryParameters = () => ({
    ...scope.queryScope(),
    status: normalized(normalizeMaintenanceWorkOrderStatusFilter(filters.status)),
    deviceAssetId: normalized(filters.deviceAssetId),
    keyword: normalized(filters.keyword),
  })

  const listQuery = useQuery(() => ({
    ...listBusinessConsoleMaintenanceWorkOrdersQueryOptions({
      query: {
        ...listQueryParameters(),
        skip: 0,
        take: PAGE_SIZE,
      },
    }),
    enabled: scope.scopeReady.value,
  }))
  const response = useScopeBoundListResponse(
    () => listQuery.data.value,
    scope.scopeKey,
    scope.scopeReady,
  )
  const firstPage = computed(() => {
    const envelope = response.value as WorkOrderListEnvelope
    if (envelope?.success !== true) return undefined
    return {
      items: envelope.data?.items ?? [],
      total: envelope.data?.total ?? 0,
    }
  })
  const identity = computed(
    () =>
      `${scope.scopeKey.value}:${normalizeMaintenanceWorkOrderStatusFilter(filters.status)}:${filters.deviceAssetId.trim()}:${filters.keyword.trim()}`,
  )
  const pager = useTaskListPagination<BusinessConsoleMaintenanceWorkOrderItem>({
    identity,
    firstPage,
    pageSize: PAGE_SIZE,
    itemKey: (item) => item.workOrderId ?? '',
    fetchPage: async ({ skip, take }) => {
      const { data } = await listBusinessConsoleMaintenanceWorkOrders({
        query: {
          ...listQueryParameters(),
          skip,
          take,
        },
        throwOnError: true,
      })
      const envelope = data as WorkOrderListEnvelope
      if (envelope?.success !== true) {
        throw new Error('维修工单下一页加载失败，请重试。')
      }
      return {
        items: envelope.data?.items ?? [],
        total: envelope.data?.total ?? 0,
      }
    },
    refreshFirstPage: listQuery.refetch,
  })
  const freshness = useListFreshness(response, scope.scopeReady)
  const responseState = useListResponseState(response, scope.scopeReady, listQuery.isLoading)

  return {
    ...scope,
    filters,
    items: pager.items,
    total: pager.total,
    loaded: pager.loaded,
    hasMore: pager.hasMore,
    loadingMore: pager.loadingMore,
    refreshing: pager.refreshing,
    loadMoreError: pager.loadMoreError,
    loadMore: pager.loadMore,
    refresh: () => (scope.scopeReady.value ? pager.refresh() : Promise.resolve()),
    pending: listQuery.isLoading,
    error: listQuery.error,
    lastUpdatedAt: freshness,
    hasSuccessfulResponse: responseState.hasSuccessfulResponse,
    hasFailedResponse: responseState.hasFailedResponse,
  }
}

export function useMaintenanceSelfWorkOrderDetail(requestedWorkOrderId: MaybeRefOrGetter<string>) {
  const scope = useMaintenanceSelfScope()
  const workOrderId = computed(() => toValue(requestedWorkOrderId).trim())
  const enabled = computed(() => scope.scopeReady.value && Boolean(workOrderId.value))
  const detailQuery = useQuery(() => ({
    ...getBusinessConsoleMaintenanceWorkOrderQueryOptions({
      path: { workOrderId: workOrderId.value },
      query: scope.queryScope(),
    }),
    enabled: enabled.value,
  }))
  const detailEnvelope = computed(() => detailQuery.data.value as WorkOrderDetailEnvelope)
  const workOrder = computed(() => {
    const envelope = detailEnvelope.value
    const item = envelope?.success === true ? envelope.data : undefined
    return item?.workOrderId === workOrderId.value ? item : undefined
  })

  const deviceAssetId = computed(() => workOrder.value?.deviceAssetId?.trim() ?? '')
  const deviceEnabled = computed(() => enabled.value && Boolean(deviceAssetId.value))
  const deviceIdentity = computed(() =>
    deviceEnabled.value ? `${scope.scopeKey.value}:device:${deviceAssetId.value}` : '',
  )
  const deviceQuery = useQuery(() => ({
    ...listBusinessConsoleDeviceAssetsQueryOptions({
      query: {
        organizationId: scope.organizationId.value,
        environmentId: scope.environmentId.value,
        includeDisabled: false,
        keyword: deviceAssetId.value,
        skip: 0,
        take: PAGE_SIZE,
      },
    }),
    enabled: deviceEnabled.value,
  }))
  const deviceResponse = useScopeBoundListResponse(
    () => deviceQuery.data.value as BusinessConsoleResourceListEnvelope | undefined,
    deviceIdentity,
    deviceEnabled,
  )
  const device = computed<BusinessConsoleResourceItem | undefined>(() => {
    const envelope = deviceResponse.value
    if (envelope?.success !== true) return undefined
    const exactItems = (envelope.data?.resources ?? []).filter(
      (item) =>
        item.active !== false &&
        typeof item.deviceAssetId === 'string' &&
        item.deviceAssetId.trim() === deviceAssetId.value,
    )
    return exactItems.length === 1 ? exactItems[0] : undefined
  })
  const hasSuccessfulResponse = computed(() => Boolean(workOrder.value))
  const hasFailedResponse = computed(
    () =>
      enabled.value &&
      !detailQuery.isLoading.value &&
      (Boolean(detailQuery.error.value) ||
        (detailEnvelope.value !== undefined && !hasSuccessfulResponse.value)),
  )

  return {
    ...scope,
    workOrderId,
    enabled,
    workOrder,
    device,
    canReadDevice: scope.canReadDevice,
    pending: detailQuery.isLoading,
    error: detailQuery.error,
    hasSuccessfulResponse,
    hasFailedResponse,
    refresh: () => (enabled.value ? detailQuery.refetch() : Promise.resolve()),
  }
}
