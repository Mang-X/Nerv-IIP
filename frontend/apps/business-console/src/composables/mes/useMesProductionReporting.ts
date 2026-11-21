import {
  confirmBusinessConsoleOperation,
  getBusinessConsoleMesProductionReportQueryOptions,
  getBusinessConsoleMesWipSummary,
  getBusinessConsoleBarcodePrintBatch,
  getBusinessConsoleMesWorkOrderDetail,
  listBusinessConsoleMesMaterialIssueRequestsQueryOptions,
  listBusinessConsoleMesProductionReportsQueryOptions,
  listBusinessConsoleMesReceivableProducedLots,
  listBusinessConsoleMesTelemetryProductionReportCandidatesQueryOptions,
  promoteBusinessConsoleMesTelemetryProductionReportCandidateMutationOptions,
  dismissBusinessConsoleMesTelemetryProductionReportCandidateMutationOptions,
  listBusinessConsoleQualityScrapReasonCodesQueryOptions,
  recordBusinessConsoleMesProductionReport,
  reverseBusinessConsoleMesProductionReportMutationOptions,
  type BusinessConsoleMesMaterialIssueRequestRow,
  type BusinessConsoleMesProductionReportListEnvelope,
  type BusinessConsoleMesProductionReportDetailEnvelope,
  type BusinessConsoleMesProductionReportDetailResponse,
  type BusinessConsoleMesProductionReportRow,
  type BusinessConsoleMesTelemetryCandidateRow,
  type BusinessConsoleRecordProductionReportRequest,
} from '@nerv-iip/api-client'
import {
  acquirePendingBusinessIntent,
  clearPendingBusinessIntent,
  shouldRetainPendingBusinessIntent,
  isAvailableMaterialLot,
  peekPendingBusinessIntent,
} from '@nerv-iip/business-core'
import type { AvailableMaterialLotFields } from '@nerv-iip/business-core'
import { useAuthStore } from '@/stores/auth'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { useMutation, useQuery, useQueryCache } from '@pinia/colada'
import { computed, reactive, shallowRef, watch, toValue, type MaybeRefOrGetter } from 'vue'
import {
  bindBusinessContext,
  hasBusinessContext,
  refetchWithBusinessContext,
  withBusinessContextEnabled,
} from '../businessContextBinding'
import { businessReadState } from '../businessReadState'
import { executeLifecycleAction } from '../lifecycleAction'
import {
  MES_REPORTING_WRITE_PERMISSION,
  MES_WORK_ORDERS_READ_PERMISSION,
  defaultContext,
  defaultFilters,
  envelopeItems,
  envelopeTotal,
  ignoreBackgroundError,
  invalidateMesQueries,
  makeIdempotencyKey,
  requirePendingPayloadSnapshot,
  toListQueryWithoutStatus,
  unwrapData,
} from './mesShared'
import { useMesPrincipalWorkScope } from './useMesWorkScope'
import { readMesOperationLifecycleRequest } from './mesOperationLifecycle'

/**
 * 生产报工（写）单独成钩：报工弹窗从工单列表 / 工序执行两处行内打开，不应为了一个写操作再拉一份工单列表。
 * 业务上下文（组织 / 环境）由本钩内部绑定并补齐到请求体，调用方只传报工对象与数量。
 */
export type MesProductionReportInput = Omit<
  BusinessConsoleRecordProductionReportRequest,
  'organizationId' | 'environmentId' | 'scopeKind' | 'scopeId'
>

export interface MesProductionQuantitySnapshot {
  plannedQuantity: number
  reportedGoodQuantity: number
}

type AvailableMaterialLot = BusinessConsoleMesMaterialIssueRequestRow & AvailableMaterialLotFields

