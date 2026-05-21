<script setup lang="ts">
import { Button } from '@nerv-iip/ui'
import { CheckCheckIcon, RefreshCwIcon } from 'lucide-vue-next'

const props = defineProps<{
  batchPending?: boolean
  markReadPending?: boolean
  pending?: boolean
  taskCount: number
  unreadCount: number
}>()

const emit = defineEmits<{
  markAllRead: []
  refresh: []
}>()
</script>

<template>
  <div class="flex flex-wrap items-center justify-between gap-3 rounded-lg border bg-background px-4 py-3">
    <div class="flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
      <span class="font-semibold text-foreground">{{ props.unreadCount }} unread</span>
      <span>{{ props.taskCount }} open tasks</span>
    </div>

    <div class="flex items-center gap-2">
      <Button
        :disabled="props.pending"
        size="sm"
        type="button"
        variant="outline"
        aria-label="Refresh notifications"
        @click="emit('refresh')"
      >
        <RefreshCwIcon class="size-4" />
        Refresh
      </Button>
      <Button
        :disabled="props.batchPending || props.markReadPending || props.pending || props.unreadCount === 0"
        size="sm"
        type="button"
        aria-label="Mark all unread notifications read"
        @click="emit('markAllRead')"
      >
        <CheckCheckIcon class="size-4" />
        Mark all read
      </Button>
    </div>
  </div>
</template>
