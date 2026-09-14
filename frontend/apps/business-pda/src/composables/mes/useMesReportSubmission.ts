import { describeRequestError } from '@/api/request-timeout'
import { makeIdempotencyKey } from '@/composables/makeIdempotencyKey'
import type { MesReportExecutionContext, RecordReportInput } from '@/composables/useBusinessMes'
import type {
  BusinessConsoleMesOperationTaskRow,
  BusinessConsoleRecordProductionReportResponse,
} from '@nerv-iip/api-client'
import type { ReportCtx } from '@nerv-iip/business-core'
import { computed, reactive, watch, type ComputedRef, type Ref } from 'vue'

export type MesReportResult = {
  status: 'success' | 'error'
  title: string
  description?: string
  preparationError?: string
  receipt?: BusinessConsoleRecordProductionReportResponse
}

interface ReportIntent {
  attempt: symbol
  workOrderId: string
  operationTaskId: string
  intentKey: string
  context: MesReportExecutionContext
  payload: Omit<RecordReportInput, 'workOrderId' | 'operationTaskId' | 'idempotencyKey'>
  status: 'pending' | 'success' | 'error'
  receipt:
    | (BusinessConsoleRecordProductionReportResponse & {
        reportNo: string
        productionReportId: string
      })
    | null
  result: MesReportResult | null
}

// One local recovery slot, not a queue: the shared write intent remains the owner of
// the wire payload/key/time. This snapshot only restores its confirmed PDA result.
const PREPARATION_STORAGE_KEY = 'nerv-iip.pda-mes-report-preparation.v1'
type StoredPreparation = Omit<ReportIntent, 'attempt' | 'status'>

function readPreparation(): StoredPreparation | undefined {
  try {
    const saved = JSON.parse(sessionStorage.getItem(PREPARATION_STORAGE_KEY) ?? 'null')
    if (
      saved?.result?.status === 'success' &&
      saved.result.receipt?.printingPreparationPending === true &&
      typeof saved.workOrderId === 'string' &&
      typeof saved.operationTaskId === 'string' &&
      typeof saved.intentKey === 'string' &&
      saved.context &&
      saved.payload &&
      typeof saved.receipt?.reportNo === 'string' &&
      typeof saved.receipt?.productionReportId === 'string'
    )
      return saved
  } catch {
    // Unavailable/corrupt browser session data must not authorize a write.
  }
}

function savePreparation(intent: ReportIntent) {
  try {
    if (intent.result?.status === 'success' && intent.result.receipt?.printingPreparationPending) {
      const { attempt: _attempt, status: _status, ...saved } = intent
      sessionStorage.setItem(PREPARATION_STORAGE_KEY, JSON.stringify(saved))
    } else if (readPreparation()?.intentKey === intent.intentKey) {
      sessionStorage.removeItem(PREPARATION_STORAGE_KEY)
    }
  } catch {
    // The current page still retains its confirmed result when storage is unavailable.
  }
}

