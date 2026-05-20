import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import RolePermissionEditor from '@/components/iam/RolePermissionEditor.vue'
import RolesTable from '@/components/iam/RolesTable.vue'
import RolesPage from './index.vue'

const iamState = vi.hoisted(() => ({
  createRole: vi.fn(),
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
    filters: reactive({
      pageIndex: 1,
      pageSize: 20,
    }),
    permissions: computed(() => [
      {
        code: 'iam.users.read',
        domain: 'IAM',
        description: 'Read IAM users',
        seeded: true,
      },
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

describe('IAM roles page', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    document.body.innerHTML = ''
    iamState.createRole.mockResolvedValue(undefined)
    iamState.refreshRoles.mockResolvedValue(undefined)
    iamState.totalCount.value = 1
    iamState.updateRolePermissions.mockResolvedValue(undefined)
    permissionState.canManage.value = true
  })

  it('renders roles and permissions without legacy color variables', async () => {
    const wrapper = mount(RolesPage, {
      global: {
        stubs: {
          DefaultLayout: {
            template: '<main><slot /></main>',
          },
        },
      },
    })

    await flushPromises()

    expect(wrapper.get('h1').text()).toBe('Roles')
    expect(wrapper.text()).toContain('Platform Administrator')
    expect(wrapper.text()).toContain('iam.users.read')
    expect(wrapper.text()).toContain('Create role')
    expect(wrapper.find('[style*="--legacy-color"]').exists()).toBe(false)
  })

  it('renders exact administrator role warning copy when editing platform admin permissions', async () => {
    const wrapper = mount(RolesPage, {
      global: {
        stubs: {
          DefaultLayout: {
            template: '<main><slot /></main>',
          },
        },
      },
    })

    await flushPromises()

    wrapper.findComponent(RolesTable).vm.$emit('editPermissions', {
      roleId: 'role-platform-admin',
      roleName: 'Platform Administrator',
      permissionCodes: ['iam.roles.update', 'iam.users.read'],
    })
    await flushPromises()

    const alertTitles = [...document.body.querySelectorAll('[data-slot="alert-title"]')].map(
      (title) => title.textContent?.trim(),
    )

    expect(alertTitles).toContain('Administrator role')
    expect(document.body.textContent).toContain(
      'Removing IAM management permissions from this role can block future role edits.',
    )
  })

  it('disables role mutation actions without manage permission', async () => {
    permissionState.canManage.value = false
    const wrapper = mount(RolesPage, {
      global: {
        stubs: {
          DefaultLayout: {
            template: '<main><slot /></main>',
          },
        },
      },
    })

    await flushPromises()

    expect(wrapper.get('button[type="button"]').attributes('disabled')).toBeDefined()
    expect(
      wrapper
        .get('button[aria-label="Open actions for Platform Administrator"]')
        .attributes('disabled'),
    ).toBeDefined()
  })

  it('renders pagination when roles exceed one page', async () => {
    iamState.totalCount.value = 45
    const wrapper = mount(RolesPage, {
      global: {
        stubs: {
          DefaultLayout: {
            template: '<main><slot /></main>',
          },
        },
      },
    })

    await flushPromises()

    expect(wrapper.text()).toContain('Showing 1-20 of 45')
  })

  it('keeps create role form state when createRole fails', async () => {
    iamState.createRole.mockRejectedValueOnce(new Error('Create failed'))

    const wrapper = mount(RolesPage, {
      global: {
        stubs: {
          DefaultLayout: {
            template: '<main><slot /></main>',
          },
        },
      },
    })

    await flushPromises()
    await wrapper.get('button').trigger('click')
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
    expect(document.body.textContent).toContain('Create role')
    expect(document.body.querySelector<HTMLInputElement>('#iam-create-role-name')?.value).toBe(
      'Operations Auditor',
    )
    expect(document.body.textContent).toContain('1 selected')
  })

  it('bounds permission dialogs and editor lists with scrollable containers', async () => {
    const wrapper = mount(RolesPage, {
      global: {
        stubs: {
          DefaultLayout: {
            template: '<main><slot /></main>',
          },
        },
      },
    })

    await flushPromises()
    await wrapper.get('button').trigger('click')
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

    wrapper.findComponent(RolesTable).vm.$emit('editPermissions', {
      roleId: 'role-platform-admin',
      roleName: 'Platform Administrator',
      permissionCodes: ['iam.roles.update', 'iam.users.read'],
    })
    await flushPromises()

    expect(
      document.body.querySelector('[data-testid="role-edit-dialog-content"]')?.className,
    ).toContain('overflow-y-auto')
    expect(
      document.body.querySelector('[data-testid="role-edit-dialog-content"]')?.className,
    ).toContain('max-h-')
  })
})
