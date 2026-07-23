<script setup lang="ts">
import type {
  BusinessConsoleSchedulingAssignment,
  BusinessConsoleSchedulingConflict,
  BusinessConsoleSchedulingPlanSummaryResponse,
  BusinessConsoleSchedulingResourceLoad,
  BusinessConsoleSchedulingUnscheduledOperation,
  BusinessConsoleSchedulingPlanRevision,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { useBusinessScheduling } from '@/composables/useBusinessScheduling'
import { useOrderUrgencies } from '@/composables/useOrderUrgency'
import OrderUrgencyBadge from '@/components/urgency/OrderUrgencyBadge.vue'
import { describeScheduleInvalidationReason } from '@/composables/useScheduleInvalidation'
import {
  schedulingPlanStatusLabel,
  schedulingPlanStatusTone,
  schedulingPlanTerminalReleaseReason,
} from '@/utils/schedulingPlanPresentation'
import SchedulingPlanGantt from '@/components/scheduling/SchedulingPlanGantt.vue'
import SchedulingOrderPool from '@/components/scheduling/SchedulingOrderPool.vue'
import SchedulingDraftBoard from '@/components/scheduling/SchedulingDraftBoard.vue'
import ScheduleRevisionReview from '@/components/scheduling/ScheduleRevisionReview.vue'
import { useSchedulingWorkbench } from '@/composables/useSchedulingWorkbench'
import { useWorkingScheduleDraft } from '@/composables/useWorkingScheduleDraft'
import { useAuthStore } from '@/stores/auth'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
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
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  Spinner,
  NvStatusBadge,
  NvTabs,
  NvTabsContent,
  NvTabsList,
  NvTabsTrigger,
  toast,
} from '@nerv-iip/ui'
import { EyeIcon, RefreshCwIcon, SendIcon } from '@lucide/vue'
import { computed, shallowRef, watch } from 'vue'
import { useRoute } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '排产工作台',
    requiredPermissions: ['business.scheduling.plans.read'],
  },
})

const {
  detailSelection,
  filters: schedulingFilters,
  planDetail,
  planDetailError,
  planDetailPending,
  plans,
  plansPending,
  refreshPlans,
  releasePlan,
  releasePlanPending,
} = useBusinessScheduling()
const auth = useAuthStore()
const permissionCodes = computed(() => auth.principal?.permissionCodes ?? [])
const canManage = computed(() => permissionCodes.value.includes(P.schedulingPlansManage))
const canPublish = computed(() => permissionCodes.value.includes(P.schedulingPlansRelease))
const workbench = useSchedulingWorkbench()
const draft = useWorkingScheduleDraft(computed(() => !canManage.value))
const revisionResult = shallowRef<BusinessConsoleSchedulingPlanRevision>()
const route = useRoute()
const orderUrgencies = useOrderUrgencies(
  computed(() => (planDetail.value?.assignments ?? []).map((assignment) => assignment.orderId)),
)

const activeView = shallowRef('table')
const detailOpen = shallowRef(false)
const targetedOrderReference = computed(() => {
  const value = route.query.orderReference
  return (Array.isArray(value) ? value[0] : value)?.trim() ?? ''
})
const routeLookupVisited = new Set<string>()

watch(workbench.schedulableCandidates, (candidates) => draft.setOrders(candidates), {
  immediate: true,
})
const actionablePlans = computed(() =>
  plans.value.filter(
    (plan): plan is BusinessConsoleSchedulingPlanSummaryResponse & { planId: string } =>
      Boolean(plan.planId),
  ),
)

watch([activeView, actionablePlans], ([view, availablePlans]) => {
  if (view !== 'gantt' || detailSelection.planId || availablePlans.length === 0) return
  detailSelection.planId = availablePlans[0]?.planId ?? ''
})

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
const selectedPlanSummary = computed(() =>
  actionablePlans.value.find((plan) => plan.planId === detailSelection.planId),
)
const targetedAssignmentFound = computed(() =>
  Boolean(
    targetedOrderReference.value &&
    planDetail.value?.assignments?.some(
      (assignment) => assignment.orderId === targetedOrderReference.value,
    ),
  ),
)

watch(targetedOrderReference, () => routeLookupVisited.clear())
watch(
  [targetedOrderReference, actionablePlans, planDetail, planDetailPending],
  ([target, availablePlans, detail, pending]) => {
    if (!target || availablePlans.length === 0 || pending) return
    if (!detailSelection.planId) {
      detailSelection.planId = availablePlans[0]?.planId ?? ''
      detailOpen.value = Boolean(detailSelection.planId)
      return
    }
    if (!detail || detail.planId !== detailSelection.planId) return
    if (detail.assignments?.some((assignment) => assignment.orderId === target)) {
      detailOpen.value = true
      return
    }

    routeLookupVisited.add(detailSelection.planId)
    const next = availablePlans.find((plan) => !routeLookupVisited.has(plan.planId))
    if (next?.planId) {
      detailSelection.planId = next.planId
      detailOpen.value = true
    }
  },
  { immediate: true },
)

