<script setup lang="ts">
import NotificationMessageList from '@/components/notifications/NotificationMessageList.vue'
import NotificationTaskList from '@/components/notifications/NotificationTaskList.vue'
import { useNotifications } from '@/composables/useNotifications'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import {
  Button,
  PageHeader,
  SectionCard,
  SectionCards,
  toast,
} from '@nerv-iip/ui'
import { CheckCheckIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: '通知',
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
const errorMessage = computed(() => (allError.value ? allError.value.message : ''))
const markAllDisabled = computed(
  () => batchPending.value || markReadPending.value || pending.value || unreadMessages.value.length === 0,
)
const ignoreHandledError = (_error: unknown) => {}

async function handleRefresh() {
  await refreshNotifications().catch(ignoreHandledError)
}

async function handleMarkRead(messageId: string) {
  try {
    await markRead(messageId)
    toast.success('通知已标记为已读')
  } catch (error) {
    ignoreHandledError(error)
  }
}

async function handleMarkAllRead() {
  try {
    await markAllUnreadRead()
    toast.success('未读通知已全部标记为已读')
  } catch (error) {
    ignoreHandledError(error)
  }
}
</script>

<template>
  <DefaultLayout>
    <section class="grid gap-6">
      <PageHeader title="通知" :breadcrumbs="[{ label: '平台' }]" :count="`${unreadMessages.length} 条未读`">
        <template #actions>
          <Button
            :disabled="pending"
            size="sm"
            type="button"
            variant="outline"
            aria-label="刷新通知"
            @click="handleRefresh"
          >
            <RefreshCwIcon class="size-4" aria-hidden="true" />
            刷新
          </Button>
          <Button
            :disabled="markAllDisabled"
            size="sm"
            type="button"
            aria-label="全部标记已读"
            @click="handleMarkAllRead"
          >
            <CheckCheckIcon class="size-4" aria-hidden="true" />
            全部已读
          </Button>
        </template>
      </PageHeader>

      <SectionCards :columns="3">
        <SectionCard description="未读消息" :value="unreadMessages.length" hint="待处理通知" />
        <SectionCard description="已读消息" :value="readMessages.length" hint="近期已读" />
        <SectionCard description="待办任务" :value="openTasks.length" hint="需要跟进的通知任务" />
      </SectionCards>

      <p v-if="errorMessage" class="text-sm text-destructive" role="alert">
        无法更新通知：{{ errorMessage }}
      </p>

      <div class="grid items-start gap-4 xl:grid-cols-[minmax(0,1fr)_24rem]">
        <div class="grid min-w-0 gap-4">
          <NotificationMessageList
            empty-text="暂无未读通知。"
            :mark-read-pending="markReadPending"
            :messages="unreadMessages"
            :pending="messagesPending"
            show-mark-read
            title="未读消息"
            @mark-read="handleMarkRead"
          />
          <NotificationMessageList
            empty-text="暂无已读通知。"
            :messages="readMessages"
            :pending="messagesPending"
            title="已读消息"
          />
        </div>

        <NotificationTaskList :pending="tasksPending" :tasks="openTasks" />
      </div>
    </section>
  </DefaultLayout>
</template>
