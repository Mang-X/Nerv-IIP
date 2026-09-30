<script setup lang="ts">
import { BellIcon, RefreshCwIcon } from '@lucide/vue'
import {
  Button,
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
  SheetTrigger,
} from '@nerv-iip/ui'
import { computed, ref } from 'vue'
import { useBusinessNotifications } from '@/composables/useBusinessNotifications'
import NotificationMessageList from './NotificationMessageList.vue'
import NotificationMessageDetail from './NotificationMessageDetail.vue'
import { resourceTypeLabel } from './notificationFormatters'

const {
  messages,
  unreadMessages,
  messagesPending,
  allError,
  markRead,
  markReadPending,
  refreshNotifications,
} = useBusinessNotifications()
const resourceType = ref('')
const selectedMessageId = ref<string>()
const resourceTypes = computed(() =>
  [
    ...new Set(
      messages.value.flatMap((message) =>
        message.resource?.resourceType ? [message.resource.resourceType] : [],
      ),
    ),
  ].sort(),
)
const filteredMessages = computed(() =>
  messages.value.filter(
    (message) => !resourceType.value || message.resource?.resourceType === resourceType.value,
  ),
)
const selectedMessage = computed(() =>
  messages.value.find((message) => message.messageId === selectedMessageId.value),
)

async function handleMarkRead(messageId: string) {
  try {
    await markRead(messageId)
  } catch {
    // useBusinessNotifications 将操作错误展示在面板中。
  }
}
</script>

<template>
  <Sheet>
    <SheetTrigger as-child>
      <Button variant="ghost" size="sm" :aria-label="`通知收件箱，${unreadMessages.length} 条未读`">
        <BellIcon class="size-4" aria-hidden="true" />
        <span class="hidden sm:inline">收件箱</span>
        <span class="rounded-full bg-muted px-1.5 text-xs tabular-nums">{{
          unreadMessages.length
        }}</span>
      </Button>
    </SheetTrigger>
    <SheetContent class="w-full overflow-y-auto sm:max-w-xl">
      <SheetHeader>
        <SheetTitle>通知收件箱</SheetTitle>
        <SheetDescription>{{ unreadMessages.length }} 条未读通知</SheetDescription>
      </SheetHeader>
      <div class="grid gap-4 px-4 pb-6">
        <div class="flex items-end gap-2">
          <label class="grid min-w-0 flex-1 gap-1 text-sm">
            业务类型
            <select
              v-model="resourceType"
              aria-label="业务类型"
              class="h-9 rounded-md border bg-background px-3"
            >
              <option value="">全部类型</option>
              <option v-for="type in resourceTypes" :key="type" :value="type">
                {{ resourceTypeLabel(type) }}
              </option>
            </select>
          </label>
          <Button
            variant="outline"
            size="sm"
            :disabled="messagesPending"
            @click="refreshNotifications"
          >
            <RefreshCwIcon class="size-4" aria-hidden="true" />刷新
          </Button>
        </div>
        <p v-if="allError" role="alert" class="text-sm text-destructive">
          无法更新通知：{{ allError.message }}
        </p>
        <NotificationMessageDetail
          v-if="selectedMessage"
          :message="selectedMessage"
          :mark-read-pending="markReadPending"
          @mark-read="handleMarkRead"
          @close="selectedMessageId = undefined"
        />
        <NotificationMessageList
          :messages="filteredMessages"
          :pending="messagesPending"
          :mark-read-pending="markReadPending"
          @mark-read="handleMarkRead"
          @open="selectedMessageId = $event"
        />
      </div>
    </SheetContent>
  </Sheet>
</template>
