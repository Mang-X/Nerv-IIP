import type { BusinessConsoleMesWorkOrderItem } from '@nerv-iip/api-client'
import type { EntityPickerOption } from '@nerv-iip/ui'
import type { useMesTelemetryProductionReportCandidates } from '@/composables/mes'
import { useMesWorkOrders } from '@/composables/mes'
import { useMesKeywordFilter } from '@/composables/mes/useMesKeywordFilter'
import {
  buildOperationTaskPickerOptions,
  buildWorkOrderPickerOptions,
} from '@/composables/mes/workOrderPickerOptions'
import {
  labelFor,
  REPORT_SUSPENSION_REASON_LABELS,
  TELEMETRY_CANDIDATE_STATUS_LABELS,
} from '@/data/businessLabels'
import { computed, ref, shallowRef, watch } from 'vue'

export function useTelemetryReportCandidates(
  candidateQueue: ReturnType<typeof useMesTelemetryProductionReportCandidates>,
) {
  type TelemetryCandidate = (typeof candidateQueue.candidates.value)[number]
  /** 候选当前处境：优先说明为什么挂起，没挂起就说它在队列里的状态。 */
  function candidateStateText(candidate: TelemetryCandidate) {
    return candidate.suspensionReason
      ? labelFor(REPORT_SUSPENSION_REASON_LABELS, candidate.suspensionReason)
      : labelFor(TELEMETRY_CANDIDATE_STATUS_LABELS, candidate.status) || '未标注状态'
  }
  const candidateWorkOrderId = ref('')
  const candidateOperationTaskId = ref('')
  const dismissalReason = ref('')
  const selectedCandidateId = ref<string | null>(null)

  // 转正要挑工单和它下面的工序任务。工单是持续新增的大目录，走服务端搜索（同追溯查询页）；
  // 工序任务取已选工单自带的工序，不另起一份目录。
  const workOrderCatalog = useMesWorkOrders({ initialTake: 50 })
  const { keyword: workOrderSearch } = useMesKeywordFilter(workOrderCatalog.filters)
  const candidateWorkOrderOptions = computed<EntityPickerOption[]>(() =>
    buildWorkOrderPickerOptions(workOrderCatalog.workOrders.value, candidateWorkOrderId.value),
  )
  // 工序取自已选工单那一行。工单目录走服务端搜索，改搜别的单号后已选工单会滑出结果集，
  // 所以记住最后一次在结果里见到的那一行，别让已选工序凭空变成「未选择」。
  const candidateWorkOrder = shallowRef<BusinessConsoleMesWorkOrderItem>()
  watch(
    [() => workOrderCatalog.workOrders.value, candidateWorkOrderId],
    ([orders, workOrderId]) => {
      const found = orders.find((order) => order.workOrderId?.trim() === workOrderId)
      if (found) candidateWorkOrder.value = found
      else if (candidateWorkOrder.value?.workOrderId?.trim() !== workOrderId)
        candidateWorkOrder.value = undefined
    },
    { immediate: true },
  )
  const candidateOperationTaskOptions = computed<EntityPickerOption[]>(() =>
    buildOperationTaskPickerOptions(candidateWorkOrder.value),
  )
  // 换了工单，上一张工单的工序任务就不再成立，跟着清掉。
  const candidateWorkOrderModel = computed({
    get: () => candidateWorkOrderId.value,
    set: (value: string) => {
      if (value === candidateWorkOrderId.value) return
      candidateWorkOrderId.value = value
      candidateOperationTaskId.value = ''
    },
  })

  function resetCandidateAction(candidate?: TelemetryCandidate) {
    // 候选自带的工单 / 工序任务就是默认选中，操作员只在需要改派时才动。
    candidateWorkOrderId.value = candidate?.workOrderId?.trim() ?? ''
    candidateOperationTaskId.value = candidate?.operationTaskId?.trim() ?? ''
    dismissalReason.value = ''
    // 选择器一页装不下整个工单目录：用候选自带的工单号当搜索词，让服务端把它和它的工序捞回来。
    workOrderSearch.value = candidateWorkOrderId.value
  }
  function toggleCandidate(candidate: TelemetryCandidate) {
    const opening = selectedCandidateId.value !== candidate.candidateId
    resetCandidateAction(opening ? candidate : undefined)
    selectedCandidateId.value = opening ? (candidate.candidateId ?? null) : null
  }

  async function promoteCandidate(candidateId?: string) {
    if (!candidateId) return
    const workOrderId = candidateWorkOrderId.value
    const operationTaskId = candidateOperationTaskId.value
    if (!workOrderId || !operationTaskId) return
    await candidateQueue.promote(candidateId, workOrderId, operationTaskId)
    selectedCandidateId.value = null
    resetCandidateAction()
  }
  async function dismissCandidate(candidateId?: string) {
    if (!candidateId || !dismissalReason.value.trim()) return
    await candidateQueue.dismiss(candidateId, dismissalReason.value.trim())
    selectedCandidateId.value = null
    resetCandidateAction()
  }

  return {
    candidateWorkOrderId,
    candidateStateText,
    candidateOperationTaskId,
    dismissalReason,
    selectedCandidateId,
    workOrderCatalog,
    workOrderSearch,
    candidateWorkOrderOptions,
    candidateOperationTaskOptions,
    candidateWorkOrderModel,
    toggleCandidate,
    promoteCandidate,
    dismissCandidate,
  }
}
