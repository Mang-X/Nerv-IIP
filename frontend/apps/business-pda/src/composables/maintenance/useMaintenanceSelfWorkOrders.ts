import {
  listBusinessConsoleWorkers,
  listBusinessConsoleMaintenanceWorkOrders,
  type BusinessConsoleMaintenanceWorkOrderItem,
} from '@nerv-iip/api-client'
import {
  normalizeMaintenanceWorkOrderStatusFilter,
  type MaintenanceWorkOrderStatusCode,
} from '@nerv-iip/business-core'
import { useQuery } from '@pinia/colada'
import { computed, reactive } from 'vue'
import { useTaskListPagination } from '../shared/useTaskListPagination'
import {
  normalizeMaintenanceDeviceReferences,
  serializeMaintenanceKey,
} from './maintenancePublicIds'
import {
  nextMaintenanceQueryInstanceNonce,
  useFreshGenerationProjection,
  useMaintenanceQueryGeneration,
} from './maintenanceQueryGuard'
import { useMaintenanceSelfScope } from './useMaintenanceSelfScope'
import {
  parseExactWorker,
  parseWorkOrderPage,
  trimToUndefined,
  type WorkOrderListEnvelope,
  type WorkOrderPage,
} from './maintenanceParsers'

const PAGE_SIZE = 20

export interface MaintenanceSelfWorkOrderFilters {
  status: '' | MaintenanceWorkOrderStatusCode
  deviceAssetIds: string[]
  keyword: string
}

