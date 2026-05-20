<script setup lang="ts">
import type {
  ConsoleCreateIamUserRequest,
  ConsoleIamUserResponse,
  ConsoleResetIamUserPasswordRequest,
  ConsoleUpdateIamUserRequest,
} from '@nerv-iip/api-client'
import IamPagination from '@/components/iam/IamPagination.vue'
import IamListToolbar from '@/components/iam/IamListToolbar.vue'
import IamPageHeader from '@/components/iam/IamPageHeader.vue'
import UserCreateDialog from '@/components/iam/UserCreateDialog.vue'
import UserEditDialog from '@/components/iam/UserEditDialog.vue'
import UserResetPasswordDialog from '@/components/iam/UserResetPasswordDialog.vue'
import UsersTable from '@/components/iam/UsersTable.vue'
import { useIamUsers } from '@/composables/useIamAdmin'
import { useHasPermission } from '@/composables/usePermissions'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Alert, AlertDescription, AlertTitle, toast } from '@nerv-iip/ui'
import { computed, shallowRef, watch } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: 'IAM Users',
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
  if (nextStatus === 'enabled') {
    return true
  }

  if (nextStatus === 'disabled') {
    return false
  }

  return undefined
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
  const data: CreateUserData = {
    body: payload,
  }

  await createUser(data)
  await refreshUsers()
  toast.success('User created')
}

async function handleUpdate(payload: Required<ConsoleUpdateIamUserRequest>) {
  const userId = selectedUser.value?.userId
  if (!userId) {
    return
  }

  const data: UpdateUserData = {
    body: payload,
    path: { userId },
  }

  await updateUser(data)
  await refreshUsers()
  toast.success('User updated')
}

async function handleDisable(user: ConsoleIamUserResponse) {
  const userId = user.userId
  if (!userId) {
    return
  }

  const data: DisableUserData = {
    path: { userId },
  }

  await disableUser(data)
  await refreshUsers()
  toast.success('User disabled')
}

async function handleResetPassword(payload: Required<ConsoleResetIamUserPasswordRequest>) {
  const userId = selectedUser.value?.userId
  if (!userId) {
    return
  }

  const data: ResetUserPasswordData = {
    body: payload,
    path: { userId },
  }

  await resetUserPassword(data)
  await refreshUsers()
  toast.success('Password reset')
}
</script>

<template>
  <DefaultLayout>
    <section class="grid gap-6">
      <IamPageHeader
        title="Users"
        description="Manage IAM users that can sign in to the Nerv IIP console."
      />

      <IamListToolbar
        v-model:search="search"
        v-model:status="status"
        action-label="Create user"
        :action-disabled="!canManageUsers"
        search-label="Search users"
        search-placeholder="Search users"
        show-status-filter
        @action="openCreateDialog"
      />

      <Alert v-if="pageError" variant="destructive">
        <AlertTitle>Unable to complete user request</AlertTitle>
        <AlertDescription>{{ pageError.message }}</AlertDescription>
      </Alert>

      <UsersTable
        :can-manage="canManageUsers"
        :pending="tablePending"
        :users="users"
        @disable="handleDisable"
        @edit="openEditDialog"
        @reset-password="openResetPasswordDialog"
      />

      <IamPagination
        :page-index="filters.pageIndex"
        :page-size="filters.pageSize"
        :total-count="totalCount"
        @page-change="filters.pageIndex = $event"
      />

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
