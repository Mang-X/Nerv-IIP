import {
  getBusinessConsoleWmsCountWorkScopesQueryOptions,
  getBusinessConsoleWmsReceiptWorkScopesQueryOptions,
  getBusinessConsoleWmsShipmentWorkScopesQueryOptions,
} from '@nerv-iip/api-client'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { reactive, shallowRef } from 'vue'

import { bindWmsWorkScopeFilters, useWmsWorkScope } from './useWmsWorkScope'

const catalogData = vi.hoisted(() => ({ value: undefined as unknown }))
const contextState = vi.hoisted(() => ({
  organizationId: 'org-001',
  environmentId: 'env-dev',
}))

vi.mock('@/stores/businessContext', () => ({
  useBusinessContextStore: () => contextState,
}))

vi.mock('@nerv-iip/api-client', () => ({
  getBusinessConsoleWmsReceiptWorkScopesQueryOptions: vi.fn(() => ({
    key: [{ _id: 'receiptWorkScopes' }],
    query: vi.fn(),
  })),
  getBusinessConsoleWmsShipmentWorkScopesQueryOptions: vi.fn(() => ({
    key: [{ _id: 'shipmentWorkScopes' }],
    query: vi.fn(),
  })),
  getBusinessConsoleWmsCountWorkScopesQueryOptions: vi.fn(() => ({
    key: [{ _id: 'countWorkScopes' }],
    query: vi.fn(),
  })),
}))

vi.mock('@pinia/colada', () => ({
  useQuery: vi.fn((factory) => {
    const options = factory()
    return {
      data: catalogData,
      isLoading: shallowRef(false),
      error: shallowRef(),
      refetch: vi.fn(),
      options,
    }
  }),
}))

describe('useWmsWorkScope', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    contextState.organizationId = 'org-001'
    contextState.environmentId = 'env-dev'
    catalogData.value = {
      success: true,
      data: {
        actorPrincipalId: 'emp049',
        items: [
          {
            scopeKind: 'work-pool',
            scopeId: 'WMS-SITE-001-RECEIVING',
            displayName: '一号仓收货作业池',
          },
          {
            scopeKind: 'site',
            scopeId: 'SITE-001',
            displayName: '一号仓库',
          },
          {
            scopeKind: 'self',
            scopeId: 'emp049',
            displayName: '吴桂芳',
          },
          {
            scopeKind: 'organization',
            scopeId: 'org-001',
            displayName: '禁止兜底的组织全量',
          },
        ],
      },
    }
  })

  it.each([
    ['receipts', getBusinessConsoleWmsReceiptWorkScopesQueryOptions],
    ['shipments', getBusinessConsoleWmsShipmentWorkScopesQueryOptions],
    ['counts', getBusinessConsoleWmsCountWorkScopesQueryOptions],
  ] as const)('按 %s 作业域查询业务上下文中的可信目录', (catalog, factory) => {
    useWmsWorkScope(catalog)

    expect(factory).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
      },
    })
  })

  it('只接受 self/work-pool/site，并默认选择后端排序的首项', () => {
    const scope = useWmsWorkScope('receipts')

    expect(scope.principalId.value).toBe('emp049')
    expect(scope.scopeOptions.value).toEqual([
      { label: '一号仓收货作业池', value: 'work-pool:WMS-SITE-001-RECEIVING' },
      { label: '一号仓库', value: 'site:SITE-001' },
      { label: '我的任务', value: 'self:emp049' },
    ])
    expect(scope.scopeKey.value).toBe('work-pool:WMS-SITE-001-RECEIVING')
    expect(scope.scopeKind.value).toBe('work-pool')
    expect(scope.scopeId.value).toBe('WMS-SITE-001-RECEIVING')
  })

  it('切换目录范围时重置分页，并把可信 kind/id 绑定到列表筛选', () => {
    const filters = reactive({
      organizationId: '',
      environmentId: '',
      skip: 40,
      take: 20,
      scopeKind: undefined as string | undefined,
      scopeId: undefined as string | undefined,
    })
    const scope = bindWmsWorkScopeFilters(filters, 'shipments')

    expect(filters).toMatchObject({
      skip: 0,
      scopeKind: 'work-pool',
      scopeId: 'WMS-SITE-001-RECEIVING',
    })

    filters.skip = 20
    scope.scopeKey.value = 'site:SITE-001'

    expect(filters).toMatchObject({
      skip: 0,
      scopeKind: 'site',
      scopeId: 'SITE-001',
    })

    scope.scopeKey.value = 'organization:org-001'
    expect(filters.scopeKind).toBeUndefined()
    expect(filters.scopeId).toBeUndefined()
  })

  it.each([
    { success: false, data: null },
    { success: true, data: null },
    { success: true, data: { actorPrincipalId: 'emp049', items: [] } },
    {
      success: true,
      data: {
        actorPrincipalId: 'emp049',
        items: [{ scopeKind: 'self', scopeId: '', displayName: '畸形范围' }],
      },
    },
  ])('目录失败、为空或畸形时 fail closed，不伪造组织全量', (response) => {
    catalogData.value = response

    const scope = useWmsWorkScope('counts')

    expect(scope.scopeOptions.value).toEqual([])
    expect(scope.hasSelection.value).toBe(false)
    expect(scope.scopeKind.value).toBeUndefined()
    expect(scope.scopeId.value).toBeUndefined()
  })
})