function forgetPreparation(intentKey: string) {
  try {
    if (readPreparation()?.intentKey === intentKey)
      sessionStorage.removeItem(PREPARATION_STORAGE_KEY)
  } catch {
    // Storage availability does not change runtime context invalidation.
  }
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
  serialValid: ComputedRef<boolean>
  serialRequired: ComputedRef<boolean>
  labelTemplateId: Ref<string>
  invalidMaterialLots: ComputedRef<boolean>
  invalidScrapReasonCode: ComputedRef<boolean>
  goodQuantity: Ref<number>
  scrapQuantity: Ref<number>
  reworkQuantity: Ref<number>
  scrapReasonCode: Ref<string>
  consumedMaterialLots: ComputedRef<RecordReportInput['consumedMaterialLots']>
  completesOperation: Ref<boolean>
  recordReport: (
    input: RecordReportInput,
    isCurrent?: () => boolean,
  ) => Promise<{
    success?: boolean
    message?: string | null
    data?: BusinessConsoleRecordProductionReportResponse | null
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

function sameStoredContext(current: MesReportExecutionContext, saved: MesReportExecutionContext) {
  // Generation is a runtime invalidation counter, not a cross-reload identity.
  return (
    reportContextKey({ ...current, generation: 0 }) ===
    reportContextKey({ ...saved, generation: 0 })
  )
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
    [pairKey, options.selectedTask, options.reportScopeReady],
    () => {
      const context = options.context.value
      const pair = options.pair.value
      const task = options.selectedTask.value
      if (!context || !pair || !task || !options.reportScopeReady.value || currentIntent.value)
        return
      const saved = readPreparation()
      if (
        !saved ||
        !sameStoredContext(context, saved.context) ||
        saved.workOrderId !== pair.workOrderId ||
        saved.operationTaskId !== pair.operationTaskId ||
        task.workOrderId !== pair.workOrderId ||
        task.operationTaskId !== pair.operationTaskId
      )
        return
      intents.set(pairKey.value, {
        ...saved,
        context: { ...context },
        attempt: Symbol('mes-report-preparation-restored'),
        status: 'success',
      })
    },
    { immediate: true },
  )

  watch(
    options.contextGeneration,
    (generation) => {
      for (const [key, intent] of intents) {
        if (intent.context.generation === generation) continue
        intent.attempt = Symbol('mes-report-context-invalidated')
        forgetPreparation(intent.intentKey)
        intents.delete(key)
      }
    },
    { flush: 'sync' },
  )

  watch(
    () => currentIntent.value?.result?.status,
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
    const confirmedResult = intent?.result?.status === 'success' ? intent.result : null
    if (
      intent?.status === 'pending' ||
      (intent?.status === 'success' && !intent.result?.receipt?.printingPreparationPending)
    )
      return
    if (!intent) {
      if (
        !options.quantityValid.value ||
        !options.serialValid.value ||
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
          ...(options.serialRequired.value && options.goodQuantity.value > 0
            ? { labelTemplateId: options.labelTemplateId.value }
            : {}),
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
      intent.result = confirmedResult ? { ...confirmedResult, preparationError: undefined } : null
    }
    options.flowContext.quantityEntered = true
    const attempt = intent.attempt
    try {
      if (!intent.receipt || intent.receipt.printingPreparationPending) {
        const receiptEnvelope = await options.recordReport(
          {
            workOrderId,
            operationTaskId,
            ...intent.payload,
            idempotencyKey: intent.intentKey,
          },
          () => pairKey.value === key && intent.attempt === attempt,
        )
        if (intent.attempt !== attempt) return
        if (!receiptEnvelope?.success) {
          throw new Error(receiptEnvelope?.message?.trim() || '报工回执无效，请重试。')
        }
        const reportNo = receiptEnvelope.data?.reportNo?.trim()
        const productionReportId = receiptEnvelope.data?.productionReportId?.trim()
        if (!reportNo || !productionReportId) {
          throw new Error('报工回执缺少真实报工单号或回执 ID，已阻止成功确认。')
        }
        intent.receipt = { ...receiptEnvelope.data, reportNo, productionReportId }
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
        receipt: intent.receipt,
      }
      savePreparation(intent)
    } catch (error) {
      if (intent.attempt !== attempt) return
      if (confirmedResult) {
        intent.status = 'success'
        intent.result = {
          ...confirmedResult,
          preparationError: describeRequestError(error, '标签准备暂未完成，请稍后重试。').message,
        }
        savePreparation(intent)
        return
      }
      if (await options.recoverLifecycleAction(error)) return
      intent.status = 'error'
      intent.result = {
        status: 'error',
        title: intent.receipt ? '报工已受理，待核验' : '报工结果待核实',
        description: describeRequestError(error, '请检查网络后重试。').message,
        receipt: intent.receipt ?? undefined,
      }
    }
  }

  return { currentIntent, result, submitting, deleteCurrentIntent, submit }
}
