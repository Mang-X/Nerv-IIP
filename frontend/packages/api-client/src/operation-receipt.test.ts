import { describe, expect, it, vi } from 'vitest'
import {
  BusinessOperationUnconfirmedError,
  confirmBusinessConsoleOperation,
  readBusinessConsoleOperationState,
  verifyBusinessConsoleOperationReadback,
} from './operation-receipt'
import {
  listBusinessConsoleWmsCountExecutions,
  listBusinessConsoleWmsOutboundOrders,
} from './generated/business-console/sdk.gen'

vi.mock('./generated/business-console/sdk.gen', () => ({
  getBusinessConsoleMesProductionReport: vi.fn(),
  listBusinessConsoleEquipmentAlarms: vi.fn(),
  listBusinessConsoleWmsCountExecutions: vi.fn(),
  listBusinessConsoleWmsInboundOrders: vi.fn(),
  listBusinessConsoleWmsOutboundOrders: vi.fn(),
}))

function common(operationType: string, resourceId: string) {
  return {
    operationType,
    authority: 'business-gateway',
    resourceType: 'business-resource',
    resourceId,
    idempotencyKey: `idem:${operationType}:${resourceId}`,
  }
}

function confirmed(operationType: string, resourceId: string) {
  return {
    ...common(operationType, resourceId),
    outcome: 'confirmed',
    stateConfirmed: true,
    readbackRequired: false,
    changedAtUtc: '2026-07-28T03:00:00Z',
    resourceStatus: 'completed',
  }
}

function accepted(
  operationType: string,
  resourceId: string,
  readbackPath = '/api/business-console/v1/readback',
) {
  return {
    success: true,
    data: {
      operationReceipt: {
        ...common(operationType, resourceId),
        outcome: 'accepted',
        stateConfirmed: false,
        readbackRequired: true,
        readbackMethod: 'GET',
        readbackPath,
      },
    },
  }
}

