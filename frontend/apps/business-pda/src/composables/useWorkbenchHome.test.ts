import { createPinia, setActivePinia } from 'pinia'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { nextTick, reactive, shallowRef, type ShallowRef } from 'vue'

import {
  HOME_PERMISSIONS,
  usePendingInspectionSummary,
  useWarehouseSummary,
} from './useWorkbenchHome'

const coladaState = vi.hoisted(() => ({
  optionsById: new Map<string, { enabled?: boolean }>(),
  refetchById: new Map<string, ReturnType<typeof vi.fn>>(),
  dataById: new Map<string, unknown>(),
  dataRefById: new Map<string, ShallowRef<unknown>>(),
  loadingById: new Map<string, ShallowRef<boolean>>(),
  errorById: new Map<string, unknown>(),
  errorRefById: new Map<string, ShallowRef<unknown>>(),
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

// `isForbiddenRequestError` 用**真身**而不是桩：这组用例要检验的恰恰是「403 能不能被认出来」，
// 把谓词换成桩等于把被测判据一起换掉。这里不能用 `importOriginal()`——它会拉起整个 generated
// 图（`@pinia/colada.gen`），与本文件对 `@pinia/colada` 的窄 mock 冲突；canonical 那个模块自身
// 零依赖，直接深引即可。
vi.mock('@nerv-iip/api-client', async () => ({
  ...(await import('../../../../packages/api-client/src/transport/request-error-status')),
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
    const error = shallowRef(coladaState.errorById.get(id))
    coladaState.optionsById.set(id, options)
    coladaState.refetchById.set(id, refetch)
    coladaState.dataRefById.set(id, data)
    coladaState.loadingById.set(id, isLoading)
    coladaState.errorRefById.set(id, error)
    return {
      data,
      error,
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

describe('useWarehouseSummary', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    coladaState.optionsById.clear()
    coladaState.refetchById.clear()
    coladaState.dataById.clear()
    coladaState.dataRefById.clear()
    coladaState.loadingById.clear()
    coladaState.errorById.clear()
    coladaState.errorRefById.clear()
  })

  it('does not request or expose count work when the principal only has receipts read', () => {
    authState.principal = {
      organizationId: 'org-001',
      environmentId: 'env-dev',
      permissionCodes: [HOME_PERMISSIONS.wmsReceipts],
    }

    const warehouse = useWarehouseSummary()

    expect(warehouse.enabled.value).toBe(true)
    expect(coladaState.optionsById.get('inbound')?.enabled).toBe(true)
    expect(coladaState.optionsById.get('putaway')?.enabled).toBe(true)
    expect(coladaState.optionsById.get('picking')?.enabled).toBe(false)
    expect(coladaState.optionsById.get('count')?.enabled).toBe(false)
    expect(warehouse.entries.value.map((entry) => entry.key)).toEqual(['inbound', 'putaway'])
  })

  it('requests and exposes only count work for a counts-read principal', () => {
    coladaState.dataById.set('count', {
      success: true,
      data: { items: [], total: 7 },
    })
    authState.principal = {
      organizationId: 'org-001',
      environmentId: 'env-dev',
      permissionCodes: [HOME_PERMISSIONS.wmsCounts],
    }

    const warehouse = useWarehouseSummary()

    expect(warehouse.enabled.value).toBe(true)
    expect(coladaState.optionsById.get('inbound')?.enabled).toBe(false)
    expect(coladaState.optionsById.get('putaway')?.enabled).toBe(false)
    expect(coladaState.optionsById.get('picking')?.enabled).toBe(false)
    expect(coladaState.optionsById.get('count')?.enabled).toBe(true)
    expect(warehouse.entries.value).toEqual([
      { key: 'count', label: '待盘点', route: '/wms/count', count: 7, state: 'counted' },
    ])
  })

  /**
   * #3474：admin 持有 WMS 权限码但没有仓储数据范围，四条请求全 403。
   * 旧实现把 403 走 `listTotal(undefined)` 吞成 `0`，屏上写「待收货 0 件」——
   * 那是假读数。这里钉住：403 一律 `count === null`、`state === 'denied'`。
   * 把 `readCount` 的 403 分支改回返回 0，本格必红。
   */
  it('reports a forbidden warehouse count as denied, never as zero', () => {
    coladaState.errorById.set('inbound', { status: 403 })
    coladaState.errorById.set('putaway', { response: { status: 403 } })
    authState.principal = {
      organizationId: 'org-001',
      environmentId: 'env-dev',
      permissionCodes: [HOME_PERMISSIONS.wmsReceipts],
    }

    const warehouse = useWarehouseSummary()

    expect(warehouse.entries.value).toEqual([
      { key: 'inbound', label: '待收货', route: '/wms/inbound', count: null, state: 'denied' },
      { key: 'putaway', label: '待上架', route: '/wms/putaway', count: null, state: 'denied' },
    ])
    for (const entry of warehouse.entries.value) expect(entry.count).not.toBe(0)
    expect(warehouse.scopeDenied.value).toBe(true)
    expect(warehouse.hasDeniedEntry.value).toBe(true)
    expect(warehouse.hasFailedEntry.value).toBe(false)
  })

  it('separates a non-403 read failure from a denied scope and from a real zero', () => {
    coladaState.errorById.set('inbound', { status: 500 })
    // `success:false` 信封同样是「不知道」，不是 0。
    coladaState.dataById.set('putaway', { success: false, message: '上架任务查询失败' })
    authState.principal = {
      organizationId: 'org-001',
      environmentId: 'env-dev',
      permissionCodes: [HOME_PERMISSIONS.wmsReceipts],
    }

    const warehouse = useWarehouseSummary()

    expect(warehouse.entries.value.map((entry) => [entry.key, entry.count, entry.state])).toEqual([
      ['inbound', null, 'failed'],
      ['putaway', null, 'failed'],
    ])
    expect(warehouse.scopeDenied.value).toBe(false)
    expect(warehouse.hasDeniedEntry.value).toBe(false)
    expect(warehouse.hasFailedEntry.value).toBe(true)
  })

  it('keeps a genuine zero as the number 0, not as an unknown state', () => {
    coladaState.dataById.set('count', { success: true, data: { items: [], total: 0 } })
    authState.principal = {
      organizationId: 'org-001',
      environmentId: 'env-dev',
      permissionCodes: [HOME_PERMISSIONS.wmsCounts],
    }

    const warehouse = useWarehouseSummary()

    expect(warehouse.entries.value).toEqual([
      { key: 'count', label: '待盘点', route: '/wms/count', count: 0, state: 'counted' },
    ])
    expect(warehouse.scopeDenied.value).toBe(false)
  })

  it('does not call a partially denied section fully denied', async () => {
    coladaState.errorById.set('inbound', { status: 403 })
    coladaState.dataById.set('putaway', { success: true, data: { items: [], total: 4 } })
    authState.principal = {
      organizationId: 'org-001',
      environmentId: 'env-dev',
      permissionCodes: [HOME_PERMISSIONS.wmsReceipts],
    }

    const warehouse = useWarehouseSummary()

    expect(warehouse.scopeDenied.value).toBe(false)
    expect(warehouse.hasDeniedEntry.value).toBe(true)
    expect(warehouse.entries.value.map((entry) => entry.state)).toEqual(['denied', 'counted'])

    // 403 被修好（管理员补了范围）后，格子要重新变成真读数而不是卡在 denied。
    coladaState.errorRefById.get('inbound')!.value = undefined
    coladaState.dataRefById.get('inbound')!.value = { success: true, data: { items: [], total: 9 } }
    await nextTick()

    expect(warehouse.entries.value[0]).toEqual({
      key: 'inbound',
      label: '待收货',
      route: '/wms/inbound',
      count: 9,
      state: 'counted',
    })
    expect(warehouse.hasDeniedEntry.value).toBe(false)
  })
})

describe('usePendingInspectionSummary', () => {
  afterEach(() => {
    vi.useRealTimers()
  })

  beforeEach(() => {
    setActivePinia(createPinia())
    vi.clearAllMocks()
    coladaState.optionsById.clear()
    coladaState.refetchById.clear()
    coladaState.dataById.clear()
    coladaState.dataRefById.clear()
    coladaState.loadingById.clear()
    coladaState.errorById.clear()
    coladaState.errorRefById.clear()
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
    vi.useFakeTimers()
    vi.setSystemTime('2026-07-28T01:00:00.000Z')
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
