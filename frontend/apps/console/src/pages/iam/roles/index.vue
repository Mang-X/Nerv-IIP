<script setup lang="ts">
import type { ConsoleIamRoleResponse } from '@nerv-iip/api-client'
import type { DataTableColumn } from '@nerv-iip/ui'
import PermissionCodeBadge from '@/components/iam/PermissionCodeBadge.vue'
import RoleCreateDialog from '@/components/iam/RoleCreateDialog.vue'
import RolePermissionEditor from '@/components/iam/RolePermissionEditor.vue'
import { useIamRoles } from '@/composables/useIamAdmin'
import { useHasPermission } from '@/composables/usePermissions'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import {
  Alert,
  AlertDescription,
  AlertTitle,
  Button,
  NvDataTable,
  NvDataTablePagination,
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  NvPageHeader,
  NvToolbar,
  toast,
} from '@nerv-iip/ui'
import { computed, shallowRef, watch } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: 'IAM 角色',
  },
})

const roles = useIamRoles()

const search = shallowRef('')
const canManageRoles = useHasPermission('iam.roles.manage')
const createDialogOpen = shallowRef(false)
const editOpen = shallowRef(false)
const selectedRole = shallowRef<ConsoleIamRoleResponse>()
const selectedPermissionCodes = shallowRef<string[]>([])

type RoleRow = ConsoleIamRoleResponse

const pageError = computed(
  () =>
    roles.rolesError.value ??
    roles.permissionsError.value ??
    roles.createRoleError.value ??
    roles.updateRolePermissionsError.value,
)

const tablePending = computed(
  () =>
    roles.rolesPending.value ||
    roles.permissionsPending.value ||
    roles.createRolePending.value ||
    roles.updateRolePermissionsPending.value,
)

const page = computed({
  get: () => roles.filters.pageIndex,
  set: (value: number) => {
    roles.filters.pageIndex = value
  },
})
const pageSize = computed({
  get: () => String(roles.filters.pageSize),
  set: (value: string) => {
    roles.filters.pageSize = Number(value) || 20
    roles.filters.pageIndex = 1
  },
})

const columns: DataTableColumn<RoleRow>[] = [
  {
    key: 'roleName',
    header: '角色名称',
    cellClass: 'font-medium',
    accessor: (r) => r.roleName || '—',
  },
  {
    key: 'roleId',
    header: '角色 ID',
    cellClass: 'font-mono text-xs text-muted-foreground',
    accessor: (r) => r.roleId || '—',
  },
  {
    key: 'permissionCount',
    header: '权限数',
    align: 'end',
    width: 'w-20',
    accessor: (r) => r.permissionCodes?.length ?? 0,
  },
  { key: 'keyPermissions', header: '关键权限' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-28' },
]

watch(
  search,
  (nextSearch) => {
    roles.filters.filterSearch = nextSearch.trim() || undefined
    roles.filters.pageIndex = 1
  },
  { immediate: true },
)

function roleLabel(role: ConsoleIamRoleResponse) {
  return role.roleName || role.roleId || '角色'
}

function keyPermissions(role: ConsoleIamRoleResponse) {
  return [...(role.permissionCodes ?? [])].sort().slice(0, 3)
}

function openCreateDialog() {
  createDialogOpen.value = true
}

function openEditPermissions(role: ConsoleIamRoleResponse) {
  selectedRole.value = role
  selectedPermissionCodes.value = [...(role.permissionCodes ?? [])].sort()
  editOpen.value = true
}

async function handleCreate(payload: { roleName: string; permissionCodes: string[] }) {
  try {
    await roles.createRole({ body: payload })
    createDialogOpen.value = false
    await roles.refreshRoles()
    toast.success('角色已创建')
  } catch {
    // 保留对话框，便于用户在不丢失输入的情况下重试。
  }
}

async function savePermissions() {
  const roleId = selectedRole.value?.roleId
  if (!roleId) return

  await roles.updateRolePermissions({
    body: { permissionCodes: [...selectedPermissionCodes.value].sort() },
    path: { roleId },
  })
  editOpen.value = false
  await roles.refreshRoles()
  toast.success('角色权限已更新')
}
</script>

<template>
  <DefaultLayout>
    <section class="grid gap-6">
      <NvPageHeader
        title="角色"
        :breadcrumbs="[{ label: '身份与访问' }]"
        :count="`${roles.totalCount.value} 个角色`"
      >
        <template #actions>
          <Button type="button" :disabled="!canManageRoles" @click="openCreateDialog"
            >新建角色</Button
          >
        </template>
      </NvPageHeader>

      <NvToolbar
        :search="search"
        search-label="搜索角色"
        search-placeholder="搜索角色"
        @update:search="search = $event"
      />

      <p v-if="pageError" class="text-sm text-destructive" role="alert">{{ pageError.message }}</p>

      <NvDataTable
        :pagination="false"
        :searchable="false"
        :column-settings="false"
        :columns="columns"
        :rows="roles.roles.value"
        row-key="roleId"
        :loading="tablePending"
        empty-message="没有符合当前条件的角色。"
      >
        <template #cell-keyPermissions="{ row }">
          <div class="flex flex-wrap gap-1.5">
            <PermissionCodeBadge v-for="code in keyPermissions(row)" :key="code" :code="code" />
            <span
              v-if="(row.permissionCodes?.length ?? 0) > keyPermissions(row).length"
              class="text-xs text-muted-foreground"
            >
              +{{ (row.permissionCodes?.length ?? 0) - keyPermissions(row).length }} 项
            </span>
            <span
              v-if="(row.permissionCodes?.length ?? 0) === 0"
              class="text-sm text-muted-foreground"
            >
              无权限
            </span>
          </div>
        </template>
        <template #cell-actions="{ row }">
          <div class="flex items-center justify-end">
            <Button
              size="sm"
              type="button"
              variant="outline"
              :aria-label="`编辑权限 ${roleLabel(row)}`"
              :disabled="!canManageRoles"
              @click="openEditPermissions(row)"
            >
              编辑权限
            </Button>
          </div>
        </template>
      </NvDataTable>

      <NvDataTablePagination
        v-model:page="page"
        v-model:page-size="pageSize"
        :total-items="roles.totalCount.value"
      />

      <RoleCreateDialog
        v-model:open="createDialogOpen"
        :pending="roles.createRolePending.value"
        :permissions="roles.permissions.value"
        @submit="handleCreate"
      />

      <Dialog v-model:open="editOpen">
        <DialogContent
          data-testid="role-edit-dialog-content"
          class="max-h-[min(90vh,48rem)] overflow-y-auto sm:max-w-3xl"
        >
          <DialogHeader>
            <DialogTitle>编辑角色权限</DialogTitle>
            <DialogDescription>
              更新分配给 {{ selectedRole?.roleName || '该角色' }} 的权限码。
            </DialogDescription>
          </DialogHeader>

          <form class="grid gap-4" @submit.prevent="savePermissions">
            <Alert v-if="selectedRole?.roleId === 'role-platform-admin'">
              <AlertTitle>管理员角色</AlertTitle>
              <AlertDescription>
                从该角色移除 IAM 管理权限可能导致后续无法再编辑角色。
              </AlertDescription>
            </Alert>

            <RolePermissionEditor
              v-model="selectedPermissionCodes"
              :permissions="roles.permissions.value"
            />

            <DialogFooter show-close-button>
              <Button
                type="submit"
                :disabled="roles.updateRolePermissionsPending.value || !canManageRoles"
              >
                保存权限
              </Button>
            </DialogFooter>
          </form>
        </DialogContent>
      </Dialog>
    </section>
  </DefaultLayout>
</template>
