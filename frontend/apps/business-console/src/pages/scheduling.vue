<script setup lang="ts">
import { TaskFacts, toModel, withWorkOrderFacts } from '@nerv-iip/scheduling'
import type {
  BusinessConsoleSchedulingAssignment,
  BusinessConsoleSchedulingConflict,
  BusinessConsoleSchedulingPlanSummaryResponse,
  BusinessConsoleSchedulingResourceLoad,
  BusinessConsoleSchedulingUnscheduledOperation,
  BusinessConsoleSchedulingPlanRevision,
  BusinessConsoleSchedulePlan,
  BusinessConsoleSchedulingPlanStatus,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { formatDateTime } from '@/utils/format'
import {
  useBusinessScheduling,
  useSchedulingPlanSummary,
} from '@/composables/useBusinessScheduling'
import { useMesWorkOrderFacts } from '@/composables/useBusinessMes'
import { useOrderUrgencies } from '@/composables/useOrderUrgency'
import {
  DEFAULT_URGENCY_DISPLAY_MODE,
  orderRowsByUrgency,
  type UrgencyDisplayMode,
} from '@/composables/useUrgencyDisplayMode'
import OrderUrgencyBadge from '@/components/urgency/OrderUrgencyBadge.vue'
import UrgencyDisplayModeSelect from '@/components/urgency/UrgencyDisplayModeSelect.vue'
import { describeScheduleInvalidationReason } from '@/composables/useScheduleInvalidation'
import {
  schedulingPlanStatusLabel,
  schedulingPlanStatusTone,
  schedulingPlanTerminalReleaseReason,
} from '@/utils/schedulingPlanPresentation'
import SchedulingMaterialShortageSummary from '@/components/scheduling/SchedulingMaterialShortageSummary.vue'
import SchedulingPlanGantt from '@/components/scheduling/SchedulingPlanGantt.vue'
import SchedulingDaySchedule from '@/components/scheduling/SchedulingDaySchedule.vue'
import { useSchedulingPlanCsv } from '@/composables/useSchedulingPlanCsv'
import SchedulingHorizonFields from '@/components/scheduling/SchedulingHorizonFields.vue'
import {
  createSchedulingHorizonInput,
  describeSchedulingHorizon,
  resolveSchedulingHorizon,
} from '@/composables/schedulingHorizon'
import MesWorkScopeSelect from '@/components/mes/MesWorkScopeSelect.vue'
import {
  firstPlanCapacityReason,
  firstPlanSelectionReason,
} from '@/composables/schedulingFirstPlanCapacity'
import SchedulingOrderPool from '@/components/scheduling/SchedulingOrderPool.vue'
import SchedulingDraftClearDialog from '@/components/scheduling/SchedulingDraftClearDialog.vue'
import SchedulingDraftBoard from '@/components/scheduling/SchedulingDraftBoard.vue'
import ScheduleRevisionReview from '@/components/scheduling/ScheduleRevisionReview.vue'
import SchedulingCandidatePanel from '@/components/scheduling/SchedulingCandidatePanel.vue'
import { useSchedulingCandidates } from '@/composables/useSchedulingCandidates'
import SchedulingRushInsertionResult from '@/components/scheduling/SchedulingRushInsertionResult.vue'
import { useSchedulingRushInsertion } from '@/composables/useSchedulingRushInsertion'
import { useSchedulingWorkbench } from '@/composables/useSchedulingWorkbench'
import { useWorkingScheduleDraft } from '@/composables/useWorkingScheduleDraft'
import { useSchedulingDraftPersistence } from '@/composables/useSchedulingDraftPersistence'
import { useAuthStore } from '@/stores/auth'
import { notifyError, notifyOperationFailure } from '@/utils/notify'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvAlertDialog,
  NvAlertDialogCancel,
  NvAlertDialogContent,
  NvAlertDialogDescription,
  NvAlertDialogFooter,
  NvAlertDialogHeader,
  NvAlertDialogTitle,
  NvButton,
  NvInput,
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
import { EyeIcon, RefreshCwIcon, SendIcon, Undo2Icon } from '@lucide/vue'
import { computed, ref, shallowRef, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'

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
  plansTotal,
  page,
  pageSize,
  plansError,
  plansPending,
  refreshPlans,
  releasePlan,
  releasePlanPending,
  revokePlan,
  revokePlanPending,
  upsertOperationOverride,
  upsertOperationOverridePending,
} = useBusinessScheduling()
const csv = useSchedulingPlanCsv()
async function downloadPlanCsv() {
  try {
    await csv.download({ ...detailSelection })
    toast.success('方案 CSV 已下载')
  } catch (error) {
    notifyOperationFailure('方案 CSV 下载失败', error, '方案 CSV 下载失败，请稍后重试。')
  }
}

const historyStatus = computed({
  get: () => schedulingFilters.status ?? 'all',
  set: (value: string) => {
    schedulingFilters.status =
      value === 'all' ? undefined : (value as BusinessConsoleSchedulingPlanStatus)
  },
})
const historyInvalidated = computed({
  get: () =>
    schedulingFilters.isInvalidated === undefined ? 'all' : String(schedulingFilters.isInvalidated),
  set: (value: string) => {
    schedulingFilters.isInvalidated = value === 'all' ? undefined : value === 'true'
  },
})
const historyStatuses = ['generated', 'released', 'superseded', 'revoked'] as const
const hasHistoryFilters = computed(() =>
  Boolean(
    schedulingFilters.status ||
    schedulingFilters.releasedOn ||
    schedulingFilters.isInvalidated !== undefined,
  ),
)
function clearHistoryFilters() {
  schedulingFilters.status = undefined
  schedulingFilters.releasedOn = ''
  schedulingFilters.isInvalidated = undefined
}
const auth = useAuthStore()
const permissionCodes = computed(() => auth.principal?.permissionCodes ?? [])
const canManage = computed(() => permissionCodes.value.includes(P.schedulingPlansManage))
const canPublish = computed(() => permissionCodes.value.includes(P.schedulingPlansRelease))
const workbench = useSchedulingWorkbench()
const clearDraftConfirmOpen = ref(false)
const draftReady = shallowRef(false)
const draft = useWorkingScheduleDraft(computed(() => !canManage.value || !draftReady.value))
const { summary: draftPlanSummary } = useSchedulingPlanSummary(() => draft.model.value?.meta.planId)
const associatedWorkOrders = useMesWorkOrderFacts(() => {
  // 待排池已带出权威商业来源（null 表示没有来源）；500 单首版不再逐单重读同一事实。
  const known = new Set(
    workbench.candidates.value
      .filter((order) => order.commercialSourceFacts !== undefined)
      .map((order) => order.workOrderId),
  )
  return [
    ...(draft.model.value?.tasks
      .filter((task) => task.type === 'operation' && !task.blockKind)
      .map((task) => task.orderId) ?? []),
    ...(planDetail.value?.assignments ?? []).map((assignment) => assignment.orderId),
  ].filter((id) => !known.has(id))
})
watch(
  associatedWorkOrders.error,
  (error) => {
    if (error) notifyError(error, '关联工单事实暂不可读取，请稍后重试。')
  },
  { immediate: true },
)
const displayWorkOrders = computed(() => {
  const orders = new Map(workbench.candidates.value.map((order) => [order.workOrderId, order]))
  for (const facts of associatedWorkOrders.workOrders.value)
    orders.set(facts.workOrderId, { ...orders.get(facts.workOrderId), ...facts })
  return [...orders.values()]
})
const draftDisplayModel = computed(() =>
  draft.model.value
    ? withWorkOrderFacts(
        draft.model.value,
        displayWorkOrders.value,
        Boolean(associatedWorkOrders.error.value),
      )
    : undefined,
)
const detailTasks = computed(() =>
  planDetail.value
    ? withWorkOrderFacts(
        toModel(planDetail.value),
        displayWorkOrders.value,
        Boolean(associatedWorkOrders.error.value),
      ).tasks
    : [],
)
function assignmentTask(assignment: BusinessConsoleSchedulingAssignment) {
  return detailTasks.value.find(
    (task) =>
      task.type === 'operation' &&
      !task.blockKind &&
      task.orderId === assignment.orderId &&
      task.operationId === assignment.operationId,
  )
}
const persistedDraftPlan = shallowRef<BusinessConsoleSchedulePlan>()
const revisionBasePlan = shallowRef<BusinessConsoleSchedulePlan>()
const revisionResult = shallowRef<BusinessConsoleSchedulingPlanRevision>()
const route = useRoute()
const router = useRouter()
const persistence = useSchedulingDraftPersistence({
  draft,
  baseline: persistedDraftPlan,
  context: () => ({
    organizationId: schedulingFilters.organizationId,
    environmentId: schedulingFilters.environmentId,
  }),
  userId: () => auth.principal?.principalId,
  canManage,
  selectedPlanId: () =>
    typeof route.query.draftPlanId === 'string' ? route.query.draftPlanId : undefined,
  selectPlan: (planId) => {
    if (route.query.draftPlanId !== planId)
      void router.replace({ query: { ...route.query, draftPlanId: planId } })
  },
})
const candidateDraftBlockedReason = computed(() => {
  const plan = persistedDraftPlan.value
  if (!plan || !draft.model.value) return undefined
  const baseline = new Map(
    (plan.assignments ?? []).map((assignment) => [assignment.assignmentId, assignment]),
  )
  const tasks = draft.exportState().tasks!
  if (
    tasks.length !== baseline.size ||
    tasks.some((task) => {
      const original = baseline.get(task.taskId)
      return (
        !original ||
        original.resourceId !== task.resourceId ||
        original.startUtc !== task.startUtc ||
        original.endUtc !== task.endUtc ||
        Boolean(original.isLocked) !== task.locked ||
        JSON.stringify(original.segments ?? []) !== JSON.stringify(task.segments ?? [])
      )
    })
  )
    return '草稿有人工编辑，请先锁定重预览生成新基线，再生成局部候选。'
  return undefined
})
const localCandidates = useSchedulingCandidates({
  context: () => ({
    organizationId: schedulingFilters.organizationId,
    environmentId: schedulingFilters.environmentId,
  }),
  baselinePlanId: () => persistedDraftPlan.value?.planId,
  onSelected: async (selection) => {
    const previousPlan = persistedDraftPlan.value
    if (!(await persistence.restoreSelection(selection.workingDraft!))) return
    revisionBasePlan.value = previousPlan
    revisionResult.value = { candidate: selection.plan, comparison: selection.comparison }
    detailSelection.planId = selection.plan?.planId ?? ''
    await refreshPlans()
    toast.success('已选定候选并保存工作草稿，请核对后确认发布')
  },
})
watch(
  persistence.ready,
  (value) => {
    draftReady.value = value
  },
  { immediate: true },
)
const draftSaveStatus = computed(
  () =>
    ({
      loading: '正在恢复草稿…',
      empty: '尚无已保存草稿',
      saving: '正在保存草稿…',
      saved: persistence.savedAtUtc.value
        ? `草稿已保存 · ${formatDateTime(persistence.savedAtUtc.value)}`
        : '草稿已保存',
      clearing: '正在清空草稿…',
      error: '草稿操作未完成',
    })[persistence.status.value],
)

const orderUrgencies = useOrderUrgencies(
  computed(() => (planDetail.value?.assignments ?? []).map((assignment) => assignment.orderId)),
)
const displayMode = shallowRef<UrgencyDisplayMode>(DEFAULT_URGENCY_DISPLAY_MODE)
// 明细资源分配默认按统一紧急度排序（呈现层重排，不改动方案结果）。
const orderedAssignments = computed(() =>
  orderRowsByUrgency(
    planDetail.value?.assignments ?? [],
    (assignment) => assignment.orderId,
    orderUrgencies.byReference.value,
  ),
)
function refreshUrgency() {
  void orderUrgencies.refresh()
  refreshPlans()
}

// 进页面先落在排程总览：那是"挑工单 → 生成 → 锁定 → 发布"的主线入口。
// 表格是历史方案的查阅面，新环境下往往是空表，不该是第一眼看到的东西。
const activeView = shallowRef('workbench')
const detailOpen = shallowRef(false)
const targetedOrderReference = computed(() => {
  const value = route.query.orderReference
  return (Array.isArray(value) ? value[0] : value)?.trim() ?? ''
})
const routeLookupVisited = new Set<string>()
// 单单排产（MAN-694 / #1262）落点：带 planId 直接定位到刚生成的方案，不必在列表里翻。
const targetedPlanId = computed(() => {
  const value = route.query.planId
  return (Array.isArray(value) ? value[0] : value)?.trim() ?? ''
})
// 排程窗口由用户指定（不再写死 7 天）；与单单排产弹窗共用同一份解析口径。
const horizonInput = ref(createSchedulingHorizonInput())
// 解析结果既进「生成首版」的禁用原因表，也是真正发给后端的窗口——只算一次，不会两处漂移。
const resolvedWorkbenchHorizon = computed(() => resolveSchedulingHorizon(horizonInput.value))

watch(workbench.schedulableCandidates, (candidates) => draft.setOrders(candidates), {
  immediate: true,
})
const actionablePlans = computed(() =>
  plans.value.filter(
    (plan): plan is BusinessConsoleSchedulingPlanSummaryResponse & { planId: string } =>
      Boolean(plan.planId),
  ),
)

/**
 * 页头读数说的是**历史排程方案**（与「排程总览」里的 MES 待排工单是两个集合，
 * 两边数字不同是设计上成立的）。但方案列表本身可能读取失败——那时不能显 0，
 * 否则"没有方案"和"取不到方案"没法区分。
 */
const plansFailed = computed(() => !plansPending.value && plansError.value != null)
const planHeaderCount = computed(() => {
  if (plansFailed.value) return '方案数取不到'
  if (plansPending.value && actionablePlans.value.length === 0) return undefined
  return `${plansTotal.value} 个方案`
})

watch([activeView, actionablePlans], ([view, availablePlans]) => {
  if (view !== 'gantt' || detailSelection.planId || availablePlans.length === 0) return
  detailSelection.planId = availablePlans[0]?.planId ?? ''
})

const columns: NvDataTableColumn<BusinessConsoleSchedulingPlanSummaryResponse>[] = [
  {
    key: 'planId',
    header: '排程方案',
    width: '14rem',
    cellClass: 'font-medium',
    accessor: (row) => row.planId ?? '未命名方案',
  },
  { key: 'status', header: '状态', width: 'w-40' },
  {
    key: 'range',
    header: '时间范围',
    width: '22rem',
    accessor: (row) =>
      row.horizonStartUtc && row.horizonEndUtc
        ? `${formatDateTime(row.horizonStartUtc)} 至 ${formatDateTime(row.horizonEndUtc)}`
        : '未记录时间范围',
  },
  { key: 'invalidation', header: '失效原因', width: '14rem', accessor: invalidationSummary },
  {
    key: 'operationCount',
    header: '工序数',
    width: '7rem',
    accessor: (row) => `${row.assignmentCount ?? 0} 道工序`,
  },
  { key: 'conflicts', header: '冲突摘要', width: '12rem', accessor: conflictSummary },
  {
    key: 'releasedAtUtc',
    header: '发布时间',
    width: 'w-44',
    accessor: (row) => (row.releasedAtUtc ? formatDateTime(row.releasedAtUtc) : '未发布'),
  },
  { key: 'generatedAtUtc', header: '创建时间', width: 'w-44' },
  { key: 'actions', header: '操作', width: '16rem', align: 'end' },
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
// 查阅方案和草案各按自身 planId 获取摘要，历史表筛选/分页不决定发布能力。
const { summary: selectedPlanStatus } = useSchedulingPlanSummary(() => detailSelection.planId)
const selectedPlanSummary = computed(() =>
  planDetail.value
    ? {
        ...selectedPlanStatus.value,
        planId: planDetail.value.planId,
        status: planDetail.value.status,
      }
    : undefined,
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
// 已经点名了具体方案（单单排产刚生成的那份）就直接打开，不再走「逐个方案找订单」的兜底。
watch(
  targetedPlanId,
  (planId) => {
    if (!planId) return
    detailSelection.planId = planId
    detailOpen.value = true
  },
  { immediate: true },
)
watch(
  [targetedOrderReference, actionablePlans, planDetail, planDetailPending],
  ([target, availablePlans, detail, pending]) => {
    if (!target || availablePlans.length === 0 || pending) return
    if (targetedPlanId.value) return
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

// 本地日期一律走 `@/utils/format` 的「YYYY-MM-DD HH:mm」。裸 `toLocaleString()` 跟的是
// **浏览器 UI 语言**，装英文系统的机器上排程明细里满屏 `1/5/2026, 8:00:00 AM`（#1418）。

function rangeFromAssignments(assignments: BusinessConsoleSchedulingAssignment[]) {
  const timestamps = assignments
    .flatMap((assignment) => [assignment.startUtc, assignment.endUtc])
    .filter((value): value is string => Boolean(value))
    .map((value) => new Date(value))
    .filter((date) => !Number.isNaN(date.getTime()))
    .sort((a, b) => a.getTime() - b.getTime())

  if (timestamps.length === 0) return '无'

  return `${formatDateTime(timestamps[0]!.toISOString())} 至 ${formatDateTime(timestamps[timestamps.length - 1]!.toISOString())}`
}

function openDetail(planId: string | undefined) {
  if (!planId) return
  detailSelection.planId = planId
  detailOpen.value = true
}

async function publish(plan: BusinessConsoleSchedulingPlanSummaryResponse | undefined) {
  if (!plan?.planId || !canRelease(plan)) return

  try {
    await releasePlan(plan.planId)
    toast.success('排程方案已发布')
  } catch (error) {
    notifyOperationFailure('发布失败', error, '发布失败，请稍后重试')
  }
}

// TODO(MAN-674 / #1241): 「生成首版」前的真实预览待后端补 workbench 级 dry-run facade
// （勿用 POST /scheduling/plans/preview——其契约要求前端提交完整 SchedulingProblemContract，
// 而 problem 只能由后端 SchedulingWorkbenchSourceProvider 从工单选择组装）。
async function generateWorkbenchPlan() {
  // 与按钮 disabled 同一处事实：命中任一禁用原因就不发请求。
  // 「排程窗口非法」也是其中一条原因，所以这里不再单独 toast——按钮本身就是灰的。
  if (generateBlockedReason.value) return
  const resolvedHorizon = resolvedWorkbenchHorizon.value
  // 类型收窄；能走到这里说明窗口原因没有命中。
  if (!resolvedHorizon.ok) return
  try {
    await workbench.generatePlan({
      organizationId: schedulingFilters.organizationId,
      environmentId: schedulingFilters.environmentId,
      horizonStartUtc: resolvedHorizon.horizonStartUtc,
      horizonEndUtc: resolvedHorizon.horizonEndUtc,
      orders: draft.includedOrders.value.map((order) => ({
        workOrderId: order.workOrderId,
      })),
    })
  } catch (error) {
    notifyOperationFailure('生成失败', error, '生成失败，请检查工单生产版本与排程基础数据')
  }
}

watch(workbench.generatedPlan, (plan) => {
  if (!plan) return
  insertion.reset()
  selectedInsertionId.value = ''
  persistedDraftPlan.value = plan
  draft.loadPlan(plan)
  detailSelection.planId = plan.planId ?? ''
  revisionResult.value = undefined
  toast.success('首版排程方案已生成')
})
watch(workbench.generationError, (error) => {
  if (error) {
    const completed = workbench.firstPlanJob.value?.status === 'completed'
    notifyOperationFailure(
      completed ? '方案加载失败' : '生成失败',
      error,
      completed ? '首版排程已完成，请稍后从方案列表查看' : '首版排程生成失败',
    )
  }
})

function includeWorkbenchOrders(ids: string[], included: boolean) {
  const reason = included && firstPlanSelectionReason(draft.orders.value, ids)
  if (reason) {
    toast.error(reason)
    return
  }
  draft.setIncluded(ids, included)
}

const firstPlanProgress = computed(() => {
  const status = workbench.firstPlanJob.value?.status
  if (workbench.generationError.value)
    return status === 'completed'
      ? '首版排程已完成，方案加载失败，请稍后从方案列表查看'
      : '首版排程未完成，请查看失败通知后重新生成'
  return status === 'created'
    ? '首版排程排队中'
    : status === 'running'
      ? '首版排程生成中'
      : status === 'completed'
        ? workbench.generatePending.value
          ? '首版排程已完成，正在加载方案'
          : '首版排程已完成'
        : ''
})

async function repreviewLockedDraft() {
  const planId = draft.model.value?.meta.planId
  // 未锁定的人工修改也是禁用原因之一（按钮灰 + title 说明 + 旁边就有「锁定全部修改」）。
  if (repreviewBlockedReason.value || !planId || draft.includedOrders.value.length === 0) return
  try {
    const revision = await workbench.revisePlan(planId, {
      organizationId: schedulingFilters.organizationId,
      environmentId: schedulingFilters.environmentId,
      includedOrderIds: draft.includedOrders.value.map((order) => order.workOrderId),
      lockedAssignments: draft.lockedAssignments.value,
    })
    insertion.reset()
    selectedInsertionId.value = ''
    revisionBasePlan.value = persistedDraftPlan.value
    revisionResult.value = revision
    if (revision.candidate) {
      persistedDraftPlan.value = revision.candidate
      draft.loadPlan(revision.candidate, revision.impact)
      detailSelection.planId = revision.candidate.planId ?? ''
    }
    toast.success('已生成锁定约束下的新版本')
  } catch (error) {
    notifyOperationFailure('重预览失败', error, '重预览失败，请检查锁定资源与时间窗口')
  }
}

const insertion = useSchedulingRushInsertion({
  baseline: persistedDraftPlan,
  context: () => ({
    organizationId: schedulingFilters.organizationId,
    environmentId: schedulingFilters.environmentId,
  }),
  enabled: canManage,
  saveOrder: async (id, values) => {
    const organizationId = schedulingFilters.organizationId
    const environmentId = schedulingFilters.environmentId
    await workbench.saveOrderPriority(id, values)
    if (
      organizationId === schedulingFilters.organizationId &&
      environmentId === schedulingFilters.environmentId &&
      canManage.value &&
      draftReady.value
    )
      draft.updateOrder(id, values)
  },
})
const selectedInsertionId = shallowRef('')
watch(insertion.message, (message) => {
  if (message.includes('候选受理失败')) notifyError(new Error(message))
})
watch(insertion.task.error, (error) => {
  if (error) notifyOperationFailure('插单计算失败', error, '急单已保存，请手动重试候选计算。')
})
async function selectInsertionCandidate() {
  const job = insertion.task.job.value
  const result = job?.result
  if (
    !job?.input ||
    !result?.candidatePlanId ||
    !result.snapshot?.problem?.orders ||
    !canManage.value ||
    workbench.revisionPending.value
  )
    return
  const selectionBaseline = persistedDraftPlan.value
  try {
    const revision = await workbench.revisePlan(result.candidatePlanId, {
      organizationId: job.input.organizationId!,
      environmentId: job.input.environmentId!,
      includedOrderIds: result.snapshot.problem.orders.map((order) => order.orderId!),
      lockedAssignments: [],
    })
    if (
      insertion.task.job.value?.jobId !== job.jobId ||
      persistedDraftPlan.value !== selectionBaseline ||
      !draftReady.value
    )
      return
    revisionBasePlan.value = result.snapshot?.baseline ?? job.acceptedBaseline?.baseline
    revisionResult.value = revision
    if (revision.candidate) {
      selectedInsertionId.value = revision.candidate.planId ?? ''
      persistedDraftPlan.value = revision.candidate
      draft.loadPlan(revision.candidate, revision.impact)
      detailSelection.planId = revision.candidate.planId ?? ''
      await persistence.save()
    }
    toast.success('所选插单候选已保存为修订，请核对后确认发布')
  } catch (error) {
    notifyOperationFailure('候选保存失败', error, '候选尚未保存，请重试。')
  }
}

async function saveWorkbenchOrderPriority(
  workOrderId: string,
  values: { priority: number; isRush: boolean },
) {
  await insertion.saveOrder(workOrderId, values)
}

function onLockedDragAttempt() {
  toast.error('该工序已锁定；请先解锁再调整资源或时间')
}

const publishConfirmOpen = shallowRef(false)
const publishTargetPlanId = shallowRef('')
function requestPublishCandidate() {
  const planId = draft.model.value?.meta.planId
  if (publishCandidateBlockedReason.value || !planId) return
  publishTargetPlanId.value = planId
  publishConfirmOpen.value = true
}
async function publishCandidate() {
  const planId = draft.model.value?.meta.planId
  if (publishCandidateBlockedReason.value || !planId || planId !== publishTargetPlanId.value) return
  detailSelection.planId = planId
  try {
    await releasePlan(planId)
    publishConfirmOpen.value = false
    toast.success('新版排程已发布')
  } catch (error) {
    notifyOperationFailure('发布失败', error, '发布失败；失效或终态方案不能发布')
  }
}

// 撤销发布：只对已发布方案开放，二次确认说明后果（MES 侧回流撤销对应工序排程）。
// 权限沿用发布权限——后端 revoke 端点同样挂在 PlansRelease 权限码下。
const revokeTargetPlanId = shallowRef('')
const revokeConfirmOpen = shallowRef(false)

function canRevoke(row: Pick<BusinessConsoleSchedulingPlanSummaryResponse, 'status'> | undefined) {
  return Boolean(row && canPublish.value && row.status === 'released')
}

function requestRevoke(
  plan: Pick<BusinessConsoleSchedulingPlanSummaryResponse, 'planId' | 'status'> | undefined,
) {
  if (!plan?.planId || !canRevoke(plan)) return
  revokeTargetPlanId.value = plan.planId
  revokeConfirmOpen.value = true
}

async function confirmRevoke() {
  const planId = revokeTargetPlanId.value
  if (!planId) return
  try {
    await revokePlan(planId)
    revokeConfirmOpen.value = false
    toast.success('排程方案已撤销发布，MES 侧将回流撤销对应工序排程')
  } catch (error) {
    notifyOperationFailure('撤销失败', error, '撤销失败，请稍后重试')
  }
}

// 单工序持久化 override：把资源/起止落库为跨方案 override，后端建方案路径自动叠加继承。
const persistedOperationKeys = shallowRef<string[]>([])

async function persistOperationOverride(taskId: string) {
  const planId = draft.model.value?.meta.planId
  const task = draft.model.value?.tasks.find((candidate) => candidate.id === taskId)
  if (!canManage.value || !task || task.type !== 'operation') return
  if (!planId) {
    // 草案模型存在但缺方案标识（异常数据）：显式提示，而不是静默吞掉点击。
    toast.error('当前草案未关联排程方案，无法持久锁定；请重新生成方案')
    return
  }
  if (!task.resourceId) {
    toast.error('该工序未分配资源，请先指定资源再持久锁定')
    return
  }
  try {
    await upsertOperationOverride({
      planId,
      operationId: task.operationId,
      resourceId: task.resourceId,
      startUtc: task.startUtc,
      endUtc: task.endUtc,
    })
    const key = `${task.orderId}:${task.operationId}`
    if (!persistedOperationKeys.value.includes(key)) {
      persistedOperationKeys.value = [...persistedOperationKeys.value, key]
    }
    toast.success('工序已持久锁定，之后重新排程也保持不变')
  } catch (error) {
    notifyOperationFailure('持久锁定失败', error, '持久锁定失败，请稍后重试')
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

/**
 * 草案工作区主操作的**禁用原因表**：按钮灰掉时必须能 hover 看到为什么灰（MAN-691 / #1259）。
 *
 * 写成「原因列表」而不是长布尔链，是为了 disabled 与 title 出自**同一处事实**
 * （disabled = 有命中的原因），也方便后续往列表里并入新原因（如排程窗口非法），
 * 不用再同时改两处判断。口径与历史方案表的 `releaseDisabledReason` 一致：
 * 不可用时说明缺什么，可用时说明这一步会做什么。
 */
type ActionBlocker = { blocked: boolean; reason: string }

function firstBlockingReason(blockers: ActionBlocker[]) {
  return blockers.find((blocker) => blocker.blocked)?.reason
}

const generateBlockedReason = computed(() =>
  firstBlockingReason([
    { blocked: !canManage.value, reason: '当前账号没有排产管理权限，不能生成排程方案' },
    { blocked: !draftReady.value, reason: '正在处理草稿，请稍候' },
    {
      blocked: draft.includedOrders.value.length === 0,
      reason: '还没有选中工单：先在待排工单池里勾选要排的工单',
    },
    {
      blocked: Boolean(firstPlanCapacityReason(draft.includedOrders.value.length)),
      reason: firstPlanCapacityReason(draft.includedOrders.value.length) ?? '',
    },
    // 窗口非法（起止倒置 / 缺值 / 跨度超上限）按 #1278 的口径并进原因表：
    // 按钮直接灰掉并说明改哪里，而不是点下去才弹一句 toast（MAN-694 / #1262）。
    {
      blocked: !resolvedWorkbenchHorizon.value.ok,
      reason: resolvedWorkbenchHorizon.value.ok
        ? ''
        : `排程窗口不可用：${resolvedWorkbenchHorizon.value.message}`,
    },
    { blocked: workbench.generatePending.value, reason: '正在生成首版方案，请稍候' },
  ]),
)
const generateDisabledReason = computed(
  () =>
    generateBlockedReason.value ??
    `按当前勾选的工单生成首版排程方案（${describeSchedulingHorizon(resolvedWorkbenchHorizon.value)}）`,
)

const repreviewBlockedReason = computed(() =>
  firstBlockingReason([
    { blocked: !canManage.value, reason: '当前账号没有排产管理权限，不能重预览' },
    { blocked: !draftReady.value, reason: '正在处理草稿，请稍候' },
    { blocked: !draft.model.value, reason: '还没有草案方案：先生成首版方案，再做锁定重预览' },
    { blocked: workbench.generatePending.value, reason: '正在生成首版方案，请稍候' },
    {
      blocked: draft.includedOrders.value.length > 10,
      reason: '锁定重预览最多支持 10 单，500 单仅用于异步首版',
    },
    { blocked: workbench.revisionPending.value, reason: '正在按锁定约束重预览，请稍候' },
    {
      blocked: draft.modifiedUnlockedTaskIds.value.length > 0,
      reason: '有未锁定的人工修改：先锁定全部修改再重预览，否则会被候选方案覆盖',
    },
  ]),
)
const repreviewDisabledReason = computed(
  () => repreviewBlockedReason.value ?? '保持已锁定工序不动，重排其余工序生成新版本',
)

// 后台刷新只读取版本状态，不重新加载用户正在编辑的草案。
const draftInvalidationReason = computed(() =>
  draftPlanSummary.value?.isInvalidated
    ? `方案已失效（${describeScheduleInvalidationReason(draftPlanSummary.value.latestInvalidationReasonCode)}），请重排后再发布`
    : undefined,
)
const draftTerminalReason = computed(() =>
  schedulingPlanTerminalReleaseReason(draftPlanSummary.value?.status),
)

const publishCandidateBlockedReason = computed(() =>
  firstBlockingReason([
    { blocked: !canPublish.value, reason: '当前账号没有排程发布权限' },
    {
      blocked: insertion.task.pending.value || workbench.revisionPending.value,
      reason: '正在处理候选，请稍候',
    },
    {
      blocked: Boolean(
        insertion.task.result.value &&
        selectedInsertionId.value !== insertion.task.result.value.candidatePlanId,
      ),
      reason: '请先选定插单候选并保存修订',
    },
    { blocked: !draftReady.value, reason: '正在处理草稿，请稍候' },
    { blocked: !draft.model.value, reason: '还没有可发布的版本：先生成首版或重预览出一版方案' },
    {
      blocked: Boolean(draftInvalidationReason.value),
      reason: draftInvalidationReason.value ?? '',
    },
    { blocked: Boolean(draftTerminalReason.value), reason: draftTerminalReason.value ?? '' },
    { blocked: releasePlanPending.value, reason: '正在发布，请稍候' },
    { blocked: workbench.generatePending.value, reason: '正在生成首版排程，请稍候' },
  ]),
)
const publishCandidateDisabledReason = computed(
  () => publishCandidateBlockedReason.value ?? '把当前草案版本发布给车间执行',
)

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
      :count="planHeaderCount"
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
        <NvTabsTrigger value="workbench">排程总览</NvTabsTrigger>
        <NvTabsTrigger value="table">表格</NvTabsTrigger>
        <NvTabsTrigger value="gantt">甘特图</NvTabsTrigger>
      </NvTabsList>

      <NvTabsContent value="workbench" class="grid gap-4">
        <div class="grid gap-4 rounded-lg border bg-card p-4">
          <div class="flex flex-wrap items-center justify-between gap-3">
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
                :disabled="!draftReady || workbench.generatePending.value || !draft.canUndo.value"
                :title="draft.canUndo.value ? '撤销上一步草案修改' : '没有可撤销的草案修改'"
                @click="draft.undo"
                >撤销</NvButton
              >
              <NvButton
                size="sm"
                variant="ghost"
                type="button"
                :disabled="!draftReady || workbench.generatePending.value || !draft.canRedo.value"
                :title="draft.canRedo.value ? '重做刚撤销的草案修改' : '没有可重做的草案修改'"
                @click="draft.redo"
                >重做</NvButton
              >
              <NvButton
                size="sm"
                variant="outline"
                type="button"
                :disabled="Boolean(generateBlockedReason)"
                :title="generateDisabledReason"
                @click="generateWorkbenchPlan"
              >
                <Spinner v-if="workbench.generatePending.value" aria-hidden="true" />生成首版
              </NvButton>
              <NvButton
                size="sm"
                variant="outline"
                type="button"
                :disabled="Boolean(repreviewBlockedReason)"
                :title="repreviewDisabledReason"
                @click="repreviewLockedDraft"
              >
                <Spinner v-if="workbench.revisionPending.value" aria-hidden="true" />锁定重预览
              </NvButton>
              <NvButton
                size="sm"
                type="button"
                :disabled="Boolean(publishCandidateBlockedReason)"
                :title="publishCandidateDisabledReason"
                @click="requestPublishCandidate"
              >
                <SendIcon aria-hidden="true" />发布新版
              </NvButton>
            </div>
          </div>

          <div class="flex flex-wrap items-center gap-3 text-sm text-muted-foreground">
            <span role="status" aria-live="polite">{{ draftSaveStatus }}</span>
            <NvSelect
              v-if="persistence.savedDrafts.value.length > 0"
              :model-value="draft.model.value?.meta.planId ?? ''"
              :disabled="persistence.busy.value || workbench.generatePending.value"
              @update:model-value="(value) => persistence.select(String(value))"
            >
              <NvSelectTrigger class="w-64" aria-label="已保存草稿方案"
                ><NvSelectValue placeholder="恢复已保存草稿"
              /></NvSelectTrigger>
              <NvSelectContent>
                <NvSelectItem
                  v-for="saved in persistence.savedDrafts.value"
                  :key="saved.planId"
                  :value="saved.planId!"
                  >{{ saved.planId }}</NvSelectItem
                >
              </NvSelectContent>
            </NvSelect>
            <NvButton
              v-if="persistence.status.value === 'error'"
              size="sm"
              variant="outline"
              :disabled="workbench.generatePending.value"
              @click="persistence.retry"
              >重试草稿操作</NvButton
            >
            <NvButton
              size="sm"
              variant="ghost"
              :disabled="
                !canManage ||
                persistence.busy.value ||
                workbench.generatePending.value ||
                !draft.model.value
              "
              title="清空当前方案的个人草稿，方案和其它草稿仍保留"
              @click="clearDraftConfirmOpen = true"
              >清空草稿</NvButton
            >
          </div>

          <!-- 排程窗口由排产员指定（MAN-694 / #1262）；「生成首版」按这个窗口求解。 -->
          <SchedulingHorizonFields
            v-model="horizonInput"
            id-prefix="workbench-horizon"
            :disabled="!canManage || !draftReady || workbench.generatePending.value"
          />
        </div>

        <p
          v-if="!canManage"
          class="rounded-md border border-warning/30 bg-warning/10 p-3 text-sm"
          role="status"
        >
          当前账号只有读取权限，可查看历史方案但不能编辑或生成新版本。
        </p>
        <p
          v-if="draftInvalidationReason"
          class="rounded-md border border-warning/30 bg-warning/10 p-3 text-sm"
          role="status"
        >
          {{ draftInvalidationReason }}
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
            :disabled="!canManage || !draftReady || workbench.generatePending.value"
            :title="
              canManage
                ? '把这些人工修改锁定为约束，重预览时不会被覆盖'
                : '当前账号没有排产管理权限，不能锁定修改'
            "
            @click="draft.lockModifiedTasks"
            >锁定全部修改</NvButton
          >
        </div>
        <p v-if="firstPlanProgress" role="status" class="text-sm text-muted-foreground">
          {{ firstPlanProgress }}
        </p>
        <SchedulingOrderPool
          :candidates="workbench.schedulableCandidates.value"
          :draft-orders="draft.orders.value"
          :loading="workbench.candidatesPending.value"
          :error="workbench.candidatesError.value"
          :scope-ready="workbench.candidatesScopeReady.value"
          :scope-message="workbench.candidatesScopeMessage.value"
          :read-only="!canManage || !draftReady || workbench.generatePending.value"
          @include="includeWorkbenchOrders"
          :can-edit-priority="
            permissionCodes.includes(P.mesWorkOrdersManage) && workbench.priorityScopeReady.value
          "
          :save-order="saveWorkbenchOrderPriority"
          @retry="workbench.refreshCandidates"
        >
          <template #scope>
            <MesWorkScopeSelect permission-code="business.mes.work-orders.read" />
          </template>
        </SchedulingOrderPool>
        <SchedulingCandidatePanel
          :candidates="localCandidates.candidates.value"
          :baseline-plan-id="persistedDraftPlan?.planId"
          :can-manage="canManage && draftReady && !workbench.generatePending.value"
          :pending="localCandidates.pending.value"
          :blocked-reason="candidateDraftBlockedReason"
          @preview="localCandidates.preview"
          @select="localCandidates.select"
        />
        <SchedulingRushInsertionResult
          :job="insertion.task.job.value"
          :message="insertion.message.value"
          :pending="insertion.task.pending.value"
          :failed="Boolean(insertion.task.error.value)"
          :saving="workbench.revisionPending.value"
          :selected="
            Boolean(
              selectedInsertionId &&
              selectedInsertionId === insertion.task.result.value?.candidatePlanId,
            )
          "
          @retry="insertion.retry"
          @select="selectInsertionCandidate"
        />
        <SchedulingDraftBoard
          :model="draftDisplayModel"
          :feedback="draft.feedback.value"
          :material-shortage-summary="persistedDraftPlan?.materialShortageSummary"
          :pending-operations="draft.pendingOperations.value"
          :read-only="!canManage || !draftReady || workbench.generatePending.value"
          :persisted-operation-keys="persistedOperationKeys"
          :persist-pending="upsertOperationOverridePending"
          @persist-override="persistOperationOverride"
          @move="draft.moveTask"
          @update="draft.updateTask"
          @lock="draft.setLocked"
          @locked-attempt="onLockedDragAttempt"
          @move-to-pending="draft.moveTaskToPending"
          @restore-pending="draft.restorePendingTask"
        />
        <ScheduleRevisionReview :revision="revisionResult" :base-plan="revisionBasePlan" />
      </NvTabsContent>

      <NvTabsContent value="table" class="grid min-w-0 gap-4">
        <!-- 只读边界要自解释：历史方案是已生成结果的查阅面，改排程只能回草案工作区。
             不写这句，用户会在表里反复点、以为"表格坏了"（MAN-691 / #1259）。 -->
        <div
          data-testid="plan-table-readonly-notice"
          class="flex flex-wrap items-start justify-between gap-3 rounded-lg border bg-muted/30 p-3"
        >
          <div class="grid gap-1">
            <p class="text-sm font-medium text-foreground">历史方案：只读查阅</p>
            <p class="max-w-2xl text-sm text-muted-foreground">
              这里列的是已生成的排程方案，只能查看明细、发布或撤销发布；工序的资源与时间不能在这张表上改。
              要调整排程，回草案工作区改完再重预览生成新版本。
            </p>
          </div>
          <NvButton
            size="sm"
            variant="outline"
            type="button"
            title="回到草案工作区调整工序资源与时间，再重预览生成新版本"
            @click="activeView = 'workbench'"
          >
            去草案工作区修改
          </NvButton>
        </div>
        <div class="flex flex-wrap items-end gap-3">
          <div class="grid gap-1">
            <span class="text-sm font-medium">状态</span>
            <NvSelect v-model="historyStatus">
              <NvSelectTrigger class="w-36" aria-label="按方案状态筛选"
                ><NvSelectValue
              /></NvSelectTrigger>
              <NvSelectContent>
                <NvSelectItem value="all">全部状态</NvSelectItem>
                <NvSelectItem v-for="status in historyStatuses" :key="status" :value="status">{{
                  schedulingPlanStatusLabel(status)
                }}</NvSelectItem>
              </NvSelectContent>
            </NvSelect>
          </div>
          <div class="grid gap-1">
            <label for="history-released-on" class="text-sm font-medium">发布日（UTC）</label>
            <NvInput
              id="history-released-on"
              v-model="schedulingFilters.releasedOn"
              type="date"
              class="w-44"
            />
          </div>
          <div class="grid gap-1">
            <span class="text-sm font-medium">失效</span>
            <NvSelect v-model="historyInvalidated">
              <NvSelectTrigger class="w-36" aria-label="按方案失效筛选"
                ><NvSelectValue
              /></NvSelectTrigger>
              <NvSelectContent>
                <NvSelectItem value="all">全部方案</NvSelectItem>
                <NvSelectItem value="true">已失效</NvSelectItem>
                <NvSelectItem value="false">未失效</NvSelectItem>
              </NvSelectContent>
            </NvSelect>
          </div>
          <p class="pb-2 text-sm text-muted-foreground">发布时间从新到旧，未发布方案排在后面</p>
        </div>
        <NvDataTable
          class="min-w-0"
          manual
          :page="page"
          :page-size="pageSize"
          :total-items="plansTotal"
          @update:page="page = $event"
          @update:page-size="pageSize = String($event)"
          :columns="columns"
          :rows="actionablePlans"
          :row-key="rowKey"
          :loading="plansPending"
          :searchable="false"
          :column-settings="false"
          :empty-message="hasHistoryFilters ? '没有符合条件的方案' : '还没有排程方案'"
          :error="plansError"
          error-message="没有取到排程方案列表，当前无法判断已有哪些方案。请重试，或稍后再看。"
          @retry="refreshPlans"
        >
          <template #empty>
            <template v-if="hasHistoryFilters">
              <p class="text-sm font-medium text-foreground">没有符合条件的方案</p>
              <p class="max-w-md text-sm text-muted-foreground">
                调整筛选条件，或清空筛选查看历史方案。
              </p>
              <NvButton size="sm" type="button" class="mt-1" @click="clearHistoryFilters"
                >清空筛选</NvButton
              >
            </template>
            <template v-else>
              <p class="text-sm font-medium text-foreground">还没有排程方案</p>
              <p class="max-w-md text-sm text-muted-foreground">
                先在排程总览里挑出要排的工单，生成首版方案后即可在这里查看、对比并发布。
              </p>
              <NvButton size="sm" type="button" class="mt-1" @click="activeView = 'workbench'">
                去排程总览生成方案
              </NvButton>
            </template>
          </template>
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
                @click="publish(row)"
              >
                <Spinner v-if="releasePlanPending" aria-hidden="true" />
                <SendIcon v-else aria-hidden="true" />
                发布
              </NvButton>
              <NvButton
                v-if="canRevoke(row)"
                size="sm"
                variant="destructive"
                type="button"
                :disabled="revokePlanPending"
                title="撤销该已发布方案，MES 侧回流撤销对应工序排程"
                @click="requestRevoke(row)"
              >
                <Undo2Icon aria-hidden="true" />
                撤销发布
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
          <NvButton
            v-if="canRevoke(planDetail)"
            size="sm"
            variant="destructive"
            type="button"
            :disabled="revokePlanPending"
            title="撤销该已发布方案，MES 侧回流撤销对应工序排程"
            @click="requestRevoke(planDetail)"
          >
            <Undo2Icon aria-hidden="true" />
            撤销发布
          </NvButton>
        </div>
        <NvButton
          class="mb-4"
          type="button"
          variant="outline"
          :disabled="!detailSelection.planId || csv.pending.value"
          @click="downloadPlanCsv"
          >下载方案 CSV</NvButton
        >
        <SchedulingPlanGantt
          :plan="planDetail"
          :summary="selectedPlanSummary"
          :work-orders="displayWorkOrders"
          :work-order-facts-unavailable="Boolean(associatedWorkOrders.error.value)"
          :loading="planDetailPending"
          :error="planDetailError"
          :release-pending="releasePlanPending"
          @open-detail="detailOpen = true"
          @release="publish(selectedPlanSummary)"
        />
        <SchedulingDaySchedule
          v-if="planDetail && !planDetailPending && !planDetailError"
          :plan="planDetail"
        />
      </NvTabsContent>
    </NvTabs>

    <!-- 撤销发布二次确认：说明 MES 侧后果，避免误触。
         v-if 按需挂载：关闭时完全不渲染 reka AlertDialog 树，也避免与页面测试针对
         NvSheet 的全局 DialogRoot stub 相互干扰（stub 会剥掉 AlertDialog 的注入上下文）。 -->
    <SchedulingDraftClearDialog
      v-model:open="clearDraftConfirmOpen"
      :pending="persistence.status.value === 'clearing' || workbench.generatePending.value"
      :clear="persistence.clear"
    />
    <NvAlertDialog v-if="publishConfirmOpen" v-model:open="publishConfirmOpen">
      <NvAlertDialogContent>
        <NvAlertDialogHeader>
          <NvAlertDialogTitle>确认发布所选方案？</NvAlertDialogTitle>
          <NvAlertDialogDescription
            >将方案
            {{ publishTargetPlanId }} 发布给车间执行，取代当前有效方案。</NvAlertDialogDescription
          >
        </NvAlertDialogHeader>
        <NvAlertDialogFooter>
          <NvAlertDialogCancel>取消</NvAlertDialogCancel>
          <NvButton
            type="button"
            :disabled="
              Boolean(publishCandidateBlockedReason) ||
              draft.model.value?.meta.planId !== publishTargetPlanId
            "
            @click="publishCandidate"
            >确认发布</NvButton
          >
        </NvAlertDialogFooter>
      </NvAlertDialogContent>
    </NvAlertDialog>
    <NvAlertDialog v-if="revokeConfirmOpen" v-model:open="revokeConfirmOpen">
      <NvAlertDialogContent>
        <NvAlertDialogHeader>
          <NvAlertDialogTitle>确认撤销发布该排程方案？</NvAlertDialogTitle>
          <NvAlertDialogDescription>
            方案 {{ revokeTargetPlanId }} 撤销后将进入「已撤销」终态，MES
            侧会回流撤销由它下达的工序排程；需要重新下达时须生成并发布新方案。
          </NvAlertDialogDescription>
        </NvAlertDialogHeader>
        <NvAlertDialogFooter>
          <NvAlertDialogCancel>取消</NvAlertDialogCancel>
          <!-- 普通 NvButton，不用 NvAlertDialogAction：后者点击即无条件关框（confirm-destroy 规则 3），
               撤销失败时框早已消失、用户无从原地重试。 -->
          <NvButton
            type="button"
            variant="destructive"
            :disabled="revokePlanPending"
            @click="confirmRevoke"
          >
            <Spinner v-if="revokePlanPending" aria-hidden="true" />
            确认撤销
          </NvButton>
        </NvAlertDialogFooter>
      </NvAlertDialogContent>
    </NvAlertDialog>

    <NvSheet v-model:open="detailOpen">
      <NvSheetContent side="right" class="w-full overflow-y-auto sm:max-w-3xl">
        <NvSheetHeader>
          <NvSheetTitle>排程方案明细</NvSheetTitle>
          <NvSheetDescription>
            {{ detailSelection.planId || '未选择方案' }} · 只读查阅，调整排程请回草案工作区
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
            <!-- 6 个指标塞进 5 列：第 6 个「负荷分钟」必然孤悬到第二行独占一格，而 1/5 的
                 列宽又装不下「设备状态未知工序」这种 7 字标签，只能断行并贴到格子边缘
                 （#1418）。改 3 列 × 2 行：列宽翻倍，长标签一行放得下，也不再有落单格子。 -->
            <div class="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
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
                <p class="text-xs text-muted-foreground">物料风险工序</p>
                <p class="text-sm font-medium text-foreground">
                  {{
                    planDetail.metrics?.materialRiskOperationCount ??
                    planDetail.materialRisks?.length ??
                    0
                  }}
                </p>
              </div>
              <div>
                <p class="text-xs text-muted-foreground">设备状态未知工序</p>
                <p class="text-sm font-medium text-foreground">
                  {{
                    planDetail.metrics?.equipmentRiskOperationCount ??
                    planDetail.equipmentRisks?.length ??
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
            <div class="flex flex-wrap items-center justify-between gap-2">
              <h3 class="text-sm font-semibold text-foreground">资源分配</h3>
              <UrgencyDisplayModeSelect v-model="displayMode" />
            </div>
            <div v-if="orderedAssignments.length" class="grid gap-2">
              <div
                v-for="assignment in orderedAssignments"
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
                    :mode="displayMode"
                    :urgency="
                      assignment.orderId
                        ? orderUrgencies.byReference.value.get(assignment.orderId)
                        : undefined
                    "
                    :source-unavailable="orderUrgencies.error?.value != null"
                    @refresh="refreshUrgency"
                  />
                </div>
                <TaskFacts
                  v-if="assignmentTask(assignment)"
                  :task="assignmentTask(assignment)!"
                  class="mt-3"
                />
                <p class="mt-1 text-sm text-muted-foreground">
                  <template v-if="assignment.segments?.length">
                    <span
                      v-for="(segment, index) in assignment.segments"
                      :key="index"
                      class="block"
                    >
                      第 {{ index + 1 }} 段 · {{ formatDateTime(segment.startUtc) }} 至
                      {{ formatDateTime(segment.endUtc) }}
                    </span>
                  </template>
                  <template v-else>
                    {{ formatDateTime(assignment.startUtc) }} 至
                    {{ formatDateTime(assignment.endUtc) }}
                  </template>
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

          <!--
            物料风险（软约束）：齐套是开工门槛不是排产门槛 —— 缺料工序照排进方案，
            这里告诉规划员开工前必须补齐哪些物料，缺口多少。
          -->
          <SchedulingMaterialShortageSummary
            :shortages="planDetail.materialShortageSummary ?? []"
          />
          <section v-if="planDetail.materialRisks?.length" class="grid gap-3">
            <h3 class="text-sm font-semibold text-foreground">物料风险 · 需在开工前完成备料</h3>
            <div class="grid gap-2">
              <div
                v-for="risk in planDetail.materialRisks"
                :key="`${risk.orderId}:${risk.operationId}`"
                class="rounded-md border border-warning/40 bg-warning/10 p-3 text-sm"
                data-testid="plan-material-risk"
              >
                <p class="font-medium">{{ risk.orderId }} · {{ risk.operationId }}</p>
                <ul v-if="risk.shortages?.length" class="mt-1 grid gap-0.5 text-xs">
                  <li
                    v-for="shortage in risk.shortages"
                    :key="`${shortage.materialId}-${shortage.materialLotId ?? ''}`"
                    class="flex justify-between gap-3 tabular-nums"
                  >
                    <span>{{ shortage.materialId }}</span>
                    <span
                      >缺 {{ shortage.shortageQuantity }}（需
                      {{ shortage.requiredQuantity }}）</span
                    >
                  </li>
                </ul>
                <p class="mt-1 text-xs">{{ risk.message }}</p>
              </div>
            </div>
          </section>

          <!--
            设备数据风险（软约束）：无快照 / 快照过期 / 采集源不可达 —— 这是数据盲区，
            不是设备不可用。工序照排，但开工前要人工确认设备状态。
          -->
          <section v-if="planDetail.equipmentRisks?.length" class="grid gap-3">
            <h3 class="text-sm font-semibold text-foreground">
              设备状态未知 · 开工前请人工确认设备可用
            </h3>
            <div class="grid gap-2">
              <div
                v-for="risk in planDetail.equipmentRisks"
                :key="`${risk.orderId}:${risk.operationId}`"
                class="rounded-md border border-border bg-muted/40 p-3 text-sm"
                data-testid="plan-equipment-risk"
              >
                <p class="font-medium">
                  {{ risk.orderId }} · {{ risk.operationId }} · {{ risk.resourceId }}
                </p>
                <p class="mt-1 text-xs text-muted-foreground">{{ risk.message }}</p>
              </div>
            </div>
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
