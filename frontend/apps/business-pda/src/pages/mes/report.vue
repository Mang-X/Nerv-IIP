<script setup lang="ts">
import { describeRequestError } from '@/api/request-timeout'
import type {
  BusinessConsoleMesOperationTaskRow,
  BusinessConsoleMesWorkOrderItem,
} from '@nerv-iip/api-client'
import {
  operationTaskStatusLabel,
  productionReportFlow,
  statusActionGate,
  type ReportCtx,
  workOrderSubtitle,
  workOrderTitle,
} from '@nerv-iip/business-core'
import {
  NvAppShellMobile,
  NvBottomSheet,
  NvListRow,
  NvMobileResult,
  NvMobileButton,
  NvMobileInput,
  NvMobileToast,
  NvScanBar,
} from '@nerv-iip/ui-mobile'
import { computed, reactive, ref, watch } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import {
  useMesExactOperationTask,
  useMesProductionMaterialLots,
  useMesProductionReports,
  useMesScrapReasonCodes,
  useMesTelemetryProductionReportCandidates,
  useMesWorkOrderDetail,
  useMesWorkOrders,
} from '@/composables/useBusinessMes'
import RetryableListError from '@/components/RetryableListError.vue'
import MesWorkScopeFilter from '@/components/mes/MesWorkScopeFilter.vue'
import { useLifecycleActionRecovery } from '@/composables/lifecycleActionRecovery'
import ListScopeMeta from '@/components/ListScopeMeta.vue'
import { makeIdempotencyKey } from '@/composables/makeIdempotencyKey'
import { useMesReportIdentity } from '@/composables/useMesReportIdentity'

definePage({
  meta: {
    requiresAuth: true,
    title: '报工',
  },
})

type WorkOrder = BusinessConsoleMesWorkOrderItem
type Task = BusinessConsoleMesOperationTaskRow

const router = useRouter()
const route = useRoute()
const routeWorkOrderId = computed(() => {
  const value = route.query.workOrderId
  return typeof value === 'string' ? value.trim() : ''
})

const {
  filters: workOrderFilters,
  workOrders,
  total: workOrderTotal,
  pending: workOrdersPending,
  error: workOrdersError,
  refresh: refreshWorkOrders,
  lastUpdatedAt: workOrdersLastUpdatedAt,
  hasSuccessfulResponse: workOrdersHasSuccessfulResponse,
  hasFailedResponse: workOrdersHasFailedResponse,
  workOrderReadScope: workOrderListReadScope,
  workOrderReadScopeMessage: workOrderListReadScopeMessage,
  workOrderReadScopeReady: workOrderListReadScopeReady,
} = useMesWorkOrders()

const {
  workOrder: workOrderDetail,
  pending: workOrderDetailPending,
  error: workOrderDetailError,
  refresh: refreshWorkOrderDetail,
  lastUpdatedAt: workOrderDetailLastUpdatedAt,
  hasSuccessfulResponse: workOrderDetailHasSuccessfulResponse,
  hasFailedResponse: workOrderDetailHasFailedResponse,
  workOrderReadScope: workOrderDetailReadScope,
  workOrderReadScopeMessage: workOrderDetailReadScopeMessage,
} = useMesWorkOrderDetail(routeWorkOrderId)
const routeOperationTaskId = computed(() => {
  const value = route.query.operationTaskId
  return typeof value === 'string' ? value.trim() : ''
})
const {
  task: exactOperationTask,
  pending: exactOperationTaskPending,
  error: exactOperationTaskError,
  refresh: refreshExactOperationTask,
  reportingReadScopeMessage: exactOperationTaskScopeMessage,
  reportingReadScopeReady: exactOperationTaskScopeReady,
} = useMesExactOperationTask(routeWorkOrderId, routeOperationTaskId, workOrderDetail)

const {
  selectedWorkOrder,
  selectedTask,
  visibleOperationTasks,
  pair,
  routeIssue,
  chooseWorkOrder: bindWorkOrder,
  chooseTask: bindTask,
  clearTask,
  clearIdentity,
} = useMesReportIdentity({
  workOrderDetail,
  workOrderDetailPending,
  workOrderDetailError,
  exactOperationTask,
  exactOperationTaskPending,
  exactOperationTaskError,
  exactOperationTaskScopeReady,
  exactOperationTaskScopeMessage,
})

const { recordReport, reportScopeMessage, reportScopePending, reportScopeReady } =
  useMesProductionReports()
