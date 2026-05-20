import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import UsersPage from './index.vue'

const iamState = vi.hoisted(() => ({
  createUser: vi.fn(),
  disableUser: vi.fn(),
  refreshUsers: vi.fn(),
  resetUserPassword: vi.fn(),
  updateUser: vi.fn(),
}))

vi.mock('@/composables/useIamAdmin', () => ({
  useIamUsers: () => ({
    createUser: iamState.createUser,
    createUserError: computed(() => undefined),
    createUserPending: shallowRef(false),
    disableUser: iamState.disableUser,
    disableUserError: computed(() => undefined),
    disableUserPending: shallowRef(false),
    filters: reactive({
      pageIndex: 1,
      pageSize: 20,
    }),
    refreshUsers: iamState.refreshUsers,
    resetUserPassword: iamState.resetUserPassword,
    resetUserPasswordError: computed(() => undefined),
    resetUserPasswordPending: shallowRef(false),
    totalCount: computed(() => 1),
    updateUser: iamState.updateUser,
    updateUserError: computed(() => undefined),
    updateUserPending: shallowRef(false),
    users: computed(() => [
      {
        userId: 'user-admin',
        loginName: 'admin',
        email: 'admin@nerv-iip.local',
        enabled: true,
      },
    ]),
    usersError: computed(() => undefined),
    usersPending: shallowRef(false),
  }),
}))

describe('IAM users page', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('renders the users list without legacy color variables', async () => {
    const wrapper = mount(UsersPage, {
      global: {
        stubs: {
          DefaultLayout: {
            template: '<main><slot /></main>',
          },
        },
      },
    })

    await flushPromises()

    expect(wrapper.get('h1').text()).toBe('Users')
    expect(wrapper.text()).toContain('admin@nerv-iip.local')
    expect(wrapper.text()).toContain('Create user')
    expect(wrapper.find('[style*="--legacy-color"]').exists()).toBe(false)
  })
})
