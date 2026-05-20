<script setup lang="ts">
import type { ConsoleIamRoleResponse } from '@nerv-iip/api-client'
import IamListToolbar from '@/components/iam/IamListToolbar.vue'
import IamPageHeader from '@/components/iam/IamPageHeader.vue'
import RoleCreateDialog from '@/components/iam/RoleCreateDialog.vue'
import RolePermissionEditor from '@/components/iam/RolePermissionEditor.vue'
import RolesTable from '@/components/iam/RolesTable.vue'
import { useIamRoles } from '@/composables/useIamAdmin'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import {
  Alert,
  AlertDescription,
  AlertTitle,
  Button,
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  toast,
} from '@nerv-iip/ui'
import { computed, shallowRef, watch } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: 'IAM Roles',
  },
})

const roles = useIamRoles()

const search = shallowRef('')
const createDialogOpen = shallowRef(false)
const editOpen = shallowRef(false)
const selectedRole = shallowRef<ConsoleIamRoleResponse>()
const selectedPermissionCodes = shallowRef<string[]>([])

const pageError = computed(() =>
  roles.rolesError.value
  ?? roles.permissionsError.value
  ?? roles.createRoleError.value
  ?? roles.updateRolePermissionsError.value,
)

const tablePending = computed(() =>
  roles.rolesPending.value
  || roles.permissionsPending.value
  || roles.createRolePending.value
  || roles.updateRolePermissionsPending.value,
)

watch(search, (nextSearch) => {
  roles.filters.filterSearch = nextSearch.trim() || undefined
  roles.filters.pageIndex = 1
}, { immediate: true })

function openCreateDialog() {
  createDialogOpen.value = true
}

function openEditPermissions(role: ConsoleIamRoleResponse) {
  selectedRole.value = role
  selectedPermissionCodes.value = [...(role.permissionCodes ?? [])].sort()
  editOpen.value = true
}

async function handleCreate(payload: { roleName: string, permissionCodes: string[] }) {
  await roles.createRole({
    body: payload,
  })
  createDialogOpen.value = false
  await roles.refreshRoles()
  toast.success('Role created')
}

async function savePermissions() {
  const roleId = selectedRole.value?.roleId
  if (!roleId) {
    return
  }

  await roles.updateRolePermissions({
    body: {
      permissionCodes: [...selectedPermissionCodes.value].sort(),
    },
    path: { roleId },
  })
  editOpen.value = false
  await roles.refreshRoles()
  toast.success('Role permissions updated')
}
</script>

<template>
  <DefaultLayout>
    <section class="grid gap-6">
      <IamPageHeader
        title="Roles"
        description="Manage IAM roles and the permission codes assigned to each role."
      />

      <IamListToolbar
        v-model:search="search"
        action-label="Create role"
        search-label="Search roles"
        search-placeholder="Search roles"
        @action="openCreateDialog"
      />

      <Alert v-if="pageError" variant="destructive">
        <AlertTitle>Unable to complete role request</AlertTitle>
        <AlertDescription>{{ pageError.message }}</AlertDescription>
      </Alert>

      <RolesTable
        :pending="tablePending"
        :roles="roles.roles.value"
        @edit-permissions="openEditPermissions"
      />

      <RoleCreateDialog
        v-model:open="createDialogOpen"
        :pending="roles.createRolePending.value"
        :permissions="roles.permissions.value"
        @submit="handleCreate"
      />

      <Dialog v-model:open="editOpen">
        <DialogContent class="sm:max-w-3xl">
          <DialogHeader>
            <DialogTitle>Edit role permissions</DialogTitle>
            <DialogDescription>
              Update the permission codes assigned to {{ selectedRole?.roleName || 'this role' }}.
            </DialogDescription>
          </DialogHeader>

          <form class="grid gap-4" @submit.prevent="savePermissions">
            <Alert v-if="selectedRole?.roleId === 'role-platform-admin'">
              <AlertTitle>Administrator role warning</AlertTitle>
              <AlertDescription>
                Removing IAM management permissions from this role can block future role edits.
              </AlertDescription>
            </Alert>

            <RolePermissionEditor
              v-model="selectedPermissionCodes"
              :permissions="roles.permissions.value"
            />

            <DialogFooter show-close-button>
              <Button type="submit" :disabled="roles.updateRolePermissionsPending.value">
                Save permissions
              </Button>
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>
    </section>
  </DefaultLayout>
</template>
