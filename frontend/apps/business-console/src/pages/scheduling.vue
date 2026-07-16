<script setup lang="ts">
import type {
  BusinessConsoleSchedulePlan,
  BusinessConsoleSchedulingAssignment,
  BusinessConsoleSchedulingConflict,
  BusinessConsoleSchedulingPlanSummaryResponse,
  BusinessConsoleSchedulingResourceLoad,
  BusinessConsoleSchedulingUnscheduledOperation,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { useBusinessScheduling } from '@/composables/useBusinessScheduling'
import { describeScheduleInvalidationReason } from '@/composables/useScheduleInvalidation'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvButton,
  NvDataTable,
  NvPageHeader,
  NvSheet,
  NvSheetContent,
  NvSheetDescription,
  NvSheetHeader,
  NvSheetTitle,
  Spinner,
  NvStatusBadge,
  NvTabs,
  NvTabsContent,
  NvTabsList,
  NvTabsTrigger,
  toast,
} from '@nerv-iip/ui'
import { CalendarClockIcon, EyeIcon, RefreshCwIcon, SendIcon } from '@lucide/vue'
import { computed, shallowRef } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: '排产工作台',
    requiredPermissions: ['business.scheduling.plans.read', 'business.scheduling.plans.release'],
  },
})

const {
  detailSelection,
  planDetail,
  planDetailError,
  planDetailPending,
  plans,
  plansPending,
  refreshPlans,
  releasePlan,
  releasePlanPending,
} = useBusinessScheduling()

const activeView = shallowRef('table')
const detailOpen = shallowRef(false)

const columns: NvDataTableColumn<BusinessConsoleSchedulingPlanSummaryResponse>[] = [
  {
    key: 'planId',
    header: '排程方案',
    cellClass: 'font-medium',
    accessor: (row) => row.planId ?? '未命名方案',
  },
  { key: 'status', header: '状态', width: 'w-40' },
  { key: 'range', header: '时间范围', accessor: () => '明细中确认' },
  { key: 'invalidation', header: '失效原因', accessor: invalidationSummary },
  {
    key: 'operationCount',
    header: '工序数',
    accessor: (row) => `${row.assignmentCount ?? 0} 道工序`,
  },
  { key: 'conflicts', header: '冲突摘要', accessor: conflictSummary },
  { key: 'generatedAtUtc', header: '创建时间', width: 'w-44' },
  { key: 'actions', header: '操作', width: 'w-40', align: 'end' },
]

const selectedPlanRange = computed(() => rangeFromAssignments(planDetail.value?.assignments ?? []))
const selectedResourceCount = computed(() => {
  const resourceIds = new Set(
    (planDetail.value?.resourceLoads ?? [])
      .map((load) => load.resourceId)
      .filter((value): value is string => Boolean(value)),
  )
  return resourceIds.size
})
const detailFeedback = computed(() => {
  if (planDetailError.value) return '明细加载失败，请稍后重试。'
  if (detailSelection.planId) return '未返回方案明细。'
  return '请选择一个排程方案查看明细。'
})

function rowKey(row: BusinessConsoleSchedulingPlanSummaryResponse) {
  return row.planId ?? row.problemId ?? 'plan'
}

function statusLabel(status?: string | null) {
  if (status === 'preview') return '预览'
  if (status === 'generated') return '已生成'
  if (status === 'released') return '已发布'
  return status ?? '未知'
}

function statusTone(status?: string | null) {
  if (status === 'released') return 'success'
  if (status === 'generated') return 'warning'
  return 'neutral'
}

function invalidationSummary(row: BusinessConsoleSchedulingPlanSummaryResponse) {
  return row.isInvalidated
    ? describeScheduleInvalidationReason(row.latestInvalidationReasonCode)
    : '—'
}

function conflictSummary(row: BusinessConsoleSchedulingPlanSummaryResponse) {
  const conflicts = row.conflictCount ?? 0
  const unscheduled = row.unscheduledOperationCount ?? 0
  if (conflicts === 0 && unscheduled === 0) return '无冲突'

  return [
    conflicts > 0 ? `${conflicts} 项冲突` : '',
    unscheduled > 0 ? `${unscheduled} 项未排` : '',
  ]
    .filter(Boolean)
    .join('，')
}

