import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'

import {
  createBusinessConsoleMesFinishedGoodsReceiptRequestMutationOptions,
  createBusinessConsoleMesRushWorkOrderMutationOptions,
  getBusinessConsoleMesFoundationReadinessQueryOptions,
  getBusinessConsoleMesOverviewQueryOptions,
  getBusinessConsoleMesWipSummaryQueryOptions,
  listBusinessConsoleMesOperationTasksQueryOptions,
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
  useMesProductionReports,
  useMesQualityContext,
  useMesSchedules,
  useMesShiftHandovers,
  useMesWipSummary,
  useMesWorkOrders,
} from './useBusinessMes'

const coladaState = vi.hoisted(() => ({
  invalidateQueries: vi.fn(async () => undefined),
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
    vi.clearAllMocks()
    coladaState.invalidateQueries.mockClear()
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

  it('creates finished goods receipt requests and invalidates dependent lists', async () => {
    const { createReceiptRequest } = useMesFinishedGoodsReceipts()

    await createReceiptRequest({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      workOrderId: 'wo-1',
      skuId: 'sku-1',
      quantity: 10,
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
})
