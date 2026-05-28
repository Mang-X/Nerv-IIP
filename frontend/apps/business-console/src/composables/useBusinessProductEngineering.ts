import {
  listBusinessConsoleEngineeringBomsQueryOptions,
  listBusinessConsoleEngineeringProductionVersionsQueryOptions,
  listBusinessConsoleEngineeringRoutingsQueryOptions,
  resolveBusinessConsoleEngineeringProductionVersionQueryOptions,
  type BusinessConsoleEngineeringBomItem,
  type BusinessConsoleEngineeringBomListEnvelope,
  type BusinessConsoleProductionVersionItem,
  type BusinessConsoleProductionVersionListEnvelope,
  type BusinessConsoleResolveProductionVersionEnvelope,
  type BusinessConsoleResolveProductionVersionResponse,
  type BusinessConsoleRoutingItem,
  type BusinessConsoleRoutingListEnvelope,
} from '@nerv-iip/api-client'
import { useQuery } from '@pinia/colada'
import { computed, reactive } from 'vue'

export interface EngineeringListFilters {
  organizationId: string
  environmentId: string
  skuCode?: string
  parentItemCode?: string
  bomStatus: string
  routingStatus: string
  productionVersionStatus: string
}

export interface EngineeringResolveFilters {
  organizationId: string
  environmentId: string
  skuCode: string
  effectiveDate: string
  lotSize: number
}

function defaultListFilters(): EngineeringListFilters {
  return reactive({
    organizationId: 'org-001',
    environmentId: 'env-dev',
    bomStatus: 'Released',
    routingStatus: 'Released',
    productionVersionStatus: 'active',
  })
}

function defaultResolveFilters(): EngineeringResolveFilters {
  return reactive({
    organizationId: 'org-001',
    environmentId: 'env-dev',
    skuCode: 'FG-FRONT-SHOCK',
    effectiveDate: new Date().toISOString().slice(0, 10),
    lotSize: 100,
  })
}

function optionalQuery<TKey extends string, TValue>(key: TKey, value: TValue | undefined) {
  return value === undefined || value === '' ? {} : { [key]: value }
}

function unwrapItems<T>(envelope: { success?: boolean; data?: { items?: T[] } | null } | undefined): T[] {
  if (!envelope?.success) {
    return []
  }

  return envelope.data?.items ?? []
}

function unwrapResolved(
  envelope: BusinessConsoleResolveProductionVersionEnvelope | undefined,
): BusinessConsoleResolveProductionVersionResponse | undefined {
  if (!envelope?.success) {
    return undefined
  }

  return envelope.data ?? undefined
}

export function useBusinessProductEngineering() {
  const filters = defaultListFilters()
  const resolveFilters = defaultResolveFilters()

  const bomsQuery = useQuery(() =>
    listBusinessConsoleEngineeringBomsQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        ...optionalQuery('parentItemCode', filters.parentItemCode),
        ...optionalQuery('status', filters.bomStatus),
      },
    }),
  )
  const routingsQuery = useQuery(() =>
    listBusinessConsoleEngineeringRoutingsQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        ...optionalQuery('skuCode', filters.skuCode),
        ...optionalQuery('status', filters.routingStatus),
      },
    }),
  )
  const productionVersionsQuery = useQuery(() =>
    listBusinessConsoleEngineeringProductionVersionsQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        ...optionalQuery('skuCode', filters.skuCode),
        ...optionalQuery('status', filters.productionVersionStatus),
      },
    }),
  )
  const resolveQuery = useQuery(() =>
    resolveBusinessConsoleEngineeringProductionVersionQueryOptions({
      query: {
        organizationId: resolveFilters.organizationId,
        environmentId: resolveFilters.environmentId,
        skuCode: resolveFilters.skuCode,
        effectiveDate: resolveFilters.effectiveDate,
        lotSize: resolveFilters.lotSize,
      },
    }),
  )

  return {
    boms: computed<BusinessConsoleEngineeringBomItem[]>(() =>
      unwrapItems((bomsQuery.data.value as BusinessConsoleEngineeringBomListEnvelope | undefined)),
    ),
    bomsError: bomsQuery.error,
    bomsPending: bomsQuery.isLoading,
    filters,
    productionVersions: computed<BusinessConsoleProductionVersionItem[]>(() =>
      unwrapItems(
        productionVersionsQuery.data.value as BusinessConsoleProductionVersionListEnvelope | undefined,
      ),
    ),
    productionVersionsError: productionVersionsQuery.error,
    productionVersionsPending: productionVersionsQuery.isLoading,
    refreshEngineering: async () => {
      await Promise.all([
        bomsQuery.refetch(),
        routingsQuery.refetch(),
        productionVersionsQuery.refetch(),
        resolveQuery.refetch(),
      ])
    },
    resolvedProductionVersion: computed(() =>
      unwrapResolved(resolveQuery.data.value as BusinessConsoleResolveProductionVersionEnvelope | undefined),
    ),
    resolveError: resolveQuery.error,
    resolveFilters,
    resolvePending: resolveQuery.isLoading,
    routings: computed<BusinessConsoleRoutingItem[]>(() =>
      unwrapItems((routingsQuery.data.value as BusinessConsoleRoutingListEnvelope | undefined)),
    ),
    routingsError: routingsQuery.error,
    routingsPending: routingsQuery.isLoading,
  }
}
