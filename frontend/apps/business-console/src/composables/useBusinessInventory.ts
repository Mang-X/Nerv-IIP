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
import { useMutation, useQuery, useQueryCache, type UseQueryEntry } from '@pinia/colada'
import { computed, reactive } from 'vue'

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

function defaultActionContext(): InventoryActionContext {
  return reactive({
    organizationId: 'org-001',
    environmentId: 'env-dev',
  })
}

function defaultAvailabilityFilters(): InventoryAvailabilityFilters {
  return reactive({
    organizationId: 'org-001',
    environmentId: 'env-dev',
    skuCode: 'SKU-001',
    uomCode: 'EA',
    siteCode: 'S1',
    qualityStatus: 'available',
    ownerType: 'owned',
  })
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

  const availabilityQuery = useQuery(() =>
    getBusinessConsoleInventoryAvailabilityQueryOptions({
      query: toAvailabilityQuery(filters),
    }),
  )

  const availability = computed(() => unwrapAvailability(availabilityQuery.data.value))

  return {
    availability,
    availabilityError: availabilityQuery.error,
    availabilityLines: computed<BusinessConsoleInventoryAvailabilityLineResponse[]>(
      () => availability.value?.items ?? [],
    ),
    availabilityPending: availabilityQuery.isLoading,
    filters,
    refreshAvailability: availabilityQuery.refetch,
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
