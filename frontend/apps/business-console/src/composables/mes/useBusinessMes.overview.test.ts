import { coladaState, setupBusinessMesTests } from './useBusinessMesTestFixture'
import { expect, it } from 'vitest'
import {
  getBusinessConsoleMesCurrentOperationSopsQueryOptions,
  getBusinessConsoleMesFoundationReadinessQueryOptions,
  getBusinessConsoleMesOverviewQueryOptions,
  getBusinessConsoleMesWipSummaryQueryOptions,
  listBusinessConsoleMesOperationTasksQueryOptions,
} from '@nerv-iip/api-client'
import {
  useMesCapacityImpacts,
  useMesDispatchTasks,
  useMesDowntimeEvents,
  useMesFoundationReadiness,
  useMesFinishedGoodsReceipts,
  useMesCurrentOperationSops,
  useMesMaterialIssueRequests,
  useMesOperationTasks,
  useMesOverview,
  useMesProductionReports,
  useMesQualityContext,
  useMesShiftHandovers,
  useMesWipSummary,
} from '.'
import { describe } from 'vitest'

describe('business MES composables', () => {
  setupBusinessMesTests()

  it('reads overview, foundation readiness, operation tasks, and WIP rows', async () => {
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
    coladaState.queryDataRefById.get('listBusinessConsoleMesOperationTasks')!.value =
      await coladaState.queryFactoriesById.get('listBusinessConsoleMesOperationTasks')!().query!({
        signal: new AbortController().signal,
      })
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
    expect(tasks.operationTasksState.value).toBe('ready')
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
})
