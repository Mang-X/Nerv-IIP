import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import type {
  BusinessConsoleDemandSourceItem,
  BusinessConsoleMrpPeggingItem,
  BusinessConsoleMrpRunItem,
  BusinessConsolePlanningSuggestionItem,
} from '@nerv-iip/api-client'
import {
  getBusinessConsolePlanningMrpPegging,
  listBusinessConsolePlanningDemands,
  listBusinessConsolePlanningMrpRuns,
  listBusinessConsolePlanningSuggestions,
} from '@nerv-iip/api-client'
import { useQuery } from '@pinia/colada'
import { computed, type Ref } from 'vue'
import type { BusinessContextFields } from '../shared/businessContextBinding'
import {
  matchDemandPeggings,
  matchPlanningSuggestion,
  matchSuggestionWorkOrder,
  peggingSuggestionIds,
} from './fulfillmentMatchers'
import { readCandidatePages } from './fulfillmentCandidatePages'
import { runNodeSource } from './fulfillmentErrors'

/**
 * MRP pegging 只能按 runId 查，而运行历史没有「按需求源找运行」的读面。
 * 因此从最新一次运行开始向前扫这么多次运行，**每一次都看、不提前收工**——
 * 接受建议后再跑一次 MRP，工单仍挂在旧建议上，只看最新一次就会把工单节点误判成空态。
 * 窗口保持小，避免把整段运行历史都拉一遍；扫完仍无命中即如实空态（文案里说明只看了最近
 * 若干次运行，不下「尚未产生」这种越界结论），绝不按相似编号猜测。
 */
export const FULFILLMENT_MRP_RUN_SCAN_LIMIT = 5

