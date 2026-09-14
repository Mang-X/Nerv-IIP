import { afterEach, beforeEach, expect, it, vi } from 'vitest'
import { computed, effectScope, nextTick, reactive, ref } from 'vue'
import { clearPendingBusinessIntent } from '@nerv-iip/business-core'
import { useMesReportSubmission } from './useMesReportSubmission'

const scopes: ReturnType<typeof effectScope>[] = []
beforeEach(() => {
  for (const entry of JSON.parse(
    sessionStorage.getItem('nerv-iip.pending-business-intents.v1') ?? '[]',
  ))
    clearPendingBusinessIntent(entry)
  sessionStorage.clear()
})
afterEach(() => {
  for (const scope of scopes.splice(0)) scope.stop()
})

function harness(
  recordReport = vi.fn(async () => ({
    success: true,
    data: { reportNo: 'RPT-A', productionReportId: 'report-a', printingPreparationPending: true },
  })),
) {
  const task = ref({
    workOrderId: 'WO-A',
    operationTaskId: 'OP-A',
    status: 'inProgress' as const,
    allowedActions: ['report'],
  })
  const context = ref({
    principalId: 'p1',
    organizationId: 'org1',
    environmentId: 'env1',
    scopeKind: 'work-center',
    scopeId: 'wc1',
    generation: 1,
  })
  const generation = ref(1)
  const flow = reactive({
    workOrderId: 'WO-A',
    operationTaskId: 'OP-A',
    quantityEntered: true,
    recorded: false,
  })
  const verifiedPair = computed(() => ({
    workOrderId: task.value.workOrderId,
    operationTaskId: task.value.operationTaskId,
  }))
  const scope = effectScope()
  scopes.push(scope)
  const submission = scope.run(() =>
    useMesReportSubmission({
      pair: computed(() =>
        task.value.allowedActions.includes('report') ? verifiedPair.value : null,
      ),
      recoveryPair: verifiedPair,
      selectedTask: computed(() => task.value),
      context: computed(() => context.value),
      contextGeneration: generation,
      flowContext: flow,
      scanGuarded: ref(false),
      reportScopeReady: computed(() => true),
      quantityValid: computed(() => true),
      serialValid: computed(() => true),
      serialRequired: computed(() => true),
      labelTemplateId: ref('tpl-1'),
      invalidMaterialLots: computed(() => false),
      invalidScrapReasonCode: computed(() => false),
      goodQuantity: ref(2),
      scrapQuantity: ref(0),
      reworkQuantity: ref(0),
      scrapReasonCode: ref(''),
      consumedMaterialLots: computed(() => []),
      completesOperation: ref(false),
      recordReport,
      confirmReport: async () => ({}),
      recoverLifecycleAction: async () => false,
    }),
  )!
  async function select(workOrderId: string, operationTaskId: string) {
    task.value = { ...task.value, workOrderId, operationTaskId }
    flow.workOrderId = workOrderId
    flow.operationTaskId = operationTaskId
    await nextTick()
  }
  return { submission, select, recordReport, context, generation }
}

it('keeps A as the occupied owner and rejects a new B write before it reaches recordReport', async () => {
  const h = harness()
  await h.submission.submit()
  await h.select('WO-B', 'OP-B')
  await h.submission.submit()
  expect(h.recordReport).toHaveBeenCalledTimes(1)
  await h.select('WO-A', 'OP-A')
  expect(h.submission.result.value?.receipt?.reportNo).toBe('RPT-A')
})

it('holds the slot while A is in flight and ignores its late response after context changes', async () => {
  let resolve!: (value: {
    success: boolean
    data: { reportNo: string; productionReportId: string; printingPreparationPending: boolean }
  }) => void
  const record = vi.fn(
    () =>
      new Promise<{
        success: boolean
        data: { reportNo: string; productionReportId: string; printingPreparationPending: boolean }
      }>((done) => {
        resolve = done
      }),
  )
  const h = harness(record)
  const request = h.submission.submit()
  await h.select('WO-B', 'OP-B')
  void h.submission.submit()
  expect(record).toHaveBeenCalledTimes(1)
  h.generation.value = 2
  h.context.value = { ...h.context.value, scopeId: 'wc2', generation: 2 }
  await nextTick()
  resolve({
    success: true,
    data: { reportNo: 'RPT-LATE', productionReportId: 'late', printingPreparationPending: false },
  })
  await request
  expect(h.submission.result.value).toBeNull()
  h.generation.value = 3
  h.context.value = { ...h.context.value, scopeId: 'wc1', generation: 3 }
  await h.select('WO-A', 'OP-A')
  expect(h.submission.result.value?.status).toBe('error')
  expect(h.submission.result.value?.receipt).toBeUndefined()
})
