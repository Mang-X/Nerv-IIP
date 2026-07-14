<script setup lang="ts">
import QualityTaskListView from '@/components/quality/QualityTaskListView.vue'
import QualityTaskScanFilter from '@/components/quality/QualityTaskScanFilter.vue'
import { useNowClock } from '@/composables/useNowClock'
import type { BusinessConsoleQualityInspectionTaskItem } from '@nerv-iip/api-client'
import { INSPECTION_TASK_SOURCE_TYPES, inspectionTaskSourceTypeLabel } from '@nerv-iip/business-core'
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
</script>

<template>
  <!-- 数据协调（时钟/筛选/排序/扫码直达）+ 子组件组合。 -->
  <div class="space-y-4 p-4">
    <QualityTaskScanFilter
      :scan-keyword="scanKeyword"
      :source-type-filter="sourceTypeFilter"
      :chips="sourceChips"
      @scan="onScan"
      @clear-scan="scanKeyword = ''"
      @pick-source-type="(type) => (sourceTypeFilter = type)"
    />

    <QualityTaskListView
      :display-tasks="displayTasks"
      :raw-count="tasks.length"
      :total="total"
      :loaded="loaded"
      :has-more="hasMore"
      :pending="pending"
      :error="error"
      :is-overdue="isOverdue"
      @select="(task) => emit('select', task)"
      @load-more="emit('loadMore')"
      @refresh="emit('refresh')"
    />
  </div>
</template>
