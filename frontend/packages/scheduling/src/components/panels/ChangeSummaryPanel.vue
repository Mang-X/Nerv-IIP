<script setup lang="ts">
import { Empty, EmptyDescription, EmptyTitle, ScrollArea, StatusBadge } from '@nerv-iip/ui'
import { changeTone, changeTypeLabel } from '../../model/labels'
import type { ScheduleChange } from '../../model/types'

defineProps<{ changes: ScheduleChange[] }>()
const emit = defineEmits<{ select: [taskId: string] }>()

function onClick(c: ScheduleChange) {
  if (c.taskId) emit('select', c.taskId)
}
</script>

<template>
  <section class="flex h-full flex-col" aria-label="变更摘要">
    <header class="flex items-center justify-between px-3 py-2">
      <h3 class="text-sm font-semibold text-foreground">变更摘要</h3>
      <span class="text-xs text-muted-foreground">{{ changes.length }} 项</span>
    </header>
    <Empty v-if="!changes.length" class="py-8">
      <EmptyTitle>暂无变更</EmptyTitle>
      <EmptyDescription>重新排程后,这里会列出移动、延后或受阻的工序。</EmptyDescription>
    </Empty>
    <ScrollArea v-else class="flex-1">
      <ul class="flex flex-col gap-1 p-2">
        <li v-for="(c, i) in changes" :key="`${c.orderId}:${c.operationId}:${i}`">
          <button
            type="button"
            :data-change-task="c.taskId"
            class="flex w-full items-center gap-2 rounded-md border border-border bg-card px-3 py-2 text-left transition-colors hover:bg-accent"
            @click="onClick(c)"
          >
            <StatusBadge :tone="changeTone[c.changeType]" :label="changeTypeLabel[c.changeType]" />
            <span class="flex-1 truncate text-sm text-foreground">{{ c.message }}</span>
            <span class="text-xs text-muted-foreground">{{ c.orderId }}</span>
          </button>
        </li>
      </ul>
    </ScrollArea>
  </section>
</template>
