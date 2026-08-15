import { useBusinessContextStore } from '@/stores/businessContext'
import { watch } from 'vue'

export interface BusinessContextFields {
  organizationId: string
  environmentId: string
}

export function bindBusinessContext<T extends BusinessContextFields>(filters: T): T {
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

/**
 * 业务上下文（组织 + 环境）是否已经就绪。
 *
 * **空值安全是这个函数的本分，不是防御式编程**：它的语义就是「回答 filters 够不够查」，
 * 而残缺的 filters 恰恰是最该回答「不够」的那种。以前直接 `.trim()`，遇到缺字段的
 * filters 会抛 TypeError，页面于是各写各的内联判断绕开它——共用件配不上共用判定，
 * `awaitingScope` 的口径很快就会全站漂移。这里收口成「问不出来就是没就绪」。
 */
export function hasBusinessContext(filters: BusinessContextFields | null | undefined) {
  return (
    (filters?.organizationId?.trim().length ?? 0) > 0 &&
    (filters?.environmentId?.trim().length ?? 0) > 0
  )
}

export function withBusinessContextEnabled<TOptions extends object>(
  options: TOptions,
  filters: BusinessContextFields,
) {
  return {
    ...options,
    enabled: hasBusinessContext(filters),
  }
}

export function refetchWithBusinessContext<TResult>(
  filters: BusinessContextFields,
  query: { refetch: () => Promise<TResult> },
): Promise<TResult | undefined> {
  return hasBusinessContext(filters) ? query.refetch() : Promise.resolve(undefined)
}
