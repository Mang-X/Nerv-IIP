<script setup lang="ts">
import type {
  BusinessConsoleMesOperationTaskRow,
  BusinessConsoleResourceItem,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn, DataTableSort } from '@nerv-iip/ui'
import { openDownloadGrantBlob } from '@nerv-iip/business-core'
import WorkOrderQuickView from '@/components/mes/WorkOrderQuickView.vue'
import { mesOperationTaskStatusOptions } from '@/composables/mes/useMesReferenceLabels'
import { useMesDisplayNames } from '@/composables/mes/useMesDisplayNames'
import { useBusinessMasterDataResources } from '@/composables/useBusinessMasterData'
import {
  describeMesReadinessReason,
  useMesCurrentOperationSops,
  useMesOperationTasks,
} from '@/composables/useBusinessMes'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvButton,
  NvDataTable,
  NvDropdownMenuItem,
  NvDropdownMenuSeparator,
  NvPageHeader,
  NvRowActions,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import { watchDebounced } from '@vueuse/core'
import {
  ClipboardCheckIcon,
  EyeIcon,
  FileTextIcon,
  RefreshCwIcon,
  ShieldCheckIcon,
  WrenchIcon,
} from 'lucide-vue-next'
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '工序执行',
    requiredPermissions: ['business.mes.operations.read'],
  },
})

type Row = BusinessConsoleMesOperationTaskRow
type CurrentSop = { fileId?: string | null; fileName?: string | null }

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
const selectedSopTask = ref<Row | null>(null)
const openingSopFileId = ref<string | null>(null)
const sopFileError = ref('')
const {
  filters: sopFilters,
  currentSops,
  currentSopsError,
  currentSopsPending,
  refreshCurrentSops,
  createSopFileDownloadGrant,
} = useMesCurrentOperationSops()

// --- Filters (live) ---
const keyword = ref('')
const statusFilter = ref('all')
const workCenterFilter = ref('all')
const shiftFilter = ref('all')

watch(statusFilter, (value) => {
  filters.status = value === 'all' ? undefined : value
})
watchDebounced(
  keyword,
  (value) => {
    filters.keyword = value.trim() || undefined
  },
  { debounce: 300, maxWait: 1000 },
)
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
  if (key === 'plannedStartUtc')
    return task.plannedStartUtc ? new Date(task.plannedStartUtc).getTime() : 0
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
const { page, pageSize } = usePagedList(filters, {
  resetOn: [keyword, statusFilter, workCenterFilter, shiftFilter],
})
const pagedTasks = computed(() => sortedTasks.value)

