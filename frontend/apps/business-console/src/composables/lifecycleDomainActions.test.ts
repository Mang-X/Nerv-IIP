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
import { useBusinessContextStore } from '@/stores/businessContext'
import { useBusinessEquipmentAlarms } from './useBusinessEquipment'
import { useMaintenanceWorkOrders } from './useBusinessMaintenance'
import { useMesOperationTasks } from './useBusinessMes'
import { useQualityNcrs } from './useBusinessQuality'
import { useWmsInboundOrders, useWmsOutboundOrders } from './useBusinessWms'
import { LifecycleStateChangedError, recoverLifecycleAction } from './lifecycleAction'

const api = vi.hoisted(() => ({
  getMaintenance: vi.fn(),
  getNcr: vi.fn(),
  listMes: vi.fn(),
}))
const queryRefetch = vi.hoisted(() => vi.fn(async () => undefined))

vi.mock('@nerv-iip/api-client', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@nerv-iip/api-client')>()
  return {
    ...actual,
    acknowledgeBusinessConsoleEquipmentAlarm: vi.fn(),
    completeBusinessConsoleMaintenanceWorkOrder: vi.fn(),
    completeBusinessConsoleWmsInboundOrder: vi.fn(),
    completeBusinessConsoleWmsOutboundOrder: vi.fn(),
    getBusinessConsoleMaintenanceWorkOrderQueryOptions: vi.fn(() => ({
      key: [],
      query: api.getMaintenance,
    })),
    getBusinessConsoleQualityNcrQueryOptions: vi.fn(() => ({
      key: [],
      query: api.getNcr,
    })),
    listBusinessConsoleEquipmentAlarms: vi.fn(),
    listBusinessConsoleMesOperationTasks: api.listMes,
    listBusinessConsoleWmsInboundOrders: vi.fn(),
    listBusinessConsoleWmsOutboundOrders: vi.fn(),
    startBusinessConsoleMesOperationTask: vi.fn(),
    submitBusinessConsoleQualityNcrDisposition: vi.fn(),
  }
})

vi.mock('@pinia/colada', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@pinia/colada')>()
  return {
    ...actual,
    useMutation: vi.fn(() => ({
      error: shallowRef(),
      isLoading: shallowRef(false),
      mutateAsync: vi.fn(),
    })),
    useQuery: vi.fn((factory) => {
      factory()
      return {
        data: shallowRef(),
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

function createHarnesses(): Record<string, DomainHarness> {
  const wms = useWmsInboundOrders()
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
          .mockResolvedValue(envelope({ inboundOrderId: 'IN-1', status }) as never),
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
      harness.command.mockResolvedValue(commandResult(409, { message: 'conflict' }) as never)

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
      harness.command.mockResolvedValue(commandResult(validation.code, validation) as never)

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

  it('allows only an explicit same-key inbound retry to replay after the order became completed', async () => {
    const wms = useWmsInboundOrders()
    vi.mocked(listBusinessConsoleWmsInboundOrders).mockResolvedValue(
      envelope({ inboundOrderId: 'IN-1', status: 'Completed' }) as never,
    )
    vi.mocked(completeBusinessConsoleWmsInboundOrder).mockResolvedValue(commandResult(200) as never)

    await expect(
      wms.completeInbound('IN-1', 'wms-intent-1', { attempt: 'initial' }),
    ).rejects.toMatchObject({ source: 'preflight' })
    expect(completeBusinessConsoleWmsInboundOrder).not.toHaveBeenCalled()

    await expect(
      wms.completeInbound('IN-1', 'wms-intent-1', { attempt: 'retry' }),
    ).resolves.toBeDefined()
    expect(completeBusinessConsoleWmsInboundOrder).toHaveBeenCalledOnce()
  })

  it('allows only an explicit same-key outbound retry while inventory posting is pending', async () => {
    const wms = useWmsOutboundOrders()
    vi.mocked(listBusinessConsoleWmsOutboundOrders).mockResolvedValue(
      envelope({ outboundOrderId: 'OUT-1', status: 'InventoryPostingPending' }) as never,
    )
    vi.mocked(completeBusinessConsoleWmsOutboundOrder).mockResolvedValue(
      commandResult(200) as never,
    )
    const payload = { packReviewNo: 'PR-1', passed: true }

    await expect(
      wms.completeOutbound('OUT-1', payload, 'wms-intent-1', { attempt: 'initial' }),
    ).rejects.toMatchObject({ source: 'preflight' })
    expect(completeBusinessConsoleWmsOutboundOrder).not.toHaveBeenCalled()

    await expect(
      wms.completeOutbound('OUT-1', payload, 'wms-intent-1', { attempt: 'retry' }),
    ).resolves.toBeDefined()
    expect(completeBusinessConsoleWmsOutboundOrder).toHaveBeenCalledOnce()
  })
})
