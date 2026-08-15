import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'
import { createPinia, setActivePinia } from 'pinia'

import {
  createBusinessConsoleMaintenanceSparePartMutationOptions,
  listBusinessConsoleMaintenanceInspectionsQueryOptions,
  listBusinessConsoleMaintenanceSparePartsQueryOptions,
  queryBusinessConsoleMaintenanceAssetReliabilityQueryOptions,
  queryBusinessConsoleMaintenanceAvailabilityWindowsQueryOptions,
  recordBusinessConsoleMaintenanceInspectionMutationOptions,
  updateBusinessConsoleMaintenancePlanMutationOptions,
} from '@nerv-iip/api-client'
import { useBusinessContextStore } from '@/stores/businessContext'
import {
  useMaintenanceAvailabilityWindows,
  useMaintenanceInspections,
  useMaintenancePlans,
  useMaintenanceReliability,
  useMaintenanceSpareParts,
  useMaintenanceWorkOrders,
} from './useBusinessMaintenance'

const coladaState = vi.hoisted(() => ({
  confirmOperation: vi.fn(),
  maintenanceWorkOrderStatus: 'Open',
  mutationCallsById: new Map<string, unknown[]>(),
  mutationFailuresById: new Map<string, Error[]>(),
  queryDataById: new Map<string, unknown>(),
  queryFactoriesById: new Map<string, () => { enabled?: boolean } & Record<string, unknown>>(),
  queryOptionsById: new Map<string, { enabled?: boolean }>(),
  queryRefetchById: new Map<string, ReturnType<typeof vi.fn>>(),
}))

vi.mock('@nerv-iip/api-client', () => ({
  confirmBusinessConsoleOperation: (...args: unknown[]) => coladaState.confirmOperation(...args),
  completeBusinessConsoleMaintenanceWorkOrderMutationOptions: vi.fn(() => ({
    key: [{ _id: 'completeBusinessConsoleMaintenanceWorkOrder' }],
    mutation: vi.fn(),
  })),
  createBusinessConsoleMaintenancePlanMutationOptions: vi.fn(() => ({
    key: [],
    mutation: vi.fn(),
  })),
  createBusinessConsoleMaintenanceSparePartMutationOptions: vi.fn(() => ({
    key: [{ _id: 'createBusinessConsoleMaintenanceSparePart' }],
    mutation: vi.fn(),
  })),
  createBusinessConsoleMaintenanceWorkOrderMutationOptions: vi.fn(() => ({
    key: [{ _id: 'createBusinessConsoleMaintenanceWorkOrder' }],
    mutation: vi.fn(),
  })),
  generateDueBusinessConsoleMaintenanceWorkOrdersMutationOptions: vi.fn(() => ({
    key: [],
    mutation: vi.fn(),
  })),
  getBusinessConsoleMaintenanceWorkOrderQueryOptions: vi.fn(() => ({
    query: vi.fn(async () => ({
      success: true,
      data: { status: coladaState.maintenanceWorkOrderStatus },
    })),
  })),
  listBusinessConsoleMaintenanceInspectionsQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleMaintenanceInspections' }],
    query: vi.fn(),
  })),
  listBusinessConsoleMaintenancePlansQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleMaintenancePlans' }],
    query: vi.fn(),
  })),
  listBusinessConsoleMaintenanceSparePartsQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleMaintenanceSpareParts' }],
    query: vi.fn(),
  })),
  listBusinessConsoleMaintenanceWorkOrdersQueryOptions: vi.fn(() => ({ key: [], query: vi.fn() })),
  queryBusinessConsoleMaintenanceAssetReliabilityQueryOptions: vi.fn(() => ({
    key: [{ _id: 'queryBusinessConsoleMaintenanceAssetReliability' }],
    query: vi.fn(),
  })),
  queryBusinessConsoleMaintenanceAvailabilityWindowsQueryOptions: vi.fn(() => ({
    key: [{ _id: 'queryBusinessConsoleMaintenanceAvailabilityWindows' }],
    query: vi.fn(),
  })),
  recordBusinessConsoleMaintenanceInspectionMutationOptions: vi.fn(() => ({
    key: [{ _id: 'recordBusinessConsoleMaintenanceInspection' }],
    mutation: vi.fn(),
  })),
  updateBusinessConsoleMaintenancePlanMutationOptions: vi.fn(() => ({
    key: [{ _id: 'updateBusinessConsoleMaintenancePlan' }],
    mutation: vi.fn(),
  })),
}))

