import { statusActionGate } from '@nerv-iip/business-core'
import { computed, reactive, ref, shallowRef, watch } from 'vue'

import {
  makeIdempotencyKey,
  useMesProductionReporting,
  type MesProductionReportInput,
} from '@/composables/useBusinessMes'
import {
  isIndeterminateLifecycleWriteError,
  recoverLifecycleAction,
} from '@/composables/lifecycleAction'
import { notifyError, notifyOperationFailure, notifySuccess } from '@/utils/notify'

/**
 * 报工上下文：**只能**由工单列表行 / 工序任务行带出，弹窗自身不提供任何挑选入口。
 * 带不出来的字段（如工序任务行没有物料与计划数量）就不展示，绝不退化成手填。
 */
export interface ProductionReportContext {
  workOrderId: string
  workOrderNo?: string | null
  operationTaskId: string
  operationTaskNo?: string | null
  operationSequence?: number | null
  operationStatus?: string | null
  /** 工作中心显示名（已解析为人读名称，不是 ID）。 */
  workCenterLabel?: string | null
  /** 物料显示名（已解析为人读名称，不是 ID）。 */
  skuLabel?: string | null
  /** 工单计划数量。 */
  plannedQuantity?: number | null
}

function toOptionalNumber(value: string) {
  const parsed = Number(value)
  return Number.isFinite(parsed) ? parsed : undefined
}

/**
 * 报工「带出式录入」表单：上下文全部由调用方带入并只读呈现，一线只填合格数量、不合格数量与完成状态。
 *
 * - **报工时间**不作为录入项：一线报的是「刚做完这一批」，提交时取当前时间。需要补录历史时间是班组长/计划
 *   岗的纠错场景，走冲销 + 重报，不在一线录入面上开口子。
 * - 结果一律 toast，弹窗内不留常驻成功/错误条（feedback-and-notifications）。
 * - 校验点提交才标红；未通过不发请求。
 */
