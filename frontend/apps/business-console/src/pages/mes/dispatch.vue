<script setup lang="ts">
import type { NvDataTableColumn } from '@nerv-iip/ui'
import type { DispatchAssignTarget } from '@/components/mes/DispatchAssignDialog.vue'
import DispatchAssignDialog from '@/components/mes/DispatchAssignDialog.vue'
import {
  useBusinessMasterDataResources,
  useBusinessWorkers,
} from '@/composables/useBusinessMasterData'
import { describeMesReadinessReason, useMesDispatchTasks } from '@/composables/useBusinessMes'
import { describeScheduleInvalidationReason } from '@/composables/useScheduleInvalidation'
import { pagedBreakdownSegments } from '@/composables/metricSegments'
import { mesOperationTaskStatusOptions } from '@/composables/mes/useMesReferenceLabels'
import { useMesDisplayNames } from '@/composables/mes/useMesDisplayNames'
import {
  hasBlockingReasons,
  isScheduleInvalidatedTask,
  isSettledTask,
  resolveDispatchAffordance,
  resolveDispatchState,
  resolveExecutionState,
  resolveScheduleState,
} from '@/composables/mes/useMesTaskSemantics'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvButton,
  NvDataTable,
  NvDropdownMenuItem,
  NvGroupPanel,
  NvInput,
  NvMetricCard,
  NvPageHeader,
  NvPagination,
  NvRowActions,
  NvSearchSelect,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import { LayoutListIcon, RefreshCwIcon, RotateCcwIcon, TableIcon, UserCheckIcon } from '@lucide/vue'
import { watchDebounced } from '@vueuse/core'
import { computed, ref, shallowRef, watch } from 'vue'
import { RouterLink } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '派工看板',
    requiredPermissions: ['business.mes.dispatch.read'],
  },
})

const {
  assignDispatchTask,
  assignDispatchTaskPending,
  dispatchTasks,
  dispatchTasksError,
  dispatchTasksPending,
  dispatchTasksTotal,
  filters,
  refreshDispatchTasks,
} = useMesDispatchTasks()

const keyword = ref('')
const statusFilter = ref('all')
const workCenterFilter = ref('all')
const shiftFilter = ref('all')
const workerFilter = ref('all')
// 分组视图是班组长的默认视角：一张工单的几道工序必须挨在一起才看得出接续关系。
const viewMode = ref<'grouped' | 'flat'>('grouped')

const { page, pageSize, pageSizeNumber, resetPage } = usePagedList(filters, {
  resetOn: [
    () => filters.keyword,
    () => filters.status,
    () => filters.workCenterId,
    () => filters.shiftId,
    () => filters.assignedUserId,
  ],
})

const { resources: workCenters } = useBusinessMasterDataResources('work-center')
const { resources: shifts } = useBusinessMasterDataResources('shift')
const { workers } = useBusinessWorkers({ employmentStatus: 'active' })
const { resolveShiftLabel, resolveWorkCenter } = useMesDisplayNames()

// 关键字打后端（facade 支持 keyword），去抖避免每敲一个字发一次请求。
watchDebounced(
  keyword,
  (value) => {
    filters.keyword = value.trim() ? value.trim() : undefined
  },
  { debounce: 300, maxWait: 1000 },
)
watch(statusFilter, (v) => (filters.status = v === 'all' ? undefined : v))
watch(workCenterFilter, (v) => (filters.workCenterId = v === 'all' ? undefined : v))
watch(shiftFilter, (v) => (filters.shiftId = v === 'all' ? undefined : v))
watch(workerFilter, (v) => (filters.assignedUserId = v === 'all' ? undefined : v))

const hasActiveFilter = computed(
  () =>
    Boolean(keyword.value.trim()) ||
    statusFilter.value !== 'all' ||
    workCenterFilter.value !== 'all' ||
    shiftFilter.value !== 'all' ||
    workerFilter.value !== 'all',
)
function resetFilters() {
  keyword.value = ''
  statusFilter.value = 'all'
  workCenterFilter.value = 'all'
  shiftFilter.value = 'all'
  workerFilter.value = 'all'
  resetPage()
}

