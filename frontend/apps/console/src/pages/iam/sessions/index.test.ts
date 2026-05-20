import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import SessionsPage from './index.vue'

vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({
    sessionId: 'session-current',
  }),
}))

vi.mock('@/composables/useIamAdmin', () => ({
  useIamSessions: () => ({
    filters: reactive({
      pageIndex: 1,
      pageSize: 20,
    }),
    refreshSessions: vi.fn(),
    revokeSession: vi.fn(),
    revokeSessionError: computed(() => undefined),
    revokeSessionPending: shallowRef(false),
    sessions: computed(() => [
      {
        sessionId: 'session-1',
        userId: 'user-admin',
        issuedAtUtc: '2026-05-20T06:00:00Z',
        expiresAtUtc: '2026-05-20T14:00:00Z',
        revokedAtUtc: null,
        permissionVersion: 7,
      },
    ]),
    sessionsError: computed(() => undefined),
    sessionsPending: shallowRef(false),
    totalCount: computed(() => 1),
  }),
}))

describe('IAM sessions page', () => {
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
})
