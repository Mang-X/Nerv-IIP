<script setup lang="ts">
import {
  Empty,
  EmptyDescription,
  EmptyTitle,
  NvInput,
  NvStatusBadge,
  ScrollArea,
} from '@nerv-iip/ui'
import { computed, shallowRef } from 'vue'
import { changeTone, changeTypeLabel } from '../../model/labels'
import type { ScheduleChange, ScheduleTask } from '../../model/types'

const props = defineProps<{
  changes: ScheduleChange[]
  baseTasks?: ScheduleTask[]
  tasks?: ScheduleTask[]
}>()
const orderFilter = shallowRef('')
const filteredChanges = computed(() => {
  const query = orderFilter.value.trim().toLocaleLowerCase()
  return props.changes
    .filter((change) => change.orderId.toLocaleLowerCase().includes(query))
    .map((change) => ({
      ...change,
      before: props.baseTasks?.find(
        (task) => task.orderId === change.orderId && task.operationId === change.operationId,
      ),
      after: props.tasks?.find(
        (task) => task.orderId === change.orderId && task.operationId === change.operationId,
      ),
    }))
})
function formatTime(value: string) {
  return new Date(value).toLocaleString('zh-CN', {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    hour12: false,
  })
}
const emit = defineEmits<{ select: [taskId: string] }>()

function onClick(c: ScheduleChange) {
  if (c.taskId) emit('select', c.taskId)
}
</script>

<template>
  <section class="flex h-full flex-col" aria-label="变更摘要">
    <header class="flex items-center justify-between px-3 py-2">
      <h3 class="text-sm font-semibold text-foreground">变更摘要</h3>
      <span class="text-xs text-muted-foreground"
        >{{ filteredChanges.length }} / {{ changes.length }} 项</span
      >
    </header>
    <NvInput
      v-model="orderFilter"
      aria-label="筛选工单"
      placeholder="输入工单号筛选"
      class="mx-3 mb-2 w-auto"
    />
    <Empty v-if="!changes.length" class="py-8">
      <EmptyTitle>暂无变更</EmptyTitle>
      <EmptyDescription>重新排程后,这里会列出移动、延后或受阻的工序。</EmptyDescription>
    </Empty>
    <Empty v-else-if="!filteredChanges.length" class="py-8">
      <EmptyTitle>没有匹配的工单变更</EmptyTitle>
      <EmptyDescription>调整或清空工单筛选后查看。</EmptyDescription>
    </Empty>
    <ScrollArea v-else class="flex-1">
      <ul class="flex flex-col gap-1 p-2">
        <li
          v-for="(c, i) in filteredChanges"
          data-change-row
          :key="`${c.orderId}:${c.operationId}:${i}`"
        >
          <button
            type="button"
            :data-change-task="c.taskId"
            class="flex w-full flex-col gap-2 rounded-md border border-border bg-card px-3 py-2 text-left transition-colors hover:bg-accent focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
            @click="onClick(c)"
          >
            <span class="flex w-full flex-wrap items-center gap-2">
              <NvStatusBadge
                :tone="changeTone[c.changeType]"
                :label="changeTypeLabel[c.changeType]"
              />
              <span class="flex-1 text-sm font-medium text-foreground">{{ c.operationId }}</span>
              <span class="text-xs text-muted-foreground">{{ c.orderId }}</span>
            </span>
            <span v-if="tasks" class="grid gap-1 text-xs text-muted-foreground">
              <span
                >资源：{{ c.before?.resourceId ?? '未排' }} →
                {{ c.after?.resourceId ?? '未排' }}</span
              >
              <span>
                原时间：<template v-if="c.before"
                  ><time :datetime="c.before.startUtc">{{ formatTime(c.before.startUtc) }}</time> ～
                  <time :datetime="c.before.endUtc">{{
                    formatTime(c.before.endUtc)
                  }}</time></template
                ><template v-else>未排</template>
              </span>
              <span>
                新时间：<template v-if="c.after"
                  ><time :datetime="c.after.startUtc">{{ formatTime(c.after.startUtc) }}</time> ～
                  <time :datetime="c.after.endUtc">{{ formatTime(c.after.endUtc) }}</time></template
                ><template v-else>未排</template>
              </span>
            </span>
            <span class="text-sm text-foreground">原因：{{ c.message }}</span>
          </button>
        </li>
      </ul>
    </ScrollArea>
  </section>
</template>
