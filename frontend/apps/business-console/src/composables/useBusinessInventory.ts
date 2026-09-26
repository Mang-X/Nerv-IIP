import {
  cancelBusinessConsoleInventoryCountTaskMutationOptions,
  confirmBusinessConsoleInventoryCountAdjustmentMutationOptions,
  createBusinessConsoleInventoryCountTaskMutationOptions,
  createOrUpdateBusinessConsoleInventoryLocationMutationOptions,
  restartBusinessConsoleInventoryCountTaskMutationOptions,
  getBusinessConsoleInventoryAvailabilityQueryOptions,
  listBusinessConsoleInventoryCountAdjustmentsQueryOptions,
  listBusinessConsoleInventoryCountTasksQueryOptions,
  listBusinessConsoleInventoryExpiryAlertsQueryOptions,
  listBusinessConsoleInventoryLocations,
  listBusinessConsoleInventoryLocationsQueryOptions,
  listBusinessConsoleInventoryMovementsQueryOptions,
  postBusinessConsoleInventoryMovementMutationOptions,
  type BusinessConsoleConfirmStockCountAdjustmentRequest,
  type BusinessConsoleCreateOrUpdateInventoryLocationRequest,
  type BusinessConsoleCreateStockCountTaskRequest,
  type BusinessConsoleInventoryAvailabilityEnvelope,
  type BusinessConsoleInventoryAvailabilityLineResponse,
  type BusinessConsoleInventoryAvailabilityResponse,
  type BusinessConsoleInventoryCountAdjustmentLineResponse,
  type BusinessConsoleInventoryCountAdjustmentListResponse,
  type BusinessConsoleInventoryCountTaskLineResponse,
  type BusinessConsoleInventoryCountTaskListResponse,
  type BusinessConsoleInventoryExpiryAlertLineResponse,
  type BusinessConsoleInventoryExpiryAlertsResponse,
  type BusinessConsoleInventoryLocationListResponse,
  type BusinessConsoleInventoryLocationResponse,
  type BusinessConsoleInventoryMovementLineResponse,
  type BusinessConsoleInventoryMovementListResponse,
  type BusinessConsolePostStockMovementRequest,
} from '@nerv-iip/api-client'
import { useMutation, useQuery, useQueryCache, type UseQueryEntry } from '@pinia/colada'
import { computed, reactive, shallowRef, watch } from 'vue'
import { useBusinessContextStore } from '@/stores/businessContext'
import { bindBusinessContext, type BusinessContextFields } from './businessContextBinding'

export interface InventoryAvailabilityFilters {
  organizationId: string
  environmentId: string
  skuCode: string
  uomCode: string
  siteCode: string
  locationCode?: string
  lotNo?: string
  serialNo?: string
  qualityStatus?: string
  ownerType?: string
  ownerId?: string
}

export interface InventoryActionContext extends BusinessContextFields {}

export interface InventoryExpiryFilters extends BusinessContextFields {
  siteCode: string
  skuCode?: string
  locationCode?: string
}

export function buildInventoryExpiryAlertsQuery(
  filters: InventoryExpiryFilters,
  page = 1,
  pageSize = 50,
) {
  return {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
    siteCode: filters.siteCode,
    ...(filters.skuCode ? { skuCode: filters.skuCode } : {}),
    ...(filters.locationCode ? { locationCode: filters.locationCode } : {}),
    nearExpiryThresholdDays: 30,
    includeZeroAvailable: true,
    page,
    pageSize,
  }
}

export function inventoryExpiryPagingScope(filters: InventoryExpiryFilters) {
  return [
    filters.organizationId,
    filters.environmentId,
    filters.siteCode,
    filters.skuCode,
    filters.locationCode,
  ]
}

function defaultAvailabilityFilters(): InventoryAvailabilityFilters {
  return bindBusinessContext(
    reactive({
      organizationId: '',
      environmentId: '',
      skuCode: '',
      uomCode: '',
      siteCode: '',
      qualityStatus: 'available',
      ownerType: 'owned',
    }),
  )
}

function optionalQuery<TKey extends string, TValue>(key: TKey, value: TValue | undefined) {
  return value === undefined || value === '' ? {} : { [key]: value }
}

