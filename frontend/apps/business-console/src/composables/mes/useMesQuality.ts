import {
  forceReleaseBusinessConsoleMesQualityHoldMutationOptions,
  getBusinessConsoleMesQualityHoldTimelineQueryOptions,
  listBusinessConsoleMesRelatedQualityItemsQueryOptions,
  recordBusinessConsoleMesDefectV2MutationOptions,
  type BusinessConsoleMesRecordDefectV2Request,
  type BusinessConsoleMesRelatedQualityItemListEnvelope,
  type BusinessConsoleMesRelatedQualityItemRow,
  type BusinessConsoleMesQualityHoldTimelineItem,
} from '@nerv-iip/api-client'
import { useMutation, useQuery, useQueryCache } from '@pinia/colada'
import { computed } from 'vue'
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
  isNonEmpty,
  makeIdempotencyKey,
  toListQuery,
} from './mesShared'

export interface MesQualityHoldSource {
  organizationId: string
  environmentId: string
  sourceService: string
  sourceDocumentId: string
}

// 单个质量保留(quality hold)的时间线读面(#886)+人工强制释放(既有 force-release 写面)。
// 由工单详情 hold 区块按活跃保留逐个实例化;定位键为 sourceService + sourceDocumentId。
// isReadable：时间线读端点要求 business.mes.quality.read（网关 MesQualityRead），高于本页 work-orders.read。
// 无该权限的用户不发时间线请求（否则每个保留逐一 403），由调用方按权限传入。
export function useMesQualityHold(
  source: () => MesQualityHoldSource,
  isReadable: () => boolean = () => true,
) {
  const queryCache = useQueryCache()
  const enabled = computed(() => {
    const s = source()
    return (
      isReadable() &&
      isNonEmpty(s.organizationId) &&
      isNonEmpty(s.environmentId) &&
      isNonEmpty(s.sourceService) &&
      isNonEmpty(s.sourceDocumentId)
    )
  })

  const timelineQuery = useQuery(() => {
    const s = source()
    return {
      ...getBusinessConsoleMesQualityHoldTimelineQueryOptions({
        path: { sourceDocumentId: s.sourceDocumentId },
        query: {
          organizationId: s.organizationId,
          environmentId: s.environmentId,
          sourceService: s.sourceService,
        },
      }),
      enabled: enabled.value,
    }
  })

  const forceReleaseMutation = useMutation({
    ...forceReleaseBusinessConsoleMesQualityHoldMutationOptions(),
    onSuccess() {
      void invalidateMesQueries(queryCache, [
        // 本读面：释放后时间线追加一条 manual-force-released 事件
        'getBusinessConsoleMesQualityHoldTimeline',
        // 保留解除改动工单详情活跃保留、列表锁定标记与齐套/开工阻塞
        'getBusinessConsoleMesWorkOrderDetail',
        'listBusinessConsoleMesWorkOrders',
        'getBusinessConsoleMesMaterialReadiness',
        'getBusinessConsoleMesProductionPlanReadiness',
      ]).catch(ignoreBackgroundError)
    },
  })

  return {
    timeline: computed<BusinessConsoleMesQualityHoldTimelineItem[]>(() => {
      const data = timelineQuery.data.value
      return data?.success ? (data.data?.items ?? []) : []
    }),
    timelinePending: timelineQuery.isLoading,
    timelineError: timelineQuery.error,
    timelineState: businessReadState(timelineQuery, () => enabled.value),
    refreshTimeline: () => (enabled.value ? timelineQuery.refetch() : Promise.resolve()),
    forceRelease: (reason: string) => {
      const s = source()
      return forceReleaseMutation.mutateAsync({
        path: { sourceDocumentId: s.sourceDocumentId },
        query: { organizationId: s.organizationId, environmentId: s.environmentId },
        body: {
          reason,
          sourceService: s.sourceService,
          idempotencyKey: makeIdempotencyKey('quality-hold-release'),
        },
      })
    },
    forceReleasePending: forceReleaseMutation.isLoading,
    forceReleaseError: forceReleaseMutation.error,
  }
}

export function useMesQualityContext() {
  const filters = defaultFilters()
  const queryCache = useQueryCache()
  const qualityQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsoleMesRelatedQualityItemsQueryOptions({
        query: toListQuery(filters),
      }),
      filters,
    ),
  )
  const defectMutation = useMutation({
    ...recordBusinessConsoleMesDefectV2MutationOptions(),
    onSuccess: () =>
      void invalidateMesQueries(queryCache, [
        'listBusinessConsoleMesRelatedQualityItems',
        'listBusinessConsoleMesOperationTasks',
        'listBusinessConsoleMesWorkOrders',
        'getBusinessConsoleMesWorkOrderDetail',
      ]).catch(ignoreBackgroundError),
  })

  return {
    filters,
    qualityItems: computed<BusinessConsoleMesRelatedQualityItemRow[]>(() =>
      envelopeItems<
        BusinessConsoleMesRelatedQualityItemRow,
        BusinessConsoleMesRelatedQualityItemListEnvelope
      >(qualityQuery.data.value),
    ),
    qualityItemsError: qualityQuery.error,
    qualityItemsPending: qualityQuery.isLoading,
    qualityItemsState: businessReadState(qualityQuery, () => hasBusinessContext(filters)),
    qualityItemsTotal: computed(() => envelopeTotal(qualityQuery.data.value)),
    recordDefect: async (
      body: Omit<BusinessConsoleMesRecordDefectV2Request, 'organizationId' | 'environmentId'>,
    ) => {
      // 页面只提交缺陷事实和已核验的写范围选择；组织与环境取当前 Business Context，
      // 不接受调用方覆盖。Gateway 仍会用主体令牌实时核验该目标上下文与工单范围。
      const organizationId = filters.organizationId.trim()
      const environmentId = filters.environmentId.trim()
      if (!organizationId || !environmentId) {
        throw new Error('尚未确定当前组织，不能登记缺陷。')
      }
      const operationTaskId = body.operationTaskId?.trim()
      const safeBody: BusinessConsoleMesRecordDefectV2Request = {
        organizationId,
        environmentId,
        workOrderId: body.workOrderId,
        ...(operationTaskId ? { operationTaskId } : {}),
        defectCode: body.defectCode,
        quantity: body.quantity,
        recordedAtUtc: body.recordedAtUtc,
        idempotencyKey: body.idempotencyKey,
        scopeKind: body.scopeKind,
        scopeId: body.scopeId,
      }
      return defectMutation.mutateAsync({ body: safeBody })
    },
    recordDefectPending: defectMutation.isLoading,
    refreshQualityItems: () => refetchWithBusinessContext(filters, qualityQuery),
  }
}

export const useMesRelatedQualityItems = useMesQualityContext
