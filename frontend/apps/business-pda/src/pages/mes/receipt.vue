<script setup lang="ts">
import type {
  BusinessConsoleMesCreateReceiptResponse,
  BusinessConsoleMesReceiptRequestRow,
  BusinessConsoleMesWorkOrderItem,
} from '@nerv-iip/api-client'
import {
  finishedGoodsReceiptFlow,
  receiptPendingReason,
  receiptStatusLabel,
  workOrderSubtitle,
  workOrderTitle,
} from '@nerv-iip/business-core'
import {
  NvAppShellMobile,
  NvBottomSheet,
  NvListRow,
  NvMobileResult,
  NvNumberKeyboard,
} from '@nerv-iip/ui-mobile'
import { computed, ref, shallowRef, watch } from 'vue'
import { useRouter } from 'vue-router'
import { describeRequestError } from '@/api/request-timeout'
import { useAuthStore } from '@/stores/auth'
import { useMesReceipts, useMesWorkOrders } from '@/composables/mes'
import RetryableListError from '@/components/RetryableListError.vue'
import { makeIdempotencyKey } from '@/composables/shared/makeIdempotencyKey'
import MesScanPrevalidation from '@/components/mes/MesScanPrevalidation.vue'
import type { MesScanAccepted } from '@/composables/mes/useMesScanPrevalidation'
import { useMesScanGate } from '@/composables/mes/useMesScanGate'

definePage({
  meta: {
    requiresAuth: true,
    title: '完工入库',
  },
})

type Receipt = BusinessConsoleMesReceiptRequestRow
type WorkOrder = BusinessConsoleMesWorkOrderItem

const router = useRouter()
const auth = useAuthStore()
const canCreateReceipt = computed(() => {
  const permissions = auth.principal?.permissionCodes ?? []
  return (
    permissions.includes('business.mes.work-orders.read') &&
    permissions.includes('business.mes.receipts.read') &&
    permissions.includes('business.mes.receipts.manage')
  )
})

const {
  filters,
  receiptScopeIdentity,
  receipts,
  pending,
  error,
  hasSuccessfulResponse,
  hasFailedResponse,
  refresh,
  createReceipt,
  readProducedLots,
  readReceipt,
} = useMesReceipts()

const {
  filters: workOrderFilters,
  workOrders,
  total: workOrderTotal,
  pending: workOrdersPending,
  error: workOrdersError,
  refresh: refreshWorkOrders,
} = useMesWorkOrders()

// 可读中文状态标签 + 工单标题/副标题来自 @nerv-iip/business-core（不外显原始状态码 / GUID）。
// 完工入库申请用工单 + 物料组合作可读标题；不把 receiptRequestId（GUID）当标签暴露，
// 它仅作为列表 key。requestNo 若有则作为业务单号附在副标题里。
function receiptTitle(req: Receipt) {
  const wo = req.workOrderId ?? '无工单'
  return req.skuId ? `${wo} · 物料 ${req.skuId}` : wo
}
function receiptSubtitle(req: Receipt) {
  const parts = [receiptStatusLabel(req.receiptStatus)]
  if (req.quantity !== undefined) parts.push(`数量 ${req.quantity}`)
  if (req.unitCost !== undefined && req.unitCost !== null)
    parts.push(`成本 ${formatReceiptNumber(req.unitCost)}`)
  if (req.requestNo) parts.push(`单号 ${req.requestNo}`)
  return parts.join(' · ')
}

const scopeReady = computed(() => Boolean(filters.organizationId && filters.environmentId))
const scopedReceipts = computed(() => (scopeReady.value ? receipts.value : []))
const listFailure = computed(() =>
  error.value
    ? error.value
    : hasFailedResponse.value
      ? new Error('完工入库申请加载失败，请重试。')
      : null,
)

function formatReceiptNumber(value: number) {
  return new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 6 }).format(value)
}

type ProducedLot = Awaited<ReturnType<typeof readProducedLots>>[number]
type ResultState = {
  status: 'success' | 'error'
  title: string
  description?: string
  action?: 'retry' | 'correct' | 'read'
}