vi.mock('@pinia/colada', () => ({
  useMutation: vi.fn((options) => {
    const key = Array.isArray(options.key) ? options.key[0] : undefined
    const id = key && typeof key === 'object' && '_id' in key ? String(key._id) : ''

    return {
      error: shallowRef(),
      isLoading: shallowRef(false),
      mutateAsync: vi.fn(async (payload) => {
        const calls = coladaState.mutationCallsById.get(id) ?? []
        calls.push(payload)
        coladaState.mutationCallsById.set(id, calls)
        const failures = coladaState.mutationFailuresById.get(id)
        const failure = failures?.shift()
        if (failure) throw failure
        options.onSuccess?.()
        return { success: true, data: {} }
      }),
    }
  }),
  useQuery: vi.fn((optionsFactory) => {
    const options = optionsFactory()
    const key = Array.isArray(options.key) ? options.key[0] : undefined
    const id = key && typeof key === 'object' && '_id' in key ? String(key._id) : ''
    coladaState.queryFactoriesById.set(id, optionsFactory)
    coladaState.queryOptionsById.set(id, options)

    const refetch = vi.fn()
    coladaState.queryRefetchById.set(id, refetch)

    return {
      data: shallowRef(coladaState.queryDataById.get(id)),
      error: shallowRef(),
      isLoading: shallowRef(false),
      refetch,
    }
  }),
}))