function toAvailabilityQuery(filters: InventoryAvailabilityFilters) {
  return {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
    skuCode: filters.skuCode,
    uomCode: filters.uomCode,
    siteCode: filters.siteCode,
    ...optionalQuery('locationCode', filters.locationCode),
    ...optionalQuery('lotNo', filters.lotNo),
    ...optionalQuery('serialNo', filters.serialNo),
    ...optionalQuery('qualityStatus', filters.qualityStatus),
    ...optionalQuery('ownerType', filters.ownerType),
    ...optionalQuery('ownerId', filters.ownerId),
  }
}

function hasRequiredAvailabilityScope(filters: InventoryAvailabilityFilters) {
  return (
    filters.organizationId.trim().length > 0 &&
    filters.environmentId.trim().length > 0 &&
    filters.skuCode.trim().length > 0 &&
    filters.uomCode.trim().length > 0 &&
    filters.siteCode.trim().length > 0
  )
}

function unwrapAvailability(
  envelope: BusinessConsoleInventoryAvailabilityEnvelope | undefined,
): BusinessConsoleInventoryAvailabilityResponse | undefined {
  if (!envelope?.success) {
    return undefined
  }

  return envelope.data ?? undefined
}

function isBusinessQuery(id: string) {
  return (entry: UseQueryEntry) => {
    const keyParts = Array.isArray(entry.key) ? entry.key : [entry.key]

    return keyParts.some((part) => {
      return typeof part === 'object' && part !== null && '_id' in part && part._id === id
    })
  }
}

function ignoreBackgroundError(_error: unknown) {}

export function useInventoryAvailability() {
  const filters = defaultAvailabilityFilters()
  const availabilityEnabled = computed(() => hasRequiredAvailabilityScope(filters))

  const availabilityQuery = useQuery(() => ({
    ...getBusinessConsoleInventoryAvailabilityQueryOptions({
      query: toAvailabilityQuery(filters),
    }),
    enabled: availabilityEnabled.value,
  }))

  const availability = computed(() => unwrapAvailability(availabilityQuery.data.value))

  return {
    availability,
    availabilityError: availabilityQuery.error,
    availabilityLines: computed<BusinessConsoleInventoryAvailabilityLineResponse[]>(
      () => availability.value?.items ?? [],
    ),
    availabilityPending: availabilityQuery.isLoading,
    filters,
    refreshAvailability: () =>
      availabilityEnabled.value ? availabilityQuery.refetch() : Promise.resolve(),
  }
}

export function useInventoryExpiryAlerts(enabledWhen: () => boolean = () => true) {
  const filters = bindBusinessContext(
    reactive<InventoryExpiryFilters>({
      organizationId: '',
      environmentId: '',
      siteCode: '',
    }),
  )
  const page = shallowRef(1)
  const pageSize = shallowRef(50)
  const enabled = computed(
    () =>
      enabledWhen() &&
      filters.organizationId.trim().length > 0 &&
      filters.environmentId.trim().length > 0 &&
      filters.siteCode.trim().length > 0,
  )
  const query = useQuery(() => ({
    ...listBusinessConsoleInventoryExpiryAlertsQueryOptions({
      query: buildInventoryExpiryAlertsQuery(filters, page.value, pageSize.value),
    }),
    enabled: enabled.value,
  }))
  const response = computed<BusinessConsoleInventoryExpiryAlertsResponse | undefined>(() => {
    if (!query.data.value?.success) return undefined
    return query.data.value.data ?? undefined
  })
  watch(
    () => inventoryExpiryPagingScope(filters),
    () => {
      page.value = 1
    },
    { flush: 'sync' },
  )

  return {
    filters,
    expiryAlertsResponse: response,
    expiryAlerts: computed<BusinessConsoleInventoryExpiryAlertLineResponse[]>(
      () => response.value?.items ?? [],
    ),
    expiryAlertsError: query.error,
    expiryAlertsPending: query.isLoading,
    expiryAlertsPage: page,
    expiryAlertsPageSize: pageSize,
    expiryAlertsTotal: computed(() => response.value?.totalCount ?? 0),
    expiryAlertsSuccessful: computed(() => response.value !== undefined && !query.error.value),
    refreshExpiryAlerts: () => (enabled.value ? query.refetch() : Promise.resolve()),
  }
}

