<script setup lang="ts">
import type { BusinessConsoleMesOperationTaskRow, BusinessConsoleResourceItem } from '@nerv-iip/api-client'
import type { DataTableColumn, DataTableSort } from '@nerv-iip/ui'
import { mesStatusOptions } from '@/composables/mes/useMesReferenceLabels'
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
  SectionCard,
  SectionCards,
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

const statusOptions = mesStatusOptions
const workCenterOptions = computed(() => toResourceOptions(workCenterResources.value))
const shiftOptions = computed(() => toResourceOptions(shiftResources.value))

const visibleTasks = computed(() => operationTasks.value)

const readyCount = computed(() => visibleTasks.value.filter((t) => t.status?.toLowerCase() === 'ready').length)
const runningCount = computed(() => visibleTasks.value.filter((t) => ['inprogress', 'active'].includes(t.status?.toLowerCase() ?? '')).length)
const blockedCount = computed(() => visibleTasks.value.filter((t) => t.status?.toLowerCase() === 'blocked').length)

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

const columns: DataTableColumn<Row>[] = [
  { key: 'operationTaskId', header: '工序任务', cellClass: 'font-medium', accessor: (r) => r.operationTaskNo ?? r.operationTaskId ?? '无编号' },
  { key: 'workOrderId', header: '工单', accessor: (r) => r.workOrderNo ?? r.workOrderId ?? '无' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'operationSequence', header: '序号', align: 'end', width: 'w-16', accessor: (r) => r.operationSequence ?? 0 },
  { key: 'workCenterId', header: '工作中心', accessor: (r) => r.workCenterName ?? r.workCenterCode ?? r.workCenterId ?? '无' },
  { key: 'deviceAssetId', header: '设备', accessor: (r) => r.deviceAssetName ?? r.deviceAssetCode ?? r.deviceAssetId ?? '未指定' },
  { key: 'shiftId', header: '班次', accessor: (r) => r.shiftId ?? '未指定' },
  { key: 'plannedStartUtc', header: '计划开始', accessor: (r) => (r.plannedStartUtc ? new Date(r.plannedStartUtc).getTime() : 0) },
  { key: 'qualityStatus', header: '质量状态' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
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

    <SectionCards :columns="4">
      <SectionCard description="本页可开工" :value="readyCount" hint="确认人员、设备、物料后进入报工" />
      <SectionCard description="本页执行中" :value="runningCount" hint="关注报工节拍与质量确认" />
      <SectionCard description="本页受阻" :value="blockedCount" hint="需班组长 / 质检 / 设备处理" />
      <SectionCard description="任务总数" :value="operationTasksTotal" hint="后端分页总数" />
    </SectionCards>

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

    <DataTable
      v-model:sort="sort"
      :columns="columns"
      :rows="pagedTasks"
      :row-key="rowKey"
      :client-sort="false"
      :loading="operationTasksPending"
      empty-message="当前没有工序任务。确认工单已释放、排程已生成后，可开工任务会出现在这里。"
    >
      <template #cell-workOrderId="{ row }">
        <button
          v-if="row.workOrderId"
          type="button"
          class="font-medium text-brand underline-offset-4 hover:underline"
          @click="openWorkOrder(row.workOrderId)"
        >
          {{ row.workOrderId }}
        </button>
        <span v-else class="text-muted-foreground">无</span>
      </template>
      <template #cell-status="{ row }">
        <StatusBadge :value="row.status" />
      </template>
      <template #cell-operationSequence="{ row }">
        <span class="tabular-nums">{{ row.operationSequence ?? 0 }}</span>
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
        <RowActions :label="`工序任务操作 ${row.operationTaskId ?? ''}`">
          <DropdownMenuItem :disabled="!row.workOrderId" @click="openWorkOrder(row.workOrderId)">
            <EyeIcon aria-hidden="true" />
            查看工单
          </DropdownMenuItem>
          <DropdownMenuItem :disabled="!canOpenReport(row)" @click="openRoute('/mes/work-orders', row)">
            <ClipboardCheckIcon aria-hidden="true" />
            {{ canOpenReport(row) ? '打开报工表单' : '缺少工单上下文' }}
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
      </template>
    </DataTable>

    <DataTablePagination
      v-model:page="page"
      v-model:page-size="pageSize"
      :total-items="operationTasksTotal"
    />
  </BusinessLayout>
</template>
