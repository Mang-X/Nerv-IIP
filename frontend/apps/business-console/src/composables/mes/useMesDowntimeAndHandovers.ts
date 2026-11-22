import {
  acceptBusinessConsoleMesShiftHandoverMutationOptions,
  confirmBusinessConsoleMesDowntimeRecoveryMutationOptions,
  createBusinessConsoleMesShiftHandoverMutationOptions,
  listBusinessConsoleMesDowntimeEventsQueryOptions,
  listBusinessConsoleSearchableDirectoryQueryOptions,
  listBusinessConsoleMesShiftHandoversQueryOptions,
  getBusinessConsoleMesShiftHandoverQueryOptions,
  recordBusinessConsoleMesDowntimeEventV2MutationOptions,
  type BusinessConsoleMesDowntimeEventListEnvelope,
  type BusinessConsoleMesDowntimeEventRow,
  type BusinessConsoleMesDowntimeReasonSummaryRow,
  type BusinessConsoleMesRecordDowntimeEventV2Request,
  type BusinessConsoleMesCreateShiftHandoverRequest,
  type BusinessConsoleMesShiftHandoverDetail,
  type BusinessConsoleMesShiftHandoverDetailEnvelope,
  type BusinessConsoleMesShiftHandoverListEnvelope,
  type BusinessConsoleMesShiftHandoverRow,
} from '@nerv-iip/api-client'
import { useMutation, useQuery, useQueryCache } from '@pinia/colada'
import { computed, shallowRef } from 'vue'
import {
  hasBusinessContext,
  refetchWithBusinessContext,
  withBusinessContextEnabled,
} from '../shared/businessContextBinding'
import { businessReadState } from '../shared/businessReadState'
import {
  defaultFilters,
  envelopeItems,
  envelopeTotal,
  ignoreBackgroundError,
  invalidateMesQueries,
  optionalQuery,
  toListQuery,
  type MesListFilters,
  unwrapData,
} from './mesShared'
import { useMesPrincipalWorkScope } from './useMesWorkScope'

