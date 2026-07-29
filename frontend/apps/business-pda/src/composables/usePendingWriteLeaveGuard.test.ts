import { mount } from '@vue/test-utils'
import { defineComponent, h, shallowRef } from 'vue'
import { describe, expect, it, vi } from 'vitest'

const routerState = vi.hoisted(() => ({
  guard: undefined as (() => boolean) | undefined,
}))

vi.mock('vue-router', () => ({
  onBeforeRouteLeave: vi.fn((guard: () => boolean) => {
    routerState.guard = guard
  }),
}))

import { usePendingWriteLeaveGuard } from './usePendingWriteLeaveGuard'

describe('usePendingWriteLeaveGuard', () => {
  it('blocks route leave and refresh only while a write result is unknown', () => {
    const locked = shallowRef(true)
    mount(
      defineComponent({
        setup() {
          usePendingWriteLeaveGuard(locked)
          return () => h('div')
        },
      }),
    )

    expect(routerState.guard?.()).toBe(false)
    const blockedRefresh = new Event('beforeunload', { cancelable: true })
    window.dispatchEvent(blockedRefresh)
    expect(blockedRefresh.defaultPrevented).toBe(true)

    locked.value = false
    expect(routerState.guard?.()).toBe(true)
    const allowedRefresh = new Event('beforeunload', { cancelable: true })
    window.dispatchEvent(allowedRefresh)
    expect(allowedRefresh.defaultPrevented).toBe(false)
  })
})