const creating = ref(false)
const selectedWorkOrder = ref<WorkOrder | null>(null)
const lots = shallowRef<ProducedLot[]>([])
const lotsPending = ref(false)
const lotsError = shallowRef<Error | null>(null)
const reloadLots = ref(0)
const selectedLotNo = ref('')
const selectedLot = computed(() =>
  lots.value.find((lot) => lot.producedLotNo === selectedLotNo.value),
)
const quantityText = ref('')
const quantity = computed(() => Number(quantityText.value))
const quantityKeyboardOpen = ref(false)
const uomCode = ref('')
const result = ref<ResultState | null>(null)
const submitting = ref(false)
const intent = shallowRef<{
  input: Parameters<typeof createReceipt>[0] & { workOrderId: string }
  requestedAtUtc: string
}>()
const accepted = shallowRef<BusinessConsoleMesCreateReceiptResponse>()

const scanGate = useMesScanGate()
const scanPending = scanGate.pending
const scanGuarded = scanGate.guarded
const scannedWorkOrderId = ref('')
const scannedOperationTaskId = ref('')

const quantityError = computed(() => {
  if (!Number.isFinite(quantity.value) || quantity.value <= 0) return '入库数量须大于 0。'
  if (!/^\d*(\.\d{0,6})?$/.test(quantityText.value)) return '入库数量最多保留 6 位小数。'
  if (selectedLot.value && quantity.value > selectedLot.value.remainingQuantity)
    return '入库数量不能超过该批次剩余可入库数量。'
})
const createValid = computed(() =>
  Boolean(
    scopeReady.value &&
    selectedWorkOrder.value?.workOrderId &&
    selectedWorkOrder.value.skuId &&
    selectedLot.value &&
    !quantityError.value &&
    uomCode.value.trim() &&
    uomCode.value.trim().length <= 30,
  ),
)
const progress = computed(() =>
  finishedGoodsReceiptFlow.progress({
    workOrderId: selectedWorkOrder.value?.workOrderId,
    skuId: selectedWorkOrder.value?.skuId,
    quantityEntered: createValid.value,
    created: result.value?.status === 'success',
  }),
)

const createSheetOpen = computed({
  get: () => creating.value && result.value === null,
  set: (open) => {
    if (!open && !submitting.value) closeCreate()
  },
})

watch(
  [receiptScopeIdentity, () => selectedWorkOrder.value?.workOrderId, reloadLots],
  async ([, workOrderId], _, onCleanup) => {
    lots.value = []
    lotsError.value = null
    lotsPending.value = false
    if (!workOrderId || !scopeReady.value) return
    const request = new AbortController()
    onCleanup(() => request.abort())
    lotsPending.value = true
    try {
      const rows = await readProducedLots(workOrderId, request.signal)
      if (request.signal.aborted) return
      lots.value = rows
      if (!rows.some((lot) => lot.producedLotNo === selectedLotNo.value)) selectedLotNo.value = ''
    } catch (error) {
      if (!request.signal.aborted) {
        lotsError.value = new Error(
          describeRequestError(error, '无法读取产出批次，请重试。').message,
        )
      }
    } finally {
      if (!request.signal.aborted) lotsPending.value = false
    }
  },
)

function changeWorkOrder() {
  selectedWorkOrder.value = null
  lots.value = []
  selectedLotNo.value = ''
  quantityText.value = ''
  uomCode.value = ''
}

function resetForm() {
  changeWorkOrder()
  quantityKeyboardOpen.value = false
  intent.value = undefined
  accepted.value = undefined
}

function openCreate() {
  if (!canCreateReceipt.value) return
  scanGate.clear('list')
  result.value = null
  resetForm()
  creating.value = true
}
function closeCreate() {
  creating.value = false
  resetForm()
}
watch(
  receiptScopeIdentity,
  () => {
    result.value = null
    submitting.value = false
    closeCreate()
  },
  { flush: 'sync' },
)

function chooseWorkOrder(wo: WorkOrder) {
  selectedWorkOrder.value = wo
}

