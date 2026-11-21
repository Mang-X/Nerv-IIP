import { useBusinessContextStore } from '@/stores/businessContext'
import { useQuery } from '@pinia/colada'
import { computed, reactive } from 'vue'
import { hasBusinessContext, refetchWithBusinessContext } from '../businessContextBinding'

export const DEFAULT_TAKE = 10

export interface BusinessErpListFilters {
  status?: string
  purchaseOrderStatus?: string
  purchaseRequisitionStatus?: string
  keyword?: string
  skip: number
  take: number
}

export function defaultFilters(
  initial: Partial<BusinessErpListFilters> = {},
): BusinessErpListFilters {
  return reactive({
    skip: 0,
    take: DEFAULT_TAKE,
    ...initial,
  })
}

interface ErpListQuery {
  organizationId: string
  environmentId: string
  status?: string
  keyword?: string
  skip: number
  take: number
}

export interface ConvertPurchaseRequisitionOptions {
  rfqSupplierCodes?: string[]
}

export function unwrapItems<T>(
  envelope: { success?: boolean; data?: { items?: T[] } | null } | undefined,
): T[] {
  return envelope?.success ? (envelope.data?.items ?? []) : []
}

export function unwrapTotal(
  envelope: { success?: boolean; data?: { total?: number } | null } | undefined,
): number {
  return envelope?.success ? (envelope.data?.total ?? 0) : 0
}

export function unwrapData<T>(
  envelope: { success?: boolean; data?: T | null } | undefined,
): T | undefined {
  return envelope?.success ? (envelope.data ?? undefined) : undefined
}

// 写操作幂等键，避免重复提交；浏览器原生 UUID，测试环境亦可用。
export function makeIdempotencyKey(): string {
  const c = globalThis.crypto
  if (c && typeof c.randomUUID === 'function') return c.randomUUID()
  return `idem-${Date.now()}-${Math.round(Math.random() * 1e9)}`
}

// 通用「单据列表」读面工厂：org/env + 服务端分页 skip/take + 状态/关键字过滤，无假分页。
export function useErpDocumentList<
  TItem,
  TEnvelope extends { success?: boolean; data?: { items?: TItem[]; total?: number } | null },
>(
  buildOptions: (query: ErpListQuery) => unknown,
  initialFilters: Partial<BusinessErpListFilters> = {},
) {
  const businessContext = useBusinessContextStore()
  const filters = defaultFilters(initialFilters)
  const query = useQuery(
    () =>
      ({
        // 各单据 query options 仅 data 泛型不同，统一经工厂收敛，故此处收窄类型。
        ...(buildOptions({
          organizationId: businessContext.organizationId,
          environmentId: businessContext.environmentId,
          status: filters.status,
          keyword: filters.keyword,
          skip: filters.skip,
          take: filters.take,
        }) as object),
        enabled: hasBusinessContext(businessContext),
      }) as never,
  )

  return {
    filters,
    organizationId: computed(() => businessContext.organizationId),
    environmentId: computed(() => businessContext.environmentId),
    /**
     * 业务上下文是否就绪 = 查询是否真的发出去了。
     * 未就绪时 `enabled:false`，pinia-colada 的 `asyncStatus` 停在 `idle`，`isLoading`
     * 为 **false**——页面只看 pending/error 会把「压根没查」当成「查过了、是 0」。
     */
    ready: computed(() => hasBusinessContext(businessContext)),
    items: computed<TItem[]>(() => unwrapItems(query.data.value as TEnvelope | undefined)),
    total: computed(() => unwrapTotal(query.data.value as TEnvelope | undefined)),
    error: query.error,
    pending: query.isLoading,
    refresh: () => refetchWithBusinessContext(businessContext, query),
  }
}
