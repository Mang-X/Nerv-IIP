import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'
import { createPinia, setActivePinia } from 'pinia'

import {
  acknowledgeBusinessConsoleEquipmentAlarm,
  completeBusinessConsoleMaintenanceWorkOrder,
  completeBusinessConsoleWmsInboundOrder,
  completeBusinessConsoleWmsOutboundOrder,
  listBusinessConsoleEquipmentAlarms,
  listBusinessConsoleWmsInboundOrders,
  listBusinessConsoleWmsOutboundOrders,
  startBusinessConsoleMesOperationTask,
  submitBusinessConsoleQualityNcrDisposition,
} from '@nerv-iip/api-client'
import {
  acquirePendingBusinessIntent,
  clearPendingBusinessIntent,
  peekPendingBusinessIntent,
} from '@nerv-iip/business-core'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useBusinessEquipmentAlarms } from './useBusinessEquipment'
import { useMaintenanceWorkOrders } from './useBusinessMaintenance'
import { useMesOperationTasks } from './useBusinessMes'
import { useQualityNcrs } from './useBusinessQuality'
import { useWmsInboundOrders, useWmsOutboundOrders } from './useBusinessWms'
import { LifecycleStateChangedError, recoverLifecycleAction } from './lifecycleAction'

const api = vi.hoisted(() => ({
  completeMaintenance: vi.fn(),
  getMaintenance: vi.fn(),
  getNcr: vi.fn(),
  listMes: vi.fn(),
  startMes: vi.fn(),
}))
const queryRefetch = vi.hoisted(() => vi.fn(async () => undefined))

vi.mock('@nerv-iip/api-client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@nerv-iip/api-client')>()
  return {
    ...actual,
    acknowledgeBusinessConsoleEquipmentAlarm: vi.fn(),
    completeBusinessConsoleMaintenanceWorkOrder: api.completeMaintenance,
    completeBusinessConsoleMaintenanceWorkOrderMutationOptions: vi.fn(() => ({
      mutation: api.completeMaintenance,
    })),
    completeBusinessConsoleWmsInboundOrder: vi.fn(),
    completeBusinessConsoleWmsOutboundOrder: vi.fn(),
    getBusinessConsoleMaintenanceWorkOrderQueryOptions: vi.fn(() => ({
      key: [],
      query: api.getMaintenance,
    })),
    getBusinessConsolePrincipalWorkContextQueryOptions: vi.fn(() => ({
      key: [{ _id: 'getBusinessConsolePrincipalWorkContext' }],
      query: vi.fn(),
    })),
    getBusinessConsoleQualityNcrQueryOptions: vi.fn(() => ({
      key: [],
      query: api.getNcr,
    })),
    listBusinessConsoleEquipmentAlarms: vi.fn(),
    listBusinessConsoleMesOperationTasks: api.listMes,
    listBusinessConsoleWmsInboundOrders: vi.fn(),
    listBusinessConsoleWmsOutboundOrders: vi.fn(),
    startBusinessConsoleMesOperationTask: api.startMes,
    startBusinessConsoleMesOperationTaskMutationOptions: vi.fn(() => ({
      mutation: api.startMes,
    })),
    submitBusinessConsoleQualityNcrDisposition: vi.fn(),
  }
})

vi.mock('@pinia/colada', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@pinia/colada')>()
  return {
    ...actual,
    useMutation: vi.fn(
      (
        options: {
          mutation?: (variables: unknown, context: unknown) => unknown
        } = {},
      ) => ({
        error: shallowRef(),
        isLoading: shallowRef(false),
        mutateAsync: vi.fn((variables: unknown) => options.mutation?.(variables, {})),
      }),
    ),
    useQuery: vi.fn((factory) => {
      const options = factory()
      const key = Array.isArray(options.key) ? options.key[0] : undefined
      const id = key && typeof key === 'object' && '_id' in key ? String(key._id) : ''
      return {
        data: shallowRef(
          id === 'getBusinessConsolePrincipalWorkContext'
            ? {
                success: true,
                data: { selectedScope: { kind: 'self', id: 'user-001' } },
              }
            : undefined,
        ),
        error: shallowRef(),
        isLoading: shallowRef(false),
        refetch: queryRefetch,
      }
    }),
    useQueryCache: vi.fn(() => ({
      invalidateQueries: vi.fn(async () => undefined),
    })),
  }
})

