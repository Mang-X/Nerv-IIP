import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'
import { Select } from '@nerv-iip/ui'

import SessionsPage from './index.vue'

const iamState = vi.hoisted(() => ({
  filters: {
    filterRevoked: undefined as boolean | undefined,
    filterSearch: undefined as string | undefined,
    pageIndex: 3,
    pageSize: 20,
  },
  refreshSessions: vi.fn(),
  revokeSession: vi.fn(),
  revokeSessionPending: { value: false },
  totalCount: { value: 1 },
}))
const permissionState = vi.hoisted(() => ({
  canRevoke: { value: true },
}))

vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({
    sessionId: 'session-current',
  }),
}))

vi.mock('@/composables/usePermissions', () => ({
  useHasPermission: () => computed(() => permissionState.canRevoke.value),
}))

vi.mock('@/composables/useIamAdmin', () => ({
  useIamSessions: () => ({
    filters: reactive(iamState.filters),
    refreshSessions: iamState.refreshSessions,
    revokeSession: iamState.revokeSession,
    revokeSessionError: computed(() => undefined),
    revokeSessionPending: iamState.revokeSessionPending,
    sessions: computed(() => [
      {
        sessionId: 'session-current',
        userId: 'user-admin',
        issuedAtUtc: '2026-05-20T05:00:00Z',
        expiresAtUtc: '2026-05-20T13:00:00Z',
        revokedAtUtc: null,
        permissionVersion: 7,
      },
      {
        sessionId: 'session-1',
        userId: 'user-admin',
        issuedAtUtc: '2026-05-20T06:00:00Z',
        expiresAtUtc: '2026-05-20T14:00:00Z',
        revokedAtUtc: null,
        permissionVersion: 7,
      },
      {
        sessionId: 'session-revoked',
        userId: 'user-admin',
        issuedAtUtc: '2026-05-19T06:00:00Z',
        expiresAtUtc: '2026-05-19T14:00:00Z',
        revokedAtUtc: '2026-05-19T10:00:00Z',
        permissionVersion: 6,
      },
    ]),
    sessionsError: computed(() => undefined),
    sessionsPending: shallowRef(false),
    totalCount: computed(() => iamState.totalCount.value),
  }),
}))

function mountPage() {
  return mount(SessionsPage, {
    global: {
      stubs: {
        DefaultLayout: { template: '<main><slot /></main>' },
      },
    },
  })
}

describe('IAM sessions page', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    document.body.innerHTML = ''
    iamState.filters.filterRevoked = undefined
    iamState.filters.filterSearch = undefined
    iamState.filters.pageIndex = 3
    iamState.filters.pageSize = 20
    iamState.revokeSession.mockResolvedValue(undefined)
    iamState.refreshSessions.mockResolvedValue(undefined)
    iamState.revokeSessionPending.value = false
    iamState.totalCount.value = 1
    permissionState.canRevoke.value = true
  })

  it('renders active sessions with FE-2 blocks and no legacy color variables', async () => {
    const wrapper = mountPage()
    await flushPromises()

    expect(wrapper.text()).toContain('会话')
    expect(wrapper.text()).toContain('session-1')
    expect(wrapper.text()).toContain('吊销')
    expect(wrapper.find('[style*="--legacy-color"]').exists()).toBe(false)
  })

  it('renders active session state with the success-tone StatusBadge', async () => {
    const wrapper = mountPage()
    await flushPromises()

    const activeBadge = wrapper.findAll('[aria-label="状态：活跃"]')[0]
    expect(activeBadge).toBeTruthy()
    expect(activeBadge.text()).toBe('活跃')
    expect(activeBadge.classes()).toContain('text-success-strong')
  })

  it('maps session status filters to revoked filters', async () => {
    const wrapper = mountPage()
    await flushPromises()

    wrapper.findComponent(Select).vm.$emit('update:modelValue', 'active')
    await flushPromises()

    expect(iamState.filters.filterRevoked).toBe(false)
    expect(iamState.filters.pageIndex).toBe(1)

    iamState.filters.pageIndex = 3
    wrapper.findComponent(Select).vm.$emit('update:modelValue', 'revoked')
    await flushPromises()

    expect(iamState.filters.filterRevoked).toBe(true)
    expect(iamState.filters.pageIndex).toBe(1)

    iamState.filters.pageIndex = 3
    wrapper.findComponent(Select).vm.$emit('update:modelValue', 'all')
    await flushPromises()

    expect(iamState.filters.filterRevoked).toBeUndefined()
    expect(iamState.filters.pageIndex).toBe(1)
  })

  it('disables revoke for the current session', async () => {
    const wrapper = mountPage()
    await flushPromises()

    expect(
      wrapper.get('button[aria-label="吊销会话 session-current"]').attributes('disabled'),
    ).toBeDefined()
  })

  it('revokes the selected session and refreshes sessions after confirmation', async () => {
    const wrapper = mountPage()
    await flushPromises()

    await wrapper.get('button[aria-label="吊销会话 session-1"]').trigger('click')
    await flushPromises()

    const confirmButton = [...document.body.querySelectorAll('button')].find(
      (button) => button.textContent?.trim() === '吊销会话',
    )
    confirmButton?.click()
    await flushPromises()

    expect(iamState.revokeSession).toHaveBeenCalledWith({
      path: { sessionId: 'session-1' },
    })
    expect(iamState.refreshSessions).toHaveBeenCalled()
  })

  it('disables session revoke without revoke permission', async () => {
    permissionState.canRevoke.value = false
    const wrapper = mountPage()
    await flushPromises()

    expect(
      wrapper.get('button[aria-label="吊销会话 session-1"]').attributes('disabled'),
    ).toBeDefined()
  })

  it('disables revoke for revoked sessions', async () => {
    const wrapper = mountPage()
    await flushPromises()

    expect(
      wrapper.get('button[aria-label="吊销会话 session-revoked"]').attributes('disabled'),
    ).toBeDefined()
  })

  it('renders the server pagination summary when sessions exceed one page', async () => {
    iamState.totalCount.value = 45
    const wrapper = mountPage()
    await flushPromises()

    expect(wrapper.text()).toContain('显示 1-20 / 45 条')
  })
})