export function useMesProductionMaterialLots(
  context: () => { workOrderId?: string | null; operationTaskId?: string | null } | null,
) {
  const auth = useAuthStore()
  const filters = bindBusinessContext(
    reactive({
      organizationId: '',
      environmentId: '',
      workOrderId: '',
      operationTaskId: '',
      skip: 0,
      take: 500,
    }),
  )
  const materialsReadPermission = computed(() =>
    (auth.principal?.permissionCodes ?? []).includes('business.mes.materials.read'),
  )

  watch(
    () => {
      const current = context()
      return [current?.workOrderId?.trim() ?? '', current?.operationTaskId?.trim() ?? '']
    },
    ([workOrderId, operationTaskId]) => {
      filters.workOrderId = workOrderId
      filters.operationTaskId = operationTaskId
    },
    { immediate: true },
  )

  const enabled = computed(
    () =>
      hasBusinessContext(filters) &&
      materialsReadPermission.value &&
      Boolean(filters.workOrderId && filters.operationTaskId),
  )
  const materialLotsQuery = useQuery(() => ({
    ...listBusinessConsoleMesMaterialIssueRequestsQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        workOrderId: filters.workOrderId,
        operationTaskId: filters.operationTaskId,
        skip: filters.skip,
        take: filters.take,
      },
    }),
    enabled: enabled.value,
  }))

  return {
    materialsReadPermission,
    materialLotsPending: materialLotsQuery.isLoading,
    materialLotsError: materialLotsQuery.error,
    availableMaterialLots: computed<AvailableMaterialLot[]>(() => {
      if (!materialLotsQuery.data.value?.success) return []
      return (materialLotsQuery.data.value.data?.items ?? []).filter(isAvailableMaterialLot)
    }),
    refreshMaterialLots: () => (enabled.value ? materialLotsQuery.refetch() : Promise.resolve()),
  }
}

