<script setup lang="ts">
import type { NotificationMessageResponse } from '@nerv-iip/api-client'
import { Button, Skeleton, StatusBadge } from '@nerv-iip/ui'
import { CheckIcon } from 'lucide-vue-next'
import { computed } from 'vue'
import {
  formatNotificationDate,
  formatResource,
  messageTitle,
  notificationSeverityLabel,
  notificationStatusLabel,
  notificationTone,
} from './notificationFormatters'

const props = defineProps<{
  emptyText: string
  markReadPending?: boolean
  messages: NotificationMessageResponse[]
  pending?: boolean
  showMarkRead?: boolean
  title: string
  /** Stable, unique section key for the heading id / aria-labelledby (e.g. 'unread'). */
  sectionId: string
}>()

const emit = defineEmits<{
  markRead: [messageId: string]
}>()

const titleId = computed(() => `notification-${props.sectionId}-title`)

function rowKey(message: NotificationMessageResponse, index: number) {
  return message.messageId ?? `message:${index}`
}
</script>

<template>
  <section class="overflow-hidden rounded-lg border bg-card" :aria-labelledby="titleId">
    <div class="flex items-center justify-between border-b px-4 py-3">
      <h2 :id="titleId" class="text-sm font-semibold">{{ props.title }}</h2>
      <span class="text-xs font-semibold text-muted-foreground">{{ props.messages.length }}</span>
    </div>

    <div v-if="props.pending" class="grid gap-3 p-4">
      <Skeleton v-for="i in 3" :key="i" class="h-20 w-full" />
    </div>

    <ul v-else-if="props.messages.length" class="m-0 list-none divide-y p-0">
      <li
        v-for="(message, index) in props.messages"
        :key="rowKey(message, index)"
        class="grid gap-2 px-4 py-3"
      >
        <div class="flex min-w-0 items-start justify-between gap-3">
          <div class="min-w-0">
            <p class="break-anywhere text-sm font-semibold text-foreground">
              {{ messageTitle(message) }}
            </p>
            <p v-if="message.summary" class="mt-1 break-anywhere text-sm text-muted-foreground">
              {{ message.summary }}
            </p>
          </div>
          <Button
            v-if="props.showMarkRead && message.messageId"
            :aria-label="`标记已读：${messageTitle(message)}`"
            :disabled="props.markReadPending"
            size="icon-sm"
            type="button"
            variant="ghost"
            @click="emit('markRead', message.messageId)"
          >
            <CheckIcon class="size-4" />
          </Button>
        </div>

        <div class="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
          <StatusBadge :label="notificationSeverityLabel(message.severity)" :tone="notificationTone(message.severity)" />
          <StatusBadge :label="notificationStatusLabel(message.status)" :tone="notificationTone(message.status)" />
          <span>{{ formatResource(message.resource) }}</span>
          <span>{{ formatNotificationDate(message.createdAtUtc) }}</span>
        </div>
      </li>
    </ul>

    <p v-else class="px-4 py-8 text-center text-sm text-muted-foreground">
      {{ props.emptyText }}
    </p>
  </section>
</template>
