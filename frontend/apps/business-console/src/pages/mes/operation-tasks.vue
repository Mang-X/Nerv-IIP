<script setup lang="ts">
import type { BusinessConsoleMesOperationTaskRow, BusinessConsoleResourceItem } from '@nerv-iip/api-client'
import type { DataTableProColumn, DataTableSort } from '@nerv-iip/ui'
import WorkOrderQuickView from '@/components/mes/WorkOrderQuickView.vue'
import { mesOperationTaskStatusOptions } from '@/composables/mes/useMesReferenceLabels'
import { useMesDisplayNames } from '@/composables/mes/useMesDisplayNames'
import { useBusinessMasterDataResources } from '@/composables/useBusinessMasterData'
import { describeMesReadinessReason, useMesOperationTasks } from '@/composables/useBusinessMes'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  ButtonPro,
  DataTablePaginationPro,
  DataTablePro,
  DropdownMenuProItem,
  DropdownMenuProSeparator,
  PageHeader,
  RowActions,
  SelectPro,
  SelectProContent,
  SelectProItem,
  SelectProTrigger,
  SelectProValue,
  StatusBadgePro,
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
const { resolveWorkCenter } = useMesDisplayNames()
const { resources: workCenterResources } = useBusinessMasterDataResources('work-center')
const { resources: shiftResources } = useBusinessMasterDataResources('shift')

const quickViewWorkOrderId = ref<string | null>(null)

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

const statusOptions = mesOperationTaskStatusOptions
const workCenterOptions = computed(() => toResourceOptions(workCenterResources.value))
const shiftOptions = computed(() => toResourceOptions(shiftResources.value))

const visibleTasks = computed(() => operationTasks.value)

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

// facade 返回人读编码（workOrderId=WO-…、workCenterId=WC-…、operationTaskId=WO-…-OP-序号）：
// 工序序号(operationSequence)作主锚点，工单/工作中心直接展示编码即可分辨。
const columns: DataTableProColumn<Row>[] = [
  { key: 'operationTaskId', header: '工序任务', cellClass: 'font-medium', accessor: (r) => r.operationTaskNo ?? r.operationTaskId ?? '无编号' },
  { key: 'workOrderId', header: '工单', accessor: (r) => r.workOrderNo ?? r.workOrderId ?? '无' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'operationSequence', header: '序号', align: 'end', width: 'w-16', accessor: (r) => r.operationSequence ?? 0 },
  { key: 'workCenterId', header: '工作中心', accessor: (r) => r.workCenterName ?? resolveWorkCenter(r.workCenterCode ?? r.workCenterId) ?? '无' },
  { key: 'deviceAssetId', header: '设备', accessor: (r) => r.deviceAssetName ?? r.deviceAssetCode ?? r.deviceAssetId ?? '未指定' },
  { key: 'shiftId', header: '班次', accessor: (r) => r.shiftId ?? '未指定' },
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
  if (workOrderId) quickViewWorkOrderId.value = workOrderId
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
        <ButtonPro size="sm" type="button" variant="outline" :disabled="operationTasksPending" @click="refreshOperationTasks">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </ButtonPro>
      </template>
    </PageHeader>

    <Toolbar v-model:search="keyword" search-placeholder="搜索任务、工单、设备">
      <template #filters>
        <SelectPro v-model="statusFilter">
          <SelectProTrigger class="h-9 w-32" aria-label="工序状态"><SelectProValue /></SelectProTrigger>
          <SelectProContent>
            <SelectProItem v-for="option in statusOptions" :key="option.value" :value="option.value">{{ option.label }}</SelectProItem>
          </SelectProContent>
        </SelectPro>
        <SelectPro v-model="workCenterFilter">
          <SelectProTrigger class="h-9 w-40" aria-label="工作中心"><SelectProValue placeholder="全部工作中心" /></SelectProTrigger>
          <SelectProContent>
            <SelectProItem value="all">全部工作中心</SelectProItem>
            <SelectProItem v-for="option in workCenterOptions" :key="option.value" :value="option.value">{{ option.label }}</SelectProItem>
          </SelectProContent>
        </SelectPro>
        <SelectPro v-model="shiftFilter">
          <SelectProTrigger class="h-9 w-32" aria-label="班次"><SelectProValue placeholder="全部班次" /></SelectProTrigger>
          <SelectProContent>
            <SelectProItem value="all">全部班次</SelectProItem>
            <SelectProItem v-for="option in shiftOptions" :key="option.value" :value="option.value">{{ option.label }}</SelectProItem>
          </SelectProContent>
        </SelectPro>
      </template>
      <template #actions>
        <ButtonPro type="button" variant="ghost" size="sm" @click="resetFilters">重置</ButtonPro>
      </template>
    </Toolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <DataTablePro
      v-model:sort="sort"
      :columns="columns"
      :rows="pagedTasks"
      :row-key="rowKey"
      :client-sort="false"
      :loading="operationTasksPending"
      :searchable="false"
      :column-settings="false"
      empty-message="当前没有工序任务。确认工单已释放、排程已生成后，可开工任务会出现在这里。"
    >
      <template #cell-operationSequence="{ row }">
        <span class="tabular-nums">工序 {{ row.operationSequence ?? '—' }}</span>
      </template>
      <template #cell-workOrderId="{ row }">
        <button
          v-if="row.workOrderId"
          type="button"
          class="text-brand underline-offset-4 hover:underline"
          @click="openWorkOrder(row.workOrderId)"
        >
          {{ row.workOrderNo ?? row.workOrderId }}
        </button>
        <span v-else class="text-muted-foreground">—</span>
      </template>
      <template #cell-status="{ row }">
        <StatusBadgePro :value="row.status" />
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
          <ButtonPro
            v-if="showReportButton(row)"
            size="sm"
            type="button"
            @click="openRoute('/mes/work-orders', row)"
          >
            <ClipboardCheckIcon aria-hidden="true" />
            报工
          </ButtonPro>
          <RowActions :label="`工序任务操作 工序 ${row.operationSequence ?? ''}`">
            <DropdownMenuProItem
              v-if="!showReportButton(row)"
              :disabled="!canOpenReport(row)"
              @click="openRoute('/mes/work-orders', row)"
            >
              <ClipboardCheckIcon aria-hidden="true" />
              {{ canOpenReport(row) ? '报工' : '暂不可报工（缺工单）' }}
            </DropdownMenuProItem>
            <DropdownMenuProItem :disabled="!row.workOrderId" @click="openWorkOrder(row.workOrderId)">
              <EyeIcon aria-hidden="true" />
              查看工单
            </DropdownMenuProItem>
            <DropdownMenuProSeparator />
            <DropdownMenuProItem @click="openRoute('/quality/inspections', row)">
              <ShieldCheckIcon aria-hidden="true" />
              呼叫质检
            </DropdownMenuProItem>
            <DropdownMenuProItem @click="openRoute('/mes/downtime', row)">
              <WrenchIcon aria-hidden="true" />
              记录异常
            </DropdownMenuProItem>
          </RowActions>
        </div>
      </template>
    </DataTablePro>

    <DataTablePaginationPro
      v-model:page="page"
      :page-size="pageSize"
      :total-items="operationTasksTotal"
      @update:page-size="(v) => (pageSize = String(v))"
    />

    <WorkOrderQuickView v-model:work-order-id="quickViewWorkOrderId" />
  </BusinessLayout>
</template>
