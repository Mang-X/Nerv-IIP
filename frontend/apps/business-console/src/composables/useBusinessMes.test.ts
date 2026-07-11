import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'
import { createPinia, setActivePinia } from 'pinia'

import {
  cancelBusinessConsoleMesWorkOrderMutationOptions,
  createBusinessConsoleMesFinishedGoodsReceiptRequestMutationOptions,
  createBusinessConsoleMesRushWorkOrderMutationOptions,
  createBusinessConsoleSopFileDownloadGrantMutationOptions,
  getBusinessConsoleMesBatchTraceabilityQueryOptions,
  getBusinessConsoleMesCurrentOperationSopsQueryOptions,
  getBusinessConsoleMesFoundationReadinessQueryOptions,
  getBusinessConsoleMesMaterialLotTraceabilityQueryOptions,
  getBusinessConsoleMesOverviewQueryOptions,
  getBusinessConsoleMesWorkOrderTraceabilityQueryOptions,
  getBusinessConsoleMesWipSummaryQueryOptions,
  listBusinessConsoleMesCapacityImpactsQueryOptions,
  listBusinessConsoleMesDispatchTasksQueryOptions,
  listBusinessConsoleMesDowntimeEventsQueryOptions,
  listBusinessConsoleMesFinishedGoodsReceiptRequestsQueryOptions,
  listBusinessConsoleMesMaterialIssueRequestsQueryOptions,
  listBusinessConsoleMesOperationTasksQueryOptions,
  listBusinessConsoleMesProductionPlansQueryOptions,
  listBusinessConsoleMesProductionReportsQueryOptions,
  listBusinessConsoleMesShiftHandoversQueryOptions,
  listBusinessConsoleMesWorkOrdersQueryOptions,
  recordBusinessConsoleMesProductionReportMutationOptions,
  runBusinessConsoleMesScheduleMutationOptions,
} from '@nerv-iip/api-client'
import {
  describeMesReadinessReason,
  useMesCapacityImpacts,
  useMesDispatchTasks,
  useMesDowntimeEvents,
  useMesFoundationReadiness,
  useMesFinishedGoodsReceipts,
  useMesCurrentOperationSops,
  useMesMaterialIssueRequests,
  useMesOperationTasks,
  useMesOverview,
  useMesProductionPlans,
  useMesProductionReports,
  useMesQualityContext,
  useMesSchedules,
  useMesShiftHandovers,
  useMesTraceability,
  useMesWipSummary,
  useMesWorkOrderDetail,
  useMesWorkOrders,
} from './useBusinessMes'
import { useBusinessContextStore } from '@/stores/businessContext'

const coladaState = vi.hoisted(() => ({
  invalidateQueries: vi.fn(async () => undefined),
  queryFactoriesById: new Map<string, () => unknown>(),
  queryDataById: new Map<string, unknown>(),
  queryRefetchById: new Map<string, ReturnType<typeof vi.fn>>(),
}))

