<script setup lang="ts">
import type {
  BusinessConsoleMesOperationTaskRow,
  BusinessConsoleMesWorkOrderItem,
} from '@nerv-iip/api-client'
import {
  operationTaskStatusLabel,
  productionReportFlow,
  type ReportCtx,
  workOrderSubtitle,
  workOrderTitle,
} from '@nerv-iip/business-core'
import {
  NvAppShellMobile,
  NvBottomSheet,
  NvListRow,
  NvMobileResult,
  NvScanBar,
} from '@nerv-iip/ui-mobile'
import { computed, reactive, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import {
  useMesOperationTasks,
  useMesProductionReports,
  useMesWorkOrders,
} from '@/composables/useBusinessMes'
import RetryableListError from '@/components/RetryableListError.vue'
import { makeIdempotencyKey } from '@/composables/makeIdempotencyKey'

definePage({
  meta: {
    requiresAuth: true,
    title: '报工',
  },
})

type WorkOrder = BusinessConsoleMesWorkOrderItem
type Task = BusinessConsoleMesOperationTaskRow

const router = useRouter()

const {
  filters: workOrderFilters,
  workOrders,
  total: workOrderTotal,
  pending: workOrdersPending,
  error: workOrdersError,
  refresh: refreshWorkOrders,
} = useMesWorkOrders()

const {
  filters: taskFilters,
  operationTasks,
  total: taskTotal,
  pending: tasksPending,
  error: tasksError,
  refresh: refreshTasks,
} = useMesOperationTasks()

const { recordReport } = useMesProductionReports()

// --- 流程上下文（productionReportFlow 驱动当前步/进度）---
const ctx = reactive<ReportCtx>({
  workOrderId: undefined,
  operationTaskId: undefined,
  quantityEntered: false,
  recorded: false,
})

const currentStep = computed(() => productionReportFlow.currentStep(ctx).id)
const progress = computed(() => productionReportFlow.progress(ctx))

// 选中的工单 / 工序行（用于展示可读标签）
const selectedWorkOrder = ref<WorkOrder | null>(null)
const selectedTask = ref<Task | null>(null)

// 工序查询按选中工单过滤
watch(
  () => selectedWorkOrder.value?.workOrderId,
  (workOrderId) => {
    taskFilters.workOrderId = workOrderId
  },
)

// --- 数量录入 ---
const goodQuantity = ref(0)
const scrapQuantity = ref(0)
const completesOperation = ref(false)

const quantityValid = computed(
  () =>
    goodQuantity.value >= 0 &&
    scrapQuantity.value >= 0 &&
    goodQuantity.value + scrapQuantity.value > 0,
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
const result = ref<ResultState | null>(null)
const submitting = ref(false)

// 稳定的逐操作幂等键：在提交时铸造一次，重试复用同键；
// 开始新报工（改选工单/工序、成功后回到起点）时清空 → 下次提交铸造新键。
const operationKey = ref('')

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

// --- 步骤操作 ---
function chooseWorkOrder(wo: WorkOrder) {
  selectedWorkOrder.value = wo
  ctx.workOrderId = wo.workOrderId
  // 重置后续步状态 → 新报工操作，作废上一个幂等键
  selectedTask.value = null
  ctx.operationTaskId = undefined
  ctx.quantityEntered = false
  operationKey.value = ''
}

function chooseTask(task: Task) {
  selectedTask.value = task
  ctx.operationTaskId = task.operationTaskId
  // 重置数量录入 → 新报工操作，作废上一个幂等键
  goodQuantity.value = 0
  scrapQuantity.value = 0
  completesOperation.value = false
  ctx.quantityEntered = false
  operationKey.value = ''
}

function closeSheet() {
  selectedTask.value = null
  ctx.operationTaskId = undefined
  ctx.quantityEntered = false
  operationKey.value = ''
}

// 返回上一步
function backToWorkOrders() {
  selectedWorkOrder.value = null
  ctx.workOrderId = undefined
  selectedTask.value = null
  ctx.operationTaskId = undefined
  ctx.quantityEntered = false
  operationKey.value = ''
}

async function submit() {
  const workOrderId = ctx.workOrderId
  const operationTaskId = selectedTask.value?.operationTaskId
  if (!workOrderId || !operationTaskId) return
  if (!quantityValid.value) return
  ctx.quantityEntered = true
  // 本次报工操作的稳定幂等键：首次提交铸造，重试复用同键。
  if (operationKey.value === '') {
    operationKey.value = makeIdempotencyKey()
  }
  submitting.value = true
  // 关闭录数量面板（结果以 Result 呈现）
  const good = goodQuantity.value
  const scrap = scrapQuantity.value
  const completes = completesOperation.value
  selectedTask.value = null
  try {
    await recordReport({
      workOrderId,
      operationTaskId,
      goodQuantity: good,
      scrapQuantity: scrap,
      completesOperation: completes,
      idempotencyKey: operationKey.value,
    })
    ctx.recorded = true
    result.value = {
      status: 'success',
      title: '报工成功',
      description: completes ? '本工序已标记完工。' : undefined,
    }
  } catch (e) {
    result.value = {
      status: 'error',
      title: '报工失败',
      description: e instanceof Error ? e.message : '请检查网络后重试。',
    }
    // 失败保留工序选择以便重试
    selectedTask.value =
      operationTasks.value.find((t) => t.operationTaskId === operationTaskId) ?? null
  } finally {
    submitting.value = false
  }
}

function continueReport() {
  // 重置整个流程，回到选工单
  result.value = null
  backToWorkOrders()
  ctx.recorded = false
}

function goBack() {
  result.value = null
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
      <!-- 步骤 1：选工单 -->
      <template v-if="currentStep === 'selectWorkOrder'">
        <NvScanBar placeholder="扫描工单号" :active="scanActive" @scan="onScanWorkOrder" />
        <p class="text-sm text-muted-foreground">选择报工的工单（共 {{ workOrderTotal }} 张）</p>
        <RetryableListError
          v-if="workOrdersError"
          :error="workOrdersError"
          :pending="workOrdersPending"
          fallback="加载工单失败，请下拉刷新或重试。"
          test-id="work-orders-error"
          @retry="() => refreshWorkOrders()"
        />
        <div
          v-else-if="!workOrdersPending && workOrders.length === 0"
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

        <p class="text-sm text-muted-foreground">选择要报工的工序（共 {{ taskTotal }} 道）</p>
        <RetryableListError
          v-if="tasksError"
          :error="tasksError"
          :pending="tasksPending"
          fallback="加载工序失败，请下拉刷新或重试。"
          test-id="tasks-error"
          @retry="() => refreshTasks()"
        />
        <div
          v-else-if="!tasksPending && operationTasks.length === 0"
          class="rounded-lg border border-dashed border-border bg-card px-4 py-8 text-center text-sm text-muted-foreground"
        >
          该工单暂无工序
        </div>
        <div v-else class="overflow-hidden rounded-lg border border-border">
          <NvListRow
            v-for="task in operationTasks"
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

        <label
          class="flex items-center justify-between gap-3 rounded-lg border border-border bg-card px-3 py-3"
        >
          <span class="text-sm font-medium text-foreground">完工本工序</span>
          <input
            v-model="completesOperation"
            data-testid="completes-operation"
            type="checkbox"
            class="size-5"
          />
        </label>

        <p v-if="!quantityValid" class="text-sm text-muted-foreground">
          良品数与次品数须为非负数，且合计大于 0。
        </p>

        <button
          type="button"
          data-testid="submit-report"
          :disabled="!quantityValid || submitting"
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
  </NvAppShellMobile>
</template>