export function useMesProductionReporting() {
  const auth = useAuthStore()
  const context = defaultContext()
  const reportScope = useMesPrincipalWorkScope(context, MES_REPORTING_WRITE_PERMISSION)
  const canReadReceiptHandoff = () =>
    [P.mesReceiptsRead, P.mesReceiptsManage, P.mesWorkOrdersRead].every((permission) =>
      auth.principal?.permissionCodes?.includes(permission),
    )
  const workOrderReadScope = useMesPrincipalWorkScope(
    context,
    MES_WORK_ORDERS_READ_PERMISSION,
    canReadReceiptHandoff,
  )
  const queryCache = useQueryCache()
  const issuedReportCompleteIntents = new Set<string>()
  const recordProductionReportPending = shallowRef(false)
  const recordProductionReportError = shallowRef<unknown>()
  // 一道工序只有一个待确认的报工槽位；未知结果后重新进入时先恢复完整载荷。
  const productionReportIntentScope = (workOrderId: string, operationTaskId: string) => ({
    principalId: auth.principal?.principalId ?? auth.sessionId ?? 'unrestored-session',
    organizationId: context.organizationId,
    environmentId: context.environmentId,
    operationType: 'mes.production-report.record',
    payloadFingerprint: JSON.stringify({ workOrderId, operationTaskId }),
  })
  function restoreProductionReport(workOrderId: string, operationTaskId: string) {
    const pending = peekPendingBusinessIntent(
      productionReportIntentScope(workOrderId, operationTaskId),
    )
    if (!pending) return undefined
    return requirePendingPayloadSnapshot<BusinessConsoleRecordProductionReportRequest>(
      pending.payloadSnapshot,
      '生产报工',
    )
  }
  async function readProductionPrintStatus(printBatchId: string) {
    const response = await getBusinessConsoleBarcodePrintBatch({
      path: { printBatchId },
      query: { ...context },
      throwOnError: true,
    })
    if (!response.data?.success) throw response.data
    return response.data.data?.printBatch?.status
  }
  const refreshProductionReportQueries = () =>
    invalidateMesQueries(queryCache, [
      'getBusinessConsoleMesOverview',
      'getBusinessConsoleMesWipSummary',
      'listBusinessConsoleMesProductionReports',
      'listBusinessConsoleMesWorkOrders',
      'listBusinessConsoleMesOperationTasks',
    ])

  async function readProductionQuantitySnapshot(
    workOrderId: string,
    operationTaskId: string,
  ): Promise<MesProductionQuantitySnapshot> {
    const normalizedWorkOrderId = workOrderId.trim()
    const normalizedOperationTaskId = operationTaskId.trim()
    const response = await getBusinessConsoleMesWipSummary({
      query: {
        organizationId: context.organizationId,
        environmentId: context.environmentId,
        workOrderId: normalizedWorkOrderId,
        operationTaskId: normalizedOperationTaskId,
        skip: 0,
        take: 1,
      },
      throwOnError: false,
    })
    if (response.error !== undefined) throw response.error
    if (!response.data?.success) {
      throw response.data ?? new Error('生产报工累计数量读取失败')
    }
    const row = response.data.data?.items?.find(
      (candidate) => candidate.operationTaskId?.trim() === normalizedOperationTaskId,
    )
    if (!row) {
      throw new Error(
        `生产工单 ${normalizedWorkOrderId} 的工序 ${normalizedOperationTaskId} 缺少累计报工数据。请刷新工序列表后重试；若仍无数据，请联系计划员核对工序。`,
      )
    }
    return {
      plannedQuantity: row.plannedQuantity ?? 0,
      reportedGoodQuantity: row.goodQuantity ?? 0,
    }
  }

  function prepareReceiptHandoff() {
    const query = { ...context }
    const principalId = auth.principal?.principalId
    const scope = workOrderReadScope.selectedScope.value
    if (!scope) return

    const selection = workOrderReadScope.scopeSelectionValue.value
    const isCurrent = () =>
      auth.principal?.principalId === principalId &&
      context.organizationId === query.organizationId &&
      context.environmentId === query.environmentId &&
      canReadReceiptHandoff() &&
      !workOrderReadScope.scopeError.value &&
      workOrderReadScope.scopeSelectionValue.value === selection &&
      workOrderReadScope.selectedScope.value?.kind === scope.kind &&
      workOrderReadScope.selectedScope.value?.id === scope.id
    if (!isCurrent()) return

    return async (workOrderId: string, operationTaskId: string, reportNo: string) => {
      if (!isCurrent()) return
      const path = { workOrderId }
      const lots = await listBusinessConsoleMesReceivableProducedLots({ path, query })
      if (!isCurrent() || !lots.data?.success) return
      const hasReceivableLot = lots.data.data?.items?.some(
        (lot) =>
          lot.reportNo === reportNo &&
          lot.operationTaskId === operationTaskId &&
          (lot.remainingQuantity ?? 0) > 0,
      )
      if (!hasReceivableLot) return

      const order = await getBusinessConsoleMesWorkOrderDetail({
        path,
        query: { ...query, scopeKind: scope.kind, scopeId: scope.id },
      })
      const skuId = order.data?.success ? order.data.data?.skuId?.trim() : undefined
      if (!skuId || !isCurrent()) return
      return { workOrderId, skuId, isCurrent }
    }
  }

  async function recordProductionReportAction(
    body: MesProductionReportInput,
    options: { onCommandAttempt?: () => void } = {},
  ) {
    recordProductionReportPending.value = true
    recordProductionReportError.value = undefined
    try {
      const selectedScope = reportScope.requireSelectedScope()
      const submittedBody = {
        ...body,
        organizationId: context.organizationId,
        environmentId: context.environmentId,
        scopeKind: selectedScope.kind,
        scopeId: selectedScope.id,
      } satisfies BusinessConsoleRecordProductionReportRequest
      const suppliedKey = submittedBody.idempotencyKey
      const intentScope = productionReportIntentScope(
        submittedBody.workOrderId ?? '',
        submittedBody.operationTaskId ?? '',
      )
      const restored = peekPendingBusinessIntent(intentScope)
      const pending = acquirePendingBusinessIntent(
        intentScope,
        () => suppliedKey?.trim() || makeIdempotencyKey('production-report'),
        submittedBody,
      )
      const stableBody =
        requirePendingPayloadSnapshot<BusinessConsoleRecordProductionReportRequest>(
          pending.payloadSnapshot,
          '生产报工',
        )
      const idempotencyKey = pending.idempotencyKey
      const workOrderId = stableBody.workOrderId?.trim()
      const operationTaskId = stableBody.operationTaskId?.trim()
      const stableScopeKind = stableBody.scopeKind?.trim()
      const stableScopeId = stableBody.scopeId?.trim()
      if (!stableScopeKind || !stableScopeId) {
        throw new Error('生产报工缺少冻结的授权作业范围，请保留当前页面并人工核实。')
      }
      const stableScope = { kind: stableScopeKind, id: stableScopeId }
      const reportCompleteIntent =
        stableBody.completesOperation && idempotencyKey && workOrderId && operationTaskId
          ? `${workOrderId}\u0000${operationTaskId}\u0000${idempotencyKey}`
          : undefined
      const isIssuedReplay =
        reportCompleteIntent !== undefined &&
        (issuedReportCompleteIntents.has(reportCompleteIntent) ||
          restored?.idempotencyKey === pending.idempotencyKey)
      const command = () => {
        if (reportCompleteIntent) issuedReportCompleteIntents.add(reportCompleteIntent)
        options.onCommandAttempt?.()
        return recordBusinessConsoleMesProductionReport({
          body: {
            ...stableBody,
            idempotencyKey,
          },
          throwOnError: false,
        })
      }
      const coordinate = async () => {
        const envelope = stableBody.completesOperation
          ? await executeLifecycleAction({
              readLatest: () =>
                readMesOperationLifecycleRequest(
                  stableBody.operationTaskId ?? '',
                  {
                    organizationId: context.organizationId,
                    environmentId: context.environmentId,
                    workOrderId: stableBody.workOrderId,
                  },
                  stableScope,
                  'report-complete',
                  isIssuedReplay,
                ),
              command,
            })
          : await command().then((response) => {
              if (response.error !== undefined) throw response.error
              if (response.data?.success === false) throw response.data
              return response.data
            })
        if (!envelope) throw new Error('生产报工未返回业务信封')
        await confirmBusinessConsoleOperation(envelope, {
          expectedOperationType: 'mes.production-report.record',
          expectedIdempotencyKey: pending.idempotencyKey,
          expectedResourceIdSelector: (candidate) => candidate.data?.productionReportId,
        })
        return envelope
      }
      let result
      try {
        result = await coordinate()
        // 报工回执只确认 MES；标签激活尚未收敛时保留同一报工意图供恢复。
        if (!result.data?.printingPreparationPending) clearPendingBusinessIntent(intentScope)
      } catch (error) {
        if (!shouldRetainPendingBusinessIntent(error)) clearPendingBusinessIntent(intentScope)
        throw error
      }
      await refreshProductionReportQueries()
      return result
    } catch (error) {
      recordProductionReportError.value = error
      throw error
    } finally {
      recordProductionReportPending.value = false
    }
  }

  return {
    restoreProductionReport,
    readProductionPrintStatus,
    recordProductionReport: recordProductionReportAction,
    recordProductionReportError,
    recordProductionReportPending,
    readProductionQuantitySnapshot,
    prepareReceiptHandoff,
    reportScopeMessage: reportScope.scopeMessage,
    reportScopePending: reportScope.scopePending,
    reportScopeReady: reportScope.scopeReady,
    refreshProductionReportState: refreshProductionReportQueries,
  }
}

