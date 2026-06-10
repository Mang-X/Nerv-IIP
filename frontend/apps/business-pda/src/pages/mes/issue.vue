<script setup lang="ts">
import type {
  BusinessConsoleMesMaterialIssueRequestRow,
  BusinessConsoleMesWorkOrderItem,
} from '@nerv-iip/api-client'
import { AppShellMobile, BottomSheet, ListRow, Result, ScanBar } from '@nerv-iip/ui-mobile'
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useMesMaterialIssue, useMesWorkOrders } from '@/composables/useBusinessMes'

definePage({
  meta: {
    requiresAuth: true,
    title: '领料',
  },
})

type IssueRequest = BusinessConsoleMesMaterialIssueRequestRow
type WorkOrder = BusinessConsoleMesWorkOrderItem

const router = useRouter()

const {
  filters,
  requests,
  total,
  pending,
  error,
  createIssue,
  confirmLineSideReceipt,
} = useMesMaterialIssue()

const {
  filters: workOrderFilters,
  workOrders,
  total: workOrderTotal,
} = useMesWorkOrders()

// --- 可读中文状态标签（不暴露原始状态码）---
const STATUS_LABELS: Record<string, string> = {
  Requested: '待领料',
  Pending: '待领料',
  Issued: '已发料',
  PartiallyReceived: '部分接收',
  Received: '已接收',
  Confirmed: '已接收',
  Completed: '已完成',
  Cancelled: '已取消',
  Rejected: '已驳回',
}
function statusLabel(status?: string) {
  return STATUS_LABELS[status ?? ''] ?? '未知状态'
}

const WORK_ORDER_STATUS_LABELS: Record<string, string> = {
  Released: '已下达',
  Planned: '已计划',
  InProgress: '生产中',
  Started: '生产中',
  Completed: '已完成',
  Closed: '已关闭',
  OnHold: '已挂起',
}
function workOrderStatusLabel(status?: string) {
  return WORK_ORDER_STATUS_LABELS[status ?? ''] ?? '未知状态'
}

// 领料申请没有自带业务单号；用工单 + 物料组合作可读标题，
// 不把 requestId（GUID）当标签暴露——它仅作为列表 key 与接收动作的 path 参数。
function requestTitle(req: IssueRequest) {
  const wo = req.workOrderId ?? '无工单'
  return req.materialId ? `${wo} · 物料 ${req.materialId}` : wo
}
function requestSubtitle(req: IssueRequest) {
  const parts = [statusLabel(req.status)]
  if (req.requestedQuantity !== undefined) parts.push(`申请 ${req.requestedQuantity}`)
  if (req.receivedQuantity !== undefined) parts.push(`已收 ${req.receivedQuantity}`)
  return parts.join(' · ')
}

function workOrderTitle(wo: WorkOrder) {
  return wo.workOrderId ?? '无工单'
}
function workOrderSubtitle(wo: WorkOrder) {
  const parts = [workOrderStatusLabel(wo.status)]
  if (wo.skuId) parts.push(`物料 ${wo.skuId}`)
  if (wo.quantity !== undefined) parts.push(`计划 ${wo.quantity}`)
  return parts.join(' · ')
}

// --- 列表加载错误 ---
const errorMessage = computed(() => {
  const e = error.value
  if (!e) return ''
  return e instanceof Error ? e.message : '加载领料申请失败，请下拉刷新或重试。'
})

// --- 结果反馈 ---
type ResultState = { status: 'success' | 'error'; title: string; description?: string; retry: () => void | Promise<void> }
const result = ref<ResultState | null>(null)
const submitting = ref(false)

// --- 新建领料表单 ---
const creating = ref(false)
const selectedWorkOrder = ref<WorkOrder | null>(null)
const issueMaterialId = ref('')
const issueQuantity = ref<number | null>(null)

const createValid = computed(() => {
  if (!selectedWorkOrder.value?.workOrderId) return false
  if (issueMaterialId.value.trim() === '') return false
  // 数量可选；若填写则须 > 0
  if (issueQuantity.value !== null && !(issueQuantity.value > 0)) return false
  return true
})

const createSheetOpen = computed({
  get: () => creating.value && result.value === null,
  set: (open) => {
    if (!open) closeCreate()
  },
})