describe('business operation receipt confirmation', () => {
  it('resolves a server-confirmed receipt without readback', async () => {
    const readback = vi.fn()
    const envelope = {
      success: true,
      data: {
        workOrderId: 'work-order-1',
        operationReceipt: confirmed('maintenance.work-order.create', 'work-order-1'),
      },
    }

    await expect(
      confirmBusinessConsoleOperation(envelope, {
        expectedOperationType: 'maintenance.work-order.create',
        expectedIdempotencyKey: 'idem:maintenance.work-order.create:work-order-1',
        expectedResourceIdSelector: (value) => value.data?.workOrderId,
        readback,
      }),
    ).resolves.toBe(envelope)
    expect(readback).not.toHaveBeenCalled()
  })

  it('fails closed when the receipt idempotency key belongs to an older intent', async () => {
    const envelope = {
      success: true,
      data: {
        operationReceipt: confirmed('maintenance.work-order.create', 'work-order-1'),
      },
    }

    await expect(
      confirmBusinessConsoleOperation(envelope, {
        expectedOperationType: 'maintenance.work-order.create',
        expectedResourceId: 'work-order-1',
        expectedIdempotencyKey: 'current-intent-key',
      } as never),
    ).rejects.toBeInstanceOf(BusinessOperationUnconfirmedError)
  })

  it('rejects a confirmed receipt without authoritative resource identity', async () => {
    const envelope = {
      success: true,
      data: {
        operationReceipt: {
          ...confirmed('maintenance.work-order.create', 'work-order-1'),
          resourceId: '',
        },
      },
    }

    await expect(
      confirmBusinessConsoleOperation(envelope, {
        expectedOperationType: 'maintenance.work-order.create',
        expectedIdempotencyKey: 'idem:maintenance.work-order.create:work-order-1',
        expectedResourceId: 'work-order-1',
      }),
    ).rejects.toBeInstanceOf(BusinessOperationUnconfirmedError)
  })

  it.each(['confirmed', 'accepted'])(
    'fails closed when a %s receipt operation type does not match the caller expectation',
    async (outcome) => {
      const envelope =
        outcome === 'confirmed'
          ? {
              success: true,
              data: {
                operationReceipt: confirmed('maintenance.work-order.complete', 'work-order-1'),
              },
            }
          : accepted('maintenance.work-order.complete', 'work-order-1')
      const readback = vi.fn()

      await expect(
        confirmBusinessConsoleOperation(envelope, {
          expectedOperationType: 'maintenance.work-order.create',
          expectedIdempotencyKey: 'idem:maintenance.work-order.complete:work-order-1',
          expectedResourceId: 'work-order-1',
          readback,
        }),
      ).rejects.toBeInstanceOf(BusinessOperationUnconfirmedError)
      expect(readback).not.toHaveBeenCalled()
    },
  )

  it.each(['confirmed', 'accepted'])(
    'fails closed when a %s receipt resource does not match the caller envelope selector',
    async (outcome) => {
      const envelope = {
        success: true,
        data: {
          workOrderId: 'work-order-from-envelope',
          operationReceipt:
            outcome === 'confirmed'
              ? confirmed('maintenance.work-order.create', 'different-work-order')
              : accepted('maintenance.work-order.create', 'different-work-order').data
                  .operationReceipt,
        },
      }
      const readback = vi.fn()

      await expect(
        confirmBusinessConsoleOperation(envelope, {
          expectedOperationType: 'maintenance.work-order.create',
          expectedIdempotencyKey: 'idem:maintenance.work-order.create:different-work-order',
          expectedResourceIdSelector: (value) => value.data?.workOrderId,
          readback,
        }),
      ).rejects.toBeInstanceOf(BusinessOperationUnconfirmedError)
      expect(readback).not.toHaveBeenCalled()
    },
  )

  it('fails closed when the caller resource selector cannot resolve the created resource', async () => {
    const envelope = {
      success: true,
      data: {
        operationReceipt: confirmed('quality.inspection-task.submit', 'inspection-record-1'),
      },
    }

    await expect(
      confirmBusinessConsoleOperation(envelope, {
        expectedOperationType: 'quality.inspection-task.submit',
        expectedIdempotencyKey: 'idem:quality.inspection-task.submit:inspection-record-1',
        expectedResourceIdSelector: (value) =>
          (value.data as { inspectionRecordId?: string })?.inspectionRecordId,
      }),
    ).rejects.toBeInstanceOf(BusinessOperationUnconfirmedError)
  })

  it.each(['authority', 'resourceType', 'idempotencyKey'] as const)(
    'fails closed when a receipt omits required common field %s',
    async (field) => {
      const operationReceipt: Record<string, unknown> = {
        ...confirmed('maintenance.work-order.create', 'work-order-1'),
      }
      delete operationReceipt[field]

      await expect(
        confirmBusinessConsoleOperation(
          { success: true, data: { workOrderId: 'work-order-1', operationReceipt } },
          {
            expectedOperationType: 'maintenance.work-order.create',
            expectedIdempotencyKey: 'idem:maintenance.work-order.create:work-order-1',
            expectedResourceId: 'work-order-1',
          },
        ),
      ).rejects.toBeInstanceOf(BusinessOperationUnconfirmedError)
    },
  )

  it.each(['changedAtUtc', 'resourceStatus'] as const)(
    'fails closed when a confirmed receipt omits %s',
    async (field) => {
      const operationReceipt: Record<string, unknown> = {
        ...confirmed('maintenance.work-order.create', 'work-order-1'),
      }
      delete operationReceipt[field]

      await expect(
        confirmBusinessConsoleOperation(
          { success: true, data: { operationReceipt } },
          {
            expectedOperationType: 'maintenance.work-order.create',
            expectedIdempotencyKey: 'idem:maintenance.work-order.create:work-order-1',
            expectedResourceId: 'work-order-1',
          },
        ),
      ).rejects.toBeInstanceOf(BusinessOperationUnconfirmedError)
    },
  )

  it.each([
    ['changedAtUtc', 'not-a-date'],
    ['resourceStatus', '   '],
  ] as const)(
    'fails closed when an accepted receipt has invalid optional %s',
    async (field, value) => {
      const envelope = accepted('wms.count-execution.complete', 'count-1')
      const operationReceipt = envelope.data.operationReceipt as Record<string, unknown>
      operationReceipt[field] = value

      await expect(
        confirmBusinessConsoleOperation(envelope, {
          expectedOperationType: 'wms.count-execution.complete',
          expectedIdempotencyKey: 'idem:wms.count-execution.complete:count-1',
          expectedResourceId: 'count-1',
        }),
      ).rejects.toBeInstanceOf(BusinessOperationUnconfirmedError)
    },
  )

  it('reads an accepted count receipt until the authoritative inventory movement is posted', async () => {
    const envelope = accepted('wms.count-execution.complete', 'count-1')
    const readback = vi
      .fn()
      .mockResolvedValueOnce({
        success: true,
        data: {
          items: [
            {
              countExecutionId: 'count-1',
              status: 'completed',
              inventoryPostingStatus: 'pending',
            },
          ],
        },
      })
      .mockResolvedValueOnce({
        success: true,
        data: {
          items: [
            {
              countExecutionId: 'count-1',
              status: 'completed',
              inventoryPostingStatus: 'posted',
            },
          ],
        },
      })

    await expect(
      confirmBusinessConsoleOperation(envelope, {
        expectedOperationType: 'wms.count-execution.complete',
        expectedIdempotencyKey: 'idem:wms.count-execution.complete:count-1',
        expectedResourceId: 'count-1',
        readback,
        retryDelayMs: 0,
      }),
    ).resolves.toBe(envelope)
    expect(readback).toHaveBeenCalledTimes(2)
  })

  it('surfaces a failed count movement and keeps completed-without-posting indeterminate', async () => {
    expect(
      verifyBusinessConsoleOperationReadback(
        { operationType: 'wms.count-execution.complete', resourceId: 'count-1' },
        {
          success: true,
          data: {
            items: [
              {
                countExecutionId: 'count-1',
                status: 'completed',
                inventoryPostingStatus: 'failed',
                inventoryPostingFailureCode: 'NEGATIVE_ON_HAND',
                inventoryPostingFailureMessage:
                  'Stock movement would make on-hand quantity negative.',
              },
            ],
          },
        },
      ),
    ).toMatchObject({
      state: 'confirmed-business-failure',
      failureCode: 'NEGATIVE_ON_HAND',
    })
    expect(
      verifyBusinessConsoleOperationReadback(
        { operationType: 'wms.count-execution.complete', resourceId: 'count-1' },
        {
          success: true,
          data: { items: [{ countExecutionId: 'count-1', status: 'completed' }] },
        },
      ),
    ).toEqual({ state: 'indeterminate' })
  })

  it('confirms WMS inbound completion when the order is waiting for quality release', async () => {
    const envelope = accepted('wms.inbound-order.complete', 'in-1')
    const readback = vi.fn().mockResolvedValue({
      success: true,
      data: { items: [{ inboundOrderId: 'in-1', status: 'PendingQualityCheck' }] },
    })

    await expect(
      confirmBusinessConsoleOperation(envelope, {
        expectedOperationType: 'wms.inbound-order.complete',
        expectedIdempotencyKey: 'idem:wms.inbound-order.complete:in-1',
        expectedResourceId: 'in-1',
        readback,
        retryDelayMs: 0,
      }),
    ).resolves.toBe(envelope)
    expect(readback).toHaveBeenCalledTimes(1)
  })

  it('surfaces a confirmed WMS posting failure without retrying readback', async () => {
    const envelope = accepted('wms.outbound-order.complete', 'out-1')
    const readback = vi.fn().mockResolvedValue({
      success: true,
      data: {
        items: [
          {
            outboundOrderId: 'out-1',
            status: 'completed',
            inventoryPostingStatus: 'failed',
            failureCode: 'inventory-shortage',
            failureMessage: '库存不足，出库过账失败',
          },
        ],
      },
    })

    await expect(
      confirmBusinessConsoleOperation(envelope, {
        expectedOperationType: 'wms.outbound-order.complete',
        expectedIdempotencyKey: 'idem:wms.outbound-order.complete:out-1',
        expectedResourceId: 'out-1',
        readback,
        retryDelayMs: 0,
      }),
    ).rejects.toMatchObject({
      code: 'business-operation-failed',
      message: '库存不足，出库过账失败',
    })
    expect(readback).toHaveBeenCalledTimes(1)
  })

  it('verifies MES and alarm readback by the receipt resource', () => {
    expect(
      verifyBusinessConsoleOperationReadback(
        { operationType: 'mes.production-report.record', resourceId: 'report-id' },
        {
          success: true,
          data: { report: { productionReportId: 'report-id', reportNo: 'PR-1' } },
        },
      ),
    ).toEqual({ state: 'confirmed-success' })
    expect(
      verifyBusinessConsoleOperationReadback(
        { operationType: 'iiot.alarm.acknowledge', resourceId: 'alarm-1' },
        {
          success: true,
          data: {
            items: [{ alarmEventId: 'alarm-1', acknowledgedAtUtc: '2026-07-28T03:00:00Z' }],
          },
        },
      ),
    ).toEqual({ state: 'confirmed-success' })
  })

  it('requires the alarm to remain shelved instead of trusting historical shelve timestamps', () => {
    const receipt = { operationType: 'iiot.alarm.shelve', resourceId: 'alarm-1' }
    const alarmWithHistoricalShelveWindow = {
      alarmEventId: 'alarm-1',
      shelvedAtUtc: '2026-07-28T03:00:00Z',
      shelvedUntilUtc: '2026-07-28T04:00:00Z',
    }

    expect(
      verifyBusinessConsoleOperationReadback(receipt, {
        success: true,
        data: {
          items: [{ ...alarmWithHistoricalShelveWindow, status: 'shelved' }],
        },
      }),
    ).toEqual({ state: 'confirmed-success' })
    expect(
      verifyBusinessConsoleOperationReadback(receipt, {
        success: true,
        data: {
          items: [{ ...alarmWithHistoricalShelveWindow, status: 'acknowledged' }],
        },
      }),
    ).toEqual({ state: 'indeterminate' })
    expect(
      verifyBusinessConsoleOperationReadback(receipt, {
        success: true,
        data: {
          items: [{ ...alarmWithHistoricalShelveWindow, status: 'raised' }],
        },
      }),
    ).toEqual({ state: 'indeterminate' })
  })

  it('dispatches an accepted receipt through the typed operation allowlist', async () => {
    vi.mocked(listBusinessConsoleWmsCountExecutions).mockResolvedValue({
      data: {
        success: true,
        data: {
          items: [
            {
              countExecutionId: 'count-1',
              status: 'completed',
              inventoryPostingStatus: 'posted',
            },
          ],
        },
      },
    } as never)
    const path =
      '/api/business-console/v1/wms/count-executions?organizationId=org-1&environmentId=env-1&scopeKind=work-pool&scopeId=POOL-A&countExecutionId=count-1'

    await expect(
      readBusinessConsoleOperationState(
        path,
        {
          operationType: 'wms.count-execution.complete',
          resourceId: 'count-1',
        },
        { scopeKind: 'work-pool', scopeId: 'POOL-A' },
      ),
    ).resolves.toMatchObject({ success: true })
    expect(listBusinessConsoleWmsCountExecutions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-1',
        environmentId: 'env-1',
        countExecutionId: 'count-1',
        scopeKind: 'work-pool',
        scopeId: 'POOL-A',
      },
      throwOnError: true,
    })
  })

  it.each([
    {
      name: 'missing receipt scope',
      path: '/api/business-console/v1/wms/count-executions?organizationId=org-1&environmentId=env-1&countExecutionId=count-1',
      scope: { scopeKind: 'work-pool', scopeId: 'POOL-A' },
    },
    {
      name: 'different frozen scope',
      path: '/api/business-console/v1/wms/count-executions?organizationId=org-1&environmentId=env-1&scopeKind=site&scopeId=SITE-A&countExecutionId=count-1',
      scope: { scopeKind: 'work-pool', scopeId: 'POOL-A' },
    },
    {
      name: 'missing frozen scope',
      path: '/api/business-console/v1/wms/count-executions?organizationId=org-1&environmentId=env-1&scopeKind=work-pool&scopeId=POOL-A&countExecutionId=count-1',
      scope: undefined,
    },
    {
      name: 'unsupported frozen scope',
      path: '/api/business-console/v1/wms/count-executions?organizationId=org-1&environmentId=env-1&scopeKind=organization&scopeId=org-1&countExecutionId=count-1',
      scope: { scopeKind: 'organization', scopeId: 'org-1' },
    },
  ])('rejects WMS readback with $name before issuing a generated GET', async ({ path, scope }) => {
    vi.mocked(listBusinessConsoleWmsCountExecutions).mockClear()
    await expect(
      readBusinessConsoleOperationState(
        path,
        { operationType: 'wms.count-execution.complete', resourceId: 'count-1' },
        scope,
      ),
    ).rejects.toThrow('作业范围')
    expect(listBusinessConsoleWmsCountExecutions).not.toHaveBeenCalled()
  })

  it('rejects an operation/path mismatch before any generated GET is called', async () => {
    const path =
      '/api/business-console/v1/wms/outbound-orders?organizationId=org-1&environmentId=env-1&outboundOrderId=out-1'

    await expect(
      readBusinessConsoleOperationState(path, {
        operationType: 'wms.count-execution.complete',
        resourceId: 'out-1',
      }),
    ).rejects.toThrow('没有受治理的回读映射')
    expect(listBusinessConsoleWmsOutboundOrders).not.toHaveBeenCalled()
  })

  it('rejects cross-origin or incomplete accepted receipts without issuing readback', async () => {
    const envelope = accepted('wms.count-execution.complete', 'count-1')
    envelope.data.operationReceipt.readbackPath = 'https://example.invalid/readback'
    const readback = vi.fn()

    await expect(
      confirmBusinessConsoleOperation(envelope, {
        expectedOperationType: 'wms.count-execution.complete',
        expectedIdempotencyKey: 'idem:wms.count-execution.complete:count-1',
        expectedResourceId: 'count-1',
        readback,
      }),
    ).rejects.toBeInstanceOf(BusinessOperationUnconfirmedError)
    expect(readback).not.toHaveBeenCalled()
  })
})
