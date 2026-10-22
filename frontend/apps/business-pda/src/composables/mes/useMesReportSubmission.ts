import { describeRequestError } from '@/api/request-timeout'
import { makeIdempotencyKey } from '@/composables/makeIdempotencyKey'
import type { MesReportExecutionContext, RecordReportInput } from '@/composables/useBusinessMes'
import type {
  BusinessConsoleMesOperationTaskRow,
  BusinessConsoleRecordProductionReportResponse,
} from '@nerv-iip/api-client'
import {
  acquirePendingBusinessIntent,
  peekPendingBusinessIntent,
  type PendingBusinessIntentScope,
  type ReportCtx,
  operationSequenceLabel,
} from '@nerv-iip/business-core'
import { computed, reactive, ref, watch, type ComputedRef, type Ref } from 'vue'
import { mesReportIntentScope } from './mesReportIntent'

export type MesReportResult = {
  status: 'success' | 'error'
  notAccepted?: boolean
  title: string
  description?: string
  preparationError?: string
  receipt?: BusinessConsoleRecordProductionReportResponse
}

interface ReportIntent {
  owner: string
  reference: PendingBusinessIntentScope
  attempt: symbol
  workOrderId: string
  operationTaskId: string
  intentKey: string
  context: MesReportExecutionContext
  payload?: Omit<RecordReportInput, 'workOrderId' | 'operationTaskId' | 'idempotencyKey'>
  status: 'pending' | 'success' | 'error'
  receipt:
    | (BusinessConsoleRecordProductionReportResponse & {
        reportNo: string
        productionReportId: string
      })
    | null
  result: MesReportResult | null
}

// One occupied slot per execution scope. No wire payload/key/time is copied here.
type StoredPreparation = Pick<
  ReportIntent,
  'owner' | 'reference' | 'workOrderId' | 'operationTaskId' | 'receipt' | 'result'
>
const slotRevision = ref(0)
const inFlight = new Set<string>()
function storageKey(context: MesReportExecutionContext) {
  return `nerv-iip.pda-mes-report-slot.v1:${encodeURIComponent(reportContextKey({ ...context, generation: 0 }))}`
}
function readPreparation(context: MesReportExecutionContext): StoredPreparation | undefined {
  try {
    const saved = JSON.parse(sessionStorage.getItem(storageKey(context)) ?? 'null')
    if (
      typeof saved?.owner === 'string' &&
      typeof saved.workOrderId === 'string' &&
      typeof saved.operationTaskId === 'string' &&
      saved.reference?.principalId === context.principalId &&
      saved.reference?.organizationId === context.organizationId &&
      saved.reference?.environmentId === context.environmentId &&
      typeof saved.reference?.payloadFingerprint === 'string'
    )
      return saved
  } catch {
    // Unavailable/corrupt browser session data must not authorize a write.
  }
}

function savePreparation(intent: ReportIntent, claim = false) {
  const existing = readPreparation(intent.context)
  if (existing ? existing.owner !== intent.owner : !claim) return false
  const { owner, reference, workOrderId, operationTaskId, receipt, result } = intent
  sessionStorage.setItem(
    storageKey(intent.context),
    JSON.stringify({ owner, reference, workOrderId, operationTaskId, receipt, result }),
  )
  slotRevision.value += 1
  return true
}
function releasePreparation(intent: ReportIntent) {
  if (readPreparation(intent.context)?.owner !== intent.owner) return
  sessionStorage.removeItem(storageKey(intent.context))
  slotRevision.value += 1
}
function frozenInput(saved: StoredPreparation): RecordReportInput | undefined {
  const pending = peekPendingBusinessIntent(saved.reference)
  const snapshot = pending?.payloadSnapshot as
    | (RecordReportInput & {
        organizationId: string
        environmentId: string
        scopeKind: string
        scopeId: string
        reportedAtUtc: string
      })
    | undefined
  if (
    !pending ||
    !snapshot ||
    snapshot.workOrderId !== saved.workOrderId ||
    snapshot.operationTaskId !== saved.operationTaskId
  )
    return
  const {
    organizationId: _org,
    environmentId: _env,
    scopeKind: _kind,
    scopeId: _scope,
    reportedAtUtc: _time,
    ...input
  } = snapshot
  return { ...input, idempotencyKey: pending.idempotencyKey }
}

