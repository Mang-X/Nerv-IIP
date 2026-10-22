import { nextTick, ref } from 'vue'
import { describe, expect, it } from 'vitest'
import { useScopeBoundListResponse } from './useScopeBoundListResponse'

describe('useScopeBoundListResponse', () => {
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
