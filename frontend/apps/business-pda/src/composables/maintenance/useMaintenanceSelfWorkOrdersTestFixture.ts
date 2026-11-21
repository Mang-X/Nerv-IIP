import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, vi } from 'vitest'
import { shallowRef, type ShallowRef } from 'vue'
import { useAuthStore } from '@/stores/auth'

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
export function setupMaintenanceSelfWorkOrdersTests() {
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
}
export {
  api,
  seedPrincipal,
  requestFor,
  queryMetadata,
  publishedValue,
  publishCached,
  authoritativeDetail,
  workOrderGuid,
  authoritativeListItem,
  withPagination,
  invalidListEnvelopes,
}

export function setupMaintenanceDetailTests() {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    api.queryFactories.clear()
    api.data.clear()
    api.errors.clear()
    api.loading.clear()
    api.refetches.clear()
  })
}
