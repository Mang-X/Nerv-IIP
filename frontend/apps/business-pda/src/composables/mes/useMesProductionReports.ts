import { mesReportIntentScope } from './mesReportIntent'
import {
  confirmBusinessConsoleOperation,
  getBusinessConsoleMesWorkOrderDetail,
  getBusinessConsoleMesProductionReport,
  listBusinessConsoleMesMaterialIssueRequestsQueryOptions,
  listBusinessConsoleMesProductionReportsQueryOptions,
  listBusinessConsoleMesReportableOperationTasks,
  listBusinessConsoleQualityScrapReasonCodesQueryOptions,
  listBusinessConsoleMesTelemetryProductionReportCandidatesQueryOptions,
  promoteBusinessConsoleMesTelemetryProductionReportCandidateMutationOptions,
  dismissBusinessConsoleMesTelemetryProductionReportCandidateMutationOptions,
  recordBusinessConsoleMesProductionReportMutationOptions,
  type BusinessConsoleMesMaterialIssueRequestRow,
  type BusinessConsoleMesOperationTaskRow,
  type BusinessConsoleMesProductionReportListEnvelope,
  type BusinessConsoleMesProductionReportDetail,
  type BusinessConsoleMesProductionReportRow,
  type BusinessConsoleMesTelemetryCandidateRow,
  type BusinessConsoleRecordProductionReportRequest,
} from '@nerv-iip/api-client'
import {
  acquirePendingBusinessIntent,
  clearPendingBusinessIntent,
  isAvailableMaterialLot,
  peekPendingBusinessIntent,
  shouldRetainPendingBusinessIntent,
  useListResponseState,
  useScopeBoundListResponse,
} from '@nerv-iip/business-core'
import type { AvailableMaterialLotFields } from '@nerv-iip/business-core'
import { useMutation, useQuery, useQueryCache } from '@pinia/colada'
import { computed, shallowRef, watch, type Ref } from 'vue'
import { assertLifecycleActionExecutable } from '@/composables/lifecycleActionRecovery'
import { useAuthStore } from '@/stores/auth'
import {
  MES_REPORTING_WRITE_PERMISSION,
  defaultFilters,
  envelopeItems,
  envelopeTotal,
  hasSameReportExecutionContext,
  hasScope,
  ignoreBackgroundError,
  invalidateMesQueries,
  scopeKey,
  scopeQuery,
  toListQuery,
  type MesReportExecutionContext,
  type RecordReportInput,
} from './mesShared'
import { useMesPrincipalWorkScope } from './useMesWorkScope'
import { readExactOperationTask } from './useMesOperationTasks'

type AvailableMaterialLot = BusinessConsoleMesMaterialIssueRequestRow & AvailableMaterialLotFields

