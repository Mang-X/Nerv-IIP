import { mount, flushPromises } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { PiniaColada } from '@pinia/colada'
import { PiniaColadaAutoRefetch } from '@pinia/colada-plugin-auto-refetch'
import { defineComponent, shallowRef } from 'vue'
import { afterEach, expect, it, vi } from 'vitest'
import { useSchedulingDowntime } from './useSchedulingDowntime'
const backend = vi.hoisted(() => ({ query: vi.fn() }))
vi.mock('@nerv-iip/api-client', async (original) => ({
  ...(await original<typeof import('@nerv-iip/api-client')>()),
  getBusinessConsoleSchedulingDowntimeImpactQueryOptions: (options: {
    path: { planId: string }
    query: object
  }) => ({
    key: ['downtime', options.path.planId, options.query],
    query: () => backend.query(options),
  }),
}))
afterEach(() => vi.useRealTimers())
it('polls the saved baseline, updates ETR/recovery, advances the display clock, and stops on unmount', async () => {
  vi.useFakeTimers()
  vi.setSystemTime(new Date('2026-10-09T08:00:00.000Z'))
  const planId = shallowRef<string>()
  const facts = shallowRef({
    expectedRestoreAtUtc: '2026-10-09T10:00:00.000Z' as string | null,
    recoveredAtUtc: null as string | null,
  })
  backend.query.mockReset().mockImplementation(async ({ path }) => ({
    success: true,
    data: { baselinePlanId: path.planId, items: [{ fact: facts.value }] },
  }))
  let downtime!: ReturnType<typeof useSchedulingDowntime>
  const wrapper = mount(
    defineComponent({
      setup() {
        downtime = useSchedulingDowntime(planId, () => ({
          organizationId: 'org-01',
          environmentId: 'env-01',
        }))
        return () => null
      },
    }),
    {
      global: {
        plugins: [
          createPinia(),
          [PiniaColada, { plugins: [PiniaColadaAutoRefetch({ autoRefetch: false })] }],
        ],
      },
    },
  )
  await flushPromises()
  expect(backend.query).not.toHaveBeenCalled()
  planId.value = 'saved-plan'
  await flushPromises()
  expect(backend.query).toHaveBeenLastCalledWith({
    path: { planId: 'saved-plan' },
    query: { organizationId: 'org-01', environmentId: 'env-01' },
  })
  expect(downtime.impact.value?.items?.[0]?.fact?.expectedRestoreAtUtc).toBe(
    '2026-10-09T10:00:00.000Z',
  )
  facts.value = { expectedRestoreAtUtc: null, recoveredAtUtc: null }
  await vi.advanceTimersByTimeAsync(5000)
  await flushPromises()
  expect(downtime.impact.value?.items?.[0]?.fact?.expectedRestoreAtUtc).toBeNull()
  expect(downtime.now.value.toISOString()).toBe('2026-10-09T08:00:05.000Z')
  facts.value = { expectedRestoreAtUtc: null, recoveredAtUtc: '2026-10-09T08:00:00.000Z' }
  await vi.advanceTimersByTimeAsync(5000)
  await flushPromises()
  expect(downtime.impact.value?.items?.[0]?.fact?.recoveredAtUtc).toBe('2026-10-09T08:00:00.000Z')
  planId.value = 'selected-plan'
  await flushPromises()
  expect(downtime.impact.value?.baselinePlanId).toBe('selected-plan')
  wrapper.unmount()
  const calls = backend.query.mock.calls.length
  const clock = downtime.now.value
  await vi.advanceTimersByTimeAsync(10000)
  expect(backend.query.mock.calls.length).toBe(calls)
  expect(downtime.now.value).toBe(clock)
})
