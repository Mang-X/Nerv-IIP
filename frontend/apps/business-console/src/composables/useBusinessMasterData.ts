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
  skip: number
  take: number
}

export interface MasterDataResourceFilters extends MasterDataListFilters {
  resourceType: string
}

export interface BusinessMasterDataGroupDefinition {
  key: string
  title: string
  resourceType?: string
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
    skip: 0,
    take: DEFAULT_TAKE,
  })
}

function defaultResourceFilters(resourceType: string): MasterDataResourceFilters {
  return reactive({
    ...defaultContext(),
    resourceType,
    skip: 0,
    take: DEFAULT_TAKE,
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

function resourceTotal(envelope: BusinessConsoleResourceListEnvelope | undefined) {
  if (!envelope?.success) {
    return 0
  }

  return envelope.data?.total ?? 0
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
        skip: filters.skip,
        take: filters.take,
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
    skusTotal: computed(() => resourceTotal(skusQuery.data.value)),
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
        skip: filters.skip,
        take: filters.take,
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
    resourcesTotal: computed(() => resourceTotal(resourcesQuery.data.value)),
  }
}

export function useBusinessMasterDataGroups(definitions: BusinessMasterDataGroupDefinition[]) {
  const filters = defaultListFilters()
  const queries = definitions.map((definition) =>
    useQuery(() =>
      listBusinessConsoleMasterDataResourcesQueryOptions({
        query: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
          resourceType: definition.resourceType ?? definition.key,
          ...optionalQuery('includeDisabled', filters.includeDisabled),
          skip: filters.skip,
          take: filters.take,
        },
      }),
    ),
  )

  return {
    filters,
    groups: computed(() =>
      definitions.map((definition, index) => ({
        ...definition,
        resourceType: definition.resourceType ?? definition.key,
        rows: resourceItems(queries[index]?.data.value),
        total: resourceTotal(queries[index]?.data.value),
      })),
    ),
    groupsError: computed(() => queries.map((query) => query.error.value).find(Boolean)),
    groupsPending: computed(() => queries.some((query) => query.isLoading.value)),
    groupsTotal: computed(() =>
      queries.reduce((total, query) => total + resourceTotal(query.data.value), 0),
    ),
    refreshGroups: () => Promise.all(queries.map((query) => query.refetch())),
  }
}
