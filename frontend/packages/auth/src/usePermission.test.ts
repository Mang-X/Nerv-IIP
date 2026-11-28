import { describe, expect, it } from 'vitest'
import { ref } from 'vue'
import { usePermission } from './usePermission'

describe('usePermission', () => {
  it('updates readiness and checks after each principal change', () => {
    const codes = ref<string[]>()
    const { isReady, can, canAny, canAll } = usePermission(codes)

    expect(isReady.value).toBe(false)
    expect(can('read')).toBe(false)
    expect(canAll([])).toBe(false)

    codes.value = []
    expect(isReady.value).toBe(true)
    expect(canAll([])).toBe(true)
    expect(canAny([])).toBe(false)

    codes.value = ['read']
    expect(can('read')).toBe(true)
    expect(canAny(['write', 'read'])).toBe(true)
    expect(canAll(['read', 'write'])).toBe(false)

    codes.value = ['read', 'write']
    expect(canAll(['read', 'write'])).toBe(true)

    codes.value = undefined
    expect(isReady.value).toBe(false)
    expect(canAny(['read', 'write'])).toBe(false)
  })
})
