import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'

import { toolingStatusLabel, toolingTypeLabel, useBusinessTooling } from './useBusinessTooling'

const state = vi.hoisted(() => ({
  queryFactory: undefined as undefined | (() => Record<string, unknown>),
  mutationCalls: new Map<string, unknown[]>(),
  invalidations: 0,
}))

vi.mock('@nerv-iip/api-client', () => ({
  listBusinessConsoleToolingAssetsQueryOptions: vi.fn((options) => ({
    key: [{ _id: 'listBusinessConsoleToolingAssets' }],
    options,
  })),
  registerBusinessConsoleToolingAssetMutationOptions: vi.fn(() => ({
    key: [{ _id: 'registerBusinessConsoleToolingAsset' }],
  })),
  changeBusinessConsoleToolingStatusMutationOptions: vi.fn(() => ({
    key: [{ _id: 'changeBusinessConsoleToolingStatus' }],
  })),
  recordBusinessConsoleToolingUsageMutationOptions: vi.fn(() => ({
    key: [{ _id: 'recordBusinessConsoleToolingUsage' }],
  })),
}))

vi.mock('@pinia/colada', () => ({
  useQuery: vi.fn((factory) => {
    state.queryFactory = factory
    return {
      data: shallowRef({
        success: true,
        data: {
          items: [{ code: 'MOULD-001', name: '前地板拉延模', status: 'available' }],
          total: 23,
        },
      }),
      error: shallowRef(),
      isLoading: shallowRef(false),
      refetch: vi.fn(),
    }
  }),
  useMutation: vi.fn((options) => {
    const id = String(options.key?.[0]?._id ?? '')
    return {
      error: shallowRef(),
      isLoading: shallowRef(false),
      mutateAsync: vi.fn(async (payload) => {
        const calls = state.mutationCalls.get(id) ?? []
        calls.push(payload)
        state.mutationCalls.set(id, calls)
        await options.onSuccess?.()
        return { success: true }
      }),
    }
  }),
  useQueryCache: () => ({
    invalidateQueries: vi.fn(async () => {
      state.invalidations += 1
    }),
  }),
}))

vi.mock('./businessContextBinding', () => ({
  bindBusinessContext: (filters: object) =>
    Object.assign(filters, {
      organizationId: 'org-001',
      environmentId: 'env-dev',
    }),
  withBusinessContextEnabled: (options: object) => options,
  refetchWithBusinessContext: (_filters: object, query: { refetch: () => unknown }) =>
    query.refetch(),
}))

describe('useBusinessTooling', () => {
  beforeEach(() => {
    state.queryFactory = undefined
    state.mutationCalls.clear()
    state.invalidations = 0
  })

  it('将工装状态和类型映射为中文，并对未知类型保留服务端值', () => {
    expect(toolingStatusLabel('available')).toBe('可用')
    expect(toolingStatusLabel('maintenance')).toBe('保养中')
    expect(toolingStatusLabel('retired')).toBe('已退役')
    expect(toolingTypeLabel('mould')).toBe('模具')
    expect(toolingTypeLabel('fixture')).toBe('夹具')
    expect(toolingTypeLabel('jig')).toBe('工装夹具')
    expect(toolingTypeLabel('cutting')).toBe('刀具')
    expect(toolingTypeLabel('gauge')).toBe('检具')
    expect(toolingTypeLabel('unknown-tooling')).toBe('unknown-tooling')
  })

  it('把关键字、状态与分页作为服务端查询参数，并读取服务端总数', () => {
    const tooling = useBusinessTooling()
    tooling.filters.keyword = '模具'
    tooling.filters.status = 'maintenance'
    tooling.filters.skip = 20
    tooling.filters.take = 10

    const options = state.queryFactory?.() as { options: { query: Record<string, unknown> } }
    expect(options.options.query).toEqual({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      keyword: '模具',
      status: 'maintenance',
      skip: 20,
      take: 10,
    })
    expect(tooling.toolingAssets.value).toHaveLength(1)
    expect(tooling.toolingTotal.value).toBe(23)
  })

  it('写操作补齐业务上下文并刷新工装列表', async () => {
    const tooling = useBusinessTooling()

    await tooling.register({
      name: '前地板拉延模',
      toolingType: 'mould',
      workCenterCodes: ['WC-PRESS'],
      skuCodes: ['SKU-FLOOR'],
      maintenanceLifeCount: 80000,
    })
    await tooling.changeStatus('MOULD-001', 'maintenance', '达到规定冲次，安排保养')
    await tooling.recordUsage('MOULD-001', 1200)

    expect(state.mutationCalls.get('registerBusinessConsoleToolingAsset')?.[0]).toMatchObject({
      body: { organizationId: 'org-001', environmentId: 'env-dev' },
    })
    expect(state.mutationCalls.get('changeBusinessConsoleToolingStatus')?.[0]).toEqual({
      body: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        code: 'MOULD-001',
        status: 'maintenance',
        reason: '达到规定冲次，安排保养',
      },
    })
    expect(state.mutationCalls.get('recordBusinessConsoleToolingUsage')?.[0]).toEqual({
      body: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        code: 'MOULD-001',
        count: 1200,
      },
    })
    expect(state.invalidations).toBe(3)
  })
})
