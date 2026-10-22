import { nextTick, ref } from 'vue'
import { describe, expect, it } from 'vitest'
import { useScopeBoundListResponse } from './useScopeBoundListResponse'

describe('useScopeBoundListResponse', () => {
  it('drops the previous scope response until the new scope publishes its own', async () => {
    const data = ref<unknown>({ success: true, data: { items: [{ id: 'A' }] } })
    const scopeKey = ref('org-a:env-a')
    const enabled = ref(true)
    const currentResponse = useScopeBoundListResponse(data, scopeKey, enabled)

    expect(currentResponse.value).toEqual({ success: true, data: { items: [{ id: 'A' }] } })

    enabled.value = false
    await nextTick()
    expect(currentResponse.value).toBeUndefined()

    scopeKey.value = 'org-b:env-b'
    enabled.value = true
    await nextTick()
    expect(currentResponse.value).toBeUndefined()

    data.value = { success: true, data: { items: [{ id: 'B' }] } }
    await nextTick()
    expect(currentResponse.value).toEqual({ success: true, data: { items: [{ id: 'B' }] } })
  })
})
