import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, nextTick, shallowRef, type ShallowRef } from 'vue'

import { useAuthStore } from '@/stores/auth'
import {
  useMaintenanceSelfWorkOrderDetail,
  useMaintenanceSelfWorkOrders,
} from './useMaintenanceSelfWorkOrders'

const api = vi.hoisted(() => ({
  list: vi.fn(),
  queryFactories: new Map<string, () => Record<string, unknown>>(),
  data: new Map<string, ShallowRef<unknown>>(),
  errors: new Map<string, ShallowRef<unknown>>(),
  loading: new Map<string, ShallowRef<boolean>>(),
  refetches: new Map<string, ReturnType<typeof vi.fn>>(),
}))

vi.mock('@nerv-iip/api-client', () => ({
  listBusinessConsoleMaintenanceWorkOrders: api.list,
  listBusinessConsoleMaintenanceWorkOrdersQueryOptions: vi.fn((request) => ({
    key: [{ _id: 'maintenance-list', request }],
    query: vi.fn(),
  })),
  getBusinessConsoleMaintenanceWorkOrderQueryOptions: vi.fn((request) => ({
    key: [{ _id: 'maintenance-detail', request }],
    query: vi.fn(),
  })),
  getBusinessConsoleMasterDataResourceDetailQueryOptions: vi.fn((request) => ({
    key: [{ _id: 'device-detail', request }],
    query: vi.fn(),
  })),
  getConsolePrincipal: vi.fn(),
  loginConsoleUser: vi.fn(),
  logoutConsoleSession: vi.fn(),
  refreshConsoleSession: vi.fn(),
}))

vi.mock('@pinia/colada', () => ({
  useQuery: vi.fn((factory: () => Record<string, unknown>) => {
    const options = factory()
    const key = (options.key as Array<{ _id?: string }> | undefined)?.[0]
    const id = key?._id ?? ''
    api.queryFactories.set(id, factory)
    const data = shallowRef()
    const error = shallowRef()
    const isLoading = shallowRef(false)
    const refetch = vi.fn(async () => undefined)
    api.data.set(id, data)
    api.errors.set(id, error)
    api.loading.set(id, isLoading)
    api.refetches.set(id, refetch)
    return {
      data,
      error,
      isLoading,
      refetch,
    }
  }),
}))

function seedPrincipal(overrides: Record<string, unknown> = {}) {
  useAuthStore().$patch((state) => {
    state.principal = {
      principalId: 'principal-1',
      principalType: 'User',
      loginName: 'technician01',
      organizationId: 'org-001',
      environmentId: 'env-dev',
      permissionCodes: [
        'business.maintenance.work-orders.read',
        'business.masterdata.resources.read',
      ],
      ...overrides,
    } as never
  })
}

function requestFor(id: string) {
  const options = api.queryFactories.get(id)?.()
  return (options?.key as Array<{ request?: unknown }> | undefined)?.[0]?.request
}

function authoritativeDetail(workOrderId: string, deviceAssetId: string) {
  return {
    workOrderId,
    deviceAssetId,
    priority: 'high',
    status: 'accepted',
    openedAtUtc: '2026-08-02T01:00:00.000Z',
    version: 7,
    allowedActions: [],
    blockReasons: [],
    lifecycle: [],
    assignedTechnicianUserId: null,
    assignedTeamId: null,
  }
}

const invalidListEnvelopes = [
  ['null data', { success: true, data: null }],
  ['missing data', { success: true }],
  ['non-array items', { success: true, data: { items: {}, total: 0 } }],
  ['negative total', { success: true, data: { items: [], total: -1 } }],
  ['non-integer total', { success: true, data: { items: [], total: 0.5 } }],
  ['inconsistent total', { success: true, data: { items: [{ workOrderId: 'WO-1' }], total: 0 } }],
  ['null item', { success: true, data: { items: [null], total: 1 } }],
  ['primitive item', { success: true, data: { items: ['WO-1'], total: 1 } }],
  ['blank strong ID', { success: true, data: { items: [{ workOrderId: ' ' }], total: 1 } }],
] as const

