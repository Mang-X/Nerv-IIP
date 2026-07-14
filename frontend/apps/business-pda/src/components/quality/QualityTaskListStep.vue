<script setup lang="ts">
import RetryableListError from '@/components/RetryableListError.vue'
import { useNowClock } from '@/composables/useNowClock'
import type { BusinessConsoleQualityInspectionTaskItem } from '@nerv-iip/api-client'
import { INSPECTION_TASK_SOURCE_TYPES, inspectionTaskSourceTypeLabel } from '@nerv-iip/business-core'
import { NvListRow, NvMobileButton, NvMobileTag, NvScanBar } from '@nerv-iip/ui-mobile'
import { computed, shallowRef } from 'vue'

type Task = BusinessConsoleQualityInspectionTaskItem

const props = defineProps<{
  tasks: Task[]
  total: number
  loaded: number
  hasMore: boolean
  pending: boolean
  error: unknown
  /** 加载全部待检任务并返回最新集合（扫码跨页直达用）。 */
  loadAll?: () => Promise<Task[]>
}>()
const emit = defineEmits<{ select: [task: Task]; loadMore: []; refresh: [] }>()

// 受控响应式时钟：任务在页面停留期间跨过 dueAtUtc，超期标记与排序会随时钟自动重算。
const now = useNowClock()

const scanKeyword = shallowRef('')
const sourceTypeFilter = shallowRef<string | null>(null)

function isOverdue(task: Task) {
  if (!task.dueAtUtc) return false
  const due = new Date(task.dueAtUtc).getTime()
  return Number.isFinite(due) && due < now.value
}

function matchesKeyword(task: Task, kw: string) {
  return [task.skuCode, task.sourceDocumentId, task.batchNo, task.serialNo].some((v) =>
    (v ?? '').toLowerCase().includes(kw),
  )
}

const scanFiltered = computed(() => {
  const kw = scanKeyword.value.trim().toLowerCase()
  if (!kw) return props.tasks
  return props.tasks.filter((t) => matchesKeyword(t, kw))
})

const filteredTasks = computed(() =>
  scanFiltered.value.filter(
    (t) => sourceTypeFilter.value === null || t.sourceType === sourceTypeFilter.value,
  ),
)

// 超期置顶（按到期升序），其余按到期升序、无到期排最后。
const displayTasks = computed(() =>
  [...filteredTasks.value].sort((a, b) => {
    const overdueDiff = Number(isOverdue(b)) - Number(isOverdue(a))
    if (overdueDiff !== 0) return overdueDiff
    const da = a.dueAtUtc ? new Date(a.dueAtUtc).getTime() : Number.POSITIVE_INFINITY
    const db = b.dueAtUtc ? new Date(b.dueAtUtc).getTime() : Number.POSITIVE_INFINITY
    return da - db
  }),
)

const sourceChips = computed(() =>
  INSPECTION_TASK_SOURCE_TYPES.map((type) => ({
    type,
    label: inspectionTaskSourceTypeLabel(type),
    count: scanFiltered.value.filter((t) => t.sourceType === type).length,
  })),
)

// 优先来源单据 / SKU 精确命中，退而求关键字唯一命中；否则返回 null（仍走筛选）。
function pickScanHit(list: Task[], kw: string): Task | null {
  const exact = list.filter(
    (t) =>
      (t.sourceDocumentId ?? '').toLowerCase() === kw || (t.skuCode ?? '').toLowerCase() === kw,
  )
  const hits = exact.length > 0 ? exact : list.filter((t) => matchesKeyword(t, kw))
  return hits.length === 1 ? hits[0] : null
}

// 扫码直达：有未加载分页时**先取全量**再判定「全局唯一命中」才进入执行——否则首页的某个命中可能
// 抢在后续页的命中（或精确命中）之前被误选；无未加载分页则直接在当前集合判定。非唯一则退化为筛选。
async function onScan(value: string) {
  const kw = value.trim().toLowerCase()
  scanKeyword.value = value
  if (!kw) return
  const pool = props.hasMore && props.loadAll ? await props.loadAll() : props.tasks
  const hit = pickScanHit(pool, kw)
  if (hit) emit('select', hit)
}

function clearScan() {
  scanKeyword.value = ''
}
function pickSourceType(type: string | null) {
  sourceTypeFilter.value = type
}

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
  <div class="space-y-4 p-4">
    <NvScanBar placeholder="扫来源单据 / SKU 直达" @scan="onScan" />

    <div
      v-if="scanKeyword"
      class="flex items-center justify-between rounded-lg border border-border bg-card px-4 py-2 text-sm"
    >
      <span class="truncate text-foreground">筛选：{{ scanKeyword }}</span>
      <NvMobileButton variant="outline" size="sm" data-testid="clear-scan" @click="clearScan">
        清除
      </NvMobileButton>
    </div>

    <div class="flex flex-wrap gap-2">
      <NvMobileButton
        :variant="sourceTypeFilter === null ? 'primary' : 'outline'"
        size="sm"
        data-testid="chip-all"
        @click="pickSourceType(null)"
      >
        全部
      </NvMobileButton>
      <NvMobileButton
        v-for="chip in sourceChips"
        :key="chip.type"
        :variant="sourceTypeFilter === chip.type ? 'primary' : 'outline'"
        size="sm"
        :data-testid="`chip-${chip.type}`"
        @click="pickSourceType(chip.type)"
      >
        {{ chip.label }} {{ chip.count }}
      </NvMobileButton>
    </div>

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
      v-else-if="tasks.length === 0"
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
  </div>
</template>
