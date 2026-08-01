<script setup lang="ts">
import QualityTaskListView from '@/components/quality/QualityTaskListView.vue'
import QualityTaskScanFilter from '@/components/quality/QualityTaskScanFilter.vue'
import TaskListShell from '@/components/task-list/TaskListShell.vue'
import { useNowClock } from '@/composables/useNowClock'
import type { BusinessConsoleQualityInspectionTaskItem } from '@nerv-iip/api-client'
import {
  INSPECTION_TASK_SOURCE_TYPES,
  inspectionTaskSourceTypeLabel,
} from '@nerv-iip/business-core'
import {
  NvMobileDropdownMenu,
  NvMobileDropdownMenuItem,
  type DropdownOption,
} from '@nerv-iip/ui-mobile'
import { computed, shallowRef, watch } from 'vue'

type Task = BusinessConsoleQualityInspectionTaskItem

const props = withDefaults(
  defineProps<{
    tasks: Task[]
    total: number
    loaded: number
    hasMore: boolean
    pending: boolean
    refreshing?: boolean
    error: unknown
    scope?: string
    scopeReady?: boolean
    updatedAt?: string | null
    hasSuccessfulResponse?: boolean
    hasFailedResponse?: boolean
    status?: string
    keyword?: string
    sourceType?: string
    overdue?: boolean
    loadingMore?: boolean
    loadMoreError?: unknown
    /** 加载全部待检任务并返回最新集合（扫码跨页直达用）。 */
    loadAll?: () => Promise<Task[]>
  }>(),
  {
    scope: undefined,
    scopeReady: true,
    updatedAt: null,
    hasSuccessfulResponse: false,
    hasFailedResponse: false,
    status: 'pending',
    keyword: '',
    sourceType: undefined,
    overdue: undefined,
    loadingMore: false,
    refreshing: false,
    loadMoreError: undefined,
    loadAll: undefined,
  },
)
const emit = defineEmits<{
  select: [task: Task]
  loadMore: []
  refresh: []
  'update:status': [value: string]
  'update:keyword': [value: string | undefined]
  'update:sourceType': [value: string | undefined]
  'update:overdue': [value: boolean | undefined]
}>()

// 受控响应式时钟：任务在页面停留期间跨过 dueAtUtc，超期标记与排序会随时钟自动重算。
const now = useNowClock()

const scanKeyword = shallowRef(props.keyword)
watch(
  () => props.keyword,
  (value) => (scanKeyword.value = value),
)
const sourceTypeFilter = computed<string | null>({
  get: () => props.sourceType ?? null,
  set: (value) => emit('update:sourceType', value ?? undefined),
})
const statusModel = computed<string | number>({
  get: () => props.status,
  set: (value) => emit('update:status', String(value)),
})
const overdueModel = computed<string | number>({
  get: () => (props.overdue === true ? 'overdue' : 'all'),
  set: (value) => emit('update:overdue', value === 'overdue' ? true : undefined),
})
const statusOptions: DropdownOption[] = [
  { label: '待领取', value: 'pending' },
  { label: '进行中', value: 'in-progress' },
]
const overdueOptions: DropdownOption[] = [
  { label: '全部时效', value: 'all' },
  { label: '仅看超期', value: 'overdue' },
]
const qualitySource = computed(() => {
  const status = props.status === 'in-progress' ? '进行中' : '待检'
  return `质检待检任务服务（当前账号 Self 范围，状态：${status}）`
})
const listError = computed(
  () =>
    props.error ??
    (props.hasFailedResponse ? new Error('质检待检任务服务未成功返回，请刷新重试。') : null),
)

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
const showEmpty = computed(
  () =>
    !props.pending &&
    !listError.value &&
    props.hasSuccessfulResponse &&
    filteredTasks.value.length === 0,
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
  emit('update:keyword', value.trim() || undefined)
  if (!kw) return
  const pool = props.hasMore && props.loadAll ? await props.loadAll() : props.tasks
  const hit = pickScanHit(pool, kw)
  if (hit) emit('select', hit)
}

function clearKeyword() {
  scanKeyword.value = ''
  emit('update:keyword', undefined)
}

function restoreState(state: { filters: Record<string, unknown> }) {
  const { status, keyword, sourceType, overdue } = state.filters
  if (typeof status === 'string') emit('update:status', status)
  emit('update:keyword', typeof keyword === 'string' && keyword ? keyword : undefined)
  emit('update:sourceType', typeof sourceType === 'string' && sourceType ? sourceType : undefined)
  emit('update:overdue', overdue === true ? true : undefined)
}
</script>

<template>
  <TaskListShell
    state-key="quality-inspection-tasks"
    :scope="props.scope ?? '组织/环境范围未就绪'"
    :source="qualitySource"
    :loaded="props.loaded"
    :total="props.total"
    :has-more="props.hasMore"
    :updated-at="props.updatedAt"
    :pending="props.pending"
    :refreshing="props.refreshing"
    :loading-more="props.loadingMore"
    :error="listError"
    :load-more-error="props.loadMoreError"
    error-test-id="tasks-error"
    failure-explanation="质检待检任务服务未成功返回，请刷新重试。"
    :filter-state="{ status, keyword, sourceType, overdue }"
    empty-description="当前账号没有符合筛选条件的质检任务；缺少登录主体或组织环境时不会发起查询。"
    @refresh="emit('refresh')"
    @retry="emit('refresh')"
    @load-more="emit('loadMore')"
    @retry-load-more="emit('loadMore')"
    @restore="restoreState"
  >
    <template #filters>
      <div class="space-y-3 px-4 py-3">
        <QualityTaskScanFilter
          :scan-keyword="scanKeyword"
          :source-type-filter="sourceTypeFilter"
          :chips="sourceChips"
          @scan="onScan"
          @clear-scan="clearKeyword"
          @pick-source-type="(type) => (sourceTypeFilter = type)"
        />
        <NvMobileDropdownMenu>
          <NvMobileDropdownMenuItem
            v-model="statusModel"
            title="任务状态"
            :options="statusOptions"
          />
          <NvMobileDropdownMenuItem
            v-model="overdueModel"
            title="时效范围"
            :options="overdueOptions"
          />
        </NvMobileDropdownMenu>
      </div>
    </template>

    <div class="space-y-4 p-4">
      <QualityTaskListView
        :display-tasks="displayTasks"
        :raw-count="tasks.length"
        :total="total"
        :loaded="loaded"
        :has-more="hasMore"
        :pending="pending"
        :error="undefined"
        :is-overdue="isOverdue"
        @select="(task) => emit('select', task)"
        @load-more="emit('loadMore')"
        @refresh="emit('refresh')"
      />
    </div>
  </TaskListShell>
</template>
