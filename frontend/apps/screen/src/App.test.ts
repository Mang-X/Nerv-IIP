import { flushPromises, mount } from '@vue/test-utils'
import { defineComponent, h, ref, shallowRef } from 'vue'
import { expect, it, vi } from 'vitest'
import App from './App.vue'

const principal = {
  principalId: 'inspector',
  organizationId: 'o',
  environmentId: 'e',
  permissionVersion: 1,
}
const current = shallowRef(principal)
vi.mock('@/stores/realAuth', () => ({
  useRealAuthStore: () => ({
    sessionId: 'same-session',
    get principal() {
      return current.value
    },
  }),
}))
vi.mock('vue-router', async (original) => ({
  ...(await original<typeof import('vue-router')>()),
  useRoute: () => ({ path: '/warehouse' }),
}))

it('相同主体、组织、环境和会话换权限版本时立即撤掉已挂载的业务内容', async () => {
  let resolveOld!: (value: string) => void
  const pending = new Promise<string>((resolve) => {
    resolveOld = resolve
  })
  const screen = defineComponent({
    setup() {
      const content = ref(current.value.permissionVersion === 1 ? '旧范围拣货单' : '正在核验新范围')
      if (current.value.permissionVersion === 1)
        void pending.then((value) => {
          content.value = value
        })
      return () => h('div', content.value)
    },
  })
  const page = mount(App, { global: { stubs: { RouterView: screen, Toaster: true } } })
  try {
    expect(page.text()).toContain('旧范围拣货单')
    current.value = { ...principal, permissionVersion: 2 }
    resolveOld('上一范围的迟到响应')
    await flushPromises()
    expect(page.text()).toBe('正在核验新范围')
  } finally {
    page.unmount()
  }
})