describe('useMaintenanceSelfWorkOrders', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    api.queryFactories.clear()
    api.data.clear()
    api.errors.clear()
    api.loading.clear()
    api.refetches.clear()
  })

  it('suppresses the personal queue when principal scope or read permission is unavailable', () => {
    seedPrincipal({ principalId: '', permissionCodes: [] })

    const result = useMaintenanceSelfWorkOrders()
    const options = api.queryFactories.get('maintenance-list')?.()

    expect(result.scopeReady.value).toBe(false)
    expect(options?.enabled).toBe(false)
    expect(result.items.value).toEqual([])
  })

  it('fails closed when maintenance read exists without device location read permission', () => {
    seedPrincipal({ permissionCodes: ['business.maintenance.work-orders.read'] })

    const list = useMaintenanceSelfWorkOrders()
    const detail = useMaintenanceSelfWorkOrderDetail(computed(() => 'WO-DETAIL'))

    expect(list.scopeReady.value).toBe(false)
    expect(api.queryFactories.get('maintenance-list')?.().enabled).toBe(false)
    expect(detail.enabled.value).toBe(false)
    expect(api.queryFactories.get('maintenance-detail')?.().enabled).toBe(false)
    expect(api.queryFactories.get('device-detail')?.().enabled).toBe(false)
  })

  it('binds status, device, keyword, and first-page pagination to the server self query', async () => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrders()

    result.filters.status = 'accepted'
    result.filters.deviceAssetId = 'device-1'
    result.filters.keyword = '主轴'
    await nextTick()

    expect(requestFor('maintenance-list')).toEqual({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        scopeKind: 'self',
        scopeId: 'principal-1',
        status: 'accepted',
        deviceAssetId: 'device-1',
        keyword: '主轴',
        skip: 0,
        take: 20,
      },
    })
  })

  it('drops an unknown restored status instead of sending it to the service', async () => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrders()

    ;(result.filters as { status: string }).status = 'future-server-state'
    await nextTick()

    expect(requestFor('maintenance-list')).toEqual({
      query: expect.not.objectContaining({ status: expect.anything() }),
    })
  })

  it('clears visible rows and reports failure for a rejected HTTP 200 envelope', async () => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrders()

    api.data.get('maintenance-list')!.value = {
      success: true,
      data: { items: [{ workOrderId: 'STALE-WO' }], total: 1 },
    }
    await nextTick()
    expect(result.items.value).toHaveLength(1)

    api.data.get('maintenance-list')!.value = {
      success: false,
      data: { items: [{ workOrderId: 'BAD-WO' }], total: 1 },
    }
    await nextTick()

    expect(result.hasFailedResponse.value).toBe(true)
    expect(result.items.value).toEqual([])
    expect(result.total.value).toBe(0)
  })

  it.each(invalidListEnvelopes)(
    'rejects a malformed first-page envelope: %s',
    async (_, envelope) => {
      seedPrincipal()
      const result = useMaintenanceSelfWorkOrders()

      api.data.get('maintenance-list')!.value = envelope
      await nextTick()

      expect(result.hasSuccessfulResponse.value).toBe(false)
      expect(result.hasFailedResponse.value).toBe(true)
      expect(result.items.value).toEqual([])
      expect(result.total.value).toBe(0)
    },
  )

  it.each(invalidListEnvelopes)(
    'rejects a malformed next-page envelope: %s',
    async (_, envelope) => {
      seedPrincipal()
      const result = useMaintenanceSelfWorkOrders()
      api.data.get('maintenance-list')!.value = {
        success: true,
        data: {
          items: Array.from({ length: 20 }, (_, index) => ({ workOrderId: `WO-${index}` })),
          total: 21,
        },
      }
      api.list.mockResolvedValueOnce({ data: envelope })
      await nextTick()

      await result.loadMore()

      expect(result.loadMoreError.value).toBeInstanceOf(Error)
      expect(result.items.value).toHaveLength(20)
    },
  )

  it('hides retained rows while the current list identity is refreshing and keeps them hidden on failure', async () => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrders()
    api.data.get('maintenance-list')!.value = {
      success: true,
      data: { items: [{ workOrderId: 'STALE-WO' }], total: 1 },
    }
    await nextTick()
    expect(result.items.value).toHaveLength(1)

    let resolveRefetch!: () => void
    api.refetches
      .get('maintenance-list')!
      .mockImplementationOnce(() => new Promise<void>((resolve) => (resolveRefetch = resolve)))
    const refreshPromise = result.refresh()
    await nextTick()

    expect(result.pending.value).toBe(true)
    expect(result.hasSuccessfulResponse.value).toBe(false)
    expect(result.items.value).toEqual([])
    expect(result.total.value).toBe(0)

    api.data.get('maintenance-list')!.value = { success: false, data: null }
    resolveRefetch()
    await refreshPromise
    await nextTick()

    expect(result.hasFailedResponse.value).toBe(true)
    expect(result.items.value).toEqual([])
  })

  it('loads the next page with the same self scope and server filters', async () => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrders()
    result.filters.status = 'inProgress'
    result.filters.deviceAssetId = 'device-2'
    result.filters.keyword = '轴承'
    api.data.get('maintenance-list')!.value = {
      success: true,
      data: {
        items: Array.from({ length: 20 }, (_, index) => ({ workOrderId: `WO-${index}` })),
        total: 21,
      },
    }
    api.list.mockResolvedValueOnce({
      data: { success: true, data: { items: [{ workOrderId: 'WO-20' }], total: 21 } },
    })
    await nextTick()

    await result.loadMore()

    expect(api.list).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        scopeKind: 'self',
        scopeId: 'principal-1',
        status: 'inProgress',
        deviceAssetId: 'device-2',
        keyword: '轴承',
        skip: 20,
        take: 20,
      },
      throwOnError: true,
    })
    expect(result.items.value.at(-1)?.workOrderId).toBe('WO-20')
  })
})

