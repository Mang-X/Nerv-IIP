import { statusActionGate } from '@nerv-iip/business-core'
import { computed, reactive, ref, shallowRef, watch } from 'vue'

import {
  makeIdempotencyKey,
  useMesProductionMaterialLots,
  useMesProductionReporting,
  useMesScrapReasonCodes,
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
  const {
    materialsReadPermission,
    materialLotsPending,
    materialLotsError,
    availableMaterialLots,
    refreshMaterialLots,
  } = useMesProductionMaterialLots(() => {
    const ctx = context()
    return ctx ? { workOrderId: ctx.workOrderId, operationTaskId: ctx.operationTaskId } : null
  })

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
    reworkQuantity: '0',
    scrapReasonCode: '',
    completesOperation: canCompleteOperation.value,
    idempotencyKey: makeIdempotencyKey('production-report'),
  })
  const {
    qualityInspectionRecordsReadPermission,
    scrapReasonCodesPending,
    scrapReasonCodesError,
    scrapReasonCodes,
    refreshScrapReasonCodes,
  } = useMesScrapReasonCodes(() => (toOptionalNumber(form.scrapQuantity) ?? 0) > 0)

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
  const materialSelections = reactive(
    new Map<string, { selected: boolean; consumedQuantity: string }>(),
  )
  let resetting = false

  function resetForm() {
    resetting = true
    intentAttempted.value = false
    intentLocked.value = false
    frozenPayload.value = undefined
    materialSelections.clear()
    quantityValidationMessage.value = ''
    overproductionConfirmationRequired.value = false
    confirmedOverproductionFingerprint.value = ''
    form.goodQuantity = '1'
    form.scrapQuantity = '0'
    form.reworkQuantity = '0'
    form.scrapReasonCode = ''
    form.completesOperation = canCompleteOperation.value
    form.idempotencyKey = makeIdempotencyKey('production-report')
    showErrors.value = false
    resetting = false
  }

  watch(
    () =>
      `${form.goodQuantity}\u0000${form.scrapQuantity}\u0000${form.reworkQuantity}\u0000${form.scrapReasonCode}\u0000${form.completesOperation}\u0000${JSON.stringify([...materialSelections])}`,
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

  watch(
    availableMaterialLots,
    (rows) => {
      const knownRequestIds = new Set(rows.map((row) => row.requestId))
      for (const row of rows) {
        if (!materialSelections.has(row.requestId)) {
          materialSelections.set(row.requestId, { selected: false, consumedQuantity: '' })
        }
      }
      for (const requestId of materialSelections.keys()) {
        if (!knownRequestIds.has(requestId)) materialSelections.delete(requestId)
      }
    },
    { immediate: true },
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
  const reworkQuantity = computed(() => toOptionalNumber(form.reworkQuantity))
  const scrapReasonCode = computed(() => form.scrapReasonCode.trim())
  const consumedMaterialLots = computed(() =>
    availableMaterialLots.value.flatMap((row) => {
      const selection = materialSelections.get(row.requestId)
      const materialLotId = row.materialLotId?.trim()
      if (!selection?.selected || !materialLotId) return []
      return [
        {
          materialId: row.materialId,
          materialLotId,
          consumedQuantity: toOptionalNumber(selection.consumedQuantity) ?? 0,
          materialIssueRequestNo: row.requestId,
        },
      ]
    }),
  )
  const invalidMaterialLots = computed(() => {
    const scrap = scrapQuantity.value ?? 0
    if (scrap > 0 && consumedMaterialLots.value.length === 0) return true
    return availableMaterialLots.value.some((row) => {
      const selection = materialSelections.get(row.requestId)
      if (!selection?.selected) return false
      const consumedQuantity = toOptionalNumber(selection.consumedQuantity)
      return (
        consumedQuantity === undefined ||
        consumedQuantity <= 0 ||
        consumedQuantity > row.receivedQuantity - row.consumedQuantity
      )
    })
  })
  const materialValidationMessage = computed(() => {
    if (!materialsReadPermission.value && (scrapQuantity.value ?? 0) > 0) {
      return '当前账号没有材料读取权限，无法为报废报工选择耗料批次。'
    }
    if ((scrapQuantity.value ?? 0) > 0 && consumedMaterialLots.value.length === 0) {
      return '报废报工至少选择一个已收料批次。'
    }
    if (invalidMaterialLots.value) return '耗料数量必须大于 0，且不能超过该批次可用数量。'
    return ''
  })
  const invalidScrapReasonCode = computed(() => {
    if ((scrapQuantity.value ?? 0) <= 0) return false
    if (!qualityInspectionRecordsReadPermission.value) return true
    if (scrapReasonCodesPending.value || scrapReasonCodesError.value) return true
    return (
      !scrapReasonCode.value ||
      !scrapReasonCodes.value.some(
        (row) => row.reasonCode?.trim() === scrapReasonCode.value && row.enabled !== false,
      )
    )
  })
  const scrapReasonValidationMessage = computed(() => {
    if ((scrapQuantity.value ?? 0) <= 0) return ''
    if (!qualityInspectionRecordsReadPermission.value) {
      return '当前账号没有质量原因码读取权限，无法提交报废报工。'
    }
    if (scrapReasonCodesPending.value) return '正在读取报废原因码，请稍后重试。'
    if (scrapReasonCodesError.value) return '报废原因码读取失败，请刷新后重试。'
    if (scrapReasonCodes.value.length === 0) return '当前没有可用的报废原因码。'
    if (!scrapReasonCode.value) return '报废数量大于 0 时必须选择报废原因码。'
    return '所选报废原因码已失效，请重新选择。'
  })

  const invalid = computed(() => {
    const good = goodQuantity.value
    const scrap = scrapQuantity.value
    const rework = reworkQuantity.value
    const totalPositive =
      good !== undefined && scrap !== undefined && rework !== undefined && good + scrap + rework > 0
    return {
      goodQuantity: good === undefined || good < 0 || !totalPositive,
      scrapQuantity: scrap === undefined || scrap < 0 || !totalPositive,
      reworkQuantity: reworkQuantity.value === undefined || reworkQuantity.value < 0,
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
    return (
      !invalid.value.goodQuantity &&
      !invalid.value.scrapQuantity &&
      !invalid.value.reworkQuantity &&
      !invalidMaterialLots.value &&
      !invalidScrapReasonCode.value
    )
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
        reworkQuantity: reworkQuantity.value,
        scrapReasonCode: (scrapQuantity.value ?? 0) > 0 ? scrapReasonCode.value : undefined,
        consumedMaterialLots: consumedMaterialLots.value,
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
    materialsReadPermission,
    materialLotsPending,
    materialLotsError,
    availableMaterialLots,
    refreshMaterialLots,
    materialSelections,
    consumedMaterialLots,
    invalidMaterialLots,
    materialValidationMessage,
    qualityInspectionRecordsReadPermission,
    scrapReasonCodesPending,
    scrapReasonCodesError,
    scrapReasonCodes,
    refreshScrapReasonCodes,
    invalidScrapReasonCode,
    scrapReasonValidationMessage,
    materialSelected: (requestId: string | undefined) =>
      requestId ? (materialSelections.get(requestId)?.selected ?? false) : false,
    materialQuantity: (requestId: string) =>
      materialSelections.get(requestId)?.consumedQuantity ?? '',
    setMaterialSelected: (
      requestId: string | undefined,
      selected: boolean | 'indeterminate' | undefined,
    ) => {
      if (!requestId) return
      const current = materialSelections.get(requestId) ?? { selected: false, consumedQuantity: '' }
      current.selected = selected === true
      materialSelections.set(requestId, current)
    },
    setMaterialQuantity: (requestId: string | undefined, quantity: string | number | undefined) => {
      if (!requestId) return
      const current = materialSelections.get(requestId) ?? { selected: false, consumedQuantity: '' }
      current.consumedQuantity = quantity === undefined ? '' : String(quantity)
      materialSelections.set(requestId, current)
    },
    intentLocked,
    recordProductionReportPending,
    quantitySnapshotPending,
    quantityValidationMessage,
    overproductionConfirmationRequired,
    resetForm,
    submit,
  }
}
