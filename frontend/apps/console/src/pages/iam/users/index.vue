<script setup lang="ts">
import type {
  ConsoleCreateIamUserRequest,
  ConsoleIamUserResponse,
  ConsoleResetIamUserPasswordRequest,
  ConsoleUpdateIamUserRequest,
} from '@nerv-iip/api-client'
import type { DataTableColumn } from '@nerv-iip/ui'
import UserCreateDialog from '@/components/iam/UserCreateDialog.vue'
import UserEditDialog from '@/components/iam/UserEditDialog.vue'
import UserResetPasswordDialog from '@/components/iam/UserResetPasswordDialog.vue'
import { useIamUsers } from '@/composables/useIamAdmin'
import { useHasPermission } from '@/composables/usePermissions'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import {
  Button,
  DataTable,
  DataTablePagination,
  PageHeader,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
  StatusBadge,
  Toolbar,
  toast,
} from '@nerv-iip/ui'
import { computed, shallowRef, watch } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: 'IAM 用户',
  },
})

const {
  createUser,
  createUserError,
  createUserPending,
  disableUser,
  disableUserError,
  disableUserPending,
  filters,
  refreshUsers,
  resetUserPassword,
  resetUserPasswordError,
  resetUserPasswordPending,
  totalCount,
  updateUser,
  updateUserError,
  updateUserPending,
  users,
  usersError,
  usersPending,
} = useIamUsers()

type CreateUserData = Parameters<typeof createUser>[0]
type DisableUserData = Parameters<typeof disableUser>[0]
type ResetUserPasswordData = Parameters<typeof resetUserPassword>[0]
type UpdateUserData = Parameters<typeof updateUser>[0]
type UserRow = ConsoleIamUserResponse

const search = shallowRef('')
const status = shallowRef<'' | 'enabled' | 'disabled'>('')
const canManageUsers = useHasPermission('iam.users.manage')
const createDialogOpen = shallowRef(false)
const editDialogOpen = shallowRef(false)
const resetPasswordDialogOpen = shallowRef(false)
const selectedUser = shallowRef<ConsoleIamUserResponse>()

const pageError = computed(
  () =>
    usersError.value ??
    createUserError.value ??
    updateUserError.value ??
    disableUserError.value ??
    resetUserPasswordError.value,
)

const tablePending = computed(
  () =>
    usersPending.value ||
    createUserPending.value ||
    updateUserPending.value ||
    disableUserPending.value ||
    resetUserPasswordPending.value,
)

// 服务端分页桥接：composable 用 1-based pageIndex + 数字 pageSize；
// DataTablePagination 用 number page + string pageSize。
const page = computed({
  get: () => filters.pageIndex,
  set: (value: number) => {
    filters.pageIndex = value
  },
})
const pageSize = computed({
  get: () => String(filters.pageSize),
  set: (value: string) => {
    filters.pageSize = Number(value) || 20
    filters.pageIndex = 1
  },
})

const statusModel = computed({
  get: () => status.value || 'all',
  set: (value: string) => {
    status.value = value === 'enabled' || value === 'disabled' ? value : ''
  },
})

