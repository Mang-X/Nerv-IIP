<script setup lang="ts">
import { consolePagePermissions } from '@/router/access'
import type { ConsoleIamRoleResponse } from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import PermissionCodeBadge from '@/components/iam/PermissionCodeBadge.vue'
import RoleCreateDialog from '@/components/iam/RoleCreateDialog.vue'
import RoleDataScopesDialog from '@/components/iam/RoleDataScopesDialog.vue'
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
  NvPagination,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvPageHeader,
  NvToolbar,
  toast,
} from '@nerv-iip/ui'
import { computed, shallowRef, watch } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    requiredPermissions: consolePagePermissions['/iam/roles'],
    title: 'IAM 角色',
  },
})

const roles = useIamRoles()
const scopeRole = shallowRef<ConsoleIamRoleResponse>()
const scopeOpen = shallowRef(false)

function editScopes(role: ConsoleIamRoleResponse) {
  scopeRole.value = role
  scopeOpen.value = true
}

async function refreshConflictingScopes() {
  scopeOpen.value = false
  toast.error('角色范围已更新，请重新打开编辑。')
  await roles.refreshRoles()
}

async function refreshScopes() {
  await roles.refreshRoles()
  toast.success('数据范围已更新')
}

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

const columns: NvDataTableColumn<RoleRow>[] = [
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
  { key: 'dataScopes', header: '数据范围' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-48' },
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
        <template #cell-dataScopes="{ row }">
          <details v-if="row.dataScopes?.length" class="text-sm">
            <summary class="cursor-pointer">{{ row.dataScopes.length }} 项</summary>
            <ul class="mt-1 grid gap-1 text-xs text-muted-foreground">
              <li
                v-for="scope in row.dataScopes"
                :key="`${scope.scopeType}:${scope.scopeCode}`"
                class="break-all"
              >
                {{ scope.scopeType }} · {{ scope.scopeCode }}
              </li>
            </ul>
          </details>
          <span v-else class="text-sm text-muted-foreground">未配置</span>
        </template>
        <template #cell-actions="{ row }">
          <div class="flex flex-wrap items-center justify-end gap-2">
            <Button
              v-if="canManageRoles"
              size="sm"
              type="button"
              variant="outline"
              @click="editScopes(row)"
            >
              编辑范围
            </Button>
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

      <NvPagination
        v-model:page="page"
        v-model:page-size="pageSize"
        :total-items="roles.totalCount.value"
      />

      <RoleDataScopesDialog
        v-if="scopeOpen && scopeRole"
        v-model:open="scopeOpen"
        :role="scopeRole"
        @saved="refreshScopes"
        @conflict="refreshConflictingScopes"
      />

      <RoleCreateDialog
        v-model:open="createDialogOpen"
        :pending="roles.createRolePending.value"
        :permissions="roles.permissions.value"
        @submit="handleCreate"
      />

      <NvDialog v-model:open="editOpen">
        <NvDialogContent class="max-h-[min(90vh,48rem)] overflow-y-auto sm:max-w-3xl">
          <NvDialogHeader>
            <NvDialogTitle>编辑角色权限</NvDialogTitle>
            <NvDialogDescription>
              更新分配给 {{ selectedRole?.roleName || '该角色' }} 的权限码。
            </NvDialogDescription>
          </NvDialogHeader>

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

            <NvDialogFooter show-close-button>
              <Button
                type="submit"
                :disabled="roles.updateRolePermissionsPending.value || !canManageRoles"
              >
                保存权限
              </Button>
            </NvDialogFooter>
          </form>
        </NvDialogContent>
      </NvDialog>
    </section>
  </DefaultLayout>
</template>
