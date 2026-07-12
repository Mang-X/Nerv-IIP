<script setup lang="ts">
import {
  Sheet,
  SheetContent,
  SheetDescription,
  SheetHeader,
  SheetTitle,
  NvStatusBadge,
} from '@nerv-iip/ui'
import { computed } from 'vue'
import { conflictReasonLabel } from '../../model/labels'
import type { ScheduleTask } from '../../model/types'

const props = defineProps<{ task?: ScheduleTask; open: boolean }>()
const emit = defineEmits<{ 'update:open': [value: boolean] }>()

const isOrder = computed(() => props.task?.type === 'order')

function fmt(iso?: string) {
  if (!iso) return '—'
  const d = new Date(iso)
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleString('zh-CN', { hour12: false })
}
</script>

<template>
  <Sheet :open="open" @update:open="emit('update:open', $event)">
    <SheetContent class="w-[380px] sm:max-w-[380px]">
      <SheetHeader>
        <SheetTitle>{{ isOrder ? '工单' : '工序' }}详情</SheetTitle>
        <SheetDescription>查看排程明细与状态。</SheetDescription>
      </SheetHeader>
      <dl v-if="task" class="grid grid-cols-[88px_1fr] gap-x-3 gap-y-3 px-4 py-2 text-sm">
        <dt class="text-muted-foreground">工单</dt>
        <dd class="text-foreground">{{ task.orderId || '—' }}</dd>
        <template v-if="!isOrder">
          <dt class="text-muted-foreground">工序</dt>
          <dd class="text-foreground">{{ task.text || '—' }}</dd>
          <dt class="text-muted-foreground">资源</dt>
          <dd class="text-foreground">{{ task.resourceId || '—' }}</dd>
        </template>
        <dt class="text-muted-foreground">开始</dt>
        <dd class="text-foreground">{{ fmt(task.startUtc) }}</dd>
        <dt class="text-muted-foreground">结束</dt>
        <dd class="text-foreground">{{ fmt(task.endUtc) }}</dd>
        <template v-if="!isOrder">
          <dt class="text-muted-foreground">锁定</dt>
          <dd>
            <NvStatusBadge :tone="task.locked ? 'info' : 'neutral'" :label="task.locked ? '已锁定' : '未锁定'" />
          </dd>
        </template>
        <template v-if="task.hasConflict && task.conflictReason">
          <dt class="text-muted-foreground">冲突</dt>
          <dd><NvStatusBadge tone="danger" :label="conflictReasonLabel[task.conflictReason]" /></dd>
        </template>
      </dl>
    </SheetContent>
  </Sheet>
</template>
