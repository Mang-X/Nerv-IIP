<script setup lang="ts">
import type { BusinessConsoleMesOperationTaskRow, BusinessConsoleResourceItem } from '@nerv-iip/api-client'
import type { DataTableColumn, DataTableSort } from '@nerv-iip/ui'
import { useBusinessMasterDataResources } from '@/composables/useBusinessMasterData'
import { describeMesReadinessReason, useMesOperationTasks } from '@/composables/useBusinessMes'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Button,
  DataTable,
  DataTablePagination,
  DropdownMenuItem,
  DropdownMenuSeparator,
  PageHeader,
  RowActions,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
  StatusBadge,
  Toolbar,
} from '@nerv-iip/ui'
import { watchDebounced } from '@vueuse/core'
import { ClipboardCheckIcon, EyeIcon, RefreshCwIcon, ShieldCheckIcon, WrenchIcon } from 'lucide-vue-next'
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'

definePage({ meta: { requiresAuth: true, title: '工序执行' } })

type Row = BusinessConsoleMesOperationTaskRow

const {
  filters,
  operationTasks,
  operationTasksError,
  operationTasksPending,
  operationTasksTotal,
  refreshOperationTasks,
} = useMesOperationTasks()

const router = useRouter()
const { resources: workCenterResources } = useBusinessMasterDataResources('work-center')
const { resources: shiftResources } = useBusinessMasterDataResources('shift')

// --- Filters (live) ---
const keyword = ref('')
const statusFilter = ref('all')
const workCenterFilter = ref('all')
const shiftFilter = ref('all')

watch(statusFilter, (value) => {
  filters.status = value === 'all' ? undefined : value
})
watchDebounced(keyword, (value) => {
  filters.keyword = value.trim() || undefined
}, { debounce: 300, maxWait: 1000 })
watch(workCenterFilter, (value) => {
  filters.workCenterId = value === 'all' ? undefined : value
})
watch(shiftFilter, (value) => {
  filters.shiftId = value === 'all' ? undefined : value
})

const statusOptions = [
  { label: '全部状态', value: 'all' },
  { label: '可开工', value: 'Ready' },
  { label: '执行中', value: 'Running' },
  { label: '暂停', value: 'Paused' },
  { label: '已完成', value: 'Completed' },
  { label: '阻塞', value: 'Blocked' },
]
const workCenterOptions = computed(() => toResourceOptions(workCenterResources.value))
const shiftOptions = computed(() => toResourceOptions(shiftResources.value))

const visibleTasks = computed(() => operationTasks.value)

// 本页可开工 / 执行中的工序条数，用于队列上方一句话提示「这一页里有几道工序现在能动手报工」。
// 不做成 SectionCards 计数卡：那是分页页内的局部数，按 playbook §0 属机械计数，会冒充总量误导一线。
const reportableOnPage = computed(
  () => visibleTasks.value.filter((t) => isReportableStatus(t.status)).length,
)

// --- Sort (page-owned, before pagination) ---
const sort = ref<DataTableSort | null>(null)
function sortValue(task: Row, key: string): string | number {
  if (key === 'operationSequence') return task.operationSequence ?? 0
  if (key === 'plannedStartUtc') return task.plannedStartUtc ? new Date(task.plannedStartUtc).getTime() : 0
  return (task[key as keyof Row] as string | null) ?? ''
}
const sortedTasks = computed(() => {
  if (!sort.value) return visibleTasks.value
  const { key, direction } = sort.value
  const factor = direction === 'asc' ? 1 : -1
  return [...visibleTasks.value].sort((a, b) => {
    const av = sortValue(a, key)
    const bv = sortValue(b, key)
    if (typeof av === 'number' && typeof bv === 'number') return (av - bv) * factor
    return String(av).localeCompare(String(bv), 'zh-Hans-CN') * factor
  })
})

// --- Pagination ---
const { page, pageSize } = usePagedList(filters, { resetOn: [keyword, statusFilter, workCenterFilter, shiftFilter] })
const pagedTasks = computed(() => sortedTasks.value)