const productionMaterialLots = useMesProductionMaterialLots(() =>
  pair.value
    ? { workOrderId: pair.value.workOrderId, operationTaskId: pair.value.operationTaskId }
    : null,
)
const {
  materialsReadPermission,
  materialLotsPending,
  materialLotsError,
  availableMaterialLots,
  refreshMaterialLots,
} = productionMaterialLots
const telemetryQueue = useMesTelemetryProductionReportCandidates()
const telemetryCandidateId = ref<string | null>(null)
const telemetryWorkOrderId = ref('')
const telemetryOperationTaskId = ref('')
const telemetryDismissReason = ref('')
function resetTelemetryAction() {
  telemetryWorkOrderId.value = ''
  telemetryOperationTaskId.value = ''
  telemetryDismissReason.value = ''
}
function toggleTelemetryCandidate(candidateId?: string) {
  resetTelemetryAction()
  telemetryCandidateId.value =
    telemetryCandidateId.value === candidateId ? null : (candidateId ?? null)
}

async function promoteTelemetryCandidate(candidate: {
  candidateId?: string
  workOrderId?: string | null
  operationTaskId?: string | null
}) {
  if (!candidate.candidateId) return
  const workOrderId = telemetryWorkOrderId.value.trim() || candidate.workOrderId?.trim()
  const operationTaskId = telemetryOperationTaskId.value.trim() || candidate.operationTaskId?.trim()
  if (!workOrderId || !operationTaskId) return
  await telemetryQueue.promote(candidate.candidateId, workOrderId, operationTaskId)
  telemetryCandidateId.value = null
  resetTelemetryAction()
}
async function dismissTelemetryCandidate(candidateId?: string) {
  if (!candidateId || !telemetryDismissReason.value.trim()) return
  await telemetryQueue.dismiss(candidateId, telemetryDismissReason.value.trim())
  telemetryCandidateId.value = null
  resetTelemetryAction()
}

// --- 流程上下文（productionReportFlow 驱动当前步/进度）---
const ctx = reactive<ReportCtx>({
  workOrderId: undefined,
  operationTaskId: undefined,
  quantityEntered: false,
  recorded: false,
})

const currentStep = computed(() => productionReportFlow.currentStep(ctx).id)
const progress = computed(() => productionReportFlow.progress(ctx))

// --- 数量录入 ---
const goodQuantity = ref(0)
const scrapQuantity = ref(0)
const reworkQuantity = ref(0)
const scrapReasonCode = ref('')
const completesOperation = ref(false)
const scrapReasonCodesState = useMesScrapReasonCodes(() => scrapQuantity.value > 0)
const {
  qualityInspectionRecordsReadPermission,
  scrapReasonCodesPending,
  scrapReasonCodesError,
  scrapReasonCodes,
  refreshScrapReasonCodes,
} = scrapReasonCodesState

const materialSelections = reactive(
  new Map<string, { selected: boolean; consumedQuantity: string }>(),
)
watch(
  productionMaterialLots.availableMaterialLots,
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
const consumedMaterialLots = computed(() =>
  productionMaterialLots.availableMaterialLots.value.flatMap((row) => {
    const selection = materialSelections.get(row.requestId)
    const materialLotId = row.materialLotId?.trim()
    if (!selection?.selected || !materialLotId) return []
    return [
      {
        materialId: row.materialId,
        materialLotId,
        consumedQuantity: Number(selection.consumedQuantity),
        materialIssueRequestNo: row.requestId,
      },
    ]
  }),
)
const invalidMaterialLots = computed(
  () =>
    (scrapQuantity.value > 0 && consumedMaterialLots.value.length === 0) ||
    productionMaterialLots.availableMaterialLots.value.some((row) => {
      const selection = materialSelections.get(row.requestId)
      if (!selection?.selected) return false
      const quantity = Number(selection.consumedQuantity)
      return (
        !Number.isFinite(quantity) ||
        quantity <= 0 ||
        quantity > row.receivedQuantity - row.consumedQuantity
      )
    }),
)
const materialValidationMessage = computed(() =>
  scrapQuantity.value > 0 && consumedMaterialLots.value.length === 0
    ? '报废报工至少选择一个已收料批次。'
    : invalidMaterialLots.value
      ? '耗料数量必须大于 0，且不能超过该批次可用数量。'
      : '',
)
const invalidScrapReasonCode = computed(() => {
  if (scrapQuantity.value <= 0) return false
  if (!qualityInspectionRecordsReadPermission.value) return true
  if (scrapReasonCodesPending.value || scrapReasonCodesError.value) return true
  return (
    !scrapReasonCode.value.trim() ||
    !scrapReasonCodes.value.some(
      (row) => row.reasonCode?.trim() === scrapReasonCode.value.trim() && row.enabled !== false,
    )
  )
})
const scrapReasonValidationMessage = computed(() => {
  if (scrapQuantity.value <= 0) return ''
  if (!qualityInspectionRecordsReadPermission.value) {
    return '当前账号没有质量原因码读取权限，无法提交报废报工。'
  }
  if (scrapReasonCodesPending.value) return '正在读取报废原因码，请稍后重试。'
  if (scrapReasonCodesError.value) return '报废原因码读取失败，请刷新后重试。'
  if (scrapReasonCodes.value.length === 0) return '当前没有可用的报废原因码。'
  if (!scrapReasonCode.value.trim()) return '报废数量大于 0 时必须选择报废原因码。'
  return '所选报废原因码已失效，请重新选择。'
})
function materialSelected(requestId: string | undefined) {
  return requestId ? (materialSelections.get(requestId)?.selected ?? false) : false
}
function materialQuantity(requestId: string | undefined) {
  return requestId ? (materialSelections.get(requestId)?.consumedQuantity ?? '') : ''
}
function setMaterialSelected(requestId: string | undefined, selected: boolean | undefined) {
  if (!requestId) return
  const current = materialSelections.get(requestId) ?? { selected: false, consumedQuantity: '' }
  current.selected = selected ?? false
  materialSelections.set(requestId, current)
}
function setMaterialQuantity(requestId: string | undefined, quantity: string | undefined) {
  if (!requestId) return
  const current = materialSelections.get(requestId) ?? { selected: false, consumedQuantity: '' }
  current.consumedQuantity = quantity ?? ''
  materialSelections.set(requestId, current)
}
function materialRemaining(row: { receivedQuantity?: number; consumedQuantity?: number }) {
  return new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 3 }).format(
    (row.receivedQuantity ?? 0) - (row.consumedQuantity ?? 0),
  )
}