export function useMesDowntimeEvents() {
  const filters = defaultFilters()
  let usesCurrentWindow = true
  function updateCurrentWindow() {
    const windowEnd = new Date()
    filters.windowStartUtc = new Date(windowEnd.getTime() - 30 * 24 * 60 * 60 * 1000).toISOString()
    filters.windowEndUtc = windowEnd.toISOString()
  }
  updateCurrentWindow()
  const queryCache = useQueryCache()
  const downtimeWriteScope = useMesPrincipalWorkScope(filters, 'business.mes.downtime.manage')
  const downtimeQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsoleMesDowntimeEventsQueryOptions({
        query: {
          ...toListQuery(filters),
          ...optionalQuery('reasonCode', filters.reasonCode),
          ...optionalQuery('windowStartUtc', filters.windowStartUtc),
          ...optionalQuery('windowEndUtc', filters.windowEndUtc),
        },
      }),
      filters,
    ),
  )
  const downtimeReasonsQuery = useQuery(() => ({
    ...listBusinessConsoleSearchableDirectoryQueryOptions({
      path: { directoryType: 'downtime-reason' },
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        pageIndex: 1,
        pageSize: 100,
        rankingMode: 'default',
      },
    }),
    // 只以业务上下文为前置，**不加权限前置**（#2793 裁定）：
    // ① 权威是网关，前端 principal 的权限码只是提示；一旦它滞后于真实授权，加了前置就再也
    //    不发请求，页面永远显示无权限且没有任何证据能纠正——403 至少是权威事实。
    // ② 这份目录同时供只读筛选下拉使用（见上方 reasonFilterOptions 注释与
    //    “reads the downtime-reason directory without waiting for a write scope” 用例），
    //    按写面权限或角色抑制请求会连带削掉读面能力。
    // ③ 少发的只是一次请求；真正的缺陷是归因，由 downtime.vue 的 recordEntryBlocker 分流
    //    403 与其它失败来解决。反之若只加前置不加分支，会直接掉进「组织尚未配置」那句更糟的误诊。
    enabled: hasBusinessContext(filters),
  }))
  const recordMutation = useMutation({
    ...recordBusinessConsoleMesDowntimeEventV2MutationOptions(),
    onSuccess: () =>
      void invalidateMesQueries(queryCache, [
        'listBusinessConsoleMesDowntimeEvents',
        'listBusinessConsoleMesCapacityImpacts',
      ]).catch(ignoreBackgroundError),
  })
  const recoverMutation = useMutation({
    ...confirmBusinessConsoleMesDowntimeRecoveryMutationOptions(),
    onSuccess: () =>
      void invalidateMesQueries(queryCache, [
        'listBusinessConsoleMesDowntimeEvents',
        'listBusinessConsoleMesCapacityImpacts',
      ]).catch(ignoreBackgroundError),
  })

  return {
    downtimeEvents: computed<BusinessConsoleMesDowntimeEventRow[]>(() =>
      envelopeItems<
        BusinessConsoleMesDowntimeEventRow,
        BusinessConsoleMesDowntimeEventListEnvelope
      >(downtimeQuery.data.value),
    ),
    downtimeEventsError: downtimeQuery.error,
    downtimeEventsPending: downtimeQuery.isLoading,
    downtimeEventsState: businessReadState(downtimeQuery, () => hasBusinessContext(filters)),
    downtimeEventsTotal: computed(() => envelopeTotal(downtimeQuery.data.value)),
    // 汇总由读面随列表一起返回，且不受 reasonCode 过滤影响，所以选中某个原因后仍能看到全部原因。
    downtimeReasonSummary: computed<BusinessConsoleMesDowntimeReasonSummaryRow[]>(() => {
      const envelope = downtimeQuery.data.value
      if (envelope?.success !== true) return []
      return envelope.data?.reasonSummary ?? []
    }),
    // `label` 是登记弹窗既有的「名称（码）」口径；`name` 是同一条目的纯名称，供只读面用
    // （读面不把工程码打在界面上，见 business-console AGENTS「UI 不暴露工程语言」）。
    // 没有名称时两者都回落成码——那是「字典没给名字」的实情，不编名字。
    downtimeReasonOptions: computed(() => {
      const envelope = downtimeReasonsQuery.data.value
      if (envelope?.success !== true || envelope.data?.status === 'unavailable') return []
      return (envelope.data?.items ?? [])
        .map((item) => {
          const value = item.code?.trim()
          if (!value) return undefined
          const name = item.displayName?.trim()
          return { value, label: name ? `${name}（${value}）` : value, name: name || value }
        })
        .filter(
          (item): item is { value: string; label: string; name: string } => item !== undefined,
        )
    }),
    downtimeReasonsError: downtimeReasonsQuery.error,
    downtimeReasonsPending: downtimeReasonsQuery.isLoading,
    downtimeWriteCoversWorkOrder: downtimeWriteScope.coversWorkOrder,
    downtimeWriteScope: downtimeWriteScope.selectedScope,
    downtimeWriteScopeMessage: downtimeWriteScope.scopeMessage,
    downtimeWriteScopePending: downtimeWriteScope.scopePending,
    downtimeWriteScopeReady: downtimeWriteScope.scopeReady,
    filters,
    recordDowntimeEvent: (
      body: Omit<
        BusinessConsoleMesRecordDowntimeEventV2Request,
        'organizationId' | 'environmentId'
      >,
    ) => {
      const organizationId = filters.organizationId.trim()
      const environmentId = filters.environmentId.trim()
      const selectedScope = downtimeWriteScope.requireSelectedScope()
      if (!organizationId || !environmentId) {
        throw new Error('尚未确定当前组织，不能登记停机。')
      }
      if (body.scopeKind !== selectedScope.kind || body.scopeId !== selectedScope.id) {
        throw new Error('停机登记范围已变化，请重新选择工单与工序。')
      }
      return recordMutation.mutateAsync({
        body: {
          ...body,
          organizationId,
          environmentId,
          scopeKind: selectedScope.kind,
          scopeId: selectedScope.id,
        },
      })
    },
    recordDowntimeEventPending: recordMutation.isLoading,
    recoverDowntimeEvent: (
      downtimeEventId: string,
      body: {
        organizationId: string
        environmentId: string
        recoveredAtUtc: string
      },
    ) =>
      recoverMutation.mutateAsync({
        path: { downtimeEventId },
        query: { organizationId: body.organizationId, environmentId: body.environmentId },
        body,
      }),
    recoverDowntimeEventPending: recoverMutation.isLoading,
    refreshDowntimeWriteScope: downtimeWriteScope.refreshScope,
    setDowntimeWindow: (window: Pick<MesListFilters, 'windowStartUtc' | 'windowEndUtc'>) => {
      usesCurrentWindow = false
      Object.assign(filters, window)
    },
    refreshDowntimeEvents: () => {
      if (usesCurrentWindow) updateCurrentWindow()
      return refetchWithBusinessContext(filters, downtimeQuery)
    },
  }
}