export function useProductionReportForm(
  context: () => ProductionReportContext | null,
  options: { onReported?: () => void; onStateChanged?: () => void } = {},
) {
  const {
    recordProductionReport,
    recordProductionReportError,
    recordProductionReportPending,
    readProductionQuantitySnapshot,
    reportScopeMessage,
    reportScopePending,
    reportScopeReady,
    refreshProductionReportState,
  } = useMesProductionReporting()

  const canCompleteOperation = computed(() => {
    const ctx = context()
    return (
      ctx !== null &&
      statusActionGate({
        domain: 'mes-operation-task',
        action: 'report-complete',
        facts: { status: ctx.operationStatus },
      }).executable
    )
  })

  const form = reactive({
    goodQuantity: '1',
    scrapQuantity: '0',
    completesOperation: canCompleteOperation.value,
    idempotencyKey: makeIdempotencyKey('production-report'),
  })

  const showErrors = ref(false)
  const intentAttempted = ref(false)
  const intentLocked = ref(false)
  const frozenPayload = shallowRef<MesProductionReportInput>()
  const quantitySnapshot = shallowRef<{
    key: string
    plannedQuantity: number
    reportedGoodQuantity: number
  }>()
  const quantitySnapshotPending = ref(false)
  const quantityValidationMessage = ref('')
  const overproductionConfirmationRequired = ref(false)
  const confirmedOverproductionFingerprint = ref('')
  let resetting = false

  function resetForm() {
    resetting = true
    intentAttempted.value = false
    intentLocked.value = false
    frozenPayload.value = undefined
    quantityValidationMessage.value = ''
    overproductionConfirmationRequired.value = false
    confirmedOverproductionFingerprint.value = ''
    form.goodQuantity = '1'
    form.scrapQuantity = '0'
    form.completesOperation = canCompleteOperation.value
    form.idempotencyKey = makeIdempotencyKey('production-report')
    showErrors.value = false
    resetting = false
  }

  watch(
    () => `${form.goodQuantity}\u0000${form.scrapQuantity}\u0000${form.completesOperation}`,
    () => {
      quantityValidationMessage.value = ''
      overproductionConfirmationRequired.value = false
      confirmedOverproductionFingerprint.value = ''
      if (resetting || !intentAttempted.value || intentLocked.value) return
      form.idempotencyKey = makeIdempotencyKey('production-report')
      intentAttempted.value = false
      frozenPayload.value = undefined
    },
  )

  // 切换报工对象（从工单 A 的工序切到 B）时整表重置，避免把 A 的数量与登记会话幂等键提交到 B。
  watch(
    () => {
      const ctx = context()
      return ctx ? `${ctx.workOrderId}|${ctx.operationTaskId}|${ctx.operationStatus ?? ''}` : ''
    },
    () => {
      quantitySnapshot.value = undefined
      resetForm()
    },
  )

  const goodQuantity = computed(() => toOptionalNumber(form.goodQuantity))
  const scrapQuantity = computed(() => toOptionalNumber(form.scrapQuantity))

  const invalid = computed(() => {
    const good = goodQuantity.value
    const scrap = scrapQuantity.value
    const totalPositive = good !== undefined && scrap !== undefined && good + scrap > 0
    return {
      goodQuantity: good === undefined || good < 0 || !totalPositive,
      scrapQuantity: scrap === undefined || scrap < 0 || !totalPositive,
    }
  })

  const canSubmit = computed(() => {
    const ctx = context()
    if (!ctx?.workOrderId?.trim() || !ctx.operationTaskId?.trim()) return false
    if (!reportScopeReady.value) return false
    if (
      form.completesOperation &&
      !statusActionGate({
        domain: 'mes-operation-task',
        action: 'report-complete',
        facts: { status: ctx.operationStatus },
      }).executable
    ) {
      return false
    }
    return !invalid.value.goodQuantity && !invalid.value.scrapQuantity
  })

  async function ensureQuantitySnapshot(ctx: ProductionReportContext) {
    const key = `${ctx.workOrderId.trim()}\u0000${ctx.operationTaskId.trim()}`
    if (quantitySnapshot.value?.key === key) return quantitySnapshot.value
    quantitySnapshotPending.value = true
    try {
      const snapshot = await readProductionQuantitySnapshot(
        ctx.workOrderId.trim(),
        ctx.operationTaskId.trim(),
      )
      quantitySnapshot.value = { key, ...snapshot }
      return quantitySnapshot.value
    } finally {
      quantitySnapshotPending.value = false
    }
  }

  function formatQuantity(value: number) {
    return new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 3 }).format(value)
  }

  function formatPercent(value: number) {
    return new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 2 }).format(value)
  }

  async function submit(): Promise<boolean> {
    showErrors.value = true
    const ctx = context()
    if (!ctx || !canSubmit.value) {
      if (!reportScopeReady.value) notifyError(reportScopeMessage.value)
      return false
    }
    let snapshot
    try {
      snapshot = await ensureQuantitySnapshot(ctx)
    } catch (error) {
      const message = `生产工单 ${ctx.workOrderNo ?? ctx.workOrderId} 的工序 ${ctx.operationTaskNo ?? ctx.operationTaskId} 无法读取累计合格数量。请刷新工序列表后重试；若仍失败，请联系计划员核对工序。`
      quantityValidationMessage.value = message
      notifyError(message)
      return false
    }
    const currentGoodQuantity = goodQuantity.value ?? 0
    const cumulativeGoodQuantity = snapshot.reportedGoodQuantity + currentGoodQuantity
    const hardMaximumGoodQuantity = snapshot.plannedQuantity * 1.2
    if (snapshot.plannedQuantity <= 0) {
      quantityValidationMessage.value = `生产工单 ${ctx.workOrderNo ?? ctx.workOrderId} 的计划量无效，无法校验本次报工。请先联系计划员修正工单计划量后重试。`
      return false
    }
    if (cumulativeGoodQuantity > hardMaximumGoodQuantity) {
      overproductionConfirmationRequired.value = false
      quantityValidationMessage.value = `生产工单 ${ctx.workOrderNo ?? ctx.workOrderId} 的工序 ${ctx.operationTaskNo ?? ctx.operationTaskId} 本次提交后累计合格数量 ${formatQuantity(cumulativeGoodQuantity)}，超过计划量 ${formatQuantity(snapshot.plannedQuantity)} 的 120% 硬上限 ${formatQuantity(hardMaximumGoodQuantity)}。请调整本次合格数量或工单计划量后重试。`
      return false
    }
    if (cumulativeGoodQuantity > snapshot.plannedQuantity) {
      const fingerprint = `${snapshot.key}\u0000${snapshot.reportedGoodQuantity}\u0000${currentGoodQuantity}`
      if (confirmedOverproductionFingerprint.value !== fingerprint) {
        const overproductionPercent =
          ((cumulativeGoodQuantity - snapshot.plannedQuantity) / snapshot.plannedQuantity) * 100
        overproductionConfirmationRequired.value = true
        confirmedOverproductionFingerprint.value = fingerprint
        quantityValidationMessage.value = `生产工单 ${ctx.workOrderNo ?? ctx.workOrderId} 的工序 ${ctx.operationTaskNo ?? ctx.operationTaskId} 本次提交后累计合格数量 ${formatQuantity(cumulativeGoodQuantity)}，已超计划 ${formatPercent(overproductionPercent)}%。确认继续请再次点击“确认超产并提交”；如数量有误，请先调整本次合格数量。`
        return false
      }
    }
    quantityValidationMessage.value = ''
    overproductionConfirmationRequired.value = false
    const body =
      frozenPayload.value ??
      ({
        workOrderId: ctx.workOrderId.trim(),
        operationTaskId: ctx.operationTaskId.trim(),
        goodQuantity: goodQuantity.value,
        scrapQuantity: scrapQuantity.value,
        completesOperation: form.completesOperation,
        reportedAtUtc: new Date().toISOString(),
        idempotencyKey: form.idempotencyKey,
      } satisfies MesProductionReportInput)
    frozenPayload.value = body
    try {
      const response = await recordProductionReport(body, {
        onCommandAttempt: () => {
          intentAttempted.value = true
        },
      })
      const reportNo = response?.data?.reportNo ?? response?.data?.productionReportId
      notifySuccess(
        `已报工${reportNo ? ` ${reportNo}` : ''} · ${ctx.operationTaskNo ?? ctx.operationTaskId}`,
      )
      resetForm()
      options.onReported?.()
      return true
    } catch (error) {
      if (
        await recoverLifecycleAction(error, {
          reset: () => {
            resetForm()
            options.onStateChanged?.()
          },
          refresh: refreshProductionReportState,
          notify: (message) => notifyError(message),
        })
      ) {
        return false
      }
      intentLocked.value = intentAttempted.value && isIndeterminateLifecycleWriteError(error)
      notifyOperationFailure(
        '报工提交失败',
        recordProductionReportError.value ?? error,
        '报工提交失败，请稍后重试。',
      )
      return false
    }
  }

  return {
    form,
    invalid,
    showErrors,
    canSubmit,
    canCompleteOperation,
    reportScopeMessage,
    reportScopePending,
    reportScopeReady,
    intentLocked,
    recordProductionReportPending,
    quantitySnapshotPending,
    quantityValidationMessage,
    overproductionConfirmationRequired,
    resetForm,
    submit,
  }
}
