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

import { useListFreshness, useScopeBoundListResponse } from './useListFreshness'
import { useTaskListPagination } from './useTaskListPagination'
import {
  MAINTENANCE_READ_MODEL_PERMISSIONS,
  canAccessMaintenanceWorkOrderReadModel,
} from '@/permissions/maintenanceReadModelAccess'
import { useAuthStore } from '@/stores/auth'

const PAGE_SIZE = 20

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

interface WorkOrderPage {
  items: BusinessConsoleMaintenanceWorkOrderItem[]
  total: number
}

export interface MaintenanceSelfWorkOrderFilters {
  status: '' | MaintenanceWorkOrderStatusCode
  deviceAssetId: string
  keyword: string
}

export type AuthoritativeMaintenanceWorkOrderDetail = BusinessConsoleMaintenanceWorkOrderItem & {
  workOrderId: string
  deviceAssetId: string
  priority: string
  status: string
  openedAtUtc: string
  version: number
  allowedActions: string[]
  blockReasons: string[]
  lifecycle: NonNullable<BusinessConsoleMaintenanceWorkOrderItem['lifecycle']>
  assignedTechnicianUserId: string | null
  assignedTeamId: string | null
}

function trimToUndefined(value: string) {
  return value.trim() || undefined
}

function isNonBlankString(value: unknown): value is string {
  return typeof value === 'string' && value.trim().length > 0
}

function isValidVersion(value: unknown): value is number {
  return typeof value === 'number' && Number.isSafeInteger(value) && value >= 0
}

function isExplicitAssignment(value: unknown) {
  return value === null || isNonBlankString(value)
}

function parseWorkOrderPage(envelope: unknown, skip: number, take: number): WorkOrderPage {
  if (!envelope || typeof envelope !== 'object' || Array.isArray(envelope)) {
    throw new Error('维修工单读取失败，请重试。')
  }
  const response = envelope as Exclude<WorkOrderListEnvelope, undefined>
  const data = response.data
  if (
    response.success !== true ||
    !data ||
    typeof data !== 'object' ||
    Array.isArray(data) ||
    !Array.isArray(data.items) ||
    !Number.isSafeInteger(data.total) ||
    (data.total ?? -1) < 0
  ) {
    throw new Error('维修工单读取失败，请重试。')
  }
  const total = data.total as number
  const items = data.items
  if (items.length > take || total < skip + items.length || (skip < total && items.length === 0)) {
    throw new Error('维修工单分页信息不一致，请重试。')
  }
  return { items, total }
}

function isAuthoritativeMaintenanceWorkOrderDetail(
  value: BusinessConsoleMaintenanceWorkOrderItem | null | undefined,
  requestedWorkOrderId: string,
): value is AuthoritativeMaintenanceWorkOrderDetail {
  return Boolean(
    value &&
    typeof value === 'object' &&
    value.workOrderId === requestedWorkOrderId &&
    isNonBlankString(value.deviceAssetId) &&
    isNonBlankString(value.priority) &&
    isNonBlankString(value.status) &&
    isNonBlankString(value.openedAtUtc) &&
    Number.isFinite(Date.parse(value.openedAtUtc)) &&
    isValidVersion(value.version) &&
    Array.isArray(value.allowedActions) &&
    value.allowedActions.every(isNonBlankString) &&
    Array.isArray(value.blockReasons) &&
    value.blockReasons.every(isNonBlankString) &&
    Object.hasOwn(value, 'assignedTechnicianUserId') &&
    isExplicitAssignment(value.assignedTechnicianUserId) &&
    Object.hasOwn(value, 'assignedTeamId') &&
    isExplicitAssignment(value.assignedTeamId) &&
    Array.isArray(value.lifecycle) &&
    value.lifecycle.every(
      (event) =>
        event !== null &&
        typeof event === 'object' &&
        isNonBlankString(event.action) &&
        isNonBlankString(event.fromStatus) &&
        isNonBlankString(event.toStatus) &&
        isValidVersion(event.resultingVersion) &&
        isNonBlankString(event.occurredAtUtc) &&
        Number.isFinite(Date.parse(event.occurredAtUtc)),
    ),
  )
}

