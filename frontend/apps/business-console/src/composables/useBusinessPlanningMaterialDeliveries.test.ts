import { beforeEach, describe, expect, it, vi } from 'vitest'
import { createPinia, setActivePinia } from 'pinia'
import { effectScope, nextTick, shallowRef } from 'vue'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useBusinessPlanningMaterialDeliveries } from './useBusinessPlanningMaterialDeliveries'

const queries = vi.hoisted(() => ({
  factories: [] as Array<
    () => {
      enabled: boolean
      query: (context: object) => Promise<unknown>
      request: {
        path?: { runId: string }
        query: {
          planId?: string
          pageIndex?: number
          organizationId: string
          environmentId: string
        }
      }
    }
  >,
}))
vi.mock('@nerv-iip/api-client', () => ({
  getBusinessConsolePlanningMaterialDeliveriesQueryOptions: (request: unknown) => ({
    request,
    key: ['deliveries'],
    query: async () => ({ success: true, data: {} }),
  }),
  listBusinessConsoleSchedulingPlanHistoryQueryOptions: (request: unknown) => ({
    request,
    key: ['plans'],
    query: async () => ({ success: true, data: { items: [], total: 0 } }),
  }),
}))
vi.mock('@pinia/colada', () => ({
  useQuery: (factory: (typeof queries.factories)[number]) => {
    queries.factories.push(factory)
    return {
      data: shallowRef(undefined),
      isLoading: shallowRef(false),
      error: shallowRef(null),
      refetch: vi.fn(),
    }
  },
}))
// PublicContract / DomainInvariant: #4097，不可自动选择最新方案；所选方案必须进入真实请求。
describe('物料交付查询选择', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    queries.factories = []
  })
  it('未选方案不发送 planId，显式选择后传递同一方案且方案分页真实', async () => {
    const scope = effectScope()
    const state = scope.run(() => useBusinessPlanningMaterialDeliveries())!
    const context = useBusinessContextStore()
    context.patchContext({ organizationId: 'org-1', environmentId: 'env-1' })
    state.selection.runId = 'run-1'
    expect(queries.factories[1]!().request).toEqual({
      path: { runId: 'run-1' },
      query: { organizationId: 'org-1', environmentId: 'env-1', planId: undefined },
    })
    state.selection.planId = 'plan-selected'
    expect(queries.factories[1]!().request.query.planId).toBe('plan-selected')
    state.planPage.value = 2
    expect(queries.factories[0]!().request.query.pageIndex).toBe(1)
    context.patchContext({ organizationId: 'org-2' })
    await nextTick()
    expect(state.selection.planId).toBe('')
    expect(state.planPage.value).toBe(1)
    expect(queries.factories[1]!().request.query.organizationId).toBe('org-2')
    scope.stop()
  })
  it('没有运行或组织环境时不发物料交付查询', () => {
    const scope = effectScope()
    const state = scope.run(() => useBusinessPlanningMaterialDeliveries())!
    expect(queries.factories[1]!().enabled).toBe(false)
    state.selection.runId = 'run-1'
    expect(queries.factories[1]!().enabled).toBe(false)
    scope.stop()
  })
})
