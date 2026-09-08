import { describeRequestError } from '@/api/request-timeout'
import { makeIdempotencyKey } from '@/composables/makeIdempotencyKey'
import type { MesReportExecutionContext, RecordReportInput } from '@/composables/useBusinessMes'
import type { BusinessConsoleMesOperationTaskRow } from '@nerv-iip/api-client'
import type { ReportCtx } from '@nerv-iip/business-core'
import { computed, reactive, watch, type ComputedRef, type Ref } from 'vue'

export type MesReportResult = {
  status: 'success' | 'error'
  title: string
  description?: string
}

interface ReportIntent {
  attempt: symbol
  workOrderId: string
  operationTaskId: string
  intentKey: string
  context: MesReportExecutionContext
  payload: Omit<RecordReportInput, 'workOrderId' | 'operationTaskId' | 'idempotencyKey'>
  status: 'pending' | 'success' | 'error'
  receipt: { reportNo: string; productionReportId: string } | null
  result: MesReportResult | null
}

interface MesReportSubmissionOptions {
  pair: ComputedRef<{ workOrderId: string; operationTaskId: string } | null>
  selectedTask: ComputedRef<BusinessConsoleMesOperationTaskRow | null>
  context: ComputedRef<MesReportExecutionContext | undefined>
  contextGeneration: Ref<number>
  flowContext: ReportCtx
  scanGuarded: Ref<boolean>
  reportScopeReady: ComputedRef<boolean>
  quantityValid: ComputedRef<boolean>
  invalidMaterialLots: ComputedRef<boolean>
  invalidScrapReasonCode: ComputedRef<boolean>
  goodQuantity: Ref<number>
  scrapQuantity: Ref<number>
  reworkQuantity: Ref<number>
  scrapReasonCode: Ref<string>
  consumedMaterialLots: ComputedRef<RecordReportInput['consumedMaterialLots']>
  completesOperation: Ref<boolean>
  recordReport: (input: RecordReportInput) => Promise<{
    success?: boolean
    message?: string | null
    data?: { reportNo?: string | null; productionReportId?: string | null } | null
  }>
  confirmReport: (input: {
    reportNo: string
    productionReportId: string
    workOrderId: string
    operationTaskId: string
    context: MesReportExecutionContext
  }) => Promise<unknown>
  recoverLifecycleAction: (error: unknown) => Promise<boolean>
}

function reportContextKey(context: MesReportExecutionContext | undefined) {
  if (!context) return ''
  return [
    context.principalId,
    context.organizationId,
    context.environmentId,
    context.scopeKind,
    context.scopeId,
    String(context.generation),
  ].join('\u0000')
}

export function useMesReportSubmission(options: MesReportSubmissionOptions) {
  const intents = reactive(new Map<string, ReportIntent>())
  const pairKey = computed(() => {
    const contextKey = reportContextKey(options.context.value)
    const pair = options.pair.value
    return pair && contextKey
      ? `${contextKey}\u0000${pair.workOrderId}\u0000${pair.operationTaskId}`
      : ''
  })
  const currentIntent = computed(() => (pairKey.value ? intents.get(pairKey.value) : undefined))
  const result = computed(() => currentIntent.value?.result ?? null)
  const submitting = computed(() => currentIntent.value?.status === 'pending')

  watch(
    options.contextGeneration,
    (generation) => {
      for (const [key, intent] of intents) {
        if (intent.context.generation === generation) continue
        intent.attempt = Symbol('mes-report-context-invalidated')
        intents.delete(key)
      }
    },
    { flush: 'sync' },
  )

  watch(
    () => currentIntent.value?.status,
    (status) => {
      options.flowContext.recorded = status === 'success'
    },
  )

  function deleteCurrentIntent() {
    if (pairKey.value) intents.delete(pairKey.value)
  }

  async function submit() {
    if (options.scanGuarded.value || !options.reportScopeReady.value) return
    const executionContext = options.context.value
    if (!executionContext) return
    const identity = options.pair.value
    const task = options.selectedTask.value
    const workOrderId = identity?.workOrderId
    const operationTaskId = identity?.operationTaskId
    if (
      !workOrderId ||
      options.flowContext.workOrderId !== workOrderId ||
      !operationTaskId ||
      options.flowContext.operationTaskId !== operationTaskId ||
      !task ||
      task.workOrderId !== workOrderId
    ) {
      return
    }
    const key = `${reportContextKey(executionContext)}\u0000${workOrderId}\u0000${operationTaskId}`
    let intent = intents.get(key)
    if (intent?.status === 'pending' || intent?.status === 'success') return
    if (!intent) {
      if (
        !options.quantityValid.value ||
        options.invalidMaterialLots.value ||
        options.invalidScrapReasonCode.value
      ) {
        return
      }
      intent = {
        attempt: Symbol('mes-report-attempt'),
        workOrderId,
        operationTaskId,
        intentKey: makeIdempotencyKey(),
        context: { ...executionContext },
        payload: {
          goodQuantity: options.goodQuantity.value,
          scrapQuantity: options.scrapQuantity.value,
          reworkQuantity: options.reworkQuantity.value,
          scrapReasonCode:
            options.scrapQuantity.value > 0 ? options.scrapReasonCode.value.trim() : undefined,
          consumedMaterialLots: options.consumedMaterialLots.value,
          completesOperation: options.completesOperation.value,
        },
        status: 'pending',
        receipt: null,
        result: null,
      }
      intents.set(key, intent)
      intent = intents.get(key)!
    } else {
      intent.attempt = Symbol('mes-report-retry')
      intent.status = 'pending'
      intent.result = null
    }
    options.flowContext.quantityEntered = true
    const attempt = intent.attempt
    try {
      if (!intent.receipt) {
        const receiptEnvelope = await options.recordReport({
          workOrderId,
          operationTaskId,
          ...intent.payload,
          idempotencyKey: intent.intentKey,
        })
        if (intent.attempt !== attempt) return
        if (!receiptEnvelope?.success) {
          throw new Error(receiptEnvelope?.message?.trim() || '报工回执无效，请重试。')
        }
        const reportNo = receiptEnvelope.data?.reportNo?.trim()
        const productionReportId = receiptEnvelope.data?.productionReportId?.trim()
        if (!reportNo || !productionReportId) {
          throw new Error('报工回执缺少真实报工单号或回执 ID，已阻止成功确认。')
        }
        intent.receipt = { reportNo, productionReportId }
      }
      const { reportNo, productionReportId } = intent.receipt
      await options.confirmReport({
        reportNo,
        productionReportId,
        workOrderId,
        operationTaskId,
        context: intent.context,
      })
      if (intent.attempt !== attempt) return
      const description = [
        `${workOrderId} · ${operationTaskId}`,
        `报工单号 ${reportNo}`,
        `回执 ID ${productionReportId}`,
      ]
      if (intent.payload.completesOperation) description.push('本工序已标记完工')
      intent.status = 'success'
      intent.result = {
        status: 'success',
        title: '报工成功',
        description: description.join('；'),
      }
    } catch (error) {
      if (intent.attempt !== attempt) return
      if (await options.recoverLifecycleAction(error)) return
      intent.status = 'error'
      intent.result = {
        status: 'error',
        title: '报工失败',
        description: describeRequestError(error, '请检查网络后重试。').message,
      }
    }
  }

  return { currentIntent, result, submitting, deleteCurrentIntent, submit }
}