function rowKey(row: BusinessConsoleSchedulingPlanSummaryResponse) {
  return row.planId ?? row.problemId ?? 'plan'
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
  const summary = actionablePlans.value.find((plan) => plan.planId === planId)
  if (!summary || !canRelease(summary)) return

  try {
    await releasePlan(planId)
    toast.success('排程方案已发布')
  } catch {
    toast.error('发布失败，请稍后重试')
  }
}

async function generateWorkbenchPlan() {
  if (!canManage.value || draft.includedOrders.value.length === 0) return
  const horizonStart = new Date()
  horizonStart.setMinutes(0, 0, 0)
  const horizonEnd = new Date(horizonStart)
  horizonEnd.setDate(horizonEnd.getDate() + 7)
  try {
    const plan = await workbench.generatePlan({
      organizationId: schedulingFilters.organizationId,
      environmentId: schedulingFilters.environmentId,
      horizonStartUtc: horizonStart.toISOString(),
      horizonEndUtc: horizonEnd.toISOString(),
      orders: draft.includedOrders.value.map((order) => ({
        workOrderId: order.workOrderId,
        priority: order.priority,
        isRush: order.isRush,
      })),
    })
    draft.loadPlan(plan)
    detailSelection.planId = plan.planId ?? ''
    revisionResult.value = undefined
    toast.success('首版排程方案已生成')
  } catch {
    toast.error('生成失败，请检查工单生产版本与排程基础数据')
  }
}

async function repreviewLockedDraft() {
  const planId = draft.model.value?.meta.planId
  if (!canManage.value || !planId || draft.includedOrders.value.length === 0) return
  if (draft.modifiedUnlockedTaskIds.value.length > 0) {
    toast.error('有未锁定的人工修改；请先锁定全部修改再重预览')
    return
  }
  try {
    const revision = await workbench.revisePlan(planId, {
      organizationId: schedulingFilters.organizationId,
      environmentId: schedulingFilters.environmentId,
      includedOrderIds: draft.includedOrders.value.map((order) => order.workOrderId),
      lockedAssignments: draft.lockedAssignments.value,
    })
    revisionResult.value = revision
    if (revision.candidate) {
      draft.loadPlan(revision.candidate, revision.impact)
      detailSelection.planId = revision.candidate.planId ?? ''
    }
    toast.success('已生成锁定约束下的新版本')
  } catch {
    toast.error('重预览失败，请检查锁定资源与时间窗口')
  }
}

function onLockedDragAttempt() {
  toast.error('该工序已锁定；请先解锁再调整资源或时间')
}

async function publishCandidate() {
  const planId = draft.model.value?.meta.planId
  if (!canPublish.value || !planId) return
  detailSelection.planId = planId
  try {
    await releasePlan(planId)
    toast.success('新版排程已发布')
  } catch {
    toast.error('发布失败；失效或终态方案不能发布')
  }
}

// 已终止或失效的方案禁止发布，避免重复下达或下达一份过期计划。
function canRelease(row: BusinessConsoleSchedulingPlanSummaryResponse) {
  return canPublish.value && !schedulingPlanTerminalReleaseReason(row.status) && !row.isInvalidated
}

