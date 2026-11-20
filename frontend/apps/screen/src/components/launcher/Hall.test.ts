import { mount, RouterLinkStub } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { expect, it, vi } from 'vitest'
import Hall from '@/pages/index.vue'

vi.mock('@/stores/realAuth', () => ({
  useRealAuthStore: () => ({ principal: { organizationId: 'org-a', environmentId: 'env-a' } }),
}))

it('正常门厅首帧保留导航和真实范围，缺源不会显示正常链路', () => {
  const page = mount(Hall, {
    global: { plugins: [createPinia()], stubs: { RouterLink: RouterLinkStub } },
  })
  try {
    expect(page.text()).not.toContain('数据链路正常')
    expect(page.text()).toContain('org-a')
    expect(page.text()).toContain('综合健康度口径未接入')
    expect(page.findAllComponents(RouterLinkStub).map((link) => link.props('to'))).toContain(
      '/andon',
    )
  } finally {
    page.unmount()
  }
})
