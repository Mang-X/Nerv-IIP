<script setup lang="ts">
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { useBusinessWorkers } from '@/composables/useBusinessMasterData'
import { describeMesReadinessReason, useMesDispatchTasks } from '@/composables/useBusinessMes'
import {
  describeScheduleInvalidationReason,
  isScheduleInvalidated,
  resolveScheduleStatus,
} from '@/composables/useScheduleInvalidation'
import { mesOperationTaskStatusOptions } from '@/composables/mes/useMesReferenceLabels'
import { useMesDisplayNames } from '@/composables/mes/useMesDisplayNames'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvButton,
  NvDataTable,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvDropdownMenuItem,
  NvField,
  NvFieldLabel,
  NvInput,
  NvPageHeader,
  NvRowActions,
  NvSectionCard,
  NvSectionCards,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  Spinner,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import { RefreshCwIcon, UserCheckIcon } from '@lucide/vue'
import { computed, ref, shallowRef, watch } from 'vue'
import { notifyError, notifySuccess } from '@/utils/notify'

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
const { page, pageSize } = usePagedList(filters, { resetOn: [() => filters.status] })
const { workers } = useBusinessWorkers()
const { resolveWorkCenter } = useMesDisplayNames()
const statusFilter = shallowRef('all')
watch(statusFilter, (value) => {
  filters.status = value === 'all' ? undefined : value
})

const blockedCount = computed(
  () => dispatchTasks.value.filter((x) => x.blockingReasons?.length).length,
)
const dispatchableCount = computed(
  () => dispatchTasks.value.filter((x) => !x.blockingReasons?.length).length,
)
const errorMessage = computed(() => formatError(dispatchTasksError.value))

type DispatchRow = (typeof dispatchTasks)['value'][number]

// 操作员选项：value=userId（与 assignedUserId 同源），label=姓名 · 工号。
const workerOptions = computed(() =>
  workers.value
    .filter((w) => w.userId)
    .map((w) => ({
      value: w.userId as string,
      label: w.employeeNo
        ? `${w.displayName ?? w.userId} · ${w.employeeNo}`
        : (w.displayName ?? (w.userId as string)),
    })),
)

