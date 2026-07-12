import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import UserCreateDialog from '@/components/iam/UserCreateDialog.vue'
import UserEditDialog from '@/components/iam/UserEditDialog.vue'
import UserResetPasswordDialog from '@/components/iam/UserResetPasswordDialog.vue'
import UsersPage from './index.vue'

const iamState = vi.hoisted(() => ({
  createUser: vi.fn(),
  disableUser: vi.fn(),
  enableUser: vi.fn(),
  filters: { pageIndex: 1, pageSize: 20 } as { pageIndex: number; pageSize: number },
  refreshUsers: vi.fn(),
  resetUserPassword: vi.fn(),
  totalCount: { value: 1 },
  updateUser: vi.fn(),
  users: [] as Array<{
    userId: string
    loginName: string
    email: string
    enabled: boolean
    accountExpiresAtUtc: string | null
    passwordChangeRequired: boolean
    passwordExpiresAtUtc: string | null
    lockoutUntilUtc: string | null
  }>,
}))
const permissionState = vi.hoisted(() => ({
  canManage: { value: true },
}))

vi.mock('@/composables/usePermissions', () => ({
  useHasPermission: () => computed(() => permissionState.canManage.value),
}))

vi.mock('@/composables/useIamAdmin', () => ({
  useIamUsers: () => ({
    createUser: iamState.createUser,
    createUserError: computed(() => undefined),
    createUserPending: shallowRef(false),
    disableUser: iamState.disableUser,
    disableUserError: computed(() => undefined),
    disableUserPending: shallowRef(false),
    enableUser: iamState.enableUser,
    enableUserError: computed(() => undefined),
    enableUserPending: shallowRef(false),
    filters: reactive(iamState.filters),
    refreshUsers: iamState.refreshUsers,
    resetUserPassword: iamState.resetUserPassword,
    resetUserPasswordError: computed(() => undefined),
    resetUserPasswordPending: shallowRef(false),
    totalCount: computed(() => iamState.totalCount.value),
    updateUser: iamState.updateUser,
    updateUserError: computed(() => undefined),
    updateUserPending: shallowRef(false),
    users: computed(() => iamState.users),
    usersError: computed(() => undefined),
    usersPending: shallowRef(false),
  }),
}))

const dialogStubs = {
  Dialog: { template: '<div><slot /></div>' },
  DialogContent: { template: '<div><slot /></div>' },
  DialogDescription: { template: '<p><slot /></p>' },
  DialogFooter: { template: '<footer><slot /></footer>' },
  DialogHeader: { template: '<header><slot /></header>' },
  DialogTitle: { template: '<h2><slot /></h2>' },
}

function mountPage() {
  return mount(UsersPage, {
    global: {
      stubs: {
        DefaultLayout: { template: '<main><slot /></main>' },
      },
    },
  })
}

