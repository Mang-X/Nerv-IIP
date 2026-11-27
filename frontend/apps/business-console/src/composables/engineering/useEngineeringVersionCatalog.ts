import {
  listBusinessConsoleEngineeringManufacturingBomsQueryOptions,
  listBusinessConsoleEngineeringRoutingsQueryOptions,
} from '@nerv-iip/api-client'
import { onScopeDispose, ref, shallowRef, watch } from 'vue'
import { hasBusinessContext, type BusinessContextFields } from '../shared/businessContextBinding'
import {
  DEFAULT_TAKE,
  PUBLISHED,
  loadEngineeringCatalogPages,
  optionalQuery,
  runQueryOption,
  unwrapItems,
  unwrapTotal,
} from './engineeringShared'

type CatalogQuery = BusinessContextFields & {
  skuCode?: string
  skip: number
  take: number
}

export function useEngineeringVersionCatalog<T>(
  context: BusinessContextFields,
  queryOptions: (query: CatalogQuery) => {
    query: (context: never) => Promise<{
      success?: boolean
      data?: { items?: T[]; total?: number } | null
    }>
  },
  keyOf: (item: T) => string,
  skuScope?: () => string,
) {
  const items = shallowRef<T[]>([])
  const total = ref(0)
  const pending = ref(false)
  const error = shallowRef<unknown>()
  let generation = 0
  let controller: AbortController | undefined

  async function refresh() {
    const request = ++generation
    controller?.abort()
    items.value = []
    total.value = 0
    pending.value = false
    error.value = undefined

    const { organizationId, environmentId } = context
    const skuCode = skuScope?.()
    if (!hasBusinessContext(context) || skuCode === '') return

    controller = new AbortController()
    const { signal } = controller
    pending.value = true
    const isCurrent = () =>
      request === generation &&
      organizationId === context.organizationId &&
      environmentId === context.environmentId &&
      skuCode === skuScope?.()

    try {
      const result = await loadEngineeringCatalogPages(
        async (skip) => {
          const envelope = await runQueryOption(
            queryOptions({ organizationId, environmentId, skuCode, skip, take: DEFAULT_TAKE }),
            signal,
          )
          return { items: unwrapItems(envelope), total: unwrapTotal(envelope) }
        },
        keyOf,
        signal,
      )
      if (isCurrent()) {
        items.value = result.items
        total.value = result.total
      }
    } catch (reason) {
      if (isCurrent()) error.value = reason
    } finally {
      if (isCurrent()) pending.value = false
    }
  }

  watch([() => context.organizationId, () => context.environmentId, () => skuScope?.()], refresh, {
    immediate: true,
  })
  onScopeDispose(() => {
    generation += 1
    controller?.abort()
  })

  return { items, total, pending, error, refresh }
}

export function usePublishedMbomCatalog(context: BusinessContextFields, skuScope?: () => string) {
  return useEngineeringVersionCatalog(
    context,
    ({ skuCode, ...query }) =>
      listBusinessConsoleEngineeringManufacturingBomsQueryOptions({
        query: { ...query, ...optionalQuery('skuCode', skuCode), status: PUBLISHED },
      }),
    (row) => JSON.stringify([row.bomCode, row.revision]),
    skuScope,
  )
}

export function usePublishedRoutingCatalog(
  context: BusinessContextFields,
  skuScope?: () => string,
) {
  return useEngineeringVersionCatalog(
    context,
    ({ skuCode, ...query }) =>
      listBusinessConsoleEngineeringRoutingsQueryOptions({
        query: { ...query, ...optionalQuery('skuCode', skuCode), status: PUBLISHED },
      }),
    (row) => JSON.stringify([row.routingCode, row.revision]),
    skuScope,
  )
}
