import { statusActionGate } from '@nerv-iip/business-core'
import { recoverLifecycleAction } from '@/composables/shared/lifecycleAction'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { notifyError, notifyOperationFailure, notifySuccess } from '@/utils/notify'
import { computed, reactive, ref } from 'vue'
import type { Ref } from 'vue'
import type { useMesWorkOrderDetail } from '@/composables/mes'

export function useWorkOrderCancellation(
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
  }: Pick<
    ReturnType<typeof useMesWorkOrderDetail>,
    | 'activateCancelPreview'
    | 'cancelPreviewReady'
    | 'cancelWorkOrder'
    | 'cancelWorkOrderPending'
    | 'finishedGoodsReceiptRequests'
    | 'materialIssueRequests'
    | 'refreshDetail'
    | 'workOrderManageScopeMessage'
    | 'workOrderManageScopeReady'
  >,
  detail: Readonly<Ref<ReturnType<typeof useMesWorkOrderDetail>['detail']['value']>>,
  operationTasks: Readonly<
    Ref<
      NonNullable<
        NonNullable<ReturnType<typeof useMesWorkOrderDetail>['detail']['value']>['operationTasks']
      >
    >
  >,
  permissionCodes: Readonly<Ref<readonly string[]>>,
  currentStatus: Readonly<Ref<string>>,
  workOrderLabel: Readonly<Ref<string>>,
  skuLabel: Readonly<Ref<string>>,
  formatStatus: (value?: string | null) => string,
  formatQuantity: (value?: number) => string,
) {
  // --- 取消工单（破坏性动作，原因必填 + 补偿预览，A1 §2/§4） ---
  const CANCELLABLE_STATUSES = new Set(['created', 'released', 'hold'])
  const CANCEL_REASONS = [
    { value: 'plan-cancelled', label: '计划取消' },
    { value: 'demand-changed', label: '需求变更' },
    { value: 'material-shortage', label: '物料短缺' },
    { value: 'quality-issue', label: '质量问题' },
    { value: 'engineering-change', label: '工程变更' },
    { value: 'duplicate', label: '重复工单' },
    { value: 'other', label: '其他（备注必填）' },
  ] as const
  const TERMINAL_TASK_STATUSES = new Set(['completed', 'closed', 'cancelled'])
  const TERMINAL_RECEIPT_STATUSES = new Set([
    'cancelled',
    'closed',
    'completed',
    'posted',
    'received',
  ])

  const cancelOpen = ref(false)
  const cancelForm = reactive({ reasonCode: '', remark: '' })

  const canCancel = computed(
    () =>
      workOrderManageScopeReady.value &&
      CANCELLABLE_STATUSES.has(currentStatus.value) &&
      statusActionGate({
        domain: 'mes-work-order',
        action: 'cancel',
        facts: { status: detail.value?.status },
      }).executable,
  )
  const cancelDisabledReason = computed(() => {
    if (!permissionCodes.value.includes(P.mesWorkOrdersManage)) return '当前角色没有取消工单权限。'
    if (!detail.value) return '工单信息加载中，请稍候。'
    if (!workOrderManageScopeReady.value) {
      return workOrderManageScopeMessage.value || '尚未取得当前主体的工单管理范围。'
    }
    if (canCancel.value) return ''
    const reasons: Record<string, string> = {
      started: '工单已开工，无法取消；请先处理在制工序。',
      running: '工单执行中，无法取消；请先暂停相关工序。',
      inProgress: '工单执行中，无法取消；请先暂停相关工序。',
      completed: '工单已完成，无法取消。',
      closed: '工单已关闭，无法取消。',
      cancelled: '工单已取消。',
    }
    return (
      reasons[currentStatus.value] ??
      `当前状态（${formatStatus(detail.value.status)}）不可取消，仅创建 / 已下达 / 暂停状态可取消。`
    )
  })

  // 补偿预览（后端暂无取消预览端点，按关联单据前端汇总，PR 已注明降级实现）。
  // 权威来源是本工单的领料申请（取消 handler 遍历的对象）：已收料→退料指引，未收料且未终结→释放预留；
  // 齐套快照仅在有已发布 MBOM 时才有行，无法覆盖无 MBOM 直接领料的工单，故不用它做补偿汇总。
  const TERMINAL_ISSUE_STATUSES = new Set(['cancelled', 'closed', 'returned', 'completed'])
  // 与后端取消退料规则一致：可退数量 = max(0, receivedQuantity - consumedQuantity)，且无 materialLotId 不可退
  // （后端 #557：无批次退料抛业务错误）。仅当可退数量>0 且有批次时才计为退料指引，避免预览/toast 误报。
  function returnableQuantityOf(row: (typeof materialIssueRequests.value)[number]): number {
    return Math.max(0, (row.receivedQuantity ?? 0) - (row.consumedQuantity ?? 0))
  }
  const lineSideReturnRows = computed(() =>
    materialIssueRequests.value.filter(
      (row) => (row.materialLotId ?? '').trim().length > 0 && returnableQuantityOf(row) > 0,
    ),
  )
  // 已收料且仍有可退量、却缺少物料批次的申请：后端退料要求批次（#557），取消整单会因此抛业务错误。
  // 不能只在预览里隐藏——必须前置阻断确认，并提示修复路径（补录批次或先消耗），否则用户确认后整单取消失败。
  const unreturnableNoLotRows = computed(() =>
    materialIssueRequests.value.filter(
      (row) => (row.materialLotId ?? '').trim().length === 0 && returnableQuantityOf(row) > 0,
    ),
  )
  const reservationRows = computed(() =>
    materialIssueRequests.value.filter(
      (row) =>
        (row.receivedQuantity ?? 0) <= 0 &&
        !TERMINAL_ISSUE_STATUSES.has((row.status ?? '').toLowerCase()),
    ),
  )
  const cancellableReceiptCount = computed(
    () =>
      finishedGoodsReceiptRequests.value.filter(
        (row) => !TERMINAL_RECEIPT_STATUSES.has((row.receiptStatus ?? '').toLowerCase()),
      ).length,
  )
  const cancellableTaskCount = computed(
    () =>
      operationTasks.value.filter(
        (task) => !TERMINAL_TASK_STATUSES.has((task.status ?? '').toLowerCase()),
      ).length,
  )
  const hasCompensation = computed(
    () =>
      reservationRows.value.length > 0 ||
      lineSideReturnRows.value.length > 0 ||
      cancellableReceiptCount.value > 0 ||
      cancellableTaskCount.value > 0,
  )

  const requiresRemark = computed(() => cancelForm.reasonCode === 'other')
  // 后端 CancelWorkOrderCommandValidator 对最终 reason 限 500 字符。最终 reason = 「原因标签[：备注]」，
  // 与 submitCancel 的拼接完全一致，据此前置限制与计数，避免用户填满后才收到后端 400。
  const REASON_MAX_LENGTH = 500
  const cancelReasonLabel = computed(
    () =>
      CANCEL_REASONS.find((option) => option.value === cancelForm.reasonCode)?.label ??
      cancelForm.reasonCode,
  )
  const finalReasonLength = computed(() => {
    const remark = cancelForm.remark.trim()
    return remark
      ? cancelReasonLabel.value.length + 1 + remark.length
      : cancelReasonLabel.value.length
  })
  const showReasonLengthHint = computed(() => finalReasonLength.value > REASON_MAX_LENGTH * 0.8)
  const remarkMaxLength = computed(() =>
    Math.max(0, REASON_MAX_LENGTH - cancelReasonLabel.value.length - 1),
  )
  const canSubmitCancel = computed(() => {
    // 破坏性动作：两项补偿预览成功拿到数据前禁用确认（慢网/失败时不允许在空数据上确认）。
    if (!workOrderManageScopeReady.value || !cancelPreviewReady.value) return false
    // 存在无批次却可退的已收料申请时，后端会拒绝整单取消——前置阻断，不允许确认。
    if (unreturnableNoLotRows.value.length > 0) return false
    if (!cancelForm.reasonCode) return false
    if (requiresRemark.value && !cancelForm.remark.trim()) return false
    if (finalReasonLength.value > REASON_MAX_LENGTH) return false
    return true
  })

  // 取消弹窗的只读上下文：全部来自当前工单详情，操作员只补原因与备注。
  const cancelContextItems = computed(() => [
    { label: '工单', value: workOrderLabel.value },
    { label: '状态', value: formatStatus(detail.value?.status) },
    { label: '物料', value: skuLabel.value },
    { label: '计划数量', value: formatQuantity(detail.value?.quantity) },
  ])

  function resetCancelForm() {
    cancelForm.reasonCode = ''
    cancelForm.remark = ''
  }
  function openCancelDialog() {
    if (!canCancel.value) return
    activateCancelPreview()
    cancelOpen.value = true
  }
  async function submitCancel() {
    if (!canSubmitCancel.value || cancelWorkOrderPending.value) return
    const label =
      CANCEL_REASONS.find((option) => option.value === cancelForm.reasonCode)?.label ??
      cancelForm.reasonCode
    const remark = cancelForm.remark.trim()
    const reason = remark ? `${label}：${remark}` : label
    // 快照补偿计数：取消成功后相关查询会失效刷新，届时预留/退料列表已清空
    const releasedCount = reservationRows.value.length
    const returnCount = lineSideReturnRows.value.length
    try {
      await cancelWorkOrder(reason)
      cancelOpen.value = false
      resetCancelForm()
      notifySuccess(
        `已取消工单 ${workOrderLabel.value}：${releasedCount} 项预留释放、${returnCount} 项退料指引生成。`,
      )
    } catch (error) {
      if (
        await recoverLifecycleAction(error, {
          reset: () => {
            cancelOpen.value = false
            resetCancelForm()
          },
          refresh: refreshDetail,
          notify: (message) => notifyError(message),
        })
      ) {
        return
      }
      notifyOperationFailure('取消工单失败', error, '取消工单失败，请稍后重试。')
    }
  }

  return {
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
  }
}
