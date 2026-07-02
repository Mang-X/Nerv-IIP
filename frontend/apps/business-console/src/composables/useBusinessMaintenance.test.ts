import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'

import {
  createBusinessConsoleMaintenanceSparePartMutationOptions,
  listBusinessConsoleMaintenanceInspectionsQueryOptions,
  listBusinessConsoleMaintenanceSparePartsQueryOptions,
  queryBusinessConsoleMaintenanceAssetReliabilityQueryOptions,
  queryBusinessConsoleMaintenanceAvailabilityWindowsQueryOptions,
  recordBusinessConsoleMaintenanceInspectionMutationOptions,
} from '@nerv-iip/api-client'
import {
  useMaintenanceAvailabilityWindows,
  useMaintenanceInspections,
  useMaintenanceReliability,
  useMaintenanceSpareParts,
} from './useBusinessMaintenance'

const coladaState = vi.hoisted(() => ({
  mutationCallsById: new Map<string, unknown[]>(),
  queryDataById: new Map<string, unknown>(),
  queryOptionsById: new Map<string, { enabled?: boolean }>(),
}))

vi.mock('@nerv-iip/api-client', () => ({
  completeBusinessConsoleMaintenanceWorkOrderMutationOptions: vi.fn(() => ({ key: [], mutation: vi.fn() })),
  createBusinessConsoleMaintenancePlanMutationOptions: vi.fn(() => ({ key: [], mutation: vi.fn() })),
  createBusinessConsoleMaintenanceSparePartMutationOptions: vi.fn(() => ({
    key: [{ _id: 'createBusinessConsoleMaintenanceSparePart' }],
    mutation: vi.fn(),
  })),
  createBusinessConsoleMaintenanceWorkOrderMutationOptions: vi.fn(() => ({ key: [], mutation: vi.fn() })),
  generateDueBusinessConsoleMaintenanceWorkOrdersMutationOptions: vi.fn(() => ({ key: [], mutation: vi.fn() })),
  listBusinessConsoleMaintenanceInspectionsQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleMaintenanceInspections' }],
    query: vi.fn(),
  })),
  listBusinessConsoleMaintenancePlansQueryOptions: vi.fn(() => ({ key: [], query: vi.fn() })),
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
        options.onSuccess?.()
        return { success: true, data: {} }
      }),
    }
  }),
  useQuery: vi.fn((optionsFactory) => {
    const options = optionsFactory()
    const key = Array.isArray(options.key) ? options.key[0] : undefined
    const id = key && typeof key === 'object' && '_id' in key ? String(key._id) : ''
    coladaState.queryOptionsById.set(id, options)

    return {
      data: shallowRef(coladaState.queryDataById.get(id)),
      error: shallowRef(),
      isLoading: shallowRef(false),
      refetch: vi.fn(),
    }
  }),
}))

describe('business maintenance composables', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    coladaState.mutationCallsById.clear()
    coladaState.queryDataById.clear()
    coladaState.queryOptionsById.clear()
  })

  it('loads inspection rows and records a real inspection through the facade', async () => {
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
    expect(coladaState.mutationCallsById.get('recordBusinessConsoleMaintenanceInspection')).toEqual([
      {
        body: expect.objectContaining({
          inspector: '设备保全班',
          planId: 'plan-1',
          workOrderId: 'wo-1',
        }),
      },
    ])
  })

  it('loads spare part requests and creates a request without inventing inventory balance', async () => {
    coladaState.queryDataById.set('listBusinessConsoleMaintenanceSpareParts', {
      success: true,
      data: {
        items: [{ sparePartRequestId: 'sp-1', workOrderId: 'wo-1', skuCode: 'BRG-6205', quantity: 2 }],
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

  it('keeps reliability disabled until a device is selected', () => {
    const reliability = useMaintenanceReliability()

    expect(queryBusinessConsoleMaintenanceAssetReliabilityQueryOptions).toHaveBeenCalledWith({
      path: { deviceAssetId: '' },
      query: expect.objectContaining({
        organizationId: 'org-001',
        environmentId: 'env-dev',
      }),
    })
    expect(coladaState.queryOptionsById.get('queryBusinessConsoleMaintenanceAssetReliability')?.enabled).toBe(false)
    expect(reliability.reliability.value).toBeUndefined()
  })

  it('loads availability windows only for an explicit device scope', () => {
    coladaState.queryDataById.set('queryBusinessConsoleMaintenanceAvailabilityWindows', {
      success: true,
      data: {
        items: [{ deviceAssetId: 'DEV-CNC-01', availabilityStatus: 'unavailable', reasonCode: 'maintenance.pm' }],
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
    expect(coladaState.queryOptionsById.get('queryBusinessConsoleMaintenanceAvailabilityWindows')?.enabled).toBe(true)
    expect(availability.availabilityWindows.value).toHaveLength(1)
  })
})