async function runCreate() {
  const original = intent.value
  if (!original || submitting.value || (!accepted.value && !canCreateReceipt.value)) return
  result.value = null
  submitting.value = true
  try {
    if (!accepted.value) {
      const response = await createReceipt(original.input, original.requestedAtUtc)
      if (intent.value !== original) return
      if (response?.success === false) throw response
      const confirmation = response?.data
      if (!confirmation?.requestNo || !confirmation.finishedGoodsReceiptRequestId) {
        result.value = {
          status: 'error',
          title: '完工入库结果待确认',
          action: 'retry',
          description: '未收到原登记单号，请重试原提交。',
        }
        return
      }
      accepted.value = confirmation
    }
    const row = await readReceipt(original.input.workOrderId, accepted.value)
    if (intent.value !== original) return
    if (
      !row ||
      row.producedLotNo !== original.input.producedLotNo ||
      typeof row.quantity !== 'number' ||
      !row.receiptStatus
    )
      throw new Error('未找到原登记')
    result.value = {
      status: 'success',
      title: '完工入库已提交',
      description: `单号 ${row.requestNo} · 批次 ${row.producedLotNo} · 数量 ${row.quantity} · ${receiptStatusLabel(row.receiptStatus)}`,
    }
  } catch (error) {
    if (intent.value !== original) return
    if (accepted.value) {
      result.value = {
        status: 'error',
        title: '完工入库待回读',
        action: 'read',
        description: `已收到单号 ${accepted.value.requestNo}，请重新读取原登记。`,
      }
      return
    }
    const failure = describeRequestError(error, '请核对产出批次、入库数量和工单计量单位后重试。')
    result.value = {
      status: 'error',
      title: failure.indeterminate ? '完工入库结果待确认' : '完工入库失败',
      description: failure.message,
      action: failure.indeterminate ? 'retry' : 'correct',
    }
  } finally {
    if (intent.value === original) submitting.value = false
  }
}

function submitCreate() {
  const wo = selectedWorkOrder.value
  const lot = selectedLot.value
  if (
    !canCreateReceipt.value ||
    !createValid.value ||
    !wo?.workOrderId ||
    !lot ||
    scanGuarded.value ||
    submitting.value
  )
    return
  intent.value = {
    requestedAtUtc: new Date().toISOString(),
    input: {
      workOrderId: wo.workOrderId,
      skuId: wo.skuId,
      quantity: quantity.value,
      uomCode: uomCode.value.trim(),
      producedLotNo: lot.producedLotNo,
      serialNo: lot.serialNo,
      idempotencyKey: makeIdempotencyKey(),
    },
  }
  creating.value = false
  return runCreate()
}

function correctCreate() {
  if (!canCreateReceipt.value) return
  intent.value = undefined
  result.value = null
  reloadLots.value += 1
  creating.value = true
}

function continueWork() {
  result.value = null
  resetForm()
}
function goBack() {
  result.value = null
  resetForm()
  router.push('/').catch(() => {})
}

const scanActive = computed(() => result.value === null && !creating.value && !submitting.value)

function onScanAccepted(value: MesScanAccepted) {
  if (value.kind !== 'work-order' && value.kind !== 'operation-task') return
  scannedWorkOrderId.value = value.workOrderId
  scannedOperationTaskId.value = value.kind === 'operation-task' ? value.operationTaskId : ''
  filters.keyword = undefined
  filters.workOrderId = value.workOrderId
}

