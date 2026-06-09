import {
  confirmBusinessConsoleInventoryCountAdjustmentMutationOptions,
  createBusinessConsoleInventoryCountTaskMutationOptions,
  getBusinessConsoleInventoryAvailabilityQueryOptions,
  postBusinessConsoleInventoryMovementMutationOptions,
  type BusinessConsoleConfirmStockCountAdjustmentRequest,
  type BusinessConsoleCreateStockCountTaskRequest,
  type BusinessConsoleInventoryAvailabilityEnvelope,
  type BusinessConsoleInventoryAvailabilityLineResponse,
  type BusinessConsoleInventoryAvailabilityResponse,
  type BusinessConsolePostStockMovementRequest,
} from '@nerv-iip/api-client'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useMutation, useQuery, useQueryCache, type UseQueryEntry } from '@pinia/colada'
import { computed, reactive, watch } from 'vue'

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

export interface InventoryActionContext {
  organizationId: string
  environmentId: string
}

function bindBusinessContext<T extends InventoryActionContext>(filters: T): T {
  const context = useBusinessContextStore()

  watch(
    () => [context.organizationId, context.environmentId] as const,
    ([organizationId, environmentId]) => {
      filters.organizationId = organizationId
      filters.environmentId = environmentId
    },
    { flush: 'sync', immediate: true },
  )

  return filters
}

function defaultActionContext(): InventoryActionContext {
  return bindBusinessContext(reactive({
    organizationId: '',
    environmentId: '',
  }))
}

function defaultAvailabilityFilters(): InventoryAvailabilityFilters {
  return bindBusinessContext(reactive({
    organizationId: '',
    environmentId: '',
    skuCode: '',
    uomCode: '',
    siteCode: '',
    qualityStatus: 'available',
    ownerType: 'owned',
  }))
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
    refreshAvailability: () => availabilityEnabled.value ? availabilityQuery.refetch() : Promise.resolve(),
  }
}

export function useInventoryMovement() {
  const queryCache = useQueryCache()
  const movementMutation = useMutation({
    ...postBusinessConsoleInventoryMovementMutationOptions(),
    onSuccess() {
      void queryCache
        .invalidateQueries({ predicate: isBusinessQuery('getBusinessConsoleInventoryAvailability') })
        .catch(ignoreBackgroundError)
    },
  })

  return {
    postMovement: (body: BusinessConsolePostStockMovementRequest) =>
      movementMutation.mutateAsync({ body }),
    postMovementError: movementMutation.error,
    postMovementPending: movementMutation.isLoading,
  }
}

export function useInventoryCounts() {
  const filters = defaultActionContext()
  const queryCache = useQueryCache()
  const createCountTaskMutation = useMutation(createBusinessConsoleInventoryCountTaskMutationOptions())
  const confirmAdjustmentMutation = useMutation({
    ...confirmBusinessConsoleInventoryCountAdjustmentMutationOptions(),
    onSuccess() {
      void queryCache
        .invalidateQueries({ predicate: isBusinessQuery('getBusinessConsoleInventoryAvailability') })
        .catch(ignoreBackgroundError)
    },
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
    filters,
  }
}
