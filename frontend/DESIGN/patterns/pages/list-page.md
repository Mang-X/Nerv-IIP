# Page: List Page

Standard CRUD entity list page. Composes Toolbar + Data Table + Create Dialog + Confirm Destroy.

## Reference implementations

- `frontend/apps/console/src/pages/iam/users/index.vue`
- `frontend/apps/console/src/pages/iam/roles/index.vue`
- `frontend/apps/console/src/pages/iam/sessions/index.vue`

## Structure

```vue
<script setup lang="ts">
// 1. Composable (handles query, pagination, mutations)
const { users, pending, refetch } = useIamAdmin()

// 2. Local UI state
const search = ref('')
const statusFilter = ref('')
const createOpen = ref(false)
const confirmTarget = ref<User | null>(null)
const confirmOpen = ref(false)

// 3. Derived (client-side filter if data set is small)
const filteredUsers = computed(() =>
  users.value.filter(u => matchesSearch(u, search.value) && matchesStatus(u, statusFilter.value))
)

// 4. Permissions
const { canManageIam } = usePermissions()
</script>

<template>
  <div class="flex flex-col gap-6 p-6">
    <!-- Page header -->
    <div>
      <h1 class="text-2xl font-semibold tracking-tight">Users</h1>
      <p class="text-sm text-muted-foreground">Manage system users and their access.</p>
    </div>

    <!-- Toolbar block -->
    <IamListToolbar
      v-model:search="search"
      v-model:status="statusFilter"
      action-label="Create User"
      search-placeholder="Search by login name or email"
      show-status-filter
      :action-disabled="!canManageIam"
      @action="createOpen = true"
    />

    <!-- Data table block -->
    <UsersTable
      :users="filteredUsers"
      :pending="pending"
      :can-manage="canManageIam"
      @edit="openEdit"
      @disable="openConfirm"
    />

    <!-- Create dialog flow -->
    <UserCreateDialog v-model:open="createOpen" @created="refetch" />

    <!-- Confirm destroy flow -->
    <AlertDialog v-model:open="confirmOpen">
      <!-- see patterns/flows/confirm-destroy.md -->
    </AlertDialog>
  </div>
</template>
```

## Layout Rules

- Top-level page container: `class="flex flex-col gap-6 p-6"`.
- Page heading: `text-2xl font-semibold tracking-tight` + optional `text-sm text-muted-foreground` subtitle.
- Toolbar and table are siblings, separated by `gap-6` (from parent flex).
- Dialogs/AlertDialogs are declared once at the page level, outside loops.

## Do NOT

- Do not repeat the `gap-6 p-6` pattern inside child components — it's a page-level concern.
- Do not put `AlertDialog` inside the Table component — it must be at the page level.
- Do not use `v-show` to hide the toolbar while loading.