const quantityValid = computed(
  () =>
    goodQuantity.value >= 0 &&
    scrapQuantity.value >= 0 &&
    reworkQuantity.value >= 0 &&
    goodQuantity.value + scrapQuantity.value + reworkQuantity.value > 0,
)

// 录数量面板：选中工序后打开
const sheetOpen = computed({
  get: () => selectedTask.value !== null && result.value === null,
  set: (open) => {
    if (!open) closeSheet()
  },
})

// --- 结果反馈 ---
type ResultState = { status: 'success' | 'error'; title: string; description?: string }
interface ReportIntent {
  attempt: symbol
  workOrderId: string
  operationTaskId: string
  intentKey: string
  payload: {
    goodQuantity: number
    scrapQuantity: number
    reworkQuantity: number
    consumedMaterialLots: Array<{
      materialId: string
      materialLotId: string
      consumedQuantity: number
      materialIssueRequestNo: string
    }>
    scrapReasonCode: string | undefined
    completesOperation: boolean
  }
  status: 'pending' | 'success' | 'error'
  result: ResultState | null
}
const intents = reactive(new Map<string, ReportIntent>())
const pairKey = computed(() =>
  pair.value ? `${pair.value.workOrderId}\u0000${pair.value.operationTaskId}` : '',
)
const currentIntent = computed(() => (pairKey.value ? intents.get(pairKey.value) : undefined))
const result = computed(() => currentIntent.value?.result ?? null)
const submitting = computed(() => currentIntent.value?.status === 'pending')
const canCompleteSelectedTask = computed(
  () =>
    selectedTask.value !== null &&
    statusActionGate({
      domain: 'mes-operation-task',
      action: 'report-complete',
      facts: { status: selectedTask.value.status },
    }).executable,
)

watch(
  [() => selectedWorkOrder.value?.workOrderId, () => selectedTask.value?.operationTaskId],
  ([workOrderId, operationTaskId], previousIdentity) => {
    const [previousWorkOrderId, previousOperationTaskId] = previousIdentity ?? []
    ctx.workOrderId = workOrderId
    ctx.operationTaskId = operationTaskId
    if (workOrderId !== previousWorkOrderId || operationTaskId !== previousOperationTaskId) {
      goodQuantity.value = 0
      scrapQuantity.value = 0
      reworkQuantity.value = 0
      scrapReasonCode.value = ''
      completesOperation.value = false
      materialSelections.clear()
      ctx.quantityEntered = false
      ctx.recorded = currentIntent.value?.status === 'success'
    }
  },
  { immediate: true },
)
watch(
  () => currentIntent.value?.status,
  (status) => {
    ctx.recorded = status === 'success'
  },
)
watch(canCompleteSelectedTask, (canComplete) => {
  if (!canComplete) completesOperation.value = false
})

// ScanBar 仅在选工单步活跃；录数量/结果时不抢焦点
const scanActive = computed(
  () =>
    currentStep.value === 'selectWorkOrder' && result.value === null && selectedTask.value === null,
)

