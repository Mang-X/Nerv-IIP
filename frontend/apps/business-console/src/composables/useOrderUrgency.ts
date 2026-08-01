import type { BusinessConsoleOrderUrgency } from '@nerv-iip/api-client'
import { listBusinessConsoleOrderUrgencies } from '@nerv-iip/api-client'
import { useQuery } from '@pinia/colada'
import { computed, reactive, toValue, type MaybeRefOrGetter } from 'vue'
import { useBusinessContextStore } from '@/stores/businessContext'
import { bindBusinessContext, hasBusinessContext } from './businessContextBinding'

/**
 * 网关对 `orderReferences` 有 4000 字符校验上限（BusinessConsoleSchedulingEndpoints
 * 的 `MaximumLength(4000)`）。需求池等宿主一次传几百上千个单号时，整串 join 必超限 →
 * 整个请求 400 → 所有行都渲染成「未计算」（#1418 B4 的根因，不是紧急度事实没算）。
 * 所以这里按字符预算分片、并发请求、合并结果；预算留出余量，绝不许单片贴着上限。
 */
export const URGENCY_REFERENCE_PARAM_BUDGET = 3500

/** 按 join(',') 后的字符数切片；单个超长引用独占一片（后端自会拒绝，不连累其他片）。 */
export function chunkReferencesByParamBudget(
  references: readonly string[],
  budget: number = URGENCY_REFERENCE_PARAM_BUDGET,
): string[][] {
  const chunks: string[][] = []
  let current: string[] = []
  let currentLength = 0
  for (const reference of references) {
    let added = current.length === 0 ? reference.length : reference.length + 1
    if (current.length > 0 && currentLength + added > budget) {
      chunks.push(current)
      current = []
      currentLength = 0
      added = reference.length
    }
    current.push(reference)
    currentLength += added
  }
  if (current.length > 0) chunks.push(current)
  return chunks
}

const urgencyRank: Record<string, number> = {
  critical: 5,
  urgent: 4,
  highrisk: 3,
  attention: 2,
  normal: 1,
}

function isMoreUrgent(
  candidate: BusinessConsoleOrderUrgency,
  current: BusinessConsoleOrderUrgency,
) {
  const candidateRank = urgencyRank[candidate.level?.toLowerCase() ?? ''] ?? 0
  const currentRank = urgencyRank[current.level?.toLowerCase() ?? ''] ?? 0
  if (candidateRank !== currentRank) return candidateRank > currentRank
  return (candidate.orderId ?? '').localeCompare(current.orderId ?? '') < 0
}

export function indexOrderUrgenciesByReference(items: BusinessConsoleOrderUrgency[]) {
  const map = new Map<string, BusinessConsoleOrderUrgency>()
  for (const item of items) {
    if (item.orderId) map.set(item.orderId, item)
    if (!item.businessReference) continue
    const current = map.get(item.businessReference)
    if (!current || isMoreUrgent(item, current)) map.set(item.businessReference, item)
  }
  return map
}

export function useOrderUrgencies(
  references: MaybeRefOrGetter<readonly (string | null | undefined)[]>,
) {
  const context = useBusinessContextStore()
  const filters = bindBusinessContext(
    reactive({
      organizationId: context.organizationId,
      environmentId: context.environmentId,
    }),
  )
  const normalizedReferences = computed(() =>
    [
      ...new Set(
        toValue(references)
          .map((value) => value?.trim())
          .filter((value): value is string => Boolean(value)),
      ),
    ].sort(),
  )
  const query = useQuery(() => ({
    key: [
      'scheduling',
      'order-urgencies',
      filters.organizationId,
      filters.environmentId,
      normalizedReferences.value.join(','),
    ],
    query: async (): Promise<BusinessConsoleOrderUrgency[]> => {
      const chunks = chunkReferencesByParamBudget(normalizedReferences.value)
      const settled = await Promise.all(
        chunks.map(async (chunk) => {
          const { data, error } = await listBusinessConsoleOrderUrgencies({
            query: {
              organizationId: filters.organizationId,
              environmentId: filters.environmentId,
              orderReferences: chunk.join(','),
            },
            throwOnError: false,
          })
          // 任一分片失败都让整次查询失败：部分结果会把「取不到」伪装成「未计算」，
          // 宿主页面必须能区分「排程侧确实没算」与「请求失败」。
          if (error || data?.success === false) {
            throw error ?? new Error(data?.message ?? '订单紧急度读取失败')
          }
          return data?.data ?? []
        }),
      )
      return settled.flat()
    },
    enabled: hasBusinessContext(filters) && normalizedReferences.value.length > 0,
  }))
  const items = computed<BusinessConsoleOrderUrgency[]>(() => query.data.value ?? [])
  const byReference = computed(() => {
    return indexOrderUrgenciesByReference(items.value)
  })

  return {
    byReference,
    error: query.error,
    items,
    pending: query.isLoading,
    refresh: query.refetch,
  }
}
