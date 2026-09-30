import { mount, flushPromises } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { defineComponent } from 'vue'
import { PiniaColada } from '@pinia/colada'
import { PiniaColadaAutoRefetch } from '@pinia/colada-plugin-auto-refetch'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useBusinessScheduling } from './useBusinessScheduling'

const backend = vi.hoisted(() => ({
  invalidated: false,
  status: 'generated',
  history: vi.fn(),
  detail: vi.fn(),
}))
vi.mock('@nerv-iip/api-client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@nerv-iip/api-client')>()),
  listBusinessConsoleSchedulingPlanHistoryQueryOptions: () => ({
    key: ['scheduling-poll-history'],
    query: backend.history,
  }),
  getBusinessConsoleSchedulingPlanQueryOptions: (options: { path: { planId: string } }) => ({
    key: ['scheduling-poll-detail', options.path.planId],
    query: backend.detail,
  }),
}))

afterEach(() => vi.useRealTimers())

describe('scheduling automatic refresh', () => {
  it('refreshes backend invalidation and status on the next 5s poll and stops after leaving', async () => {
    // #4072：使用真实 AutoRefetch 插件证明下一轮刷新；MES 停机触发后同样消费既有失效事实。
    vi.useFakeTimers()
    backend.invalidated = false
    backend.status = 'generated'
    backend.history.mockReset().mockImplementation(async () => ({
      success: true,
      data: {
        total: 1,
        items: [
          {
            planId: 'plan-001',
            isInvalidated: backend.invalidated,
            latestInvalidationReasonCode: backend.invalidated ? 'equipmentUnavailable' : undefined,
          },
        ],
      },
    }))
    backend.detail.mockReset().mockImplementation(async () => ({
      success: true,
      data: { planId: 'plan-001', status: backend.status },
    }))
    const pinia = createPinia()
    let scheduling!: ReturnType<typeof useBusinessScheduling>
    const wrapper = mount(
      defineComponent({
        setup() {
          useBusinessContextStore().patchContext({
            organizationId: 'org-001',
            environmentId: 'env-dev',
          })
          scheduling = useBusinessScheduling()
          scheduling.detailSelection.planId = 'plan-001'
          return () => null
        },
      }),
      {
        global: {
          plugins: [
            pinia,
            [PiniaColada, { plugins: [PiniaColadaAutoRefetch({ autoRefetch: false })] }],
          ],
        },
      },
    )
    try {
      await flushPromises()
      expect(scheduling.plans.value[0]?.isInvalidated).toBe(false)
      expect(scheduling.planDetail.value?.status).toBe('generated')
      backend.invalidated = true
      backend.status = 'superseded'
      await vi.advanceTimersByTimeAsync(5000)
      await flushPromises()
      expect(scheduling.plans.value[0]?.isInvalidated).toBe(true)
      expect(scheduling.plans.value[0]?.latestInvalidationReasonCode).toBe('equipmentUnavailable')
      expect(scheduling.planDetail.value?.status).toBe('superseded')
      wrapper.unmount()
      const calls = [backend.history.mock.calls.length, backend.detail.mock.calls.length]
      await vi.advanceTimersByTimeAsync(10_000)
      expect([backend.history.mock.calls.length, backend.detail.mock.calls.length]).toEqual(calls)
    } finally {
      wrapper.unmount()
    }
  })
})
