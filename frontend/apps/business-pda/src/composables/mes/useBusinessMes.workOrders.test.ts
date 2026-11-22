import { coladaState, reactiveAuthState, setupBusinessMesTests } from './useBusinessMesTestFixture'
import { expect, it } from 'vitest'
import { nextTick, shallowRef } from 'vue'
import {
  getBusinessConsoleMesWorkOrderDetailQueryOptions,
  listBusinessConsoleMesWorkOrdersQueryOptions,
} from '@nerv-iip/api-client'
import {
  useMesOperationTasks,
  useMesProductionReports,
  useMesWorkOrderDetail,
  useMesWorkOrders,
} from '.'
import { describe } from 'vitest'

describe('pda useBusinessMes composables', () => {
  setupBusinessMesTests()

  it('enables list queries once a principal scope is present', () => {
    useMesWorkOrders()

    expect(listBusinessConsoleMesWorkOrdersQueryOptions).toHaveBeenCalledWith({
      query: expect.objectContaining({ organizationId: 'org-001', environmentId: 'env-dev' }),
    })
    expect(coladaState.queryOptionsById.get('listBusinessConsoleMesWorkOrders')?.enabled).toBe(true)
  })

  it('exposes failed or malformed work-order, operation, and report responses', () => {
    coladaState.queryDataById.set('listBusinessConsoleMesWorkOrders', {
      success: false,
      message: '工单查询失败',
    })
    coladaState.queryDataById.set('listBusinessConsoleMesOperationTasks', [])
    coladaState.queryDataById.set('listBusinessConsoleMesProductionReports', {
      data: { items: [], total: 0 },
    })

    const workOrders = useMesWorkOrders()
    const operationTasks = useMesOperationTasks()
    const reports = useMesProductionReports()

    expect(workOrders.workOrders.value).toEqual([])
    expect(workOrders.total.value).toBe(0)
    expect(workOrders.hasSuccessfulResponse.value).toBe(false)
    expect(workOrders.hasFailedResponse.value).toBe(true)
    expect(operationTasks.operationTasks.value).toEqual([])
    expect(operationTasks.total.value).toBe(0)
    expect(operationTasks.hasSuccessfulResponse.value).toBe(false)
    expect(operationTasks.hasFailedResponse.value).toBe(true)
    expect(reports.productionReports.value).toEqual([])
    expect(reports.total.value).toBe(0)
    expect(reports.hasSuccessfulResponse.value).toBe(false)
    expect(reports.hasFailedResponse.value).toBe(true)
  })

  it('unbinds work-order, operation, and report projections on an org/env scope switch', async () => {
    for (const id of [
      'listBusinessConsoleMesWorkOrders',
      'listBusinessConsoleMesOperationTasks',
      'listBusinessConsoleMesProductionReports',
    ]) {
      coladaState.queryDataById.set(id, {
        success: true,
        data: { items: [{ id: `old-${id}` }], total: 4 },
      })
    }

    const workOrders = useMesWorkOrders()
    const operationTasks = useMesOperationTasks()
    const reports = useMesProductionReports()
    expect(workOrders.workOrders.value).toHaveLength(1)
    expect(operationTasks.operationTasks.value).toHaveLength(1)
    expect(reports.productionReports.value).toHaveLength(1)

    reactiveAuthState.principal = { organizationId: 'org-002', environmentId: 'env-prod' }
    await nextTick()

    expect(workOrders.workOrders.value).toEqual([])
    expect(workOrders.total.value).toBe(0)
    expect(workOrders.hasSuccessfulResponse.value).toBe(false)
    expect(operationTasks.operationTasks.value).toEqual([])
    expect(operationTasks.total.value).toBe(0)
    expect(operationTasks.hasSuccessfulResponse.value).toBe(false)
    expect(reports.productionReports.value).toEqual([])
    expect(reports.total.value).toBe(0)
    expect(reports.hasSuccessfulResponse.value).toBe(false)
  })

  it('uses the exact strong-ID work-order detail query for report route identity', () => {
    const workOrderId = shallowRef('WO-OUTSIDE-101')
    useMesWorkOrderDetail(workOrderId)

    expect(getBusinessConsoleMesWorkOrderDetailQueryOptions).toHaveBeenCalledWith({
      path: { workOrderId: 'WO-OUTSIDE-101' },
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        scopeKind: 'work-center',
        scopeId: 'WC-A',
      },
    })
    expect(coladaState.queryOptionsById.get('getBusinessConsoleMesWorkOrderDetail')?.enabled).toBe(
      true,
    )
  })

  it('exposes success:false and malformed work-order detail responses as retryable failures', async () => {
    coladaState.queryDataById.set('getBusinessConsoleMesWorkOrderDetail', {
      success: false,
      message: '工单详情查询失败',
    })
    const result = useMesWorkOrderDetail(shallowRef('WO-501'))

    expect(result.workOrder.value).toBeUndefined()
    expect(result.hasSuccessfulResponse.value).toBe(false)
    expect(result.hasFailedResponse.value).toBe(true)
    expect(result.error.value).toBeInstanceOf(Error)
    expect((result.error.value as Error).message).toBe('工单详情查询失败')

    coladaState.queryDataRefById.get('getBusinessConsoleMesWorkOrderDetail')!.value = {
      success: true,
      data: null,
    }
    await nextTick()

    expect(result.workOrder.value).toBeUndefined()
    expect(result.hasSuccessfulResponse.value).toBe(false)
    expect(result.hasFailedResponse.value).toBe(true)
    expect((result.error.value as Error).message).toBe('工单详情响应无效，请重试。')

    coladaState.queryDataRefById.get('getBusinessConsoleMesWorkOrderDetail')!.value = [
      { workOrderId: 'WO-501' },
    ]
    await nextTick()

    expect(result.workOrder.value).toBeUndefined()
    expect(result.hasSuccessfulResponse.value).toBe(false)
    expect(result.hasFailedResponse.value).toBe(true)
    expect((result.error.value as Error).message).toBe('工单详情响应无效，请重试。')
  })

  it.each([
    ['missing required fields', { workOrderId: 'WO-501' }],
    [
      'non-array operation tasks',
      {
        workOrderId: 'WO-501',
        skuId: 'SKU-501',
        quantity: 1,
        status: 'Released',
        readinessStatus: 'ready',
        blockingReasons: [],
        operationTasks: {},
      },
    ],
    [
      'non-canonical work-order identity',
      {
        workOrderId: ' WO-501 ',
        skuId: 'SKU-501',
        quantity: 1,
        status: 'Released',
        readinessStatus: 'ready',
        blockingReasons: [],
        operationTasks: [],
      },
    ],
  ])('rejects a success:true detail payload with %s', (_name, data) => {
    coladaState.queryDataById.set('getBusinessConsoleMesWorkOrderDetail', {
      success: true,
      data,
    })

    const result = useMesWorkOrderDetail(shallowRef('WO-501'))

    expect(result.workOrder.value).toBeUndefined()
    expect(result.hasSuccessfulResponse.value).toBe(false)
    expect(result.hasFailedResponse.value).toBe(true)
    expect((result.error.value as Error).message).toBe('工单详情响应无效，请重试。')
  })

  it('unbinds work-order detail when the work-order or org/env identity changes', async () => {
    coladaState.queryDataById.set('getBusinessConsoleMesWorkOrderDetail', {
      success: true,
      data: {
        workOrderId: 'WO-A',
        skuId: 'SKU-A',
        quantity: 1,
        status: 'Released',
        readinessStatus: 'ready',
        blockingReasons: [],
        operationTasks: [],
      },
    })
    const workOrderId = shallowRef('WO-A')
    const result = useMesWorkOrderDetail(workOrderId)

    expect(result.workOrder.value?.workOrderId).toBe('WO-A')
    expect(result.hasSuccessfulResponse.value).toBe(true)

    workOrderId.value = 'WO-B'
    await nextTick()
    expect(result.workOrder.value).toBeUndefined()
    expect(result.hasSuccessfulResponse.value).toBe(false)
    expect(result.hasFailedResponse.value).toBe(false)

    coladaState.queryDataRefById.get('getBusinessConsoleMesWorkOrderDetail')!.value = {
      success: true,
      data: {
        workOrderId: 'WO-A',
        skuId: 'SKU-A',
        quantity: 1,
        status: 'Released',
        readinessStatus: 'ready',
        blockingReasons: [],
        operationTasks: [],
      },
    }
    await nextTick()
    expect(result.workOrder.value).toBeUndefined()
    expect(result.hasSuccessfulResponse.value).toBe(false)
    expect(result.hasFailedResponse.value).toBe(false)

    coladaState.queryDataRefById.get('getBusinessConsoleMesWorkOrderDetail')!.value = {
      success: true,
      data: {
        workOrderId: 'WO-B',
        skuId: 'SKU-B',
        quantity: 2,
        status: 'Released',
        readinessStatus: 'ready',
        blockingReasons: [],
        operationTasks: [],
      },
    }
    await nextTick()
    expect(result.workOrder.value?.workOrderId).toBe('WO-B')

    reactiveAuthState.principal = { organizationId: 'org-002', environmentId: 'env-prod' }
    await nextTick()
    expect(result.workOrder.value).toBeUndefined()
    expect(result.hasSuccessfulResponse.value).toBe(false)
    expect(result.hasFailedResponse.value).toBe(false)
  })
})