type DomainHarness = {
  command: ReturnType<typeof vi.fn>
  invoke: () => Promise<unknown>
  setStatus: (status: string) => void
  refresh: () => Promise<unknown>
}

function envelope(item: object) {
  return { data: { success: true, data: { items: [item], total: 1 } } }
}

function commandResult(status: number, error?: unknown) {
  return error
    ? { error, response: { status } }
    : { data: { success: true, data: {} }, response: { status } }
}

function confirmedCommandResult(operationType: string, resourceId: string, idempotencyKey: string) {
  return {
    data: {
      success: true,
      data: {
        operationReceipt: {
          operationType,
          authority: 'business-gateway',
          resourceType: 'test-resource',
          resourceId,
          idempotencyKey,
          outcome: 'confirmed',
          stateConfirmed: true,
          readbackRequired: false,
          changedAtUtc: '2026-07-29T00:00:00.000Z',
          resourceStatus: 'Completed',
          readbackMethod: null,
          readbackPath: null,
        },
      },
    },
    response: { status: 200 },
  }
}

const GENERATED_THROW_DOMAINS = new Set(['Maintenance', 'MES'])

function arrangeCommandFailure(
  harness: DomainHarness,
  domain: string,
  status: number,
  error: object,
) {
  if (GENERATED_THROW_DOMAINS.has(domain)) {
    ;(error as { response?: { status: number } }).response = { status }
    harness.command.mockRejectedValue(error)
    return
  }
  harness.command.mockResolvedValue(commandResult(status, error) as never)
}

function createHarnesses(): Record<string, DomainHarness> {
  const wms = useWmsInboundOrders({ scopeKind: 'self', scopeId: 'user-001' })
  const maintenance = useMaintenanceWorkOrders()
  const quality = useQualityNcrs()
  const equipment = useBusinessEquipmentAlarms()
  const mes = useMesOperationTasks()

  return {
    WMS: {
      command: vi.mocked(completeBusinessConsoleWmsInboundOrder),
      invoke: () => wms.completeInbound('IN-1', 'wms-intent-1'),
      setStatus: (status) =>
        vi
          .mocked(listBusinessConsoleWmsInboundOrders)
          .mockResolvedValue(envelope({ inboundOrderId: 'IN-1', status, version: 1 }) as never),
      refresh: wms.refreshInboundOrders,
    },
    Maintenance: {
      command: vi.mocked(completeBusinessConsoleMaintenanceWorkOrder),
      invoke: () => maintenance.completeWorkOrder('MWO-1', {} as never),
      setStatus: (status) =>
        api.getMaintenance.mockResolvedValue({
          success: true,
          data: { workOrderId: 'MWO-1', status },
        }),
      refresh: maintenance.refreshWorkOrders,
    },
    Quality: {
      command: vi.mocked(submitBusinessConsoleQualityNcrDisposition),
      invoke: () => quality.submitDisposition('NCR-1', { dispositionType: 'use-as-is' }),
      setStatus: (status) =>
        api.getNcr.mockResolvedValue({
          success: true,
          data: { id: 'NCR-1', status },
        }),
      refresh: quality.refreshNcrs,
    },
    Equipment: {
      command: vi.mocked(acknowledgeBusinessConsoleEquipmentAlarm),
      invoke: () => equipment.acknowledgeAlarm('ALM-1', 'operator-1'),
      setStatus: (status) =>
        vi
          .mocked(listBusinessConsoleEquipmentAlarms)
          .mockResolvedValue(envelope({ alarmEventId: 'ALM-1', status }) as never),
      refresh: equipment.refreshAlarms,
    },
    MES: {
      command: vi.mocked(startBusinessConsoleMesOperationTask),
      invoke: () =>
        mes.startOperationTask(
          'OP-1',
          { organizationId: 'org-1', environmentId: 'env-1', workOrderId: 'WO-1' },
          { idempotencyKey: 'start-op-1' },
        ),
      setStatus: (status) =>
        api.listMes.mockResolvedValue(
          envelope({ operationTaskId: 'OP-1', workOrderId: 'WO-1', status }),
        ),
      refresh: mes.refreshOperationTasks,
    },
  }
}