function formatDateTime(value?: string | null) {
  if (!value) return '无'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}

function rangeFromAssignments(assignments: BusinessConsoleSchedulingAssignment[]) {
  const timestamps = assignments
    .flatMap((assignment) => [assignment.startUtc, assignment.endUtc])
    .filter((value): value is string => Boolean(value))
    .map((value) => new Date(value))
    .filter((date) => !Number.isNaN(date.getTime()))
    .sort((a, b) => a.getTime() - b.getTime())

  if (timestamps.length === 0) return '无'

  return `${timestamps[0]!.toLocaleString()} 至 ${timestamps[timestamps.length - 1]!.toLocaleString()}`
}

function openDetail(planId: string | undefined) {
  if (!planId) return
  detailSelection.planId = planId
  detailOpen.value = true
}

async function publish(planId: string | undefined) {
  if (!planId) return

  try {
    await releasePlan(planId)
    toast.success('排程方案已发布')
  } catch {
    toast.error('发布失败，请稍后重试')
  }
}

function isReleased(
  row: BusinessConsoleSchedulingPlanSummaryResponse | BusinessConsoleSchedulePlan | undefined,
) {
  return row?.status === 'released'
}

// 失效方案禁止发布类操作：排程前提已变化，须先重排再发布，否则会下达一份过期计划。
function canRelease(row: BusinessConsoleSchedulingPlanSummaryResponse) {
  return !isReleased(row) && !row.isInvalidated
}

function releaseDisabledReason(row: BusinessConsoleSchedulingPlanSummaryResponse) {
  if (isReleased(row)) return '方案已发布'
  if (row.isInvalidated)
    return `方案已失效（${describeScheduleInvalidationReason(row.latestInvalidationReasonCode)}），请重排后再发布`
  return '发布该排程方案'
}

function loadText(load: BusinessConsoleSchedulingResourceLoad) {
  const assigned = load.assignedMinutes ?? 0
  const available = load.availableMinutes ?? 0
  const utilization =
    load.utilization === undefined ? '无' : `${Math.round(load.utilization * 100)}%`
  return `${assigned} / ${available} 分钟，利用率 ${utilization}`
}

function assignmentText(assignment: BusinessConsoleSchedulingAssignment) {
  return [
    assignment.orderId ?? '未关联工单',
    assignment.operationSequence ? `第 ${assignment.operationSequence} 道` : '工序',
    assignment.workCenterId ?? assignment.resourceId ?? '未分配资源',
  ].join(' · ')
}

function conflictText(conflict: BusinessConsoleSchedulingConflict) {
  return [
    severityLabel(conflict.severity),
    reasonLabel(conflict.reasonCode),
    conflict.message ?? '',
  ]
    .filter(Boolean)
    .join(' · ')
}

function unscheduledText(item: BusinessConsoleSchedulingUnscheduledOperation) {
  return [
    item.orderId ?? '未关联工单',
    item.operationId ?? '工序',
    reasonLabel(item.reasonCode),
    item.message ?? '',
  ]
    .filter(Boolean)
    .join(' · ')
}

function severityLabel(severity?: string | null) {
  if (severity === 'info') return '提示'
  if (severity === 'warning') return '预警'
  if (severity === 'error') return '阻断'
  return ''
}

