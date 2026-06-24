<script setup lang="ts">
import type {
  BusinessConsoleMesReceiptRequestRow,
  BusinessConsoleMesWorkOrderItem,
} from '@nerv-iip/api-client'
import {
  finishedGoodsReceiptFlow,
  type ReceiptCtx,
  receiptStatusLabel,
  workOrderSubtitle,
  workOrderTitle,
} from '@nerv-iip/business-core'
import { AppShellMobile, BottomSheet, ListRow, Result, ScanBar } from '@nerv-iip/ui-mobile'
import { computed, reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useMesReceipts, useMesWorkOrders } from '@/composables/useBusinessMes'
import { makeIdempotencyKey } from '@/composables/makeIdempotencyKey'

definePage({
  meta: {
    requiresAuth: true,
    title: '完工入库',
  },
})

type Receipt = BusinessConsoleMesReceiptRequestRow
type WorkOrder = BusinessConsoleMesWorkOrderItem

const router = useRouter()

const {
  filters,
  receipts,
  total,
  pending,
  error,
  createReceipt,
} = useMesReceipts()

const {
  filters: workOrderFilters,
  workOrders,
  total: workOrderTotal,
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
  if (req.unitCost !== undefined && req.unitCost !== null) parts.push(`成本 ${formatReceiptNumber(req.unitCost)}`)
  if (req.requestNo) parts.push(`单号 ${req.requestNo}`)
  return parts.join(' · ')
}

function formatReceiptNumber(value: number) {
  return new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 6 }).format(value)
}

// --- 列表加载错误 ---
const errorMessage = computed(() => {
  const e = error.value
  if (!e) return ''
  return e instanceof Error ? e.message : '加载完工入库申请失败，请下拉刷新或重试。'
})

// --- 流程上下文（finishedGoodsReceiptFlow 驱动当前步/进度）---
const ctx = reactive<ReceiptCtx>({
  workOrderId: undefined,
  skuId: undefined,
  quantityEntered: false,
  unitCostEntered: false,
  created: false,
})

const currentStep = computed(() => finishedGoodsReceiptFlow.currentStep(ctx).id)
const progress = computed(() => finishedGoodsReceiptFlow.progress(ctx))

// --- 新建完工入库表单 ---
const creating = ref(false)
const selectedWorkOrder = ref<WorkOrder | null>(null)
const skuId = ref('')
const quantity = ref<number | null>(null)
const unitCost = ref<number | null>(null)
const uomCode = ref('')

// SKU/数量/单位成本录入是否就绪（用于驱动流程第二步 done）
function syncEnterStep() {
  ctx.skuId = skuId.value.trim() || undefined
  ctx.quantityEntered = quantity.value !== null && quantity.value > 0
  ctx.unitCostEntered = unitCost.value !== null && unitCost.value > 0
}

const createValid = computed(() => {
  if (!selectedWorkOrder.value?.workOrderId) return false
  if (skuId.value.trim() === '') return false
  if (uomCode.value.trim() === '') return false
  if (quantity.value === null || !(quantity.value > 0)) return false
  if (unitCost.value === null || !(unitCost.value > 0)) return false
  return true
})

// --- 结果反馈 ---
type ResultState = { status: 'success' | 'error'; title: string; description?: string }
const result = ref<ResultState | null>(null)
const submitting = ref(false)

// 稳定的逐操作幂等键：提交时铸造一次，重试复用同键；
// 开始新完工入库（重新打开新建、成功）时清空 → 下次提交铸造新键。
const operationKey = ref('')

const createSheetOpen = computed({
  get: () => creating.value && result.value === null,
  set: (open) => {
    if (!open) closeCreate()
  },
})

function resetForm() {
  selectedWorkOrder.value = null
  skuId.value = ''
  quantity.value = null
  unitCost.value = null
  uomCode.value = ''
  ctx.workOrderId = undefined
  ctx.skuId = undefined
  ctx.quantityEntered = false
  ctx.unitCostEntered = false
  ctx.created = false
  // 新一轮完工入库 → 作废上一个幂等键
  operationKey.value = ''
}

function openCreate() {
  result.value = null
  resetForm()
  creating.value = true
}
function closeCreate() {
  creating.value = false
  resetForm()
}

function chooseWorkOrder(wo: WorkOrder) {
  selectedWorkOrder.value = wo
  ctx.workOrderId = wo.workOrderId
  // 工单自带 skuId 时预填，便于扫码后直接确认
  if (wo.skuId && skuId.value.trim() === '') {
    skuId.value = wo.skuId
  }
  syncEnterStep()
}
function changeWorkOrder() {
  selectedWorkOrder.value = null
  ctx.workOrderId = undefined
}

async function submitCreate() {
  const workOrderId = selectedWorkOrder.value?.workOrderId
  const sku = skuId.value.trim()
  const uom = uomCode.value.trim()
  const qty = quantity.value
  const cost = unitCost.value
  if (!workOrderId || sku === '' || uom === '' || qty === null || !(qty > 0) || cost === null || !(cost > 0)) return
  syncEnterStep()
  // 首次提交铸造稳定幂等键，重试复用同键。
  if (operationKey.value === '') {
    operationKey.value = makeIdempotencyKey()
  }
  submitting.value = true
  creating.value = false
  try {
    await createReceipt({
      workOrderId,
      skuId: sku,
      quantity: qty,
      unitCost: cost,
      uomCode: uom,
      idempotencyKey: operationKey.value,
    })
    ctx.created = true
    result.value = {
      status: 'success',
      title: '完工入库已提交',
    }
  } catch (e) {
    result.value = {
      status: 'error',
      title: '完工入库失败',
      description: e instanceof Error ? e.message : '请检查网络后重试。',
    }
  } finally {
    submitting.value = false
  }
}

