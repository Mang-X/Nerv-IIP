import { nextTick, ref } from 'vue'
import { afterEach, describe, expect, it, vi } from 'vitest'
import { useListFreshness, useScopeBoundListResponse } from './useListFreshness'

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

  it('clears an unbound scope timestamp and preserves it during a same-scope refresh', async () => {
    vi.useFakeTimers()
    vi.setSystemTime('2026-07-28T01:00:00.000Z')
    const data = ref<unknown>({ success: true, data: { items: [] } })
    const enabled = ref(true)
    const updatedAt = useListFreshness(data, enabled)

    expect(updatedAt.value).toBe('2026-07-28T01:00:00.000Z')

    data.value = { success: false }
    await nextTick()
    expect(updatedAt.value).toBe('2026-07-28T01:00:00.000Z')

    data.value = undefined
    await nextTick()
    expect(updatedAt.value).toBeNull()

    enabled.value = false
    data.value = { success: true, data: { items: [] } }
    await nextTick()
    expect(updatedAt.value).toBeNull()
  })

  it('binds a cached response restored just before its scope identity changes', async () => {
    const scopeA = { success: true, data: { id: 'A' } }
    const scopeB = { success: true, data: { id: 'B' } }
    const data = ref(scopeA)
    const scopeKey = ref('scope-a')
    const bound = useScopeBoundListResponse(data, scopeKey, true)

    expect(bound.value).toStrictEqual(scopeA)

    // Query libraries may publish the cached value before the dependent scope key
    // watcher runs during browser back/forward restoration.
    data.value = scopeB
    scopeKey.value = 'scope-b'
    await nextTick()

    expect(bound.value).toStrictEqual(scopeB)
  })
})