const workerFilterOptions = computed(() => [
  { value: 'all', label: '全部工人' },
  ...workers.value
    .filter((w) => w.userId)
    .map((w) => ({
      value: w.userId as string,
      label: w.employeeNo
        ? `${w.displayName ?? w.employeeNo} · ${w.employeeNo}`
        : (w.displayName ?? ''),
    })),
])

type DispatchRow = (typeof dispatchTasks)['value'][number]

// 三个决策口径：还有多少要派、已经派出去多少、这一页里多少已经完工。
// 只统计当前页（facade 不给全量分状态计数），所以文案说的是「本页」而不是总量。
const pendingCount = computed(
  () => dispatchTasks.value.filter((r) => resolveDispatchState(r).key === 'unassigned').length,
)
const assignedCount = computed(
  () => dispatchTasks.value.filter((r) => resolveDispatchState(r).key === 'assigned').length,
)
const settledCount = computed(
  () => dispatchTasks.value.filter((r) => isSettledTask(r.status)).length,
)
const dispatchSegments = computed(() =>
  pagedBreakdownSegments(dispatchTasksTotal.value, [
    { key: 'pending', label: '待派工', value: pendingCount.value, tone: 'warning' },
    { key: 'assigned', label: '已派工', value: assignedCount.value, tone: 'success' },
    { key: 'settled', label: '已完工', value: settledCount.value, tone: 'neutral' },
  ]),
)

const errorMessage = computed(() => formatError(dispatchTasksError.value))

// 分组只对当前这一页的工序生效——facade 的分页单位是工序、不是工单，
// 所以组头写「本页 N 道」，不谎称这就是该工单的全部工序。
interface DispatchGroup {
  key: string
  workOrderId?: string | null
  workOrderNo: string
  workCenter: string
  rows: DispatchRow[]
  pending: number
  blocked: number
}
const groups = computed<DispatchGroup[]>(() => {
  const map = new Map<string, DispatchGroup>()
  for (const row of dispatchTasks.value) {
    const key = row.workOrderNo ?? row.workOrderId ?? '未关联工单'
    let group = map.get(key)
    if (!group) {
      group = {
        key,
        workOrderId: row.workOrderId,
        workOrderNo: key,
        workCenter:
          row.workCenterName ?? resolveWorkCenter(row.workCenterCode ?? row.workCenterId) ?? '',
        rows: [],
        pending: 0,
        blocked: 0,
      }
      map.set(key, group)
    }
    group.rows.push(row)
    if (resolveDispatchState(row).key === 'unassigned') group.pending += 1
    if (hasBlockingReasons(row)) group.blocked += 1
  }
  // 待派工多的工单排前面——班组长先处理还没派人的活。
  return [...map.values()].sort(
    (a, b) => b.pending - a.pending || a.workOrderNo.localeCompare(b.workOrderNo),
  )
})

function groupSummary(group: DispatchGroup) {
  const parts = [`${group.rows.length} 道工序`]
  if (group.pending > 0) parts.push(`${group.pending} 道待派工`)
  if (group.blocked > 0) parts.push(`${group.blocked} 道有阻塞`)
  return parts.join(' · ')
}