describe('useMaintenanceSelfWorkOrderDetail', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    api.queryFactories.clear()
    api.data.clear()
    api.errors.clear()
    api.loading.clear()
    api.refetches.clear()
  })

  it('revalidates the strong ID with the authenticated self scope', () => {
    seedPrincipal()
    useMaintenanceSelfWorkOrderDetail(computed(() => 'WO-DETAIL'))

    expect(requestFor('maintenance-detail')).toEqual({
      path: { workOrderId: 'WO-DETAIL' },
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        scopeKind: 'self',
        scopeId: 'principal-1',
      },
    })
  })

  it('rejects and clears a matching-ID detail when authoritative fields are incomplete', async () => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrderDetail(computed(() => 'WO-DETAIL'))

    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: {
        workOrderId: 'WO-DETAIL',
        deviceAssetId: 'device-1',
        priority: 'high',
        status: 'accepted',
        openedAtUtc: '2026-08-02T01:00:00.000Z',
        version: 7,
        allowedActions: [],
        blockReasons: [],
        lifecycle: [],
        assignedTechnicianUserId: null,
        assignedTeamId: null,
      },
    }
    await nextTick()
    expect(result.workOrder.value).toBeUndefined()
    expect(result.pending.value).toBe(true)

    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: {
        workOrderId: 'WO-DETAIL',
        deviceAssetId: 'device-1',
        priority: 'high',
        status: 'accepted',
        openedAtUtc: '2026-08-02T01:00:00.000Z',
        version: 7,
        allowedActions: [],
        blockReasons: [],
      },
    }
    await nextTick()

    expect(result.workOrder.value).toBeUndefined()
    expect(result.hasFailedResponse.value).toBe(true)
    expect(api.queryFactories.get('device-detail')?.().enabled).toBe(false)
  })

  it.each([
    ['technician', { assignedTeamId: null }],
    ['team', { assignedTechnicianUserId: null }],
    ['both', {}],
  ])('rejects detail when the explicit %s assignment field is missing', async (_, assignments) => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrderDetail(computed(() => 'WO-DETAIL'))
    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: {
        ...authoritativeDetail('WO-DETAIL', 'device-1'),
        assignedTechnicianUserId: undefined,
        assignedTeamId: undefined,
        ...assignments,
      },
    }
    const data = (api.data.get('maintenance-detail')!.value as { data: Record<string, unknown> })
      .data
    if (!('assignedTechnicianUserId' in assignments)) delete data.assignedTechnicianUserId
    if (!('assignedTeamId' in assignments)) delete data.assignedTeamId
    await nextTick()

    expect(result.workOrder.value).toBeUndefined()
    expect(result.hasFailedResponse.value).toBe(true)
    expect(api.queryFactories.get('device-detail')?.().enabled).toBe(false)
  })

  it.each([
    ['actor principal', { actorPrincipalId: ' ' }],
    ['reason', { reason: '' }],
    ['technician', { technicianUserId: undefined }],
    ['team', { teamId: undefined }],
  ])('rejects detail when a lifecycle event has an invalid %s audit field', async (_, override) => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrderDetail(computed(() => 'WO-DETAIL'))
    const event: Record<string, unknown> = {
      action: 'accept',
      fromStatus: 'opened',
      toStatus: 'accepted',
      actorPrincipalId: 'principal-1',
      technicianUserId: null,
      teamId: null,
      reason: '现场接单',
      resultingVersion: 2,
      occurredAtUtc: '2026-08-02T01:00:00.000Z',
      ...override,
    }
    if (Object.hasOwn(override, 'technicianUserId')) delete event.technicianUserId
    if (Object.hasOwn(override, 'teamId')) delete event.teamId
    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: { ...authoritativeDetail('WO-DETAIL', 'device-1'), lifecycle: [event] },
    }
    await nextTick()

    expect(result.workOrder.value).toBeUndefined()
    expect(result.hasFailedResponse.value).toBe(true)
    expect(api.queryFactories.get('device-detail')?.().enabled).toBe(false)
  })

  it.each([
    ['closed status', { status: 'closed', allowedActions: ['start'] }],
    ['cancelled status', { status: 'cancelled', allowedActions: ['accept'] }],
    ['terminal block reason', { blockReasons: ['terminal-status'], allowedActions: ['start'] }],
  ])('rejects contradictory terminal action facts: %s', async (_, override) => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrderDetail(computed(() => 'WO-DETAIL'))
    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: { ...authoritativeDetail('WO-DETAIL', 'device-1'), ...override },
    }
    await nextTick()

    expect(result.workOrder.value).toBeUndefined()
    expect(result.hasFailedResponse.value).toBe(true)
    expect(api.queryFactories.get('device-detail')?.().enabled).toBe(false)
  })

  it('treats the strong-ID device detail as part of aggregate detail state', async () => {
    seedPrincipal()
    const routeId = shallowRef('WO-NEW')
    const result = useMaintenanceSelfWorkOrderDetail(routeId)

    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: { workOrderId: 'WO-OLD', deviceAssetId: 'device-old' },
    }
    await nextTick()
    expect(result.workOrder.value).toBeUndefined()
    expect(requestFor('device-detail')).toEqual({
      path: { resourceType: 'device-asset', code: '' },
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
      },
    })

    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: authoritativeDetail('WO-NEW', 'device-new'),
    }
    await nextTick()
    expect(result.workOrder.value).toBeUndefined()
    expect(result.pending.value).toBe(true)
    expect(requestFor('device-detail')).toEqual({
      path: { resourceType: 'device-asset', code: 'device-new' },
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
      },
    })

    api.data.get('device-detail')!.value = {
      success: true,
      data: {
        resourceType: 'device-asset',
        deviceAssetId: 'device-old',
        code: 'CNC-OLD',
        displayName: '迟到的旧设备',
        organizationId: 'org-001',
        environmentId: 'env-dev',
      },
    }
    await nextTick()
    expect(result.device.value).toBeUndefined()
    expect(result.workOrder.value).toBeUndefined()
    expect(result.hasFailedResponse.value).toBe(true)

    api.data.get('device-detail')!.value = undefined
    api.errors.get('device-detail')!.value = { status: 403 }
    await nextTick()
    expect(result.device.value).toBeUndefined()
    expect(result.workOrder.value).toBeUndefined()
    expect(result.hasFailedResponse.value).toBe(true)
    expect(result.error.value).toEqual({ status: 403 })

    api.errors.get('device-detail')!.value = undefined
    api.data.get('device-detail')!.value = { success: false, data: null }
    await nextTick()
    expect(result.hasFailedResponse.value).toBe(true)

    api.data.get('device-detail')!.value = {
      success: true,
      data: {
        resourceType: 'work-center',
        deviceAssetId: 'device-new',
        code: 'CNC-NEW',
        organizationId: 'org-001',
        environmentId: 'env-dev',
      },
    }
    await nextTick()
    expect(result.hasFailedResponse.value).toBe(true)

    api.data.get('device-detail')!.value = {
      success: true,
      data: {
        resourceType: 'device-asset',
        deviceAssetId: 'device-new',
        code: 'CNC-NEW',
        organizationId: 'org-other',
        environmentId: 'env-dev',
      },
    }
    await nextTick()
    expect(result.device.value).toBeUndefined()
    expect(result.workOrder.value).toBeUndefined()
    expect(result.hasFailedResponse.value).toBe(true)

    api.data.get('device-detail')!.value = {
      success: true,
      data: {
        resourceType: 'device-asset',
        deviceAssetId: 'device-new',
        code: 'CNC-NEW',
        displayName: '一号数控机床',
        organizationId: 'org-001',
        environmentId: 'env-dev',
        workshopCode: 'WS-1',
      },
    }
    await nextTick()
    expect(result.device.value).toMatchObject({
      deviceAssetId: 'device-new',
      code: 'CNC-NEW',
      displayName: '一号数控机床',
      workshopCode: 'WS-1',
    })
    expect(result.workOrder.value?.workOrderId).toBe('WO-NEW')
    expect(result.hasSuccessfulResponse.value).toBe(true)
    expect(result.hasFailedResponse.value).toBe(false)

    api.errors.get('maintenance-detail')!.value = { status: 403 }
    await nextTick()
    expect(result.workOrder.value).toBeUndefined()
    expect(result.device.value).toBeUndefined()
    expect(result.hasFailedResponse.value).toBe(true)
    api.errors.get('maintenance-detail')!.value = undefined

    routeId.value = 'WO-LATEST'
    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: authoritativeDetail('WO-LATEST', 'device-latest'),
    }
    await nextTick()
    expect(result.device.value).toBeUndefined()
    expect(result.workOrder.value).toBeUndefined()
    expect(result.pending.value).toBe(true)

    api.data.get('device-detail')!.value = {
      success: true,
      data: {
        resourceType: 'device-asset',
        deviceAssetId: 'device-new',
        code: 'CNC-NEW',
        displayName: '迟到的一号数控机床',
        organizationId: 'org-001',
        environmentId: 'env-dev',
      },
    }
    await nextTick()
    expect(result.device.value).toBeUndefined()
    expect(result.workOrder.value).toBeUndefined()
    expect(result.hasFailedResponse.value).toBe(true)
  })

  it('retries the work order first and then the newly resolved device detail', async () => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrderDetail(computed(() => 'WO-DETAIL'))
    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: authoritativeDetail('WO-DETAIL', 'device-1'),
    }
    await nextTick()

    const calls: string[] = []
    api.refetches.get('maintenance-detail')!.mockImplementation(async () => {
      calls.push('work-order')
    })
    api.refetches.get('device-detail')!.mockImplementation(async () => {
      calls.push('device')
    })

    await result.refresh()

    expect(calls).toEqual(['work-order', 'device'])

    calls.length = 0
    api.refetches.get('maintenance-detail')!.mockImplementation(async () => {
      calls.push('work-order')
      api.errors.get('maintenance-detail')!.value = { status: 403 }
    })

    await result.refresh()

    expect(calls).toEqual(['work-order'])
  })

  it('hides retained aggregate detail during work-order and device refreshes', async () => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrderDetail(computed(() => 'WO-DETAIL'))
    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: authoritativeDetail('WO-DETAIL', 'device-1'),
    }
    api.data.get('device-detail')!.value = {
      success: true,
      data: {
        resourceType: 'device-asset',
        deviceAssetId: 'device-1',
        code: 'CNC-01',
        displayName: '旧设备',
        organizationId: 'org-001',
        environmentId: 'env-dev',
      },
    }
    await nextTick()
    expect(result.workOrder.value?.workOrderId).toBe('WO-DETAIL')
    expect(result.device.value?.displayName).toBe('旧设备')

    api.loading.get('maintenance-detail')!.value = true
    await nextTick()
    expect(result.pending.value).toBe(true)
    expect(result.hasSuccessfulResponse.value).toBe(false)
    expect(result.workOrder.value).toBeUndefined()
    expect(result.device.value).toBeUndefined()

    api.loading.get('maintenance-detail')!.value = false
    api.errors.get('maintenance-detail')!.value = { status: 403 }
    await nextTick()
    expect(result.hasFailedResponse.value).toBe(true)
    expect(result.workOrder.value).toBeUndefined()

    api.errors.get('maintenance-detail')!.value = undefined
    api.loading.get('device-detail')!.value = true
    await nextTick()
    expect(result.pending.value).toBe(true)
    expect(result.hasSuccessfulResponse.value).toBe(false)
    expect(result.workOrder.value).toBeUndefined()
    expect(result.device.value).toBeUndefined()

    api.loading.get('device-detail')!.value = false
    api.data.get('device-detail')!.value = { success: false, data: null }
    await nextTick()
    expect(result.hasFailedResponse.value).toBe(true)
    expect(result.workOrder.value).toBeUndefined()
  })
})