export interface InventoryMovementFilters extends BusinessContextFields {
  skuCode?: string
  siteCode?: string
  locationCode?: string
  lotNo?: string
  movementType?: string
  fromDate?: string
  toDate?: string
}

function hasBusinessScope(filters: BusinessContextFields) {
  return filters.organizationId.trim().length > 0 && filters.environmentId.trim().length > 0
}

/**
 * 库存移动。
 *
 * 此前只有过账 mutation，页面表格挂在会话内本地队列上、刷新即空；现在读面来自服务端分页，
 * 过账成功后失效查询即可，本地队列不再需要。
 */
export function useInventoryMovement() {
  const queryCache = useQueryCache()
  const filters = bindBusinessContext(
    reactive<InventoryMovementFilters>({
      organizationId: '',
      environmentId: '',
    }),
  )
  const page = shallowRef(1)
  const pageSize = shallowRef(50)
  const enabled = computed(() => hasBusinessScope(filters))

  const movementsQuery = useQuery(() => ({
    ...listBusinessConsoleInventoryMovementsQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        ...optionalQuery('skuCode', filters.skuCode),
        ...optionalQuery('siteCode', filters.siteCode),
        ...optionalQuery('locationCode', filters.locationCode),
        ...optionalQuery('lotNo', filters.lotNo),
        ...optionalQuery('movementType', filters.movementType),
        ...optionalQuery('fromDate', filters.fromDate),
        ...optionalQuery('toDate', filters.toDate),
        page: page.value,
        pageSize: pageSize.value,
      },
    }),
    enabled: enabled.value,
  }))
  const movements = computed<BusinessConsoleInventoryMovementListResponse | undefined>(() => {
    if (!movementsQuery.data.value?.success) return undefined
    return movementsQuery.data.value.data ?? undefined
  })

  watch(
    () => [
      filters.organizationId,
      filters.environmentId,
      filters.skuCode,
      filters.siteCode,
      filters.locationCode,
      filters.lotNo,
      filters.movementType,
      filters.fromDate,
      filters.toDate,
    ],
    () => {
      page.value = 1
    },
    { flush: 'sync' },
  )

  const movementMutation = useMutation({
    ...postBusinessConsoleInventoryMovementMutationOptions(),
    onSuccess() {
      void queryCache
        .invalidateQueries({
          predicate: isBusinessQuery('getBusinessConsoleInventoryAvailability'),
        })
        .catch(ignoreBackgroundError)
      void queryCache
        .invalidateQueries({
          predicate: isBusinessQuery('listBusinessConsoleInventoryMovements'),
        })
        .catch(ignoreBackgroundError)
    },
  })

  return {
    filters,
    movements,
    movementRows: computed<BusinessConsoleInventoryMovementLineResponse[]>(
      () => movements.value?.items ?? [],
    ),
    movementsError: movementsQuery.error,
    movementsPending: movementsQuery.isLoading,
    movementsPage: page,
    movementsPageSize: pageSize,
    movementsTotal: computed(() => movements.value?.totalCount ?? 0),
    refreshMovements: () => (enabled.value ? movementsQuery.refetch() : Promise.resolve()),
    postMovement: (body: BusinessConsolePostStockMovementRequest) =>
      movementMutation.mutateAsync({ body }),
    postMovementError: movementMutation.error,
    postMovementPending: movementMutation.isLoading,
  }
}

export interface InventoryCountFilters extends BusinessContextFields {
  status?: string
  skuCode?: string
  siteCode?: string
  locationCode?: string
}

/**
 * 库存盘点。
 *
 * 此前只有 mutation、没有任何 QueryOptions，页面表格挂在会话内本地队列上、刷新即空。
 * 现在盘点任务与盘点调整都来自服务端分页读面；建任务 / 确认差异成功后失效查询即可，
 * 新建的任务刷新之后仍然在。
 */
