import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import RolePermissionEditor from '@/components/iam/RolePermissionEditor.vue'
import RolesPage from './index.vue'

const iamState = vi.hoisted(() => ({
  createRole: vi.fn(),
  filters: { pageIndex: 1, pageSize: 20 } as { pageIndex: number; pageSize: number },
  refreshRoles: vi.fn(),
  totalCount: { value: 1 },
  updateRolePermissions: vi.fn(),
}))
const permissionState = vi.hoisted(() => ({
  canManage: { value: true },
}))

vi.mock('@/composables/usePermissions', () => ({
  useHasPermission: () => computed(() => permissionState.canManage.value),
}))

vi.mock('@/composables/useIamAdmin', () => ({
  useIamRoles: () => ({
    createRole: iamState.createRole,
    createRoleError: computed(() => undefined),
    createRolePending: shallowRef(false),
    filters: reactive(iamState.filters),
    permissions: computed(() => [
      { code: 'iam.users.read', domain: 'IAM', description: 'Read IAM users', seeded: true },
      {
        code: 'iam.roles.update',
        domain: 'IAM',
        description: 'Update IAM role permissions',
        seeded: true,
      },
    ]),
    permissionsError: computed(() => undefined),
    permissionsPending: shallowRef(false),
    refreshPermissions: vi.fn(),
    refreshRoles: iamState.refreshRoles,
    roles: computed(() => [
      {
        roleId: 'role-platform-admin',
        roleName: 'Platform Administrator',
        permissionCodes: ['iam.users.read', 'iam.roles.update'],
      },
    ]),
    rolesError: computed(() => undefined),
    rolesPending: shallowRef(false),
    totalCount: computed(() => iamState.totalCount.value),
    updateRolePermissions: iamState.updateRolePermissions,
    updateRolePermissionsError: computed(() => undefined),
    updateRolePermissionsPending: shallowRef(false),
  }),
}))

function mountPage() {
  return mount(RolesPage, {
    global: {
      stubs: {
        DefaultLayout: { template: '<main><slot /></main>' },
      },
    },
  })
}

describe('IAM roles page', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    document.body.innerHTML = ''
    iamState.createRole.mockResolvedValue(undefined)
    iamState.refreshRoles.mockResolvedValue(undefined)
    iamState.filters.pageIndex = 1
    iamState.filters.pageSize = 20
    iamState.totalCount.value = 1
    iamState.updateRolePermissions.mockResolvedValue(undefined)
    permissionState.canManage.value = true
  })

  it('renders roles and permissions with FE-2 blocks and no legacy color variables', async () => {
    const wrapper = mountPage()
    await flushPromises()

    expect(wrapper.text()).toContain('角色')
    expect(wrapper.text()).toContain('Platform Administrator')
    expect(wrapper.text()).toContain('iam.users.read')
    expect(wrapper.text()).toContain('新建角色')
    expect(wrapper.find('[style*="--legacy-color"]').exists()).toBe(false)
  })

  it('renders exact administrator role warning copy when editing platform admin permissions', async () => {
    const wrapper = mountPage()
    await flushPromises()

    await wrapper.get('button[aria-label="编辑权限 Platform Administrator"]').trigger('click')
    await flushPromises()

    const alertTitles = [...document.body.querySelectorAll('[data-slot="alert-title"]')].map(
      (title) => title.textContent?.trim(),
    )

    expect(alertTitles).toContain('管理员角色')
    expect(document.body.textContent).toContain(
      '从该角色移除 IAM 管理权限可能导致后续无法再编辑角色。',
    )
  })

  it('disables role mutation actions without manage permission', async () => {
    permissionState.canManage.value = false
    const wrapper = mountPage()
    await flushPromises()

    const createButton = wrapper.findAll('button').find((button) => button.text() === '新建角色')
    expect(createButton?.attributes('disabled')).toBeDefined()
    expect(
      wrapper.get('button[aria-label="编辑权限 Platform Administrator"]').attributes('disabled'),
    ).toBeDefined()
  })

  it('renders the server pagination summary when roles exceed one page', async () => {
    iamState.totalCount.value = 45
    const wrapper = mountPage()
    await flushPromises()

    expect(wrapper.text()).toContain('显示 1-20 / 45 条')
  })

  it('keeps create role form state when createRole fails', async () => {
    iamState.createRole.mockRejectedValueOnce(new Error('Create failed'))

    const wrapper = mountPage()
    await flushPromises()

    const createButton = wrapper.findAll('button').find((button) => button.text() === '新建角色')
    await createButton!.trigger('click')
    await flushPromises()

    const roleName = document.body.querySelector<HTMLInputElement>('#iam-create-role-name')
    roleName!.value = 'Operations Auditor'
    roleName!.dispatchEvent(new Event('input', { bubbles: true }))

    wrapper.findComponent(RolePermissionEditor).vm.$emit('update:modelValue', ['iam.users.read'])
    await flushPromises()

    document.body
      .querySelector('form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()

    expect(iamState.createRole).toHaveBeenCalledWith({
      body: {
        permissionCodes: ['iam.users.read'],
        roleName: 'Operations Auditor',
      },
    })
    expect(document.body.textContent).toContain('新建角色')
    expect(document.body.querySelector<HTMLInputElement>('#iam-create-role-name')?.value).toBe(
      'Operations Auditor',
    )
    expect(document.body.textContent).toContain('已选 1 项')
  })

  it('bounds permission dialogs and editor lists with scrollable containers', async () => {
    const wrapper = mountPage()
    await flushPromises()

    const createButton = wrapper.findAll('button').find((button) => button.text() === '新建角色')
    await createButton!.trigger('click')
    await flushPromises()

    expect(
      document.body.querySelector('[data-testid="role-create-dialog-content"]')?.className,
    ).toContain('overflow-y-auto')
    expect(
      document.body.querySelector('[data-testid="role-create-dialog-content"]')?.className,
    ).toContain('max-h-')
    expect(
      document.body.querySelector('[data-testid="role-permission-editor-scroll"]')?.className,
    ).toContain('overflow-y-auto')

    await wrapper.get('button[aria-label="编辑权限 Platform Administrator"]').trigger('click')
    await flushPromises()

    expect(
      document.body.querySelector('[data-testid="role-edit-dialog-content"]')?.className,
    ).toContain('overflow-y-auto')
    expect(
      document.body.querySelector('[data-testid="role-edit-dialog-content"]')?.className,
    ).toContain('max-h-')
  })
})
