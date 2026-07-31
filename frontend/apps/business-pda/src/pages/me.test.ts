import { mount } from '@vue/test-utils'
import { ref } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

const push = vi.fn(() => Promise.resolve())
type LogoutStatus = 'revoked' | 'failed' | 'timed-out' | 'no-session'
const logoutAndRevoke = vi.fn<
  (_options?: { timeoutMs?: number }) => Promise<{ status: LogoutStatus }>
>(() => Promise.resolve({ status: 'revoked' }))
const clearCache = vi.fn()
const refresh = vi.fn(() => Promise.resolve())
const profileState = ref<'loading' | 'error' | 'partial' | 'ready'>('ready')

vi.mock('vue-router', () => ({ useRouter: () => ({ push }) }))
vi.mock('@/stores/auth', () => ({ useAuthStore: () => ({ logoutAndRevoke }) }))
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
    wmsAuthorizedScopeLabels: ref([
      '收货/上架 · 作业池 · 一号仓收货作业池',
      '收货/上架 · 站点 · 一号仓',
    ]),
    wmsCurrentScopeLabels: ref(['收货/上架 · 作业池 · 一号仓收货作业池']),
    resolvedAtUtc: ref('2026-07-31T12:00:00.000Z'),
    online: ref(true),
    state: profileState,
    refresh,
  }),
  usePdaLogout: () => ({ clearCache }),
}))

import MePage from './me.vue'

describe('PDA profile page', () => {
  beforeEach(() => {
    profileState.value = 'ready'
    logoutAndRevoke.mockReset()
    logoutAndRevoke.mockResolvedValue({ status: 'revoked' })
    clearCache.mockClear()
    refresh.mockClear()
    push.mockClear()
  })

  it('renders verified identity, worker, roles, scope and network facts', () => {
    const wrapper = mount(MePage)

    expect(wrapper.text()).toContain('user-emp-010')
    expect(wrapper.text()).toContain('EMP-010')
    expect(wrapper.text()).toContain('操作工')
    expect(wrapper.text()).toContain('机加早班')
    expect(wrapper.text()).toContain('PDA 操作员')
    expect(wrapper.text()).toContain('班组 · 机加早班')
    expect(wrapper.text()).toContain('收货/上架 · 站点 · 一号仓')
    expect(wrapper.text()).toContain('当前选择')
    expect(wrapper.text()).toContain('2026')
    expect(wrapper.text()).toContain('在线')
  })

  it.each([
    ['loading', '正在加载角色与范围'],
    ['error', '加载角色与范围失败'],
    ['partial', '部分角色或范围加载失败'],
  ] as const)(
    'renders %s distinctly from confirmed empty and offers retry',
    async (state, message) => {
      profileState.value = state
      const wrapper = mount(MePage)

      expect(wrapper.text()).toContain(message)
      expect(wrapper.text()).not.toContain('未返回可读角色')
      if (state !== 'loading') {
        await wrapper.get('[data-testid="retry-profile"]').trigger('click')
        expect(refresh).toHaveBeenCalledOnce()
      }
    },
  )

  it('clears query/application caches and session before returning to login', async () => {
    const wrapper = mount(MePage)

    await wrapper.get('[data-testid="logout"]').trigger('click')

    expect(clearCache).toHaveBeenCalledOnce()
    expect(logoutAndRevoke).toHaveBeenCalledOnce()
    expect(push).toHaveBeenCalledWith({ path: '/login' })
  })

  it.each([
    ['failed', 'failed'],
    ['timed-out', 'timed-out'],
  ] as const)(
    'fails safe locally and reports a %s remote revoke outcome',
    async (status, query) => {
      logoutAndRevoke.mockResolvedValueOnce({ status })
      const wrapper = mount(MePage)

      await wrapper.get('[data-testid="logout"]').trigger('click')

      expect(clearCache).toHaveBeenCalledOnce()
      expect(push).toHaveBeenCalledWith({ path: '/login', query: { logout: query } })
    },
  )
})
