import {
  type BusinessConsoleCompleteWmsCountExecutionRequest,
  type BusinessConsoleCompleteWmsOutboundOrderRequest,
  type BusinessConsoleWmsInboundLineCaptureInput,
} from '@nerv-iip/api-client'
import { type EntryKey } from '@pinia/colada'
import { computed, reactive, shallowRef, toValue, watch, type MaybeRefOrGetter } from 'vue'
import { LifecycleActionUnavailableError } from '@/composables/shared/lifecycleActionRecovery'
import { useAuthStore } from '@/stores/auth'
import { useWmsWorkScope, type WmsWorkScopeCatalogKind } from '@/composables/wms/useWmsWorkScope'

const DEFAULT_TAKE = 100

export const TASK_PAGE_SIZE = 20

export interface WmsScopeFilters {
  skip: number
  take: number
  status?: string
  keyword?: string
}

export interface WmsTaskFilters extends WmsScopeFilters {
  locationCode?: string
  lotNo?: string
}

export interface WmsInboundFilters extends WmsScopeFilters {
  locationCode?: string
  lotNo?: string
}

// outbound/count 写入参数：调用方传业务字段 + idempotencyKey（页面在用户发起操作时生成一次，
// 重试复用同一键以防丢响应导致重复入库；新操作才换新键）。org/env 不在 body，由本封装从主体注入。
export type CompleteOutboundInput = Omit<
  BusinessConsoleCompleteWmsOutboundOrderRequest,
  'expectedVersion' | 'scopeKind' | 'scopeId'
>

export type CompleteCountInput = Omit<
  BusinessConsoleCompleteWmsCountExecutionRequest,
  'expectedVersion' | 'scopeKind' | 'scopeId'
>

export type WmsCompletionAttemptOptions = Readonly<{
  attempt: 'initial' | 'retry'
  onCommandAttempt?: () => void
}>

// 收货现场按行采集的批号/效期（#935 闭环）：随 completeInbound 落库。
export type InboundLineCapture = BusinessConsoleWmsInboundLineCaptureInput

export function defaultFilters<T extends WmsScopeFilters>(initial: Partial<T> = {}): T {
  return reactive({ skip: 0, take: DEFAULT_TAKE, ...initial }) as T
}

export function optionalQuery<TKey extends string, TValue>(key: TKey, value: TValue | undefined) {
  return value === undefined || value === '' ? {} : { [key]: value }
}

export function listItems<TItem>(
  envelope: { success?: boolean; data?: { items?: TItem[] } | null } | undefined,
) {
  return envelope?.success ? (envelope.data?.items ?? []) : []
}

export function listTotal(
  envelope: { success?: boolean; data?: { total?: number } | null } | undefined,
) {
  return envelope?.success ? (envelope.data?.total ?? 0) : 0
}

type ListEnvelope<TItem> =
  | { success?: boolean; data?: { items?: TItem[]; total?: number } | null }
  | undefined

export function usePagedAccumulator<TSource, TItem>(
  envelope: MaybeRefOrGetter<ListEnvelope<TSource>>,
  enabled: MaybeRefOrGetter<boolean>,
  project: (item: TSource) => TItem | undefined,
  itemKey: (item: TItem) => string,
) {
  const items = shallowRef<TItem[]>([])
  const total = shallowRef(0)
  const nextSkip = shallowRef(0)
  const exhausted = shallowRef(true)

  function merge(projected: TItem[], replace: boolean) {
    const byKey = new Map<string, TItem>()
    if (!replace) {
      for (const item of items.value) byKey.set(itemKey(item), item)
    }
    for (const item of projected) byKey.set(itemKey(item), item)
    items.value = [...byKey.values()]
  }

  function reset() {
    items.value = []
    total.value = 0
    nextSkip.value = 0
    exhausted.value = true
  }

  function acceptPage(value: ListEnvelope<TSource>, skip: number, take: number) {
    if (value?.success !== true) {
      throw new Error('WMS 列表响应无效，请刷新后重试')
    }
    const rawItems = listItems<TSource>(value)
    merge(
      rawItems.map(project).filter((item): item is TItem => item !== undefined),
      skip === 0,
    )
    total.value = listTotal(value)
    nextSkip.value = skip + rawItems.length
    exhausted.value =
      rawItems.length === 0 || rawItems.length < take || nextSkip.value >= total.value
  }

  watch(
    [() => toValue(envelope), () => toValue(enabled)],
    ([value, ready]) => {
      if (!ready || value === undefined) {
        reset()
        return
      }
      if (value.success !== true) return
      acceptPage(value, 0, TASK_PAGE_SIZE)
    },
    { immediate: true, flush: 'sync' },
  )

  return { items, total, nextSkip, exhausted, reset, acceptPage }
}