const columns: NvDataTableColumn<DispatchRow>[] = [
  {
    key: 'operationTaskId',
    header: '工序任务',
    cellClass: 'font-medium',
    accessor: (r) => r.operationTaskNo ?? r.operationTaskId ?? '无',
  },
  { key: 'workOrderId', header: '工单', accessor: (r) => r.workOrderNo ?? r.workOrderId ?? '无' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'scheduleStatus', header: '排程状态', width: 'w-56' },
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
  { key: 'plannedStartUtc', header: '计划开始', width: 'w-44' },
  { key: 'blockingReasons', header: '阻塞处理' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
]

// ── 派工（指派操作员）─────────────────────────────────────────────
const assignOpen = shallowRef(false)
const assignTarget = shallowRef<DispatchRow | null>(null)
const assignedUserId = ref('')
function canDispatch(row: DispatchRow) {
  return (
    Boolean(row.operationTaskId) &&
    !row.blockingReasons?.length &&
    !isScheduleInvalidated(row.status)
  )
}
function openAssign(row: DispatchRow) {
  if (!canDispatch(row)) return
  assignTarget.value = row
  assignedUserId.value = ''
  assignOpen.value = true
}
async function confirmAssign() {
  const target = assignTarget.value
  if (!target?.operationTaskId || !assignedUserId.value) return
  try {
    await assignDispatchTask(target.operationTaskId, {
      organizationId: filters.organizationId,
      environmentId: filters.environmentId,
      assignedUserId: assignedUserId.value,
      // 设备/班次沿用任务已排程值，不在此变更。
      deviceAssetId: target.deviceAssetId ?? undefined,
      shiftId: target.shiftId ?? undefined,
      idempotencyKey: `dispatch-assign-${Date.now()}-${Math.random().toString(36).slice(2, 10)}`,
    })
    notifySuccess('已派工：操作员已指派。')
    assignOpen.value = false
    assignTarget.value = null
    void refreshDispatchTasks()
  } catch (error) {
    notifyError(error)
  }
}

function dispatchActionLabel(row: DispatchRow) {
  if (isScheduleInvalidated(row.status)) return '排程已失效，待重排'
  if (row.blockingReasons?.length) return '有阻塞，先处理'
  return '派工（指派操作员）'
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
      :count="`${dispatchTasksTotal} 个待派工序`"
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

    <NvSectionCards :columns="3">
      <NvSectionCard description="派工任务" :value="dispatchTasksTotal" hint="后端筛选总数" />
      <NvSectionCard description="本页可派工" :value="dispatchableCount" hint="当前页统计" />
      <NvSectionCard description="本页有阻塞" :value="blockedCount" hint="当前页统计" />
    </NvSectionCards>

    <NvToolbar :show-search="false">
      <template #filters>
        <NvSelect v-model="statusFilter">
          <NvSelectTrigger class="h-9 w-32" aria-label="派工状态"
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
      </template>
    </NvToolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="dispatchTasksTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :columns="columns"
      :rows="dispatchTasks"
      row-key="operationTaskId"
      :loading="dispatchTasksPending"
      empty-message="暂无待派工序。工单释放并排程后，待派工序会出现在这里。"
      :searchable="false"
      :column-settings="false"
    >
      <template #cell-status="{ row }"><NvStatusBadge :value="row.status" /></template>
      <template #cell-scheduleStatus="{ row }">
        <!-- 失效任务:橙色警示条 + 失效原因 + 系统已发起(非"已送达")计划员重排通知(后端 SchedulePlanInvalidated→Notification intent) -->
        <div
          v-if="isScheduleInvalidated(row.status)"
          class="grid gap-1 rounded-md border-l-2 border-warning bg-warning/10 px-2 py-1.5"
        >
          <NvStatusBadge label="排程已失效" tone="warning" />
          <p class="text-xs text-foreground">
            {{ describeScheduleInvalidationReason(row.scheduleInvalidationReasonCode) }}
          </p>
          <p class="text-xs text-muted-foreground">
            系统已自动发起计划员重排通知，待重新排程后可派工。
          </p>
        </div>
        <NvStatusBadge
          v-else
          :label="resolveScheduleStatus(row).label"
          :tone="resolveScheduleStatus(row).tone"
        />
      </template>
      <template #cell-plannedStartUtc="{ row }">{{ formatDateTime(row.plannedStartUtc) }}</template>
      <template #cell-blockingReasons="{ row }">
        <div v-if="row.blockingReasons?.length" class="grid gap-2">
          <div
            v-for="reason in readinessList(row.blockingReasons)"
            :key="`${row.operationTaskId}-${reason.code}`"
            class="grid gap-0.5"
          >
            <NvStatusBadge :label="reason.label" tone="warning" />
            <p class="text-xs text-muted-foreground">{{ reason.nextStep }}</p>
          </div>
        </div>
        <span v-else class="text-muted-foreground">可派工</span>
      </template>
      <template #cell-actions="{ row }">
        <NvRowActions :label="`派工操作 ${row.operationTaskId ?? ''}`">
          <NvDropdownMenuItem :disabled="!canDispatch(row)" @click="openAssign(row)">
            <UserCheckIcon aria-hidden="true" />
            {{ dispatchActionLabel(row) }}
          </NvDropdownMenuItem>
        </NvRowActions>
      </template>
    </NvDataTable>

    <NvDialog v-model:open="assignOpen">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>派工 · 指派操作员</NvDialogTitle>
          <NvDialogDescription>
            为工单
            {{ assignTarget?.workOrderId ?? '' }} 的工序任务指派操作员；设备与班次沿用排程结果。
          </NvDialogDescription>
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="confirmAssign">
          <NvField>
            <NvFieldLabel for="assign-operator"
              >操作员 <span class="text-destructive">*</span></NvFieldLabel
            >
            <NvSelect v-model="assignedUserId">
              <NvSelectTrigger id="assign-operator"
                ><NvSelectValue placeholder="选择操作员"
              /></NvSelectTrigger>
              <NvSelectContent>
                <NvSelectItem v-for="o in workerOptions" :key="o.value" :value="o.value">{{
                  o.label
                }}</NvSelectItem>
              </NvSelectContent>
            </NvSelect>
          </NvField>
          <NvDialogFooter>
            <NvButton type="button" variant="outline" @click="assignOpen = false">取消</NvButton>
            <NvButton type="submit" :disabled="assignDispatchTaskPending || !assignedUserId">
              <Spinner v-if="assignDispatchTaskPending" aria-hidden="true" />
              确认派工
            </NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