// TODO(#420): operationTaskId / workOrderId / workCenterId / deviceAssetId / shiftId 均为后端 GUID，
// facade 暂不回工序号、工单号、工作中心/设备/班次名称。本页以「工序序号」(operationSequence) 作人读锚点
// （现场识别一道工序的自然编号），其余 GUID 列降级为占位/标签，不把裸 GUID 当人读标识。
const columns: DataTableColumn<Row>[] = [
  { key: 'operationSequence', header: '工序', cellClass: 'font-medium', align: 'start', width: 'w-20', accessor: (r) => r.operationSequence ?? 0 },
  { key: 'workOrderId', header: '工单' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'workCenterId', header: '工作中心' },
  { key: 'shiftId', header: '班次' },
  { key: 'plannedStartUtc', header: '计划开始', accessor: (r) => (r.plannedStartUtc ? new Date(r.plannedStartUtc).getTime() : 0) },
  { key: 'qualityStatus', header: '质量状态' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-24' },
]

function rowKey(task: Row) {
  return task.operationTaskId ?? `${task.workOrderId}-${task.operationSequence}`
}

const errorMessage = computed(() => formatError(operationTasksError.value))

function resetFilters() {
  keyword.value = ''
  statusFilter.value = 'all'
  workCenterFilter.value = 'all'
  shiftFilter.value = 'all'
}

function openWorkOrder(workOrderId?: string | null) {
  if (!workOrderId) return
  void router.push({ path: `/mes/work-orders/${encodeURIComponent(workOrderId)}` })
}
function openRoute(path: string, task: Row) {
  void router.push({
    path,
    query: {
      operationTaskId: task.operationTaskId ?? undefined,
      workOrderId: task.workOrderId ?? undefined,
      workCenterId: task.workCenterId ?? undefined,
    },
  })
}
function canOpenReport(task: Row) {
  return Boolean(task.workOrderId && task.operationTaskId)
}
// 可开工 / 执行中的工序才是一线现在能动手报工的；据此把行尾报工入口直接显出来，不必再翻下拉。
function isReportableStatus(status?: string | null) {
  return ['Ready', 'Running', 'Started', 'InProgress'].includes(status ?? '')
}
// 行尾是否直显「报工」按钮：状态能报工 且 工单/工序上下文齐全（跳转报工表单要带这两个 id）。
function showReportButton(task: Row) {
  return isReportableStatus(task.status) && canOpenReport(task)
}
function formatDateTime(value?: string | null) {
  if (!value) return '无'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}
function readiness(value?: string | null) {
  return describeMesReadinessReason(value ?? '未检')
}
function readinessNeedsAction(value?: string | null) {
  return ['QUALITY_PLAN_MISSING', 'QUALITY_HOLD_ACTIVE', 'EQUIPMENT_UNAVAILABLE', 'EQUIPMENT_MAINTENANCE_CONFLICT']
    .includes(describeMesReadinessReason(value ?? '').code)
}
function toResourceOptions(items: BusinessConsoleResourceItem[]) {
  return items
    .filter((item) => item.active !== false && item.code)
    .map((item) => ({ label: item.displayName ? `${item.displayName} (${item.code})` : item.code!, value: item.code! }))
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
</script>

<template>
  <BusinessLayout>
    <PageHeader
      title="工序执行"
      :breadcrumbs="[{ label: '制造执行' }]"
      :count="`${operationTasksTotal} 个工序任务`"
    >
      <template #actions>
        <Button size="sm" type="button" variant="outline" :disabled="operationTasksPending" @click="refreshOperationTasks">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
      </template>
    </PageHeader>

    <p class="text-sm text-muted-foreground">
      这里是分到本班的<span class="font-medium text-foreground">工序任务队列</span>。先用状态/工作中心/班次筛出眼下要做的工序，再从行尾直接
      <span class="font-medium text-foreground">报工</span>；遇到要送检点<span class="font-medium text-foreground">呼叫质检</span>、设备或物料卡住点<span class="font-medium text-foreground">记录异常</span>。
    </p>

    <Toolbar v-model:search="keyword" search-placeholder="搜索任务、工单、设备">
      <template #filters>
        <Select v-model="statusFilter">
          <SelectTrigger class="h-9 w-32" aria-label="工序状态"><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem v-for="option in statusOptions" :key="option.value" :value="option.value">{{ option.label }}</SelectItem>
          </SelectContent>
        </Select>
        <Select v-model="workCenterFilter">
          <SelectTrigger class="h-9 w-40" aria-label="工作中心"><SelectValue placeholder="全部工作中心" /></SelectTrigger>
          <SelectContent>
            <SelectItem value="all">全部工作中心</SelectItem>
            <SelectItem v-for="option in workCenterOptions" :key="option.value" :value="option.value">{{ option.label }}</SelectItem>
          </SelectContent>
        </Select>
        <Select v-model="shiftFilter">
          <SelectTrigger class="h-9 w-32" aria-label="班次"><SelectValue placeholder="全部班次" /></SelectTrigger>
          <SelectContent>
            <SelectItem value="all">全部班次</SelectItem>
            <SelectItem v-for="option in shiftOptions" :key="option.value" :value="option.value">{{ option.label }}</SelectItem>
          </SelectContent>
        </Select>
      </template>
      <template #actions>
        <Button type="button" variant="ghost" size="sm" @click="resetFilters">重置</Button>
      </template>
    </Toolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <p v-if="!operationTasksPending && visibleTasks.length" class="text-sm text-muted-foreground" aria-live="polite">
      本页 <span class="font-medium text-foreground">{{ reportableOnPage }}</span> 道工序现在能动手，行尾「报工」直接登记产量。
    </p>

    <DataTable
      v-model:sort="sort"
      :columns="columns"
      :rows="pagedTasks"
      :row-key="rowKey"
      :client-sort="false"
      :loading="operationTasksPending"
      empty-message="当前没有工序任务。确认工单已释放、排程已生成后，可开工任务会出现在这里。"
    >
      <template #cell-operationSequence="{ row }">
        <span class="tabular-nums">工序 {{ row.operationSequence ?? '—' }}</span>
      </template>
      <!-- TODO(#420): workOrderId 为后端 GUID，facade 暂不回工单号；不显裸 GUID，以「查看工单」按钮承载跳转，单元格只给标签。 -->
      <template #cell-workOrderId="{ row }">
        <button
          v-if="row.workOrderId"
          type="button"
          class="text-brand underline-offset-4 hover:underline"
          @click="openWorkOrder(row.workOrderId)"
        >
          查看工单
        </button>
        <span v-else class="text-muted-foreground">未关联工单</span>
      </template>
      <template #cell-status="{ row }">
        <StatusBadge :value="row.status" />
      </template>
      <!-- TODO(#420): workCenterId 为后端 GUID，facade 暂不回工作中心名称；用上方「工作中心」筛选按名称定位，单元格不显裸 GUID。 -->
      <template #cell-workCenterId="{ row }">
        <span class="text-muted-foreground">{{ row.workCenterId ? '名称待接入' : '未指定' }}</span>
      </template>
      <!-- TODO(#420): shiftId 为后端 GUID，facade 暂不回班次名称；用上方「班次」筛选按名称定位，单元格不显裸 GUID。 -->
      <template #cell-shiftId="{ row }">
        <span class="text-muted-foreground">{{ row.shiftId ? '名称待接入' : '未指定' }}</span>
      </template>
      <template #cell-plannedStartUtc="{ row }">
        {{ formatDateTime(row.plannedStartUtc) }}
      </template>
      <template #cell-qualityStatus="{ row }">
        <div class="grid gap-0.5">
          <span>{{ readiness(row.qualityStatus).label }}</span>
          <span v-if="readinessNeedsAction(row.qualityStatus)" class="text-xs text-muted-foreground">
            {{ readiness(row.qualityStatus).nextStep }}
          </span>
        </div>
      </template>
      <template #cell-actions="{ row }">
        <div class="flex items-center justify-end gap-1">
          <Button
            v-if="showReportButton(row)"
            size="sm"
            type="button"
            @click="openRoute('/mes/work-orders', row)"
          >
            <ClipboardCheckIcon aria-hidden="true" />
            报工
          </Button>
          <RowActions :label="`工序任务操作 工序 ${row.operationSequence ?? ''}`">
            <DropdownMenuItem
              v-if="!showReportButton(row)"
              :disabled="!canOpenReport(row)"
              @click="openRoute('/mes/work-orders', row)"
            >
              <ClipboardCheckIcon aria-hidden="true" />
              {{ canOpenReport(row) ? '报工' : '暂不可报工（缺工单）' }}
            </DropdownMenuItem>
            <DropdownMenuItem :disabled="!row.workOrderId" @click="openWorkOrder(row.workOrderId)">
              <EyeIcon aria-hidden="true" />
              查看工单
            </DropdownMenuItem>
            <DropdownMenuSeparator />
            <DropdownMenuItem @click="openRoute('/quality/inspections', row)">
              <ShieldCheckIcon aria-hidden="true" />
              呼叫质检
            </DropdownMenuItem>
            <DropdownMenuItem @click="openRoute('/mes/downtime', row)">
              <WrenchIcon aria-hidden="true" />
              记录异常
            </DropdownMenuItem>
          </RowActions>
        </div>
      </template>
    </DataTable>

    <DataTablePagination
      v-model:page="page"
      v-model:page-size="pageSize"
      :total-items="operationTasksTotal"
    />
  </BusinessLayout>
</template>