export function stableItemKey(item: object, ...candidates: Array<string | null | undefined>) {
  return candidates.find((candidate) => candidate?.trim()) ?? JSON.stringify(item)
}

export function isCurrentPagingRequest(
  requestScopeKey: string,
  requestGeneration: number,
  currentScopeKey: string,
  currentGeneration: number,
) {
  return requestScopeKey === currentScopeKey && requestGeneration === currentGeneration
}

export function exactItem<TItem>(
  envelope: { success?: boolean; data?: { items?: TItem[] } | null } | undefined,
  matches: (item: TItem) => boolean,
) {
  const matchesById = listItems(envelope).filter(matches)
  return matchesById.length === 1 ? matchesById[0] : undefined
}

export function lifecycleUnavailable(resource?: { status?: string }) {
  const status = resource?.status?.trim().toLowerCase()
  const terminal = new Set(['completed', 'completedwithdifference', 'cancelled']).has(status ?? '')
  return new LifecycleActionUnavailableError({
    known: resource !== undefined,
    terminal,
    executable: false,
    legalNoop: false,
    reason:
      resource === undefined
        ? 'unknown-status'
        : terminal
          ? 'terminal-status'
          : 'incompatible-state',
  })
}

export function requireVersion(resource: { status?: string; version?: number } | undefined) {
  const version = resource?.version
  if (!Number.isInteger(version)) throw lifecycleUnavailable(resource)
  return version!
}

export function requireFrozenScope(
  snapshot: unknown,
): snapshot is { expectedVersion: number; scopeKind: string; scopeId: string } {
  if (!snapshot || typeof snapshot !== 'object') return false
  const value = snapshot as Record<string, unknown>
  return (
    Number.isInteger(value.expectedVersion) &&
    typeof value.scopeKind === 'string' &&
    value.scopeKind.trim().length > 0 &&
    typeof value.scopeId === 'string' &&
    value.scopeId.trim().length > 0
  )
}

// 明细接口仅需要租户边界；作业列表另由 WMS 可信目录绑定 self/work-pool/site。
export function useWmsTenantScope() {
  const auth = useAuthStore()
  const organizationId = computed(() => auth.principal?.organizationId ?? '')
  const environmentId = computed(() => auth.principal?.environmentId ?? '')
  const hasScope = computed(() => Boolean(organizationId.value && environmentId.value))
  const principalId = computed(
    () => auth.principal?.principalId ?? auth.sessionId ?? 'unrestored-session',
  )
  const scopeKey = computed(() => `${organizationId.value.trim()}:${environmentId.value.trim()}`)
  const scopeQuery = () => ({
    organizationId: organizationId.value,
    environmentId: environmentId.value,
  })
  const scopeQueryWithPaging = (filters: WmsScopeFilters) => ({
    ...scopeQuery(),
    skip: filters.skip,
    take: filters.take,
    ...optionalQuery('status', filters.status),
    ...optionalQuery('keyword', filters.keyword),
  })
  return {
    organizationId,
    environmentId,
    principalId,
    hasScope,
    scopeKey,
    scopeQuery,
    scopeQueryWithPaging,
  }
}

export function useAuthorizedWmsScope(catalog: WmsWorkScopeCatalogKind) {
  const scope = useWmsWorkScope(catalog)
  const responseScopeKey = computed(() =>
    JSON.stringify([scope.sessionKey.value, scope.scopeKey.value]),
  )
  const bindQuery = <T extends { key: EntryKey }>(options: T) => ({
    ...options,
    key: [...options.key, scope.sessionKey.value],
    enabled: scope.hasSelection.value,
  })
  const tenantQuery = () => ({
    organizationId: scope.organizationId.value,
    environmentId: scope.environmentId.value,
  })
  const scopeQuery = () => ({
    ...tenantQuery(),
    scopeKind: scope.scopeKind.value!,
    scopeId: scope.scopeId.value!,
  })
  const scopeQueryWithPaging = (filters: WmsScopeFilters) => ({
    ...scopeQuery(),
    skip: filters.skip,
    take: filters.take,
    ...optionalQuery('status', filters.status),
    ...optionalQuery('keyword', filters.keyword),
  })

  return {
    ...scope,
    hasScope: scope.hasSelection,
    selectedScopeKey: scope.scopeKey,
    responseScopeKey,
    bindQuery,
    tenantQuery,
    scopeQuery,
    scopeQueryWithPaging,
  }
}