export function useInventoryCounts() {
  const queryCache = useQueryCache()
  const filters = bindBusinessContext(
    reactive<InventoryCountFilters>({
      organizationId: '',
      environmentId: '',
    }),
  )
  const page = shallowRef(1)
  const pageSize = shallowRef(50)
  const enabled = computed(() => hasBusinessScope(filters))

  const countTasksQuery = useQuery(() => ({
    ...listBusinessConsoleInventoryCountTasksQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        ...optionalQuery('status', filters.status),
        ...optionalQuery('skuCode', filters.skuCode),
        ...optionalQuery('siteCode', filters.siteCode),
        ...optionalQuery('locationCode', filters.locationCode),
        page: page.value,
        pageSize: pageSize.value,
      },
    }),
    enabled: enabled.value,
  }))
  const countTasks = computed<BusinessConsoleInventoryCountTaskListResponse | undefined>(() => {
    if (!countTasksQuery.data.value?.success) return undefined
    return countTasksQuery.data.value.data ?? undefined
  })

  const adjustmentsQuery = useQuery(() => ({
    ...listBusinessConsoleInventoryCountAdjustmentsQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        page: 1,
        pageSize: pageSize.value,
      },
    }),
    enabled: enabled.value,
  }))
  const adjustments = computed<BusinessConsoleInventoryCountAdjustmentListResponse | undefined>(
    () => {
      if (!adjustmentsQuery.data.value?.success) return undefined
      return adjustmentsQuery.data.value.data ?? undefined
    },
  )

  watch(
    () => [
      filters.organizationId,
      filters.environmentId,
      filters.status,
      filters.skuCode,
      filters.siteCode,
      filters.locationCode,
    ],
    () => {
      page.value = 1
    },
    { flush: 'sync' },
  )

  function invalidateCountQueries() {
    for (const id of [
      'listBusinessConsoleInventoryCountTasks',
      'listBusinessConsoleInventoryCountAdjustments',
      'getBusinessConsoleInventoryAvailability',
    ]) {
      void queryCache
        .invalidateQueries({ predicate: isBusinessQuery(id) })
        .catch(ignoreBackgroundError)
    }
  }

  const createCountTaskMutation = useMutation({
    ...createBusinessConsoleInventoryCountTaskMutationOptions(),
    onSuccess: invalidateCountQueries,
  })
  const confirmAdjustmentMutation = useMutation({
    ...confirmBusinessConsoleInventoryCountAdjustmentMutationOptions(),
    onSuccess: invalidateCountQueries,
  })
  // 重盘：把 recount-required 的死单重新冻结台账、重取快照后放回待实盘。
  const restartCountTaskMutation = useMutation({
    ...restartBusinessConsoleInventoryCountTaskMutationOptions(),
    onSuccess: invalidateCountQueries,
  })
  // 关闭：任务不再盘了就作废收尾，台账同时解冻。
  const cancelCountTaskMutation = useMutation({
    ...cancelBusinessConsoleInventoryCountTaskMutationOptions(),
    onSuccess: invalidateCountQueries,
  })

  return {
    confirmAdjustment: (
      countTaskId: string,
      body: BusinessConsoleConfirmStockCountAdjustmentRequest,
    ) =>
      confirmAdjustmentMutation.mutateAsync({
        path: {
          countTaskId,
        },
        query: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
        },
        body,
      }),
    confirmAdjustmentError: confirmAdjustmentMutation.error,
    confirmAdjustmentPending: confirmAdjustmentMutation.isLoading,
    createCountTask: (body: BusinessConsoleCreateStockCountTaskRequest) =>
      createCountTaskMutation.mutateAsync({ body }),
    createCountTaskError: createCountTaskMutation.error,
    createCountTaskPending: createCountTaskMutation.isLoading,
    restartCountTask: (countTaskId: string) =>
      restartCountTaskMutation.mutateAsync({
        path: { countTaskId },
        query: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
        },
      }),
    restartCountTaskError: restartCountTaskMutation.error,
    restartCountTaskPending: restartCountTaskMutation.isLoading,
    cancelCountTask: (countTaskId: string, reason: string) =>
      cancelCountTaskMutation.mutateAsync({
        path: { countTaskId },
        query: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
        },
        body: { reason },
      }),
    cancelCountTaskError: cancelCountTaskMutation.error,
    cancelCountTaskPending: cancelCountTaskMutation.isLoading,
    countTasks,
    countTaskRows: computed<BusinessConsoleInventoryCountTaskLineResponse[]>(
      () => countTasks.value?.items ?? [],
    ),
    countTasksError: countTasksQuery.error,
    countTasksPending: countTasksQuery.isLoading,
    countTasksPage: page,
    countTasksPageSize: pageSize,
    countTasksTotal: computed(() => countTasks.value?.totalCount ?? 0),
    countAdjustments: adjustments,
    countAdjustmentRows: computed<BusinessConsoleInventoryCountAdjustmentLineResponse[]>(
      () => adjustments.value?.items ?? [],
    ),
    refreshCountTasks: () => (enabled.value ? countTasksQuery.refetch() : Promise.resolve()),
    filters,
  }
}