async function retryCreate() {
  result.value = null
  creating.value = true
  await submitCreate()
}

function continueWork() {
  result.value = null
  resetForm()
}
function goBack() {
  result.value = null
  operationKey.value = ''
  router.push('/').catch(() => {})
}

// ScanBar 仅在列表态活跃；新建/结果展开时不抢焦点
const scanActive = computed(() =>
  result.value === null && !creating.value,
)

function onScan(value: string) {
  filters.keyword = value
}
function onScanWorkOrder(value: string) {
  workOrderFilters.keyword = value
}
</script>

<template>
  <AppShellMobile>
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
          type="button"
          data-testid="new-receipt"
          class="ml-auto rounded-lg bg-primary px-3 py-1.5 text-sm font-medium text-primary-foreground"
          @click="openCreate"
        >
          新建完工入库
        </button>
      </div>
    </template>

    <!-- 写操作结果反馈 -->
    <Result
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
          @click="retryCreate"
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
    </Result>

    <div v-else class="space-y-4 p-4">
      <ScanBar placeholder="扫描工单号 / 入库单" :active="scanActive" @scan="onScan" />

      <p class="text-sm text-muted-foreground">共 {{ total }} 条完工入库申请</p>

      <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

      <div
        v-if="!pending && !error && receipts.length === 0"
        class="rounded-lg border border-dashed border-border bg-card px-4 py-8 text-center text-sm text-muted-foreground"
      >
        暂无完工入库申请
      </div>

      <div v-else class="overflow-hidden rounded-lg border border-border">
        <ListRow
          v-for="req in receipts"
          :key="req.receiptRequestId ?? `${req.workOrderId}-${req.skuId}`"
          :title="receiptTitle(req)"
          :subtitle="receiptSubtitle(req)"
          :interactive="false"
        />
      </div>
    </div>

    <!-- 新建完工入库（finishedGoodsReceiptFlow：选工单 → 录 SKU/数量/单位成本/单位 → 创建）-->
    <BottomSheet
      :open="createSheetOpen"
      title="新建完工入库"
      @update:open="createSheetOpen = $event"
    >
      <div class="space-y-4 pb-2">
        <p class="text-xs text-muted-foreground">
          第 {{ progress.completed + 1 > progress.total ? progress.total : progress.completed + 1 }}/{{ progress.total }} 步
        </p>

        <!-- 步骤 1：选工单 -->
        <div v-if="currentStep === 'selectWorkOrder' || !selectedWorkOrder" class="space-y-2">
          <ScanBar placeholder="扫描工单号" :active="false" @scan="onScanWorkOrder" />
          <p class="text-sm text-muted-foreground">选择完工入库的工单（共 {{ workOrderTotal }} 张）</p>
          <div
            v-if="workOrders.length === 0"
            class="rounded-lg border border-dashed border-border bg-card px-4 py-8 text-center text-sm text-muted-foreground"
          >
            暂无可完工入库的工单
          </div>
          <div v-else class="max-h-64 overflow-y-auto overflow-x-hidden rounded-lg border border-border">
            <ListRow
              v-for="wo in workOrders"
              :key="wo.workOrderId"
              data-testid="receipt-work-order"
              :title="workOrderTitle(wo)"
              :subtitle="workOrderSubtitle(wo)"
              @select="chooseWorkOrder(wo)"
            />
          </div>
        </div>

        <!-- 步骤 2：录 SKU / 数量 / 单位成本 / 单位 -->
        <div v-else class="space-y-4">
          <div class="flex items-center justify-between rounded-lg border border-border bg-card px-4 py-3">
            <div class="min-w-0">
              <p class="text-sm text-muted-foreground">当前工单</p>
              <p class="truncate text-base font-medium text-foreground">{{ workOrderTitle(selectedWorkOrder) }}</p>
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

          <label class="block space-y-1">
            <span class="text-sm font-medium text-foreground">入库物料（SKU）</span>
            <input
              v-model="skuId"
              data-testid="receipt-sku"
              type="text"
              class="min-h-touch w-full rounded-lg border border-border bg-card px-3 text-base outline-none focus:border-primary"
              @input="syncEnterStep"
            />
          </label>

          <label class="block space-y-1">
            <span class="text-sm font-medium text-foreground">入库数量</span>
            <input
              v-model.number="quantity"
              data-testid="receipt-quantity"
              type="number"
              inputmode="decimal"
              min="0.000001"
              step="0.000001"
              class="min-h-touch w-full rounded-lg border border-border bg-card px-3 text-base outline-none focus:border-primary"
              @input="syncEnterStep"
            />
          </label>

          <label class="block space-y-1">
            <span class="text-sm font-medium text-foreground">单位成本</span>
            <input
              v-model.number="unitCost"
              data-testid="receipt-unit-cost"
              type="number"
              inputmode="decimal"
              min="0.000001"
              step="0.000001"
              class="min-h-touch w-full rounded-lg border border-border bg-card px-3 text-base outline-none focus:border-primary"
              @input="syncEnterStep"
            />
          </label>

          <label class="block space-y-1">
            <span class="text-sm font-medium text-foreground">计量单位</span>
            <input
              v-model="uomCode"
              data-testid="receipt-uom"
              type="text"
              class="min-h-touch w-full rounded-lg border border-border bg-card px-3 text-base outline-none focus:border-primary"
            />
          </label>

          <p v-if="!createValid" class="text-sm text-muted-foreground">
            请填写入库物料与计量单位，且入库数量、单位成本须大于 0。
          </p>

          <button
            type="button"
            data-testid="submit-receipt"
            :disabled="!createValid || submitting"
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
    </BottomSheet>
  </AppShellMobile>
</template>