function releaseDisabledReason(row: BusinessConsoleSchedulingPlanSummaryResponse) {
  if (!canPublish.value) return '当前账号没有排程发布权限'
  const terminalReason = schedulingPlanTerminalReleaseReason(row.status)
  if (terminalReason) return terminalReason
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
    tooling: '工装约束',
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
      :count="`${actionablePlans.length} 个方案`"
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
        <NvTabsTrigger value="workbench">领导演示工作台</NvTabsTrigger>
        <NvTabsTrigger value="table">表格</NvTabsTrigger>
        <NvTabsTrigger value="gantt">甘特图</NvTabsTrigger>
      </NvTabsList>

      <NvTabsContent value="workbench" class="grid gap-4">
        <div
          class="flex flex-wrap items-center justify-between gap-3 rounded-lg border bg-card p-4"
        >
          <div>
            <p class="font-semibold">批量待排 → 编辑锁定 → 重预览 → 对比发布</p>
            <p class="text-sm text-muted-foreground">
              已选择 {{ draft.includedOrders.value.length }} 个工单，锁定
              {{ draft.lockedAssignments.value.length }} 道工序。
            </p>
          </div>
          <div class="flex flex-wrap gap-2">
            <NvButton
              size="sm"
              variant="ghost"
              type="button"
              :disabled="!draft.canUndo.value"
              @click="draft.undo"
              >撤销</NvButton
            >
            <NvButton
              size="sm"
              variant="ghost"
              type="button"
              :disabled="!draft.canRedo.value"
              @click="draft.redo"
              >重做</NvButton
            >
            <NvButton
              size="sm"
              variant="outline"
              type="button"
              :disabled="
                !canManage ||
                draft.includedOrders.value.length === 0 ||
                workbench.generatePending.value
              "
              @click="generateWorkbenchPlan"
            >
              <Spinner v-if="workbench.generatePending.value" aria-hidden="true" />生成首版
            </NvButton>
            <NvButton
              size="sm"
              variant="outline"
              type="button"
              :disabled="!canManage || !draft.model.value || workbench.revisionPending.value"
              @click="repreviewLockedDraft"
            >
              <Spinner v-if="workbench.revisionPending.value" aria-hidden="true" />锁定重预览
            </NvButton>
            <NvButton
              size="sm"
              type="button"
              :disabled="!canPublish || !draft.model.value || releasePlanPending"
              @click="publishCandidate"
            >
              <SendIcon aria-hidden="true" />发布新版
            </NvButton>
          </div>
        </div>

        <p
          v-if="!canManage"
          class="rounded-md border border-warning/30 bg-warning/10 p-3 text-sm"
          role="status"
        >
          当前账号只有读取权限，可查看历史方案但不能编辑或生成新版本。
        </p>
        <div
          v-if="draft.modifiedUnlockedTaskIds.value.length > 0"
          class="flex flex-wrap items-center justify-between gap-3 rounded-md border border-warning/30 bg-warning/10 p-3 text-sm"
          role="status"
        >
          <span>
            {{ draft.modifiedUnlockedTaskIds.value.length }}
            道人工修改尚未锁定；重预览前需锁定，避免修改被候选方案覆盖。
          </span>
          <NvButton
            size="sm"
            variant="outline"
            type="button"
            :disabled="!canManage"
            @click="draft.lockModifiedTasks"
            >锁定全部修改</NvButton
          >
        </div>
        <SchedulingOrderPool
          :candidates="workbench.schedulableCandidates.value"
          :draft-orders="draft.orders.value"
          :loading="workbench.candidatesPending.value"
          :read-only="!canManage"
          @include="draft.setIncluded"
          @update="draft.updateOrder"
        />
        <SchedulingDraftBoard
          :model="draft.model.value"
          :pending-operations="draft.pendingOperations.value"
          :read-only="!canManage"
          @move="draft.moveTask"
          @update="draft.updateTask"
          @lock="draft.setLocked"
          @locked-attempt="onLockedDragAttempt"
          @move-to-pending="draft.moveTaskToPending"
          @restore-pending="draft.restorePendingTask"
        />
        <ScheduleRevisionReview :revision="revisionResult" />
      </NvTabsContent>

      <NvTabsContent value="table" class="grid gap-4">
        <NvDataTable
          :pagination="false"
          :columns="columns"
          :rows="actionablePlans"
          :row-key="rowKey"
          :loading="plansPending"
          :searchable="false"
          :column-settings="false"
          empty-message="暂无 APS 排程方案。请先通过排程服务生成方案。"
        >
          <template #cell-status="{ row }">
            <div class="flex flex-wrap items-center gap-1.5">
              <NvStatusBadge
                :label="schedulingPlanStatusLabel(row.status)"
                :tone="schedulingPlanStatusTone(row.status)"
              />
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
        <div class="mb-4 flex flex-wrap items-center gap-3 rounded-lg border bg-card p-3">
          <label for="gantt-plan-select" class="text-sm font-medium text-foreground"
            >排程方案</label
          >
          <NvSelect v-model="detailSelection.planId">
            <NvSelectTrigger id="gantt-plan-select" class="w-full sm:w-80" aria-label="排程方案">
              <NvSelectValue placeholder="选择排程方案" />
            </NvSelectTrigger>
            <NvSelectContent>
              <NvSelectItem
                v-for="plan in actionablePlans"
                :key="rowKey(plan)"
                :value="plan.planId"
              >
                {{ plan.planId }} · {{ schedulingPlanStatusLabel(plan.status) }}
              </NvSelectItem>
            </NvSelectContent>
          </NvSelect>
        </div>
        <SchedulingPlanGantt
          :plan="planDetail"
          :summary="selectedPlanSummary"
          :loading="planDetailPending"
          :error="planDetailError"
          :release-pending="releasePlanPending"
          @open-detail="detailOpen = true"
          @release="publish(detailSelection.planId)"
        />
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

        <p
          v-if="targetedOrderReference"
          class="mt-4 rounded-md border border-primary/30 bg-primary/5 px-3 py-2 text-sm text-foreground"
          role="status"
        >
          {{
            targetedAssignmentFound
              ? `已定位订单 ${targetedOrderReference}`
              : `正在定位订单 ${targetedOrderReference}`
          }}
        </p>

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
                :label="schedulingPlanStatusLabel(planDetail.status)"
                :tone="schedulingPlanStatusTone(planDetail.status)"
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
                :class="{
                  'border-primary/50 bg-primary/5': assignment.orderId === targetedOrderReference,
                }"
                :data-targeted-order="
                  assignment.orderId === targetedOrderReference ? 'true' : undefined
                "
              >
                <div class="flex items-center justify-between gap-3">
                  <p class="text-sm font-medium text-foreground">
                    {{ assignmentText(assignment) }}
                  </p>
                  <OrderUrgencyBadge
                    :order-reference="assignment.orderId ?? ''"
                    :urgency="
                      assignment.orderId
                        ? orderUrgencies.byReference.value.get(assignment.orderId)
                        : undefined
                    "
                  />
                </div>
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
