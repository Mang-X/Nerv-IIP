import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'
import { useAuthStore } from '@/stores/auth'
import { useNotificationDeadLetters } from './useNotificationDeadLetters'

const state = vi.hoisted(() => ({ envelope: {} as unknown, refreshFails: false }))
vi.mock('@nerv-iip/api-client', () => {
  const query = () => ({ key: [], query: vi.fn() })
  const mutation = () => ({ mutation: async () => state.envelope })
  return {
    getConsoleNotificationDeadLetterMetricsQueryOptions: query,
    getConsoleNotificationDeadLetterQueryOptions: query,
    listConsoleNotificationDeadLettersQueryOptions: query,
    ignoreConsoleNotificationDeadLetterMutationOptions: mutation,
    replayConsoleNotificationDeadLetterMutationOptions: mutation,
    replayConsoleNotificationDeadLettersMutationOptions: mutation,
  }
})
vi.mock('@pinia/colada', () => ({
  useQueryCache: () => ({ invalidateQueries: async () => {} }),
  useQuery: () => ({
    data: shallowRef(state.envelope),
    error: shallowRef(),
    isLoading: shallowRef(false),
    refetch: async () => {
      if (state.refreshFails) throw {}
    },
  }),
  useMutation: (options: { mutation: (vars: unknown) => Promise<unknown> }) => ({
    error: shallowRef(),
    isLoading: shallowRef(false),
    mutateAsync: options.mutation,
  }),
}))

describe('dead-letter Chinese failure feedback', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    useAuthStore().principal = { organizationId: 'org-1', environmentId: 'env-1' }
    state.envelope = { success: false }
    state.refreshFails = false
  })
  it('reports loading and refresh failures instead of presenting success', async () => {
    const letters = useNotificationDeadLetters()
    expect(letters.allError.value?.message).toBe('无法加载死信队列。')
    state.refreshFails = true
    await letters.refreshDeadLetters()
    expect(letters.actionError.value?.message).toBe('无法刷新死信队列。')
  })
  it.each([
    ['replay', '无法重放死信。'],
    ['replayFiltered', '无法批量重放死信。'],
    ['ignore', '无法忽略死信。'],
  ] as const)('rejects a failed %s operation with Chinese feedback', async (operation, message) => {
    const letters = useNotificationDeadLetters()
    await expect(
      operation === 'ignore'
        ? letters.ignore('dead-letter-1', '不需处理')
        : operation === 'replay'
          ? letters.replay('dead-letter-1')
          : letters.replayFiltered(),
    ).rejects.toThrow(message)
    expect(letters.actionError.value?.message).toBe(message)
  })
})