export function useMaintenanceSelfWorkOrders() {
  const instanceNonce = nextMaintenanceQueryInstanceNonce()
  const scope = useMaintenanceSelfScope()
  const filters = reactive<MaintenanceSelfWorkOrderFilters>({
    status: '',
    deviceAssetIds: [],
    keyword: '',
  })
  const normalizedDeviceAssetIds = computed(() =>
    normalizeMaintenanceDeviceReferences(filters.deviceAssetIds),
  )
  const listQueryParameters = () => ({
    ...scope.queryScope(),
    status: trimToUndefined(normalizeMaintenanceWorkOrderStatusFilter(filters.status)),
    deviceAssetReferences:
      normalizedDeviceAssetIds.value.length > 0 ? normalizedDeviceAssetIds.value : undefined,
    keyword: trimToUndefined(filters.keyword),
  })
  const identity = computed(() =>
    serializeMaintenanceKey({
      scope: [
        scope.organizationId.value,
        scope.environmentId.value,
        'self',
        scope.principalId.value,
      ],
      filters: {
        status: normalizeMaintenanceWorkOrderStatusFilter(filters.status),
        deviceAssetIds: normalizedDeviceAssetIds.value,
        keyword: filters.keyword.trim(),
      },
      instanceNonce,
    }),
  )
  const generation = useMaintenanceQueryGeneration(identity, scope.scopeReady)
  const principalIdentity = computed(() =>
    scope.scopeReady.value
      ? serializeMaintenanceKey({
          scope: [
            scope.organizationId.value,
            scope.environmentId.value,
            'self',
            scope.principalId.value,
          ],
          instanceNonce,
        })
      : '',
  )
  const principalGeneration = useMaintenanceQueryGeneration(principalIdentity, scope.scopeReady)
  const principalIdentityQuery = useQuery(() => {
    const requestedGeneration = principalGeneration.value
    const requestedIdentity = principalIdentity.value
    const organizationId = scope.organizationId.value
    const environmentId = scope.environmentId.value
    const principalId = scope.principalId.value
    return {
      key: [
        {
          _id: 'maintenance-list-principal',
          scope: [organizationId, environmentId, principalId],
          generation: requestedGeneration,
          identity: requestedIdentity,
        },
      ],
      enabled: scope.scopeReady.value,
      query: async () => {
        const { data } = await listBusinessConsoleWorkers({
          query: {
            organizationId,
            environmentId,
            userId: principalId,
            pageIndex: 1,
            pageSize: 1,
            includeDisabled: true,
          },
          throwOnError: true,
        })
        const displayName = parseExactWorker(data, principalId)
        if (!displayName) throw new Error('身份资料暂不可用')
        return { generation: requestedGeneration, identity: requestedIdentity, value: displayName }
      },
    }
  })
  const principalIdentityResponse = useFreshGenerationProjection(
    () => principalIdentityQuery.data.value,
    principalIdentity,
    principalGeneration,
    scope.scopeReady,
  )
  const principalDisplayName = computed(() =>
    !principalIdentityQuery.isLoading.value && !principalIdentityQuery.error.value
      ? principalIdentityResponse.value
      : undefined,
  )

  const listQuery = useQuery(() => {
    const requestedGeneration = generation.value
    const requestedIdentity = identity.value
    const request = {
      query: {
        ...listQueryParameters(),
        skip: 0,
        take: PAGE_SIZE,
      },
    }
    return {
      key: [
        {
          _id: 'maintenance-list',
          request,
          generation: requestedGeneration,
          identity: requestedIdentity,
        },
      ],
      enabled: scope.scopeReady.value,
      query: async () => {
        const { data } = await listBusinessConsoleMaintenanceWorkOrders({
          ...request,
          throwOnError: true,
        })
        return {
          generation: requestedGeneration,
          identity: requestedIdentity,
          value: data as WorkOrderListEnvelope,
        }
      },
    }
  })
  const response = useFreshGenerationProjection(
    () => listQuery.data.value,
    identity,
    generation,
    scope.scopeReady,
  )
  const firstPageState = computed<{ page?: WorkOrderPage; error?: Error }>(() => {
    if (response.value === undefined) return {}
    try {
      return { page: parseWorkOrderPage(response.value, 0, PAGE_SIZE, scope.principalId.value) }
    } catch (error) {
      return {
        error: error instanceof Error ? error : new Error('维修工单读取失败，请重试。'),
      }
    }
  })
  const firstPage = computed(() => firstPageState.value.page)
  const pagerIdentity = computed(() => serializeMaintenanceKey([identity.value, generation.value]))
  const pager = useTaskListPagination<BusinessConsoleMaintenanceWorkOrderItem>({
    identity: pagerIdentity,
    firstPage,
    pageSize: PAGE_SIZE,
    itemKey: (item) => item.workOrderId ?? '',
    fetchPage: async ({ skip, take }) => {
      const query = listQueryParameters()
      const principalId = scope.principalId.value
      const { data } = await listBusinessConsoleMaintenanceWorkOrders({
        query: {
          ...query,
          skip,
          take,
        },
        throwOnError: true,
      })
      return parseWorkOrderPage(data, skip, take, principalId)
    },
    refreshFirstPage: listQuery.refetch,
  })
  const pending = computed(
    () =>
      scope.scopeReady.value &&
      (listQuery.isLoading.value ||
        pager.refreshing.value ||
        (!listQuery.error.value && response.value === undefined)),
  )
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
  const refresh = async () => {
    if (!scope.scopeReady.value) return
    const requestedIdentity = identity.value
    const requestedGeneration = generation.value
    const requestedPrincipalIdentity = principalIdentity.value
    const requestedPrincipalGeneration = principalGeneration.value
    await pager.refresh()
    if (
      !scope.scopeReady.value ||
      identity.value !== requestedIdentity ||
      generation.value !== requestedGeneration ||
      principalIdentity.value !== requestedPrincipalIdentity ||
      principalGeneration.value !== requestedPrincipalGeneration
    ) {
      return
    }
    await principalIdentityQuery.refetch()
  }

  return {
    ...scope,
    filters,
    principalDisplayName,
    items: visibleItems,
    total: visibleTotal,
    loaded: visibleLoaded,
    hasMore: visibleHasMore,
    loadingMore: pager.loadingMore,
    refreshing: pager.refreshing,
    loadMoreError: pager.loadMoreError,
    loadMore: pager.loadMore,
    refresh,
    pending,
    error,
    hasSuccessfulResponse,
    hasFailedResponse,
  }
}