export interface InventoryLocationFilters extends BusinessContextFields {
  keyword?: string
}

/**
 * 库位主数据维护（#3770）：列出全部库位（含停用），新增与编辑都走 Inventory 的库位 upsert。
 */
export function useInventoryLocations() {
  const filters = bindBusinessContext(
    reactive<InventoryLocationFilters>({
      organizationId: '',
      environmentId: '',
    }),
  )
  const page = shallowRef(1)
  const pageSize = shallowRef(20)
  const enabled = computed(() => hasBusinessScope(filters))

  const locationsQuery = useQuery(() => ({
    ...listBusinessConsoleInventoryLocationsQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        ...optionalQuery('keyword', filters.keyword?.trim()),
        page: page.value,
        pageSize: pageSize.value,
      },
    }),
    enabled: enabled.value,
  }))
  const locations = computed<BusinessConsoleInventoryLocationListResponse | undefined>(() => {
    if (!locationsQuery.data.value?.success) return undefined
    return locationsQuery.data.value.data ?? undefined
  })

  watch(
    () => [filters.organizationId, filters.environmentId, filters.keyword],
    () => {
      page.value = 1
    },
    { flush: 'sync' },
  )

  const refreshLocations = () => (enabled.value ? locationsQuery.refetch() : Promise.resolve())

  return {
    filters,
    locationRows: computed<BusinessConsoleInventoryLocationResponse[]>(
      () => locations.value?.items ?? [],
    ),
    locationsError: locationsQuery.error,
    locationsPending: locationsQuery.isLoading,
    locationsPage: page,
    locationsPageSize: pageSize,
    locationsTotal: computed(() => locations.value?.totalCount ?? 0),
    refreshLocations,
  }
}

/**
 * 新建 / 编辑库位（upsert）。库位维护页与表单里库位选择器的就地新增共用：
 * 保存后刷新库位列表与网关可搜目录，选择器才搜得到刚建的库位。
 */
export function useInventoryLocationSave() {
  const context = useBusinessContextStore()
  const queryCache = useQueryCache()
  const saveLocationMutation = useMutation({
    ...createOrUpdateBusinessConsoleInventoryLocationMutationOptions(),
    onSuccess: () => {
      for (const id of [
        'listBusinessConsoleInventoryLocations',
        'listBusinessConsoleSearchableDirectory',
      ]) {
        void queryCache
          .invalidateQueries({ predicate: isBusinessQuery(id) })
          .catch(ignoreBackgroundError)
      }
    },
  })

  /**
   * 库位保存是 upsert：新建时若编码已存在会直接改写那条库位。
   * 新建前按编码精确查一次，把「已存在」挡在提交之前。
   */
  async function locationCodeExists(locationCode: string) {
    const { data } = await listBusinessConsoleInventoryLocations({
      query: {
        organizationId: context.organizationId,
        environmentId: context.environmentId,
        keyword: locationCode,
        page: 1,
        pageSize: 100,
      },
      throwOnError: true,
    })
    return (data.data?.items ?? []).some((item) => item.locationCode === locationCode)
  }

  return {
    locationCodeExists,
    saveLocation: (body: BusinessConsoleCreateOrUpdateInventoryLocationRequest) =>
      saveLocationMutation.mutateAsync({ body }),
    saveLocationPending: saveLocationMutation.isLoading,
  }
}