export function useMesProductionMaterialLots(
  context: () => { workOrderId?: string | null; operationTaskId?: string | null } | null,
) {
  const auth = useAuthStore()
  const filters = defaultFilters()
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
      hasScope(filters) &&
      materialsReadPermission.value &&
      Boolean(filters.workOrderId && filters.operationTaskId),
  )
  const materialLotsQuery = useQuery(() => ({
    ...listBusinessConsoleMesMaterialIssueRequestsQueryOptions({
      query: {
        ...toListQuery(filters),
        skip: 0,
        take: 500,
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

/** 只消费 Quality 域发布的 scrap 专用原因码目录，不在 PDA 维护原因码字典。 */
export function useMesScrapReasonCodes(shouldLoad: () => boolean) {
  const auth = useAuthStore()
  const filters = defaultFilters()
  const qualityInspectionRecordsReadPermission = computed(() =>
    (auth.principal?.permissionCodes ?? []).includes('business.quality.inspection-records.read'),
  )
  const enabled = computed(
    () => hasScope(filters) && qualityInspectionRecordsReadPermission.value && shouldLoad(),
  )
  const query = useQuery(() => ({
    ...listBusinessConsoleQualityScrapReasonCodesQueryOptions({
      query: {
        ...scopeQuery(filters),
        skip: 0,
        take: 100,
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

const REPORTABLE_TASK_PAGE_SIZE = 100

export function useMesProductionReports(workOrderId?: Readonly<Ref<string>>) {
  const auth = useAuthStore()
  const filters = defaultFilters()
  const reportScope = useMesPrincipalWorkScope(filters, MES_REPORTING_WRITE_PERMISSION)
  const queryCache = useQueryCache()
  const scopeReady = computed(() => hasScope(filters))
  const reportContextIdentity = computed(() => {
    const selectedScope = reportScope.selectedScope.value
    return [
      reportScope.principalIdentity.value,
      filters.organizationId,
      filters.environmentId,
      selectedScope?.kind ?? '',
      selectedScope?.id ?? '',
    ].join('\u0000')
  })
  const contextGeneration = shallowRef(0)
  watch(
    reportContextIdentity,
    () => {
      contextGeneration.value += 1
    },
    { immediate: true, flush: 'sync' },
  )
  const reportContext = computed<MesReportExecutionContext | undefined>(() => {
    const selectedScope = reportScope.selectedScope.value
    if (!selectedScope || !hasScope(filters)) return undefined
    return {
      principalId: reportScope.principalIdentity.value,
      organizationId: filters.organizationId,
      environmentId: filters.environmentId,
      scopeKind: selectedScope.kind,
      scopeId: selectedScope.id,
      generation: contextGeneration.value,
    }
  })

  const reportableTasksQuery = useQuery(() => {
    const context = reportContext.value
    const requestedWorkOrderId = workOrderId?.value.trim() ?? ''
    return {
      key: [
        'mes-reporting-write-authority',
        context?.principalId ?? '',
        context?.organizationId ?? '',
        context?.environmentId ?? '',
        context?.scopeKind ?? '',
        context?.scopeId ?? '',
        context?.generation ?? 0,
        requestedWorkOrderId,
      ],
      enabled: Boolean(context && requestedWorkOrderId),
      query: async ({ signal }) => {
        if (!context || !requestedWorkOrderId) return undefined
        const items: BusinessConsoleMesOperationTaskRow[] = []
        let skip = 0
        while (true) {
          const response = await listBusinessConsoleMesReportableOperationTasks({
            query: {
              organizationId: context.organizationId,
              environmentId: context.environmentId,
              workOrderId: requestedWorkOrderId,
              scopeKind: context.scopeKind,
              scopeId: context.scopeId,
              skip,
              take: REPORTABLE_TASK_PAGE_SIZE,
            },
            signal,
          })
          const envelope = response.data
          if (!envelope?.success || !envelope.data) {
            throw new Error(envelope?.message?.trim() || '可报工工序读取失败，请重试。')
          }
          const page = envelope.data.items ?? []
          items.push(...page)
          skip += page.length
          if (
            page.length < REPORTABLE_TASK_PAGE_SIZE ||
            (envelope.data.total !== undefined && skip >= envelope.data.total)
          ) {
            return { generation: context.generation, items }
          }
        }
      },
    }
  })
  const reportableTasks = computed(() => {
    const context = reportContext.value
    const response = reportableTasksQuery.data.value
    return context && response?.generation === context.generation ? response.items : undefined
  })
  const reportableTasksReady = computed(
    () =>
      Boolean(reportContext.value && workOrderId?.value.trim()) &&
      !reportableTasksQuery.isLoading.value &&
      !reportableTasksQuery.error.value &&
      reportableTasks.value !== undefined,
  )

  const reportsQuery = useQuery(() => ({
    ...listBusinessConsoleMesProductionReportsQueryOptions({
      query: toListQuery(filters),
    }),
    enabled: scopeReady.value,
  }))
  const currentResponse = useScopeBoundListResponse(
    () => reportsQuery.data.value,
    () => scopeKey(filters),
    scopeReady,
  )
  const { hasSuccessfulResponse, hasFailedResponse } = useListResponseState(
    currentResponse,
    scopeReady,
    reportsQuery.isLoading,
  )

  const recordMutation = useMutation({
    ...recordBusinessConsoleMesProductionReportMutationOptions(),
    onSuccess() {
      void invalidateMesQueries(queryCache, [
        'listBusinessConsoleMesProductionReports',
        'listBusinessConsoleMesWorkOrders',
      ]).catch(ignoreBackgroundError)
    },
  })
  return {
    filters,
    productionReports: computed<BusinessConsoleMesProductionReportRow[]>(() =>
      envelopeItems<
        BusinessConsoleMesProductionReportRow,
        BusinessConsoleMesProductionReportListEnvelope
      >(currentResponse.value),
    ),
    total: computed(() => envelopeTotal(currentResponse.value)),
    pending: reportsQuery.isLoading,
    error: reportsQuery.error,
    hasSuccessfulResponse,
    hasFailedResponse,
    refresh: () => (scopeReady.value ? reportsQuery.refetch() : Promise.resolve()),
    reportScopeMessage: reportScope.scopeMessage,
    reportScopePending: reportScope.scopePending,
    reportScopeReady: reportScope.scopeReady,
    reportScope: reportScope.selectedScope,
    reportContext,
    contextGeneration,
    reportableTasks,
    reportableTasksPending: reportableTasksQuery.isLoading,
    reportableTasksError: reportableTasksQuery.error,
    reportableTasksReady,
    refreshReportableTasks: () =>
      reportContext.value && workOrderId?.value.trim()
        ? reportableTasksQuery.refetch()
        : Promise.resolve(),
    confirmReport: async (input: {
      reportNo: string
      productionReportId: string
      workOrderId: string
      operationTaskId: string
      context: MesReportExecutionContext
    }): Promise<BusinessConsoleMesProductionReportDetail> => {
      const reportNo = input.reportNo.trim()
      const assertCurrentContext = () => {
        const currentContext = reportContext.value
        if (
          !currentContext ||
          !reportNo ||
          !hasSameReportExecutionContext(currentContext, input.context)
        ) {
          throw new Error('报工公开回读上下文无效，尚不能确认成功。')
        }
      }
      assertCurrentContext()
      let report: BusinessConsoleMesProductionReportDetail | undefined
      try {
        const { data } = await getBusinessConsoleMesProductionReport({
          path: { reportNo },
          query: {
            organizationId: input.context.organizationId,
            environmentId: input.context.environmentId,
          },
          throwOnError: true,
        })
        report = data?.success ? data.data?.report : undefined
      } catch {
        assertCurrentContext()
        throw new Error('报工已受理，但公开记录尚未回读到同一工单与工序，请重试核验。')
      }
      assertCurrentContext()
      if (
        report?.reportNo?.trim() !== reportNo ||
        report.productionReportId?.trim() !== input.productionReportId.trim() ||
        report.workOrderId?.trim() !== input.workOrderId.trim() ||
        report.operationTaskId?.trim() !== input.operationTaskId.trim()
      ) {
        throw new Error('报工已受理，但公开记录尚未回读到同一工单与工序，请重试核验。')
      }
      return report
    },
    recordReport: async (
      input: RecordReportInput,
      isCurrent?: () => boolean,
      recovering?: boolean,
    ) => {
      const selectedScope = reportScope.requireSelectedScope()
      const executionContext = reportContext.value
      const assertCurrent = () => {
        const current = reportContext.value
        if (
          !executionContext ||
          !current ||
          !hasSameReportExecutionContext(current, executionContext) ||
          isCurrent?.() === false
        ) {
          throw new Error('作业身份或范围已变化，请重新选择工单与工序后报工。')
        }
      }
      assertCurrent()
      const suppliedKey = input.idempotencyKey
      const scope = mesReportIntentScope(executionContext!, input)
      const isReplay = recovering ?? Boolean(peekPendingBusinessIntent(scope))
      const currentPayload = {
        ...input,
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        reportedAtUtc: new Date().toISOString(),
        scopeKind: selectedScope.kind,
        scopeId: selectedScope.id,
      } satisfies BusinessConsoleRecordProductionReportRequest
      const pending = acquirePendingBusinessIntent(
        scope,
        () =>
          suppliedKey?.trim() ||
          globalThis.crypto?.randomUUID?.() ||
          `mes-report-${Date.now()}-${Math.random()}`,
        currentPayload,
      )
      try {
        const workOrderId = input.workOrderId?.trim()
        const operationTaskId = input.operationTaskId?.trim()
        let authoritative: BusinessConsoleMesOperationTaskRow | undefined
        if (isReplay && workOrderId) {
          const { data } = await getBusinessConsoleMesWorkOrderDetail({
            path: { workOrderId },
            query: {
              ...scopeQuery(filters),
              scopeKind: selectedScope.kind,
              scopeId: selectedScope.id,
            },
            throwOnError: true,
          })
          if (!data?.success || data.data?.workOrderId !== workOrderId) {
            throw new Error('原报工的工单工序核验失败，请重试核验。')
          }
          authoritative = data.data.operationTasks?.find(
            (task) => task.workOrderId === workOrderId && task.operationTaskId === operationTaskId,
          )
        } else if (operationTaskId) {
          authoritative = await readExactOperationTask(
            filters,
            operationTaskId,
            selectedScope,
            workOrderId,
            'reportable',
          )
        }
        const samePair =
          authoritative?.workOrderId === workOrderId &&
          authoritative?.operationTaskId === operationTaskId
        assertCurrent()
        const reportAllowed = authoritative?.allowedActions?.some(
          (action) => action.trim().toLowerCase() === 'report',
        )
        if (input.completesOperation) {
          assertLifecycleActionExecutable({
            domain: 'mes-operation-task',
            action: 'report-complete',
            facts: {
              status: samePair ? authoritative?.status : undefined,
              idempotentReplay: isReplay,
            },
          })
        }
        if (!samePair || (!isReplay && !reportAllowed)) {
          throw new Error('当前工序不可报工。')
        }
      } catch (error) {
        if (!isReplay) {
          assertCurrent()
          clearPendingBusinessIntent(scope)
          throw Object.assign(error instanceof Object ? error : new Error(String(error)), {
            reportNotAccepted: true,
          })
        }
        throw error
      }
      const frozenPayload =
        pending.payloadSnapshot !== undefined
          ? (pending.payloadSnapshot as BusinessConsoleRecordProductionReportRequest)
          : currentPayload
      try {
        const result = await confirmBusinessConsoleOperation(
          await recordMutation.mutateAsync({
            body: {
              ...frozenPayload,
              idempotencyKey: pending.idempotencyKey,
            },
          }),
          {
            expectedOperationType: 'mes.production-report.record',
            expectedIdempotencyKey: pending.idempotencyKey,
            expectedResourceIdSelector: (envelope) => envelope.data?.productionReportId,
          },
        )
        try {
          assertCurrent()
        } catch (error) {
          // A dispatched write may have committed; a stale response cannot release its intent.
          throw Object.assign(error as Error, { indeterminate: true })
        }
        if (!result?.data?.printingPreparationPending) clearPendingBusinessIntent(scope)
        return result
      } catch (error) {
        if (!isReplay && !shouldRetainPendingBusinessIntent(error)) {
          assertCurrent()
          clearPendingBusinessIntent(scope)
          throw Object.assign(error instanceof Object ? error : new Error(String(error)), {
            reportNotAccepted: true,
          })
        }
        throw error
      }
    },
  }
}

export function useMesTelemetryProductionReportCandidates() {
  const filters = Object.assign(defaultFilters(), { status: 'pending-confirmation' })
  const queryCache = useQueryCache()
  const query = useQuery(() => ({
    ...listBusinessConsoleMesTelemetryProductionReportCandidatesQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        status: filters.status,
        workCenterId: filters.workCenterId,
        deviceAssetId: filters.deviceAssetId,
        skip: filters.skip,
        take: filters.take,
      },
    }),
    enabled: hasScope(filters),
  }))
  const promoteMutation = useMutation({
    ...promoteBusinessConsoleMesTelemetryProductionReportCandidateMutationOptions(),
    onSuccess: () =>
      void invalidateMesQueries(queryCache, [
        'listBusinessConsoleMesTelemetryProductionReportCandidates',
        'listBusinessConsoleMesProductionReports',
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
        query.data.value as CandidateEnvelope | undefined,
      ),
    ),
    total: computed(() => envelopeTotal(query.data.value as CandidateEnvelope | undefined)),
    pending: query.isLoading,
    /** 转为正式报工，返回报工单号；业务拒绝（含 success=false 信封）一律抛出，交给页面上屏原因。 */
    promote: async (candidateId: string, workOrderId: string, operationTaskId: string) => {
      const envelope = await promoteMutation.mutateAsync({
        path: { candidateId },
        query: { organizationId: filters.organizationId, environmentId: filters.environmentId },
        body: { workOrderId, operationTaskId },
      })
      if (!envelope?.success) throw envelope
      return envelope.data?.reportNo ?? ''
    },
    dismiss: async (candidateId: string, reason: string) => {
      const envelope = await dismissMutation.mutateAsync({
        path: { candidateId },
        query: { organizationId: filters.organizationId, environmentId: filters.environmentId },
        body: { reason },
      })
      if (!envelope?.success) throw envelope
    },
  }
}

const TELEMETRY_TARGET_TASK_TAKE = 20

/**
 * 遥测候选转正时「报到哪道工序」的候选：只取可报工（执行中）的工序任务。
 * 没输关键词时按候选设备收窄——即这台设备当前在制的工序；输入工单号搜索时放开设备限制。
 */
export function useMesTelemetryCandidateTargetTasks(
  context: Readonly<Ref<MesReportExecutionContext | undefined>>,
  deviceAssetId: Readonly<Ref<string>>,
  keyword: Readonly<Ref<string>>,
) {
  const query = useQuery(() => {
    const current = context.value
    const device = deviceAssetId.value.trim()
    const search = keyword.value.trim()
    return {
      key: [
        'mes-telemetry-candidate-target-tasks',
        current?.principalId ?? '',
        current?.organizationId ?? '',
        current?.environmentId ?? '',
        current?.scopeKind ?? '',
        current?.scopeId ?? '',
        current?.generation ?? 0,
        device,
        search,
      ],
      enabled: Boolean(current && (device || search)),
      query: async ({ signal }) => {
        if (!current) return []
        const response = await listBusinessConsoleMesReportableOperationTasks({
          query: {
            organizationId: current.organizationId,
            environmentId: current.environmentId,
            scopeKind: current.scopeKind,
            scopeId: current.scopeId,
            deviceAssetId: search ? undefined : device || undefined,
            keyword: search || undefined,
            skip: 0,
            take: TELEMETRY_TARGET_TASK_TAKE,
          },
          signal,
        })
        const envelope = response.data
        if (!envelope?.success || !envelope.data) {
          throw new Error(envelope?.message?.trim() || '执行中工序读取失败，请重试。')
        }
        return envelope.data.items ?? []
      },
    }
  })
  return {
    tasks: computed(() => query.data.value ?? []),
    pending: query.isLoading,
    error: query.error,
    refresh: () => query.refetch(),
  }
}
