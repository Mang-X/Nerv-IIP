import { nextTick, ref } from 'vue'
import { describe, expect, it } from 'vitest'
import { useListFreshness } from './useListFreshness'

describe('useListFreshness', () => {
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
})