/**
 * 报废原因码只消费 Quality 域已发布的 scrap 专用目录；不在 MES/PDA 侧维护字典。
 * 调用方通过 shouldLoad 将读取限定在确有报废数量的报工场景，且权限或业务上下文不满足时不发请求。
 */
export function useMesScrapReasonCodes(shouldLoad: () => boolean) {
  const auth = useAuthStore()
  const filters = bindBusinessContext(
    reactive({
      organizationId: '',
      environmentId: '',
      skip: 0,
      take: 100,
    }),
  )
  const qualityInspectionRecordsReadPermission = computed(() =>
    (auth.principal?.permissionCodes ?? []).includes('business.quality.inspection-records.read'),
  )
  const enabled = computed(
    () =>
      hasBusinessContext(filters) && qualityInspectionRecordsReadPermission.value && shouldLoad(),
  )
  const query = useQuery(() => ({
    ...listBusinessConsoleQualityScrapReasonCodesQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        skip: filters.skip,
        take: filters.take,
      },
    }),
    enabled: enabled.value,
  }))

  return {
    qualityInspectionRecordsReadPermission,
    scrapReasonCodesPending: query.isLoading,
    scrapReasonCodesError: query.error,
    scrapReasonCodes: computed(() =>
      query.data.value?.success ? (query.data.value.data?.items ?? []) : [],
    ),
    refreshScrapReasonCodes: () => (enabled.value ? query.refetch() : Promise.resolve()),
  }
}

