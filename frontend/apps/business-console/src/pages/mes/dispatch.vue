<script setup lang="ts">
import type { NvDataTableColumn } from '@nerv-iip/ui'
import CarriedContextSummary from '@/components/business/CarriedContextSummary.vue'
import { useBusinessWorkers } from '@/composables/useBusinessMasterData'
import { describeMesReadinessReason, useMesDispatchTasks } from '@/composables/useBusinessMes'
import {
  describeScheduleInvalidationReason,
  isScheduleInvalidated,
  resolveScheduleStatus,
} from '@/composables/useScheduleInvalidation'
import { pagedBreakdownSegments } from '@/composables/metricSegments'
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
  NvMetricCard,
  NvPageHeader,
  NvRowActions,
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
// 派工候选只取在岗员工；默认按所选工序的工作中心收敛（工作中心 → 所辖班组 → 班组成员）。
const {
  workers,
  workersPending,
  filters: workerFilters,
} = useBusinessWorkers({
  employmentStatus: 'active',
})
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
// 派工的决策点是「能派多少、卡住多少」——一张构成卡替代三张各说一半的卡。
const dispatchSegments = computed(() =>
  pagedBreakdownSegments(dispatchTasksTotal.value, [
    { key: 'blocked', label: '有阻塞', value: blockedCount.value, tone: 'danger' },
    { key: 'dispatchable', label: '可派工', value: dispatchableCount.value, tone: 'success' },
  ]),
)
const errorMessage = computed(() => formatError(dispatchTasksError.value))

type DispatchRow = (typeof dispatchTasks)['value'][number]

