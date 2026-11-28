<script setup lang="ts">
import type { NotificationTaskResponse } from '@nerv-iip/api-client'
import { Skeleton, NvStatusBadge } from '@nerv-iip/ui'
import {
  formatNotificationDate,
  notificationStatusLabel,
  notificationTaskTitle,
  notificationTaskTypeLabel,
  notificationTone,
} from './notificationFormatters'

const props = defineProps<{
  pending?: boolean
  allStatuses?: boolean
  operationLinks?: ReadonlyMap<string, string>
  tasks: NotificationTaskResponse[]
  messageTitles: ReadonlyMap<string, string>
}>()

function rowKey(task: NotificationTaskResponse, index: number) {
  return task.taskId ?? task.messageId ?? `task:${index}`
}
</script>

<template>
  <section
    class="overflow-hidden rounded-lg border bg-card"
    aria-labelledby="notification-tasks-title"
  >
    <div class="flex items-center justify-between border-b px-4 py-3">
      <h2 id="notification-tasks-title" class="text-sm font-semibold">
        {{ props.allStatuses ? '通知任务' : '待办任务' }}
      </h2>
      <span class="text-xs font-semibold text-muted-foreground">{{ props.tasks.length }}</span>
    </div>

    <div v-if="props.pending" class="grid gap-3 p-4">
      <Skeleton v-for="i in 3" :key="i" class="h-16 w-full" />
    </div>

    <ul v-else-if="props.tasks.length" class="m-0 list-none divide-y p-0">
      <li
        v-for="(task, index) in props.tasks"
        :key="rowKey(task, index)"
        class="grid gap-2 px-4 py-3"
      >
        <div class="flex min-w-0 items-start justify-between gap-3">
          <div class="min-w-0">
            <p class="break-anywhere text-sm font-semibold">
              {{ notificationTaskTitle(task, props.messageTitles) }}
            </p>
            <p class="break-anywhere text-xs text-muted-foreground">
              {{ notificationTaskTypeLabel(task.taskType) }}
              <span v-if="task.actionRef || task.messageId || task.taskId">
                · {{ task.actionRef || task.messageId || task.taskId }}
              </span>
            </p>
          </div>
          <NvStatusBadge
            :label="notificationStatusLabel(task.status)"
            :tone="notificationTone(task.status)"
          />
        </div>
        <RouterLink
          v-if="task.messageId && operationLinks?.has(task.messageId)"
          :to="`/operations/${encodeURIComponent(operationLinks.get(task.messageId)!)}`"
          class="text-sm text-primary underline"
          >查看运维任务</RouterLink
        >
        <p class="text-xs text-muted-foreground">{{ formatNotificationDate(task.createdAtUtc) }}</p>
      </li>
    </ul>

    <p v-else class="px-4 py-8 text-center text-sm text-muted-foreground">
      没有符合条件的通知任务。
    </p>
  </section>
</template>
