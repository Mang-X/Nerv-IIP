import { mount } from '@vue/test-utils'
import { computed, ref } from 'vue'
import { describe, expect, it, vi } from 'vitest'

const push = vi.fn(() => Promise.resolve())
const logout = vi.fn(() => Promise.resolve())
const clearCache = vi.fn()

vi.mock('vue-router', () => ({ useRouter: () => ({ push }) }))
vi.mock('@/stores/auth', () => ({ useAuthStore: () => ({ logout }) }))
vi.mock('@/composables/usePdaProfile', () => ({
  usePdaProfile: () => ({
    principalId: ref('user-emp-010'),
    principalType: ref('User'),
    loginName: ref('emp010'),
    displayName: ref('王建国'),
    employeeNo: ref('EMP-010'),
    jobTitle: ref('操作工'),
    departmentName: ref('生产部'),
    teamNames: ref(['机加早班']),
    roleNames: ref(['PDA 操作员', '现场人员']),
    scopeLabels: ref(['班组 · 机加早班']),
    online: ref(true),
    pending: ref(false),
    error: ref(null),
  }),
  usePdaLogout: () => ({ clearCache }),
}))

import MePage from './me.vue'

describe('PDA profile page', () => {
  it('renders verified identity, worker, roles, scope and network facts', () => {
    const wrapper = mount(MePage)

    expect(wrapper.text()).toContain('user-emp-010')
    expect(wrapper.text()).toContain('EMP-010')
    expect(wrapper.text()).toContain('操作工')
    expect(wrapper.text()).toContain('机加早班')
    expect(wrapper.text()).toContain('PDA 操作员')
    expect(wrapper.text()).toContain('班组 · 机加早班')
    expect(wrapper.text()).toContain('在线')
  })

  it('clears query/application caches and session before returning to login', async () => {
    const wrapper = mount(MePage)

    await wrapper.get('[data-testid="logout"]').trigger('click')

    expect(clearCache).toHaveBeenCalledOnce()
    expect(logout).toHaveBeenCalledOnce()
    expect(push).toHaveBeenCalledWith('/login')
  })
})
