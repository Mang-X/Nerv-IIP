<script setup lang="ts">
import type { BusinessConsoleNotificationMessageItem } from '@nerv-iip/api-client'
import { Button } from '@nerv-iip/ui'
import { isReadMessage } from '@/composables/useBusinessNotifications'
import { messageTitle, formatNotificationDate, resourceTypeLabel } from './notificationFormatters'

defineProps<{
  messages: BusinessConsoleNotificationMessageItem[]
  pending: boolean
  markReadPending: boolean
}>()
defineEmits<{ open: [messageId: string]; markRead: [messageId: string] }>()
</script>

<template>
  <p v-if="pending" role="status" class="py-6 text-center text-sm text-muted-foreground">
    正在加载通知…
  </p>
  <ul v-else-if="messages.length" class="divide-y rounded-lg border bg-card">
    <li v-for="message in messages" :key="message.messageId" class="grid gap-2 p-4">
      <div class="flex items-start justify-between gap-3">
        <button
          type="button"
          class="break-anywhere text-left text-sm font-semibold underline-offset-4 hover:underline"
          :disabled="!message.messageId"
          @click="$emit('open', message.messageId!)"
        >
          {{ messageTitle(message) }}
        </button>
        <span class="shrink-0 text-xs text-muted-foreground">{{
          isReadMessage(message) ? '已读' : '未读'
        }}</span>
      </div>
      <p v-if="message.summary" class="line-clamp-2 break-anywhere text-sm text-muted-foreground">
        {{ message.summary }}
      </p>
      <div class="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
        <span v-if="message.resource?.resourceType">{{
          resourceTypeLabel(message.resource.resourceType)
        }}</span>
        <span>{{ formatNotificationDate(message.createdAtUtc) }}</span>
        <Button
          v-if="message.messageId && !isReadMessage(message)"
          variant="ghost"
          size="sm"
          :aria-label="`标记已读：${messageTitle(message)}`"
          :disabled="markReadPending"
          @click="$emit('markRead', message.messageId)"
          >标记已读</Button
        >
      </div>
    </li>
  </ul>
  <p v-else class="py-6 text-center text-sm text-muted-foreground">暂无符合条件的通知。</p>
</template>
