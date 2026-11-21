import { reactive } from 'vue'
import { bindBusinessContext } from '../shared/businessContextBinding'
import { LifecycleStateChangedError } from '../shared/lifecycleAction'

const DEFAULT_TAKE = 100

function requirePendingPayloadSnapshot<T extends object>(snapshot: unknown, operation: string): T {
  if (!snapshot || typeof snapshot !== 'object') {
    throw new Error(`${operation}缺少上次未完成提交的内容，请保留当前页面并人工核实。`)
  }
  return snapshot as T
}

type WmsListEnvelope<TItem> = {
  success?: boolean
  data?: { items?: TItem[]; total?: number } | null
}

export async function fetchAllWmsPages<TItem>(
  fetchPage: (skip: number) => Promise<WmsListEnvelope<TItem>>,
  label: string,
): Promise<WmsListEnvelope<TItem>> {
  const items: TItem[] = []
  let total = 0
  let skip = 0
  let firstPage: WmsListEnvelope<TItem> | undefined

  while (true) {
    const page = await fetchPage(skip)
    firstPage ??= page
    if (!page.success) throw new Error(`${label}读取失败，请刷新后重试。`)

    const pageItems = page.data?.items ?? []
    total = page.data?.total ?? 0
    items.push(...pageItems)
    if (items.length >= total) break
    if (pageItems.length === 0) throw new Error(`${label}读取不完整，请刷新后重试。`)
    skip += pageItems.length
  }

  return {
    ...firstPage,
    success: true,
    data: { ...firstPage?.data, items, total },
  }
}

export interface WmsListFilters {
  organizationId: string
  environmentId: string
  skip: number
  take: number
  status?: string
  keyword?: string
  scopeKind?: string
  scopeId?: string
  workScopeRequired?: boolean
}

export type WmsCompletionAttemptOptions = Readonly<{
  attempt: 'initial' | 'retry'
  onCommandAttempt?: () => void
}>

export interface WmsWarehouseTaskListFilters extends WmsListFilters {
  locationCode?: string
  lotNo?: string
}

export function defaultFilters<T extends WmsListFilters>(initial: Partial<T> = {}): T {
  return bindBusinessContext(
    reactive({
      organizationId: '',
      environmentId: '',
      skip: 0,
      take: DEFAULT_TAKE,
      ...initial,
    }) as T,
  )
}

export function optionalQuery<TKey extends string, TValue>(key: TKey, value: TValue | undefined) {
  return value === undefined || value === '' ? {} : { [key]: value }
}

export function baseQuery(filters: WmsListFilters) {
  return {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
    skip: filters.skip,
    take: filters.take,
    ...optionalQuery('status', filters.status),
    ...optionalQuery('keyword', filters.keyword),
    ...optionalQuery('scopeKind', filters.scopeKind),
    ...optionalQuery('scopeId', filters.scopeId),
  }
}

export function listItems<TItem>(
  envelope: { success?: boolean; data?: { items?: TItem[] } | null } | undefined,
) {
  if (!envelope?.success) {
    return []
  }

  return envelope.data?.items ?? []
}

export function listTotal(
  envelope: { success?: boolean; data?: { total?: number } | null } | undefined,
) {
  if (!envelope?.success) {
    return 0
  }

  return envelope.data?.total ?? 0
}

// 写操作需要幂等键以防重复提交；浏览器原生 UUID，测试环境（jsdom）亦可用。
export function createWmsIdempotencyKey(): string {
  const c = globalThis.crypto
  if (c && typeof c.randomUUID === 'function') return c.randomUUID()
  return `idem-${Date.now()}-${Math.round(Math.random() * 1e9)}`
}

function requireSuccessfulList<TItem>(
  result: Readonly<{
    data?: { success?: boolean; data?: { items?: TItem[] } | null }
    error?: unknown
  }>,
): TItem[] {
  if (result.error !== undefined) throw result.error
  if (!result.data?.success) throw result.data ?? new Error('读取最新状态失败')
  return result.data.data?.items ?? []
}

export function exactSuccessfulItem<TItem>(
  result: Readonly<{
    data?: { success?: boolean; data?: { items?: TItem[] } | null }
    error?: unknown
  }>,
  matches: (item: TItem) => boolean,
) {
  const matchingItems = requireSuccessfulList(result).filter(matches)
  return matchingItems.length === 1 ? matchingItems[0] : undefined
}

export function listScopeReady(filters: WmsListFilters) {
  const tenantReady =
    filters.organizationId.trim().length > 0 && filters.environmentId.trim().length > 0
  if (!tenantReady) return false
  if (!filters.workScopeRequired) return true
  return Boolean(filters.scopeKind?.trim() && filters.scopeId?.trim())
}

export function listScopeKey(filters: WmsListFilters) {
  return [
    filters.organizationId.trim(),
    filters.environmentId.trim(),
    filters.scopeKind?.trim() ?? '',
    filters.scopeId?.trim() ?? '',
  ].join(':')
}

export function withWmsListScopeEnabled<TOptions extends object>(
  options: TOptions,
  filters: WmsListFilters,
) {
  return {
    ...options,
    enabled: listScopeReady(filters),
  }
}

export function refetchWithWmsListScope<TResult>(
  filters: WmsListFilters,
  query: { refetch: () => Promise<TResult> },
): Promise<TResult | undefined> {
  return listScopeReady(filters) ? query.refetch() : Promise.resolve(undefined)
}

type FrozenWmsCompletionScope = {
  expectedVersion: number
  scopeKind: string
  scopeId: string
}

export function trustedWorkScope(
  filters: WmsListFilters,
  restoredSnapshot?: unknown,
): Pick<FrozenWmsCompletionScope, 'scopeKind' | 'scopeId'> {
  if (restoredSnapshot !== undefined) {
    if (!restoredSnapshot || typeof restoredSnapshot !== 'object') {
      throw new LifecycleStateChangedError('preflight')
    }
    const snapshot = restoredSnapshot as Partial<FrozenWmsCompletionScope>
    const scopeKind = snapshot.scopeKind?.trim()
    const scopeId = snapshot.scopeId?.trim()
    if (!scopeKind || !scopeId) throw new LifecycleStateChangedError('preflight')
    return { scopeKind, scopeId }
  }

  const scopeKind = filters.scopeKind?.trim()
  const scopeId = filters.scopeId?.trim()
  if (!scopeKind || !scopeId) throw new LifecycleStateChangedError('preflight')
  return { scopeKind, scopeId }
}

export function requireVersion(resource?: { version?: number }) {
  if (!Number.isInteger(resource?.version) || resource!.version! <= 0) {
    throw new LifecycleStateChangedError('preflight')
  }
  return resource!.version!
}

export function requireFrozenCompletion<TPayload extends object>(
  snapshot: unknown,
  operation: string,
): TPayload & FrozenWmsCompletionScope {
  const payload = requirePendingPayloadSnapshot<TPayload & Partial<FrozenWmsCompletionScope>>(
    snapshot,
    operation,
  )
  if (
    !Number.isInteger(payload.expectedVersion) ||
    payload.expectedVersion! <= 0 ||
    !payload.scopeKind?.trim() ||
    !payload.scopeId?.trim()
  ) {
    throw new Error(`${operation}缺少冻结的作业范围或版本，请保留当前页面并人工核实。`)
  }
  return payload as TPayload & FrozenWmsCompletionScope
}
