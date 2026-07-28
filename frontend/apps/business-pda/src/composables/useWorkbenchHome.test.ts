import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { reactive, shallowRef } from 'vue'

import { HOME_PERMISSIONS, usePendingInspectionSummary } from './useWorkbenchHome'

const coladaState = vi.hoisted(() => ({
  optionsById: new Map<string, { enabled?: boolean }>(),
  refetchById: new Map<string, ReturnType<typeof vi.fn>>(),
}))

const authState = vi.hoisted(() => ({
  principal: undefined as
    | {
        organizationId?: string
        environmentId?: string
        permissionCodes?: string[]
      }
    | undefined,
}))
const reactiveAuthState = reactive(authState)

function queryOptions(id: string) {
  return vi.fn(() => ({
    key: [{ _id: id }],
    query: vi.fn(),
  }))
}

vi.mock('@nerv-iip/api-client', () => ({
  listBusinessConsoleMesDispatchTasksQueryOptions: queryOptions('dispatch'),
  listBusinessConsoleQualityInspectionTasksQueryOptions: queryOptions('inspection'),
  listBusinessConsoleWmsCountExecutionsQueryOptions: queryOptions('count'),
  listBusinessConsoleWmsInboundOrdersQueryOptions: queryOptions('inbound'),
  listBusinessConsoleWmsPickingTasksQueryOptions: queryOptions('picking'),
  listBusinessConsoleWmsPutawayTasksQueryOptions: queryOptions('putaway'),
  listBusinessConsoleWorkersQueryOptions: queryOptions('workers'),
}))

vi.mock('@pinia/colada', () => ({
  useQuery: vi.fn((optionsFactory) => {
    const options = optionsFactory()
    const key = Array.isArray(options.key) ? options.key[0] : undefined
    const id = key && typeof key === 'object' && '_id' in key ? String(key._id) : ''
    const refetch = vi.fn()
    coladaState.optionsById.set(id, options)
    coladaState.refetchById.set(id, refetch)
    return {
      data: shallowRef(),
      error: shallowRef(),
      isLoading: shallowRef(false),
      refetch,
    }
  }),
}))

vi.mock('@/stores/auth', () => ({
  useAuthStore: vi.fn(() => ({
    get principal() {
      return reactiveAuthState.principal
    },
  })),
}))

describe('usePendingInspectionSummary', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    coladaState.optionsById.clear()
    coladaState.refetchById.clear()
    authState.principal = {
      organizationId: 'org-001',
      environmentId: 'env-dev',
      permissionCodes: [HOME_PERMISSIONS.quality],
    }
  })

  it('keeps the permitted section visible but suppresses query and manual refresh without scope', async () => {
    authState.principal = {
      organizationId: '',
      environmentId: '',
      permissionCodes: [HOME_PERMISSIONS.quality],
    }

    const inspection = usePendingInspectionSummary()
    await inspection.refresh()

    expect(inspection.visible.value).toBe(true)
    expect(inspection.scopeReady.value).toBe(false)
    expect(inspection.enabled.value).toBe(false)
    expect(coladaState.optionsById.get('inspection')?.enabled).toBe(false)
    expect(coladaState.refetchById.get('inspection')).not.toHaveBeenCalled()
  })
})
