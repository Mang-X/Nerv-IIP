import {
  coladaState,
  workOrderDetailQuery,
  setupBusinessMesTests,
} from './useBusinessMesTestFixture'
import { expect, it, vi } from 'vitest'
import {
  cancelBusinessConsoleMesWorkOrder,
  closeBusinessConsoleMesWorkOrderMutationOptions,
  getBusinessConsoleMesWorkOrderDetailQueryOptions,
  holdBusinessConsoleMesWorkOrder,
  recordBusinessConsoleMesEngineeringChangeDecisionMutationOptions,
  releaseBusinessConsoleMesWorkOrderMutationOptions,
} from '@nerv-iip/api-client'
import { useMesWorkOrderDetail, useMesWorkOrders } from './useBusinessMes'
import { describe } from 'vitest'

describe('business MES composables', () => {
  setupBusinessMesTests()

  it('cancels a work order with the reason payload and invalidates MES + inventory queries', async () => {
    const detail = useMesWorkOrderDetail()
    detail.filters.workOrderId = 'WO-CANCEL'

    await detail.cancelWorkOrder('计划取消：产线调整')

    expect(cancelBusinessConsoleMesWorkOrder).toHaveBeenCalledWith({
      path: { workOrderId: 'WO-CANCEL' },
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        scopeKind: 'work-center',
        scopeId: 'WC-A',
      },
      body: { reason: '计划取消：产线调整' },
      throwOnError: false,
    })
    // 本域 9 键 + 跨域库存可用量 1 键（A1 §4.2 跨域刷新首个落地）
    expect(coladaState.invalidateQueries).toHaveBeenCalledTimes(10)
  })

  it('uses one frozen manage scope for cancel readback and command when read/manage defaults differ', async () => {
    coladaState.queryDataById.set(
      'getBusinessConsolePrincipalWorkContext:business.mes.work-orders.read',
      {
        success: true,
        data: { selectedScope: { kind: 'work-center', id: 'WC-READ' } },
      },
    )
    coladaState.queryDataById.set(
      'getBusinessConsolePrincipalWorkContext:business.mes.work-orders.manage',
      {
        success: true,
        data: { selectedScope: { kind: 'work-center', id: 'WC-MANAGE' } },
      },
    )
    const detail = useMesWorkOrderDetail()
    detail.filters.workOrderId = 'WO-SCOPE'
    vi.mocked(getBusinessConsoleMesWorkOrderDetailQueryOptions).mockClear()

    await detail.cancelWorkOrder('范围核对')

    expect(getBusinessConsoleMesWorkOrderDetailQueryOptions).toHaveBeenCalledWith({
      path: { workOrderId: 'WO-SCOPE' },
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        scopeKind: 'work-center',
        scopeId: 'WC-MANAGE',
      },
    })
    expect(cancelBusinessConsoleMesWorkOrder).toHaveBeenCalledWith(
      expect.objectContaining({
        query: expect.objectContaining({
          scopeKind: 'work-center',
          scopeId: 'WC-MANAGE',
        }),
      }),
    )
  })

  it('holds a work order with the reason through the selected manage scope and refreshes MES reads', async () => {
    const detail = useMesWorkOrderDetail()
    detail.filters.workOrderId = 'WO-HOLD'
    vi.mocked(getBusinessConsoleMesWorkOrderDetailQueryOptions).mockClear()

    await detail.holdWorkOrder('设备异常，等待维修确认')

    expect(getBusinessConsoleMesWorkOrderDetailQueryOptions).toHaveBeenCalledWith({
      path: { workOrderId: 'WO-HOLD' },
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        scopeKind: 'work-center',
        scopeId: 'WC-A',
      },
    })
    expect(holdBusinessConsoleMesWorkOrder).toHaveBeenCalledWith({
      path: { workOrderId: 'WO-HOLD' },
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        scopeKind: 'work-center',
        scopeId: 'WC-A',
      },
      body: { reason: '设备异常，等待维修确认' },
      throwOnError: false,
    })
    const invalidationCalls = coladaState.invalidateQueries.mock.calls
    const expectedInvalidatedIds = [
      'getBusinessConsoleMesWorkOrderDetail',
      'listBusinessConsoleMesWorkOrders',
      'getBusinessConsoleMesOverview',
      'getBusinessConsoleMesWipSummary',
      'listBusinessConsoleMesOperationTasks',
      'listBusinessConsoleMesDispatchTasks',
    ]
    expect(
      expectedInvalidatedIds.filter((id) =>
        invalidationCalls.some(([options]) => options.predicate({ key: [{ _id: id }] })),
      ),
    ).toEqual(expectedInvalidatedIds)
    expect(invalidationCalls).toHaveLength(expectedInvalidatedIds.length)
  })

  it('rejects manual hold when the submit-time work-order status is terminal', async () => {
    workOrderDetailQuery.mockResolvedValueOnce({
      success: true,
      data: { workOrderId: 'WO-HOLD', status: 'Closed' },
    })
    const detail = useMesWorkOrderDetail()
    detail.filters.workOrderId = 'WO-HOLD'

    await expect(detail.holdWorkOrder('设备异常，等待维修确认')).rejects.toThrow(
      '状态已被其他操作更新',
    )

    expect(workOrderDetailQuery).toHaveBeenCalledTimes(1)
    expect(holdBusinessConsoleMesWorkOrder).not.toHaveBeenCalled()
    expect(coladaState.invalidateQueries).not.toHaveBeenCalled()
  })

  it('preflights and releases the exact work order through the selected manage scope', async () => {
    coladaState.queryDataById.set(
      'getBusinessConsolePrincipalWorkContext:business.mes.work-orders.read',
      {
        success: true,
        data: { selectedScope: { kind: 'work-center', id: 'WC-READ' } },
      },
    )
    coladaState.queryDataById.set(
      'getBusinessConsolePrincipalWorkContext:business.mes.work-orders.manage',
      {
        success: true,
        data: {
          principal: { id: 'user-001' },
          selectedScope: { kind: 'workshop', id: 'WS-MANAGE' },
          authorizedScopes: [
            { kind: 'workshop', id: 'WS-MANAGE' },
            { kind: 'work-center', id: 'WC-READ' },
          ],
          coveredWorkCenters: [{ id: 'WC-READ', workshopId: 'WS-MANAGE' }],
        },
      },
    )
    const workOrders = useMesWorkOrders()
    vi.mocked(getBusinessConsoleMesWorkOrderDetailQueryOptions).mockClear()

    await expect(workOrders.readWorkOrderForRelease('WO-RELEASE')).resolves.toMatchObject({
      workOrderId: 'WO-RELEASE',
      status: 'Created',
      productionVersionId: 'PV-1',
    })
    expect(getBusinessConsoleMesWorkOrderDetailQueryOptions).toHaveBeenCalledWith({
      path: { workOrderId: 'WO-RELEASE' },
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        scopeKind: 'work-center',
        scopeId: 'WC-READ',
      },
    })

    await workOrders.releaseWorkOrder('WO-RELEASE', {
      organizationId: 'org-001',
      environmentId: 'env-dev',
      confirmWarnings: true,
    })

    const mutation = vi
      .mocked(releaseBusinessConsoleMesWorkOrderMutationOptions)
      .mock.results.at(-1)?.value.mutation as ReturnType<typeof vi.fn>
    expect(mutation).toHaveBeenCalledWith(
      expect.objectContaining({
        path: { workOrderId: 'WO-RELEASE' },
        query: {
          organizationId: 'org-001',
          environmentId: 'env-dev',
          scopeKind: 'workshop',
          scopeId: 'WS-MANAGE',
        },
      }),
    )
    expect(getBusinessConsoleMesWorkOrderDetailQueryOptions).toHaveBeenCalledTimes(2)
  })

  it('does not release when the submit-time detail becomes blocked', async () => {
    coladaState.queryDataById.set(
      'getBusinessConsolePrincipalWorkContext:business.mes.work-orders.read',
      {
        success: true,
        data: { selectedScope: { kind: 'work-center', id: 'WC-READ' } },
      },
    )
    coladaState.queryDataById.set(
      'getBusinessConsolePrincipalWorkContext:business.mes.work-orders.manage',
      {
        success: true,
        data: {
          principal: { id: 'user-001' },
          selectedScope: { kind: 'workshop', id: 'WS-MANAGE' },
          authorizedScopes: [
            { kind: 'workshop', id: 'WS-MANAGE' },
            { kind: 'work-center', id: 'WC-READ' },
          ],
          coveredWorkCenters: [{ id: 'WC-READ', workshopId: 'WS-MANAGE' }],
        },
      },
    )
    const ready = {
      success: true,
      data: {
        workOrderId: 'WO-RELEASE',
        status: 'Created',
        productionVersionId: 'PV-1',
        operationTasks: [
          {
            status: 'queued',
            workCenterId: 'WC-READ',
            blockReasons: [],
            evaluatedAtUtc: '2026-08-25T00:00:00.000Z',
          },
        ],
        qualityHolds: [],
      },
    }
    const blocked = {
      success: true,
      data: {
        ...ready.data,
        operationTasks: [
          {
            ...ready.data.operationTasks[0],
            blockReasons: ['MATERIAL_REQUIREMENT_SNAPSHOT_MISSING: 工单缺少齐套需求快照'],
          },
        ],
      },
    }
    workOrderDetailQuery.mockResolvedValueOnce(ready).mockResolvedValueOnce(blocked)
    const workOrders = useMesWorkOrders()

    await expect(workOrders.readWorkOrderForRelease('WO-RELEASE')).resolves.toBeTruthy()
    await expect(
      workOrders.releaseWorkOrder('WO-RELEASE', {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        confirmWarnings: true,
      }),
    ).rejects.toThrow('齐套快照缺失')

    const mutation = vi
      .mocked(releaseBusinessConsoleMesWorkOrderMutationOptions)
      .mock.results.at(-1)?.value.mutation as ReturnType<typeof vi.fn>
    expect(workOrderDetailQuery).toHaveBeenCalledTimes(2)
    expect(mutation).not.toHaveBeenCalled()
  })

  it('rejects a target outside the selected manage scope before release', async () => {
    coladaState.queryDataById.set(
      'getBusinessConsolePrincipalWorkContext:business.mes.work-orders.read',
      {
        success: true,
        data: { selectedScope: { kind: 'work-center', id: 'WC-READ' } },
      },
    )
    coladaState.queryDataById.set(
      'getBusinessConsolePrincipalWorkContext:business.mes.work-orders.manage',
      {
        success: true,
        data: {
          principal: { id: 'user-001' },
          selectedScope: { kind: 'work-center', id: 'WC-OTHER' },
          authorizedScopes: [{ kind: 'work-center', id: 'WC-OTHER' }],
          coveredWorkCenters: [{ id: 'WC-OTHER', workshopId: 'WS-OTHER' }],
        },
      },
    )
    const workOrders = useMesWorkOrders()

    await expect(workOrders.readWorkOrderForRelease('WO-RELEASE')).rejects.toThrow(
      '不在所选管理范围',
    )

    const mutation = vi
      .mocked(releaseBusinessConsoleMesWorkOrderMutationOptions)
      .mock.results.at(-1)?.value.mutation as ReturnType<typeof vi.fn>
    expect(mutation).not.toHaveBeenCalled()
  })

  it('closes a work order and records an engineering decision through the manage scope', async () => {
    const detail = useMesWorkOrderDetail()
    detail.filters.workOrderId = 'WO-1953'

    await detail.closeWorkOrder()
    await detail.recordEngineeringChangeDecision({
      changeNumber: 'ECO-1953-001',
      decision: 'continue-with-archived-version',
      reason: '当前批次继续使用现行版本，待下批次切换。',
    })

    expect(
      vi.mocked(closeBusinessConsoleMesWorkOrderMutationOptions).mock.results.at(-1)?.value
        .mutation,
    ).toHaveBeenCalledWith(
      expect.objectContaining({
        path: { workOrderId: 'WO-1953' },
        query: expect.objectContaining({ scopeKind: 'work-center', scopeId: 'WC-A' }),
        body: {},
      }),
    )
    expect(
      vi
        .mocked(recordBusinessConsoleMesEngineeringChangeDecisionMutationOptions)
        .mock.results.at(-1)?.value.mutation,
    ).toHaveBeenCalledWith(
      expect.objectContaining({
        path: { workOrderId: 'WO-1953' },
        body: {
          changeNumber: 'ECO-1953-001',
          decision: 'continue-with-archived-version',
          reason: '当前批次继续使用现行版本，待下批次切换。',
        },
      }),
    )
  })
})
