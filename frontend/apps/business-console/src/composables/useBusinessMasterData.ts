import {
  createBusinessConsoleSkuMutationOptions,
  listBusinessConsoleMasterDataResourcesQueryOptions,
  listBusinessConsoleSkusQueryOptions,
  type BusinessConsoleCreateSkuRequest,
  type BusinessConsoleResourceItem,
  type BusinessConsoleResourceListEnvelope,
} from '@nerv-iip/api-client'
import { useMutation, useQuery, useQueryCache, type UseQueryEntry } from '@pinia/colada'
import { computed, reactive } from 'vue'

const DEFAULT_TAKE = 100

export interface BusinessContextFilters {
  organizationId: string
  environmentId: string
}

export interface MasterDataListFilters extends BusinessContextFilters {
  includeDisabled?: boolean
}

export interface MasterDataResourceFilters extends MasterDataListFilters {
  resourceType: string
}

function defaultContext(): BusinessContextFilters {
  return reactive({
    organizationId: 'org-001',
    environmentId: 'env-dev',
  })
}

function defaultListFilters(): MasterDataListFilters {
  return reactive({
    ...defaultContext(),
  })
}

function defaultResourceFilters(resourceType: string): MasterDataResourceFilters {
  return reactive({
    ...defaultContext(),
    resourceType,
  })
}

function optionalQuery<TKey extends string, TValue>(key: TKey, value: TValue | undefined) {
  return value === undefined ? {} : { [key]: value }
}

function resourceItems(envelope: BusinessConsoleResourceListEnvelope | undefined) {
  if (!envelope?.success) {
    return []
  }

  return envelope.data?.resources ?? []
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

export function useBusinessSkus() {
  const filters = defaultListFilters()
  const queryCache = useQueryCache()

  const skusQuery = useQuery(() =>
    listBusinessConsoleSkusQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        ...optionalQuery('includeDisabled', filters.includeDisabled),
        take: DEFAULT_TAKE,
      },
    }),
  )

  const createSkuMutation = useMutation({
    ...createBusinessConsoleSkuMutationOptions(),
    onSuccess() {
      void queryCache
        .invalidateQueries({ predicate: isBusinessQuery('listBusinessConsoleSkus') })
        .catch(ignoreBackgroundError)
    },
  })

  return {
    createSku: (body: BusinessConsoleCreateSkuRequest) => createSkuMutation.mutateAsync({ body }),
    createSkuError: createSkuMutation.error,
    createSkuPending: createSkuMutation.isLoading,
    filters,
    refreshSkus: skusQuery.refetch,
    skus: computed<BusinessConsoleResourceItem[]>(() => resourceItems(skusQuery.data.value)),
    skusError: skusQuery.error,
    skusPending: skusQuery.isLoading,
  }
}

export function useBusinessMasterDataResources(resourceType: string) {
  const filters = defaultResourceFilters(resourceType)

  const resourcesQuery = useQuery(() =>
    listBusinessConsoleMasterDataResourcesQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        resourceType: filters.resourceType,
        ...optionalQuery('includeDisabled', filters.includeDisabled),
        take: DEFAULT_TAKE,
      },
    }),
  )

  return {
    filters,
    refreshResources: resourcesQuery.refetch,
    resources: computed<BusinessConsoleResourceItem[]>(() =>
      resourceItems(resourcesQuery.data.value),
    ),
    resourcesError: resourcesQuery.error,
    resourcesPending: resourcesQuery.isLoading,
  }
}
