import { traceabilityReturnPath } from '@/composables/mes/traceWorkOrderLink'
import { statusActionGate } from '@nerv-iip/business-core'
import type { NvDataTableColumn, NvMetricStatus, NvMetricTone, StatusTone } from '@nerv-iip/ui'
import { recoverLifecycleAction } from '@/composables/shared/lifecycleAction'
import { isSchedulableWorkbenchCandidate } from '@/composables/scheduling/useSchedulingWorkbench'
import {
  SINGLE_ORDER_SCHEDULING_DENIED_REASON,
  useCanScheduleSingleOrder,
} from '@/composables/scheduling/useSingleOrderScheduling'
import {
  describeMesReadinessReasons,
  makeIdempotencyKey,
  useMesWorkOrderTransformations,
  useMesWorkOrderDetail,
  type MesWorkOrderTransformationResult,
} from '@/composables/mes'
import {
  type MergeTransformationSubmit,
  type SplitTransformationSubmit,
  type WorkOrderTransformationState,
} from '@/components/mes/WorkOrderTransformationDialog.vue'
import { isTransformationConflict } from '@/composables/mes/workOrderTransformation'
import { useMesDisplayNames } from '@/composables/mes/useMesDisplayNames'
import { describeMaterialShortageStage } from '@/composables/mes/materialReadinessScope'
import { useMesReferenceLabels } from '@/composables/mes/useMesReferenceLabels'
import { operationQualityStatusLabel } from '@/data/businessLabels'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { useAuthStore } from '@/stores/auth'
import { readFaceText } from '@/utils/readFace'
import {
  inlineErrorMessage,
  isForbiddenError,
  notifyError,
  notifyOperationFailure,
  notifySuccess,
} from '@/utils/notify'
import { computed, reactive, ref, shallowRef } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { operationTaskLabel } from '@/utils/mesTaskIdentity'
import { useWorkOrderMaterialActions } from './useWorkOrderMaterialActions'
import { useWorkOrderCancellation } from './useWorkOrderCancellation'

