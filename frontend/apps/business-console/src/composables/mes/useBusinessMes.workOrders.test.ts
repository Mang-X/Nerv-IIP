import {
  coladaState,
  workOrderDetailQuery,
  setupBusinessMesTests,
} from './useBusinessMesTestFixture'
import { expect, it } from 'vitest'
import { nextTick } from 'vue'
import { listBusinessConsoleMesWorkOrdersQueryOptions } from '@nerv-iip/api-client'
import {
  useMesCapacityImpacts,
  useMesCurrentOperationSops,
  useMesOperationTasks,
  useMesOverview,
  useMesProductionPlans,
  useMesWipSummary,
  useMesWorkOrderFacts,
  useMesWorkOrders,
} from '.'
import { useBusinessContextStore } from '@/stores/businessContext'
import { describe } from 'vitest'

describe('business MES composables', () => {
  setupBusinessMesTests()

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
        scopeKind: 'work-center',
        scopeId: 'WC-A',
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

  it('reads commercial facts for a completed historical work order absent from the candidate pool', async () => {
    const commercialSourceFacts = {
      status: 'resolved',
      salesOrders: [{ salesOrderNo: 'SO-HISTORY', customerCode: 'CUSTOMER-HISTORY' }],
    }
    workOrderDetailQuery.mockResolvedValue({
      success: true,
      data: {
        workOrderId: 'WO-COMPLETED',
        status: 'Completed',
        quantity: 100,
        completedQuantity: 100,
        operationTasks: [{ operationTaskId: 'OP-10', status: 'Completed' }],
        commercialSourceFacts,
      },
    })
    const facts = useMesWorkOrderFacts(() => ['WO-COMPLETED', 'WO-COMPLETED'])
    const response = await coladaState.queryFactoriesById.get('schedulingWorkOrderFacts')!().query!(
      {
        signal: new AbortController().signal,
      },
    )
    coladaState.queryDataRefById.get('schedulingWorkOrderFacts')!.value = response
    await nextTick()
    expect(facts.workOrders.value).toEqual([
      {
        workOrderId: 'WO-COMPLETED',
        executionStatus: 'Completed',
        quantity: 100,
        completedQuantity: 100,
        operationTasks: [{ operationTaskId: 'OP-10', status: 'Completed' }],
        commercialSourceFacts,
      },
    ])
    expect(workOrderDetailQuery).toHaveBeenCalledTimes(1)
  })

  it('applies a caller-owned initial page size before creating the work-order query', () => {
    useMesWorkOrders({ initialTake: 500 })

    expect(listBusinessConsoleMesWorkOrdersQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        scopeKind: 'work-center',
        scopeId: 'WC-A',
        skip: 0,
        take: 500,
      },
    })
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
        scopeKind: 'work-center',
        scopeId: 'WC-A',
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

  it('keeps the current WIP request disabled when the consuming page lacks operations permission', () => {
    useMesWipSummary(() => false)

    expect(coladaState.queryFactoriesById.get('getBusinessConsoleMesWipSummary')?.()).toMatchObject(
      { enabled: false },
    )
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

  it('does not manually refresh operation tasks before an authorized read scope is ready', async () => {
    coladaState.queryDataById.delete(
      'getBusinessConsolePrincipalWorkContext:business.mes.operations.read',
    )
    const operationTasks = useMesOperationTasks()
    const refetch = coladaState.queryRefetchById.get('listBusinessConsoleMesOperationTasks')

    await operationTasks.refreshOperationTasks()

    expect(refetch).not.toHaveBeenCalled()
    expect(operationTasks.operationListScopeReady.value).toBe(false)
    expect(operationTasks.operationListScopeMessage.value).toContain('正在核验当前作业范围')
  })
})