export function useFulfillmentPlanningQueries(
  ctx: BusinessContextFields,
  salesOrderNo: Readonly<Ref<string | undefined>>,
  hasScope: Readonly<Ref<boolean>>,
  canRead: (permission: string) => boolean,
) {
  // —— 2. 生产需求（DemandSource.sourceReference === salesOrderNo）——
  const demandQuery = useQuery(() => ({
    key: ['fulfillment', 'demand', ctx.organizationId, ctx.environmentId, salesOrderNo.value ?? ''],
    query: () => {
      const scope = {
        organizationId: ctx.organizationId,
        environmentId: ctx.environmentId,
        keyword: salesOrderNo.value,
      }
      return readCandidatePages<BusinessConsoleDemandSourceItem>((skip, take) =>
        listBusinessConsolePlanningDemands({
          query: { ...scope, skip, take },
          throwOnError: false,
        }),
      )
    },
    enabled: hasScope.value && canRead(P.planningDemandsRead),
  }))

  // —— 3. MRP 建议（MrpPeggingItem.demandSourceReference === salesOrderNo）——
  // pegging 只能按 runId 查，先取运行列表（后端按创建时间倒序），再从最新一次向前扫。
  const mrpRunsQuery = useQuery(() => ({
    key: ['fulfillment', 'mrp-runs', ctx.organizationId, ctx.environmentId],
    query: () =>
      runNodeSource<{ items?: BusinessConsoleMrpRunItem[] } | null>(async () => {
        const { data, error, response } = await listBusinessConsolePlanningMrpRuns({
          query: { organizationId: ctx.organizationId, environmentId: ctx.environmentId },
          throwOnError: false,
        })
        return {
          data: data?.success ? (data.data ?? null) : null,
          error: data?.success === false ? data : error,
          response,
        }
      }),
    enabled: canRead(P.planningMrpRead) && hasScope.value,
  }))

  const scanRunIds = computed(() =>
    (mrpRunsQuery.data.value?.items ?? [])
      .map((run) => run.runId?.trim())
      .filter((runId): runId is string => Boolean(runId))
      .slice(0, FULFILLMENT_MRP_RUN_SCAN_LIMIT),
  )

  const peggingQuery = useQuery(() => ({
    key: [
      'fulfillment',
      'pegging',
      ctx.organizationId,
      ctx.environmentId,
      salesOrderNo.value ?? '',
      scanRunIds.value.join('|'),
    ],
    // 扫描窗内**每一次**运行都要看，命中不提前收工：接受建议后再跑 MRP，本单会多出一条
    // 尚未被接受的新建议，而工单挂在旧建议上——只看最新一次运行就会把工单节点误判成空态。
    // 结果按「新运行在前」排列，展示取最新、找工单则沿列表往回找。
    query: async () => {
      const scope = { organizationId: ctx.organizationId, environmentId: ctx.environmentId }
      const source = salesOrderNo.value
      const collected: BusinessConsoleMrpPeggingItem[] = []
      for (const runId of scanRunIds.value) {
        const data = await runNodeSource<{ items?: BusinessConsoleMrpPeggingItem[] } | null>(
          async () => {
            const { data, error, response } = await getBusinessConsolePlanningMrpPegging({
              path: { runId },
              query: scope,
              throwOnError: false,
            })
            return {
              data: data?.success ? (data.data ?? null) : null,
              error: data?.success === false ? data : error,
              response,
            }
          },
        )
        collected.push(...matchDemandPeggings(data?.items, source))
      }
      return collected
    },
    enabled: canRead(P.planningMrpRead) && hasScope.value && scanRunIds.value.length > 0,
  }))

  const matchedPeggings = computed(() => peggingQuery.data.value ?? [])
  const suggestionIds = computed(() => peggingSuggestionIds(matchedPeggings.value))
  // MRP 这一跳由「运行列表 + pegging 扫描」两个请求共同构成，任一失败都属该节点失败域。
  const mrpError = computed(() => mrpRunsQuery.error.value ?? peggingQuery.error.value)
  const mrpLoading = computed(() => mrpRunsQuery.isLoading.value || peggingQuery.isLoading.value)

  // —— 建议本体（拿人读的 SKU/数量与下游工单引用；suggestionId 是 GUID，不上屏）——
  //
  // 服务端过滤只到 `status`：建议读面的契约没有 suggestionId / keyword / runId 参数
  // （见 types.gen.ts 的 ListBusinessConsolePlanningSuggestionsData），
  // 想把「按 id 取一条」下推到服务端目前做不到。这里用 `status: 'Accepted'` 做真实的
  // 服务端收敛——只有被接受的建议才会回写下游工单引用，未接受的拉回来也没用；
  // 待读面支持按 id / 需求源过滤后应换成精确查询。
  const suggestionQuery = useQuery(() => ({
    key: [
      'fulfillment',
      'planning-suggestions',
      'accepted',
      ctx.organizationId,
      ctx.environmentId,
      suggestionIds.value.join('|'),
    ],
    query: () =>
      runNodeSource<{ items?: BusinessConsolePlanningSuggestionItem[] } | null>(async () => {
        const { data, error, response } = await listBusinessConsolePlanningSuggestions({
          query: {
            organizationId: ctx.organizationId,
            environmentId: ctx.environmentId,
            status: 'Accepted',
          },
          throwOnError: false,
        })
        return {
          data: data?.success ? (data.data ?? null) : null,
          error: data?.success === false ? data : error,
          response,
        }
      }),
    enabled: canRead(P.planningMrpRead) && hasScope.value && suggestionIds.value.length > 0,
  }))

  const matchedSuggestion = computed(() =>
    matchPlanningSuggestion(suggestionQuery.data.value?.items, suggestionIds.value),
  )
  const matchedWorkOrder = computed(() =>
    matchSuggestionWorkOrder(suggestionQuery.data.value?.items, suggestionIds.value),
  )
  return {
    demandQuery,
    mrpRunsQuery,
    scanRunIds,
    peggingQuery,
    matchedPeggings,
    suggestionIds,
    mrpError,
    mrpLoading,
    suggestionQuery,
    matchedSuggestion,
    matchedWorkOrder,
  }
}
