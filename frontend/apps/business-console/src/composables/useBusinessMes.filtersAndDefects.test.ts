import { coladaState, setupBusinessMesTests } from './useBusinessMesTestFixture'
import { expect, it, vi } from 'vitest'
import {
  getBusinessConsoleMesWipSummaryQueryOptions,
  listBusinessConsoleMesCapacityImpactsQueryOptions,
  listBusinessConsoleMesDispatchTasksQueryOptions,
  listBusinessConsoleMesFinishedGoodsReceiptRequestsQueryOptions,
  listBusinessConsoleMesMaterialIssueRequestsQueryOptions,
  listBusinessConsoleMesProductionPlansQueryOptions,
  listBusinessConsoleMesProductionReportsQueryOptions,
  listBusinessConsoleMesShiftHandoversQueryOptions,
  listBusinessConsoleMesWorkOrdersQueryOptions,
  recordBusinessConsoleMesDefectV2MutationOptions,
} from '@nerv-iip/api-client'
import {
  useMesCapacityImpacts,
  useMesDispatchTasks,
  useMesFinishedGoodsReceipts,
  useMesMaterialIssueRequests,
  useMesProductionPlans,
  useMesProductionReports,
  useMesQualityContext,
  useMesShiftHandovers,
  useMesWipSummary,
  useMesWorkOrders,
} from './useBusinessMes'
import { useBusinessContextStore } from '@/stores/businessContext'
import { describe } from 'vitest'

describe('business MES composables', () => {
  setupBusinessMesTests()

  it('injects the current business context into the defect wire body and rejects caller overrides', async () => {
    const quality = useMesQualityContext()
    const body = {
      organizationId: 'forged-org',
      environmentId: 'forged-env',
      workOrderId: 'WO-2',
      operationTaskId: 'OP-2',
      defectCode: 'SCRATCH',
      quantity: 2.5,
      recordedAtUtc: '2026-08-26T01:02:03.000Z',
      idempotencyKey: 'defect-key',
      actor: 'forged-user',
      scopeKind: 'work-center',
      scopeId: 'WC-2',
    }

    await quality.recordDefect(body as never)

    const mutation = vi.mocked(recordBusinessConsoleMesDefectV2MutationOptions).mock.results.at(-1)
      ?.value.mutation as ReturnType<typeof vi.fn>
    expect(mutation).toHaveBeenCalledWith({
      body: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        workOrderId: 'WO-2',
        operationTaskId: 'OP-2',
        defectCode: 'SCRATCH',
        quantity: 2.5,
        recordedAtUtc: '2026-08-26T01:02:03.000Z',
        idempotencyKey: 'defect-key',
        scopeKind: 'work-center',
        scopeId: 'WC-2',
      },
    })
    expect(mutation.mock.calls[0]?.[0].body).not.toHaveProperty('actor')
    const invalidationCalls = coladaState.invalidateQueries.mock.calls
    const invalidatedIds = [
      'listBusinessConsoleMesRelatedQualityItems',
      'listBusinessConsoleMesOperationTasks',
      'listBusinessConsoleMesWorkOrders',
      'getBusinessConsoleMesWorkOrderDetail',
    ].filter((id) =>
      invalidationCalls.some(([options]) => options.predicate({ key: [{ _id: id }] })),
    )
    expect(invalidatedIds).toEqual([
      'listBusinessConsoleMesRelatedQualityItems',
      'listBusinessConsoleMesOperationTasks',
      'listBusinessConsoleMesWorkOrders',
      'getBusinessConsoleMesWorkOrderDetail',
    ])
  })

  it('fails closed before defect mutation when the current business context is missing', async () => {
    useBusinessContextStore().patchContext({ organizationId: '', environmentId: '' })
    const quality = useMesQualityContext()

    await expect(
      quality.recordDefect({
        workOrderId: 'WO-2',
        defectCode: 'SCRATCH',
        quantity: 2.5,
        recordedAtUtc: '2026-08-26T01:02:03.000Z',
        idempotencyKey: 'defect-key',
        scopeKind: 'work-center',
        scopeId: 'WC-2',
      }),
    ).rejects.toThrow('尚未确定当前组织')

    const mutation = vi.mocked(recordBusinessConsoleMesDefectV2MutationOptions).mock.results.at(-1)
      ?.value.mutation as ReturnType<typeof vi.fn>
    expect(mutation).not.toHaveBeenCalled()
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
        scopeKind: 'work-center',
        scopeId: 'WC-A',
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
})