function openCreate() {
  result.value = null
  selectedWorkOrder.value = null
  issueMaterialId.value = ''
  issueQuantity.value = null
  creating.value = true
}
function closeCreate() {
  creating.value = false
  selectedWorkOrder.value = null
}
function chooseWorkOrder(wo: WorkOrder) {
  selectedWorkOrder.value = wo
}

async function submitCreate() {
  const workOrderId = selectedWorkOrder.value?.workOrderId
  const materialId = issueMaterialId.value.trim()
  if (!workOrderId || materialId === '') return
  if (issueQuantity.value !== null && !(issueQuantity.value > 0)) return
  const quantity = issueQuantity.value
  submitting.value = true
  creating.value = false
  const doSubmit = () => createIssue(workOrderId, {
    materialId,
    ...(quantity === null ? {} : { quantity }),
  })
  try {
    await doSubmit()
    result.value = {
      status: 'success',
      title: '领料申请已提交',
      retry: () => {},
    }
  } catch (e) {
    result.value = {
      status: 'error',
      title: '领料申请失败',
      description: e instanceof Error ? e.message : '请检查网络后重试。',
      retry: retryCreate,
    }
  } finally {
    submitting.value = false
  }
}

async function retryCreate() {
  result.value = null
  await submitCreate()
}

// --- 线边接收 ---
const receiving = ref<IssueRequest | null>(null)
const receivedQuantity = ref<number | null>(null)

const receiveValid = computed(() => {
  // 接收数量可选；若填写则须 >= 0
  if (receivedQuantity.value === null) return true
  return receivedQuantity.value >= 0
})

const receiveSheetOpen = computed({
  get: () => receiving.value !== null && result.value === null,
  set: (open) => {
    if (!open) closeReceive()
  },
})

function openReceive(req: IssueRequest) {
  result.value = null
  receiving.value = req
  receivedQuantity.value = req.requestedQuantity ?? null
}
function closeReceive() {
  receiving.value = null
  receivedQuantity.value = null
}

async function submitReceive() {
  const req = receiving.value
  const requestId = req?.requestId
  if (!requestId) return
  if (!receiveValid.value) return
  const quantity = receivedQuantity.value
  submitting.value = true
  receiving.value = null
  const doSubmit = () => confirmLineSideReceipt(requestId, {
    ...(quantity === null ? {} : { receivedQuantity: quantity }),
  })
  try {
    await doSubmit()
    result.value = {
      status: 'success',
      title: '线边接收已确认',
      retry: () => {},
    }
  } catch (e) {
    result.value = {
      status: 'error',
      title: '线边接收失败',
      description: e instanceof Error ? e.message : '请检查网络后重试。',
      retry: () => retryReceive(req!, quantity),
    }
  } finally {
    submitting.value = false
  }
}

async function retryReceive(req: IssueRequest, quantity: number | null) {
  result.value = null
  receiving.value = req
  receivedQuantity.value = quantity
  await submitReceive()
}

function continueWork() {
  result.value = null
}
function goBack() {
  result.value = null
  router.push('/').catch(() => {})
}

