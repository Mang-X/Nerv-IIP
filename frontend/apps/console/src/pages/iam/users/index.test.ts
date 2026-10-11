import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import UserCreateDialog from '@/components/iam/UserCreateDialog.vue'
import UserEditDialog from '@/components/iam/UserEditDialog.vue'
import UserResetPasswordDialog from '@/components/iam/UserResetPasswordDialog.vue'
import UserRolesDialog from '@/components/iam/UserRolesDialog.vue'
import UsersPage from './index.vue'

const iamState = vi.hoisted(() => ({
  createUser: vi.fn(),
  disableUser: vi.fn(),
  enableUser: vi.fn(),
  rejection: undefined as { message: string } | undefined,
  filters: { pageIndex: 1, pageSize: 20 } as { pageIndex: number; pageSize: number },
  membershipError: undefined as { message: string } | undefined,
  membershipPending: false,
  membershipRoleIds: [] as string[],
  refreshUsers: vi.fn(),
  replaceUserMembership: vi.fn(),
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

const roleOptions = [
  { roleId: 'role-erp-sales', roleName: '销售', permissionCodes: ['erp.sales.read'] },
  { roleId: 'role-platform-admin', roleName: '平台管理员', permissionCodes: ['iam.users.manage'] },
]

vi.mock('@/composables/useIamAdmin', () => ({
  useIamRoleOptions: () => ({
    roleOptions: computed(() => roleOptions),
    roleOptionsError: computed(() => undefined),
    roleOptionsPending: shallowRef(false),
  }),
  useIamUserMembership: (userId: () => string | undefined) => ({
    membership: computed(() =>
      userId() ? { userId: userId(), roleIds: iamState.membershipRoleIds } : undefined,
    ),
    membershipError: computed(() => iamState.membershipError),
    membershipPending: shallowRef(iamState.membershipPending),
  }),
  useIamUsers: () => ({
    createUser: iamState.createUser,
    createUserError: computed(() => iamState.rejection),
    createUserPending: shallowRef(false),
    disableUser: iamState.disableUser,
    disableUserError: computed(() => undefined),
    disableUserPending: shallowRef(false),
    enableUser: iamState.enableUser,
    enableUserError: computed(() => undefined),
    enableUserPending: shallowRef(false),
    filters: reactive(iamState.filters),
    refreshUsers: iamState.refreshUsers,
    replaceUserMembership: iamState.replaceUserMembership,
    replaceUserMembershipError: computed(() => undefined),
    replaceUserMembershipPending: shallowRef(false),
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
  NvDialog: { template: '<div><slot /></div>' },
  NvDialogContent: { template: '<div><slot /></div>' },
  NvDialogDescription: { template: '<p><slot /></p>' },
  NvDialogFooter: { template: '<footer><slot /></footer>' },
  NvDialogHeader: { template: '<header><slot /></header>' },
  NvDialogTitle: { template: '<h2><slot /></h2>' },
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
    iamState.rejection = undefined
    document.body.innerHTML = ''
    iamState.createUser.mockResolvedValue({ data: { userId: 'user-created' }, success: true })
    iamState.replaceUserMembership.mockResolvedValue(undefined)
    iamState.membershipError = undefined
    iamState.membershipPending = false
    iamState.membershipRoleIds = ['role-platform-admin']
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

  it('renders the Chinese duplicate-name rejection received from IAM', async () => {
    iamState.rejection = { message: '登录名「admin」已被使用。' }
    const wrapper = mountPage()
    await flushPromises()
    expect(wrapper.get('[role="alert"]').text()).toBe('登录名「admin」已被使用。')
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

  it('does not show internal user identities even when the login name is missing', async () => {
    iamState.users[0]!.loginName = ''
    const wrapper = mountPage()
    await flushPromises()
    expect(wrapper.text()).not.toContain('user-admin')
    expect(wrapper.text()).not.toContain('用户 ID')
    expect(wrapper.find('button[aria-label="分配角色 用户"]').exists()).toBe(true)
    await wrapper.get('button[aria-label="分配角色 用户"]').trigger('click')
    await flushPromises()
    expect(document.body.textContent).not.toContain('user-admin')
  })

  it('creates a user and assigns the chosen roles in the current organization environment', async () => {
    const wrapper = mountPage()
    await flushPromises()

    const user = {
      email: 'operator@nerv-iip.local',
      loginName: 'operator',
      password: 'Operator123!',
    }
    wrapper.findComponent(UserCreateDialog).vm.$emit('submit', {
      roleIds: ['role-erp-sales'],
      user,
    })
    await flushPromises()

    expect(iamState.createUser).toHaveBeenCalledWith({ body: user })
    expect(iamState.replaceUserMembership).toHaveBeenCalledWith({
      body: { roleIds: ['role-erp-sales'] },
      path: { userId: 'user-created' },
    })
    expect(iamState.createUser.mock.invocationCallOrder[0]).toBeLessThan(
      iamState.replaceUserMembership.mock.invocationCallOrder[0]!,
    )
  })

  it('assigns roles to an existing user starting from the current membership', async () => {
    const wrapper = mountPage()
    await flushPromises()

    await wrapper.get('button[aria-label="分配角色 admin"]').trigger('click')
    await flushPromises()

    const dialog = wrapper.findComponent(UserRolesDialog)
    expect(dialog.props('currentRoleIds')).toEqual(['role-platform-admin'])
    dialog.vm.$emit('submit', ['role-erp-sales', 'role-platform-admin'])
    await flushPromises()

    expect(iamState.replaceUserMembership).toHaveBeenCalledWith({
      body: { roleIds: ['role-erp-sales', 'role-platform-admin'] },
      path: { userId: 'user-admin' },
    })
  })

  it.each([
    ['loading', { membershipPending: true }],
    ['failed to load', { membershipError: { message: 'iam-unavailable' } }],
  ])(
    'blocks saving roles while the current membership is %s so the user is not removed by an empty selection',
    async (_state, overrides) => {
      Object.assign(iamState, overrides)
      const wrapper = mountPage()
      await flushPromises()

      await wrapper.get('button[aria-label="分配角色 admin"]').trigger('click')
      await flushPromises()

      expect(wrapper.findComponent(UserRolesDialog).props('disabled')).toBe(true)
    },
  )

  it('allows saving roles once the current membership has loaded', async () => {
    const wrapper = mountPage()
    await flushPromises()

    await wrapper.get('button[aria-label="分配角色 admin"]').trigger('click')
    await flushPromises()

    expect(wrapper.findComponent(UserRolesDialog).props('disabled')).toBe(false)
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
      props: { open: true, roles: roleOptions },
      global: { stubs: { ...dialogStubs } },
    })

    await flushPromises()

    expect(wrapper.findAll('[role="alert"]')).toHaveLength(0)

    await wrapper.get('form').trigger('submit')

    expect(wrapper.findAll('[role="alert"]')).toHaveLength(4)
    expect(wrapper.text()).toContain('请输入登录名。')
    expect(wrapper.text()).toContain('请输入邮箱。')
    expect(wrapper.text()).toContain('请输入密码。')
    expect(wrapper.text()).toContain('请至少选择一个角色。')
    expect(wrapper.emitted('submit')).toBeUndefined()
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

  it('does not create a user until at least one role is chosen', async () => {
    const wrapper = mount(UserCreateDialog, {
      props: { open: true, roles: roleOptions },
      global: { stubs: { ...dialogStubs } },
    })

    await wrapper.get('#iam-create-login-name').setValue('new-user')
    await wrapper.get('#iam-create-email').setValue('new-user@nerv-iip.local')
    await wrapper.get('#iam-create-password').setValue('Password123!')
    await wrapper.get('form').trigger('submit')

    expect(wrapper.emitted('submit')).toBeUndefined()
    expect(wrapper.findAll('[role="alert"]')).toHaveLength(1)
    expect(wrapper.text()).toContain('请至少选择一个角色。')
  })

  it('emits account expiry and chosen roles when creating a user', async () => {
    const wrapper = mount(UserCreateDialog, {
      props: { open: true, roles: roleOptions },
      global: { stubs: { ...dialogStubs } },
    })

    await wrapper.get('#iam-create-login-name').setValue('new-user')
    await wrapper.get('#iam-create-email').setValue('new-user@nerv-iip.local')
    await wrapper.get('#iam-create-password').setValue('Password123!')
    await wrapper.get('#iam-create-account-expires').setValue('2026-08-31')
    await wrapper.get('#iam-create-role-role-erp-sales').trigger('click')
    await wrapper.get('form').trigger('submit')

    expect(wrapper.emitted('submit')?.[0]).toEqual([
      {
        roleIds: ['role-erp-sales'],
        user: {
          accountExpiresAtUtc: '2026-08-31T23:59:59Z',
          email: 'new-user@nerv-iip.local',
          loginName: 'new-user',
          password: 'Password123!',
        },
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