describe('business maintenance composables', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    coladaState.confirmOperation.mockReset()
    coladaState.confirmOperation.mockImplementation(async (value) => value)
    coladaState.maintenanceWorkOrderStatus = 'Open'
    coladaState.mutationCallsById.clear()
    coladaState.mutationFailuresById.clear()
    coladaState.queryDataById.clear()
    coladaState.queryFactoriesById.clear()
    coladaState.queryOptionsById.clear()
    coladaState.queryRefetchById.clear()
  })

  it('reuses one completion intent key after timeout and rotates it after success', async () => {
    const workOrders = useMaintenanceWorkOrders({
      organizationId: 'org-001',
      environmentId: 'env-dev',
    })
    coladaState.mutationFailuresById.set('completeBusinessConsoleMaintenanceWorkOrder', [
      Object.assign(new Error('timeout'), { name: 'RequestTimeoutError' }),
    ])
    const body = {
      organizationId: 'org-001',
      environmentId: 'env-dev',
      result: 'repaired',
      downtimeReasonCode: 'breakdown',
      downtimeMinutes: 20,
      spareParts: [{ skuCode: 'SP-1', quantity: 1, uomCode: 'EA' }],
    }

    await expect(workOrders.completeWorkOrder('wo-001', body)).rejects.toThrow('timeout')
    await workOrders.completeWorkOrder('wo-001', body)
    await workOrders.completeWorkOrder('wo-001', body)

    const calls = coladaState.mutationCallsById.get(
      'completeBusinessConsoleMaintenanceWorkOrder',
    ) as Array<{ body: { idempotencyKey?: string } }>
    expect(calls).toHaveLength(3)
    expect(calls[0]?.body.idempotencyKey).toBeTruthy()
    expect(calls[1]?.body.idempotencyKey).toBe(calls[0]?.body.idempotencyKey)
    expect(calls[2]?.body.idempotencyKey).not.toBe(calls[1]?.body.idempotencyKey)
  })

  it('blocks a terminal maintenance completion when there is no matching pending replay', async () => {
    coladaState.maintenanceWorkOrderStatus = 'Completed'
    const workOrders = useMaintenanceWorkOrders({
      organizationId: 'org-001',
      environmentId: 'env-dev',
    })

    await expect(
      workOrders.completeWorkOrder('wo-terminal', {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        result: 'repaired',
        downtimeReasonCode: 'breakdown',
        idempotencyKey: 'caller-new-without-pending',
      }),
    ).rejects.toThrow('状态已被其他操作更新')

    expect(
      coladaState.mutationCallsById.get('completeBusinessConsoleMaintenanceWorkOrder') ?? [],
    ).toHaveLength(0)
  })

  it('replays a terminal completion with the durable key after the caller key changes', async () => {
    const workOrders = useMaintenanceWorkOrders({
      organizationId: 'org-001',
      environmentId: 'env-dev',
    })
    const intent = {
      organizationId: 'org-001',
      environmentId: 'env-dev',
      result: 'repaired',
      downtimeReasonCode: 'breakdown',
      downtimeMinutes: 20,
      spareParts: [{ skuCode: 'SP-1', quantity: 1, uomCode: 'EA' }],
    }
    coladaState.mutationFailuresById.set('completeBusinessConsoleMaintenanceWorkOrder', [
      Object.assign(new Error('timeout'), { name: 'RequestTimeoutError' }),
    ])

    await expect(
      workOrders.completeWorkOrder('wo-replay', {
        ...intent,
        idempotencyKey: 'durable-old-key',
      }),
    ).rejects.toThrow('timeout')

    coladaState.maintenanceWorkOrderStatus = 'Completed'
    coladaState.confirmOperation
      .mockRejectedValueOnce(
        Object.assign(new Error('receipt unconfirmed'), {
          code: 'business-operation-unconfirmed',
          indeterminate: true,
        }),
      )
      .mockImplementation(async (value) => value)

    await expect(
      workOrders.completeWorkOrder('wo-replay', {
        ...intent,
        idempotencyKey: 'caller-new-key',
      }),
    ).rejects.toThrow('receipt unconfirmed')
    await workOrders.completeWorkOrder('wo-replay', {
      ...intent,
      idempotencyKey: 'caller-newer-key',
    })

    const calls = coladaState.mutationCallsById.get(
      'completeBusinessConsoleMaintenanceWorkOrder',
    ) as Array<{ body: { idempotencyKey?: string } }>
    expect(calls.map((call) => call.body.idempotencyKey)).toEqual([
      'durable-old-key',
      'durable-old-key',
      'durable-old-key',
    ])
    expect(coladaState.confirmOperation).toHaveBeenCalledTimes(2)
    expect(coladaState.confirmOperation).toHaveBeenNthCalledWith(
      1,
      expect.anything(),
      expect.objectContaining({ expectedIdempotencyKey: 'durable-old-key' }),
    )
    expect(coladaState.confirmOperation).toHaveBeenNthCalledWith(
      2,
      expect.anything(),
      expect.objectContaining({ expectedIdempotencyKey: 'durable-old-key' }),
    )
  })

  it('rotates a maintenance create key after an explicit 422 rejection', async () => {
    const workOrders = useMaintenanceWorkOrders({
      organizationId: 'org-001',
      environmentId: 'env-dev',
    })
    const intent = {
      organizationId: 'org-001',
      environmentId: 'env-dev',
      deviceAssetId: 'DEV-1',
      priority: 'high',
      openedBy: 'operator-1',
    }
    coladaState.confirmOperation
      .mockRejectedValueOnce(Object.assign(new Error('validation failed'), { statusCode: 422 }))
      .mockImplementation(async (value) => value)

    await expect(
      workOrders.createWorkOrder({ ...intent, idempotencyKey: 'maintenance-key-1' }),
    ).rejects.toThrow('validation failed')
    await workOrders.createWorkOrder({ ...intent, idempotencyKey: 'maintenance-key-2' })

    const calls = coladaState.mutationCallsById.get(
      'createBusinessConsoleMaintenanceWorkOrder',
    ) as Array<{ body: { idempotencyKey?: string } }>
    expect(calls.map((call) => call.body.idempotencyKey)).toEqual([
      'maintenance-key-1',
      'maintenance-key-2',
    ])
  })

  it('injects a fresh create key without requiring page-level idempotency state', async () => {
    const workOrders = useMaintenanceWorkOrders({
      organizationId: 'org-001',
      environmentId: 'env-dev',
    })
    const body = {
      organizationId: 'org-001',
      environmentId: 'env-dev',
      deviceAssetId: 'DEV-1',
      priority: 'high',
      openedBy: 'operator-1',
    }

    await workOrders.createWorkOrder(body)
    await workOrders.createWorkOrder(body)

    const calls = coladaState.mutationCallsById.get(
      'createBusinessConsoleMaintenanceWorkOrder',
    ) as Array<{ body: { idempotencyKey?: string } }>
    expect(calls[0]?.body.idempotencyKey).toMatch(/^maintenance-create-/)
    expect(calls[1]?.body.idempotencyKey).not.toBe(calls[0]?.body.idempotencyKey)
  })

  it('exposes an unsuccessful work-order envelope as a business-response failure', () => {
    const context = useBusinessContextStore()
    context.patchContext({ organizationId: 'org-001', environmentId: 'env-dev' })
    coladaState.queryDataById.set('', { success: false })

    const workOrders = useMaintenanceWorkOrders()

    expect(workOrders.workOrdersHasSuccessfulResponse.value).toBe(false)
    expect(workOrders.workOrdersHasFailedResponse.value).toBe(true)
  })

  it('loads inspection rows and records a real inspection through the facade', async () => {
    const context = useBusinessContextStore()
    context.patchContext({ organizationId: 'org-001', environmentId: 'env-dev' })
    coladaState.queryDataById.set('listBusinessConsoleMaintenanceInspections', {
      success: true,
      data: {
        items: [{ inspectionId: 'inspection-1', deviceAssetId: 'DEV-CNC-01', result: 'passed' }],
        total: 1,
      },
    })

    const inspections = useMaintenanceInspections()

    expect(listBusinessConsoleMaintenanceInspectionsQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        skip: 0,
        take: 100,
      },
    })
    expect(inspections.inspections.value).toHaveLength(1)
    expect(inspections.inspectionsTotal.value).toBe(1)

    await inspections.recordInspection({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      planId: 'plan-1',
      workOrderId: 'wo-1',
      inspector: '设备保全班',
      inspectedAtUtc: '2026-07-02T08:00:00.000Z',
      result: 'passed',
    })

    expect(recordBusinessConsoleMaintenanceInspectionMutationOptions).toHaveBeenCalled()
    expect(coladaState.mutationCallsById.get('recordBusinessConsoleMaintenanceInspection')).toEqual(
      [
        {
          body: expect.objectContaining({
            inspector: '设备保全班',
            planId: 'plan-1',
            workOrderId: 'wo-1',
          }),
        },
      ],
    )
  })

  it('loads spare part requests and creates a request without inventing inventory balance', async () => {
    const context = useBusinessContextStore()
    context.patchContext({ organizationId: 'org-001', environmentId: 'env-dev' })
    coladaState.queryDataById.set('listBusinessConsoleMaintenanceSpareParts', {
      success: true,
      data: {
        items: [{ sparePartLineId: 'sp-1', workOrderId: 'wo-1', skuCode: 'BRG-6205', quantity: 2 }],
        total: 1,
      },
    })

    const spareParts = useMaintenanceSpareParts()

    expect(listBusinessConsoleMaintenanceSparePartsQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        skip: 0,
        take: 100,
      },
    })
    expect(spareParts.spareParts.value).toHaveLength(1)
    expect(spareParts.spareParts.value[0]?.sparePartLineId).toBe('sp-1')
    expect(spareParts.sparePartsTotal.value).toBe(1)

    await spareParts.createSparePart({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      workOrderId: 'wo-1',
      skuCode: 'BRG-6205',
      quantity: 2,
      uomCode: 'EA',
    })

    expect(createBusinessConsoleMaintenanceSparePartMutationOptions).toHaveBeenCalled()
    expect(coladaState.mutationCallsById.get('createBusinessConsoleMaintenanceSparePart')).toEqual([
      {
        body: expect.objectContaining({
          workOrderId: 'wo-1',
          skuCode: 'BRG-6205',
          quantity: 2,
        }),
      },
    ])
  })

  it('updates a plan in the current business scope and awaits the scoped list refresh', async () => {
    const context = useBusinessContextStore()
    context.patchContext({ organizationId: 'org-maint', environmentId: 'env-maint' })
    const plans = useMaintenancePlans()
    const refetch = coladaState.queryRefetchById.get('listBusinessConsoleMaintenancePlans')!
    let finishRefetch!: () => void
    refetch.mockReturnValue(
      new Promise<void>((resolve) => {
        finishRefetch = resolve
      }),
    )

    let updateSettled = false
    const updatePromise = plans
      .updatePlan('plan-945', {
        organizationId: 'org-maint',
        environmentId: 'env-maint',
        interval: null,
        runtimeHourInterval: 1200,
      })
      .then(() => {
        updateSettled = true
      })
    await Promise.resolve()

    expect(updateBusinessConsoleMaintenancePlanMutationOptions).toHaveBeenCalled()
    expect(coladaState.mutationCallsById.get('updateBusinessConsoleMaintenancePlan')).toEqual([
      {
        path: { planId: 'plan-945' },
        body: {
          organizationId: 'org-maint',
          environmentId: 'env-maint',
          interval: null,
          runtimeHourInterval: 1200,
        },
      },
    ])
    expect(refetch).toHaveBeenCalledOnce()
    expect(updateSettled).toBe(false)

    finishRefetch()
    await updatePromise
    expect(updateSettled).toBe(true)
  })

  it('keeps reliability disabled until a device is selected', () => {
    const context = useBusinessContextStore()
    context.patchContext({ organizationId: 'org-001', environmentId: 'env-dev' })
    const reliability = useMaintenanceReliability()

    expect(queryBusinessConsoleMaintenanceAssetReliabilityQueryOptions).toHaveBeenCalledWith({
      path: { deviceAssetId: '' },
      query: expect.objectContaining({
        organizationId: 'org-001',
        environmentId: 'env-dev',
      }),
    })
    expect(
      coladaState.queryOptionsById.get('queryBusinessConsoleMaintenanceAssetReliability')?.enabled,
    ).toBe(false)
    expect(reliability.reliability.value).toBeUndefined()
  })

  it('loads availability windows only for an explicit device scope', () => {
    const context = useBusinessContextStore()
    context.patchContext({ organizationId: 'org-001', environmentId: 'env-dev' })
    coladaState.queryDataById.set('queryBusinessConsoleMaintenanceAvailabilityWindows', {
      success: true,
      data: {
        items: [
          {
            deviceAssetId: 'DEV-CNC-01',
            availabilityStatus: 'unavailable',
            reasonCode: 'maintenance.pm',
          },
        ],
      },
    })

    const availability = useMaintenanceAvailabilityWindows({ deviceAssetIds: 'DEV-CNC-01' })

    expect(queryBusinessConsoleMaintenanceAvailabilityWindowsQueryOptions).toHaveBeenCalledWith({
      query: expect.objectContaining({
        organizationId: 'org-001',
        environmentId: 'env-dev',
        deviceAssetIds: 'DEV-CNC-01',
      }),
    })
    expect(
      coladaState.queryOptionsById.get('queryBusinessConsoleMaintenanceAvailabilityWindows')
        ?.enabled,
    ).toBe(true)
    expect('availability' in availability).toBe(false)
    expect(availability.availabilityWindows.value).toHaveLength(1)
  })

  it('disables maintenance list queries until business context is selected', () => {
    useMaintenanceInspections()

    expect(listBusinessConsoleMaintenanceInspectionsQueryOptions).toHaveBeenCalledWith({
      query: expect.objectContaining({ organizationId: '', environmentId: '' }),
    })
    expect(
      coladaState.queryOptionsById.get('listBusinessConsoleMaintenanceInspections')?.enabled,
    ).toBe(false)
  })

  it('does not refetch maintenance lists when business context is empty', async () => {
    const inspections = useMaintenanceInspections()
    const refetch = coladaState.queryRefetchById.get('listBusinessConsoleMaintenanceInspections')

    await inspections.refreshInspections()

    expect(refetch).not.toHaveBeenCalled()

    useBusinessContextStore().patchContext({
      organizationId: 'org-maint',
      environmentId: 'env-maint',
    })
    await inspections.refreshInspections()

    expect(refetch).toHaveBeenCalledOnce()
  })

  it('updates maintenance query scope when business context changes', () => {
    const context = useBusinessContextStore()
    context.patchContext({ organizationId: 'org-maint-a', environmentId: 'env-maint-a' })
    useMaintenanceSpareParts()

    context.patchContext({ organizationId: 'org-maint-b', environmentId: 'env-maint-b' })
    coladaState.queryFactoriesById.get('listBusinessConsoleMaintenanceSpareParts')?.()

    expect(listBusinessConsoleMaintenanceSparePartsQueryOptions).toHaveBeenLastCalledWith({
      query: expect.objectContaining({
        organizationId: 'org-maint-b',
        environmentId: 'env-maint-b',
      }),
    })
  })
})
