<script setup lang="ts">
import { consolePagePermissions } from '@/router/access'
import NotificationMessageList from '@/components/notifications/NotificationMessageList.vue'
import NotificationTaskList from '@/components/notifications/NotificationTaskList.vue'
import { useNotifications } from '@/composables/useNotifications'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import { Button, NvPageHeader, toast } from '@nerv-iip/ui'
import { CheckCheckIcon, RefreshCwIcon } from '@lucide/vue'
import { computed, shallowRef } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    requiredPermissions: consolePagePermissions['/notifications'],
    title: '通知',
  },
})

const {
  actionError,
  canReadMessages,
  canReadTasks,
  canReadOperationTasks,
  canMarkRead,
  messagesError,
  tasksError,
  batchPending,
  markAllUnreadRead,
  markRead,
  markReadPending,
  messagesPending,
  openTasks,
  readMessages,
  refreshNotifications,
  tasksPending,
  tasks,
  unreadMessages,
} = useNotifications()

const messageTitles = computed(() => {
  const titles = new Map<string, string>()
  for (const message of [...unreadMessages.value, ...readMessages.value]) {
    if (message.messageId && message.title?.trim()) {
      titles.set(message.messageId, message.title.trim())
    }
  }
  return titles
})

const messageFilter = shallowRef<'all' | 'unread' | 'read'>('all')
const taskFilter = shallowRef<'all' | 'open'>('open')
const sinceDate = shallowRef('')

function withinRange(createdAtUtc?: string) {
  if (!sinceDate.value) return true
  const threshold = new Date(`${sinceDate.value}T00:00:00`).getTime()
  return Boolean(createdAtUtc) && Date.parse(createdAtUtc!) >= threshold
}

const visibleUnread = computed(() =>
  unreadMessages.value.filter((message) => withinRange(message.createdAtUtc)),
)
const visibleRead = computed(() =>
  readMessages.value.filter((message) => withinRange(message.createdAtUtc)),
)
const visibleTasks = computed(() =>
  (taskFilter.value === 'all' ? tasks.value : openTasks.value).filter((task) =>
    withinRange(task.createdAtUtc),
  ),
)
const operationLinks = computed(() => {
  const links = new Map<string, string>()
  if (!canReadOperationTasks.value) return links
  for (const message of [...unreadMessages.value, ...readMessages.value]) {
    if (
      message.messageId &&
      message.resource?.resourceType === 'operation-task' &&
      message.resource.resourceId
    ) {
      links.set(message.messageId, message.resource.resourceId)
    }
  }
  return links
})

const pending = computed(() => messagesPending.value || tasksPending.value)
const markAllDisabled = computed(
  () =>
    batchPending.value ||
    markReadPending.value ||
    pending.value ||
    visibleUnread.value.length === 0,
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
    await markAllUnreadRead(visibleUnread.value)
    toast.success('本批未读通知已标记为已读')
  } catch (error) {
    ignoreHandledError(error)
  }
}
</script>

<template>
  <DefaultLayout>
    <section class="grid gap-6">
      <NvPageHeader
        title="通知"
        :breadcrumbs="[{ label: '平台' }]"
        :count="
          canReadMessages && !messagesPending && !messagesError
            ? `${unreadMessages.length} 条未读`
            : undefined
        "
      >
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
            v-if="canReadMessages && canMarkRead && messageFilter !== 'read'"
            :disabled="markAllDisabled"
            size="sm"
            type="button"
            aria-label="标记本批已读"
            @click="handleMarkAllRead"
          >
            <CheckCheckIcon class="size-4" aria-hidden="true" />
            标记本批已读
          </Button>
        </template>
      </NvPageHeader>

      <p v-if="actionError" class="text-sm text-destructive" role="alert">
        {{ actionError.message }}
      </p>

      <div class="flex flex-wrap items-end gap-3 text-sm">
        <label v-if="canReadMessages" class="grid gap-1">
          消息状态
          <select v-model="messageFilter" class="rounded-md border bg-background p-2">
            <option value="all">全部消息</option>
            <option value="unread">未读</option>
            <option value="read">已读</option>
          </select>
        </label>
        <label v-if="canReadTasks" class="grid gap-1">
          任务状态
          <select v-model="taskFilter" class="rounded-md border bg-background p-2">
            <option value="open">待办</option>
            <option value="all">全部任务</option>
          </select>
        </label>
        <label class="grid gap-1">
          创建日期自
          <input v-model="sinceDate" type="date" class="rounded-md border bg-background p-2" />
        </label>
      </div>

      <div
        class="grid items-start gap-4"
        :class="{ 'xl:grid-cols-[minmax(0,1fr)_24rem]': canReadMessages && canReadTasks }"
      >
        <div v-if="canReadMessages" class="grid min-w-0 gap-4">
          <p v-if="messagesError" role="alert" class="text-sm text-destructive">
            {{ messagesError.message }}
          </p>
          <template v-else>
            <NotificationMessageList
              v-if="messageFilter !== 'read'"
              empty-text="没有符合条件的未读通知。"
              :can-read-operation-tasks="canReadOperationTasks"
              :mark-read-pending="markReadPending"
              :messages="visibleUnread"
              :pending="messagesPending"
              section-id="unread"
              :show-mark-read="canMarkRead"
              title="未读消息"
              @mark-read="handleMarkRead"
            />
            <NotificationMessageList
              v-if="messageFilter !== 'unread'"
              empty-text="没有符合条件的已读通知。"
              :can-read-operation-tasks="canReadOperationTasks"
              :messages="visibleRead"
              :pending="messagesPending"
              section-id="read"
              title="已读消息"
            />
          </template>
        </div>

        <p v-if="canReadTasks && tasksError" role="alert" class="text-sm text-destructive">
          {{ tasksError.message }}
        </p>
        <NotificationTaskList
          v-else-if="canReadTasks"
          :pending="tasksPending"
          :tasks="visibleTasks"
          :all-statuses="taskFilter === 'all'"
          :operation-links="operationLinks"
          :message-titles="messageTitles"
        />
      </div>
    </section>
  </DefaultLayout>
</template>
