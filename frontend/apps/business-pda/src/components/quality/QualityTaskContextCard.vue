<script setup lang="ts">
import { useNowClock } from '@/composables/useNowClock'
import type { BusinessConsoleQualityInspectionTaskItem } from '@nerv-iip/api-client'
import { inspectionTaskSourceTypeLabel } from '@nerv-iip/business-core'
import { NvMobileTag } from '@nerv-iip/ui-mobile'

const props = defineProps<{
  task: BusinessConsoleQualityInspectionTaskItem
  planLabel: string
}>()

// 受控响应式时钟：停留执行页跨过 dueAtUtc 时超期标记自动重算（不再依赖非响应式 Date.now()）。
const now = useNowClock()

function isOverdue() {
  if (!props.task.dueAtUtc) return false
  const due = new Date(props.task.dueAtUtc).getTime()
  return Number.isFinite(due) && due < now.value
}
function dueText(iso?: string) {
  if (!iso) return ''
  const d = new Date(iso)
  return Number.isNaN(d.getTime()) ? '' : d.toLocaleString('zh-CN')
}
</script>

<template>
  <!-- 任务上下文（常显，防错检）-->
  <section class="space-y-1 rounded-lg border border-border bg-card p-4" data-testid="task-context">
    <div class="flex items-center gap-2">
      <NvMobileTag variant="default">
        {{ inspectionTaskSourceTypeLabel(task.sourceType) }}
      </NvMobileTag>
      <NvMobileTag v-if="isOverdue()" variant="danger">超期</NvMobileTag>
    </div>
    <p class="text-base font-semibold text-foreground">{{ task.skuCode ?? '未知物料' }}</p>
    <dl class="space-y-1 text-sm">
      <div v-if="task.sourceDocumentId" class="flex items-baseline justify-between gap-4">
        <dt class="shrink-0 whitespace-nowrap text-muted-foreground">来源单据</dt>
        <dd class="min-w-0 truncate text-right text-foreground">{{ task.sourceDocumentId }}</dd>
      </div>
      <div v-if="task.quantity != null" class="flex items-baseline justify-between gap-4">
        <dt class="shrink-0 whitespace-nowrap text-muted-foreground">数量</dt>
        <dd class="min-w-0 truncate text-right text-foreground">
          {{ task.quantity }}{{ task.uomCode ?? '' }}
        </dd>
      </div>
      <div v-if="planLabel" class="flex items-baseline justify-between gap-4">
        <dt class="shrink-0 whitespace-nowrap text-muted-foreground">检验计划</dt>
        <dd class="min-w-0 truncate text-right text-foreground">{{ planLabel }}</dd>
      </div>
      <div v-if="task.batchNo" class="flex items-baseline justify-between gap-4">
        <dt class="shrink-0 whitespace-nowrap text-muted-foreground">批次</dt>
        <dd class="min-w-0 truncate text-right text-foreground">{{ task.batchNo }}</dd>
      </div>
      <div v-if="task.serialNo" class="flex items-baseline justify-between gap-4">
        <dt class="shrink-0 whitespace-nowrap text-muted-foreground">序列号</dt>
        <dd class="min-w-0 truncate text-right text-foreground">{{ task.serialNo }}</dd>
      </div>
      <div v-if="task.dueAtUtc" class="flex items-baseline justify-between gap-4">
        <dt class="shrink-0 whitespace-nowrap text-muted-foreground">应检至</dt>
        <dd
          class="min-w-0 truncate text-right text-foreground"
          :class="isOverdue() ? 'text-destructive' : undefined"
        >
          {{ dueText(task.dueAtUtc) }}
        </dd>
      </div>
    </dl>
  </section>
</template>
