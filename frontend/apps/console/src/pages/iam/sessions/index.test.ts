import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import IamListToolbar from '@/components/iam/IamListToolbar.vue'
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
}))

vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({
    sessionId: 'session-current',
  }),
}))

vi.mock('@/composables/useIamAdmin', () => ({
  useIamSessions: () => ({
    filters: iamState.filters,
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
    totalCount: computed(() => 1),
  }),
}))

describe('IAM sessions page', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    document.body.innerHTML = ''
    iamState.filters.filterRevoked = undefined
    iamState.filters.filterSearch = undefined
    iamState.filters.pageIndex = 3
    iamState.revokeSession.mockResolvedValue(undefined)
    iamState.refreshSessions.mockResolvedValue(undefined)
    iamState.revokeSessionPending.value = false
  })

  it('renders active sessions without legacy color variables', async () => {
    const wrapper = mount(SessionsPage, {
      global: {
        stubs: {
          DefaultLayout: {
            template: '<main><slot /></main>',
          },
        },
      },
    })

    await flushPromises()

    expect(wrapper.get('h1').text()).toBe('Sessions')
    expect(wrapper.text()).toContain('session-1')
    expect(wrapper.text()).toContain('Revoke')
    expect(wrapper.find('[style*="--legacy-color"]').exists()).toBe(false)
  })

  it('maps session status filters to revoked filters', async () => {
    const wrapper = mount(SessionsPage, {
      global: {
        stubs: {
          DefaultLayout: {
            template: '<main><slot /></main>',
          },
        },
      },
    })

    await flushPromises()

    wrapper.findComponent(IamListToolbar).vm.$emit('update:status', 'active')
    await flushPromises()

    expect(iamState.filters.filterRevoked).toBe(false)
    expect(iamState.filters.pageIndex).toBe(1)

    iamState.filters.pageIndex = 3
    wrapper.findComponent(IamListToolbar).vm.$emit('update:status', 'revoked')
    await flushPromises()

    expect(iamState.filters.filterRevoked).toBe(true)
    expect(iamState.filters.pageIndex).toBe(1)

    iamState.filters.pageIndex = 3
    wrapper.findComponent(IamListToolbar).vm.$emit('update:status', '')
    await flushPromises()

    expect(iamState.filters.filterRevoked).toBeUndefined()
    expect(iamState.filters.pageIndex).toBe(1)
  })

  it('warns when revoking the current session', async () => {
    const wrapper = mount(SessionsPage, {
      global: {
        stubs: {
          DefaultLayout: {
            template: '<main><slot /></main>',
          },
        },
      },
    })

    await flushPromises()
    await wrapper.get('button[aria-label="Revoke session session-current"]').trigger('click')
    await flushPromises()

    expect(document.body.textContent).toContain(
      'This is your current session and you may be signed out.',
    )
  })

  it('revokes the selected session and refreshes sessions after confirmation', async () => {
    const wrapper = mount(SessionsPage, {
      global: {
        stubs: {
          DefaultLayout: {
            template: '<main><slot /></main>',
          },
        },
      },
    })

    await flushPromises()
    await wrapper.get('button[aria-label="Revoke session session-1"]').trigger('click')
    await flushPromises()

    const confirmButton = [...document.body.querySelectorAll('button')]
      .find(button => button.textContent?.trim() === 'Revoke session')
    confirmButton?.click()
    await flushPromises()

    expect(iamState.revokeSession).toHaveBeenCalledWith({
      path: { sessionId: 'session-1' },
    })
    expect(iamState.refreshSessions).toHaveBeenCalled()
    expect(document.body.textContent).not.toContain(
      'Revoking session-1 ends the refresh path for this session.',
    )
  })

  it('disables revoke for revoked sessions', async () => {
    const wrapper = mount(SessionsPage, {
      global: {
        stubs: {
          DefaultLayout: {
            template: '<main><slot /></main>',
          },
        },
      },
    })

    await flushPromises()

    expect(wrapper.get('button[aria-label="Revoke session session-revoked"]').attributes('disabled'))
      .toBeDefined()
  })
})
