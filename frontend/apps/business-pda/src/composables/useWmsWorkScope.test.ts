import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'

import {
  getBusinessConsoleWmsCountWorkScopesQueryOptions,
  getBusinessConsoleWmsReceiptWorkScopesQueryOptions,
  getBusinessConsoleWmsShipmentWorkScopesQueryOptions,
} from '@nerv-iip/api-client'
import { useWmsWorkScope } from './useWmsWorkScope'

const catalogData = vi.hoisted(() => ({ value: undefined as unknown }))
const authState = vi.hoisted(() => ({
  principal: {
    principalId: 'session-principal',
    organizationId: 'org-001',
    environmentId: 'env-dev',
  },
}))

vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({
    get principal() {
      return authState.principal
    },
  }),
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
    catalogData.value = {
      success: true,
      data: {
        actorPrincipalId: 'emp049',
        items: [
          {
            scopeKind: 'work-pool',
            scopeId: 'WMS-SITE-001-RECEIVING',
            displayName: '一号仓收货作业池',
            siteCode: 'SITE-001',
            poolCode: 'WMS-SITE-001-RECEIVING',
          },
          {
            scopeKind: 'site',
            scopeId: 'SITE-001',
            displayName: '一号仓库',
            siteCode: 'SITE-001',
          },
          {
            scopeKind: 'self',
            scopeId: 'emp049',
            displayName: '吴桂芳',
            siteCode: 'SITE-001',
          },
          { scopeKind: 'organization', scopeId: 'org-001', displayName: '禁止兜底的组织全量' },
        ],
      },
    }
  })

  it.each([
    ['receipts', getBusinessConsoleWmsReceiptWorkScopesQueryOptions],
    ['shipments', getBusinessConsoleWmsShipmentWorkScopesQueryOptions],
    ['counts', getBusinessConsoleWmsCountWorkScopesQueryOptions],
  ] as const)('按 %s 作业域查询后端可信目录，不让客户端提交 permission', (catalog, factory) => {
    useWmsWorkScope(catalog)

    expect(factory).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
      },
    })
  })

  it('只接受 self/work-pool/site，并按目录中的 self 默认选择我的任务', () => {
    const scope = useWmsWorkScope('shipments')

    expect(scope.principalId.value).toBe('emp049')
    expect(scope.scopeOptions.value).toEqual([
      { label: '一号仓收货作业池', value: 'work-pool:WMS-SITE-001-RECEIVING' },
      { label: '一号仓库', value: 'site:SITE-001' },
      { label: '我的任务', value: 'self:emp049' },
    ])
    expect(scope.scopeKey.value).toBe('self:emp049')
    expect(scope.scopeKind.value).toBe('self')
    expect(scope.scopeId.value).toBe('emp049')
    expect(scope.hasSelection.value).toBe(true)
  })

  it('切换作业池或站点时只输出目录授权的 kind/id', () => {
    const scope = useWmsWorkScope('receipts')

    scope.scopeKey.value = 'work-pool:WMS-SITE-001-RECEIVING'
    expect(scope.scopeKind.value).toBe('work-pool')
    expect(scope.scopeId.value).toBe('WMS-SITE-001-RECEIVING')

    scope.scopeKey.value = 'organization:org-001'
    expect(scope.hasSelection.value).toBe(false)
    expect(scope.scopeKind.value).toBeUndefined()
    expect(scope.scopeId.value).toBeUndefined()
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
  })
})