// 分组视图的组内表不重复「工单」列（组头已写），平铺视图才需要。
const groupColumns: NvDataTableColumn<DispatchRow>[] = [
  {
    key: 'operationTaskNo',
    header: '工序',
    cellClass: 'font-medium',
    accessor: (r) => r.operationCode ?? r.operationTaskNo ?? r.operationTaskId ?? '无',
  },
  { key: 'status', header: '执行状态', width: 'w-28' },
  { key: 'dispatchState', header: '派工', width: 'w-40' },
  { key: 'scheduleState', header: '排程', width: 'w-40' },
  {
    key: 'deviceAssetId',
    header: '设备',
    accessor: (r) => r.deviceAssetName ?? r.deviceAssetCode ?? r.deviceAssetId ?? '未指定',
  },
  { key: 'shiftId', header: '班次', width: 'w-28', accessor: (r) => resolveShiftLabel(r.shiftId) },
  { key: 'plannedStartUtc', header: '计划开始', width: 'w-44' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
]
const flatColumns: NvDataTableColumn<DispatchRow>[] = [
  {
    key: 'operationTaskNo',
    header: '工序',
    cellClass: 'font-medium',
    accessor: (r) => r.operationCode ?? r.operationTaskNo ?? r.operationTaskId ?? '无',
  },
  { key: 'workOrderNo', header: '工单', accessor: (r) => r.workOrderNo ?? r.workOrderId ?? '无' },
  { key: 'status', header: '执行状态', width: 'w-28' },
  { key: 'dispatchState', header: '派工', width: 'w-40' },
  { key: 'scheduleState', header: '排程', width: 'w-40' },
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
  { key: 'shiftId', header: '班次', width: 'w-28', accessor: (r) => resolveShiftLabel(r.shiftId) },
  { key: 'plannedStartUtc', header: '计划开始', width: 'w-44' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
]

// ── 派工 ────────────────────────────────────────────────────────
const assignOpen = shallowRef(false)
const assignTarget = shallowRef<DispatchAssignTarget | null>(null)

function openAssign(row: DispatchRow) {
  if (!resolveDispatchAffordance(row).enabled) return
  assignTarget.value = row
  assignOpen.value = true
}

function readinessList(reasons?: string[] | null) {
  return (reasons ?? []).map(describeMesReadinessReason)
}
function formatDateTime(value?: string | null) {
  if (!value) return '未指定'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="派工看板"
      :breadcrumbs="[{ label: '制造执行' }]"
      :count="`${dispatchTasksTotal} 道工序`"
    >
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="dispatchTasksPending"
          @click="refreshDispatchTasks"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <!-- 只留一张构成卡。曾并排放过一张「派工阻塞」告警卡，但它由 blockingReasons 驱动，
         而该字段在派工 facade 里恒为空数组，于是那张卡永远显示「当前待派工序没有阻塞」——
         一个后端根本没检查过的断言。阻塞回填后再按真数据加卡。 -->
    <NvMetricCard
      class="sm:max-w-md"
      variant="breakdown"
      label="工序"
      :value="dispatchTasksTotal"
      unit="道"
      :segments="dispatchSegments"
    />

    <NvToolbar :show-search="false">
      <template #filters>
        <NvInput
          v-model="keyword"
          class="h-9 w-56"
          placeholder="工单号 / 工序 / 工作中心"
          aria-label="搜索工序"
        />
        <NvSelect v-model="statusFilter">
          <NvSelectTrigger class="h-9 w-32" aria-label="执行状态"
            ><NvSelectValue
          /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem
              v-for="option in mesOperationTaskStatusOptions"
              :key="option.value"
              :value="option.value"
              >{{ option.label }}</NvSelectItem
            >
          </NvSelectContent>
        </NvSelect>
        <NvSelect v-model="workCenterFilter">
          <NvSelectTrigger class="h-9 w-40" aria-label="工作中心"
            ><NvSelectValue
          /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem value="all">全部工作中心</NvSelectItem>
            <NvSelectItem v-for="wc in workCenters" :key="wc.code ?? ''" :value="wc.code ?? ''">
              {{ wc.displayName ?? wc.code }}
            </NvSelectItem>
          </NvSelectContent>
        </NvSelect>
        <NvSelect v-model="shiftFilter">
          <NvSelectTrigger class="h-9 w-28" aria-label="班次"><NvSelectValue /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem value="all">全部班次</NvSelectItem>
            <NvSelectItem v-for="s in shifts" :key="s.code ?? ''" :value="s.code ?? ''">
              {{ s.displayName ?? s.code }}
            </NvSelectItem>
          </NvSelectContent>
        </NvSelect>
        <NvSearchSelect
          v-model="workerFilter"
          class="h-9 w-40"
          :options="workerFilterOptions"
          placeholder="全部工人"
          search-placeholder="搜索姓名 / 工号…"
          aria-label="受派工人"
        />
        <NvButton
          v-if="hasActiveFilter"
          size="sm"
          type="button"
          variant="ghost"
          @click="resetFilters"
        >
          <RotateCcwIcon aria-hidden="true" />
          重置
        </NvButton>
      </template>
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          :variant="viewMode === 'grouped' ? 'secondary' : 'ghost'"
          aria-label="按工单分组"
          @click="viewMode = 'grouped'"
        >
          <LayoutListIcon aria-hidden="true" />
          按工单
        </NvButton>
        <NvButton
          size="sm"
          type="button"
          :variant="viewMode === 'flat' ? 'secondary' : 'ghost'"
          aria-label="平铺列表"
          @click="viewMode = 'flat'"
        >
          <TableIcon aria-hidden="true" />
          平铺
        </NvButton>
      </template>
    </NvToolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <!-- 分组视图：一张工单一块，组内按工序顺序排；组头写清本页规模与待办。 -->
    <template v-if="viewMode === 'grouped'">
      <div v-if="groups.length" class="grid gap-3">
        <NvGroupPanel
          v-for="group in groups"
          :key="group.key"
          :title="group.workOrderNo"
          :subtitle="group.workCenter || undefined"
          :count="groupSummary(group)"
          :collapsed-summary="groupSummary(group)"
        >
          <template #meta>
            <RouterLink
              v-if="group.workOrderId"
              :to="`/mes/work-orders/${encodeURIComponent(group.workOrderId)}`"
              class="text-xs text-brand underline-offset-4 hover:underline"
              @click.stop
            >
              打开工单
            </RouterLink>
          </template>
          <NvDataTable
            :columns="groupColumns"
            :rows="group.rows"
            row-key="operationTaskId"
            :pagination="false"
            :searchable="false"
            :column-settings="false"
            density="compact"
            class="rounded-none border-0"
          >
            <template #cell-status="{ row }">
              <NvStatusBadge
                :label="resolveExecutionState(row.status).label"
                :tone="resolveExecutionState(row.status).tone"
              />
            </template>
            <template #cell-dispatchState="{ row }">
              <NvStatusBadge
                :label="resolveDispatchState(row).label"
                :tone="resolveDispatchState(row).tone"
              />
            </template>
            <template #cell-scheduleState="{ row }">
              <span
                class="inline-flex"
                :title="
                  isScheduleInvalidatedTask(row.status)
                    ? describeScheduleInvalidationReason(row.scheduleInvalidationReasonCode)
                    : undefined
                "
              >
                <NvStatusBadge
                  :label="resolveScheduleState(row).label"
                  :tone="resolveScheduleState(row).tone"
                />
              </span>
            </template>
            <template #cell-plannedStartUtc="{ row }">
              {{ formatDateTime(row.plannedStartUtc) }}
            </template>
            <template #cell-actions="{ row }">
              <NvRowActions :label="`工序操作 ${row.operationTaskNo ?? ''}`">
                <NvDropdownMenuItem
                  :disabled="!resolveDispatchAffordance(row).enabled"
                  :title="resolveDispatchAffordance(row).blockedReason"
                  @click="openAssign(row)"
                >
                  <UserCheckIcon aria-hidden="true" />
                  {{ resolveDispatchAffordance(row).label }}
                </NvDropdownMenuItem>
              </NvRowActions>
            </template>
          </NvDataTable>
          <!-- 阻塞项只在真有的时候出现，不给「无阻塞」的恒真断言（facade 目前不下发阻塞）。 -->
          <div
            v-for="row in group.rows.filter(hasBlockingReasons)"
            :key="`blocked-${row.operationTaskId}`"
            class="border-t border-warning/30 bg-warning/10 px-4 py-2.5 text-sm"
          >
            <p class="font-medium text-foreground">
              {{ row.operationCode ?? row.operationTaskNo }} 开工阻塞
            </p>
            <p
              v-for="reason in readinessList(row.blockingReasons)"
              :key="reason.code"
              class="text-xs text-muted-foreground"
            >
              {{ reason.label }} —— {{ reason.nextStep }}
            </p>
          </div>
        </NvGroupPanel>

        <NvPagination
          v-model:page="page"
          :page-size="pageSizeNumber"
          :total-items="dispatchTasksTotal"
          @update:page-size="(v) => (pageSize = String(v))"
        />
      </div>
      <p
        v-else-if="!dispatchTasksPending"
        class="rounded-xl border border-dashed p-8 text-center text-sm text-muted-foreground"
      >
        {{
          hasActiveFilter
            ? '当前筛选条件下没有工序，换个条件或重置筛选。'
            : '暂无工序。工单下达后，它的工序会出现在这里等待派工。'
        }}
      </p>
      <p
        v-else
        class="rounded-xl border border-dashed p-8 text-center text-sm text-muted-foreground"
      >
        正在加载工序…
      </p>
    </template>

    <!-- 平铺视图：跨工单横向比对（按工作中心 / 按人看负荷）时用。 -->
    <NvDataTable
      v-else
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="dispatchTasksTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :columns="flatColumns"
      :rows="dispatchTasks"
      row-key="operationTaskId"
      :loading="dispatchTasksPending"
      :searchable="false"
      :column-settings="false"
      :empty-message="
        hasActiveFilter
          ? '当前筛选条件下没有工序，换个条件或重置筛选。'
          : '暂无工序。工单下达后，它的工序会出现在这里等待派工。'
      "
    >
      <template #cell-workOrderNo="{ row }">
        <RouterLink
          v-if="row.workOrderId"
          :to="`/mes/work-orders/${encodeURIComponent(row.workOrderId)}`"
          class="text-brand underline-offset-4 hover:underline"
        >
          {{ row.workOrderNo ?? row.workOrderId }}
        </RouterLink>
        <span v-else>无</span>
      </template>
      <template #cell-status="{ row }">
        <NvStatusBadge
          :label="resolveExecutionState(row.status).label"
          :tone="resolveExecutionState(row.status).tone"
        />
      </template>
      <template #cell-dispatchState="{ row }">
        <NvStatusBadge
          :label="resolveDispatchState(row).label"
          :tone="resolveDispatchState(row).tone"
        />
      </template>
      <template #cell-scheduleState="{ row }">
        <span
          class="inline-flex"
          :title="
            isScheduleInvalidatedTask(row.status)
              ? describeScheduleInvalidationReason(row.scheduleInvalidationReasonCode)
              : undefined
          "
        >
          <NvStatusBadge
            :label="resolveScheduleState(row).label"
            :tone="resolveScheduleState(row).tone"
          />
        </span>
      </template>
      <template #cell-plannedStartUtc="{ row }">{{ formatDateTime(row.plannedStartUtc) }}</template>
      <template #cell-actions="{ row }">
        <NvRowActions :label="`工序操作 ${row.operationTaskNo ?? ''}`">
          <NvDropdownMenuItem
            :disabled="!resolveDispatchAffordance(row).enabled"
            :title="resolveDispatchAffordance(row).blockedReason"
            @click="openAssign(row)"
          >
            <UserCheckIcon aria-hidden="true" />
            {{ resolveDispatchAffordance(row).label }}
          </NvDropdownMenuItem>
        </NvRowActions>
      </template>
    </NvDataTable>

    <DispatchAssignDialog
      v-model:open="assignOpen"
      :target="assignTarget"
      :assign="
        (operationTaskId, body) =>
          assignDispatchTask(operationTaskId, {
            organizationId: filters.organizationId,
            environmentId: filters.environmentId,
            ...body,
          })
      "
      :pending="assignDispatchTaskPending"
      @assigned="refreshDispatchTasks"
    />
  </BusinessLayout>
</template>