// facade 返回人读编码（workOrderId=WO-…、workCenterId=WC-…、operationTaskId=WO-…-OP-序号）：
// 工序序号(operationSequence)作主锚点，工单/工作中心直接展示编码即可分辨。
const columns: NvDataTableColumn<Row>[] = [
  {
    key: 'operationTaskId',
    header: '工序任务',
    cellClass: 'font-medium',
    accessor: (r) => r.operationTaskNo ?? r.operationTaskId ?? '无编号',
  },
  { key: 'workOrderId', header: '工单', accessor: (r) => r.workOrderNo ?? r.workOrderId ?? '无' },
  { key: 'status', header: '状态', width: 'w-24' },
  {
    key: 'operationSequence',
    header: '序号',
    align: 'end',
    width: 'w-16',
    accessor: (r) => r.operationSequence ?? 0,
  },
  {
    key: 'workCenterId',
    header: '工作中心',
    accessor: (r) =>
      r.workCenterName ?? resolveWorkCenter(r.workCenterCode ?? r.workCenterId) ?? '无',
  },
  {
    key: 'deviceAssetId',
    header: '设备',
    accessor: (r) => r.deviceAssetName ?? r.deviceAssetCode ?? r.deviceAssetId ?? '未指定',
  },
  { key: 'shiftId', header: '班次', accessor: (r) => r.shiftId ?? '未指定' },
  {
    key: 'plannedStartUtc',
    header: '计划开始',
    accessor: (r) => (r.plannedStartUtc ? new Date(r.plannedStartUtc).getTime() : 0),
  },
  { key: 'qualityStatus', header: '质量状态' },
  { key: 'sop', header: 'SOP', align: 'end', width: 'w-20' },
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
function openSops(task: Row) {
  selectedSopTask.value = task
  sopFileError.value = ''
  sopFilters.operationCode = task.operationCode?.trim() ?? ''
  sopFilters.workCenterCode = (task.workCenterCode ?? task.workCenterId)?.trim() ?? ''
  sopFilters.routingCode = ''
  sopFilters.routingRevision = ''
  sopFilters.asOfDate = ''
}
async function openSopFile(sop: CurrentSop) {
  const fileId = sop.fileId?.trim()
  if (!fileId) {
    sopFileError.value = '当前SOP未绑定可查看的文件。'
    return
  }
  sopFileError.value = ''
  openingSopFileId.value = fileId
  try {
    const grant = await createSopFileDownloadGrant(fileId)
    if (!grant) throw new Error('无法获取SOP查看授权。')
    await openDownloadGrantBlob(grant)
  } catch (error) {
    sopFileError.value = error instanceof Error ? error.message : '无法打开SOP。'
  } finally {
    openingSopFileId.value = null
  }
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
function formatDate(value?: string | null) {
  if (!value) return '无'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleDateString()
}
function canOpenSops(task: Row) {
  return Boolean(task.operationCode?.trim())
}
const selectedSopErrorMessage = computed(() => formatError(currentSopsError.value))
const selectedSopTitle = computed(() => {
  const task = selectedSopTask.value
  if (!task) return ''
  return `${task.operationTaskNo ?? task.operationTaskId ?? '工序任务'} · ${task.operationCode ?? '未绑定工序'}`
})
function readiness(value?: string | null) {
  return describeMesReadinessReason(value ?? '未检')
}
function readinessNeedsAction(value?: string | null) {
  return [
    'QUALITY_PLAN_MISSING',
    'QUALITY_HOLD_ACTIVE',
    'EQUIPMENT_UNAVAILABLE',
    'EQUIPMENT_MAINTENANCE_CONFLICT',
  ].includes(describeMesReadinessReason(value ?? '').code)
}
function toResourceOptions(items: BusinessConsoleResourceItem[]) {
  return items
    .filter((item) => item.active !== false && item.code)
    .map((item) => ({
      label: item.displayName ? `${item.displayName} (${item.code})` : item.code!,
      value: item.code!,
    }))
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="工序执行"
      :breadcrumbs="[{ label: '制造执行' }]"
      :count="`${operationTasksTotal} 个工序任务`"
    >
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="operationTasksPending"
          @click="refreshOperationTasks"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <NvToolbar v-model:search="keyword" search-placeholder="搜索任务、工单、设备">
      <template #filters>
        <NvSelect v-model="statusFilter">
          <NvSelectTrigger class="h-9 w-32" aria-label="工序状态"
            ><NvSelectValue
          /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem
              v-for="option in statusOptions"
              :key="option.value"
              :value="option.value"
              >{{ option.label }}</NvSelectItem
            >
          </NvSelectContent>
        </NvSelect>
        <NvSelect v-model="workCenterFilter">
          <NvSelectTrigger class="h-9 w-40" aria-label="工作中心"
            ><NvSelectValue placeholder="全部工作中心"
          /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem value="all">全部工作中心</NvSelectItem>
            <NvSelectItem
              v-for="option in workCenterOptions"
              :key="option.value"
              :value="option.value"
              >{{ option.label }}</NvSelectItem
            >
          </NvSelectContent>
        </NvSelect>
        <NvSelect v-model="shiftFilter">
          <NvSelectTrigger class="h-9 w-32" aria-label="班次"
            ><NvSelectValue placeholder="全部班次"
          /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem value="all">全部班次</NvSelectItem>
            <NvSelectItem
              v-for="option in shiftOptions"
              :key="option.value"
              :value="option.value"
              >{{ option.label }}</NvSelectItem
            >
          </NvSelectContent>
        </NvSelect>
      </template>
      <template #actions>
        <NvButton type="button" variant="ghost" size="sm" @click="resetFilters">重置</NvButton>
      </template>
    </NvToolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="operationTasksTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
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
        <NvStatusBadge :value="row.status" />
      </template>
      <template #cell-plannedStartUtc="{ row }">
        {{ formatDateTime(row.plannedStartUtc) }}
      </template>
      <template #cell-qualityStatus="{ row }">
        <div class="grid gap-0.5">
          <span>{{ readiness(row.qualityStatus).label }}</span>
          <span
            v-if="readinessNeedsAction(row.qualityStatus)"
            class="text-xs text-muted-foreground"
          >
            {{ readiness(row.qualityStatus).nextStep }}
          </span>
        </div>
      </template>
      <template #cell-sop="{ row }">
        <NvButton
          size="icon"
          type="button"
          variant="ghost"
          :disabled="!canOpenSops(row)"
          :title="canOpenSops(row) ? '查看当前SOP' : '当前任务未绑定标准工序'"
          @click="openSops(row)"
        >
          <FileTextIcon aria-hidden="true" />
          <span class="sr-only">查看当前SOP</span>
        </NvButton>
      </template>
      <template #cell-actions="{ row }">
        <div class="flex items-center justify-end gap-1">
          <NvButton
            v-if="showReportButton(row)"
            size="sm"
            type="button"
            @click="openRoute('/mes/work-orders', row)"
          >
            <ClipboardCheckIcon aria-hidden="true" />
            报工
          </NvButton>
          <NvRowActions :label="`工序任务操作 工序 ${row.operationSequence ?? ''}`">
            <NvDropdownMenuItem :disabled="!canOpenSops(row)" @click="openSops(row)">
              <FileTextIcon aria-hidden="true" />
              {{ canOpenSops(row) ? '查看当前SOP' : '未绑定标准工序' }}
            </NvDropdownMenuItem>
            <NvDropdownMenuSeparator />
            <NvDropdownMenuItem
              v-if="!showReportButton(row)"
              :disabled="!canOpenReport(row)"
              @click="openRoute('/mes/work-orders', row)"
            >
              <ClipboardCheckIcon aria-hidden="true" />
              {{ canOpenReport(row) ? '报工' : '暂不可报工（缺工单）' }}
            </NvDropdownMenuItem>
            <NvDropdownMenuItem
              :disabled="!row.workOrderId"
              @click="openWorkOrder(row.workOrderId)"
            >
              <EyeIcon aria-hidden="true" />
              查看工单
            </NvDropdownMenuItem>
            <NvDropdownMenuSeparator />
            <NvDropdownMenuItem @click="openRoute('/quality/inspections', row)">
              <ShieldCheckIcon aria-hidden="true" />
              呼叫质检
            </NvDropdownMenuItem>
            <NvDropdownMenuItem @click="openRoute('/mes/downtime', row)">
              <WrenchIcon aria-hidden="true" />
              记录异常
            </NvDropdownMenuItem>
          </NvRowActions>
        </div>
      </template>
    </NvDataTable>

    <section
      v-if="selectedSopTask"
      class="grid gap-3 border-t border-border pt-4"
      aria-live="polite"
    >
      <div class="flex flex-wrap items-center justify-between gap-3">
        <div class="min-w-0">
          <h2 class="text-sm font-semibold text-foreground">{{ selectedSopTitle }}</h2>
          <p class="text-xs text-muted-foreground">
            当前生效 SOP ·
            {{
              selectedSopTask.workCenterName ??
              resolveWorkCenter(selectedSopTask.workCenterCode ?? selectedSopTask.workCenterId) ??
              selectedSopTask.workCenterId ??
              '无工作中心'
            }}
          </p>
        </div>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="currentSopsPending"
          @click="refreshCurrentSops"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新SOP
        </NvButton>
      </div>
      <p
        v-if="selectedSopErrorMessage || sopFileError"
        class="text-sm text-destructive"
        role="alert"
      >
        {{ selectedSopErrorMessage || sopFileError }}
      </p>
      <p v-else-if="currentSopsPending" class="text-sm text-muted-foreground">正在加载当前SOP...</p>
      <ul v-else-if="currentSops.length" class="grid gap-2 md:grid-cols-2">
        <li
          v-for="sop in currentSops"
          :key="`${sop.documentNumber}-${sop.revision}-${sop.fileId}`"
          class="grid gap-1 rounded-md border border-border px-3 py-2 text-sm"
        >
          <span class="font-medium text-foreground">{{ sop.fileName || sop.documentNumber }}</span>
          <span class="text-xs text-muted-foreground">
            {{ sop.documentNumber }} · rev {{ sop.revision }} · 生效
            {{ formatDate(sop.effectiveDate) }}
          </span>
          <NvButton
            size="sm"
            type="button"
            variant="outline"
            class="w-fit"
            :disabled="openingSopFileId === sop.fileId"
            @click="openSopFile(sop)"
          >
            <EyeIcon aria-hidden="true" />
            查看SOP
          </NvButton>
        </li>
      </ul>
      <p v-else class="text-sm text-muted-foreground">当前工序没有已发布且已生效的SOP。</p>
    </section>

    <WorkOrderQuickView v-model:work-order-id="quickViewWorkOrderId" />
  </BusinessLayout>
</template>
