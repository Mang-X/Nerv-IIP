<script setup lang="ts">
import type { ConsoleIamSessionResponse } from '@nerv-iip/api-client'
import IamListToolbar from '@/components/iam/IamListToolbar.vue'
import IamPageHeader from '@/components/iam/IamPageHeader.vue'
import RevokeSessionDialog from '@/components/iam/RevokeSessionDialog.vue'
import SessionsTable from '@/components/iam/SessionsTable.vue'
import { useIamSessions } from '@/composables/useIamAdmin'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { useAuthStore } from '@/stores/auth'
import { Alert, AlertDescription, AlertTitle, toast } from '@nerv-iip/ui'
import { computed, shallowRef, unref, watch } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: 'IAM Sessions',
  },
})

const auth = useAuthStore()
const sessions = useIamSessions()

const search = shallowRef('')
const revokeOpen = shallowRef(false)
const selectedSession = shallowRef<ConsoleIamSessionResponse>()

const currentSessionId = computed(() => unref(auth.sessionId))

const pageError = computed(() =>
  sessions.sessionsError.value
  ?? sessions.revokeSessionError.value,
)

const tablePending = computed(() =>
  sessions.sessionsPending.value
  || sessions.revokeSessionPending.value,
)

watch(search, (nextSearch) => {
  sessions.filters.filterSearch = nextSearch.trim() || undefined
  sessions.filters.pageIndex = 1
}, { immediate: true })

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
  toast.success('Session revoked')
}
</script>

<template>
  <DefaultLayout>
    <section class="grid gap-6">
      <IamPageHeader
        title="Sessions"
        description="Inspect active and revoked IAM console sessions."
      />

      <IamListToolbar
        v-model:search="search"
        action-label="Refresh"
        search-label="Search sessions"
        search-placeholder="Search sessions"
        @action="refreshSessions"
      />

      <Alert v-if="pageError" variant="destructive">
        <AlertTitle>Unable to complete session request</AlertTitle>
        <AlertDescription>{{ pageError.message }}</AlertDescription>
      </Alert>

      <SessionsTable
        :current-session-id="currentSessionId"
        :pending="tablePending"
        :sessions="sessions.sessions.value"
        @revoke="openRevokeDialog"
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