export function useMesShiftHandovers() {
  const filters = defaultFilters()
  const queryCache = useQueryCache()
  const handoversQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsoleMesShiftHandoversQueryOptions({
        query: toListQuery(filters),
      }),
      filters,
    ),
  )
  const createMutation = useMutation({
    ...createBusinessConsoleMesShiftHandoverMutationOptions(),
    onSuccess: () =>
      void invalidateMesQueries(queryCache, ['listBusinessConsoleMesShiftHandovers']).catch(
        ignoreBackgroundError,
      ),
  })
  const acceptMutation = useMutation({
    ...acceptBusinessConsoleMesShiftHandoverMutationOptions(),
    onSuccess: () =>
      void invalidateMesQueries(queryCache, ['listBusinessConsoleMesShiftHandovers']).catch(
        ignoreBackgroundError,
      ),
  })

  // 列表行只带三类明细的计数，明细本身要按交接单单独取。选中哪一张由调用页写这个 ref，
  // 清空即停止取数（详情抽屉关掉后不再持有请求）。
  const detailHandoverId = shallowRef('')
  const detailQuery = useQuery(() => ({
    ...getBusinessConsoleMesShiftHandoverQueryOptions({
      path: { handoverId: detailHandoverId.value },
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
      },
    }),
    enabled: hasBusinessContext(filters) && detailHandoverId.value.trim().length > 0,
  }))

  return {
    detailHandoverId,
    handoverDetail: computed<BusinessConsoleMesShiftHandoverDetail | undefined>(() =>
      unwrapData<
        BusinessConsoleMesShiftHandoverDetail,
        BusinessConsoleMesShiftHandoverDetailEnvelope
      >(detailQuery.data.value),
    ),
    handoverDetailError: detailQuery.error,
    handoverDetailPending: detailQuery.isLoading,
    // #3328：接班的网关请求体已经空了（原来只有一个下游从不消费的 idempotencyKey），
    // 组织/环境仍走 query，所以这里不再传 body。
    acceptShiftHandover: (
      handoverId: string,
      context: { organizationId: string; environmentId: string },
    ) =>
      acceptMutation.mutateAsync({
        path: { handoverId },
        query: { organizationId: context.organizationId, environmentId: context.environmentId },
      }),
    createShiftHandover: (body: BusinessConsoleMesCreateShiftHandoverRequest) =>
      createMutation.mutateAsync({ body }),
    filters,
    handovers: computed<BusinessConsoleMesShiftHandoverRow[]>(() =>
      envelopeItems<
        BusinessConsoleMesShiftHandoverRow,
        BusinessConsoleMesShiftHandoverListEnvelope
      >(handoversQuery.data.value),
    ),
    handoversError: handoversQuery.error,
    handoversPending: handoversQuery.isLoading,
    handoversState: businessReadState(handoversQuery, () => hasBusinessContext(filters)),
    handoversTotal: computed(() => envelopeTotal(handoversQuery.data.value)),
    refreshHandovers: () => refetchWithBusinessContext(filters, handoversQuery),
  }
}
