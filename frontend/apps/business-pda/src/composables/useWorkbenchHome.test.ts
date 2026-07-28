import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { nextTick, reactive, shallowRef, type ShallowRef } from 'vue'

import { HOME_PERMISSIONS, usePendingInspectionSummary } from './useWorkbenchHome'

const coladaState = vi.hoisted(() => ({
  optionsById: new Map<string, { enabled?: boolean }>(),
  refetchById: new Map<string, ReturnType<typeof vi.fn>>(),
  dataById: new Map<string, unknown>(),
  dataRefById: new Map<string, ShallowRef<unknown>>(),
  loadingById: new Map<string, ShallowRef<boolean>>(),
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
    const data = shallowRef(coladaState.dataById.get(id))
    const isLoading = shallowRef(false)
    coladaState.optionsById.set(id, options)
    coladaState.refetchById.set(id, refetch)
    coladaState.dataRefById.set(id, data)
    coladaState.loadingById.set(id, isLoading)
    return {
      data,
      error: shallowRef(),
      isLoading,
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
    coladaState.dataById.clear()
    coladaState.dataRefById.clear()
    coladaState.loadingById.clear()
    authState.principal = {
      organizationId: 'org-001',
      environmentId: 'env-dev',
      permissionCodes: [HOME_PERMISSIONS.quality],
    }
  })

  it('keeps the permitted section visible but suppresses query and manual refresh without scope', async () => {
    reactiveAuthState.principal = {
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

  it('exposes a failed inspection envelope instead of a successful empty response', () => {
    coladaState.dataById.set('inspection', {
      success: false,
      message: '待检任务查询失败',
    })

    const inspection = usePendingInspectionSummary()

    expect(inspection.hasSuccessfulResponse.value).toBe(false)
    expect(inspection.hasFailedResponse.value).toBe(true)
  })

  it('does not report stale inspection success while a refresh is in flight', async () => {
    coladaState.dataById.set('inspection', {
      success: true,
      data: { items: [], total: 0 },
    })

    const inspection = usePendingInspectionSummary()
    expect(inspection.hasSuccessfulResponse.value).toBe(true)

    coladaState.loadingById.get('inspection')!.value = true
    await nextTick()

    expect(inspection.hasSuccessfulResponse.value).toBe(false)
    expect(inspection.hasFailedResponse.value).toBe(false)
  })

  it('hides cached inspection data when scope is lost and waits for the restored scope response', async () => {
    coladaState.dataById.set('inspection', {
      success: true,
      data: {
        items: [{ inspectionTaskId: 'OLD-INSPECTION', skuCode: 'OLD-SKU' }],
        total: 7,
      },
    })

    const inspection = usePendingInspectionSummary()
    expect(inspection.tasks.value).toHaveLength(1)
    expect(inspection.total.value).toBe(7)

    reactiveAuthState.principal = {
      organizationId: '',
      environmentId: '',
      permissionCodes: [HOME_PERMISSIONS.quality],
    }
    await nextTick()
    await inspection.refresh()

    expect(inspection.scopeReady.value).toBe(false)
    expect(inspection.tasks.value).toEqual([])
    expect(inspection.total.value).toBe(0)
    expect(coladaState.refetchById.get('inspection')).not.toHaveBeenCalled()

    reactiveAuthState.principal = {
      organizationId: 'org-002',
      environmentId: 'env-prod',
      permissionCodes: [HOME_PERMISSIONS.quality],
    }
    await nextTick()

    expect(inspection.scopeReady.value).toBe(true)
    expect(inspection.tasks.value).toEqual([])
    expect(inspection.total.value).toBe(0)
    expect(inspection.hasSuccessfulResponse.value).toBe(false)

    coladaState.dataRefById.get('inspection')!.value = {
      success: true,
      data: {
        items: [{ inspectionTaskId: 'NEW-INSPECTION', skuCode: 'NEW-SKU' }],
        total: 1,
      },
    }
    await nextTick()

    expect(inspection.tasks.value).toEqual([
      expect.objectContaining({ inspectionTaskId: 'NEW-INSPECTION' }),
    ])
    expect(inspection.total.value).toBe(1)
    expect(inspection.hasSuccessfulResponse.value).toBe(true)

    coladaState.loadingById.get('inspection')!.value = true
    await nextTick()

    expect(inspection.tasks.value).toEqual([
      expect.objectContaining({ inspectionTaskId: 'NEW-INSPECTION' }),
    ])
    expect(inspection.total.value).toBe(1)
  })
})
