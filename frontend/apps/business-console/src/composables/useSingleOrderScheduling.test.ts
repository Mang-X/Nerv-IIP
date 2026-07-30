import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'
import { createPinia, setActivePinia } from 'pinia'

import { useBusinessContextStore } from '@/stores/businessContext'
import {
  singleOrderSchedulingResultRoute,
  useSingleOrderScheduling,
} from './useSingleOrderScheduling'

const state = vi.hoisted(() => ({
  bodies: [] as unknown[],
  invalidated: [] as unknown[],
  response: { success: true, data: { planId: 'PLAN-SINGLE-1' } } as {
    success: boolean
    data: { planId: string } | null
    message?: string
  },
}))

vi.mock('@nerv-iip/api-client', () => ({
  createBusinessConsoleSchedulingWorkbenchPlanMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars: { body: unknown }) => {
      state.bodies.push(vars.body)
      return state.response
    }),
  })),
}))

vi.mock('@pinia/colada', () => ({
  useMutation: vi.fn((options) => ({
    isLoading: shallowRef(false),
    mutateAsync: vi.fn(async (vars) => {
      const result = await options.mutation(vars)
      await options.onSuccess?.(result)
      return result
    }),
  })),
  useQueryCache: vi.fn(() => ({
    invalidateQueries: vi.fn(async (args: unknown) => {
      state.invalidated.push(args)
    }),
  })),
}))

describe('单单排产（MAN-694 / #1262）', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    state.bodies = []
    state.invalidated = []
    state.response = { success: true, data: { planId: 'PLAN-SINGLE-1' } }
  })

  function withContext() {
    useBusinessContextStore().patchContext({
      organizationId: 'org-001',
      environmentId: 'env-dev',
    })
    return useSingleOrderScheduling()
  }

  it('复用 workbench 生成端点，orders 里只放这一张工单，窗口用调用方给的值', async () => {
    const scheduling = withContext()

    const plan = await scheduling.scheduleSingleOrder({
      workOrderId: '  WO-77  ',
      priority: 10,
      isRush: true,
      horizonStartUtc: '2026-08-01T00:00:00.000Z',
      horizonEndUtc: '2026-08-02T00:00:00.000Z',
    })

    expect(plan.planId).toBe('PLAN-SINGLE-1')
    expect(state.bodies).toHaveLength(1)
    expect(state.bodies[0]).toEqual({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      horizonStartUtc: '2026-08-01T00:00:00.000Z',
      horizonEndUtc: '2026-08-02T00:00:00.000Z',
      orders: [{ workOrderId: 'WO-77', priority: 10, isRush: true }],
    })
    // 生成后必须让方案列表/明细失效，否则跳过去看到的还是旧列表。
    expect(state.invalidated.length).toBeGreaterThan(0)
  })

  it('业务范围为空时不发请求（Common Mistakes #13）', async () => {
    const scheduling = useSingleOrderScheduling()

    await expect(
      scheduling.scheduleSingleOrder({
        workOrderId: 'WO-77',
        priority: 100,
        isRush: false,
        horizonStartUtc: '2026-08-01T00:00:00.000Z',
        horizonEndUtc: '2026-08-02T00:00:00.000Z',
      }),
    ).rejects.toThrow('请先选择组织与环境后再排产。')
    expect(state.bodies).toHaveLength(0)
  })

  it('工单为空时不发请求，也不用任何演示单号兜底（Common Mistakes #14）', async () => {
    const scheduling = withContext()

    await expect(
      scheduling.scheduleSingleOrder({
        workOrderId: '   ',
        priority: 100,
        isRush: false,
        horizonStartUtc: '2026-08-01T00:00:00.000Z',
        horizonEndUtc: '2026-08-02T00:00:00.000Z',
      }),
    ).rejects.toThrow('请先选择要排产的工单。')
    expect(state.bodies).toHaveLength(0)
  })

  it('服务端回失败信封时抛出服务端原因，不假装成功', async () => {
    state.response = { success: false, data: null, message: '工单没有生产版本' }
    const scheduling = withContext()

    await expect(
      scheduling.scheduleSingleOrder({
        workOrderId: 'WO-77',
        priority: 100,
        isRush: false,
        horizonStartUtc: '2026-08-01T00:00:00.000Z',
        horizonEndUtc: '2026-08-02T00:00:00.000Z',
      }),
    ).rejects.toThrow('工单没有生产版本')
  })

  it('落点带上 planId 与工单号，工作台可直接定位刚生成的方案', () => {
    expect(singleOrderSchedulingResultRoute('PLAN-1', 'WO-77')).toEqual({
      path: '/scheduling',
      query: { planId: 'PLAN-1', orderReference: 'WO-77' },
    })
  })
})
