import {
  coladaState,
  authState,
  reactiveAuthState,
  setupBusinessMesTests,
} from './useBusinessMesTestFixture'
import { expect, it, vi } from 'vitest'
import { nextTick } from 'vue'
import {
  listBusinessConsoleMesOperationTasks,
  listBusinessConsoleMesOperationTasksQueryOptions,
  listBusinessConsoleMesWorkOrdersQueryOptions,
} from '@nerv-iip/api-client'
import {
  useMesCurrentOperationSops,
  useMesOperationTasks,
  useMesProductionReports,
  useMesWorkOrders,
} from './useBusinessMes'
import { describe } from 'vitest'

describe('pda useBusinessMes composables', () => {
  setupBusinessMesTests()

  it('keeps list queries disabled when the principal has no org/env scope', () => {
    reactiveAuthState.principal = undefined

    useMesWorkOrders()
    useMesOperationTasks()
    useMesCurrentOperationSops()

    expect(listBusinessConsoleMesWorkOrdersQueryOptions).toHaveBeenCalledWith({
      query: expect.objectContaining({ organizationId: '', environmentId: '' }),
    })
    expect(coladaState.queryOptionsById.get('listBusinessConsoleMesWorkOrders')?.enabled).toBe(
      false,
    )
    expect(coladaState.queryOptionsById.get('listBusinessConsoleMesOperationTasks')?.enabled).toBe(
      false,
    )
    expect(
      coladaState.queryOptionsById.get('getBusinessConsoleMesCurrentOperationSops')?.enabled,
    ).toBe(false)
  })

  it('loads MES operation tasks in bounded pages and sends filters to the server', async () => {
    coladaState.queryDataById.set('listBusinessConsoleMesOperationTasks', {
      success: true,
      data: { items: [{ operationTaskId: 'ot-1' }], total: 2 },
    })
    vi.mocked(listBusinessConsoleMesOperationTasks).mockResolvedValueOnce({
      data: {
        success: true,
        data: { items: [{ operationTaskId: 'ot-2' }], total: 2 },
      },
    } as never)

    const result = useMesOperationTasks()
    ;(
      result.filters as typeof result.filters & {
        operationTaskId?: string
      }
    ).operationTaskId = 'ot-exact-beyond-first-page'
    result.filters.keyword = undefined
    coladaState.queryFactoriesById.get('listBusinessConsoleMesOperationTasks')?.()
    await nextTick()
    coladaState.queryDataRefById.get('listBusinessConsoleMesOperationTasks')!.value = {
      success: true,
      data: { items: [{ operationTaskId: 'ot-1' }], total: 2 },
    }
    await nextTick()
    await result.loadMore()

    expect(listBusinessConsoleMesOperationTasksQueryOptions).toHaveBeenCalledWith({
      query: expect.objectContaining({
        operationTaskId: 'ot-exact-beyond-first-page',
        skip: 0,
        take: 20,
      }),
    })
    expect(listBusinessConsoleMesOperationTasks).toHaveBeenCalledWith({
      query: expect.objectContaining({
        skip: 1,
        take: 20,
        operationTaskId: 'ot-exact-beyond-first-page',
      }),
      throwOnError: true,
    })
    expect(result.operationTasks.value.map((task) => task.operationTaskId)).toEqual([
      'ot-1',
      'ot-2',
    ])
  })

  it('does not manually refresh work-order, operation, or report lists without org/env scope', async () => {
    authState.principal = undefined

    const workOrders = useMesWorkOrders()
    const operationTasks = useMesOperationTasks()
    const reports = useMesProductionReports()
    await Promise.all([workOrders.refresh(), operationTasks.refresh(), reports.refresh()])

    expect(coladaState.refetchById.get('listBusinessConsoleMesWorkOrders')).not.toHaveBeenCalled()
    expect(
      coladaState.refetchById.get('listBusinessConsoleMesOperationTasks'),
    ).not.toHaveBeenCalled()
    expect(
      coladaState.refetchById.get('listBusinessConsoleMesProductionReports'),
    ).not.toHaveBeenCalled()
  })

  it('does not manually refresh work-order or operation lists before read scopes are ready', async () => {
    coladaState.queryDataById.delete(
      'getBusinessConsolePrincipalWorkContext:business.mes.work-orders.read',
    )
    coladaState.queryDataById.delete(
      'getBusinessConsolePrincipalWorkContext:business.mes.operations.read',
    )

    const workOrders = useMesWorkOrders()
    const operationTasks = useMesOperationTasks()
    await Promise.all([workOrders.refresh(), operationTasks.refresh()])

    expect(coladaState.refetchById.get('listBusinessConsoleMesWorkOrders')).not.toHaveBeenCalled()
    expect(
      coladaState.refetchById.get('listBusinessConsoleMesOperationTasks'),
    ).not.toHaveBeenCalled()
    expect(workOrders.workOrderReadScopeReady.value).toBe(false)
    expect(workOrders.workOrderReadScopeMessage.value).toContain('尚未选择已授权作业范围')
    expect(operationTasks.operationListScopeReady.value).toBe(false)
    expect(operationTasks.operationListScopeMessage.value).toContain('尚未选择已授权作业范围')
  })
})