// 可读中文状态标签 + 工单标题/副标题来自 @nerv-iip/business-core。
const taskStatusLabel = operationTaskStatusLabel

function taskTitle(task: Task) {
  const seq = task.operationSequence === undefined ? '' : `工序 ${task.operationSequence}`
  const wo = task.workOrderId ?? '无工单'
  return seq ? `${wo} · ${seq}` : wo
}
function taskSubtitle(task: Task) {
  const parts = [taskStatusLabel(task.status)]
  if (task.workCenterId) parts.push(`工作中心 ${task.workCenterId}`)
  return parts.join(' · ')
}

const workScopeKindLabels: Record<string, string> = {
  self: '本人',
  team: '班组',
  'work-center': '工作中心',
  workshop: '车间',
  organization: '组织',
}
function workScopeLabel(scope: { kind: string; id: string; displayName?: string } | undefined) {
  if (!scope) return '当前主体授权工单范围未就绪'
  const kind = workScopeKindLabels[scope.kind] ?? scope.kind
  return `当前主体授权工单范围 · ${scope.displayName || scope.id}（${kind}）`
}
const workOrderScope = computed(() => workScopeLabel(workOrderListReadScope.value))
const workOrderEmptyExplanation = computed(() =>
  workOrderListReadScopeReady.value
    ? '当前主体授权工单范围内暂无生产工单。'
    : workOrderListReadScopeMessage.value || '尚未取得当前主体的授权工单范围，未发起查询。',
)
const showWorkOrdersEmpty = computed(
  () =>
    !workOrdersPending.value &&
    !workOrdersError.value &&
    !workOrdersHasFailedResponse.value &&
    workOrdersHasSuccessfulResponse.value &&
    workOrders.value.length === 0,
)
const operationTaskScope = computed(() => workScopeLabel(workOrderDetailReadScope.value))
const operationTaskEmptyExplanation = computed(
  () =>
    workOrderDetailReadScopeMessage.value ||
    '当前空态只代表所选授权工单返回的工序集合为空；接口未提供工序总数。',
)

// --- 步骤操作 ---
function chooseWorkOrder(wo: WorkOrder) {
  void bindWorkOrder(wo)
}

function chooseTask(task: Task) {
  void bindTask(task)
}

function closeSheet() {
  void clearTask()
}

// 返回上一步
function backToWorkOrders() {
  void clearIdentity()
}

function resetReportIntent() {
  if (pairKey.value) intents.delete(pairKey.value)
  goodQuantity.value = 0
  scrapQuantity.value = 0
  reworkQuantity.value = 0
  scrapReasonCode.value = ''
  materialSelections.clear()
  completesOperation.value = false
  ctx.quantityEntered = false
  ctx.recorded = false
  void clearIdentity()
}

const lifecycleRecovery = useLifecycleActionRecovery({
  reset: resetReportIntent,
  refresh: () =>
    Promise.all([refreshWorkOrders(), refreshWorkOrderDetail(), refreshExactOperationTask()]),
})

async function submit() {
  if (!reportScopeReady.value) return
  const identity = pair.value
  const task = selectedTask.value
  const workOrderId = identity?.workOrderId
  const operationTaskId = identity?.operationTaskId
  if (
    !workOrderId ||
    ctx.workOrderId !== workOrderId ||
    !operationTaskId ||
    ctx.operationTaskId !== operationTaskId ||
    !task ||
    task.workOrderId !== workOrderId
  ) {
    return
  }
  const key = `${workOrderId}\u0000${operationTaskId}`
  let intent = intents.get(key)
  if (intent?.status === 'pending' || intent?.status === 'success') return
  if (!intent) {
    if (!quantityValid.value || invalidMaterialLots.value || invalidScrapReasonCode.value) return
    intent = {
      attempt: Symbol('mes-report-attempt'),
      workOrderId,
      operationTaskId,
      intentKey: makeIdempotencyKey(),
      payload: {
        goodQuantity: goodQuantity.value,
        scrapQuantity: scrapQuantity.value,
        reworkQuantity: reworkQuantity.value,
        scrapReasonCode: scrapQuantity.value > 0 ? scrapReasonCode.value.trim() : undefined,
        consumedMaterialLots: consumedMaterialLots.value,
        completesOperation: completesOperation.value,
      },
      status: 'pending',
      result: null,
    }
    intents.set(key, intent)
    intent = intents.get(key)!
  } else {
    intent.attempt = Symbol('mes-report-retry')
    intent.status = 'pending'
    intent.result = null
  }
  ctx.quantityEntered = true
  if (!intent) return
  const attempt = intent.attempt
  try {
    const receiptEnvelope = await recordReport({
      workOrderId,
      operationTaskId,
      ...intent.payload,
      idempotencyKey: intent.intentKey,
    })
    if (intent.attempt !== attempt) return
    if (!receiptEnvelope?.success) {
      throw new Error(receiptEnvelope?.message?.trim() || '报工回执无效，请重试。')
    }
    const receipt = receiptEnvelope.data
    const reportNo = receipt?.reportNo?.trim()
    const productionReportId = receipt?.productionReportId?.trim()
    if (!reportNo || !productionReportId) {
      throw new Error('报工回执缺少真实报工单号或回执 ID，已阻止成功确认。')
    }
    const description = [`${workOrderId} · ${operationTaskId}`]
    description.push(`报工单号 ${reportNo}`)
    description.push(`回执 ID ${productionReportId}`)
    if (intent.payload.completesOperation) description.push('本工序已标记完工')
    intent.status = 'success'
    intent.result = {
      status: 'success',
      title: '报工成功',
      description: description.join('；'),
    }
  } catch (e) {
    if (intent.attempt !== attempt) return
    if (await lifecycleRecovery.handle(e)) return
    intent.status = 'error'
    intent.result = {
      status: 'error',
      title: '报工失败',
      description: describeRequestError(e, '请检查网络后重试。').message,
    }
  }
}