function onCreateScanAccepted(value: MesScanAccepted) {
  if (value.kind !== 'work-order' && value.kind !== 'operation-task') return
  scannedWorkOrderId.value = value.workOrderId
  scannedOperationTaskId.value = value.kind === 'operation-task' ? value.operationTaskId : ''
  workOrderFilters.keyword = undefined
  workOrderFilters.workOrderId = value.workOrderId
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
        <h1 class="text-lg font-semibold text-foreground">完工入库</h1>
        <button
          v-if="canCreateReceipt"
          type="button"
          data-testid="new-receipt"
          :disabled="scanPending || submitting || result !== null"
          class="ml-auto rounded-lg bg-primary px-3 py-1.5 text-sm font-medium text-primary-foreground"
          @click="openCreate"
        >
          新建完工入库
        </button>
      </div>
    </template>

    <!-- 写操作结果反馈 -->
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
          data-testid="continue-receipt"
          class="min-h-touch w-full rounded-lg bg-primary text-base font-medium text-primary-foreground"
          @click="continueWork"
        >
          继续
        </button>
        <button
          v-else
          type="button"
          data-testid="retry-receipt"
          class="min-h-touch w-full rounded-lg bg-primary text-base font-medium text-primary-foreground"
          @click="result.action === 'correct' ? correctCreate() : runCreate()"
        >
          {{
            result.action === 'correct'
              ? '返回修改'
              : result.action === 'read'
                ? '重新读取原登记'
                : '重试原提交'
          }}
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

    <p v-else-if="submitting" role="status" class="p-4">正在核对完工入库登记…</p>
    <div v-else class="space-y-4 p-4">
      <MesScanPrevalidation
        :organization-id="filters.organizationId"
        :environment-id="filters.environmentId"
        :work-order-id="scannedWorkOrderId"
        :operation-task-id="scannedOperationTaskId"
        placeholder="扫描工单或工序"
        :active="scanActive"
        :accepted-kinds="['work-order', 'operation-task']"
        @accepted="onScanAccepted"
        @status-change="scanGate.set('list', $event)"
      />

      <RetryableListError
        v-if="listFailure"
        :error="listFailure"
        :pending="pending"
        fallback="加载完工入库申请失败，请下拉刷新或重试。"
        test-id="receipt-error"
        @retry="() => refresh()"
      />

      <div
        v-if="
          scopeReady && !pending && !error && hasSuccessfulResponse && scopedReceipts.length === 0
        "
        class="rounded-lg border border-dashed border-border bg-card px-4 py-8 text-center text-sm text-muted-foreground"
      >
        暂无完工入库申请
      </div>

      <div
        v-else-if="scopeReady && scopedReceipts.length > 0"
        class="overflow-hidden rounded-lg border border-border"
      >
        <NvListRow
          v-for="req in scopedReceipts"
          :key="req.receiptRequestId ?? `${req.workOrderId}-${req.skuId}`"
          :title="receiptTitle(req)"
          :subtitle="receiptSubtitle(req)"
          :interactive="false"
        >
          <!-- 待入库卡在哪一环（#3728 在 PDA 的补齐，#3767）：判断逻辑与业务控制台共用
               @nerv-iip/business-core 的 receiptPendingReason，不在这里另抄一份。 -->
          <template v-if="receiptPendingReason(req)" #meta>
            <p
              data-testid="receipt-pending-reason"
              class="text-xs leading-snug text-muted-foreground"
            >
              {{ receiptPendingReason(req) }}
            </p>
          </template>
        </NvListRow>
      </div>
    </div>

    <!-- 新建完工入库（选工单 → 选批次并填写数量和单位 → 登记）-->
    <NvBottomSheet
      :open="canCreateReceipt && createSheetOpen"
      title="新建完工入库"
      @update:open="createSheetOpen = $event"
    >
      <div class="space-y-4 pb-2">
        <p class="text-xs text-muted-foreground">
          第
          {{ progress.completed + 1 > progress.total ? progress.total : progress.completed + 1 }}/{{
            progress.total
          }}
          步
        </p>

        <!-- 步骤 1：选工单 -->
        <div v-if="!selectedWorkOrder" class="space-y-2">
          <MesScanPrevalidation
            :organization-id="filters.organizationId"
            :environment-id="filters.environmentId"
            :work-order-id="scannedWorkOrderId"
            :operation-task-id="scannedOperationTaskId"
            placeholder="扫描工单或工序"
            :accepted-kinds="['work-order', 'operation-task']"
            @accepted="onCreateScanAccepted"
            @status-change="scanGate.set('create', $event)"
          />
          <p class="text-sm text-muted-foreground">
            选择完工入库的工单（共 {{ workOrderTotal }} 张）
          </p>
          <RetryableListError
            v-if="workOrdersError"
            :error="workOrdersError"
            :pending="workOrdersPending"
            fallback="加载工单失败，请稍后重试。"
            test-id="receipt-work-orders-error"
            @retry="() => refreshWorkOrders()"
          />
          <div
            v-else-if="workOrders.length === 0"
            class="rounded-lg border border-dashed border-border bg-card px-4 py-8 text-center text-sm text-muted-foreground"
          >
            暂无可完工入库的工单
          </div>
          <div
            v-else
            class="max-h-64 overflow-y-auto overflow-x-hidden rounded-lg border border-border"
          >
            <NvListRow
              v-for="wo in workOrders"
              :key="wo.workOrderId"
              data-testid="receipt-work-order"
              :title="workOrderTitle(wo)"
              :subtitle="workOrderSubtitle(wo)"
              @select="chooseWorkOrder(wo)"
            />
          </div>
        </div>

        <!-- 步骤 2：选择实际产出批次，填写数量与单位 -->
        <div v-else class="space-y-4">
          <div
            class="flex items-center justify-between rounded-lg border border-border bg-card px-4 py-3"
          >
            <div class="min-w-0">
              <p class="text-sm text-muted-foreground">当前工单</p>
              <p class="truncate text-base font-medium text-foreground">
                {{ workOrderTitle(selectedWorkOrder) }}
              </p>
            </div>
            <button
              type="button"
              data-testid="change-work-order"
              class="shrink-0 text-sm text-primary"
              @click="changeWorkOrder"
            >
              改选工单
            </button>
          </div>

          <p class="text-sm">入库物料 {{ selectedWorkOrder.skuCode ?? selectedWorkOrder.skuId }}</p>
          <fieldset class="space-y-2">
            <legend class="text-sm font-medium">选择产出批次</legend>
            <p v-if="lotsPending" role="status" class="text-sm">正在读取产出批次…</p>
            <RetryableListError
              v-else-if="lotsError"
              :error="lotsError"
              :pending="lotsPending"
              fallback="无法读取产出批次，请重试。"
              @retry="reloadLots += 1"
            />
            <p v-else-if="!lots.length" class="text-sm text-muted-foreground">
              当前工单暂无可入库批次。请确认已报工产出，或改选工单。
              <button type="button" class="min-h-touch text-primary" @click="reloadLots += 1">
                刷新批次
              </button>
            </p>
            <div v-else class="max-h-40 overflow-y-auto">
              <label
                v-for="lot in lots"
                :key="lot.producedLotNo"
                class="flex min-h-touch items-center gap-2 rounded-lg border border-border px-3 py-2"
              >
                <input
                  v-model="selectedLotNo"
                  type="radio"
                  name="receipt-lot"
                  :value="lot.producedLotNo"
                />
                <span class="min-w-0">
                  <span class="block break-all">{{ lot.producedLotNo }}</span>
                  <span class="block text-sm text-muted-foreground">
                    <template v-if="lot.reportNo">报工 {{ lot.reportNo }} · </template>
                    <template v-if="lot.serialNo">序列号 {{ lot.serialNo }} · </template>
                    剩余可入库 {{ formatReceiptNumber(lot.remainingQuantity) }}
                  </span>
                </span>
              </label>
            </div>
          </fieldset>

          <button
            type="button"
            data-testid="receipt-quantity"
            :aria-describedby="
              quantityText && quantityError && !quantityKeyboardOpen
                ? 'receipt-quantity-error'
                : undefined
            "
            class="min-h-touch w-full rounded-lg border border-border bg-card px-3 text-left"
            @click="quantityKeyboardOpen = true"
          >
            入库数量 {{ quantityText || '请输入' }}
          </button>
          <NvNumberKeyboard
            v-model="quantityText"
            v-model:show="quantityKeyboardOpen"
            title="入库数量"
          />

          <label class="block space-y-1">
            <span class="text-sm font-medium text-foreground">计量单位</span>
            <input
              v-model="uomCode"
              data-testid="receipt-uom"
              maxlength="30"
              type="text"
              class="min-h-touch w-full rounded-lg border border-border bg-card px-3 text-base outline-none focus:border-primary"
            />
          </label>

          <p
            v-if="quantityText && quantityError && !quantityKeyboardOpen"
            id="receipt-quantity-error"
            class="text-sm text-destructive"
          >
            {{ quantityError }}
          </p>
          <p v-if="!uomCode.trim()" class="text-sm text-muted-foreground">请填写工单计量单位。</p>

          <button
            type="button"
            data-testid="submit-receipt"
            :disabled="!createValid || submitting || scanGuarded"
            class="min-h-touch w-full rounded-lg bg-primary text-base font-medium text-primary-foreground disabled:opacity-60"
            @click="submitCreate"
          >
            提交完工入库
          </button>
          <button
            type="button"
            class="min-h-touch w-full rounded-lg border border-border bg-card text-base font-medium text-foreground"
            @click="closeCreate"
          >
            取消
          </button>
        </div>
      </div>
    </NvBottomSheet>
  </NvAppShellMobile>
</template>