export function useMesProductionReportDetail(reportNo: MaybeRefOrGetter<string | null>) {
  const filters = defaultContext()
  const query = useQuery(() => ({
    ...getBusinessConsoleMesProductionReportQueryOptions({
      path: { reportNo: toValue(reportNo) ?? '' },
      query: { organizationId: filters.organizationId, environmentId: filters.environmentId },
    }),
    enabled: hasBusinessContext(filters) && !!toValue(reportNo),
  }))
  return {
    detail: computed(() => {
      const response = unwrapData<
        BusinessConsoleMesProductionReportDetailResponse,
        BusinessConsoleMesProductionReportDetailEnvelope
      >(query.data.value)
      return response?.report?.reportNo === toValue(reportNo) ? response : undefined
    }),
    detailError: query.error,
    detailPending: query.isLoading,
    refresh: () => query.refetch(),
  }
}

export function useMesProductionReports() {
  const filters = defaultFilters()
  const queryCache = useQueryCache()
  const reverseDetailReportNo = shallowRef('')

  const reportsQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsoleMesProductionReportsQueryOptions({
        query: toListQueryWithoutStatus(filters),
      }),
      filters,
    ),
  )

  const reverseDetailQuery = useQuery(() => ({
    ...getBusinessConsoleMesProductionReportQueryOptions({
      path: { reportNo: reverseDetailReportNo.value },
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
      },
    }),
    enabled: hasBusinessContext(filters) && reverseDetailReportNo.value.trim().length > 0,
  }))

  const reverseMutation = useMutation({
    ...reverseBusinessConsoleMesProductionReportMutationOptions(),
    onSuccess() {
      void invalidateMesQueries(queryCache, [
        // 本域:冲销新增负向记录行,并使原报工在列表中呈现为已冲销
        'listBusinessConsoleMesProductionReports',
        // 工单累计良品/报废回退(WorkOrder.ReverseProductionProgress),状态可能 Completed→Started
        'getBusinessConsoleMesWorkOrderDetail',
        'listBusinessConsoleMesWorkOrders',
        'getBusinessConsoleMesOverview',
        'getBusinessConsoleMesWipSummary',
        // 冲销 reopen 报工所在工序任务(OperationTask.ReopenAfterReportReversal)
        'listBusinessConsoleMesOperationTasks',
        // 冲销取消该产出批次未过账的完工入库请求(FinishedGoodsReceiptRequest.Cancel)
        'listBusinessConsoleMesFinishedGoodsReceiptRequests',
      ]).catch(ignoreBackgroundError)
      // 冲销仅持久化负向物料消耗,不发布库存过账/预留事件(见 MES ReverseProductionReportCommandHandler),
      // 故不失效库存可用量读面——与取消工单(释放预留→需失效库存)语义不同,不做无据的跨域失效。
    },
  })

  return {
    filters,
    productionReports: computed<BusinessConsoleMesProductionReportRow[]>(() =>
      envelopeItems<
        BusinessConsoleMesProductionReportRow,
        BusinessConsoleMesProductionReportListEnvelope
      >(reportsQuery.data.value),
    ),
    productionReportsError: reportsQuery.error,
    productionReportsPending: reportsQuery.isLoading,
    productionReportsState: businessReadState(reportsQuery, () => hasBusinessContext(filters)),
    productionReportsTotal: computed(() => envelopeTotal(reportsQuery.data.value)),
    refreshProductionReports: () => refetchWithBusinessContext(filters, reportsQuery),
    activateReverseDetail(reportNo: string) {
      reverseDetailReportNo.value = reportNo.trim()
    },
    deactivateReverseDetail() {
      reverseDetailReportNo.value = ''
    },
    reverseProductionReportDetail: computed<
      BusinessConsoleMesProductionReportDetailResponse | undefined
    >(() =>
      reverseDetailReportNo.value
        ? unwrapData<
            BusinessConsoleMesProductionReportDetailResponse,
            BusinessConsoleMesProductionReportDetailEnvelope
          >(reverseDetailQuery.data.value)
        : undefined,
    ),
    reverseProductionReportDetailError: reverseDetailQuery.error,
    reverseProductionReportDetailPending: reverseDetailQuery.isLoading,
    reverseProductionReport: (
      reportNo: string,
      body: { reason: string; reversedAtUtc?: string; idempotencyKey?: string },
    ) =>
      reverseMutation.mutateAsync({
        path: { reportNo },
        query: { organizationId: filters.organizationId, environmentId: filters.environmentId },
        body,
      }),
    reverseProductionReportError: reverseMutation.error,
    reverseProductionReportPending: reverseMutation.isLoading,
  }
}