// ScanBar 仅在列表态活跃；新建/接收/结果展开时不抢焦点
const scanActive = computed(() =>
  result.value === null && !creating.value && receiving.value === null,
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
        <h1 class="text-lg font-semibold text-foreground">领料</h1>
        <button
          type="button"
          data-testid="new-issue"
          class="ml-auto rounded-lg bg-primary px-3 py-1.5 text-sm font-medium text-primary-foreground"
          @click="openCreate"
        >
          新建领料
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
          class="min-h-touch w-full rounded-lg bg-primary text-base font-medium text-primary-foreground"
          @click="continueWork"
        >
          继续
        </button>
        <button
          v-else
          type="button"
          data-testid="retry-issue"
          class="min-h-touch w-full rounded-lg bg-primary text-base font-medium text-primary-foreground"
          @click="result.retry"
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
      <ScanBar placeholder="扫描工单号 / 领料单" :active="scanActive" @scan="onScan" />

      <p class="text-sm text-muted-foreground">共 {{ total }} 条领料申请</p>

      <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

      <div
        v-if="!pending && !error && requests.length === 0"
        class="rounded-lg border border-dashed border-border bg-card px-4 py-8 text-center text-sm text-muted-foreground"
      >
        暂无领料申请
      </div>

      <div v-else class="overflow-hidden rounded-lg border border-border">
        <ListRow
          v-for="req in requests"
          :key="req.requestId ?? `${req.workOrderId}-${req.materialId}`"
          :title="requestTitle(req)"
          :subtitle="requestSubtitle(req)"
          :interactive="false"
        >
          <template #trailing>
            <button
              type="button"
              :data-testid="`receive-${req.requestId}`"
              class="shrink-0 rounded-lg border border-border bg-card px-3 py-1.5 text-sm font-medium text-primary"
              @click="openReceive(req)"
            >
              线边接收
            </button>
          </template>
        </ListRow>
      </div>
    </div>

    <!-- 新建领料表单 -->
    <BottomSheet
      :open="createSheetOpen"
      title="新建领料"
      @update:open="createSheetOpen = $event"
    >
      <div class="space-y-4 pb-2">
        <!-- 选工单 -->
        <div v-if="!selectedWorkOrder" class="space-y-2">
          <ScanBar placeholder="扫描工单号" :active="false" @scan="onScanWorkOrder" />
          <p class="text-sm text-muted-foreground">选择领料的工单（共 {{ workOrderTotal }} 张）</p>
          <div
            v-if="workOrders.length === 0"
            class="rounded-lg border border-dashed border-border bg-card px-4 py-8 text-center text-sm text-muted-foreground"
          >
            暂无可领料的工单
          </div>
          <div v-else class="max-h-64 overflow-y-auto overflow-x-hidden rounded-lg border border-border">
            <ListRow
              v-for="wo in workOrders"
              :key="wo.workOrderId"
              :data-testid="`issue-work-order`"
              :title="workOrderTitle(wo)"
              :subtitle="workOrderSubtitle(wo)"
              @select="chooseWorkOrder(wo)"
            />
          </div>
        </div>

        <!-- 选好工单：填物料 + 数量 -->
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
              @click="selectedWorkOrder = null"
            >
              改选工单
            </button>
          </div>

          <label class="block space-y-1">
            <span class="text-sm font-medium text-foreground">物料编号</span>
            <input
              v-model="issueMaterialId"
              data-testid="issue-material"
              type="text"
              class="min-h-touch w-full rounded-lg border border-border bg-card px-3 text-base outline-none focus:border-primary"
            />
          </label>

          <label class="block space-y-1">
            <span class="text-sm font-medium text-foreground">领料数量（可选）</span>
            <input
              v-model.number="issueQuantity"
              data-testid="issue-quantity"
              type="number"
              inputmode="numeric"
              min="0"
              class="min-h-touch w-full rounded-lg border border-border bg-card px-3 text-base outline-none focus:border-primary"
            />
          </label>

          <p v-if="!createValid" class="text-sm text-muted-foreground">
            请填写物料编号；若填写数量须大于 0。
          </p>

          <button
            type="button"
            data-testid="submit-issue"
            :disabled="!createValid || submitting"
            class="min-h-touch w-full rounded-lg bg-primary text-base font-medium text-primary-foreground disabled:opacity-60"
            @click="submitCreate"
          >
            提交领料
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

    <!-- 线边接收 -->
    <BottomSheet
      :open="receiveSheetOpen"
      :title="receiving ? requestTitle(receiving) : ''"
      @update:open="receiveSheetOpen = $event"
    >
      <div v-if="receiving" class="space-y-4 pb-2">
        <p class="text-sm text-muted-foreground">
          当前状态：{{ statusLabel(receiving.status) }}
        </p>

        <label class="block space-y-1">
          <span class="text-sm font-medium text-foreground">接收数量</span>
          <input
            v-model.number="receivedQuantity"
            data-testid="received-quantity"
            type="number"
            inputmode="numeric"
            min="0"
            class="min-h-touch w-full rounded-lg border border-border bg-card px-3 text-base outline-none focus:border-primary"
          />
        </label>

        <p v-if="!receiveValid" class="text-sm text-muted-foreground">
          接收数量须为非负数。
        </p>

        <button
          type="button"
          data-testid="submit-receive"
          :disabled="!receiveValid || submitting"
          class="min-h-touch w-full rounded-lg bg-primary text-base font-medium text-primary-foreground disabled:opacity-60"
          @click="submitReceive"
        >
          确认接收
        </button>
        <button
          type="button"
          class="min-h-touch w-full rounded-lg border border-border bg-card text-base font-medium text-foreground"
          @click="closeReceive"
        >
          取消
        </button>
      </div>
    </BottomSheet>
  </AppShellMobile>
</template>