// 操作员选项：value=userId（与 assignedUserId 同源），label=姓名 · 工号（技能）。
// 有技能登记的排在前面——同一工作中心内优先派给有对应技能的人。
const workerOptions = computed(() =>
  workers.value
    .filter((w) => w.userId)
    .slice()
    .sort((a, b) => (b.skills?.length ?? 0) - (a.skills?.length ?? 0))
    .map((w) => {
      const skills = (w.skills ?? []).map((s) => s.skillName).filter(Boolean)
      const suffix = skills.length > 0 ? `（${skills.join('、')}）` : ''
      const base = w.employeeNo
        ? `${w.displayName ?? w.userId} · ${w.employeeNo}`
        : (w.displayName ?? (w.userId as string))
      return { value: w.userId as string, label: `${base}${suffix}` }
    }),
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
  {
    key: 'assignedUserName',
    header: '受派工人',
    width: 'w-32',
    accessor: (r) => r.assignedUserName ?? (r.assignedUserId ? '未知工人' : '未派工'),
  },
  { key: 'plannedStartUtc', header: '计划开始', width: 'w-44' },
  { key: 'blockingReasons', header: '阻塞处理' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
]

// ── 派工（指派操作员）─────────────────────────────────────────────
const assignOpen = shallowRef(false)
const assignTarget = shallowRef<DispatchRow | null>(null)
const assignedUserId = ref('')
// 点提交才标红（create-dialog 硬规则）：未选操作员时不禁用按钮，而是标红 + 提示且不发请求。
const assignShowErrors = ref(false)

// 派工弹窗的只读上下文：全部由所选行带出（设备/班次沿用排程结果，不在此变更）。
const assignContextItems = computed(() => {
  const row = assignTarget.value
  if (!row) return []
  return [
    { label: '工序任务', value: row.operationTaskNo ?? row.operationTaskId },
    { label: '工单', value: row.workOrderNo ?? row.workOrderId },
    {
      label: '工作中心',
      value: row.workCenterName ?? resolveWorkCenter(row.workCenterCode ?? row.workCenterId),
    },
    { label: '设备', value: row.deviceAssetName ?? row.deviceAssetCode ?? row.deviceAssetId },
    { label: '班次', value: row.shiftId },
    { label: '计划开始', value: formatDateTime(row.plannedStartUtc) },
  ]
})

function canDispatch(row: DispatchRow) {
  return (
    Boolean(row.operationTaskId) &&
    !row.blockingReasons?.length &&
    !isScheduleInvalidated(row.status)
  )
}
// 候选范围：默认「本工作中心班组」，找不到人时可显式切到「全部在岗员工」——不做静默兜底。
const candidateScope = shallowRef<'work-center' | 'all'>('work-center')
const targetWorkCenterCode = computed(
  () => assignTarget.value?.workCenterCode ?? assignTarget.value?.workCenterId ?? undefined,
)
watch([candidateScope, targetWorkCenterCode, assignOpen], () => {
  workerFilters.workCenterCode =
    assignOpen.value && candidateScope.value === 'work-center'
      ? targetWorkCenterCode.value
      : undefined
})

// 候选是服务端按工作中心收敛后才回来的，所以「只有一个人就直接选中」必须等列表落地再判，
// 不能在 openAssign 里读上一轮的候选（会把上一个工作中心的人预选进来）。
watch(workerOptions, (options) => {
  if (!assignOpen.value) return
  if (options.length === 1) {
    assignedUserId.value = options[0]!.value
    return
  }

  // 切换候选范围后原选中项可能已不在候选内，清掉避免提交一个不在范围里的人。
  if (assignedUserId.value && !options.some((option) => option.value === assignedUserId.value)) {
    assignedUserId.value = ''
  }
})

function openAssign(row: DispatchRow) {
  if (!canDispatch(row)) return
  assignTarget.value = row
  assignedUserId.value = ''
  assignShowErrors.value = false
  candidateScope.value = 'work-center'
  assignOpen.value = true
}
async function confirmAssign() {
  // 点提交才校验：未选操作员则标红 + 提示，不发请求。
  assignShowErrors.value = true
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

    <NvMetricCard
      class="sm:max-w-md"
      variant="breakdown"
      label="待派工序"
      :value="dispatchTasksTotal"
      unit="个"
      :segments="dispatchSegments"
    />

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
          <NvDialogTitle>派工</NvDialogTitle>
          <!-- 派工对象已在下方只读区完整呈现；此处仅供读屏播报。 -->
          <NvDialogDescription class="sr-only">
            派工对象：工序任务
            {{ assignTarget?.operationTaskNo ?? assignTarget?.operationTaskId }}。
          </NvDialogDescription>
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="confirmAssign">
          <!-- 工序任务 / 工单 / 工作中心 / 设备 / 班次 / 计划开始全部由所选行带出，只读呈现；班组长只挑操作员。 -->
          <CarriedContextSummary label="派工对象" :items="assignContextItems" />

          <NvField>
            <NvFieldLabel for="assign-scope">候选范围</NvFieldLabel>
            <NvSelect v-model="candidateScope">
              <NvSelectTrigger id="assign-scope"><NvSelectValue /></NvSelectTrigger>
              <NvSelectContent>
                <NvSelectItem value="work-center">本工作中心班组</NvSelectItem>
                <NvSelectItem value="all">全部在岗员工</NvSelectItem>
              </NvSelectContent>
            </NvSelect>
          </NvField>
          <NvField>
            <NvFieldLabel for="assign-operator"
              >操作员 <span class="text-destructive">*</span></NvFieldLabel
            >
            <NvSelect v-model="assignedUserId" :disabled="workerOptions.length === 0">
              <NvSelectTrigger
                id="assign-operator"
                :data-invalid="assignShowErrors && !assignedUserId ? '' : undefined"
                ><NvSelectValue placeholder="选择操作员"
              /></NvSelectTrigger>
              <NvSelectContent>
                <NvSelectItem v-for="o in workerOptions" :key="o.value" :value="o.value">{{
                  o.label
                }}</NvSelectItem>
              </NvSelectContent>
            </NvSelect>
            <p
              v-if="!workersPending && workerOptions.length === 0"
              class="text-sm text-muted-foreground"
            >
              {{
                candidateScope === 'work-center'
                  ? '该工作中心暂无在岗班组成员，可切换到「全部在岗员工」。'
                  : '暂无在岗员工，请先在「基础数据 · 员工」维护。'
              }}
            </p>
          </NvField>
          <!-- 点提交才标红；未选操作员不发请求。 -->
          <p
            v-if="assignShowErrors && !assignedUserId"
            class="text-sm text-destructive"
            role="alert"
          >
            请选择操作员（已标红）。
          </p>
          <NvDialogFooter>
            <NvButton type="button" variant="outline" @click="assignOpen = false">取消</NvButton>
            <NvButton type="submit" :disabled="assignDispatchTaskPending">
              <Spinner v-if="assignDispatchTaskPending" aria-hidden="true" />
              <UserCheckIcon v-else aria-hidden="true" />
              派工
            </NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