export function useMesTelemetryProductionReportCandidates() {
  const filters = Object.assign(defaultFilters(), {
    status: 'pending-confirmation',
    fromUtc: undefined as string | undefined,
    toUtc: undefined as string | undefined,
  })
  const queryCache = useQueryCache()
  const candidatesQuery = useQuery(() =>
    withBusinessContextEnabled(
      listBusinessConsoleMesTelemetryProductionReportCandidatesQueryOptions({
        query: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
          status: filters.status === 'all' ? undefined : filters.status || undefined,
          workCenterId: filters.workCenterId || undefined,
          deviceAssetId: filters.deviceAssetId || undefined,
          fromUtc: filters.fromUtc,
          toUtc: filters.toUtc,
          skip: filters.skip,
          take: filters.take,
        },
      }),
      filters,
    ),
  )
  const promoteMutation = useMutation({
    ...promoteBusinessConsoleMesTelemetryProductionReportCandidateMutationOptions(),
    onSuccess: () =>
      void invalidateMesQueries(queryCache, [
        'listBusinessConsoleMesTelemetryProductionReportCandidates',
        'listBusinessConsoleMesProductionReports',
        'listBusinessConsoleMesWorkOrders',
      ]).catch(ignoreBackgroundError),
  })
  const dismissMutation = useMutation({
    ...dismissBusinessConsoleMesTelemetryProductionReportCandidateMutationOptions(),
    onSuccess: () =>
      void invalidateMesQueries(queryCache, [
        'listBusinessConsoleMesTelemetryProductionReportCandidates',
      ]).catch(ignoreBackgroundError),
  })
  type CandidateEnvelope = {
    data?: { items?: BusinessConsoleMesTelemetryCandidateRow[]; total?: number } | null
  }
  return {
    filters,
    candidates: computed(() =>
      envelopeItems<BusinessConsoleMesTelemetryCandidateRow, CandidateEnvelope>(
        candidatesQuery.data.value as CandidateEnvelope | undefined,
      ),
    ),
    total: computed(() =>
      envelopeTotal(candidatesQuery.data.value as CandidateEnvelope | undefined),
    ),
    pending: candidatesQuery.isLoading,
    error: candidatesQuery.error,
    state: businessReadState(candidatesQuery, () => hasBusinessContext(filters)),
    refresh: () => refetchWithBusinessContext(filters, candidatesQuery),
    promote: (candidateId: string, workOrderId: string, operationTaskId: string) =>
      promoteMutation.mutateAsync({
        path: { candidateId },
        query: { organizationId: filters.organizationId, environmentId: filters.environmentId },
        body: { workOrderId, operationTaskId },
      }),
    dismiss: (candidateId: string, reason: string) =>
      dismissMutation.mutateAsync({
        path: { candidateId },
        query: { organizationId: filters.organizationId, environmentId: filters.environmentId },
        body: { reason },
      }),
    actionPending: computed(
      () => promoteMutation.isLoading.value || dismissMutation.isLoading.value,
    ),
  }
}
