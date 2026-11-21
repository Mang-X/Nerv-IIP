import { mount } from '@vue/test-utils'
import { defineComponent, h, nextTick, shallowRef } from 'vue'
import { describe, expect, it, vi } from 'vitest'

const routerState = vi.hoisted(() => ({
  guard: undefined as (() => boolean) | undefined,
}))

vi.mock('vue-router', () => ({
  onBeforeRouteLeave: vi.fn((guard: () => boolean) => {
    routerState.guard = guard
  }),
}))

import { usePendingWriteLeaveGuard } from '@nerv-iip/app-shell/pending-write'

describe('usePendingWriteLeaveGuard', () => {
  it('blocks route leave and refresh only while a write result is unknown', async () => {
    const locked = shallowRef(true)
    const showWriter = shallowRef(true)
    const Writer = defineComponent({
      setup() {
        usePendingWriteLeaveGuard(locked)
        return () => h('div')
      },
    })
    mount(defineComponent({ setup: () => () => (showWriter.value ? h(Writer) : null) }))

    expect(routerState.guard?.()).toBe(false)
    const blockedRefresh = new Event('beforeunload', { cancelable: true })
    window.dispatchEvent(blockedRefresh)
    expect(blockedRefresh.defaultPrevented).toBe(true)

    locked.value = false
    expect(routerState.guard?.()).toBe(true)
    const allowedRefresh = new Event('beforeunload', { cancelable: true })
    window.dispatchEvent(allowedRefresh)
    expect(allowedRefresh.defaultPrevented).toBe(false)

    locked.value = true
    showWriter.value = false
    await nextTick()
    const afterUnmount = new Event('beforeunload', { cancelable: true })
    window.dispatchEvent(afterUnmount)
    expect(afterUnmount.defaultPrevented).toBe(false)
  })
})