vi.mock('@nerv-iip/api-client', () => ({
  acceptBusinessConsoleMesShiftHandoverMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
  assignBusinessConsoleMesDispatchTaskMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
  cancelBusinessConsoleMesWorkOrderMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
  completeBusinessConsoleMesOperationTaskMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
  confirmBusinessConsoleMesDowntimeRecoveryMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
  convertBusinessConsoleMesPlanToWorkOrderMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
  createBusinessConsoleMesFinishedGoodsReceiptRequestMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
  createBusinessConsoleMesMaterialIssueRequestMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
  createBusinessConsoleMesRushWorkOrderMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
  createBusinessConsoleMesShiftHandoverMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
  createBusinessConsoleSopFileDownloadGrantMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: {
        fileId: vars.path.fileId,
        downloadUrl: '/api/business-console/v1/files/download-grants/grant-sop/content',
        downloadHeaders: {
          'X-Organization-Id': vars.body.organizationId,
          'X-Environment-Id': vars.body.environmentId,
        },
      },
    })),
  })),
  getBusinessConsoleMesBatchTraceabilityQueryOptions: vi.fn(() => ({
    key: [{ _id: 'getBusinessConsoleMesBatchTraceability' }],
    query: vi.fn(),
  })),
  getBusinessConsoleMesCurrentOperationSopsQueryOptions: vi.fn(() => ({
    key: [{ _id: 'getBusinessConsoleMesCurrentOperationSops' }],
    query: vi.fn(),
  })),
  getBusinessConsoleMesCapacityImpactsQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleMesCapacityImpacts' }],
    query: vi.fn(),
  })),
  getBusinessConsoleMesFoundationReadinessQueryOptions: vi.fn(() => ({
    key: [{ _id: 'getBusinessConsoleMesFoundationReadiness' }],
    query: vi.fn(),
  })),
  getBusinessConsoleMesMaterialReadinessQueryOptions: vi.fn(() => ({
    key: [{ _id: 'getBusinessConsoleMesMaterialReadiness' }],
    query: vi.fn(),
  })),
  getBusinessConsoleMesMaterialLotTraceabilityQueryOptions: vi.fn(() => ({
    key: [{ _id: 'getBusinessConsoleMesMaterialLotTraceability' }],
    query: vi.fn(),
  })),
  getBusinessConsoleMesOverviewQueryOptions: vi.fn(() => ({
    key: [{ _id: 'getBusinessConsoleMesOverview' }],
    query: vi.fn(),
  })),
  getBusinessConsoleMesProductionPlanReadinessQueryOptions: vi.fn(() => ({
    key: [{ _id: 'getBusinessConsoleMesProductionPlanReadiness' }],
    query: vi.fn(),
  })),
  getBusinessConsoleMesWipSummaryQueryOptions: vi.fn(() => ({
    key: [{ _id: 'getBusinessConsoleMesWipSummary' }],
    query: vi.fn(),
  })),
  getBusinessConsoleMesWorkOrderDetailQueryOptions: vi.fn(() => ({
    key: [{ _id: 'getBusinessConsoleMesWorkOrderDetail' }],
    query: vi.fn(),
  })),
  getBusinessConsoleMesWorkOrderTraceabilityQueryOptions: vi.fn(() => ({
    key: [{ _id: 'getBusinessConsoleMesWorkOrderTraceability' }],
    query: vi.fn(),
  })),
  listBusinessConsoleMesDispatchTasksQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleMesDispatchTasks' }],
    query: vi.fn(),
  })),
  listBusinessConsoleMesDowntimeEventsQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleMesDowntimeEvents' }],
    query: vi.fn(),
  })),
  listBusinessConsoleMesCapacityImpactsQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleMesCapacityImpacts' }],
    query: vi.fn(),
  })),
  listBusinessConsoleMesFinishedGoodsReceiptRequestsQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleMesFinishedGoodsReceiptRequests' }],
    query: vi.fn(),
  })),
  listBusinessConsoleMesMaterialIssueRequestsQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleMesMaterialIssueRequests' }],
    query: vi.fn(),
  })),
  listBusinessConsoleMesOperationTasksQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleMesOperationTasks' }],
    query: vi.fn(),
  })),
  listBusinessConsoleMesProductionPlansQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleMesProductionPlans' }],
    query: vi.fn(),
  })),
  listBusinessConsoleMesProductionReportsQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleMesProductionReports' }],
    query: vi.fn(),
  })),
  listBusinessConsoleMesRelatedQualityItemsQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleMesRelatedQualityItems' }],
    query: vi.fn(),
  })),
  listBusinessConsoleMesShiftHandoversQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleMesShiftHandovers' }],
    query: vi.fn(),
  })),
  listBusinessConsoleMesWorkOrdersQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleMesWorkOrders' }],
    query: vi.fn(),
  })),
  pauseBusinessConsoleMesOperationTaskMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
  recordBusinessConsoleMesDefectMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
  recordBusinessConsoleMesDowntimeEventMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
  recordBusinessConsoleMesProductionReportMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
  releaseBusinessConsoleMesWorkOrderMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
  resumeBusinessConsoleMesOperationTaskMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
  runBusinessConsoleMesScheduleMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
  startBusinessConsoleMesOperationTaskMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars) => ({
      success: true,
      data: vars.body,
    })),
  })),
}))