interface MesReportSubmissionOptions {
  pair: ComputedRef<{ workOrderId: string; operationTaskId: string } | null>
  recoveryPair: ComputedRef<{ workOrderId: string; operationTaskId: string } | null>
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
    recovering?: boolean,
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

export function useMesReportSubmission(options: MesReportSubmissionOptions) {
  const intents = reactive(new Map<string, ReportIntent>())
  const pairKey = computed(() => {
    const contextKey = reportContextKey(options.context.value)
    const pair = options.recoveryPair.value
    return pair && contextKey
      ? `${contextKey}\u0000${pair.workOrderId}\u0000${pair.operationTaskId}`
      : ''
  })
  const currentIntent = computed(() => (pairKey.value ? intents.get(pairKey.value) : undefined))
  const result = computed(() => currentIntent.value?.result ?? null)
  const submitting = computed(() => {
    slotRevision.value
    return !!currentIntent.value && inFlight.has(currentIntent.value.owner)
  })
  const occupied = computed(() => {
    slotRevision.value
    return options.context.value ? readPreparation(options.context.value) : undefined
  })
  const conflictingPreparation = computed(() => {
    const saved = occupied.value
    const pair = options.recoveryPair.value
    return saved &&
      (saved.workOrderId !== pair?.workOrderId || saved.operationTaskId !== pair?.operationTaskId)
      ? saved
      : undefined
  })

  watch(
    pairKey,
    (_current, previous) => {
      const intent = intents.get(previous)
      if (!intent || !inFlight.has(intent.owner)) return
      intent.attempt = Symbol('mes-report-route-invalidated')
      intents.delete(previous)
    },
    { flush: 'sync' },
  )

  watch(
    [pairKey, options.selectedTask, options.reportScopeReady, slotRevision],
    () => {
      const context = options.context.value
      const pair = options.recoveryPair.value
      const task = options.selectedTask.value
      if (!context || !pair || !task || !options.reportScopeReady.value || currentIntent.value)
        return
      const saved = readPreparation(context)
      if (
        !saved ||
        saved.workOrderId !== pair.workOrderId ||
        saved.operationTaskId !== pair.operationTaskId ||
        task.workOrderId !== pair.workOrderId ||
        task.operationTaskId !== pair.operationTaskId
      )
        return
      const input = frozenInput(saved)
      const {
        workOrderId: _workOrder,
        operationTaskId: _operation,
        idempotencyKey: intentKey,
        ...payload
      } = input ?? {}
      intents.set(pairKey.value, {
        ...saved,
        intentKey: intentKey ?? '',
        payload: input ? payload : undefined,
        context: { ...context },
        attempt: Symbol('mes-report-preparation-restored'),
        status: saved.result?.status ?? 'error',
        result: saved.result ?? {
          status: 'error',
          title: '报工结果待核实',
          description: '请沿用原报工核验结果，勿重复录入产量。',
        },
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
    const identity = options.recoveryPair.value
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
    const slot = readPreparation(executionContext)
    if (slot && (slot.workOrderId !== workOrderId || slot.operationTaskId !== operationTaskId))
      return
    const confirmedResult = intent?.result?.status === 'success' ? intent.result : null
    if (
      (intent && inFlight.has(intent.owner)) ||
      (intent?.status === 'success' && !intent.result?.receipt?.printingPreparationPending)
    )
      return
    if (!intent) {
      if (
        !options.pair.value ||
        !options.quantityValid.value ||
        !options.serialValid.value ||
        options.invalidMaterialLots.value ||
        options.invalidScrapReasonCode.value
      ) {
        return
      }
      intent = {
        owner: makeIdempotencyKey(),
        reference: {} as PendingBusinessIntentScope,
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
      const input = {
        workOrderId,
        operationTaskId,
        ...intent.payload!,
        idempotencyKey: intent.intentKey,
      }
      intent.reference = mesReportIntentScope(executionContext, input)
      const pending = acquirePendingBusinessIntent(intent.reference, () => intent!.intentKey, {
        ...input,
        organizationId: executionContext.organizationId,
        environmentId: executionContext.environmentId,
        reportedAtUtc: new Date().toISOString(),
        scopeKind: executionContext.scopeKind,
        scopeId: executionContext.scopeId,
      })
      intent.intentKey = pending.idempotencyKey
      intents.set(key, intent)
      intent = intents.get(key)!
    } else {
      intent.attempt = Symbol('mes-report-retry')
      intent.status = 'pending'
      intent.result = confirmedResult ? { ...confirmedResult, preparationError: undefined } : null
    }
    options.flowContext.quantityEntered = true
    const attempt = intent.attempt
    const isCurrent = () => pairKey.value === key && intent.attempt === attempt
    try {
      if (!savePreparation(intent, !slot)) return
      inFlight.add(intent.owner)
      slotRevision.value += 1
      if (!intent.receipt || intent.receipt.printingPreparationPending) {
        const input = frozenInput(intent)
        if (!input) throw new Error('原报工请求凭据不可用，请联系现场负责人核验，勿重新录入产量。')
        const receiptEnvelope = await options.recordReport(input, isCurrent, !!slot)
        if (!isCurrent()) return
        if (!receiptEnvelope?.success) {
          throw new Error(receiptEnvelope?.message?.trim() || '报工回执无效，请重试。')
        }
        const reportNo = receiptEnvelope.data?.reportNo?.trim()
        const productionReportId = receiptEnvelope.data?.productionReportId?.trim()
        if (!reportNo || !productionReportId) {
          throw new Error('报工结果缺少报工单号，已阻止成功确认。')
        }
        intent.receipt = { ...receiptEnvelope.data, reportNo, productionReportId }
        savePreparation(intent)
      }
      const { reportNo, productionReportId } = intent.receipt
      await options.confirmReport({
        reportNo,
        productionReportId,
        workOrderId,
        operationTaskId,
        context: intent.context,
      })
      if (!isCurrent()) return
      const description = [
        `${workOrderId} · ${operationSequenceLabel(task.operationSequence)}`,
        `报工单号 ${reportNo}`,
      ]
      if (intent.payload?.completesOperation) description.push('本工序已标记完工')
      intent.status = 'success'
      intent.result = {
        status: 'success',
        title: '报工成功',
        description: description.join('；'),
        receipt: intent.receipt,
      }
      if (intent.receipt.printingPreparationPending) savePreparation(intent)
      else releasePreparation(intent)
    } catch (error) {
      if (!isCurrent()) return
      if (confirmedResult) {
        intent.status = 'success'
        intent.result = {
          ...confirmedResult,
          preparationError: describeRequestError(error, '标签准备暂未完成，请稍后重试。').message,
        }
        savePreparation(intent)
        return
      }
      const notAccepted =
        !intent.receipt && (error as { reportNotAccepted?: boolean })?.reportNotAccepted === true
      if (notAccepted) releasePreparation(intent)
      if (await options.recoverLifecycleAction(error)) return
      if (!isCurrent()) return
      intent.status = 'error'
      intent.result = {
        status: 'error',
        notAccepted,
        title: notAccepted
          ? '报工未提交'
          : intent.receipt
            ? '报工已受理，待核验'
            : '报工结果待核实',
        description: describeRequestError(error, '请检查网络后重试。').message,
        receipt: intent.receipt ?? undefined,
      }
      if (!notAccepted) savePreparation(intent)
    } finally {
      inFlight.delete(intent.owner)
      slotRevision.value += 1
    }
  }

  return { currentIntent, result, submitting, conflictingPreparation, deleteCurrentIntent, submit }
}
