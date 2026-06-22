<script setup lang="ts">
import type { ConsoleIamSessionResponse } from '@nerv-iip/api-client'
import type { DataTableColumn } from '@nerv-iip/ui'
import RevokeSessionDialog from '@/components/iam/RevokeSessionDialog.vue'
import { useIamSessions } from '@/composables/useIamAdmin'
import { useHasPermission } from '@/composables/usePermissions'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { useAuthStore } from '@/stores/auth'
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
import { RefreshCwIcon } from 'lucide-vue-next'
import { computed, shallowRef, unref, watch } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: 'IAM 会话',
  },
})

const auth = useAuthStore()
const sessions = useIamSessions()

const search = shallowRef('')
const status = shallowRef<'' | 'active' | 'revoked'>('')
const canRevokeSessions = useHasPermission('iam.sessions.revoke')
const revokeOpen = shallowRef(false)
const selectedSession = shallowRef<ConsoleIamSessionResponse>()

type SessionRow = ConsoleIamSessionResponse

const currentSessionId = computed(() => unref(auth.sessionId))

const pageError = computed(() => sessions.sessionsError.value ?? sessions.revokeSessionError.value)

const tablePending = computed(
  () => sessions.sessionsPending.value || sessions.revokeSessionPending.value,
)

const page = computed({
  get: () => sessions.filters.pageIndex,
  set: (value: number) => {
    sessions.filters.pageIndex = value
  },
})
const pageSize = computed({
  get: () => String(sessions.filters.pageSize),
  set: (value: string) => {
    sessions.filters.pageSize = Number(value) || 20
    sessions.filters.pageIndex = 1
  },
})

const statusModel = computed({
  get: () => status.value || 'all',
  set: (value: string) => {
    status.value = value === 'active' || value === 'revoked' ? value : ''
  },
})

const columns: DataTableColumn<SessionRow>[] = [
  { key: 'sessionId', header: '会话 ID', cellClass: 'font-mono text-xs' },
  {
    key: 'userId',
    header: '用户 ID',
    cellClass: 'font-mono text-xs text-muted-foreground',
    accessor: (r) => r.userId || '—',
  },
  { key: 'issuedAtUtc', header: '签发时间', accessor: (r) => formatDate(r.issuedAtUtc) },
  { key: 'expiresAtUtc', header: '过期时间', accessor: (r) => formatDate(r.expiresAtUtc) },
  { key: 'status', header: '状态', width: 'w-24' },
  {
    key: 'permissionVersion',
    header: '权限版本',
    align: 'end',
    width: 'w-24',
    accessor: (r) => r.permissionVersion ?? '—',
  },
  { key: 'actions', header: '操作', align: 'end', width: 'w-24' },
]

watch(
  [search, status],
  ([nextSearch, nextStatus]) => {
    sessions.filters.filterSearch = nextSearch.trim() || undefined
    sessions.filters.filterRevoked = statusToRevokedFilter(nextStatus)
    sessions.filters.pageIndex = 1
  },
  { immediate: true },
)

function statusToRevokedFilter(nextStatus: '' | 'active' | 'revoked') {
  if (nextStatus === 'active') return false
  if (nextStatus === 'revoked') return true
  return undefined
}

function formatDate(value?: string | null) {
  if (!value) return '—'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return date.toLocaleString()
}

function sessionLabel(session: ConsoleIamSessionResponse) {
  return session.sessionId || '会话'
}

function isCurrentSession(session: ConsoleIamSessionResponse) {
  return Boolean(session.sessionId) && session.sessionId === currentSessionId.value
}

function canRevoke(session: ConsoleIamSessionResponse) {
  return (
    Boolean(session.sessionId) &&
    !session.revokedAtUtc &&
    session.sessionId !== currentSessionId.value &&
    canRevokeSessions.value
  )
}

function openRevokeDialog(session: ConsoleIamSessionResponse) {
  selectedSession.value = session
  revokeOpen.value = true
}

async function refreshSessions() {
  await sessions.refreshSessions()
}

async function confirmRevoke(sessionId: string) {
  await sessions.revokeSession({ path: { sessionId } })
  revokeOpen.value = false
  await sessions.refreshSessions()
  toast.success('会话已吊销')
}
</script>

<template>
  <DefaultLayout>
    <section class="grid gap-6">
      <PageHeader
        title="会话"
        :breadcrumbs="[{ label: '身份与访问' }]"
        :count="`${sessions.totalCount.value} 个会话`"
      >
        <template #actions>
          <Button
            size="sm"
            type="button"
            variant="outline"
            :disabled="tablePending"
            @click="refreshSessions"
          >
            <RefreshCwIcon aria-hidden="true" />
            刷新
          </Button>
        </template>
      </PageHeader>

      <Toolbar
        :search="search"
        search-label="搜索会话"
        search-placeholder="搜索会话"
        @update:search="search = $event"
      >
        <template #filters>
          <Select v-model="statusModel">
            <SelectTrigger class="h-9 w-36" aria-label="按状态筛选">
              <SelectValue placeholder="状态" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="all">全部状态</SelectItem>
              <SelectItem value="active">活跃</SelectItem>
              <SelectItem value="revoked">已吊销</SelectItem>
            </SelectContent>
          </Select>
        </template>
      </Toolbar>

      <p v-if="pageError" class="text-sm text-destructive" role="alert">{{ pageError.message }}</p>

      <DataTable
        :columns="columns"
        :rows="sessions.sessions.value"
        row-key="sessionId"
        :loading="tablePending"
        empty-message="没有符合当前条件的会话。"
      >
        <template #cell-sessionId="{ row }">
          <div class="flex flex-col gap-1">
            <span>{{ row.sessionId || '—' }}</span>
            <span v-if="isCurrentSession(row)" class="text-xs font-normal text-muted-foreground">
              当前会话
            </span>
          </div>
        </template>
        <template #cell-status="{ row }">
          <StatusBadge
            :label="row.revokedAtUtc ? '已吊销' : '活跃'"
            :tone="row.revokedAtUtc ? 'neutral' : 'success'"
          />
        </template>
        <template #cell-actions="{ row }">
          <div class="flex items-center justify-end">
            <Button
              size="sm"
              type="button"
              variant="destructive"
              :aria-label="`吊销会话 ${sessionLabel(row)}`"
              :disabled="!canRevoke(row)"
              @click="openRevokeDialog(row)"
            >
              吊销
            </Button>
          </div>
        </template>
      </DataTable>

      <DataTablePagination
        v-model:page="page"
        v-model:page-size="pageSize"
        :total-items="sessions.totalCount.value"
      />

      <RevokeSessionDialog
        v-model:open="revokeOpen"
        :current-session-id="currentSessionId"
        :pending="sessions.revokeSessionPending.value"
        :session="selectedSession"
        @confirm="confirmRevoke"
      />
    </section>
  </DefaultLayout>
</template>
