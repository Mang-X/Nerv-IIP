import { PiniaColada } from '@pinia/colada'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { defineComponent } from 'vue'
import { afterEach, expect, it, vi } from 'vitest'
import { useBusinessContextStore } from '@/stores/businessContext'
import {
  MRP_RUN_POLL_INTERVAL_MS,
  MRP_RUN_POLL_TIMEOUT_MS,
  useBusinessPlanning,
} from './useBusinessPlanning'

const runs = vi.hoisted(() => vi.fn())
vi.mock('@nerv-iip/api-client', async (importOriginal) => {
  const empty = async () => ({ success: true, data: { items: [] } })
  return {
    ...(await importOriginal<typeof import('@nerv-iip/api-client')>()),
    listBusinessConsolePlanningDemandsQueryOptions: () => ({ key: ['demands'], query: empty }),
    listBusinessConsolePlanningMpsBucketsQueryOptions: () => ({ key: ['buckets'], query: empty }),
    listBusinessConsolePlanningSuggestionsQueryOptions: () => ({
      key: ['suggestions'],
      query: empty,
    }),
    listBusinessConsolePlanningMrpRunsQueryOptions: () => ({ key: ['runs'], query: runs }),
    runBusinessConsolePlanningMrpMutationOptions: () => ({
      mutation: async () => ({ success: true, data: { runId: 'run-1' } }),
    }),
  }
})

async function startRun() {
  vi.useFakeTimers()
  runs.mockReset().mockResolvedValue({ success: true, data: { items: [] } })
  let planning!: ReturnType<typeof useBusinessPlanning>
  mount(
    defineComponent({
      setup() {
        useBusinessContextStore().patchContext({ organizationId: 'org', environmentId: 'env' })
        planning = useBusinessPlanning()
        return () => null
      },
    }),
    { global: { plugins: [createPinia(), PiniaColada] } },
  )
  await flushPromises()
  await planning.runMrp()
  runs.mockRejectedValue(new Error('offline'))
  await vi.advanceTimersByTimeAsync(MRP_RUN_POLL_INTERVAL_MS)
  expect(planning.mrpRunsError.value?.message).toBe('offline')
  expect(planning.activeMrpRun.status).toBe('queued')
  return planning
}

afterEach(() => vi.useRealTimers())

it('completes the accepted run after a failed poll recovers', async () => {
  const planning = await startRun()
  runs.mockResolvedValue({
    success: true,
    data: { items: [{ runId: 'run-1', status: 'Completed', suggestionCount: 4 }] },
  })
  await vi.advanceTimersByTimeAsync(MRP_RUN_POLL_INTERVAL_MS)
  expect(planning.activeMrpRun).toMatchObject({ status: 'completed', suggestionCount: 4 })
  expect(planning.mrpRunsError.value).toBeNull()
})

it('stops tracking at the deadline after consecutive query failures', async () => {
  const planning = await startRun()
  await vi.advanceTimersByTimeAsync(MRP_RUN_POLL_TIMEOUT_MS - 2 * MRP_RUN_POLL_INTERVAL_MS)
  expect(planning.activeMrpRun.status).toBe('queued')
  await vi.advanceTimersByTimeAsync(MRP_RUN_POLL_INTERVAL_MS)
  expect(planning.activeMrpRun.status).toBe('polling-timeout')
  const calls = runs.mock.calls.length
  await vi.advanceTimersByTimeAsync(MRP_RUN_POLL_INTERVAL_MS)
  expect(runs).toHaveBeenCalledTimes(calls)
})