const terminalStatus: Record<string, string> = {
  WMS: 'Completed',
  Maintenance: 'Completed',
  Quality: 'Closed',
  Equipment: 'Cleared',
  MES: 'Completed',
}
const allowedStatus: Record<string, string> = {
  WMS: 'Open',
  Maintenance: 'Open',
  Quality: 'Open',
  Equipment: 'Raised',
  MES: 'Queued',
}

describe('Business Console lifecycle domain actions', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    useBusinessContextStore().patchContext({
      organizationId: 'org-1',
      environmentId: 'env-1',
    })
    vi.clearAllMocks()
  })

  it.each(Object.keys(terminalStatus))(
    '%s blocks terminal and unknown authoritative states before the write',
    async (domain) => {
      const harness = createHarnesses()[domain]!

      harness.setStatus(terminalStatus[domain]!)
      await expect(harness.invoke()).rejects.toMatchObject({ source: 'preflight' })
      harness.setStatus('UnexpectedState')
      await expect(harness.invoke()).rejects.toMatchObject({ source: 'preflight' })

      expect(harness.command).not.toHaveBeenCalled()
    },
  )

  it.each(Object.keys(allowedStatus))(
    '%s classifies 409 and recovers by clearing and refreshing',
    async (domain) => {
      const harness = createHarnesses()[domain]!
      harness.setStatus(allowedStatus[domain]!)
      arrangeCommandFailure(harness, domain, 409, { message: 'conflict' })

      const error = await harness.invoke().catch((reason) => reason)
      expect(error).toBeInstanceOf(LifecycleStateChangedError)
      const reset = vi.fn()
      const notify = vi.fn()
      await expect(
        recoverLifecycleAction(error, { reset, refresh: harness.refresh, notify }),
      ).resolves.toBe(true)
      expect(reset).toHaveBeenCalledOnce()
      expect(queryRefetch).toHaveBeenCalled()
      expect(notify).toHaveBeenCalledWith('状态已被其他操作更新')
    },
  )

  it.each(Object.keys(allowedStatus))(
    '%s preserves 400/422 validation errors and keeps the action context',
    async (domain) => {
      const harness = createHarnesses()[domain]!
      harness.setStatus(allowedStatus[domain]!)
      const validation = { message: 'validation', code: domain === 'Quality' ? 422 : 400 }
      arrangeCommandFailure(harness, domain, validation.code, validation)

      const error = await harness.invoke().catch((reason) => reason)
      expect(error).toBe(validation)
      const reset = vi.fn()
      await expect(
        recoverLifecycleAction(error, {
          reset,
          refresh: harness.refresh,
          notify: vi.fn(),
        }),
      ).resolves.toBe(false)
      expect(reset).not.toHaveBeenCalled()
    },
  )

  it('replays a completed inbound only from an exact restored intent and its OLD key', async () => {
    const wms = useWmsInboundOrders({ scopeKind: 'self', scopeId: 'user-001' })
    const pendingScope = {
      principalId: 'unrestored-session',
      organizationId: 'org-1',
      environmentId: 'env-1',
      operationType: 'wms.inbound-order.complete',
      payloadFingerprint: 'IN-1',
    }
    clearPendingBusinessIntent(pendingScope)
    vi.mocked(listBusinessConsoleWmsInboundOrders).mockResolvedValue(
      envelope({ inboundOrderId: 'IN-1', status: 'Completed' }) as never,
    )
    vi.mocked(completeBusinessConsoleWmsInboundOrder).mockResolvedValue(
      confirmedCommandResult('wms.inbound-order.complete', 'IN-1', 'wms-intent-old') as never,
    )

    await expect(
      wms.completeInbound('IN-1', 'wms-intent-new', { attempt: 'initial' }),
    ).rejects.toMatchObject({ source: 'preflight' })
    expect(completeBusinessConsoleWmsInboundOrder).not.toHaveBeenCalled()
    expect(peekPendingBusinessIntent(pendingScope)).toBeUndefined()

    await expect(
      wms.completeInbound('IN-1', 'wms-intent-new', { attempt: 'retry' }),
    ).rejects.toMatchObject({ source: 'preflight' })
    expect(completeBusinessConsoleWmsInboundOrder).not.toHaveBeenCalled()

    acquirePendingBusinessIntent(pendingScope, () => 'wms-intent-old', {
      scopeKind: 'self',
      scopeId: 'user-001',
      expectedVersion: 4,
    })
    await expect(
      wms.completeInbound('IN-1', 'wms-intent-new', { attempt: 'retry' }),
    ).resolves.toBeDefined()
    expect(completeBusinessConsoleWmsInboundOrder).toHaveBeenCalledWith(
      expect.objectContaining({
        body: {
          idempotencyKey: 'wms-intent-old',
          scopeKind: 'self',
          scopeId: 'user-001',
          expectedVersion: 4,
        },
      }),
    )
  })

  it('replays a posting-pending outbound only from an exact restored intent and its OLD key', async () => {
    const wms = useWmsOutboundOrders({ scopeKind: 'self', scopeId: 'user-001' })
    const payload = { packReviewNo: 'PR-1', passed: true }
    const pendingScope = {
      principalId: 'unrestored-session',
      organizationId: 'org-1',
      environmentId: 'env-1',
      operationType: 'wms.outbound-order.complete',
      payloadFingerprint: 'OUT-1:{"packReviewNo":"PR-1","passed":true}',
    }
    clearPendingBusinessIntent(pendingScope)
    vi.mocked(listBusinessConsoleWmsOutboundOrders).mockResolvedValue(
      envelope({ outboundOrderId: 'OUT-1', status: 'InventoryPostingPending' }) as never,
    )
    vi.mocked(completeBusinessConsoleWmsOutboundOrder).mockResolvedValue(
      confirmedCommandResult('wms.outbound-order.complete', 'OUT-1', 'wms-intent-old') as never,
    )

    await expect(
      wms.completeOutbound('OUT-1', payload, 'wms-intent-new', { attempt: 'initial' }),
    ).rejects.toMatchObject({ source: 'preflight' })
    expect(completeBusinessConsoleWmsOutboundOrder).not.toHaveBeenCalled()
    expect(peekPendingBusinessIntent(pendingScope)).toBeUndefined()

    await expect(
      wms.completeOutbound('OUT-1', payload, 'wms-intent-new', { attempt: 'retry' }),
    ).rejects.toMatchObject({ source: 'preflight' })
    expect(completeBusinessConsoleWmsOutboundOrder).not.toHaveBeenCalled()

    acquirePendingBusinessIntent(pendingScope, () => 'wms-intent-old', {
      ...payload,
      scopeKind: 'self',
      scopeId: 'user-001',
      expectedVersion: 5,
    })
    await expect(
      wms.completeOutbound('OUT-1', payload, 'wms-intent-new', { attempt: 'retry' }),
    ).resolves.toBeDefined()
    expect(completeBusinessConsoleWmsOutboundOrder).toHaveBeenCalledWith(
      expect.objectContaining({
        body: {
          ...payload,
          idempotencyKey: 'wms-intent-old',
          scopeKind: 'self',
          scopeId: 'user-001',
          expectedVersion: 5,
        },
      }),
    )
  })
})
