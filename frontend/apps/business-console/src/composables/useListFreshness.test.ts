import { nextTick, ref } from 'vue'
import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  useListFreshness,
  useListResponseState,
  useScopeBoundListResponse,
} from './useListFreshness'

describe('useListFreshness', () => {
  afterEach(() => {
    vi.useRealTimers()
  })

  it('does not timestamp failed envelopes or missing-scope responses', async () => {
    const data = ref<unknown>({ success: false })
    const enabled = ref(false)
    const updatedAt = useListFreshness(data, enabled)

    expect(updatedAt.value).toBeNull()
    enabled.value = true
    await nextTick()
    expect(updatedAt.value).toBeNull()

    data.value = { success: true, data: { items: [] } }
    await nextTick()
    expect(updatedAt.value).not.toBeNull()
  })

  it.each([
    ['null', null],
    ['a primitive', 'success'],
    ['an array', []],
    ['an object without success', { data: { items: [] } }],
  ])('does not timestamp %s as a successful response', async (_label, value) => {
    const data = ref<unknown>(value)
    const enabled = ref(true)
    const updatedAt = useListFreshness(data, enabled)

    await nextTick()
    expect(updatedAt.value).toBeNull()
  })

  it('binds freshness and response state to the current scope', async () => {
    vi.useFakeTimers()
    vi.setSystemTime('2026-07-28T01:00:00.000Z')
    const data = ref<unknown>({ success: true, data: { items: [{ id: 'A' }] } })
    const scopeKey = ref('org-a:env-a')
    const enabled = ref(true)
    const pending = ref(false)
    const currentResponse = useScopeBoundListResponse(data, scopeKey, enabled)
    const updatedAt = useListFreshness(currentResponse, enabled)
    const { hasSuccessfulResponse, hasFailedResponse } = useListResponseState(
      currentResponse,
      enabled,
      pending,
    )

    expect(updatedAt.value).toBe('2026-07-28T01:00:00.000Z')
    expect(hasSuccessfulResponse.value).toBe(true)

    pending.value = true
    await nextTick()
    expect(updatedAt.value).toBe('2026-07-28T01:00:00.000Z')
    expect(hasSuccessfulResponse.value).toBe(false)

    pending.value = false
    enabled.value = false
    await nextTick()
    expect(currentResponse.value).toBeUndefined()
    expect(updatedAt.value).toBeNull()

    scopeKey.value = 'org-b:env-b'
    enabled.value = true
    await nextTick()
    expect(currentResponse.value).toBeUndefined()
    expect(updatedAt.value).toBeNull()

    data.value = { success: false }
    await nextTick()
    expect(updatedAt.value).toBeNull()
    expect(hasSuccessfulResponse.value).toBe(false)
    expect(hasFailedResponse.value).toBe(true)

    vi.setSystemTime('2026-07-28T02:00:00.000Z')
    data.value = { success: true, data: { items: [{ id: 'B' }] } }
    await nextTick()
    expect(updatedAt.value).toBe('2026-07-28T02:00:00.000Z')
    expect(hasSuccessfulResponse.value).toBe(true)
    expect(hasFailedResponse.value).toBe(false)
  })
})
