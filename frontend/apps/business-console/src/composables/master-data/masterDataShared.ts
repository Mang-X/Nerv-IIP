import { type BusinessConsoleResourceListEnvelope } from '@nerv-iip/api-client'
import { useQueryCache, type UseQueryEntry } from '@pinia/colada'
import { reactive } from 'vue'
import { bindBusinessContext, type BusinessContextFields } from '../shared/businessContextBinding'

export const DEFAULT_TAKE = 100

export interface BusinessContextFilters extends BusinessContextFields {}

export interface MasterDataListFilters extends BusinessContextFilters {
  keyword?: string
  includeDisabled?: boolean
  skip: number
  take: number
}

export function defaultContext(): BusinessContextFilters {
  return bindBusinessContext(
    reactive({
      organizationId: '',
      environmentId: '',
    }),
  )
}

export function defaultListFilters(): MasterDataListFilters {
  return bindBusinessContext(
    reactive({
      organizationId: '',
      environmentId: '',
      skip: 0,
      take: DEFAULT_TAKE,
    }),
  )
}

export function optionalQuery<TKey extends string, TValue>(key: TKey, value: TValue | undefined) {
  return value === undefined ? {} : { [key]: value }
}

export function resourceItems(envelope: BusinessConsoleResourceListEnvelope | undefined) {
  if (!envelope?.success) {
    return []
  }

  return envelope.data?.resources ?? []
}

export function resourceTotal(envelope: BusinessConsoleResourceListEnvelope | undefined) {
  if (!envelope?.success) {
    return 0
  }

  return envelope.data?.total ?? 0
}

export function isBusinessQuery(id: string) {
  return (entry: UseQueryEntry) => {
    const keyParts = Array.isArray(entry.key) ? entry.key : [entry.key]

    return keyParts.some((part) => {
      return typeof part === 'object' && part !== null && '_id' in part && part._id === id
    })
  }
}

export function ignoreBackgroundError(_error: unknown) {}

/**
 * 新建之后要刷新的查询：该类自己的列表 + 网关可搜目录。选择器（`DirectoryPicker`）取的是目录，
 * 不刷新的话刚建好的项搜不到，选中后也只能显示编码。
 */
export function invalidateAfterCreate(
  queryCache: ReturnType<typeof useQueryCache>,
  listIds: string[],
) {
  for (const id of [...listIds, 'listBusinessConsoleSearchableDirectory']) {
    void queryCache
      .invalidateQueries({ predicate: isBusinessQuery(id) })
      .catch(ignoreBackgroundError)
  }
}

export function newCreateIdempotencyKey(resourceType: string) {
  const cryptoApi = globalThis.crypto
  if (cryptoApi && typeof cryptoApi.randomUUID === 'function') {
    return `${resourceType}-${cryptoApi.randomUUID()}`
  }

  return `${resourceType}-${Date.now()}-${Math.random().toString(16).slice(2)}`
}

export function withCreateIdempotency<TBody>(resourceType: string, body: TBody): TBody {
  if (typeof body !== 'object' || body === null) {
    return body
  }

  const current = 'idempotencyKey' in body ? body.idempotencyKey : undefined
  if (typeof current === 'string' && current.trim().length > 0) {
    return body
  }

  return {
    ...body,
    idempotencyKey: newCreateIdempotencyKey(resourceType),
  }
}

export async function fetchCompleteCatalog<T>(
  fetchPage: (
    skip: number,
  ) => Promise<{ success?: boolean; data?: { items?: T[]; total?: number } | null }>,
) {
  const items: T[] = []
  while (true) {
    const page = await fetchPage(items.length)
    if (!page.success || !page.data) throw new Error('基础数据目录读取失败，请刷新后重试。')
    const batch = page.data.items ?? []
    items.push(...batch)
    const total = page.data.total ?? items.length
    if (items.length >= total) return { success: true, data: { items, total } }
    if (batch.length === 0) throw new Error('基础数据目录已变化，请刷新后重试。')
  }
}
