import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import RolesPage from './index.vue'

const iamState = vi.hoisted(() => ({
  createRole: vi.fn(),
  refreshRoles: vi.fn(),
  updateRolePermissions: vi.fn(),
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
    totalCount: computed(() => 1),
    updateRolePermissions: iamState.updateRolePermissions,
    updateRolePermissionsError: computed(() => undefined),
    updateRolePermissionsPending: shallowRef(false),
  }),
}))

describe('IAM roles page', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    iamState.createRole.mockResolvedValue(undefined)
    iamState.refreshRoles.mockResolvedValue(undefined)
    iamState.updateRolePermissions.mockResolvedValue(undefined)
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
})