function continueReport() {
  if (pairKey.value) intents.delete(pairKey.value)
  backToWorkOrders()
}

function goBack() {
  router.push('/').catch(() => {})
}

function onScanWorkOrder(value: string) {
  workOrderFilters.keyword = value
}
</script>

<template>
  <NvAppShellMobile>
    <template #header>
      <div class="flex items-center gap-3 px-4 py-3">
        <button
          type="button"
          aria-label="返回"
          class="text-sm text-muted-foreground"
          @click="router.push('/').catch(() => {})"
        >
          返回
        </button>
        <h1 class="text-lg font-semibold text-foreground">报工</h1>
        <span class="ml-auto text-xs text-muted-foreground">
          第
          {{ progress.completed + 1 > progress.total ? progress.total : progress.completed + 1 }}/{{
            progress.total
          }}
          步
        </span>
      </div>
    </template>

    <!-- 报工结果反馈 -->
    <NvMobileResult
      v-if="result"
      :status="result.status"
      :title="result.title"
      :description="result.description"
    >
      <template #actions>
        <button
          v-if="result.status === 'success'"
          type="button"
          data-testid="continue-report"
          class="min-h-touch w-full rounded-lg bg-primary text-base font-medium text-primary-foreground"
          @click="continueReport"
        >
          继续报工
        </button>
        <button
          v-else
          type="button"
          data-testid="retry-report"
          class="min-h-touch w-full rounded-lg bg-primary text-base font-medium text-primary-foreground"
          @click="submit"
        >
          重试
        </button>
        <button
          type="button"
          class="min-h-touch w-full rounded-lg border border-border bg-card text-base font-medium text-foreground"
          @click="goBack"
        >
          返回
        </button>
      </template>
    </NvMobileResult>

    <div v-else class="space-y-4 p-4">
      <MesWorkScopeFilter permission-code="business.mes.work-orders.read" />
      <p
        v-if="reportScopeMessage"
        data-testid="report-scope-message"
        class="rounded-lg border border-destructive/40 px-4 py-3 text-sm text-destructive"
        role="alert"
      >
        {{ reportScopeMessage }}
      </p>
      <p
        v-if="routeIssue"
        role="alert"
        data-testid="report-route-issue"
        class="rounded-lg border border-destructive/40 bg-destructive/5 px-4 py-3 text-sm text-destructive"
      >
        {{ routeIssue }}
      </p>
      <section
        v-if="
          currentStep === 'selectWorkOrder' &&
          routeWorkOrderId &&
          (workOrderDetailError || workOrderDetailHasFailedResponse)
        "
        class="space-y-2"
      >
        <ListScopeMeta
          :scope="operationTaskScope"
          source="生产工单详情服务（服务端按当前主体与所选授权工单范围校验）"
          :loaded="0"
          :total="0"
          :updated-at="workOrderDetailLastUpdatedAt"
          failed
          failure-explanation="工单详情服务未成功返回，请重试。"
        />
        <RetryableListError
          :error="workOrderDetailError ?? '工单详情服务未成功返回'"
          :pending="workOrderDetailPending"
          fallback="加载工单详情失败，请重试。"
          test-id="work-order-detail-error"
          @retry="() => refreshWorkOrderDetail()"
        />
      </section>
      <section
        v-if="telemetryQueue.candidates.value.length"
        class="space-y-3 rounded-lg border border-warning/40 bg-warning/5 p-3"
      >
        <div class="flex items-center justify-between">
          <h2 class="font-semibold">遥测待确认</h2>
          <span class="text-xs text-muted-foreground">{{ telemetryQueue.total.value }} 条</span>
        </div>
        <div
          v-for="candidate in telemetryQueue.candidates.value"
          :key="candidate.candidateId"
          class="rounded-lg border border-border bg-card p-3"
        >
          <NvMobileButton
            variant="text"
            block
            class="h-auto justify-start p-0 text-left"
            @click="toggleTelemetryCandidate(candidate.candidateId)"
          >
            <span class="block font-medium"
              >{{ candidate.deviceAssetId }} · {{ candidate.goodQuantity }} 件</span
            ><span class="block text-xs text-muted-foreground">{{
              candidate.suspensionReason ?? candidate.status
            }}</span>
          </NvMobileButton>
          <div v-if="telemetryCandidateId === candidate.candidateId" class="mt-3 space-y-2">
            <NvMobileInput
              v-model="telemetryWorkOrderId"
              :placeholder="candidate.workOrderId ?? '真实工单号'"
            />
            <NvMobileInput
              v-model="telemetryOperationTaskId"
              :placeholder="candidate.operationTaskId ?? '真实工序任务号'"
            />
            <NvMobileInput v-model="telemetryDismissReason" placeholder="忽略原因（忽略时必填）" />
            <div class="grid grid-cols-2 gap-2">
              <NvMobileButton variant="primary" @click="promoteTelemetryCandidate(candidate)"
                >确认转正</NvMobileButton
              ><NvMobileButton
                variant="outline"
                :disabled="!telemetryDismissReason.trim()"
                @click="dismissTelemetryCandidate(candidate.candidateId)"
                >忽略</NvMobileButton
              >
            </div>
          </div>
        </div>
      </section>
      <!-- 步骤 1：选工单 -->
      <template v-if="currentStep === 'selectWorkOrder'">
        <NvScanBar placeholder="扫描工单号" :active="scanActive" @scan="onScanWorkOrder" />
        <p class="text-sm text-muted-foreground">选择报工的工单（共 {{ workOrderTotal }} 张）</p>
        <ListScopeMeta
          :scope="workOrderScope"
          source="生产工单服务（服务端按当前主体与所选授权工单范围过滤）"
          :loaded="workOrders.length"
          :total="workOrderTotal"
          :updated-at="workOrdersLastUpdatedAt"
          :failed="workOrdersHasFailedResponse || Boolean(workOrdersError)"
          failure-explanation="生产工单服务未成功返回，请刷新重试。"
          :empty="!workOrderListReadScopeReady || showWorkOrdersEmpty"
          :empty-explanation="workOrderEmptyExplanation"
        />
        <RetryableListError
          v-if="workOrdersError || workOrdersHasFailedResponse"
          :error="workOrdersError ?? '生产工单服务未成功返回'"
          :pending="workOrdersPending"
          fallback="加载工单失败，请下拉刷新或重试。"
          test-id="work-orders-error"
          @retry="() => refreshWorkOrders()"
        />
        <div
          v-else-if="showWorkOrdersEmpty"
          class="rounded-lg border border-dashed border-border bg-card px-4 py-8 text-center text-sm text-muted-foreground"
        >
          暂无可报工的工单
        </div>
        <div v-else class="overflow-hidden rounded-lg border border-border">
          <NvListRow
            v-for="wo in workOrders"
            :key="wo.workOrderId"
            :title="workOrderTitle(wo)"
            :subtitle="workOrderSubtitle(wo)"
            @select="chooseWorkOrder(wo)"
          />
        </div>
      </template>

      <!-- 步骤 2+：已选工单，选工序 -->
      <template v-else>
        <div
          class="flex items-center justify-between rounded-lg border border-border bg-card px-4 py-3"
        >
          <div class="min-w-0">
            <p class="text-sm text-muted-foreground">当前工单</p>
            <p class="truncate text-base font-medium text-foreground">
              {{ selectedWorkOrder ? workOrderTitle(selectedWorkOrder) : '' }}
            </p>
          </div>
          <button
            type="button"
            data-testid="change-work-order"
            class="shrink-0 text-sm text-primary"
            @click="backToWorkOrders"
          >
            改选工单
          </button>
        </div>

        <ListScopeMeta
          :scope="operationTaskScope"
          source="生产工序服务（当前主体授权工单详情返回集合，接口未提供服务总数）"
          :loaded="visibleOperationTasks.length"
          :total="visibleOperationTasks.length"
          :updated-at="workOrderDetailLastUpdatedAt"
          :failed="workOrderDetailHasFailedResponse || Boolean(workOrderDetailError)"
          failure-explanation="工单详情服务未成功返回，请重试。"
          :empty="
            !workOrderDetailPending &&
            !workOrderDetailError &&
            workOrderDetailHasSuccessfulResponse &&
            selectedWorkOrder !== null &&
            visibleOperationTasks.length === 0
          "
          :empty-explanation="operationTaskEmptyExplanation"
        />
        <RetryableListError
          v-if="workOrderDetailError || workOrderDetailHasFailedResponse"
          :error="workOrderDetailError ?? '工单详情服务未成功返回'"
          :pending="workOrderDetailPending"
          fallback="加载工单详情失败，请重试。"
          test-id="work-order-detail-error"
          @retry="() => refreshWorkOrderDetail()"
        />
        <p class="text-sm text-muted-foreground">
          选择要报工的工序（共 {{ visibleOperationTasks.length }} 道）
        </p>
        <div
          v-if="
            !workOrderDetailPending &&
            !workOrderDetailError &&
            workOrderDetailHasSuccessfulResponse &&
            visibleOperationTasks.length === 0
          "
          class="rounded-lg border border-dashed border-border bg-card px-4 py-8 text-center text-sm text-muted-foreground"
        >
          该工单暂无工序
        </div>
        <div v-else class="overflow-hidden rounded-lg border border-border">
          <NvListRow
            v-for="task in visibleOperationTasks"
            :key="task.operationTaskId ?? `${task.workOrderId}-${task.operationSequence}`"
            :title="taskTitle(task)"
            :subtitle="taskSubtitle(task)"
            @select="chooseTask(task)"
          />
        </div>
      </template>
    </div>

    <!-- 步骤 3：录数量 -->
    <NvBottomSheet
      :open="sheetOpen"
      :title="selectedTask ? taskTitle(selectedTask) : ''"
      @update:open="sheetOpen = $event"
    >
      <div v-if="selectedTask" class="space-y-4 pb-2">
        <p class="text-sm text-muted-foreground">
          当前状态：{{ taskStatusLabel(selectedTask.status) }}
        </p>

        <label class="block space-y-1">
          <span class="text-sm font-medium text-foreground">良品数</span>
          <input
            v-model.number="goodQuantity"
            data-testid="good-quantity"
            type="number"
            inputmode="numeric"
            min="0"
            class="min-h-touch w-full rounded-lg border border-border bg-card px-3 text-base outline-none focus:border-primary"
          />
        </label>

        <label class="block space-y-1">
          <span class="text-sm font-medium text-foreground">次品数</span>
          <input
            v-model.number="scrapQuantity"
            data-testid="scrap-quantity"
            type="number"
            inputmode="numeric"
            min="0"
            class="min-h-touch w-full rounded-lg border border-border bg-card px-3 text-base outline-none focus:border-primary"
          />
        </label>

        <section
          v-if="scrapQuantity > 0"
          data-testid="scrap-reason-code-field"
          class="space-y-2 rounded-lg border border-border bg-card p-3"
        >
          <div class="flex items-center justify-between gap-3">
            <label for="scrap-reason-code" class="text-sm font-medium text-foreground">
              报废原因码 <span class="text-destructive">*</span>
            </label>
            <button
              type="button"
              class="text-sm text-primary"
              :disabled="scrapReasonCodesPending || submitting"
              @click="refreshScrapReasonCodes"
            >
              刷新
            </button>
          </div>
          <select
            id="scrap-reason-code"
            v-model="scrapReasonCode"
            data-testid="scrap-reason-code"
            class="min-h-touch w-full rounded-lg border border-border bg-background px-3 text-base outline-none focus:border-primary disabled:opacity-60"
            :disabled="
              !qualityInspectionRecordsReadPermission ||
              scrapReasonCodesPending ||
              !!scrapReasonCodesError ||
              submitting ||
              !reportScopeReady
            "
          >
            <option value="">请选择报废原因码</option>
            <option
              v-for="reason in scrapReasonCodes"
              :key="reason.reasonCode"
              :value="reason.reasonCode"
            >
              {{ reason.reasonCode }} · {{ reason.reasonName }}
            </option>
          </select>
          <p v-if="scrapReasonCodesError" class="text-sm text-destructive" role="alert">
            报废原因码读取失败，请刷新后重试。
          </p>
          <p v-else-if="scrapReasonCodesPending" class="text-sm text-muted-foreground">
            正在读取报废原因码…
          </p>
          <p
            v-else-if="!qualityInspectionRecordsReadPermission"
            class="text-sm text-muted-foreground"
          >
            当前账号没有质量原因码读取权限，报废报工已禁用。
          </p>
          <p v-else-if="scrapReasonValidationMessage" class="text-sm text-destructive" role="alert">
            {{ scrapReasonValidationMessage }}
          </p>
        </section>

        <label class="block space-y-1">
          <span class="text-sm font-medium text-foreground">返修数</span>
          <input
            v-model.number="reworkQuantity"
            data-testid="rework-quantity"
            type="number"
            inputmode="numeric"
            min="0"
            class="min-h-touch w-full rounded-lg border border-border bg-card px-3 text-base outline-none focus:border-primary"
          />
        </label>

        <label
          class="flex items-center justify-between gap-3 rounded-lg border border-border bg-card px-3 py-3"
        >
          <span class="text-sm font-medium text-foreground">完工本工序</span>
          <input
            v-model="completesOperation"
            data-testid="completes-operation"
            type="checkbox"
            :disabled="!canCompleteSelectedTask"
            class="size-5"
          />
        </label>

        <p v-if="!canCompleteSelectedTask" class="text-sm text-muted-foreground">
          当前工序可报数量；仅执行中的工序可同时完工。
        </p>

        <section
          v-if="materialsReadPermission"
          data-testid="production-material-lots"
          class="space-y-2 rounded-lg border border-border bg-card p-3"
        >
          <div class="flex items-center justify-between gap-3">
            <h3 class="text-sm font-medium text-foreground">耗料批次</h3>
            <button
              type="button"
              class="text-sm text-primary"
              :disabled="materialLotsPending || submitting"
              @click="refreshMaterialLots"
            >
              刷新
            </button>
          </div>
          <p v-if="materialLotsError" class="text-sm text-destructive">
            已收料批次读取失败，请刷新后重试。
          </p>
          <p v-else-if="materialLotsPending" class="text-sm text-muted-foreground">
            正在读取已收料批次…
          </p>
          <p v-else-if="availableMaterialLots.length === 0" class="text-sm text-muted-foreground">
            当前工序暂无可用已收料批次。
          </p>
          <div v-for="row in availableMaterialLots" :key="row.requestId" class="space-y-2">
            <label class="flex items-center gap-2 text-sm text-foreground">
              <input
                :checked="materialSelected(row.requestId)"
                :data-testid="`material-lot-${row.requestId}`"
                type="checkbox"
                class="size-5"
                :disabled="submitting"
                @change="
                  setMaterialSelected(row.requestId, ($event.target as HTMLInputElement).checked)
                "
              />
              <span>
                {{ row.materialId }} · {{ row.materialLotId }}
                <span class="text-muted-foreground">
                  （{{ row.operationTaskId ? '本工序' : '工单级' }}，可用
                  {{ materialRemaining(row) }} {{ row.uomCode }}）
                </span>
              </span>
            </label>
            <input
              :value="materialQuantity(row.requestId)"
              :data-testid="`material-quantity-${row.requestId}`"
              type="number"
              inputmode="decimal"
              min="0"
              step="any"
              placeholder="耗用数量"
              class="min-h-touch w-full rounded-lg border border-border bg-background px-3 text-base outline-none focus:border-primary disabled:opacity-60"
              :disabled="!materialSelected(row.requestId) || submitting"
              @input="setMaterialQuantity(row.requestId, ($event.target as HTMLInputElement).value)"
            />
          </div>
          <p v-if="materialValidationMessage" class="text-sm text-destructive" role="alert">
            {{ materialValidationMessage }}
          </p>
        </section>
        <p v-else data-testid="material-permission-message" class="text-sm text-muted-foreground">
          当前账号没有材料读取权限，耗料批次选择已禁用。
        </p>

        <p v-if="!quantityValid" class="text-sm text-muted-foreground">
          良品数、次品数与返修数须为非负数，且合计大于 0。
        </p>
        <p v-else-if="scrapReasonValidationMessage" class="text-sm text-destructive" role="alert">
          {{ scrapReasonValidationMessage }}
        </p>

        <button
          type="button"
          data-testid="submit-report"
          :disabled="
            !quantityValid ||
            invalidMaterialLots ||
            invalidScrapReasonCode ||
            submitting ||
            reportScopePending ||
            !reportScopeReady
          "
          class="min-h-touch w-full rounded-lg bg-primary text-base font-medium text-primary-foreground disabled:opacity-60"
          @click="submit"
        >
          提交报工
        </button>
        <button
          type="button"
          data-testid="change-operation"
          class="min-h-touch w-full rounded-lg border border-border bg-card text-base font-medium text-foreground"
          @click="closeSheet"
        >
          改选工序
        </button>
      </div>
    </NvBottomSheet>

    <NvMobileToast
      :show="lifecycleRecovery.toast.value.show"
      :message="lifecycleRecovery.toast.value.message"
      :type="lifecycleRecovery.toast.value.type"
      @update:show="lifecycleRecovery.setToastOpen"
    />
  </NvAppShellMobile>
</template>
