import { mount, flushPromises } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { defineComponent, shallowRef } from 'vue'
import { PiniaColada } from '@pinia/colada'
import { PiniaColadaAutoRefetch } from '@pinia/colada-plugin-auto-refetch'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useBusinessScheduling, useSchedulingPlanSummary } from './useBusinessScheduling'

const backend = vi.hoisted(() => ({
  invalidated: false,
  status: 'generated',
  history: vi.fn(),
  detail: vi.fn(),
  summaryHistory: vi.fn(),
}))
vi.mock('@nerv-iip/api-client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@nerv-iip/api-client')>()),
  listBusinessConsoleSchedulingPlanHistory: backend.summaryHistory,
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
  it('polls the draft beyond the first unfiltered history page independently of browsing another plan', async () => {
    vi.useFakeTimers()
    backend.invalidated = false
    backend.status = 'generated'
    backend.history.mockReset().mockResolvedValue({ success: true, data: { items: [], total: 0 } })
    backend.detail.mockReset().mockResolvedValue({ success: true, data: { planId: 'plan-other' } })
    backend.summaryHistory.mockReset().mockImplementation(async ({ query }) => ({
      data: {
        success: true,
        data: {
          total: 101,
          items:
            query.pageIndex === 0
              ? Array.from({ length: 100 }, (_, i) => ({ planId: `plan-${i + 2}` }))
              : [
                  {
                    planId: 'plan-001',
                    status: backend.status,
                    isInvalidated: backend.invalidated,
                    latestInvalidationReasonCode: 'equipmentUnavailable',
                  },
                ],
        },
      },
    }))
    const pinia = createPinia()
    const draftId = shallowRef<string>()
    let summary!: ReturnType<typeof useSchedulingPlanSummary>
    const wrapper = mount(
      defineComponent({
        setup() {
          useBusinessContextStore().patchContext({
            organizationId: 'org-001',
            environmentId: 'env-dev',
          })
          const browsing = useBusinessScheduling()
          browsing.filters.isInvalidated = false
          browsing.detailSelection.planId = 'plan-other'
          summary = useSchedulingPlanSummary(draftId)
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
      expect(backend.summaryHistory).not.toHaveBeenCalled()
      draftId.value = 'plan-001'
      await flushPromises()
      expect(summary.summary.value?.isInvalidated).toBe(false)
      expect(backend.summaryHistory.mock.calls.map(([options]) => options.query)).toEqual([
        { organizationId: 'org-001', environmentId: 'env-dev', pageIndex: 0, pageSize: 100 },
        { organizationId: 'org-001', environmentId: 'env-dev', pageIndex: 1, pageSize: 100 },
      ])
      backend.invalidated = true
      backend.status = 'superseded'
      await vi.advanceTimersByTimeAsync(5000)
      await flushPromises()
      expect(summary.summary.value).toMatchObject({
        planId: 'plan-001',
        isInvalidated: true,
        status: 'superseded',
      })
      wrapper.unmount()
      const calls = backend.summaryHistory.mock.calls.length
      await vi.advanceTimersByTimeAsync(10_000)
      expect(backend.summaryHistory).toHaveBeenCalledTimes(calls)
    } finally {
      wrapper.unmount()
    }
  })
})
