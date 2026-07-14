<script setup lang="ts">
import RetryableListError from '@/components/RetryableListError.vue'
import type { BusinessConsoleQualityInspectionTaskItem } from '@nerv-iip/api-client'
import { inspectionTaskSourceTypeLabel } from '@nerv-iip/business-core'
import { NvListRow, NvMobileButton, NvMobileTag } from '@nerv-iip/ui-mobile'

type Task = BusinessConsoleQualityInspectionTaskItem

defineProps<{
  /** 已按筛选/排序处理后的展示集合。 */
  displayTasks: Task[]
  /** 原始已加载集合大小（区分「没有任务」与「筛选未命中」）。 */
  rawCount: number
  total: number
  loaded: number
  hasMore: boolean
  pending: boolean
  error: unknown
  /** 超期判定（容器持有响应式时钟）。 */
  isOverdue: (task: Task) => boolean
}>()
const emit = defineEmits<{ select: [task: Task]; loadMore: []; refresh: [] }>()

function taskTitle(task: Task) {
  return `${inspectionTaskSourceTypeLabel(task.sourceType)} · ${task.skuCode ?? '未知物料'}`
}
function taskSubtitle(task: Task) {
  const parts: string[] = []
  if (task.sourceDocumentId) parts.push(`来源单 ${task.sourceDocumentId}`)
  if (task.quantity != null) parts.push(`数量 ${task.quantity}${task.uomCode ?? ''}`)
  if (task.batchNo) parts.push(`批次 ${task.batchNo}`)
  return parts.join(' · ')
}
function dueText(iso?: string) {
  if (!iso) return ''
  const d = new Date(iso)
  return Number.isNaN(d.getTime()) ? '' : d.toLocaleString('zh-CN')
}
</script>

<template>
  <!-- 列表状态（error/loading/empty/partial）与任务行渲染 -->
  <RetryableListError
    v-if="error"
    :error="error"
    :pending="pending"
    fallback="待检任务加载失败，请稍后重试。"
    test-id="tasks-error"
    @retry="() => emit('refresh')"
  />

  <div v-else-if="pending" class="px-4 py-6 text-center text-sm text-muted-foreground">
    加载中…
  </div>

  <div
    v-else-if="rawCount === 0"
    class="rounded-lg border border-dashed border-border bg-card px-4 py-8 text-center text-sm text-muted-foreground"
  >
    暂无待检任务
  </div>

  <div
    v-else-if="displayTasks.length === 0 && hasMore"
    data-testid="tasks-partial-no-match"
    class="space-y-3 rounded-lg border border-dashed border-border bg-card px-4 py-8 text-center text-sm text-muted-foreground"
  >
    <p>在已加载的 {{ loaded }} 条待检任务中未匹配（共 {{ total }} 条）。</p>
    <NvMobileButton
      variant="outline"
      block
      data-testid="load-more"
      :disabled="pending"
      @click="emit('loadMore')"
    >
      加载更多
    </NvMobileButton>
  </div>

  <div
    v-else-if="displayTasks.length === 0"
    class="rounded-lg border border-dashed border-border bg-card px-4 py-8 text-center text-sm text-muted-foreground"
  >
    未找到匹配的待检任务
  </div>

  <div v-else class="overflow-hidden rounded-lg border border-border">
    <NvListRow
      v-for="task in displayTasks"
      :key="task.inspectionTaskId"
      data-testid="task-row"
      :title="taskTitle(task)"
      :subtitle="taskSubtitle(task)"
      @select="emit('select', task)"
    >
      <template #trailing>
        <div class="flex shrink-0 flex-col items-end gap-1">
          <NvMobileTag
            v-if="isOverdue(task)"
            :data-testid="`overdue-${task.inspectionTaskId}`"
            variant="danger"
          >
            超期
          </NvMobileTag>
          <span v-if="task.dueAtUtc" class="text-xs text-muted-foreground">
            {{ dueText(task.dueAtUtc) }}
          </span>
        </div>
      </template>
    </NvListRow>
  </div>
</template>
