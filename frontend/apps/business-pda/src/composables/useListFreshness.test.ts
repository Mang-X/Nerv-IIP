import { nextTick, ref, shallowRef, watch } from 'vue'
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
})

describe('useScopeBoundListResponse', () => {
  type Envelope = { success: true; id: string }

  /**
   * Models a query cache that resolves an already-cached entry synchronously
   * when the key changes — i.e. browser back/forward returning to a scope the
   * cache still holds. The cached value is republished *before* the projection's
   * own invalidation watcher observes the new key, and it never changes again,
   * so a projection cleared after that publish can never recover.
   */
  function createCacheHitHarness(cache: Record<string, Envelope>, initialScopeKey: string) {
    const scopeKey = ref(initialScopeKey)
    const data = shallowRef<Envelope | undefined>(cache[initialScopeKey])
    const stop = watch(
      scopeKey,
      (key) => {
        data.value = cache[key]
      },
      { flush: 'sync' },
    )
    const projection = useScopeBoundListResponse(data, scopeKey, () => true)
    return { scopeKey, projection, stop }
  }

  it('rebinds to a cached response when navigation returns to a previous scope', () => {
    const first: Envelope = { success: true, id: 'WO-1' }
    const second: Envelope = { success: true, id: 'WO-2' }
    const { scopeKey, projection, stop } = createCacheHitHarness(
      { 'scope:WO-1': first, 'scope:WO-2': second },
      'scope:WO-1',
    )

    expect(projection.value).toBe(first)

    // Forward navigation to a scope the cache has never served: still correct.
    scopeKey.value = 'scope:WO-2'
    expect(projection.value).toBe(second)

    // Browser back: the cache hit republishes WO-1 before the invalidation runs.
    scopeKey.value = 'scope:WO-1'
    expect(projection.value).toBe(first)

    // Browser forward: the regression — the projection used to freeze at
    // undefined here, which the report page rendered as "工单不存在".
    scopeKey.value = 'scope:WO-2'
    expect(projection.value).toBe(second)

    stop()
  })

  it('still drops a response bound to a scope that is no longer current', () => {
    const scopeKey = ref('scope:WO-1')
    const enabled = ref(true)
    const data = shallowRef<Envelope | undefined>({ success: true, id: 'WO-1' })
    const projection = useScopeBoundListResponse(data, scopeKey, enabled)

    expect(projection.value).toEqual({ success: true, id: 'WO-1' })

    // Scope moves on while the query has not published anything for it yet:
    // the previous scope's response must not leak into the new scope.
    scopeKey.value = 'scope:WO-2'
    expect(projection.value).toBeUndefined()

    // Unbinding the scope entirely clears the projection too.
    scopeKey.value = 'scope:WO-1'
    data.value = { success: true, id: 'WO-1' }
    expect(projection.value).toEqual({ success: true, id: 'WO-1' })
    enabled.value = false
    expect(projection.value).toBeUndefined()
  })
})