vi.mock('@pinia/colada', () => ({
  useMutation: vi.fn((options) => ({
    error: shallowRef(),
    isLoading: shallowRef(false),
    mutateAsync: vi.fn(async (vars) => {
      const result = await options.mutation(vars)
      await options.onSuccess?.(result)
      return result
    }),
  })),
  useQuery: vi.fn((optionsFactory) => {
    const options = optionsFactory()
    const key = Array.isArray(options.key) ? options.key[0] : undefined
    const id = key && typeof key === 'object' && '_id' in key ? String(key._id) : ''
    coladaState.queryFactoriesById.set(id, optionsFactory)

    const refetch = vi.fn()
    coladaState.queryRefetchById.set(id, refetch)

    return {
      data: shallowRef(coladaState.queryDataById.get(id)),
      error: shallowRef(),
      isLoading: shallowRef(false),
      refetch,
    }
  }),
  useQueryCache: vi.fn(() => ({
    invalidateQueries: coladaState.invalidateQueries,
  })),
}))

describe('business MES composables', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    useBusinessContextStore().patchContext({ organizationId: 'org-001', environmentId: 'env-dev' })
    vi.clearAllMocks()
    coladaState.invalidateQueries.mockClear()
    coladaState.queryFactoriesById.clear()
    coladaState.queryDataById.clear()
    coladaState.queryRefetchById.clear()
  })

  it('maps backend MES readiness reason codes to shared labels and next steps', () => {
    expect(describeMesReadinessReason('QUALITY_PLAN_MISSING')).toMatchObject({
      code: 'QUALITY_PLAN_MISSING',
      label: '检验方案缺失',
      nextStep: '维护并启用 SKU 与工序检验方案后重新检查',
    })
    expect(describeMesReadinessReason('QUALITY_HOLD_ACTIVE')).toMatchObject({
      label: '质量冻结中',
      nextStep: '处理质量冻结、NCR 或放行状态后再执行',
    })
    expect(describeMesReadinessReason('EQUIPMENT_UNAVAILABLE')).toMatchObject({
      label: '设备不可用',
      nextStep: '处理报警/停机或改派可用设备',
    })
    expect(describeMesReadinessReason('EQUIPMENT_MAINTENANCE_CONFLICT')).toMatchObject({
      label: '维修占用冲突',
      nextStep: '调整维修窗口、等待释放或选择替代设备',
    })
  })

  it('lists work orders with default context and safe items', () => {
    coladaState.queryDataById.set('listBusinessConsoleMesWorkOrders', {
      success: true,
      data: {
        total: 128,
        items: [
          {
            workOrderId: 'wo-1',
            status: 'released',
          },
        ],
      },
    })

    const { workOrders, workOrdersTotal } = useMesWorkOrders()

    expect(listBusinessConsoleMesWorkOrdersQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        skip: 0,
        take: 100,
      },
    })
    expect(workOrdersTotal.value).toBe(128)
    expect(workOrders.value).toEqual([
      {
        workOrderId: 'wo-1',
        status: 'released',
      },
    ])
  })

  it('defaults work orders to an empty array for unsuccessful envelopes', () => {
    coladaState.queryDataById.set('listBusinessConsoleMesWorkOrders', {
      success: false,
    })

    const { workOrders } = useMesWorkOrders()

    expect(workOrders.value).toEqual([])
  })

  it('uses the latest business context store values for MES list queries', () => {
    const context = useBusinessContextStore()
    context.patchContext({ organizationId: 'org-mes-a', environmentId: 'env-mes-a' })

    const workOrders = useMesWorkOrders()
    workOrders.filters.keyword = 'filter'

    context.patchContext({ organizationId: 'org-mes-b', environmentId: 'env-mes-b' })
    coladaState.queryFactoriesById.get('listBusinessConsoleMesWorkOrders')?.()

    expect(listBusinessConsoleMesWorkOrdersQueryOptions).toHaveBeenLastCalledWith({
      query: {
        organizationId: 'org-mes-b',
        environmentId: 'env-mes-b',
        keyword: 'filter',
        skip: 0,
        take: 100,
      },
    })
  })

  it('disables MES list queries until business context is selected', () => {
    const context = useBusinessContextStore()
    context.patchContext({ organizationId: '', environmentId: '' })

    useMesWorkOrders()
    useMesOverview()
    useMesProductionPlans()
    useMesOperationTasks()
    useMesWipSummary()
    useMesCapacityImpacts()

    expect(
      coladaState.queryFactoriesById.get('listBusinessConsoleMesWorkOrders')?.(),
    ).toMatchObject({ enabled: false })
    expect(coladaState.queryFactoriesById.get('getBusinessConsoleMesOverview')?.()).toMatchObject({
      enabled: false,
    })
    expect(
      coladaState.queryFactoriesById.get('listBusinessConsoleMesProductionPlans')?.(),
    ).toMatchObject({ enabled: false })
    expect(
      coladaState.queryFactoriesById.get('listBusinessConsoleMesOperationTasks')?.(),
    ).toMatchObject({ enabled: false })
    const sops = useMesCurrentOperationSops()
    expect(
      coladaState.queryFactoriesById.get('getBusinessConsoleMesCurrentOperationSops')?.(),
    ).toMatchObject({ enabled: false })
    expect(sops.currentSops.value).toEqual([])
    expect(coladaState.queryFactoriesById.get('getBusinessConsoleMesWipSummary')?.()).toMatchObject(
      { enabled: false },
    )
    expect(
      coladaState.queryFactoriesById.get('listBusinessConsoleMesCapacityImpacts')?.(),
    ).toMatchObject({ enabled: false })
  })

  it('does not refetch MES lists when business context is empty', async () => {
    const context = useBusinessContextStore()
    context.patchContext({ organizationId: '', environmentId: '' })
    const workOrders = useMesWorkOrders()
    const refetch = coladaState.queryRefetchById.get('listBusinessConsoleMesWorkOrders')

    await workOrders.refreshWorkOrders()

    expect(refetch).not.toHaveBeenCalled()

    context.patchContext({ organizationId: 'org-mes', environmentId: 'env-mes' })
    await workOrders.refreshWorkOrders()

    expect(refetch).toHaveBeenCalledOnce()
  })

  it('submits rush work orders and production reports', async () => {
    const { createRushWorkOrder, recordProductionReport } = useMesWorkOrders()

    await createRushWorkOrder({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      workOrderId: 'wo-rush',
      skuId: 'sku-1',
      quantity: 10,
      dueUtc: '2026-05-24T00:00:00Z',
      workCenterId: 'wc-1',
      durationMinutes: 60,
    })
    await recordProductionReport({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      workOrderId: 'wo-rush',
      operationTaskId: 'op-1',
      goodQuantity: 8,
      scrapQuantity: 1,
      completesOperation: true,
      reportedAtUtc: '2026-05-24T01:00:00Z',
    })

    expect(createBusinessConsoleMesRushWorkOrderMutationOptions).toHaveBeenCalled()
    expect(
      vi.mocked(createBusinessConsoleMesRushWorkOrderMutationOptions).mock.results[0]?.value
        .mutation,
    ).toHaveBeenCalledWith({
      body: expect.objectContaining({
        workOrderId: 'wo-rush',
      }),
    })
    expect(recordBusinessConsoleMesProductionReportMutationOptions).toHaveBeenCalled()
    expect(
      vi.mocked(recordBusinessConsoleMesProductionReportMutationOptions).mock.results[0]?.value
        .mutation,
    ).toHaveBeenCalledWith({
      body: expect.objectContaining({
        operationTaskId: 'op-1',
      }),
    })
    expect(coladaState.invalidateQueries).toHaveBeenCalledTimes(8)
  })

  it('reads overview, foundation readiness, operation tasks, and WIP rows', () => {
    coladaState.queryDataById.set('getBusinessConsoleMesOverview', {
      success: true,
      data: {
        counts: [{ key: 'WorkOrders', count: 2 }],
        blockers: [{ areaCode: 'materials', code: 'SHORTAGE', message: '缺料', count: 1 }],
        pendingWork: [{ roleCode: 'planner', workType: 'dispatch', count: 3 }],
      },
    })
    coladaState.queryDataById.set('getBusinessConsoleMesFoundationReadiness', {
      success: true,
      data: {
        status: 'Ready',
        areas: [{ areaCode: 'master-data', status: 'Ready', issues: [] }],
      },
    })
    coladaState.queryDataById.set('listBusinessConsoleMesOperationTasks', {
      success: true,
      data: {
        total: 77,
        items: [{ operationTaskId: 'op-1', workOrderId: 'wo-1', status: 'Ready' }],
      },
    })
    coladaState.queryDataById.set('getBusinessConsoleMesWipSummary', {
      success: true,
      data: {
        total: 23,
        items: [{ workOrderId: 'wo-1', operationTaskId: 'op-1', goodQuantity: 5 }],
      },
    })

    const overview = useMesOverview()
    const readiness = useMesFoundationReadiness()
    const tasks = useMesOperationTasks()
    const wip = useMesWipSummary()

    expect(getBusinessConsoleMesOverviewQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
      },
    })
    expect(getBusinessConsoleMesFoundationReadinessQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
      },
    })
    expect(listBusinessConsoleMesOperationTasksQueryOptions).toHaveBeenCalled()
    expect(getBusinessConsoleMesWipSummaryQueryOptions).toHaveBeenCalled()
    expect(overview.counts.value).toHaveLength(1)
    expect(readiness.readiness.value?.status).toBe('Ready')
    expect(tasks.operationTasks.value).toHaveLength(1)
    expect(tasks.operationTasksTotal.value).toBe(77)
    expect(wip.wipRows.value).toHaveLength(1)
    expect(wip.wipTotal.value).toBe(23)
  })

  it('queries current SOP documents by operation and work center context', () => {
    coladaState.queryDataById.set('getBusinessConsoleMesCurrentOperationSops', {
      success: true,
      data: {
        items: [
          {
            documentNumber: 'SOP-ASSY',
            revision: 'B',
            operationCode: 'OP-ASSY',
            fileId: 'file-sop-b',
          },
        ],
      },
    })
    const sops = useMesCurrentOperationSops()
    sops.filters.operationCode = ' OP-ASSY '
    sops.filters.workCenterCode = ' WC-A '

    coladaState.queryFactoriesById.get('getBusinessConsoleMesCurrentOperationSops')?.()

    expect(getBusinessConsoleMesCurrentOperationSopsQueryOptions).toHaveBeenLastCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        operationCode: 'OP-ASSY',
        workCenterCode: 'WC-A',
      },
    })
    expect(
      coladaState.queryFactoriesById.get('getBusinessConsoleMesCurrentOperationSops')?.(),
    ).toMatchObject({ enabled: true })
    expect(sops.currentSops.value[0]).toMatchObject({ revision: 'B', fileId: 'file-sop-b' })
  })

  it('creates SOP file download grants through the generated mutation options', async () => {
    const sops = useMesCurrentOperationSops()

    const grant = await sops.createSopFileDownloadGrant('file-sop-b')

    expect(createBusinessConsoleSopFileDownloadGrantMutationOptions).toHaveBeenCalled()
    expect(grant).toMatchObject({
      fileId: 'file-sop-b',
      downloadUrl: '/api/business-console/v1/files/download-grants/grant-sop/content',
      downloadHeaders: {
        'X-Organization-Id': 'org-001',
        'X-Environment-Id': 'env-dev',
      },
    })
  })

  it('exposes secondary MES list totals from response envelopes', () => {
    const totals = new Map([
      ['listBusinessConsoleMesCapacityImpacts', 11],
      ['listBusinessConsoleMesDispatchTasks', 12],
      ['listBusinessConsoleMesDowntimeEvents', 13],
      ['listBusinessConsoleMesFinishedGoodsReceiptRequests', 14],
      ['listBusinessConsoleMesMaterialIssueRequests', 15],
      ['listBusinessConsoleMesProductionReports', 16],
      ['listBusinessConsoleMesRelatedQualityItems', 17],
      ['listBusinessConsoleMesShiftHandovers', 18],
    ])
    for (const [id, total] of totals) {
      coladaState.queryDataById.set(id, { success: true, data: { items: [], total } })
    }

    expect(useMesCapacityImpacts().capacityImpactsTotal.value).toBe(11)
    expect(useMesDispatchTasks().dispatchTasksTotal.value).toBe(12)
    expect(useMesDowntimeEvents().downtimeEventsTotal.value).toBe(13)
    expect(useMesFinishedGoodsReceipts().receiptRequestsTotal.value).toBe(14)
    expect(useMesMaterialIssueRequests().materialIssueRequestsTotal.value).toBe(15)
    expect(useMesProductionReports().productionReportsTotal.value).toBe(16)
    expect(useMesQualityContext().qualityItemsTotal.value).toBe(17)
    expect(useMesShiftHandovers().handoversTotal.value).toBe(18)
  })

  it('sends MES list search and structured filters as server query parameters', () => {
    const workOrders = useMesWorkOrders()
    workOrders.filters.keyword = 'filter'
    workOrders.filters.workCenterId = 'WC-FILTER'
    workOrders.filters.shiftId = 'SHIFT-FILTER'
    workOrders.filters.deviceAssetId = 'DEV-FILTER'
    workOrders.filters.status = 'Released'
    workOrders.filters.skip = 20
    workOrders.filters.take = 10

    coladaState.queryFactoriesById.get('listBusinessConsoleMesWorkOrders')?.()

    expect(listBusinessConsoleMesWorkOrdersQueryOptions).toHaveBeenLastCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        status: 'Released',
        keyword: 'filter',
        workCenterId: 'WC-FILTER',
        shiftId: 'SHIFT-FILTER',
        deviceAssetId: 'DEV-FILTER',
        skip: 20,
        take: 10,
      },
    })
  })

  it('sends MES production plan source and readiness filters as server query parameters', () => {
    const plans = useMesProductionPlans()
    plans.filters.keyword = 'sales'
    plans.filters.source = 'sales'
    plans.filters.readinessStatus = 'Ready'
    plans.filters.workCenterId = 'WC-FILTER'
    plans.filters.shiftId = 'SHIFT-FILTER'
    plans.filters.deviceAssetId = 'DEV-FILTER'
    plans.filters.skip = 5
    plans.filters.take = 25

    coladaState.queryFactoriesById.get('listBusinessConsoleMesProductionPlans')?.()

    expect(listBusinessConsoleMesProductionPlansQueryOptions).toHaveBeenLastCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        keyword: 'sales',
        workCenterId: 'WC-FILTER',
        shiftId: 'SHIFT-FILTER',
        deviceAssetId: 'DEV-FILTER',
        source: 'sales',
        readinessStatus: 'Ready',
        skip: 5,
        take: 25,
      },
    })
  })

  it('sends secondary MES list filters as server query parameters', () => {
    const cases = [
      {
        id: 'getBusinessConsoleMesWipSummary',
        options: getBusinessConsoleMesWipSummaryQueryOptions,
        composable: useMesWipSummary,
      },
      {
        id: 'listBusinessConsoleMesCapacityImpacts',
        options: listBusinessConsoleMesCapacityImpactsQueryOptions,
        composable: useMesCapacityImpacts,
      },
      {
        id: 'listBusinessConsoleMesDispatchTasks',
        options: listBusinessConsoleMesDispatchTasksQueryOptions,
        composable: useMesDispatchTasks,
      },
      {
        id: 'listBusinessConsoleMesFinishedGoodsReceiptRequests',
        options: listBusinessConsoleMesFinishedGoodsReceiptRequestsQueryOptions,
        composable: useMesFinishedGoodsReceipts,
      },
      {
        id: 'listBusinessConsoleMesMaterialIssueRequests',
        options: listBusinessConsoleMesMaterialIssueRequestsQueryOptions,
        composable: useMesMaterialIssueRequests,
      },
      {
        id: 'listBusinessConsoleMesDowntimeEvents',
        options: listBusinessConsoleMesDowntimeEventsQueryOptions,
        composable: useMesDowntimeEvents,
      },
      {
        id: 'listBusinessConsoleMesShiftHandovers',
        options: listBusinessConsoleMesShiftHandoversQueryOptions,
        composable: useMesShiftHandovers,
      },
      {
        id: 'listBusinessConsoleMesProductionReports',
        options: listBusinessConsoleMesProductionReportsQueryOptions,
        composable: useMesProductionReports,
      },
    ] as const

    for (const testCase of cases) {
      const result = testCase.composable()
      result.filters.keyword = 'filter'
      result.filters.workCenterId = 'WC-FILTER'
      result.filters.shiftId = 'SHIFT-FILTER'
      result.filters.deviceAssetId = 'DEV-FILTER'
      result.filters.skip = 5
      result.filters.take = 25

      coladaState.queryFactoriesById.get(testCase.id)?.()

      expect(testCase.options).toHaveBeenLastCalledWith({
        query: {
          organizationId: 'org-001',
          environmentId: 'env-dev',
          keyword: 'filter',
          workCenterId: 'WC-FILTER',
          shiftId: 'SHIFT-FILTER',
          deviceAssetId: 'DEV-FILTER',
          skip: 5,
          take: 25,
        },
      })
    }
  })

  it('creates finished goods receipt requests and invalidates dependent lists', async () => {
    const { createReceiptRequest } = useMesFinishedGoodsReceipts()

    await createReceiptRequest({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      workOrderId: 'wo-1',
      skuId: 'sku-1',
      quantity: 10,
      unitCost: 12.34,
      uomCode: 'EA',
      requestedAtUtc: '2026-05-26T00:00:00.000Z',
      idempotencyKey: 'receipt-1',
    })

    expect(createBusinessConsoleMesFinishedGoodsReceiptRequestMutationOptions).toHaveBeenCalled()
    expect(coladaState.invalidateQueries).toHaveBeenCalledTimes(2)
  })

  it('runs schedule mutations through the generated option', async () => {
    const { runSchedule } = useMesSchedules()

    await runSchedule({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      trigger: 'manual',
    })

    expect(runBusinessConsoleMesScheduleMutationOptions).toHaveBeenCalled()
    expect(
      vi.mocked(runBusinessConsoleMesScheduleMutationOptions).mock.results[0]?.value.mutation,
    ).toHaveBeenCalledWith({
      body: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        trigger: 'manual',
      },
    })
  })

  it('suppresses traceability queries when their required scope is empty', () => {
    const traceability = useMesTraceability()

    const workOrderOptions = coladaState.queryFactoriesById.get(
      'getBusinessConsoleMesWorkOrderTraceability',
    )?.()
    traceability.filters.mode = 'batch'
    const batchOptions = coladaState.queryFactoriesById.get(
      'getBusinessConsoleMesBatchTraceability',
    )?.()
    traceability.filters.mode = 'material-lot'
    const materialLotOptions = coladaState.queryFactoriesById.get(
      'getBusinessConsoleMesMaterialLotTraceability',
    )?.()

    expect(workOrderOptions).toMatchObject({ enabled: false })
    expect(batchOptions).toMatchObject({ enabled: false })
    expect(materialLotOptions).toMatchObject({ enabled: false })
    expect(getBusinessConsoleMesWorkOrderTraceabilityQueryOptions).not.toHaveBeenCalledWith(
      expect.objectContaining({ path: { workOrderId: 'WO-001' } }),
    )
    expect(getBusinessConsoleMesBatchTraceabilityQueryOptions).not.toHaveBeenCalledWith(
      expect.objectContaining({ path: { batchOrSerial: 'BATCH-001' } }),
    )
    expect(getBusinessConsoleMesMaterialLotTraceabilityQueryOptions).not.toHaveBeenCalledWith(
      expect.objectContaining({ path: { materialLotId: 'LOT-001' } }),
    )
  })

  it('cancels a work order with the reason payload and invalidates MES + inventory queries', async () => {
    const detail = useMesWorkOrderDetail()
    detail.filters.workOrderId = 'WO-CANCEL'

    await detail.cancelWorkOrder('计划取消：产线调整')

    expect(cancelBusinessConsoleMesWorkOrderMutationOptions).toHaveBeenCalled()
    expect(
      vi.mocked(cancelBusinessConsoleMesWorkOrderMutationOptions).mock.results[0]?.value.mutation,
    ).toHaveBeenCalledWith({
      path: { workOrderId: 'WO-CANCEL' },
      query: { organizationId: 'org-001', environmentId: 'env-dev' },
      body: { reason: '计划取消：产线调整' },
    })
    // 本域 9 键 + 跨域库存可用量 1 键（A1 §4.2 跨域刷新首个落地）
    expect(coladaState.invalidateQueries).toHaveBeenCalledTimes(10)
  })

  it('scopes the cancel compensation preview receipts to the current work order', () => {
    coladaState.queryDataById.set('listBusinessConsoleMesFinishedGoodsReceiptRequests', {
      success: true,
      data: {
        total: 2,
        items: [
          { receiptRequestId: 'fg-1', workOrderId: 'WO-CANCEL', receiptStatus: 'created' },
          { receiptRequestId: 'fg-2', workOrderId: 'WO-OTHER', receiptStatus: 'created' },
        ],
      },
    })

    const detail = useMesWorkOrderDetail()
    detail.filters.workOrderId = 'WO-CANCEL'
    detail.activateCancelPreview()

    expect(detail.finishedGoodsReceiptRequests.value).toEqual([
      { receiptRequestId: 'fg-1', workOrderId: 'WO-CANCEL', receiptStatus: 'created' },
    ])
  })

  it('scopes the cancel compensation material issue requests to the current work order', () => {
    coladaState.queryDataById.set('listBusinessConsoleMesMaterialIssueRequests', {
      success: true,
      data: {
        total: 3,
        items: [
          {
            requestId: 'MIR-1',
            workOrderId: 'WO-CANCEL',
            receivedQuantity: 30,
            status: 'Received',
          },
          {
            requestId: 'MIR-2',
            workOrderId: 'WO-CANCEL',
            receivedQuantity: 0,
            status: 'Requested',
          },
          { requestId: 'MIR-9', workOrderId: 'WO-OTHER', receivedQuantity: 5, status: 'Received' },
        ],
      },
    })

    const detail = useMesWorkOrderDetail()
    detail.filters.workOrderId = 'WO-CANCEL'
    detail.activateCancelPreview()

    // 领料申请是补偿预览的权威来源，且必须按当前工单过滤（不掺入 WO-OTHER）
    expect(detail.materialIssueRequests.value).toEqual([
      { requestId: 'MIR-1', workOrderId: 'WO-CANCEL', receivedQuantity: 30, status: 'Received' },
      { requestId: 'MIR-2', workOrderId: 'WO-CANCEL', receivedQuantity: 0, status: 'Requested' },
    ])
  })

  it('filters both cancel compensation lists server-side by the current work order', () => {
    const detail = useMesWorkOrderDetail()
    detail.filters.workOrderId = 'WO-CANCEL'
    detail.activateCancelPreview()

    // 重新求值 query 工厂，拿到当前 workOrderId 下发送给 facade 的查询参数（服务端过滤，避免组织级前 100 条截断漏报）
    coladaState.queryFactoriesById.get('listBusinessConsoleMesMaterialIssueRequests')?.()
    coladaState.queryFactoriesById.get('listBusinessConsoleMesFinishedGoodsReceiptRequests')?.()

    expect(listBusinessConsoleMesMaterialIssueRequestsQueryOptions).toHaveBeenCalledWith(
      expect.objectContaining({ query: expect.objectContaining({ workOrderId: 'WO-CANCEL' }) }),
    )
    expect(listBusinessConsoleMesFinishedGoodsReceiptRequestsQueryOptions).toHaveBeenCalledWith(
      expect.objectContaining({ query: expect.objectContaining({ workOrderId: 'WO-CANCEL' }) }),
    )
  })

  it('marks cancel preview ready only after both compensation lists return data', () => {
    const pending = useMesWorkOrderDetail()
    pending.filters.workOrderId = 'WO-CANCEL'
    pending.activateCancelPreview()
    // 两项列表都未返回数据 → 未就绪，破坏性确认按钮应被禁用（慢网/失败不允许在空数据上确认）
    expect(pending.cancelPreviewReady.value).toBe(false)

    coladaState.queryDataById.set('listBusinessConsoleMesFinishedGoodsReceiptRequests', {
      success: true,
      data: { total: 0, items: [] },
    })
    coladaState.queryDataById.set('listBusinessConsoleMesMaterialIssueRequests', {
      success: true,
      data: { total: 0, items: [] },
    })

    const ready = useMesWorkOrderDetail()
    ready.filters.workOrderId = 'WO-CANCEL'
    ready.activateCancelPreview()
    expect(ready.cancelPreviewReady.value).toBe(true)
  })
})
