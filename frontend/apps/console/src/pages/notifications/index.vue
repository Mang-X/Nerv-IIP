<script setup lang="ts">
import NotificationMessageList from '@/components/notifications/NotificationMessageList.vue'
import NotificationTaskList from '@/components/notifications/NotificationTaskList.vue'
import NotificationToolbar from '@/components/notifications/NotificationToolbar.vue'
import { useNotifications } from '@/composables/useNotifications'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Alert, AlertDescription, AlertTitle, toast } from '@nerv-iip/ui'
import { computed } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: 'Notifications',
  },
})

const {
  allError,
  batchPending,
  markAllUnreadRead,
  markRead,
  markReadPending,
  messagesPending,
  openTasks,
  readMessages,
  refreshNotifications,
  tasksPending,
  unreadMessages,
} = useNotifications()

const pending = computed(() => messagesPending.value || tasksPending.value)
const ignoreHandledError = (_error: unknown) => {}

async function handleRefresh() {
  await refreshNotifications().catch(ignoreHandledError)
}

async function handleMarkRead(messageId: string) {
  try {
    await markRead(messageId)
    toast.success('Notification marked read')
  } catch (error) {
    ignoreHandledError(error)
  }
}

async function handleMarkAllRead() {
  try {
    await markAllUnreadRead()
    toast.success('Unread notifications marked read')
  } catch (error) {
    ignoreHandledError(error)
  }
}
</script>

<template>
  <DefaultLayout>
    <div class="grid gap-4">
      <div class="flex flex-wrap items-end justify-between gap-3">
        <div>
          <p class="text-xs font-bold uppercase tracking-wider text-primary">Console</p>
          <h1 class="text-xl font-semibold text-foreground">Notifications</h1>
        </div>
      </div>

      <NotificationToolbar
        :batch-pending="batchPending"
        :mark-read-pending="markReadPending"
        :pending="pending"
        :task-count="openTasks.length"
        :unread-count="unreadMessages.length"
        @mark-all-read="handleMarkAllRead"
        @refresh="handleRefresh"
      />

      <Alert v-if="allError" variant="destructive">
        <AlertTitle>Unable to update notifications</AlertTitle>
        <AlertDescription>{{ allError.message }}</AlertDescription>
      </Alert>

      <div class="grid items-start gap-4 xl:grid-cols-[minmax(0,1fr)_24rem]">
        <div class="grid min-w-0 gap-4">
          <NotificationMessageList
            empty-text="No unread notifications."
            :mark-read-pending="markReadPending"
            :messages="unreadMessages"
            :pending="messagesPending"
            show-mark-read
            title="Unread messages"
            @mark-read="handleMarkRead"
          />
          <NotificationMessageList
            empty-text="No read notifications."
            :messages="readMessages"
            :pending="messagesPending"
            title="Read messages"
          />
        </div>

        <NotificationTaskList :pending="tasksPending" :tasks="openTasks" />
      </div>
    </div>
  </DefaultLayout>
</template>
