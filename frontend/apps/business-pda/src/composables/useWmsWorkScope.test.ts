import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'

import { getBusinessConsolePrincipalWorkContextQueryOptions } from '@nerv-iip/api-client'
import { useWmsWorkScope } from './useWmsWorkScope'

const contextData = vi.hoisted(() => ({ value: undefined as unknown }))
const authState = vi.hoisted(() => ({
  principal: {
    principalId: 'emp049',
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
  getBusinessConsolePrincipalWorkContextQueryOptions: vi.fn(() => ({
    key: [{ _id: 'principalWorkContext' }],
    query: vi.fn(),
  })),
}))

vi.mock('@pinia/colada', () => ({
  useQuery: vi.fn((factory) => {
    const options = factory()
    return {
      data: contextData,
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
    contextData.value = {
      success: true,
      data: {
        authorizedScopes: [
          { kind: 'team', id: 'TEAM-WMS-01', displayName: '仓储一组' },
          { kind: 'site', id: 'SITE-001', displayName: '一号仓库' },
          { kind: 'self', id: 'emp049', displayName: '吴桂芳' },
          { kind: 'work-center', id: 'WC-01', displayName: '不属于仓储队列' },
        ],
      },
    }
  })

  it('查询当前权限下的主体范围，并默认选择我的任务', () => {
    const scope = useWmsWorkScope('business.wms.shipments.read')

    expect(getBusinessConsolePrincipalWorkContextQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        permissionCode: 'business.wms.shipments.read',
      },
    })
    expect(scope.scopeOptions.value).toEqual([
      { label: '仓储一组', value: 'team:TEAM-WMS-01' },
      { label: '一号仓库', value: 'site:SITE-001' },
      { label: '我的任务', value: 'self:emp049' },
    ])
    expect(scope.scopeKey.value).toBe('self:emp049')
    expect(scope.scopeKind.value).toBe('self')
    expect(scope.scopeId.value).toBe('emp049')
    expect(scope.hasSelection.value).toBe(true)
  })

  it('切换团队或仓库范围时只输出经后端授权的 kind/id', () => {
    const scope = useWmsWorkScope('business.wms.shipments.read')

    scope.scopeKey.value = 'team:TEAM-WMS-01'
    expect(scope.scopeKind.value).toBe('team')
    expect(scope.scopeId.value).toBe('TEAM-WMS-01')

    scope.scopeKey.value = 'work-center:WC-01'
    expect(scope.hasSelection.value).toBe(false)
  })

  it('后端未返回可用范围时不伪造组织全量', () => {
    contextData.value = { success: true, data: { authorizedScopes: [] } }

    const scope = useWmsWorkScope('business.wms.receipts.read')

    expect(scope.scopeOptions.value).toEqual([])
    expect(scope.hasSelection.value).toBe(false)
  })
})