function reasonLabel(reason?: string | null) {
  const labels: Record<string, string> = {
    dueDate: '交期风险',
    capacity: '产能不足',
    calendar: '日历不可用',
    material: '物料约束',
    quality: '质量约束',
    equipment: '设备约束',
    noEligibleResource: '无可用资源',
    outsideHorizon: '超出排程窗口',
    invalidLockedAssignment: '锁定分配无效',
    predecessorUnscheduled: '前序未排',
  }

  return reason ? (labels[reason] ?? reason) : ''
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="排产工作台"
      :breadcrumbs="[{ label: '需求与计划' }]"
      :count="`${plans.length} 个方案`"
    >
      <template #actions>
        <NvButton size="sm" variant="outline" type="button" @click="refreshPlans">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <NvTabs v-model="activeView">
      <NvTabsList>
        <NvTabsTrigger value="table">表格</NvTabsTrigger>
        <NvTabsTrigger value="gantt">甘特图</NvTabsTrigger>
      </NvTabsList>

      <NvTabsContent value="table" class="grid gap-4">
        <NvDataTable
          :pagination="false"
          :columns="columns"
          :rows="plans"
          :row-key="rowKey"
          :loading="plansPending"
          :searchable="false"
          :column-settings="false"
          empty-message="暂无 APS 排程方案。请先通过排程服务生成方案。"
        >
          <template #cell-status="{ row }">
            <div class="flex flex-wrap items-center gap-1.5">
              <NvStatusBadge :label="statusLabel(row.status)" :tone="statusTone(row.status)" />
              <NvStatusBadge v-if="row.isInvalidated" label="已失效" tone="warning" />
            </div>
          </template>
          <template #cell-invalidation="{ row }">
            <span v-if="row.isInvalidated" class="text-sm text-warning-strong">
              {{ describeScheduleInvalidationReason(row.latestInvalidationReasonCode) }}
            </span>
            <span v-else class="text-muted-foreground">—</span>
          </template>
          <template #cell-generatedAtUtc="{ row }">
            {{ formatDateTime(row.generatedAtUtc) }}
          </template>
          <template #cell-actions="{ row }">
            <div class="flex justify-end gap-2">
              <NvButton size="sm" variant="outline" type="button" @click="openDetail(row.planId)">
                <EyeIcon aria-hidden="true" />
                明细
              </NvButton>
              <NvButton
                size="sm"
                type="button"
                :disabled="!canRelease(row) || releasePlanPending"
                :title="releaseDisabledReason(row)"
                @click="publish(row.planId)"
              >
                <Spinner v-if="releasePlanPending" aria-hidden="true" />
                <SendIcon v-else aria-hidden="true" />
                发布
              </NvButton>
            </div>
          </template>
        </NvDataTable>
      </NvTabsContent>

      <NvTabsContent value="gantt">
        <div class="rounded-lg border bg-card p-8 text-center">
          <CalendarClockIcon class="mx-auto size-10 text-muted-foreground" aria-hidden="true" />
          <h2 class="mt-4 text-base font-semibold text-foreground">甘特可视化待接入</h2>
          <p class="mx-auto mt-2 max-w-xl text-sm text-muted-foreground">
            当前版本只展示来自 APS facade
            的方案列表和明细。后续接入正式甘特组件后，此区域会替换为真实排程时间轴。
          </p>
        </div>
      </NvTabsContent>
    </NvTabs>

    <NvSheet v-model:open="detailOpen">
      <NvSheetContent side="right" class="w-full overflow-y-auto sm:max-w-3xl">
        <NvSheetHeader>
          <NvSheetTitle>排程方案明细</NvSheetTitle>
          <NvSheetDescription>
            {{ detailSelection.planId || '未选择方案' }}
          </NvSheetDescription>
        </NvSheetHeader>

        <div
          v-if="planDetailPending"
          class="mt-6 flex items-center gap-2 text-sm text-muted-foreground"
        >
          <Spinner aria-hidden="true" />
          正在读取方案明细
        </div>

        <div v-else-if="planDetail" class="mt-6 grid gap-6">
          <section class="grid gap-3 rounded-lg border bg-background p-4">
            <div class="flex flex-wrap items-center justify-between gap-3">
              <div>
                <h3 class="text-sm font-semibold text-foreground">计划概览</h3>
                <p class="mt-1 text-sm text-muted-foreground">{{ selectedPlanRange }}</p>
              </div>
              <NvStatusBadge
                :label="statusLabel(planDetail.status)"
                :tone="statusTone(planDetail.status)"
              />
            </div>
            <div class="grid gap-3 sm:grid-cols-4">
              <div>
                <p class="text-xs text-muted-foreground">资源数</p>
                <p class="text-sm font-medium text-foreground">{{ selectedResourceCount }}</p>
              </div>
              <div>
                <p class="text-xs text-muted-foreground">已排工序</p>
                <p class="text-sm font-medium text-foreground">
                  {{
                    planDetail.metrics?.scheduledOperationCount ??
                    planDetail.assignments?.length ??
                    0
                  }}
                </p>
              </div>
              <div>
                <p class="text-xs text-muted-foreground">未排工序</p>
                <p class="text-sm font-medium text-foreground">
                  {{
                    planDetail.metrics?.unscheduledOperationCount ??
                    planDetail.unscheduledOperations?.length ??
                    0
                  }}
                </p>
              </div>
              <div>
                <p class="text-xs text-muted-foreground">负荷分钟</p>
                <p class="text-sm font-medium text-foreground">
                  {{ planDetail.metrics?.assignedMinutes ?? 0 }}
                </p>
              </div>
            </div>
          </section>

          <section class="grid gap-3">
            <h3 class="text-sm font-semibold text-foreground">资源分配</h3>
            <div v-if="planDetail.assignments?.length" class="grid gap-2">
              <div
                v-for="assignment in planDetail.assignments"
                :key="assignment.assignmentId ?? assignmentText(assignment)"
                class="rounded-md border bg-background p-3"
              >
                <p class="text-sm font-medium text-foreground">{{ assignmentText(assignment) }}</p>
                <p class="mt-1 text-sm text-muted-foreground">
                  {{ formatDateTime(assignment.startUtc) }} 至
                  {{ formatDateTime(assignment.endUtc) }}
                </p>
              </div>
            </div>
            <p v-else class="rounded-md border bg-muted/30 p-3 text-sm text-muted-foreground">
              暂无资源分配。
            </p>
          </section>

          <section class="grid gap-3">
            <h3 class="text-sm font-semibold text-foreground">资源负荷</h3>
            <div v-if="planDetail.resourceLoads?.length" class="grid gap-2">
              <div
                v-for="load in planDetail.resourceLoads"
                :key="load.resourceId ?? load.windowStartUtc"
                class="rounded-md border bg-background p-3"
              >
                <p class="text-sm font-medium text-foreground">
                  {{ load.resourceId ?? '未命名资源' }}
                </p>
                <p class="mt-1 text-sm text-muted-foreground">{{ loadText(load) }}</p>
              </div>
            </div>
            <p v-else class="rounded-md border bg-muted/30 p-3 text-sm text-muted-foreground">
              暂无资源负荷。
            </p>
          </section>

          <section class="grid gap-3">
            <h3 class="text-sm font-semibold text-foreground">冲突与不可排原因</h3>
            <div
              v-if="planDetail.conflicts?.length || planDetail.unscheduledOperations?.length"
              class="grid gap-2"
            >
              <p
                v-for="conflict in planDetail.conflicts ?? []"
                :key="conflict.conflictId ?? conflictText(conflict)"
                class="rounded-md border border-warning/30 bg-warning/10 p-3 text-sm text-foreground"
              >
                {{ conflictText(conflict) }}
              </p>
              <p
                v-for="item in planDetail.unscheduledOperations ?? []"
                :key="unscheduledText(item)"
                class="rounded-md border border-destructive/30 bg-destructive/10 p-3 text-sm text-foreground"
              >
                {{ unscheduledText(item) }}
              </p>
            </div>
            <p v-else class="rounded-md border bg-muted/30 p-3 text-sm text-muted-foreground">
              未返回冲突或不可排原因。
            </p>
          </section>
        </div>

        <div
          v-else
          class="mt-6 rounded-lg border bg-muted/30 p-4 text-sm text-muted-foreground"
          role="status"
        >
          {{ detailFeedback }}
        </div>
      </NvSheetContent>
    </NvSheet>
  </BusinessLayout>
</template>
