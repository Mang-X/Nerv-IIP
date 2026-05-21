<script setup lang="ts">
import type { NotificationTaskResponse } from '@nerv-iip/api-client'
import { Badge, Skeleton } from '@nerv-iip/ui'
import { formatNotificationDate, notificationBadgeVariant } from './notificationFormatters'

const props = defineProps<{
  pending?: boolean
  tasks: NotificationTaskResponse[]
}>()

function rowKey(task: NotificationTaskResponse, index: number) {
  return task.taskId ?? task.messageId ?? `task:${index}`
}
</script>

<template>
  <section class="overflow-hidden rounded-lg border bg-background" aria-labelledby="notification-tasks-title">
    <div class="flex items-center justify-between border-b px-4 py-3">
      <h2 id="notification-tasks-title" class="text-sm font-semibold">Open tasks</h2>
      <span class="text-xs font-semibold text-muted-foreground">{{ props.tasks.length }}</span>
    </div>

    <div v-if="props.pending" class="grid gap-3 p-4">
      <Skeleton v-for="i in 3" :key="i" class="h-16 w-full" />
    </div>

    <ul v-else-if="props.tasks.length" class="m-0 list-none divide-y p-0">
      <li v-for="(task, index) in props.tasks" :key="rowKey(task, index)" class="grid gap-2 px-4 py-3">
        <div class="flex min-w-0 items-start justify-between gap-3">
          <div class="min-w-0">
            <p class="break-anywhere text-sm font-semibold">
              {{ task.taskType ?? 'notification task' }}
            </p>
            <p class="break-anywhere text-xs text-muted-foreground">
              {{ task.actionRef ?? task.messageId ?? task.taskId ?? 'No action reference' }}
            </p>
          </div>
          <Badge :variant="notificationBadgeVariant(task.status)">
            {{ task.status ?? 'unknown' }}
          </Badge>
        </div>
        <p class="text-xs text-muted-foreground">{{ formatNotificationDate(task.createdAtUtc) }}</p>
      </li>
    </ul>

    <p v-else class="px-4 py-8 text-center text-sm text-muted-foreground">
      No open notification tasks.
    </p>
  </section>
</template>