const columns: DataTableColumn<UserRow>[] = [
  { key: 'loginName', header: '登录名', cellClass: 'font-medium', accessor: (r) => r.loginName || '—' },
  { key: 'email', header: '邮箱', accessor: (r) => r.email || '—' },
  { key: 'userId', header: '用户 ID', cellClass: 'font-mono text-xs text-muted-foreground', accessor: (r) => r.userId || '—' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-56' },
]

watch(
  [search, status],
  ([nextSearch, nextStatus]) => {
    filters.filterSearch = nextSearch.trim() || undefined
    filters.filterEnabled = statusToEnabledFilter(nextStatus)
    filters.pageIndex = 1
  },
  { immediate: true },
)

function statusToEnabledFilter(nextStatus: '' | 'enabled' | 'disabled') {
  if (nextStatus === 'enabled') return true
  if (nextStatus === 'disabled') return false
  return undefined
}

function userLabel(user: ConsoleIamUserResponse) {
  return user.loginName || user.userId || '用户'
}

function openCreateDialog() {
  createDialogOpen.value = true
}

function openEditDialog(user: ConsoleIamUserResponse) {
  selectedUser.value = user
  editDialogOpen.value = true
}

function openResetPasswordDialog(user: ConsoleIamUserResponse) {
  selectedUser.value = user
  resetPasswordDialogOpen.value = true
}

async function handleCreate(payload: Required<ConsoleCreateIamUserRequest>) {
  const data: CreateUserData = { body: payload }
  await createUser(data)
  await refreshUsers()
  toast.success('用户已创建')
}

async function handleUpdate(payload: Required<ConsoleUpdateIamUserRequest>) {
  const userId = selectedUser.value?.userId
  if (!userId) return

  const data: UpdateUserData = { body: payload, path: { userId } }
  await updateUser(data)
  await refreshUsers()
  toast.success('用户已更新')
}

async function handleDisable(user: ConsoleIamUserResponse) {
  const userId = user.userId
  if (!userId) return

  const data: DisableUserData = { path: { userId } }
  await disableUser(data)
  await refreshUsers()
  toast.success('用户已停用')
}

async function handleResetPassword(payload: Required<ConsoleResetIamUserPasswordRequest>) {
  const userId = selectedUser.value?.userId
  if (!userId) return

  const data: ResetUserPasswordData = { body: payload, path: { userId } }
  await resetUserPassword(data)
  await refreshUsers()
  toast.success('密码已重置')
}
</script>

<template>
  <DefaultLayout>
    <section class="grid gap-6">
      <PageHeader title="用户" :breadcrumbs="[{ label: '身份与访问' }]" :count="`${totalCount} 个用户`">
        <template #actions>
          <Button type="button" :disabled="!canManageUsers" @click="openCreateDialog">新建用户</Button>
        </template>
      </PageHeader>

      <Toolbar
        :search="search"
        search-label="搜索用户"
        search-placeholder="搜索用户"
        @update:search="search = $event"
      >
        <template #filters>
          <Select v-model="statusModel">
            <SelectTrigger class="h-9 w-36" aria-label="按状态筛选">
              <SelectValue placeholder="状态" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">全部状态</SelectItem>
              <SelectItem value="enabled">已启用</SelectItem>
              <SelectItem value="disabled">已禁用</SelectItem>
            </SelectContent>
          </Select>
        </template>
      </Toolbar>

      <p v-if="pageError" class="text-sm text-destructive" role="alert">{{ pageError.message }}</p>

      <DataTable
        :columns="columns"
        :rows="users"
        row-key="userId"
        :loading="tablePending"
        empty-message="没有符合当前条件的用户。"
      >
        <template #cell-status="{ row }">
          <StatusBadge
            :label="row.enabled === false ? '禁用' : '启用'"
            :tone="row.enabled === false ? 'neutral' : 'success'"
          />
        </template>
        <template #cell-actions="{ row }">
          <div class="flex items-center justify-end gap-2">
            <Button
              size="sm"
              type="button"
              variant="outline"
              :aria-label="`编辑用户 ${userLabel(row)}`"
              :disabled="!canManageUsers"
              @click="openEditDialog(row)"
            >
              编辑
            </Button>
            <Button
              size="sm"
              type="button"
              variant="outline"
              :aria-label="`重置密码 ${userLabel(row)}`"
              :disabled="!canManageUsers"
              @click="openResetPasswordDialog(row)"
            >
              重置密码
            </Button>
            <Button
              size="sm"
              type="button"
              variant="destructive"
              :aria-label="`停用用户 ${userLabel(row)}`"
              :disabled="!canManageUsers || row.enabled === false"
              @click="handleDisable(row)"
            >
              停用
            </Button>
          </div>
        </template>
      </DataTable>

      <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="totalCount" />

      <UserCreateDialog v-model:open="createDialogOpen" @submit="handleCreate" />
      <UserEditDialog v-model:open="editDialogOpen" :user="selectedUser" @submit="handleUpdate" />
      <UserResetPasswordDialog
        v-model:open="resetPasswordDialogOpen"
        :user="selectedUser"
        @submit="handleResetPassword"
      />
    </section>
  </DefaultLayout>
</template>