function useMaintenanceSelfScope() {
  const auth = useAuthStore()
  const organizationId = computed(() => auth.principal?.organizationId?.trim() ?? '')
  const environmentId = computed(() => auth.principal?.environmentId?.trim() ?? '')
  const principalId = computed(() => auth.principal?.principalId?.trim() ?? '')
  const permissions = computed(() => new Set(auth.principal?.permissionCodes ?? []))
  const canReadMaintenance = computed(() =>
    permissions.value.has(MAINTENANCE_READ_MODEL_PERMISSIONS.workOrders),
  )
  const canReadDevice = computed(() =>
    permissions.value.has(MAINTENANCE_READ_MODEL_PERMISSIONS.masterDataResources),
  )
  const canRead = computed(() => canAccessMaintenanceWorkOrderReadModel(permissions.value))
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
    status: trimToUndefined(normalizeMaintenanceWorkOrderStatusFilter(filters.status)),
    deviceAssetId: trimToUndefined(filters.deviceAssetId),
    keyword: trimToUndefined(filters.keyword),
  })
  const identity = computed(
    () =>
      `${scope.scopeKey.value}:${normalizeMaintenanceWorkOrderStatusFilter(filters.status)}:${filters.deviceAssetId.trim()}:${filters.keyword.trim()}`,
  )

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
  const response = useScopeBoundListResponse(() => listQuery.data.value, identity, scope.scopeReady)
  const firstPageState = computed<{ page?: WorkOrderPage; error?: Error }>(() => {
    if (response.value === undefined) return {}
    try {
      return { page: parseWorkOrderPage(response.value, 0, PAGE_SIZE) }
    } catch (error) {
      return {
        error: error instanceof Error ? error : new Error('维修工单读取失败，请重试。'),
      }
    }
  })
  const firstPage = computed(() => firstPageState.value.page)
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
      return parseWorkOrderPage(data, skip, take)
    },
    refreshFirstPage: listQuery.refetch,
  })
  const pending = computed(() => listQuery.isLoading.value || pager.refreshing.value)
  const hasSuccessfulResponse = computed(
    () =>
      scope.scopeReady.value &&
      !pending.value &&
      !listQuery.error.value &&
      Boolean(firstPage.value),
  )
  const hasFailedResponse = computed(
    () =>
      scope.scopeReady.value &&
      !pending.value &&
      Boolean(listQuery.error.value || firstPageState.value.error),
  )
  const error = computed(() => listQuery.error.value ?? firstPageState.value.error)
  const freshness = useListFreshness(
    computed(() => {
      if (firstPage.value) return { success: true }
      if (firstPageState.value.error) return { success: false }
      return undefined
    }),
    scope.scopeReady,
  )
  const visibleItems = computed(() =>
    pending.value || hasFailedResponse.value ? [] : pager.items.value,
  )
  const visibleTotal = computed(() =>
    pending.value || hasFailedResponse.value ? 0 : pager.total.value,
  )
  const visibleLoaded = computed(() => visibleItems.value.length)
  const visibleHasMore = computed(
    () => !pending.value && !hasFailedResponse.value && pager.hasMore.value,
  )

  return {
    ...scope,
    filters,
    items: visibleItems,
    total: visibleTotal,
    loaded: visibleLoaded,
    hasMore: visibleHasMore,
    loadingMore: pager.loadingMore,
    refreshing: pager.refreshing,
    loadMoreError: pager.loadMoreError,
    loadMore: pager.loadMore,
    refresh: () => (scope.scopeReady.value ? pager.refresh() : Promise.resolve()),
    pending,
    error,
    lastUpdatedAt: freshness,
    hasSuccessfulResponse,
    hasFailedResponse,
  }
}

export function useMaintenanceSelfWorkOrderDetail(requestedWorkOrderId: MaybeRefOrGetter<string>) {
  const scope = useMaintenanceSelfScope()
  const workOrderId = computed(() => toValue(requestedWorkOrderId).trim())
  const enabled = computed(() => scope.scopeReady.value && Boolean(workOrderId.value))
  const detailIdentity = computed(() =>
    enabled.value ? `${scope.scopeKey.value}:work-order:${workOrderId.value}` : '',
  )
  const detailQuery = useQuery(() => ({
    ...getBusinessConsoleMaintenanceWorkOrderQueryOptions({
      path: { workOrderId: workOrderId.value },
      query: scope.queryScope(),
    }),
    enabled: enabled.value,
  }))
  const detailEnvelope = useScopeBoundListResponse(
    () => detailQuery.data.value as WorkOrderDetailEnvelope,
    detailIdentity,
    enabled,
  )
  const validatedWorkOrder = computed(() => {
    const envelope = detailEnvelope.value
    const item = envelope?.success === true ? envelope.data : undefined
    return isAuthoritativeMaintenanceWorkOrderDetail(item, workOrderId.value) ? item : undefined
  })

  const deviceAssetId = computed(() => validatedWorkOrder.value?.deviceAssetId?.trim() ?? '')
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
  const validatedDevice = computed<BusinessConsoleResourceItem | undefined>(() => {
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
  const deviceResponseAvailable = computed(() => deviceResponse.value !== undefined)
  const hasSuccessfulResponse = computed(() =>
    Boolean(
      validatedWorkOrder.value &&
      validatedDevice.value &&
      !detailQuery.isLoading.value &&
      !deviceQuery.isLoading.value &&
      !detailQuery.error.value &&
      !deviceQuery.error.value,
    ),
  )
  const workOrder = computed(() =>
    hasSuccessfulResponse.value ? validatedWorkOrder.value : undefined,
  )
  const device = computed(() => (hasSuccessfulResponse.value ? validatedDevice.value : undefined))
  const pending = computed(
    () =>
      enabled.value &&
      (detailQuery.isLoading.value ||
        Boolean(
          !detailQuery.error.value &&
          validatedWorkOrder.value &&
          (deviceQuery.isLoading.value ||
            (!deviceResponseAvailable.value && !deviceQuery.error.value)),
        )),
  )
  const hasFailedResponse = computed(
    () =>
      enabled.value &&
      !pending.value &&
      (Boolean(detailQuery.error.value) ||
        (detailEnvelope.value !== undefined && !validatedWorkOrder.value) ||
        Boolean(
          validatedWorkOrder.value &&
          (deviceQuery.error.value || (deviceResponseAvailable.value && !validatedDevice.value)),
        )),
  )
  const error = computed(() => detailQuery.error.value ?? deviceQuery.error.value)

  const refresh = async () => {
    if (!enabled.value) return
    await detailQuery.refetch()
    if (validatedWorkOrder.value && !detailQuery.error.value && deviceEnabled.value) {
      await deviceQuery.refetch()
    }
  }

  return {
    ...scope,
    workOrderId,
    enabled,
    workOrder,
    device,
    canReadDevice: scope.canReadDevice,
    pending,
    error,
    hasSuccessfulResponse,
    hasFailedResponse,
    refresh,
  }
}
