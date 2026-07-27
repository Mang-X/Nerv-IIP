import type { BusinessConsoleRecordProductionReportRequest } from '@nerv-iip/api-client'
import { statusActionGate } from '@nerv-iip/business-core'
import { computed, reactive, ref, watch } from 'vue'

import { makeIdempotencyKey, useMesProductionReporting } from '@/composables/useBusinessMes'
import { recoverLifecycleAction } from '@/composables/lifecycleAction'
import { notifyError, notifySuccess } from '@/utils/notify'

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

  function resetForm() {
    form.goodQuantity = '1'
    form.scrapQuantity = '0'
    form.completesOperation = canCompleteOperation.value
    form.idempotencyKey = makeIdempotencyKey('production-report')
    showErrors.value = false
  }

  // 切换报工对象（从工单 A 的工序切到 B）时整表重置，避免把 A 的数量与登记会话幂等键提交到 B。
  watch(
    () => {
      const ctx = context()
      return ctx ? `${ctx.workOrderId}|${ctx.operationTaskId}|${ctx.operationStatus ?? ''}` : ''
    },
    () => resetForm(),
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

  async function submit(): Promise<boolean> {
    showErrors.value = true
    const ctx = context()
    if (!ctx || !canSubmit.value) return false
    const body: BusinessConsoleRecordProductionReportRequest = {
      workOrderId: ctx.workOrderId.trim(),
      operationTaskId: ctx.operationTaskId.trim(),
      goodQuantity: goodQuantity.value,
      scrapQuantity: scrapQuantity.value,
      completesOperation: form.completesOperation,
      reportedAtUtc: new Date().toISOString(),
      idempotencyKey: form.idempotencyKey,
    }
    try {
      const response = await recordProductionReport(body)
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
      notifyError(recordProductionReportError.value ?? error, '报工提交失败，请稍后重试。')
      return false
    }
  }

  return {
    form,
    invalid,
    showErrors,
    canSubmit,
    canCompleteOperation,
    recordProductionReportPending,
    resetForm,
    submit,
  }
}