export function useWorkOrderDetailPage() {
  const route = useRoute()
  const router = useRouter()
  const {
    activateCancelPreview,
    cancelPreviewError,
    cancelPreviewPending,
    cancelPreviewReady,
    cancelWorkOrder,
    cancelWorkOrderPending,
    holdWorkOrder,
    holdWorkOrderPending,
    closeWorkOrder,
    closeWorkOrderPending,
    confirmLineSideReceipt,
    returnLineSideMaterial,
    returnLineSideMaterialPending,
    createMaterialIssueRequest,
    createMaterialIssueRequestPending,
    detail: sourceDetail,
    detailError,
    detailPending,
    filters,
    finishedGoodsReceiptRequests,
    materialIssueRequests,
    materialIssueRequestsError,
    materialIssueRequestsPending,
    materialReadiness,
    materialReadinessError,
    materialReadinessPending,
    refreshDetail,
    refreshMaterialIssueRequests,
    refreshMaterialReadiness,
    recordEngineeringChangeDecision,
    recordEngineeringChangeDecisionPending,
    retryCancelPreview,
    workOrderManageScopeMessage,
    workOrderManageScope,
    workOrderManageScopeReady,
    workOrderReadScope,
    workOrderReadScopeMessage,
  } = useMesWorkOrderDetail(() => {
    const value = (route.params as Record<string, string | string[] | undefined>).workOrderId
    return (Array.isArray(value) ? value[0] : value) ?? ''
  })
  const workOrderTransformations = useMesWorkOrderTransformations({
    filters,
    readScope: workOrderReadScope,
    manageScope: workOrderManageScope,
  })

  // 工序行的班次只有标识，班次名在主数据里（shifts: true 才会拉这份名录）。
  const { resolveShiftLabel, resolveSkuLabel } = useMesDisplayNames({ shifts: true })
  const { statusLabel } = useMesReferenceLabels()
  const auth = useAuthStore()
  const permissionCodes = computed(() => auth.principal?.permissionCodes ?? [])
  // 人工强制释放质量保留需 business.mes.quality.write（网关 MesQualityWrite），无权则只读时间线。
  const canManageQualityHold = computed(() => permissionCodes.value.includes(P.mesQualityWrite))
  // 时间线读端点需 business.mes.quality.read（高于本页 work-orders.read）：无则不加载时间线，避免逐个保留 403。
  const canReadQualityHold = computed(() => permissionCodes.value.includes(P.mesQualityRead))
  // 来源检验记录下钻目标页需 business.quality.inspection-records.read：无则面板不显示互链，避免点后被路由守卫拒。
  const canReadInspectionRecords = computed(() =>
    permissionCodes.value.includes(P.qualityInspectionRecordsRead),
  )

  const canReadReports = computed(() => permissionCodes.value.includes(P.mesReportingRead))
  const canReadInventory = computed(() => permissionCodes.value.includes(P.inventoryLedgerRead))
  const canReadReceipts = computed(() => permissionCodes.value.includes(P.mesReceiptsRead))
  const sourceSuggestionRoute = computed(() => {
    const source = detail.value?.sourcePlanReference
    if (
      !permissionCodes.value.includes(P.planningMrpRead) ||
      source?.sourceDocumentType !== 'PlanningSuggestion' ||
      !source.sourceDocumentId
    )
      return undefined
    return { path: '/planning', query: { suggestionId: source.sourceDocumentId } }
  })

  const traceReturn = computed(() => traceabilityReturnPath(route.query.traceReturn))
  const traceSourceMessage = computed(() => {
    if (!traceReturn.value) return ''
    if (
      route.query.traceOrganizationId !== filters.organizationId ||
      route.query.traceEnvironmentId !== filters.environmentId
    ) {
      return '当前查看范围已改变，请返回追溯重新查询。'
    }
    if (!sourceDetail.value) return ''
    if (sourceDetail.value.workOrderId !== filters.workOrderId) {
      return '工单详情与追溯来源不一致，请返回追溯核对。'
    }
    const operationTaskId = route.query.operationTaskId
    if (
      operationTaskId &&
      !sourceDetail.value.operationTasks?.some((task) => task.operationTaskId === operationTaskId)
    ) {
      return '工单详情中未找到来源工序，请返回追溯核对。'
    }
    return ''
  })
  // 公开读回与来源身份不一致时，不把另一对象的事实展示成来源工单。
  const detail = computed(() => (traceSourceMessage.value ? undefined : sourceDetail.value))
  const operationTasks = computed(() => detail.value?.operationTasks ?? [])
  const workOrderLabel = computed(() => readFaceText(filters.workOrderId, '未编号工单'))
  // 工单质量保留（活跃 + 已释放周期）。定位键齐备（sourceService + sourceDocumentId）的才渲染。
  // 已释放周期保留在列表里，使强制释放后面板不卸载、释放时间/方式与时间线仍可见（issue 验收「时间线完整」）。
  const qualityHolds = computed(() =>
    (detail.value?.qualityHolds ?? []).filter(
      (hold): hold is typeof hold & { sourceService: string; sourceDocumentId: string } =>
        Boolean(hold.sourceService) && Boolean(hold.sourceDocumentId),
    ),
  )
  // 「有效质量保留」文案与锁定语义只看活跃周期（与列表锁定图标口径一致）。
  const activeQualityHoldCount = computed(
    () => qualityHolds.value.filter((hold) => hold.isActive).length,
  )
  // 缺口环节（#1291）随行预计算：每行只算一次，模板里直接读，不在单元格里反复调用。
  const materialRows = computed(() =>
    (materialReadiness.value?.items ?? []).map((row) => ({
      ...row,
      stage: describeMaterialShortageStage(row),
    })),
  )
  const blockingReasons = computed(() => [
    ...(detail.value?.blockingReasons ?? []),
    ...(materialReadiness.value?.blockingReasons ?? []),
  ])
  const blockingReasonDisplays = computed(() => describeMesReadinessReasons(blockingReasons.value))
  // 三张子表各读各的面，失败也各归各的表：合成一条页面级错误行会让「哪张表挂了」丢失。
  const detailErrorMessage = computed(() => inlineErrorMessage(detailError.value))
  const materialReadinessErrorMessage = computed(() =>
    inlineErrorMessage(materialReadinessError.value),
  )
  const materialIssueRequestsErrorMessage = computed(() =>
    inlineErrorMessage(materialIssueRequestsError.value),
  )

  // 工单头部四卡：状态与用料给「能不能开工」的结论，工序进度给「做到哪了」。
  const skuLabel = computed(() => resolveSkuLabel(detail.value?.skuId))
  const splitDialogOpen = ref(false)
  const splitState = ref<WorkOrderTransformationState>('idle')
  const splitError = ref('')
  const splitResult = ref<MesWorkOrderTransformationResult | null>(null)
  const splitIdempotencyKey = ref('')
  const splitSource = computed(() =>
    detail.value?.workOrderId
      ? {
          workOrderId: detail.value.workOrderId,
          label: workOrderLabel.value,
          skuId: detail.value.skuId,
          skuLabel: skuLabel.value,
          productionVersionId: detail.value.productionVersionId,
          quantity: detail.value.quantity,
          // PR-C 当前详情读契约没有返回 UOM；拆分结果由服务端继承单位，页面不猜测。
          uomCode: undefined,
          status: detail.value.status,
        }
      : undefined,
  )
  const canTransformWorkOrder = computed(
    () =>
      workOrderManageScopeReady.value &&
      Boolean(detail.value) &&
      ['created', 'released'].includes((detail.value?.status ?? '').toLowerCase()),
  )
  const splitPending = computed(
    () => workOrderTransformations.splitWorkOrderPending.value || splitState.value === 'loading',
  )

  function openSplitDialog() {
    if (!canTransformWorkOrder.value) return
    splitState.value = 'idle'
    splitError.value = ''
    splitResult.value = null
    splitIdempotencyKey.value = makeIdempotencyKey(`split-work-order-${filters.workOrderId}`)
    splitDialogOpen.value = true
  }

  async function submitSplit(payload: SplitTransformationSubmit | MergeTransformationSubmit) {
    if (!('targets' in payload)) return
    splitState.value = 'loading'
    splitError.value = ''
    try {
      const result = await workOrderTransformations.splitWorkOrder(filters.workOrderId, payload)
      splitResult.value = result
      if (result.readback) {
        splitState.value = 'success'
        notifySuccess(`工单 ${workOrderLabel.value} 已拆分，结果已回读。`)
        await refreshDetail()
      } else {
        splitState.value = 'accepted'
        notifySuccess('拆分请求已受理，但结果暂未回读；请在本窗口重试回读。')
      }
    } catch (error) {
      splitState.value = isTransformationConflict(error) ? 'conflict' : 'error'
      splitError.value = inlineErrorMessage(error, '拆分工单失败，请刷新后重试。')
      notifyOperationFailure('拆分工单失败', error, '拆分工单失败，请刷新后重试。')
    }
  }

  async function retrySplitReadback() {
    const current = splitResult.value
    if (!current) return
    splitState.value = 'loading'
    try {
      const readback = await workOrderTransformations.readTransformation(
        current.mutation.transformationId,
      )
      splitResult.value = { ...current, readback, readbackError: undefined }
      splitState.value = 'success'
      notifySuccess('拆分结果已回读。')
      await refreshDetail()
    } catch (error) {
      splitResult.value = { ...current, readbackError: error }
      splitState.value = 'accepted'
      notifyOperationFailure(
        '回读拆分结果失败',
        error,
        '拆分已受理，但结果暂不可用，请稍后重试回读。',
      )
    }
  }
  const completedTaskCount = computed(
    () =>
      operationTasks.value.filter((task) =>
        ['completed', 'closed'].includes((task.status ?? '').toLowerCase()),
      ).length,
  )
  const taskProgress = computed(() =>
    operationTasks.value.length
      ? Math.round((completedTaskCount.value / operationTasks.value.length) * 100)
      : 0,
  )
  const workOrderTone = computed<NvMetricTone>(() => {
    if (blockingReasons.value.length) return 'danger'
    const status = (detail.value?.status ?? '').toLowerCase()
    if (status === 'completed' || status === 'closed') return 'success'
    if (status === 'hold' || status === 'cancelled') return 'warning'
    return 'neutral'
  })
  // 「无阻塞」是一个安全结论：工单详情或用料齐套任一读面没取到，都不许下这个结论
  // （作业范围未就绪整页拒载时曾反显「无阻塞」假文案，#1288）。
  const workOrderStatusPill = computed<NvMetricStatus>(() => {
    if (blockingReasons.value.length) {
      return { label: `${blockingReasons.value.length} 项阻塞`, tone: 'danger' }
    }
    if (!detail.value || !materialReadiness.value) {
      return { label: '结论未取得', tone: 'neutral' }
    }
    return { label: '无阻塞', tone: 'success' }
  })
  const materialShortageCount = computed(
    () => materialRows.value.filter((row) => (row.shortageQuantity ?? 0) > 0).length,
  )
  const materialConclusion = computed(() =>
    materialReadinessError.value || materialReadinessPending.value
      ? undefined
      : materialReadiness.value,
  )
  const materialReadForbidden = computed(() => isForbiddenError(materialReadinessError.value))
  const materialIssueReadForbidden = computed(() =>
    isForbiddenError(materialIssueRequestsError.value),
  )
  const materialReadPermissionMessage = '当前角色没有用料读取权限，请联系管理员确认岗位授权。'
  const materialReadStateLabel = computed(() =>
    materialReadForbidden.value
      ? '无用料读取权限'
      : materialReadinessError.value
        ? '齐套更新失败'
        : materialReadinessPending.value
          ? '正在更新齐套'
          : '未取得',
  )
  const materialTone = computed<NvMetricTone>(() => {
    if (materialConclusion.value?.readinessStatus === 'Blocked') return 'danger'
    return materialConclusion.value?.readinessStatus === 'Ready' ? 'success' : 'neutral'
  })

  type TaskRow = (typeof operationTasks)['value'][number]
  const taskColumns: NvDataTableColumn<TaskRow>[] = [
    {
      key: 'operationTaskId',
      header: '任务',
      width: 'w-64',
      cellClass: 'font-medium whitespace-normal break-all',
      accessor: (r) => operationTaskLabel(r),
    },
    { key: 'status', header: '状态', width: 'w-24' },
    { key: 'scheduleStatus', header: '排程状态', width: 'w-28' },
    {
      key: 'operationSequence',
      header: '序号',
      align: 'end',
      width: 'w-16',
      accessor: (r) => r.operationSequence ?? 0,
    },
    // 读面已回 *Name / *Code（见 MesOperationTaskRow）；两者都缺时显占位，不回吐内部标识。
    {
      key: 'workCenterId',
      header: '工作中心',
      width: 'w-48',
      cellClass: 'whitespace-normal break-all',
      accessor: (r) => r.workCenterName?.trim() || r.workCenterCode?.trim() || '—',
    },
    {
      key: 'deviceAssetId',
      header: '设备',
      width: 'w-48',
      cellClass: 'whitespace-normal break-all',
      accessor: (r) => r.deviceAssetName?.trim() || r.deviceAssetCode?.trim() || '未指定',
    },
    {
      key: 'shiftId',
      header: '班次',
      width: 'w-40',
      cellClass: 'whitespace-normal break-all',
      accessor: (r) => resolveShiftLabel(r.shiftId),
    },
    { key: 'startedAtUtc', header: '开始', width: 'w-44' },
    {
      key: 'qualityStatus',
      header: '质量',
      width: 'w-32',
      cellClass: 'whitespace-normal break-all',
      // 这一列会收到总体判定值（ready/warning/blocked），走库存质量状态表会把 `Ready`
      // 原样上屏（#1418）——统一走判定优先的口径。
      accessor: (r) => operationQualityStatusLabel(r.qualityStatus),
    },
  ]

  type MaterialRow = (typeof materialRows)['value'][number]
  const materialColumns: NvDataTableColumn<MaterialRow>[] = [
    {
      key: 'materialId',
      header: '物料',
      cellClass: 'font-medium',
      // 齐套读面只回物料标识，物料名在 SKU 主数据里；主数据缺就显编码，是 GUID 则不上屏。
      accessor: (r) => resolveSkuLabel(r.materialId),
    },
    {
      key: 'materialLotId',
      header: '批次',
      accessor: (r) => readFaceText(r.materialLotId, '未指定'),
    },
    { key: 'requiredQuantity', header: '需求', align: 'end', width: 'w-20' },
    { key: 'availableQuantity', header: '线边可用', align: 'end', width: 'w-24' },
    { key: 'stagedQuantity', header: '已备', align: 'end', width: 'w-20' },
    { key: 'shortageQuantity', header: '短缺', align: 'end', width: 'w-20' },
    {
      key: 'status',
      header: '状态',
      width: 'w-24',
      accessor: (r) => materialRowStatus(r.status).label,
    },
    // 缺口卡在哪个环节 + 下一步动作（#1291：齐套页要能讲清「缺什么、缺在哪个环节、下一步动作」）。
    { key: 'shortageStage', header: '缺在哪个环节', width: 'w-56' },
    { key: 'actions', header: '操作', width: 'w-28' },
  ]

  const {
    canManageMaterials,
    issueOpen,
    issueForm,
    receiveOpen,
    receiveError,
    receiveForm,
    receiveRetry,
    receiptPending,
    returnOpen,
    returnForm,
    materialIssueRows,
    materialIssueColumns,
    operationTaskOptions,
    issueUomCode,
    issueUomBlockReason,
    canSubmitIssue,
    canSubmitReceipt,
    openIssueDialog,
    canReceive,
    returnFormMaxQuantity,
    canReturn,
    openReceiveDialog,
    openReturnDialog,
    submitIssue,
    submitReceipt,
    submitReturn,
  } = useWorkOrderMaterialActions(
    {
      filters,
      materialIssueRequests,
      createMaterialIssueRequest,
      createMaterialIssueRequestPending,
      confirmLineSideReceipt,
      returnLineSideMaterial,
      returnLineSideMaterialPending,
      refreshMaterialReadiness,
    },
    operationTasks,
    permissionCodes,
    resolveSkuLabel,
  )

  const currentStatus = computed(() => (detail.value?.status ?? '').toLowerCase())
  const holdOpen = shallowRef(false)
  const holdReason = shallowRef('')
  const holdShowErrors = shallowRef(false)
  const HOLD_REASON_MAX_LENGTH = 500
  const canHold = computed(
    () =>
      workOrderManageScopeReady.value &&
      statusActionGate({
        domain: 'mes-work-order',
        action: 'hold',
        facts: { status: detail.value?.status },
      }).executable,
  )
  const holdReasonError = computed(() => (holdReason.value.trim() ? '' : '请输入挂起原因。'))
  function openHoldDialog() {
    if (!canHold.value) return
    holdReason.value = ''
    holdShowErrors.value = false
    holdOpen.value = true
  }
  async function submitHold() {
    if (!canHold.value || holdWorkOrderPending.value) return
    holdShowErrors.value = true
    if (holdReasonError.value) return
    try {
      await holdWorkOrder(holdReason.value.trim())
      holdOpen.value = false
      notifySuccess(`工单 ${workOrderLabel.value} 已挂起。`)
      await refreshDetail()
    } catch (error) {
      if (
        await recoverLifecycleAction(error, {
          reset: () => {
            holdOpen.value = false
            holdReason.value = ''
          },
          refresh: refreshDetail,
          notify: (message) => notifyError(message),
        })
      ) {
        return
      }
      notifyOperationFailure('挂起工单失败', error, '挂起工单失败，请稍后重试。')
    }
  }
  const closeOpen = ref(false)
  const engineeringImpactsRefreshVersion = ref(0)
  const decisionOpen = ref(false)
  const decisionShowErrors = ref(false)
  const decisionForm = reactive({ changeNumber: '', decision: '', reason: '' })
  const ENGINEERING_CHANGE_NUMBER_MAX_LENGTH = 100
  const ENGINEERING_CHANGE_REASON_MAX_LENGTH = 500
  const canClose = computed(
    () => workOrderManageScopeReady.value && currentStatus.value === 'completed',
  )
  const canRecordEngineeringDecision = computed(
    () => workOrderManageScopeReady.value && !['closed', 'cancelled'].includes(currentStatus.value),
  )
  const decisionFieldErrors = computed(() => ({
    changeNumber: !decisionForm.changeNumber.trim()
      ? '请输入变更单号。'
      : decisionForm.changeNumber.trim().length > ENGINEERING_CHANGE_NUMBER_MAX_LENGTH
        ? `变更单号不能超过 ${ENGINEERING_CHANGE_NUMBER_MAX_LENGTH} 个字符。`
        : '',
    decision: decisionForm.decision ? '' : '请选择处理意见。',
    reason: !decisionForm.reason.trim()
      ? '请输入决策说明。'
      : decisionForm.reason.trim().length > ENGINEERING_CHANGE_REASON_MAX_LENGTH
        ? `决策说明不能超过 ${ENGINEERING_CHANGE_REASON_MAX_LENGTH} 个字符。`
        : '',
  }))
  const decisionValidationSummary = computed(() =>
    Object.values(decisionFieldErrors.value).filter(Boolean),
  )
  const canSubmitDecision = computed(
    () => canRecordEngineeringDecision.value && decisionValidationSummary.value.length === 0,
  )
  async function submitClose() {
    if (!canClose.value || closeWorkOrderPending.value) return
    try {
      await closeWorkOrder()
      closeOpen.value = false
      notifySuccess(`工单 ${workOrderLabel.value} 已关闭。`)
      await refreshDetail()
    } catch (error) {
      notifyOperationFailure('关闭工单失败', error, '关闭工单失败，请稍后重试。')
    }
  }
  function openDecisionDialog() {
    if (!canRecordEngineeringDecision.value) return
    decisionForm.changeNumber = ''
    decisionForm.decision = ''
    decisionForm.reason = ''
    decisionShowErrors.value = false
    decisionOpen.value = true
  }
  async function submitDecision() {
    if (!canRecordEngineeringDecision.value || recordEngineeringChangeDecisionPending.value) return
    decisionShowErrors.value = true
    if (!canSubmitDecision.value) return
    try {
      await recordEngineeringChangeDecision({
        changeNumber: decisionForm.changeNumber.trim(),
        decision: decisionForm.decision,
        reason: decisionForm.reason.trim(),
      })
      decisionOpen.value = false
      engineeringImpactsRefreshVersion.value++
      notifySuccess(
        `工程变更决策已记录：${decisionForm.decision === 'continue-with-archived-version' ? '继续使用现行版本' : '停止工单'}。`,
      )
      await refreshDetail()
    } catch (error) {
      notifyOperationFailure('记录工程变更决策失败', error, '记录工程变更决策失败，请稍后重试。')
    }
  }

  // 「对该单排产」（MAN-694 / #1262）：终态工单排不了，读权限也生成不了方案——
  // 两种情况都禁用并说明原因，而不是让用户点完吃一个 400。
  const scheduleOpen = ref(false)
  const canScheduleSingleOrder = useCanScheduleSingleOrder()
  const scheduleDisabledReason = computed(() => {
    if (!detail.value) return '工单信息加载中，请稍候。'
    if (!isSchedulableWorkbenchCandidate(detail.value)) {
      return detail.value.productionVersionId
        ? '工单已处于终态，不能再排产。'
        : '工单没有生产版本，排程无法展开工艺路线。'
    }
    if (!canScheduleSingleOrder.value) return SINGLE_ORDER_SCHEDULING_DENIED_REASON
    return ''
  })
  // 空态复用同一份 scheduleDisabledReason：禁用原因只有一处真实来源，避免空态文案与
  // tooltip（:947）各写各的、漏分支或说反原因（#2707）。
  const operationTasksEmptyMessage = computed(() =>
    scheduleDisabledReason.value
      ? `暂无工序任务。${scheduleDisabledReason.value}`
      : '暂无工序任务。点击上方「对该单排产」生成方案，在「排产工作台」发布后可在这里派工与报工。',
  )
  const {
    CANCEL_REASONS,
    REASON_MAX_LENGTH,
    cancelOpen,
    cancelForm,
    canCancel,
    cancelDisabledReason,
    lineSideReturnRows,
    returnableQuantityOf,
    unreturnableNoLotRows,
    reservationRows,
    cancellableReceiptCount,
    cancellableTaskCount,
    hasCompensation,
    requiresRemark,
    finalReasonLength,
    showReasonLengthHint,
    remarkMaxLength,
    canSubmitCancel,
    cancelContextItems,
    openCancelDialog,
    submitCancel,
  } = useWorkOrderCancellation(
    {
      activateCancelPreview,
      cancelPreviewReady,
      cancelWorkOrder,
      cancelWorkOrderPending,
      finishedGoodsReceiptRequests,
      materialIssueRequests,
      refreshDetail,
      workOrderManageScopeMessage,
      workOrderManageScopeReady,
    },
    detail,
    operationTasks,
    permissionCodes,
    currentStatus,
    workOrderLabel,
    skuLabel,
    formatStatus,
    formatQuantity,
  )

  function refreshAll() {
    engineeringImpactsRefreshVersion.value++
    void refreshDetail()
    void refreshMaterialReadiness()
  }
  function openRoute(path: string) {
    void router.push({
      path,
      query: {
        workOrderId: filters.workOrderId,
        skuId: detail.value?.skuId ?? undefined,
        quantity: detail.value?.quantity?.toString() ?? undefined,
      },
    })
  }
  function formatDateTime(value?: string | null) {
    if (!value) return '无'
    const date = new Date(value)
    return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
  }
  function formatQuantity(value?: number) {
    return new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 3 }).format(value ?? 0)
  }
  /** 齐套行状态（MES 齐套读面只回 Shortage / Ready）；词表外的码显示「—」。 */
  function materialRowStatus(value?: string | null): { label: string; tone: StatusTone } {
    const status = value?.toLowerCase()
    if (status === 'shortage') return { label: '缺料', tone: 'warning' }
    if (status === 'ready') return { label: '已齐套', tone: 'success' }
    return { label: '—', tone: 'neutral' }
  }
  function formatStatus(value?: string | null) {
    const map: Record<string, string> = {
      blocked: '阻塞',
      cancelled: '已取消',
      closed: '已关闭',
      completed: '已完成',
      created: '已创建',
      hold: '已暂停',
      ready: '可开工',
      released: '已下达',
      running: '执行中',
      scrapped: '已报废',
      started: '已开工',
      warning: '预警',
    }
    return (value && map[value.toLowerCase()]) || '—'
  }

  return {
    CANCEL_REASONS,
    ENGINEERING_CHANGE_NUMBER_MAX_LENGTH,
    ENGINEERING_CHANGE_REASON_MAX_LENGTH,
    HOLD_REASON_MAX_LENGTH,
    REASON_MAX_LENGTH,
    activeQualityHoldCount,
    blockingReasonDisplays,
    blockingReasons,
    canCancel,
    canClose,
    canHold,
    canManageMaterials,
    canManageQualityHold,
    canReadInspectionRecords,
    canReadInventory,
    canReadQualityHold,
    canReadReceipts,
    canReadReports,
    canReceive,
    canRecordEngineeringDecision,
    canReturn,
    canSubmitCancel,
    canSubmitIssue,
    canSubmitReceipt,
    canTransformWorkOrder,
    cancelContextItems,
    cancelDisabledReason,
    cancelForm,
    cancelOpen,
    cancelPreviewError,
    cancelPreviewPending,
    cancelWorkOrderPending,
    cancellableReceiptCount,
    cancellableTaskCount,
    closeOpen,
    closeWorkOrderPending,
    completedTaskCount,
    confirmLineSideReceiptPending: receiptPending,
    createMaterialIssueRequestPending,
    decisionFieldErrors,
    decisionForm,
    decisionOpen,
    decisionShowErrors,
    decisionValidationSummary,
    engineeringImpactsRefreshVersion,
    detail,
    detailError,
    detailErrorMessage,
    detailPending,
    filters,
    finalReasonLength,
    formatDateTime,
    formatQuantity,
    formatStatus,
    hasCompensation,
    holdOpen,
    holdReason,
    holdReasonError,
    holdShowErrors,
    holdWorkOrderPending,
    issueForm,
    issueOpen,
    issueUomBlockReason,
    issueUomCode,
    lineSideReturnRows,
    materialColumns,
    materialConclusion,
    materialIssueColumns,
    materialIssueReadForbidden,
    materialIssueRequestsError,
    materialIssueRequestsErrorMessage,
    materialIssueRequestsPending,
    materialIssueRows,
    materialReadForbidden,
    materialReadPermissionMessage,
    materialReadStateLabel,
    materialReadiness,
    materialReadinessError,
    materialReadinessErrorMessage,
    materialReadinessPending,
    materialRowStatus,
    materialRows,
    materialShortageCount,
    materialTone,
    openCancelDialog,
    openDecisionDialog,
    openHoldDialog,
    openIssueDialog,
    openReceiveDialog,
    openReturnDialog,
    openRoute,
    openSplitDialog,
    operationTaskOptions,
    operationTasks,
    operationTasksEmptyMessage,
    qualityHolds,
    receiveError,
    receiveForm,
    receiveRetry,
    receiveOpen,
    recordEngineeringChangeDecisionPending,
    refreshAll,
    refreshDetail,
    refreshMaterialIssueRequests,
    refreshMaterialReadiness,
    remarkMaxLength,
    requiresRemark,
    reservationRows,
    resolveSkuLabel,
    retryCancelPreview,
    retrySplitReadback,
    returnForm,
    returnFormMaxQuantity,
    returnLineSideMaterialPending,
    returnOpen,
    returnableQuantityOf,
    route,
    scheduleDisabledReason,
    scheduleOpen,
    showReasonLengthHint,
    skuLabel,
    sourceSuggestionRoute,
    splitDialogOpen,
    splitError,
    splitIdempotencyKey,
    splitPending,
    splitResult,
    splitSource,
    splitState,
    statusLabel,
    submitCancel,
    submitClose,
    submitDecision,
    submitHold,
    submitIssue,
    submitReceipt,
    submitReturn,
    submitSplit,
    taskColumns,
    taskProgress,
    traceReturn,
    traceSourceMessage,
    unreturnableNoLotRows,
    workOrderLabel,
    workOrderManageScopeReady,
    workOrderReadScope,
    workOrderReadScopeMessage,
    workOrderStatusPill,
    workOrderTone,
  }
}