describe('IAM users page', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    document.body.innerHTML = ''
    iamState.createUser.mockResolvedValue(undefined)
    iamState.disableUser.mockResolvedValue(undefined)
    iamState.enableUser.mockResolvedValue(undefined)
    iamState.refreshUsers.mockResolvedValue(undefined)
    iamState.resetUserPassword.mockResolvedValue(undefined)
    iamState.filters.pageIndex = 1
    iamState.filters.pageSize = 20
    iamState.totalCount.value = 1
    iamState.updateUser.mockResolvedValue(undefined)
    iamState.users = [
      {
        userId: 'user-admin',
        loginName: 'admin',
        email: 'admin@nerv-iip.local',
        enabled: true,
        accountExpiresAtUtc: null,
        passwordChangeRequired: false,
        passwordExpiresAtUtc: null,
        lockoutUntilUtc: null,
      },
    ]
    permissionState.canManage.value = true
  })

  it('renders the users list with FE-2 blocks and no legacy color variables', async () => {
    const wrapper = mountPage()
    await flushPromises()

    expect(wrapper.text()).toContain('用户')
    expect(wrapper.text()).toContain('admin@nerv-iip.local')
    expect(wrapper.text()).toContain('新建用户')
    expect(wrapper.find('[style*="--legacy-color"]').exists()).toBe(false)
  })

  it('renders enabled status with the success-tone StatusBadge', async () => {
    const wrapper = mountPage()
    await flushPromises()

    const enabledBadge = wrapper.find('[aria-label="状态：启用"]')
    expect(enabledBadge.exists()).toBe(true)
    expect(enabledBadge.text()).toBe('启用')
    expect(enabledBadge.classes()).toContain('text-success-strong')
  })

  it('labels search and row actions for assistive technology', async () => {
    const wrapper = mountPage()
    await flushPromises()

    expect(wrapper.get('input[type="search"]').attributes('aria-label')).toBe('搜索用户')
    expect(wrapper.find('button[aria-label="编辑用户 admin"]').exists()).toBe(true)
    expect(wrapper.find('button[aria-label="重置密码 admin"]').exists()).toBe(true)
    expect(wrapper.find('button[aria-label="停用用户 admin"]').exists()).toBe(true)
  })

  it('disables user mutation actions without manage permission', async () => {
    permissionState.canManage.value = false
    const wrapper = mountPage()
    await flushPromises()

    const createButton = wrapper.findAll('button').find((button) => button.text() === '新建用户')
    expect(createButton?.attributes('disabled')).toBeDefined()
    expect(wrapper.get('button[aria-label="编辑用户 admin"]').attributes('disabled')).toBeDefined()
    expect(wrapper.get('button[aria-label="重置密码 admin"]').attributes('disabled')).toBeDefined()
    expect(wrapper.get('button[aria-label="停用用户 admin"]').attributes('disabled')).toBeDefined()
  })

  it('renders the server pagination summary and changes the server page', async () => {
    iamState.totalCount.value = 45
    const wrapper = mountPage()
    await flushPromises()

    expect(wrapper.text()).toContain('显示 1–20 / 45 条')

    wrapper.findComponent({ name: 'NvPagination' }).vm.$emit('update:page', 2)
    await flushPromises()

    expect(iamState.filters.pageIndex).toBe(2)
  })

  it('confirms before disabling a user', async () => {
    const wrapper = mountPage()
    await flushPromises()

    await wrapper.get('button[aria-label="停用用户 admin"]').trigger('click')
    await flushPromises()

    expect(document.body.textContent).toContain('确认停用用户 admin')
    expect(iamState.disableUser).not.toHaveBeenCalled()

    const confirmButton = [...document.body.querySelectorAll('button')].find(
      (button) => button.textContent?.trim() === '停用',
    )
    confirmButton?.click()
    await flushPromises()

    expect(iamState.disableUser).toHaveBeenCalledWith({ path: { userId: 'user-admin' } })
    expect(iamState.refreshUsers).toHaveBeenCalled()
  })

  it('enables a disabled user and refreshes the list', async () => {
    iamState.users = [
      {
        userId: 'user-disabled',
        loginName: 'disabled',
        email: 'disabled@nerv-iip.local',
        enabled: false,
        accountExpiresAtUtc: null,
        passwordChangeRequired: false,
        passwordExpiresAtUtc: null,
        lockoutUntilUtc: null,
      },
    ]
    const wrapper = mountPage()
    await flushPromises()

    await wrapper.get('button[aria-label="启用用户 disabled"]').trigger('click')
    await flushPromises()

    expect(iamState.enableUser).toHaveBeenCalledWith({ path: { userId: 'user-disabled' } })
    expect(iamState.refreshUsers).toHaveBeenCalled()
  })

  it('renders lifecycle and password policy fields', async () => {
    iamState.users = [
      {
        userId: 'user-policy',
        loginName: 'policy',
        email: 'policy@nerv-iip.local',
        enabled: true,
        accountExpiresAtUtc: '2026-08-31T23:59:59Z',
        passwordChangeRequired: true,
        passwordExpiresAtUtc: '2026-09-30T23:59:59Z',
        lockoutUntilUtc: null,
      },
    ]
    const wrapper = mountPage()
    await flushPromises()

    expect(wrapper.text()).toContain('2026-08-31')
    expect(wrapper.text()).toContain('需改密')
  })

  it('refreshes users after resetting a password', async () => {
    const wrapper = mountPage()
    await flushPromises()

    await wrapper.get('button[aria-label="重置密码 admin"]').trigger('click')
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
      props: { open: true },
      global: { stubs: { ...dialogStubs } },
    })

    await flushPromises()

    expect(wrapper.findAll('[role="alert"]')).toHaveLength(0)

    await wrapper.get('form').trigger('submit')

    expect(wrapper.findAll('[role="alert"]')).toHaveLength(3)
    expect(wrapper.text()).toContain('请输入登录名。')
    expect(wrapper.text()).toContain('请输入邮箱。')
    expect(wrapper.text()).toContain('请输入密码。')
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
          accountExpiresAtUtc: null,
          passwordChangeRequired: false,
          passwordExpiresAtUtc: null,
          lockoutUntilUtc: null,
        },
      },
      global: { stubs: { ...dialogStubs } },
    })

    await flushPromises()

    expect(wrapper.findAll('[role="alert"]')).toHaveLength(0)

    await wrapper.get('form').trigger('submit')

    expect(wrapper.findAll('[role="alert"]')).toHaveLength(2)
    expect(wrapper.text()).toContain('请输入登录名。')
    expect(wrapper.text()).toContain('请输入邮箱。')
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
          accountExpiresAtUtc: null,
          passwordChangeRequired: false,
          passwordExpiresAtUtc: null,
          lockoutUntilUtc: null,
        },
      },
      global: { stubs: { ...dialogStubs } },
    })

    await flushPromises()

    expect(wrapper.findAll('[role="alert"]')).toHaveLength(0)

    await wrapper.get('form').trigger('submit')

    expect(wrapper.findAll('[role="alert"]')).toHaveLength(1)
    expect(wrapper.text()).toContain('请输入新密码。')
  })

  it('emits account expiry when creating a user', async () => {
    const wrapper = mount(UserCreateDialog, {
      props: { open: true },
      global: { stubs: { ...dialogStubs } },
    })

    await wrapper.get('#iam-create-login-name').setValue('new-user')
    await wrapper.get('#iam-create-email').setValue('new-user@nerv-iip.local')
    await wrapper.get('#iam-create-password').setValue('Password123!')
    await wrapper.get('#iam-create-account-expires').setValue('2026-08-31')
    await wrapper.get('form').trigger('submit')

    expect(wrapper.emitted('submit')?.[0]).toEqual([
      {
        accountExpiresAtUtc: '2026-08-31T23:59:59Z',
        email: 'new-user@nerv-iip.local',
        loginName: 'new-user',
        password: 'Password123!',
      },
    ])
  })

  it('reflects and emits enabled state and account expiry when editing a user', async () => {
    const wrapper = mount(UserEditDialog, {
      props: {
        open: true,
        user: {
          userId: 'user-edit',
          loginName: 'edit-user',
          email: 'edit-user@nerv-iip.local',
          enabled: true,
          accountExpiresAtUtc: '2026-08-31T23:59:59Z',
          passwordChangeRequired: false,
          passwordExpiresAtUtc: null,
          lockoutUntilUtc: null,
        },
      },
      global: { stubs: { ...dialogStubs } },
    })

    const checkbox = wrapper.get('[role="checkbox"]')
    expect(checkbox.attributes('aria-checked')).toBe('true')
    await checkbox.trigger('click')
    await wrapper.get('#iam-edit-account-expires').setValue('2026-09-30')
    await wrapper.get('form').trigger('submit')

    expect(wrapper.emitted('submit')?.[0]).toEqual([
      {
        accountExpiresAtUtc: '2026-09-30T23:59:59Z',
        email: 'edit-user@nerv-iip.local',
        enabled: false,
        loginName: 'edit-user',
      },
    ])
  })
})
