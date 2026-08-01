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
  listBusinessConsoleDeviceAssetsQueryOptions: vi.fn((request) => ({
    key: [{ _id: 'device-directory', request }],
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
    api.data.set(id, data)
    api.errors.set(id, error)
    return {
      data,
      error,
      isLoading: shallowRef(false),
      refetch: vi.fn(async () => undefined),
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

describe('useMaintenanceSelfWorkOrders', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    api.queryFactories.clear()
    api.data.clear()
    api.errors.clear()
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
    expect(api.queryFactories.get('device-directory')?.().enabled).toBe(false)
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

  it('binds device lookup to scope and requested stable ID, rejecting code matches and late old responses', async () => {
    seedPrincipal()
    const routeId = shallowRef('WO-NEW')
    const result = useMaintenanceSelfWorkOrderDetail(routeId)

    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: { workOrderId: 'WO-OLD', deviceAssetId: 'device-old' },
    }
    await nextTick()
    expect(result.workOrder.value).toBeUndefined()
    expect(requestFor('device-directory')).toEqual({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        includeDisabled: false,
        keyword: '',
        skip: 0,
        take: 20,
      },
    })

    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: { workOrderId: 'WO-NEW', deviceAssetId: 'device-new' },
    }
    await nextTick()
    expect(result.workOrder.value?.workOrderId).toBe('WO-NEW')
    expect(requestFor('device-directory')).toEqual({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        includeDisabled: false,
        keyword: 'device-new',
        skip: 0,
        take: 20,
      },
    })

    api.data.get('device-directory')!.value = {
      success: true,
      data: {
        resources: [
          {
            deviceAssetId: 'device-old',
            code: 'device-new',
            displayName: '迟到的旧设备',
            workshopCode: 'WS-OLD',
          },
        ],
        total: 1,
      },
    }
    await nextTick()
    expect(result.device.value).toBeUndefined()

    api.data.get('device-directory')!.value = {
      success: true,
      data: {
        resources: [
          { deviceAssetId: 'device-new', code: 'CNC-NEW', displayName: '重复设备一' },
          { deviceAssetId: ' device-new ', code: 'CNC-NEW-2', displayName: '重复设备二' },
        ],
        total: 2,
      },
    }
    await nextTick()
    expect(result.device.value).toBeUndefined()

    api.data.get('device-directory')!.value = {
      success: true,
      data: {
        resources: [
          {
            deviceAssetId: 'device-new',
            code: 'CNC-NEW',
            displayName: '一号数控机床',
            workshopCode: 'WS-1',
          },
        ],
        total: 1,
      },
    }
    await nextTick()
    expect(result.device.value).toMatchObject({
      deviceAssetId: 'device-new',
      code: 'CNC-NEW',
      displayName: '一号数控机床',
      workshopCode: 'WS-1',
    })

    routeId.value = 'WO-LATEST'
    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: { workOrderId: 'WO-LATEST', deviceAssetId: 'device-latest' },
    }
    await nextTick()
    expect(result.device.value).toBeUndefined()

    api.data.get('device-directory')!.value = {
      success: true,
      data: {
        resources: [
          {
            deviceAssetId: 'device-new',
            code: 'CNC-NEW',
            displayName: '迟到的一号数控机床',
          },
        ],
      },
    }
    await nextTick()
    expect(result.device.value).toBeUndefined()
  })
})
