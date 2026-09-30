<script setup lang="ts">
import { formatDateTime } from '@/utils/format'
import type { BusinessConsoleNotificationMessageItem } from '@nerv-iip/api-client'
import { Button } from '@nerv-iip/ui'
import { messageTitle, resourceTypeLabel } from './notificationFormatters'
import { isReadMessage } from '@/composables/useBusinessNotifications'

defineProps<{ message: BusinessConsoleNotificationMessageItem; markReadPending: boolean }>()
defineEmits<{ markRead: [messageId: string]; close: [] }>()
</script>

<template>
  <section class="grid gap-3 rounded-lg border bg-card p-4" aria-label="通知详情">
    <div class="flex items-start justify-between gap-3">
      <h2 class="break-anywhere text-sm font-semibold">{{ messageTitle(message) }}</h2>
      <Button variant="ghost" size="sm" @click="$emit('close')">收起详情</Button>
    </div>
    <p v-if="message.summary" class="whitespace-pre-wrap break-anywhere text-sm">
      {{ message.summary }}
    </p>
    <dl class="grid gap-2 text-sm">
      <div>
        <dt class="text-muted-foreground">状态</dt>
        <dd>{{ isReadMessage(message) ? '已读' : '未读' }}</dd>
      </div>
      <div>
        <dt class="text-muted-foreground">发送时间</dt>
        <dd>{{ formatDateTime(message.createdAtUtc) }}</dd>
      </div>
      <template v-if="message.resource">
        <div>
          <dt class="text-muted-foreground">业务类型</dt>
          <dd class="break-anywhere">
            {{
              message.resource.resourceType
                ? resourceTypeLabel(message.resource.resourceType)
                : '未指定'
            }}
          </dd>
        </div>
        <div v-if="message.resource.resourceId">
          <dt class="text-muted-foreground">关联资源</dt>
          <dd class="break-anywhere">{{ message.resource.resourceId }}</dd>
        </div>
        <div v-if="message.resource.fileId">
          <dt class="text-muted-foreground">关联文件</dt>
          <dd class="break-anywhere">{{ message.resource.fileId }}</dd>
        </div>
      </template>
      <p v-else class="text-muted-foreground">无关联资源</p>
    </dl>
    <Button
      v-if="message.messageId && !isReadMessage(message)"
      size="sm"
      :disabled="markReadPending"
      @click="$emit('markRead', message.messageId)"
      >标记已读</Button
    >
  </section>
</template>
