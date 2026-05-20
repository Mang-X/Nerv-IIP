import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import IamListToolbar from '@/components/iam/IamListToolbar.vue'
import UserCreateDialog from '@/components/iam/UserCreateDialog.vue'
import UserEditDialog from '@/components/iam/UserEditDialog.vue'
import UserResetPasswordDialog from '@/components/iam/UserResetPasswordDialog.vue'
import UsersTable from '@/components/iam/UsersTable.vue'
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

const dialogStubs = {
  Dialog: {
    template: '<div><slot /></div>',
  },
  DialogContent: {
    template: '<div><slot /></div>',
  },
  DialogDescription: {
    template: '<p><slot /></p>',
  },
  DialogFooter: {
    template: '<footer><slot /></footer>',
  },
  DialogHeader: {
    template: '<header><slot /></header>',
  },
  DialogTitle: {
    template: '<h2><slot /></h2>',
  },
}

describe('IAM users page', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    iamState.createUser.mockResolvedValue(undefined)
    iamState.disableUser.mockResolvedValue(undefined)
    iamState.refreshUsers.mockResolvedValue(undefined)
    iamState.resetUserPassword.mockResolvedValue(undefined)
    iamState.updateUser.mockResolvedValue(undefined)
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

  it('labels search and row actions for assistive technology', async () => {
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

    expect(wrapper.get('input[type="search"]').attributes('aria-label')).toBe('Search users')
    expect(wrapper.find('button[aria-label="Open actions for admin"]').exists()).toBe(true)
  })

  it('refreshes users after resetting a password', async () => {
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

    wrapper.findComponent(UsersTable).vm.$emit('resetPassword', {
      userId: 'user-admin',
      loginName: 'admin',
      email: 'admin@nerv-iip.local',
      enabled: true,
    })
    wrapper.findComponent(UserResetPasswordDialog).vm.$emit('submit', {
      newPassword: 'new-password',
    })
    await flushPromises()

    expect(iamState.resetUserPassword).toHaveBeenCalledWith({
      body: { newPassword: 'new-password' },
      path: { userId: 'user-admin' },
    })
    expect(iamState.refreshUsers).toHaveBeenCalled()
  })
})

describe('IAM users form dialogs', () => {
  it('renders create validation alerts only after submit', async () => {
    const wrapper = mount(UserCreateDialog, {
      props: {
        open: true,
      },
      global: {
        stubs: {
          ...dialogStubs,
        },
      },
    })

    await flushPromises()

    expect(wrapper.findAll('[role="alert"]')).toHaveLength(0)

    await wrapper.get('form').trigger('submit')

    expect(wrapper.findAll('[role="alert"]')).toHaveLength(3)
    expect(wrapper.text()).toContain('Login name is required.')
    expect(wrapper.text()).toContain('Email is required.')
    expect(wrapper.text()).toContain('Password is required.')
  })

  it('renders edit validation alerts only after submit', async () => {
    const wrapper = mount(UserEditDialog, {
      props: {
        open: true,
        user: {
          userId: 'user-admin',
          loginName: '',
          email: '',
          enabled: true,
        },
      },
      global: {
        stubs: {
          ...dialogStubs,
        },
      },
    })

    await flushPromises()

    expect(wrapper.findAll('[role="alert"]')).toHaveLength(0)

    await wrapper.get('form').trigger('submit')

    expect(wrapper.findAll('[role="alert"]')).toHaveLength(2)
    expect(wrapper.text()).toContain('Login name is required.')
    expect(wrapper.text()).toContain('Email is required.')
  })

  it('renders reset password validation alerts only after submit', async () => {
    const wrapper = mount(UserResetPasswordDialog, {
      props: {
        open: true,
        user: {
          userId: 'user-admin',
          loginName: 'admin',
          email: 'admin@nerv-iip.local',
          enabled: true,
        },
      },
      global: {
        stubs: {
          ...dialogStubs,
        },
      },
    })

    await flushPromises()

    expect(wrapper.findAll('[role="alert"]')).toHaveLength(0)

    await wrapper.get('form').trigger('submit')

    expect(wrapper.findAll('[role="alert"]')).toHaveLength(1)
    expect(wrapper.text()).toContain('New password is required.')
  })
})
