import {
  withFactoryTimeContext,
  scheduleTime,
  toModel,
  withWorkOrderFacts,
} from '@nerv-iip/scheduling'
import type {
  BusinessConsoleSchedulingAssignment,
  BusinessConsoleSchedulingPlanSummaryResponse,
  BusinessConsoleSchedulingPlanRevision,
  BusinessConsoleSchedulePlan,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { formatDateTime } from '@/utils/format'
import {
  useBusinessScheduling,
  useSchedulingPlanSummary,
} from '@/composables/scheduling/useBusinessScheduling'
import { useSchedulingTimeContext } from '@/composables/scheduling/useSchedulingTimeContext'
import { useMesWorkOrderFacts } from '@/composables/mes'
import { useOrderUrgencies } from '@/composables/urgency/useOrderUrgency'
import {
  DEFAULT_URGENCY_DISPLAY_MODE,
  orderRowsByUrgency,
  type UrgencyDisplayMode,
} from '@/composables/urgency/useUrgencyDisplayMode'
import { describeScheduleInvalidationReason } from '@/composables/scheduling/useScheduleInvalidation'
import { schedulingPlanTerminalReleaseReason } from '@/utils/schedulingPlanPresentation'
import {
  createSchedulingHorizonInput,
  describeSchedulingHorizon,
  resolveSchedulingHorizon,
} from '@/composables/scheduling/schedulingHorizon'
import {
  firstPlanCapacityReason,
  firstPlanSelectionReason,
} from '@/composables/scheduling/schedulingFirstPlanCapacity'
import { useSchedulingDowntime } from '@/composables/scheduling/useSchedulingDowntime'
import { withDowntimeImpact } from '@/composables/scheduling/schedulingDowntime'
import SchedulingCandidatePanel from '@/components/scheduling/SchedulingCandidatePanel.vue'
import { useSchedulingCandidates } from '@/composables/scheduling/useSchedulingCandidates'
import { useSchedulingRushInsertion } from '@/composables/scheduling/useSchedulingRushInsertion'
import { useSchedulingWorkbench } from '@/composables/scheduling/useSchedulingWorkbench'
import { useWorkingScheduleDraft } from '@/composables/scheduling/useWorkingScheduleDraft'
import { useSchedulingDraftPersistence } from '@/composables/scheduling/useSchedulingDraftPersistence'
import { useAuthStore } from '@/stores/auth'
import {
  inlineErrorMessage,
  notifyError,
  notifyOperationFailure,
  notifySuccess,
} from '@/utils/notify'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { computed, ref, shallowRef, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useSchedulingHistoryFilters } from './useSchedulingHistoryFilters'
import { useSchedulingPlanPublication } from './useSchedulingPlanPublication'
import { useSchedulingPlanDetails } from './useSchedulingPlanDetails'

export function useSchedulingPageController() {
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
  const {
    csv,
    downloadPlanCsv,
    historyStatus,
    historyInvalidated,
    historyStatuses,
    hasHistoryFilters,
    clearHistoryFilters,
  } = useSchedulingHistoryFilters({ detailSelection, filters: schedulingFilters })

  const auth = useAuthStore()
  const permissionCodes = computed(() => auth.principal?.permissionCodes ?? [])
  const canManage = computed(() => permissionCodes.value.includes(P.schedulingPlansManage))
  const canPublish = computed(() => permissionCodes.value.includes(P.schedulingPlansRelease))
  const workbench = useSchedulingWorkbench()
  const clearDraftConfirmOpen = ref(false)
  const draftReady = shallowRef(false)
  const draft = useWorkingScheduleDraft(computed(() => !canManage.value || !draftReady.value))
  const { summary: draftPlanSummary } = useSchedulingPlanSummary(
    () => draft.model.value?.meta.planId,
  )
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
      ? withFactoryTimeContext(
          withWorkOrderFacts(
            toModel(planDetail.value),
            displayWorkOrders.value,
            Boolean(associatedWorkOrders.error.value),
          ),
          planFactoryTimeContext.items.value,
        ).tasks
      : [],
  )
  function assignmentTime(
    assignment: BusinessConsoleSchedulingAssignment,
    instant?: string | null,
  ) {
    const zone = assignmentTask(assignment)?.timezone ?? ''
    return instant
      ? scheduleTime(instant, zone, {
          year: 'numeric',
          month: '2-digit',
          day: '2-digit',
          hour: '2-digit',
          minute: '2-digit',
        })
      : '—'
  }
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
  const factoryTimeContext = useSchedulingTimeContext(
    () => persistedDraftPlan.value?.planId,
    () => ({
      organizationId: schedulingFilters.organizationId,
      environmentId: schedulingFilters.environmentId,
    }),
  )
  const planFactoryTimeContext = useSchedulingTimeContext(
    () => planDetail.value?.planId,
    () => ({
      organizationId: schedulingFilters.organizationId,
      environmentId: schedulingFilters.environmentId,
    }),
  )
  watch(factoryTimeContext.error, (error) => {
    if (error) notifyError(error, '工厂时区暂不可读取，当前草案已保留。')
  })
  watch(planFactoryTimeContext.error, (error) => {
    if (error) notifyError(error, '方案工厂时区暂不可读取，请稍后重试。')
  })
  const downtime = useSchedulingDowntime(
    () => persistedDraftPlan.value?.planId,
    () => ({
      organizationId: schedulingFilters.organizationId,
      environmentId: schedulingFilters.environmentId,
    }),
  )
  watch(downtime.error, (error) => {
    if (error) notifyError(error, '设备停机影响暂不可读取，请稍后重试。')
  })
  const downtimeDisplayModel = computed(() =>
    withDowntimeImpact(draftDisplayModel.value, downtime.impact.value, downtime.now.value),
  )
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
  const draftPlacementChanged = computed(() => {
    const plan = persistedDraftPlan.value
    if (!plan || !draft.model.value) return false
    const baseline = new Map(
      toModel(plan)
        .tasks.filter((task) => task.type === 'operation' && !task.blockKind)
        .map((task) => [task.id, task]),
    )
    const tasks = draft.exportState().tasks!
    return (
      tasks.length !== baseline.size ||
      tasks.some((task) => {
        const original = baseline.get(task.taskId!)
        return (
          !original ||
          original.resourceId !== task.resourceId ||
          original.workCenterId !== task.workCenterId ||
          original.startUtc !== task.startUtc ||
          original.endUtc !== task.endUtc ||
          original.locked !== task.locked ||
          JSON.stringify(original.segments ?? []) !== JSON.stringify(task.segments ?? [])
        )
      })
    )
  })
  const candidateDraftBlockedReason = computed(() =>
    draftPlacementChanged.value
      ? '草稿有人工编辑，请先锁定重预览生成新基线，再生成局部候选。'
      : undefined,
  )
  const downtimeCandidateBlockedReason = computed(() =>
    !canManage.value
      ? '当前账号没有排产管理权限'
      : !draftReady.value
        ? '正在恢复工作草稿，请稍候'
        : workbench.generatePending.value
          ? '正在生成方案，请稍候'
          : !persistedDraftPlan.value?.planId
            ? '先生成或恢复已保存基线'
            : candidateDraftBlockedReason.value,
  )
  async function previewDowntimeCandidates() {
    if (downtimeCandidateBlockedReason.value || localCandidates.pending.value) return
    await localCandidates.preview()
    candidatePanel.value?.$el.scrollIntoView({ behavior: 'instant', block: 'start' })
  }
  const candidatePanel = shallowRef<InstanceType<typeof SchedulingCandidatePanel>>()
  function attachCandidatePanel(panel: unknown) {
    candidatePanel.value = (panel ?? undefined) as typeof candidatePanel.value
  }
  const localCandidates = useSchedulingCandidates({
    context: () => ({
      organizationId: schedulingFilters.organizationId,
      environmentId: schedulingFilters.environmentId,
    }),
    baselinePlanId: () => persistedDraftPlan.value?.planId,
    onSelected: async (selection) => {
      const previousPlan = persistedDraftPlan.value
      if (!(await persistence.restoreSelection(selection.workingDraft!))) return
      insertion.reset()
      selectedInsertionId.value = ''
      revisionBasePlan.value = previousPlan
      revisionResult.value = { candidate: selection.plan, comparison: selection.comparison }
      detailSelection.planId = selection.plan?.planId ?? ''
      await refreshPlans()
      notifySuccess('已选定候选并保存工作草稿，请核对后确认发布')
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
    { key: 'conflicts', header: '约束摘要', width: '12rem', accessor: conflictSummary },
    {
      key: 'releasedAtUtc',
      header: '发布时间',
      width: 'w-44',
      accessor: (row) => (row.releasedAtUtc ? formatDateTime(row.releasedAtUtc) : '未发布'),
    },
    { key: 'generatedAtUtc', header: '创建时间', width: 'w-44' },
    { key: 'actions', header: '操作', width: '16rem', align: 'end' },
  ]

  const {
    detailOpen,
    selectedPlanRange,
    selectedResourceCount,
    detailFeedback,
    selectedPlanSummary,
    targetedAssignmentFound,
    targetedOrderReference,
    openDetail,
  } = useSchedulingPlanDetails(
    { detailSelection, planDetail, planDetailPending, planDetailError },
    route,
    actionablePlans,
    rangeFromAssignments,
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
      conflicts > 0 ? `${conflicts} 项约束（冲突/预警）` : '',
      unscheduled > 0 ? `${unscheduled} 项未排` : '',
    ]
      .filter(Boolean)
      .join('，')
  }

  // 方案范围沿用工序已验证的工厂时区；混合时区不折算为单一工厂。

  function rangeFromAssignments(assignments: BusinessConsoleSchedulingAssignment[]) {
    const timestamps = assignments
      .flatMap((assignment) => [assignment.startUtc, assignment.endUtc])
      .filter((value): value is string => Boolean(value))
      .map((value) => new Date(value))
      .filter((date) => !Number.isNaN(date.getTime()))
      .sort((a, b) => a.getTime() - b.getTime())

    if (timestamps.length === 0) return '无'

    const timezones = new Set(
      assignments.map((assignment) => assignmentTask(assignment)?.timezone ?? ''),
    )
    if (timezones.has('')) return '工厂时区未解析'
    if (timezones.size > 1) return '跨工厂时区，请查看各工序时间'

    return `${assignmentTime(assignments[0]!, timestamps[0]!.toISOString())} 至 ${assignmentTime(assignments[0]!, timestamps[timestamps.length - 1]!.toISOString())}`
  }

  const {
    publish,
    revokeTargetPlanId,
    revokeConfirmOpen,
    canRevoke,
    requestRevoke,
    confirmRevoke,
    canRelease,
    releaseDisabledReason,
  } = useSchedulingPlanPublication({ releasePlan, revokePlan }, canPublish)

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
    notifySuccess('首版排程方案已生成')
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
      notifyError(undefined, reason)
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
      notifySuccess('已生成锁定约束下的新版本')
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
    if (insertion.saveError.value) return
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
      notifySuccess('所选插单候选已保存为修订，请核对后确认发布')
    } catch (error) {
      if (insertion.task.job.value?.jobId !== job.jobId) return
      insertion.saveError.value = inlineErrorMessage(error, '候选尚未保存，请重新计算。')
      notifyOperationFailure('候选保存失败', error, '候选尚未保存，请重新计算。')
    }
  }

  async function saveWorkbenchOrderPriority(
    workOrderId: string,
    values: { priority: number; isRush: boolean },
  ) {
    await insertion.saveOrder(workOrderId, values)
  }

  function onLockedDragAttempt() {
    notifyError(undefined, '该工序已锁定；请先解锁再调整资源或时间')
  }

  const publishConfirmOpen = shallowRef(false)
  const publishTarget = shallowRef<{
    plan: BusinessConsoleSchedulePlan
    organizationId: string
    environmentId: string
  }>()
  const publishTargetPlanId = computed(() => publishTarget.value?.plan.planId)
  const publishTargetChanged = computed(() => {
    const target = publishTarget.value
    return (
      !target ||
      target.plan !== persistedDraftPlan.value ||
      target.organizationId !== schedulingFilters.organizationId ||
      target.environmentId !== schedulingFilters.environmentId
    )
  })
  function requestPublishCandidate() {
    const plan = persistedDraftPlan.value
    if (publishCandidateBlockedReason.value || !plan?.planId) return
    publishTarget.value = {
      plan,
      organizationId: schedulingFilters.organizationId,
      environmentId: schedulingFilters.environmentId,
    }
    publishConfirmOpen.value = true
  }
  async function publishCandidate() {
    const planId = publishTargetPlanId.value
    if (publishCandidateBlockedReason.value || publishTargetChanged.value || !planId) return
    detailSelection.planId = planId
    try {
      await releasePlan(planId)
      publishConfirmOpen.value = false
      notifySuccess('新版排程已发布')
    } catch (error) {
      notifyOperationFailure('发布失败', error, '发布失败；失效或终态方案不能发布')
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
      notifyError(undefined, '当前草案未关联排程方案，无法持久锁定；请重新生成方案')
      return
    }
    if (!task.resourceId) {
      notifyError(undefined, '该工序未分配资源，请先指定资源再持久锁定')
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
      notifySuccess('工序已持久锁定，之后重新排程也保持不变')
    } catch (error) {
      notifyOperationFailure('持久锁定失败', error, '持久锁定失败，请稍后重试')
    }
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
      {
        blocked:
          !persistedDraftPlan.value?.planId ||
          draft.model.value?.meta.planId !== persistedDraftPlan.value.planId,
        reason: '还没有可发布的版本：先生成首版或重预览出一版方案',
      },
      {
        blocked: draftPlacementChanged.value,
        reason: '草稿有未生成修订的修改，请先锁定重预览，再确认发布新版本',
      },
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

  function assignmentText(assignment: BusinessConsoleSchedulingAssignment) {
    return [
      assignment.orderId ?? '未关联工单',
      assignment.operationSequence ? `第 ${assignment.operationSequence} 道` : '工序',
      assignment.workCenterId ?? assignment.resourceId ?? '未分配资源',
    ].join(' · ')
  }

  return {
    actionablePlans,
    activeView,
    assignmentTask,
    assignmentText,
    assignmentTime,
    associatedWorkOrders,
    attachCandidatePanel,
    canManage,
    canRelease,
    canRevoke,
    candidateDraftBlockedReason,
    clearDraftConfirmOpen,
    clearHistoryFilters,
    columns,
    confirmRevoke,
    csv,
    detailFeedback,
    detailOpen,
    detailSelection,
    displayMode,
    displayWorkOrders,
    downloadPlanCsv,
    downtime,
    downtimeCandidateBlockedReason,
    downtimeDisplayModel,
    draft,
    draftDisplayModel,
    draftInvalidationReason,
    draftReady,
    draftSaveStatus,
    factoryTimeContext,
    firstPlanProgress,
    generateBlockedReason,
    generateDisabledReason,
    generateWorkbenchPlan,
    hasHistoryFilters,
    historyInvalidated,
    historyStatus,
    historyStatuses,
    horizonInput,
    includeWorkbenchOrders,
    insertion,
    localCandidates,
    onLockedDragAttempt,
    openDetail,
    orderUrgencies,
    orderedAssignments,
    page,
    pageSize,
    permissionCodes,
    persistOperationOverride,
    persistedDraftPlan,
    persistedOperationKeys,
    persistence,
    planDetail,
    planDetailError,
    planDetailPending,
    planFactoryTimeContext,
    planHeaderCount,
    plansError,
    plansPending,
    plansTotal,
    previewDowntimeCandidates,
    publish,
    publishCandidate,
    publishCandidateBlockedReason,
    publishCandidateDisabledReason,
    publishConfirmOpen,
    publishTargetChanged,
    publishTargetPlanId,
    refreshPlans,
    refreshUrgency,
    releaseDisabledReason,
    releasePlanPending,
    repreviewBlockedReason,
    repreviewDisabledReason,
    repreviewLockedDraft,
    requestPublishCandidate,
    requestRevoke,
    revisionBasePlan,
    revisionResult,
    revokeConfirmOpen,
    revokePlanPending,
    revokeTargetPlanId,
    rowKey,
    saveWorkbenchOrderPriority,
    schedulingFilters,
    selectInsertionCandidate,
    selectedInsertionId,
    selectedPlanRange,
    selectedPlanSummary,
    selectedResourceCount,
    targetedAssignmentFound,
    targetedOrderReference,
    upsertOperationOverridePending,
    workbench,
  }
}
