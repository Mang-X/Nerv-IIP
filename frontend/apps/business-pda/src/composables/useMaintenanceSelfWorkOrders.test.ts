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
  detail: vi.fn(),
  workers: vi.fn(),
  resourceDetail: vi.fn(),
  queryFactories: new Map<string, () => Record<string, unknown>>(),
  data: new Map<string, ShallowRef<unknown>>(),
  rawData: new Map<string, ShallowRef<unknown>>(),
  errors: new Map<string, ShallowRef<unknown>>(),
  loading: new Map<string, ShallowRef<boolean>>(),
  refetches: new Map<string, ReturnType<typeof vi.fn>>(),
}))

vi.mock('@nerv-iip/api-client', () => ({
  listBusinessConsoleMaintenanceWorkOrders: api.list,
  getBusinessConsoleMaintenanceWorkOrder: api.detail,
  listBusinessConsoleWorkers: api.workers,
  getBusinessConsoleMasterDataResourceDetail: api.resourceDetail,
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
    const rawData = shallowRef()
    const data = {
      get value() {
        return rawData.value
      },
      set value(value: unknown) {
        if (value === undefined) {
          rawData.value = undefined
          return
        }
        if (
          value &&
          typeof value === 'object' &&
          'generation' in value &&
          'identity' in value &&
          'value' in value
        ) {
          rawData.value = value
          return
        }
        const currentOptions = factory()
        const currentKey = (currentOptions.key as Array<Record<string, unknown>> | undefined)?.[0]
        rawData.value = {
          generation: currentKey?.generation,
          identity: currentKey?.identity,
          value,
        }
      },
    } as ShallowRef<unknown>
    const error = shallowRef()
    const isLoading = shallowRef(false)
    const refetch = vi.fn(async () => undefined)
    api.data.set(id, data)
    api.rawData.set(id, rawData)
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

function queryMetadata(id: string) {
  const options = api.queryFactories.get(id)?.()
  return (options?.key as Array<Record<string, unknown>> | undefined)?.[0]
}

function publishedValue(id: string) {
  const published = api.data.get(id)?.value as { value?: unknown } | undefined
  return published?.value
}

function publishCached(id: string, generation: number, identity: string, value: unknown) {
  api.rawData.get(id)!.value = { generation, identity, value }
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
    assignedTechnicianUserId: 'principal-1',
    assignedTeamId: null,
  }
}

function workOrderGuid(index: number) {
  return `019f0000-0000-7000-8000-${String(index).padStart(12, '0')}`
}

function authoritativeListItem(index: number, overrides: Record<string, unknown> = {}) {
  return {
    workOrderId: workOrderGuid(index),
    deviceAssetId: 'DEV-CNC-01',
    priority: 'high',
    status: 'accepted',
    openedAtUtc: '2026-08-02T01:00:00.000Z',
    version: 7,
    assignedTechnicianUserId: 'principal-1',
    ...overrides,
  }
}

function withoutListField(field: string) {
  const item = authoritativeListItem(1) as Record<string, unknown>
  delete item[field]
  return item
}

function withPagination(envelope: unknown, skip: number, take: number) {
  if (!envelope || typeof envelope !== 'object' || Array.isArray(envelope)) return envelope
  const response = envelope as { data?: unknown }
  if (!response.data || typeof response.data !== 'object' || Array.isArray(response.data)) {
    return envelope
  }
  return { ...response, data: { ...response.data, skip, take } }
}

const invalidListEnvelopes = [
  ['null data', { success: true, data: null }],
  ['missing data', { success: true }],
  ['non-array items', { success: true, data: { items: {}, total: 0 } }],
  ['negative total', { success: true, data: { items: [], total: -1 } }],
  ['non-integer total', { success: true, data: { items: [], total: 0.5 } }],
  ['inconsistent total', { success: true, data: { items: [authoritativeListItem(1)], total: 0 } }],
  ['null item', { success: true, data: { items: [null], total: 1 } }],
  ['primitive item', { success: true, data: { items: ['WO-1'], total: 1 } }],
  ['blank strong ID', { success: true, data: { items: [{ workOrderId: ' ' }], total: 1 } }],
  [
    'non-GUID strong ID',
    {
      success: true,
      data: { items: [authoritativeListItem(1, { workOrderId: 'WO-1' })], total: 1 },
    },
  ],
  [
    'empty GUID',
    {
      success: true,
      data: {
        items: [
          authoritativeListItem(1, {
            workOrderId: '00000000-0000-0000-0000-000000000000',
          }),
        ],
        total: 1,
      },
    },
  ],
  [
    'numeric source reference',
    {
      success: true,
      data: { items: [authoritativeListItem(1, { sourceReferenceId: 42 })], total: 1 },
    },
  ],
  [
    'object source reference',
    {
      success: true,
      data: { items: [authoritativeListItem(1, { sourceReferenceId: {} })], total: 1 },
    },
  ],
  [
    'missing device',
    { success: true, data: { items: [withoutListField('deviceAssetId')], total: 1 } },
  ],
  [
    'missing priority',
    { success: true, data: { items: [withoutListField('priority')], total: 1 } },
  ],
  ['missing status', { success: true, data: { items: [withoutListField('status')], total: 1 } }],
  ['missing version', { success: true, data: { items: [withoutListField('version')], total: 1 } }],
  [
    'unsafe version',
    {
      success: true,
      data: {
        items: [authoritativeListItem(1, { version: Number.MAX_SAFE_INTEGER + 1 })],
        total: 1,
      },
    },
  ],
  [
    'invalid date',
    {
      success: true,
      data: {
        items: [authoritativeListItem(1, { openedAtUtc: '2026-02-30T01:00:00Z' })],
        total: 1,
      },
    },
  ],
  [
    'timezone-less date',
    {
      success: true,
      data: { items: [authoritativeListItem(1, { openedAtUtc: '2026-08-02T01:00:00' })], total: 1 },
    },
  ],
] as const

describe('useMaintenanceSelfWorkOrders', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    api.queryFactories.clear()
    api.data.clear()
    api.rawData.clear()
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
    const detail = useMaintenanceSelfWorkOrderDetail(
      computed(() => '019f0000-0000-7000-8000-000000000101'),
    )

    expect(list.scopeReady.value).toBe(false)
    expect(api.queryFactories.get('maintenance-list')?.().enabled).toBe(false)
    expect(detail.enabled.value).toBe(false)
    expect(api.queryFactories.get('maintenance-detail')?.().enabled).toBe(false)
    expect(api.queryFactories.get('device-detail')?.().enabled).toBe(false)
  })

  it('uses a non-repeating component-instance identity for every list and detail query family', async () => {
    seedPrincipal()
    useMaintenanceSelfWorkOrders()
    const firstList = queryMetadata('maintenance-list')
    const firstPrincipal = queryMetadata('maintenance-list-principal')
    useMaintenanceSelfWorkOrderDetail(computed(() => workOrderGuid(101)))
    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: authoritativeDetail(workOrderGuid(101), 'DEV-CNC-01'),
    }
    await nextTick()
    const firstDetail = queryMetadata('maintenance-detail')
    const firstDevice = queryMetadata('device-detail')
    const firstIdentities = queryMetadata('maintenance-identities')

    useMaintenanceSelfWorkOrders()
    const secondList = queryMetadata('maintenance-list')
    const secondPrincipal = queryMetadata('maintenance-list-principal')
    useMaintenanceSelfWorkOrderDetail(computed(() => workOrderGuid(101)))
    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: authoritativeDetail(workOrderGuid(101), 'DEV-CNC-01'),
    }
    await nextTick()
    const secondDetail = queryMetadata('maintenance-detail')
    const secondDevice = queryMetadata('device-detail')
    const secondIdentities = queryMetadata('maintenance-identities')

    for (const [first, second] of [
      [firstList, secondList],
      [firstPrincipal, secondPrincipal],
      [firstDetail, secondDetail],
      [firstDevice, secondDevice],
      [firstIdentities, secondIdentities],
    ]) {
      expect(first?.identity).toBeTypeOf('string')
      expect(second?.identity).toBeTypeOf('string')
      expect(second?.identity).not.toBe(first?.identity)
    }
  })

  it('binds status, device, keyword, and first-page pagination to the server self query', async () => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrders()

    result.filters.status = 'accepted'
    result.filters.deviceAssetIds = ['019f0000-0000-7000-8000-000000000001', 'DEV-CNC-01']
    result.filters.keyword = '主轴'
    await nextTick()

    expect(requestFor('maintenance-list')).toEqual({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        scopeKind: 'self',
        scopeId: 'principal-1',
        status: 'accepted',
        deviceAssetIds: '019f0000-0000-7000-8000-000000000001,DEV-CNC-01',
        keyword: '主轴',
        skip: 0,
        take: 20,
      },
    })
  })

  it('normalizes GUID filters case-insensitively while preserving code case and Ordinal dedupe', async () => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrders()
    const publicId = '019f0000-0000-7000-8000-000000000001'

    result.filters.deviceAssetIds = [publicId.toUpperCase(), publicId]
    await nextTick()
    expect(
      (requestFor('maintenance-list') as { query: { deviceAssetIds?: string } }).query
        .deviceAssetIds,
    ).toBe(publicId)

    result.filters.deviceAssetIds = ['DEV-A', 'dev-a', 'DEV-A']
    await nextTick()
    expect(
      (requestFor('maintenance-list') as { query: { deviceAssetIds?: string } }).query
        .deviceAssetIds,
    ).toBe('DEV-A,dev-a')
  })

  it('keeps scope and filter query identities collision-free for delimiter-bearing values', async () => {
    seedPrincipal({ organizationId: 'org:env', environmentId: 'x' })
    const result = useMaintenanceSelfWorkOrders()
    const firstScopeKey = result.scopeKey.value
    const firstIdentity = queryMetadata('maintenance-list')?.identity

    seedPrincipal({ organizationId: 'org', environmentId: 'env:x' })
    await nextTick()
    expect(result.scopeKey.value).not.toBe(firstScopeKey)
    expect(queryMetadata('maintenance-list')?.identity).not.toBe(firstIdentity)

    result.filters.deviceAssetIds = ['DEV-A,DEV-B']
    result.filters.keyword = `bearing\u001fteam`
    await nextTick()
    const delimiterIdentity = queryMetadata('maintenance-list')?.identity

    result.filters.deviceAssetIds = ['DEV-A', 'DEV-B']
    result.filters.keyword = 'bearing'
    await nextTick()
    expect(queryMetadata('maintenance-list')?.identity).not.toBe(delimiterIdentity)
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
      data: {
        items: [authoritativeListItem(1)],
        total: 1,
        skip: 0,
        take: 20,
      },
    }
    await nextTick()
    expect(result.items.value).toHaveLength(1)

    api.data.get('maintenance-list')!.value = {
      success: false,
      data: {
        items: [authoritativeListItem(2)],
        total: 1,
        skip: 0,
        take: 20,
      },
    }
    await nextTick()

    expect(result.hasFailedResponse.value).toBe(true)
    expect(result.items.value).toEqual([])
    expect(result.total.value).toBe(0)
  })

  it('normalizes uppercase public GUIDs returned by the list contract', async () => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrders()
    const workOrderId = workOrderGuid(1)
    const deviceAssetId = '019f0000-0000-7000-8000-000000000001'

    api.data.get('maintenance-list')!.value = {
      success: true,
      data: {
        items: [
          authoritativeListItem(1, {
            workOrderId: workOrderId.toUpperCase(),
            deviceAssetId: deviceAssetId.toUpperCase(),
          }),
        ],
        total: 1,
        skip: 0,
        take: 20,
      },
    }
    await nextTick()

    expect(result.items.value[0]).toMatchObject({ workOrderId, deviceAssetId })
  })

  it('requires a fresh list generation when scope returns A to B to A', async () => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrders()
    const aMetadata = queryMetadata('maintenance-list')!
    const aEnvelope = {
      success: true,
      data: { items: [authoritativeListItem(1)], total: 1, skip: 0, take: 20 },
    }
    api.data.get('maintenance-list')!.value = aEnvelope
    await nextTick()
    expect(result.items.value[0]?.workOrderId).toBe(workOrderGuid(1))

    seedPrincipal({ principalId: 'principal-2' })
    await nextTick()
    expect(result.items.value).toEqual([])
    expect(queryMetadata('maintenance-list')?.generation).not.toBe(aMetadata.generation)

    api.data.get('maintenance-list')!.value = {
      success: true,
      data: {
        items: [authoritativeListItem(2, { assignedTechnicianUserId: 'principal-2' })],
        total: 1,
        skip: 0,
        take: 20,
      },
    }
    await nextTick()
    expect(result.items.value[0]?.workOrderId).toBe(workOrderGuid(2))

    seedPrincipal()
    await nextTick()
    publishCached(
      'maintenance-list',
      aMetadata.generation as number,
      aMetadata.identity as string,
      aEnvelope,
    )
    await nextTick()

    expect(result.items.value).toEqual([])
    expect(result.hasSuccessfulResponse.value).toBe(false)
    expect(result.pending.value).toBe(true)

    api.errors.get('maintenance-list')!.value = { status: 403 }
    await nextTick()
    expect(result.items.value).toEqual([])
    expect(result.hasFailedResponse.value).toBe(true)
  })

  it.each(invalidListEnvelopes)(
    'rejects a malformed first-page envelope: %s',
    async (_, envelope) => {
      seedPrincipal()
      const result = useMaintenanceSelfWorkOrders()

      api.data.get('maintenance-list')!.value = withPagination(envelope, 0, 20)
      await nextTick()

      expect(result.hasSuccessfulResponse.value).toBe(false)
      expect(result.hasFailedResponse.value).toBe(true)
      expect(result.items.value).toEqual([])
      expect(result.total.value).toBe(0)
    },
  )

  it.each([
    ['missing skip', { take: 20 }],
    ['missing take', { skip: 0 }],
    ['unsafe skip', { skip: Number.MAX_SAFE_INTEGER + 1, take: 20 }],
    ['fractional take', { skip: 0, take: 20.5 }],
    ['misaligned skip', { skip: 20, take: 20 }],
    ['misaligned take', { skip: 0, take: 10 }],
  ])('rejects invalid first-page response pagination: %s', async (_, pagination) => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrders()
    api.data.get('maintenance-list')!.value = {
      success: true,
      data: { items: [], total: 0, ...pagination },
    }
    await nextTick()

    expect(result.hasFailedResponse.value).toBe(true)
    expect(result.items.value).toEqual([])
  })

  it.each([null, undefined, 'principal-other'])(
    'rejects a first-page row not authoritatively assigned to the current principal: %s',
    async (assignedTechnicianUserId) => {
      seedPrincipal()
      const result = useMaintenanceSelfWorkOrders()
      api.data.get('maintenance-list')!.value = {
        success: true,
        data: {
          items: [authoritativeListItem(1, { assignedTechnicianUserId })],
          total: 1,
          skip: 0,
          take: 20,
        },
      }
      await nextTick()

      expect(result.hasFailedResponse.value).toBe(true)
      expect(result.items.value).toEqual([])
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
          items: Array.from({ length: 20 }, (_, index) => authoritativeListItem(index)),
          total: 21,
          skip: 0,
          take: 20,
        },
      }
      api.list.mockResolvedValueOnce({ data: withPagination(envelope, 20, 20) })
      await nextTick()

      await result.loadMore()

      expect(result.loadMoreError.value).toBeInstanceOf(Error)
      expect(result.items.value).toHaveLength(20)
    },
  )

  it.each([
    ['misaligned skip', { skip: 0, take: 20 }],
    ['misaligned take', { skip: 20, take: 10 }],
  ])('rejects invalid next-page response pagination: %s', async (_, pagination) => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrders()
    api.data.get('maintenance-list')!.value = {
      success: true,
      data: {
        items: Array.from({ length: 20 }, (_, index) => authoritativeListItem(index)),
        total: 21,
        skip: 0,
        take: 20,
      },
    }
    api.list.mockResolvedValueOnce({
      data: {
        success: true,
        data: {
          items: [authoritativeListItem(20)],
          total: 21,
          ...pagination,
        },
      },
    })
    await nextTick()

    await result.loadMore()

    expect(result.loadMoreError.value).toBeInstanceOf(Error)
    expect(result.items.value).toHaveLength(20)
    expect(
      result.items.value.find((item) => item.workOrderId === workOrderGuid(20)),
    ).toBeUndefined()
  })

  it.each([null, undefined, 'principal-other'])(
    'rejects a next-page row not authoritatively assigned to the current principal: %s',
    async (assignedTechnicianUserId) => {
      seedPrincipal()
      const result = useMaintenanceSelfWorkOrders()
      api.data.get('maintenance-list')!.value = {
        success: true,
        data: {
          items: Array.from({ length: 20 }, (_, index) => authoritativeListItem(index)),
          total: 21,
          skip: 0,
          take: 20,
        },
      }
      api.list.mockResolvedValueOnce({
        data: {
          success: true,
          data: {
            items: [authoritativeListItem(20, { assignedTechnicianUserId })],
            total: 21,
            skip: 20,
            take: 20,
          },
        },
      })
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
      data: {
        items: [authoritativeListItem(1)],
        total: 1,
        skip: 0,
        take: 20,
      },
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

  it('retries the exact principal identity lookup together with a list refresh', async () => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrders()
    api.data.get('maintenance-list')!.value = {
      success: true,
      data: { items: [authoritativeListItem(1)], total: 1, skip: 0, take: 20 },
    }
    api.errors.get('maintenance-list-principal')!.value = { status: 502 }
    await nextTick()
    expect(result.principalDisplayName.value).toBeUndefined()

    api.refetches.get('maintenance-list-principal')!.mockImplementationOnce(async () => {
      api.errors.get('maintenance-list-principal')!.value = undefined
      api.data.get('maintenance-list-principal')!.value = '张维修'
    })
    await result.refresh()
    await nextTick()

    expect(api.refetches.get('maintenance-list-principal')).toHaveBeenCalledTimes(1)
    expect(result.principalDisplayName.value).toBe('张维修')
  })

  it('loads the next page with the same self scope and server filters', async () => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrders()
    result.filters.status = 'inProgress'
    result.filters.deviceAssetIds = ['device-2', 'DEV-2', 'device-2']
    result.filters.keyword = '轴承'
    api.data.get('maintenance-list')!.value = {
      success: true,
      data: {
        items: Array.from({ length: 20 }, (_, index) => authoritativeListItem(index)),
        total: 21,
        skip: 0,
        take: 20,
      },
    }
    api.list.mockResolvedValueOnce({
      data: {
        success: true,
        data: {
          items: [authoritativeListItem(20)],
          total: 21,
          skip: 20,
          take: 20,
        },
      },
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
        deviceAssetIds: 'device-2,DEV-2',
        keyword: '轴承',
        skip: 20,
        take: 20,
      },
      throwOnError: true,
    })
    expect(result.items.value.at(-1)?.workOrderId).toBe(workOrderGuid(20))
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

  it.each(['', 'WO-DETAIL', '00000000-0000-0000-0000-000000000000'])(
    'keeps an invalid or empty strong ID locally unavailable without downstream requests: %j',
    async (routeId) => {
      seedPrincipal()
      const result = useMaintenanceSelfWorkOrderDetail(computed(() => routeId))

      expect(result.enabled.value).toBe(false)
      expect(api.queryFactories.get('maintenance-detail')?.().enabled).toBe(false)
      expect(api.queryFactories.get('device-detail')?.().enabled).toBe(false)
      expect(api.queryFactories.get('maintenance-identities')?.().enabled).toBe(false)

      await result.refresh()

      expect(api.refetches.get('maintenance-detail')).not.toHaveBeenCalled()
      expect(api.refetches.get('device-detail')).not.toHaveBeenCalled()
      expect(api.workers).not.toHaveBeenCalled()
      expect(api.resourceDetail).not.toHaveBeenCalled()
    },
  )

  it('revalidates the strong ID with the authenticated self scope', () => {
    seedPrincipal()
    useMaintenanceSelfWorkOrderDetail(computed(() => '019f0000-0000-7000-8000-000000000101'))

    expect(requestFor('maintenance-detail')).toEqual({
      path: { workOrderId: '019f0000-0000-7000-8000-000000000101' },
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
    const result = useMaintenanceSelfWorkOrderDetail(
      computed(() => '019f0000-0000-7000-8000-000000000101'),
    )

    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: {
        workOrderId: '019f0000-0000-7000-8000-000000000101',
        deviceAssetId: 'device-1',
        priority: 'high',
        status: 'accepted',
        openedAtUtc: '2026-08-02T01:00:00.000Z',
        version: 7,
        allowedActions: [],
        blockReasons: [],
        lifecycle: [],
        assignedTechnicianUserId: 'principal-1',
        assignedTeamId: null,
      },
    }
    await nextTick()
    expect(result.workOrder.value).toBeUndefined()
    expect(result.pending.value).toBe(true)

    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: {
        workOrderId: '019f0000-0000-7000-8000-000000000101',
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

  it.each([42, { inspectionId: 'opaque' }])(
    'rejects a malformed detail source reference without throwing: %j',
    async (sourceReferenceId) => {
      seedPrincipal()
      const workOrderId = '019f0000-0000-7000-8000-000000000101'
      const result = useMaintenanceSelfWorkOrderDetail(computed(() => workOrderId))

      expect(() => {
        api.data.get('maintenance-detail')!.value = {
          success: true,
          data: { ...authoritativeDetail(workOrderId, 'DEV-CNC-01'), sourceReferenceId },
        }
      }).not.toThrow()
      await nextTick()

      expect(result.authoritativeHasFailedResponse.value).toBe(true)
      expect(result.authoritativeWorkOrder.value).toBeUndefined()
      expect(api.queryFactories.get('device-detail')?.().enabled).toBe(false)
    },
  )

  it('rejects a non-canonical response ID even when it exactly matches an invalid route value', async () => {
    seedPrincipal()
    const routeId = shallowRef('019f0000-0000-7000-8000-000000000101')
    const result = useMaintenanceSelfWorkOrderDetail(routeId)

    routeId.value = 'WO-DETAIL'
    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: authoritativeDetail('WO-DETAIL', 'DEV-CNC-01'),
    }
    await nextTick()

    expect(result.enabled.value).toBe(false)
    expect(result.authoritativeWorkOrder.value).toBeUndefined()
    expect(api.queryFactories.get('device-detail')?.().enabled).toBe(false)
  })

  it.each([
    ['technician', { assignedTeamId: null }],
    ['team', { assignedTechnicianUserId: 'principal-1' }],
    ['both', {}],
  ])('rejects detail when the explicit %s assignment field is missing', async (_, assignments) => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrderDetail(
      computed(() => '019f0000-0000-7000-8000-000000000101'),
    )
    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: {
        ...authoritativeDetail('019f0000-0000-7000-8000-000000000101', 'device-1'),
        assignedTechnicianUserId: undefined,
        assignedTeamId: undefined,
        ...assignments,
      },
    }
    const data = (publishedValue('maintenance-detail') as { data: Record<string, unknown> }).data
    if (!('assignedTechnicianUserId' in assignments)) delete data.assignedTechnicianUserId
    if (!('assignedTeamId' in assignments)) delete data.assignedTeamId
    await nextTick()

    expect(result.workOrder.value).toBeUndefined()
    expect(result.hasFailedResponse.value).toBe(true)
    expect(api.queryFactories.get('device-detail')?.().enabled).toBe(false)
  })

  it.each([null, 'principal-other'])(
    'rejects detail assigned to a different or absent technician: %s',
    async (assignedTechnicianUserId) => {
      seedPrincipal()
      const result = useMaintenanceSelfWorkOrderDetail(
        computed(() => '019f0000-0000-7000-8000-000000000101'),
      )
      api.data.get('maintenance-detail')!.value = {
        success: true,
        data: {
          ...authoritativeDetail('019f0000-0000-7000-8000-000000000101', 'device-1'),
          assignedTechnicianUserId,
        },
      }
      await nextTick()

      expect(result.workOrder.value).toBeUndefined()
      expect(result.hasFailedResponse.value).toBe(true)
      expect(api.queryFactories.get('device-detail')?.().enabled).toBe(false)
    },
  )

  it.each([
    ['actor principal', { actorPrincipalId: ' ' }],
    ['reason', { reason: '' }],
    ['technician', { technicianUserId: undefined }],
    ['team', { teamId: undefined }],
  ])('rejects detail when a lifecycle event has an invalid %s audit field', async (_, override) => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrderDetail(
      computed(() => '019f0000-0000-7000-8000-000000000101'),
    )
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
      data: {
        ...authoritativeDetail('019f0000-0000-7000-8000-000000000101', 'device-1'),
        lifecycle: [event],
      },
    }
    await nextTick()

    expect(result.workOrder.value).toBeUndefined()
    expect(result.hasFailedResponse.value).toBe(true)
    expect(api.queryFactories.get('device-detail')?.().enabled).toBe(false)
  })

  it.each(['1', '2026-02-30T01:00:00Z', '2026-08-02T01:00:00'])(
    'rejects a non-RFC3339 or impossible opened timestamp: %s',
    async (openedAtUtc) => {
      seedPrincipal()
      const result = useMaintenanceSelfWorkOrderDetail(
        computed(() => '019f0000-0000-7000-8000-000000000101'),
      )
      api.data.get('maintenance-detail')!.value = {
        success: true,
        data: {
          ...authoritativeDetail('019f0000-0000-7000-8000-000000000101', 'device-1'),
          openedAtUtc,
        },
      }
      await nextTick()

      expect(result.authoritativeHasFailedResponse.value).toBe(true)
      expect(result.authoritativeWorkOrder.value).toBeUndefined()
    },
  )

  it.each(['1', '2026-02-30T01:00:00Z', '2026-08-02T01:00:00'])(
    'rejects a non-RFC3339 or impossible lifecycle timestamp: %s',
    async (occurredAtUtc) => {
      seedPrincipal()
      const result = useMaintenanceSelfWorkOrderDetail(
        computed(() => '019f0000-0000-7000-8000-000000000101'),
      )
      api.data.get('maintenance-detail')!.value = {
        success: true,
        data: {
          ...authoritativeDetail('019f0000-0000-7000-8000-000000000101', 'device-1'),
          lifecycle: [
            {
              action: 'accept',
              fromStatus: 'open',
              toStatus: 'accepted',
              actorPrincipalId: 'principal-1',
              technicianUserId: 'principal-1',
              teamId: null,
              reason: '现场接单',
              resultingVersion: 2,
              occurredAtUtc,
            },
          ],
        },
      }
      await nextTick()

      expect(result.authoritativeHasFailedResponse.value).toBe(true)
    },
  )

  it.each([
    ['403', { error: { status: 403 } }],
    ['timeout', { error: new Error('timeout') }],
    ['bad shape', { data: { success: true, data: { resourceType: 'device-asset' } } }],
  ])(
    'keeps authoritative assignment confirmed when device enrichment returns %s',
    async (_, outcome) => {
      seedPrincipal()
      const result = useMaintenanceSelfWorkOrderDetail(
        computed(() => '019f0000-0000-7000-8000-000000000101'),
      )
      api.data.get('maintenance-detail')!.value = {
        success: true,
        data: authoritativeDetail('019f0000-0000-7000-8000-000000000101', 'DEV-CNC-01'),
      }
      await nextTick()
      if ('error' in outcome) api.errors.get('device-detail')!.value = outcome.error
      if ('data' in outcome) api.data.get('device-detail')!.value = outcome.data
      await nextTick()

      expect(result.authoritativeHasSuccessfulResponse.value).toBe(true)
      expect(result.authoritativeWorkOrder.value?.assignedTechnicianUserId).toBe('principal-1')
      expect(result.deviceHasFailedResponse.value).toBe(true)
      expect(result.workOrder.value).toBeUndefined()
    },
  )

  it.each([
    ['closed status', { status: 'closed', allowedActions: ['start'] }],
    ['cancelled status', { status: 'cancelled', allowedActions: ['accept'] }],
    ['terminal block reason', { blockReasons: ['terminal-status'], allowedActions: ['start'] }],
  ])('rejects contradictory terminal action facts: %s', async (_, override) => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrderDetail(
      computed(() => '019f0000-0000-7000-8000-000000000101'),
    )
    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: {
        ...authoritativeDetail('019f0000-0000-7000-8000-000000000101', 'device-1'),
        ...override,
      },
    }
    await nextTick()

    expect(result.workOrder.value).toBeUndefined()
    expect(result.hasFailedResponse.value).toBe(true)
    expect(api.queryFactories.get('device-detail')?.().enabled).toBe(false)
  })

  it.each([[null], [1]])(
    'rejects malformed block reasons without throwing during render: %j',
    async (blockReason) => {
      seedPrincipal()
      const result = useMaintenanceSelfWorkOrderDetail(
        computed(() => '019f0000-0000-7000-8000-000000000101'),
      )

      expect(() => {
        api.data.get('maintenance-detail')!.value = {
          success: true,
          data: {
            ...authoritativeDetail('019f0000-0000-7000-8000-000000000101', 'device-1'),
            blockReasons: [blockReason],
          },
        }
      }).not.toThrow()
      await nextTick()

      expect(result.workOrder.value).toBeUndefined()
      expect(result.hasFailedResponse.value).toBe(true)
      expect(api.queryFactories.get('device-detail')?.().enabled).toBe(false)
    },
  )

  it('accepts a device detail resolved by public ID when its business code differs', async () => {
    seedPrincipal()
    const deviceAssetId = '019f0000-0000-7000-8000-000000000001'
    const workOrderId = '019f0000-0000-7000-8000-000000000101'
    const result = useMaintenanceSelfWorkOrderDetail(computed(() => workOrderId.toUpperCase()))
    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: authoritativeDetail(workOrderId.toUpperCase(), deviceAssetId.toUpperCase()),
    }
    await nextTick()

    expect(requestFor('maintenance-detail')).toMatchObject({ path: { workOrderId } })
    expect(requestFor('device-detail')).toMatchObject({
      path: { resourceType: 'device-asset', code: deviceAssetId },
    })

    api.data.get('device-detail')!.value = {
      success: true,
      data: {
        resourceType: 'device-asset',
        deviceAssetId,
        code: 'DEV-CNC-01',
        displayName: '一号数控机床',
        active: true,
        snapshotVersion: 'device-v1',
        organizationId: 'org-001',
        environmentId: 'env-dev',
      },
    }
    await nextTick()

    expect(result.hasSuccessfulResponse.value).toBe(true)
    expect(result.workOrder.value).toMatchObject({ workOrderId, deviceAssetId })
    expect(result.device.value?.code).toBe('DEV-CNC-01')
  })

  it.each([
    ['resource type', { resourceType: 42 }],
    ['organization', { organizationId: {} }],
    ['environment', { environmentId: 42 }],
    ['code', { code: {} }],
    ['device public ID', { deviceAssetId: '00000000-0000-0000-0000-000000000000' }],
    ['display name', { displayName: 42 }],
    ['missing active flag', { active: undefined }],
    ['active flag type', { active: 'true' }],
    ['inactive device', { active: false }],
    ['retired device', { retired: true }],
    ['device with retirement date', { retiredOn: '2026-08-01T00:00:00.000Z' }],
    ['missing snapshot version', { snapshotVersion: undefined }],
    ['blank snapshot version', { snapshotVersion: ' ' }],
    ['snapshot version type', { snapshotVersion: 7 }],
    ['site', { siteCode: {} }],
    ['plant', { plantCode: 42 }],
    ['workshop', { workshopCode: {} }],
    ['line', { lineCode: 42 }],
    ['work center', { workCenterCode: {} }],
    ['station', { stationCode: 42 }],
  ])('fails malformed device %s data closed without a computed exception', async (_, override) => {
    seedPrincipal()
    const workOrderId = '019f0000-0000-7000-8000-000000000101'
    const result = useMaintenanceSelfWorkOrderDetail(computed(() => workOrderId))
    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: authoritativeDetail(workOrderId, 'DEV-CNC-01'),
    }
    await nextTick()

    expect(() => {
      api.data.get('device-detail')!.value = {
        success: true,
        data: {
          resourceType: 'device-asset',
          deviceAssetId: '019f0000-0000-7000-8000-000000000001',
          code: 'DEV-CNC-01',
          displayName: '一号数控机床',
          active: true,
          snapshotVersion: 'device-v1',
          organizationId: 'org-001',
          environmentId: 'env-dev',
          ...override,
        },
      }
    }).not.toThrow()
    await nextTick()

    expect(result.authoritativeHasSuccessfulResponse.value).toBe(true)
    expect(result.deviceHasFailedResponse.value).toBe(true)
    expect(result.hasFailedResponse.value).toBe(true)
    expect(result.device.value).toBeUndefined()
  })

  it('resolves readable users and teams through exact scoped MasterData contracts', async () => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrderDetail(
      computed(() => '019f0000-0000-7000-8000-000000000101'),
    )
    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: {
        ...authoritativeDetail('019f0000-0000-7000-8000-000000000101', 'DEV-CNC-01'),
        assignedTeamId: 'TEAM-A',
      },
    }
    await nextTick()
    api.workers.mockResolvedValueOnce({
      data: {
        success: true,
        data: {
          pageIndex: 1,
          pageSize: 1,
          totalCount: 1,
          items: [
            {
              userId: 'principal-1',
              employeeNo: 'EMP-001',
              displayName: '张维修',
              employmentStatus: 'active',
              active: true,
              teams: [],
              skills: [],
              snapshotVersion: 'worker-v1',
            },
          ],
        },
      },
    })
    api.resourceDetail.mockResolvedValueOnce({
      data: {
        success: true,
        data: {
          resourceType: 'team',
          code: 'TEAM-A',
          displayName: '甲班',
          active: true,
          snapshotVersion: 'team-v1',
          organizationId: 'org-001',
          environmentId: 'env-dev',
        },
      },
    })

    const options = api.queryFactories.get('maintenance-identities')?.()
    expect(options?.enabled).toBe(true)
    const identities = await (options?.query as () => Promise<unknown>)()
    api.data.get('maintenance-identities')!.value = identities
    await nextTick()

    expect(api.workers).toHaveBeenCalledWith({
      query: expect.objectContaining({
        organizationId: 'org-001',
        environmentId: 'env-dev',
        userId: 'principal-1',
        pageIndex: 1,
        pageSize: 1,
      }),
      throwOnError: true,
    })
    expect(api.resourceDetail).toHaveBeenCalledWith({
      path: { resourceType: 'team', code: 'TEAM-A' },
      query: { organizationId: 'org-001', environmentId: 'env-dev' },
      throwOnError: true,
    })
    expect(result.identities.value).toEqual({
      users: { 'principal-1': '张维修' },
      teams: { 'TEAM-A': '甲班' },
    })
  })

  it('fails identity enrichment closed when an exact worker response returns another user', async () => {
    seedPrincipal()
    useMaintenanceSelfWorkOrderDetail(computed(() => '019f0000-0000-7000-8000-000000000101'))
    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: authoritativeDetail('019f0000-0000-7000-8000-000000000101', 'DEV-CNC-01'),
    }
    await nextTick()
    api.workers.mockResolvedValueOnce({
      data: {
        success: true,
        data: {
          pageIndex: 1,
          pageSize: 1,
          totalCount: 1,
          items: [
            {
              userId: 'principal-other',
              employeeNo: 'EMP-002',
              displayName: '错误人员',
              employmentStatus: 'active',
              active: true,
              teams: [],
              skills: [],
              snapshotVersion: 'worker-v1',
            },
          ],
        },
      },
    })

    const options = api.queryFactories.get('maintenance-identities')?.()
    await expect((options?.query as () => Promise<unknown>)()).rejects.toThrow('身份资料暂不可用')
  })

  it('bounds identity enrichment requests for an oversized lifecycle', async () => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrderDetail(
      computed(() => '019f0000-0000-7000-8000-000000000101'),
    )
    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: {
        ...authoritativeDetail('019f0000-0000-7000-8000-000000000101', 'DEV-CNC-01'),
        lifecycle: Array.from({ length: 21 }, (_, index) => ({
          action: 'accept',
          fromStatus: 'open',
          toStatus: 'accepted',
          actorPrincipalId: `principal-${index + 2}`,
          technicianUserId: null,
          teamId: null,
          reason: '历史动作',
          resultingVersion: index + 1,
          occurredAtUtc: '2026-08-02T01:02:03.000Z',
        })),
      },
    }
    await nextTick()

    expect(api.queryFactories.get('maintenance-identities')?.().enabled).toBe(false)
    expect(result.identitiesUnavailable.value).toBe(true)
    expect(api.workers).not.toHaveBeenCalled()
  })

  it('clears retained identity names synchronously when the work-order identity changes', async () => {
    seedPrincipal()
    const routeId = shallowRef('019f0000-0000-7000-8000-000000000102')
    const result = useMaintenanceSelfWorkOrderDetail(routeId)
    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: authoritativeDetail('019f0000-0000-7000-8000-000000000102', 'DEV-A'),
    }
    await nextTick()
    api.data.get('maintenance-identities')!.value = {
      users: { 'principal-1': '旧人员' },
      teams: {},
    }
    await nextTick()
    expect(result.identities.value?.users['principal-1']).toBe('旧人员')

    routeId.value = '019f0000-0000-7000-8000-000000000103'
    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: authoritativeDetail('019f0000-0000-7000-8000-000000000103', 'DEV-B'),
    }
    await nextTick()

    expect(result.identities.value).toBeUndefined()
  })

  it('rejects cached and delayed detail generations when route returns A to B to A', async () => {
    seedPrincipal()
    const workOrderA = '019f0000-0000-7000-8000-000000000102'
    const workOrderB = '019f0000-0000-7000-8000-000000000103'
    const routeId = shallowRef(workOrderA)
    const result = useMaintenanceSelfWorkOrderDetail(routeId)
    const aMetadata = queryMetadata('maintenance-detail')!
    const aEnvelope = {
      success: true,
      data: {
        ...authoritativeDetail(workOrderA, 'DEV-A'),
        lifecycle: [
          {
            action: 'accept',
            fromStatus: 'open',
            toStatus: 'accepted',
            actorPrincipalId: 'principal-1',
            technicianUserId: 'principal-1',
            teamId: null,
            reason: '旧 A 生命周期',
            resultingVersion: 7,
            occurredAtUtc: '2026-08-02T01:02:03.000Z',
          },
        ],
      },
    }
    api.data.get('maintenance-detail')!.value = aEnvelope
    await nextTick()
    expect(result.authoritativeWorkOrder.value?.lifecycle[0]?.reason).toBe('旧 A 生命周期')

    routeId.value = workOrderB
    await nextTick()
    publishCached(
      'maintenance-detail',
      aMetadata.generation as number,
      aMetadata.identity as string,
      aEnvelope,
    )
    await nextTick()
    expect(result.authoritativeWorkOrder.value).toBeUndefined()
    expect(api.queryFactories.get('device-detail')?.().enabled).toBe(false)

    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: authoritativeDetail(workOrderB, 'DEV-B'),
    }
    await nextTick()
    expect(result.authoritativeWorkOrder.value?.workOrderId).toBe(workOrderB)

    routeId.value = workOrderA
    await nextTick()
    publishCached(
      'maintenance-detail',
      aMetadata.generation as number,
      aMetadata.identity as string,
      aEnvelope,
    )
    await nextTick()
    expect(result.authoritativeWorkOrder.value).toBeUndefined()
    expect(result.identities.value).toBeUndefined()
    expect(api.queryFactories.get('device-detail')?.().enabled).toBe(false)

    api.errors.get('maintenance-detail')!.value = { status: 403 }
    await nextTick()
    expect(result.authoritativeHasFailedResponse.value).toBe(true)
    expect(result.authoritativeWorkOrder.value).toBeUndefined()
  })

  it('treats the strong-ID device detail as part of aggregate detail state', async () => {
    seedPrincipal()
    const routeId = shallowRef('019f0000-0000-7000-8000-000000000104')
    const result = useMaintenanceSelfWorkOrderDetail(routeId)

    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: { workOrderId: '019f0000-0000-7000-8000-000000000105', deviceAssetId: 'device-old' },
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
      data: authoritativeDetail('019f0000-0000-7000-8000-000000000104', 'DEV-CNC-01'),
    }
    await nextTick()
    expect(result.workOrder.value).toBeUndefined()
    expect(result.pending.value).toBe(true)
    expect(requestFor('device-detail')).toEqual({
      path: { resourceType: 'device-asset', code: 'DEV-CNC-01' },
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
      },
    })

    api.data.get('device-detail')!.value = {
      success: true,
      data: {
        resourceType: 'device-asset',
        deviceAssetId: '019f0000-0000-7000-8000-000000000001',
        code: 'DEV-CNC-OLD',
        displayName: '迟到的旧设备',
        active: true,
        snapshotVersion: 'device-v1',
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
        deviceAssetId: '019f0000-0000-7000-8000-000000000001',
        code: 'DEV-CNC-01',
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
        deviceAssetId: '019f0000-0000-7000-8000-000000000001',
        code: 'DEV-CNC-01',
        active: true,
        snapshotVersion: 'device-v1',
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
        deviceAssetId: '019f0000-0000-7000-8000-000000000001',
        code: 'DEV-CNC-01',
        displayName: '一号数控机床',
        active: true,
        snapshotVersion: 'device-v1',
        organizationId: 'org-001',
        environmentId: 'env-dev',
        workshopCode: 'WS-1',
      },
    }
    await nextTick()
    expect(result.device.value).toMatchObject({
      code: 'DEV-CNC-01',
      displayName: '一号数控机床',
      workshopCode: 'WS-1',
    })
    expect(result.workOrder.value?.workOrderId).toBe('019f0000-0000-7000-8000-000000000104')
    expect(result.hasSuccessfulResponse.value).toBe(true)
    expect(result.hasFailedResponse.value).toBe(false)

    api.errors.get('maintenance-detail')!.value = { status: 403 }
    await nextTick()
    expect(result.workOrder.value).toBeUndefined()
    expect(result.device.value).toBeUndefined()
    expect(result.hasFailedResponse.value).toBe(true)
    api.errors.get('maintenance-detail')!.value = undefined

    routeId.value = '019f0000-0000-7000-8000-000000000106'
    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: authoritativeDetail('019f0000-0000-7000-8000-000000000106', 'DEV-LATEST'),
    }
    await nextTick()
    expect(result.device.value).toBeUndefined()
    expect(result.workOrder.value).toBeUndefined()
    expect(result.pending.value).toBe(true)

    api.data.get('device-detail')!.value = {
      success: true,
      data: {
        resourceType: 'device-asset',
        deviceAssetId: '019f0000-0000-7000-8000-000000000001',
        code: 'DEV-CNC-01',
        displayName: '迟到的一号数控机床',
        active: true,
        snapshotVersion: 'device-v1',
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
    const result = useMaintenanceSelfWorkOrderDetail(
      computed(() => '019f0000-0000-7000-8000-000000000101'),
    )
    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: authoritativeDetail('019f0000-0000-7000-8000-000000000101', 'DEV-CNC-01'),
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

  it('retries identity enrichment after authoritative detail revalidation', async () => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrderDetail(
      computed(() => '019f0000-0000-7000-8000-000000000101'),
    )
    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: authoritativeDetail('019f0000-0000-7000-8000-000000000101', 'DEV-CNC-01'),
    }
    api.errors.get('maintenance-identities')!.value = { status: 502 }
    await nextTick()
    expect(result.identitiesUnavailable.value).toBe(true)

    const calls: string[] = []
    api.refetches.get('maintenance-detail')!.mockImplementationOnce(async () => {
      calls.push('work-order')
    })
    api.refetches.get('maintenance-identities')!.mockImplementationOnce(async () => {
      calls.push('identities')
      api.errors.get('maintenance-identities')!.value = undefined
      api.data.get('maintenance-identities')!.value = {
        users: { 'principal-1': '张维修' },
        teams: {},
      }
    })

    await result.refresh()
    await nextTick()

    expect(calls[0]).toBe('work-order')
    expect(calls).toContain('identities')
    expect(result.identitiesUnavailable.value).toBe(false)
    expect(result.identities.value?.users['principal-1']).toBe('张维修')
  })

  it('hides retained aggregate detail during work-order and device refreshes', async () => {
    seedPrincipal()
    const result = useMaintenanceSelfWorkOrderDetail(
      computed(() => '019f0000-0000-7000-8000-000000000101'),
    )
    api.data.get('maintenance-detail')!.value = {
      success: true,
      data: authoritativeDetail('019f0000-0000-7000-8000-000000000101', 'DEV-CNC-01'),
    }
    await nextTick()
    api.data.get('device-detail')!.value = {
      success: true,
      data: {
        resourceType: 'device-asset',
        deviceAssetId: '019f0000-0000-7000-8000-000000000001',
        code: 'DEV-CNC-01',
        displayName: '旧设备',
        active: true,
        snapshotVersion: 'device-v1',
        organizationId: 'org-001',
        environmentId: 'env-dev',
      },
    }
    await nextTick()
    expect(result.workOrder.value?.workOrderId).toBe('019f0000-0000-7000-8000-000000000101')
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
