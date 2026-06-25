import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'
import { createPinia, setActivePinia } from 'pinia'

import {
  createBusinessConsoleMesFinishedGoodsReceiptRequestMutationOptions,
  createBusinessConsoleMesRushWorkOrderMutationOptions,
  getBusinessConsoleMesBatchTraceabilityQueryOptions,
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
  useMesWorkOrders,
} from './useBusinessMes'
import { useBusinessContextStore } from '@/stores/businessContext'

const coladaState = vi.hoisted(() => ({
  invalidateQueries: vi.fn(async () => undefined),
  queryFactoriesById: new Map<string, () => unknown>(),
  queryDataById: new Map<string, unknown>(),
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
  getBusinessConsoleMesBatchTraceabilityQueryOptions: vi.fn(() => ({
    key: [{ _id: 'getBusinessConsoleMesBatchTraceability' }],
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

    return {
      data: shallowRef(coladaState.queryDataById.get(id)),
      error: shallowRef(),
      isLoading: shallowRef(false),
      refetch: vi.fn(),
    }
  }),
  useQueryCache: vi.fn(() => ({
    invalidateQueries: coladaState.invalidateQueries,
  })),
}))

describe('business MES composables', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    coladaState.invalidateQueries.mockClear()
    coladaState.queryFactoriesById.clear()
    coladaState.queryDataById.clear()
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

    const workOrderOptions = coladaState.queryFactoriesById
      .get('getBusinessConsoleMesWorkOrderTraceability')?.()
    traceability.filters.mode = 'batch'
    const batchOptions = coladaState.queryFactoriesById
      .get('getBusinessConsoleMesBatchTraceability')?.()
    traceability.filters.mode = 'material-lot'
    const materialLotOptions = coladaState.queryFactoriesById
      .get('getBusinessConsoleMesMaterialLotTraceability')?.()

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
})
