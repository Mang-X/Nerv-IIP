<script setup lang="ts">
import RetryableListError from '@/components/RetryableListError.vue'
import { warehouseTaskStatusLabel } from '@nerv-iip/business-core'
import {
  NvBottomSheet,
  NvListRow,
  NvMobileButton,
  NvMobileDropdownMenu,
  NvMobileDropdownMenuItem,
  NvMobileEmpty,
  NvMobileInput,
  NvMobileTag,
  NvPullRefresh,
  NvScanBar,
  NvSearchBar,
  type DropdownOption,
} from '@nerv-iip/ui-mobile'
import { useIntersectionObserver } from '@vueuse/core'
import { computed, shallowRef, useTemplateRef, watch } from 'vue'

export interface WarehouseTaskExecutionItem {
  warehouseTaskId: string
  taskNo?: string
  sourceOrderNo?: string
  skuCode?: string
  lotNo?: string | null
  serialNo?: string | null
  uomCode?: string
  fromLocationCode?: string
  toLocationCode?: string
  plannedQuantity?: number
  executedQuantity?: number
  status?: string
  version?: number
  assignedOperatorUserId?: string | null
  assignedTeamId?: string | null
  allowedActions?: string[]
  blockReasons?: string[]
}

export interface WarehouseTaskScopeOption {
  label: string
  value: string
}

export interface WarehouseTaskExecutionIntent {
  action: 'start' | 'progress' | 'exception' | 'complete'
  task: WarehouseTaskExecutionItem
  executedQuantity?: number
  exceptionCode?: string
  reason?: string
}

const props = withDefaults(
  defineProps<{
    title: string
    taskType: 'picking' | 'putaway'
    tasks: WarehouseTaskExecutionItem[]
    total: number
    pending: boolean
    refreshing: boolean
    loadingMore: boolean
    currentPrincipalId?: string
    status?: string
    scopeKey?: string
    keyword?: string
    locationCode?: string
    scopeOptions?: WarehouseTaskScopeOption[]
    error?: unknown
    actionPending?: boolean
  }>(),
  {
    currentPrincipalId: undefined,
    status: undefined,
    scopeKey: undefined,
    keyword: undefined,
    locationCode: undefined,
    scopeOptions: () => [],
    error: undefined,
    actionPending: false,
  },
)

const emit = defineEmits<{
  'update:status': [value: string | undefined]
  'update:scopeKey': [value: string | undefined]
  'update:keyword': [value: string | undefined]
  'scan-location': [value: string]
  refresh: []
  loadMore: []
  retry: []
  execute: [intent: WarehouseTaskExecutionIntent]
}>()

const selectedTask = shallowRef<WarehouseTaskExecutionItem>()
const executedQuantity = shallowRef<number | string>(0)
const selectedReason = shallowRef('')
const validationMessage = shallowRef('')

const statusOptions: DropdownOption[] = [
  { label: '全部状态', value: '' },
  { label: '待执行', value: 'Open' },
  { label: '执行中', value: 'InProgress' },
  { label: '异常待处理', value: 'Exception' },
  { label: '已完成', value: 'Completed' },
  { label: '差异完成', value: 'CompletedWithDifference' },
]
const quickReasons = [
  { code: 'short-stock', label: '库位缺货' },
  { code: 'damaged', label: '包装破损' },
  { code: 'lot-mismatch', label: '批次不符' },
  { code: 'location-blocked', label: '库位受阻' },
] as const

const scopeDropdownOptions = computed<DropdownOption[]>(() =>
  props.scopeOptions.map((option) => ({ label: option.label, value: option.value })),
)
const scopeModel = computed<string | number | undefined>({
  get: () => props.scopeKey,
  set: (value) => emit('update:scopeKey', value ? String(value) : undefined),
})
const statusModel = computed<string | number | undefined>({
  get: () => props.status,
  set: (value) => emit('update:status', value ? String(value) : undefined),
})
const keywordModel = computed<string>({
  get: () => props.keyword ?? '',
  set: (value) => emit('update:keyword', value || undefined),
})
const hasMore = computed(() => props.tasks.length < props.total)
const loadMoreSentinel = useTemplateRef<HTMLElement>('loadMoreSentinel')
const selectedActions = computed(() => selectedTask.value?.allowedActions ?? [])
const canStart = computed(() => selectedActions.value.includes('start'))
const canProgress = computed(() => selectedActions.value.includes('progress'))
const canReportException = computed(() => selectedActions.value.includes('exception'))
const canComplete = computed(() => selectedActions.value.includes('complete'))
const actionSheetOpen = computed(() => selectedTask.value !== undefined)
const actionLabel = computed(() => (props.taskType === 'picking' ? '拣货' : '上架'))

useIntersectionObserver(
  loadMoreSentinel,
  ([entry]) => {
    if (entry?.isIntersecting && hasMore.value && !props.pending && !props.loadingMore) {
      emit('loadMore')
    }
  },
  { rootMargin: '80px 0px' },
)

function taskTitle(task: WarehouseTaskExecutionItem) {
  return `任务 ${task.taskNo || task.sourceOrderNo || ''}`.trim()
}

function taskSubtitle(task: WarehouseTaskExecutionItem) {
  const quantity = `${task.executedQuantity ?? 0} / ${task.plannedQuantity ?? 0} ${task.uomCode ?? ''}`
  return [
    task.skuCode ? `物料 ${task.skuCode}` : undefined,
    task.lotNo ? `批次 ${task.lotNo}` : undefined,
    `${task.fromLocationCode ?? ''} → ${task.toLocationCode ?? ''}`,
    quantity.trim(),
    warehouseTaskStatusLabel(task.status),
  ]
    .filter(Boolean)
    .join(' · ')
}

function selectedReasonLabel() {
  return quickReasons.find((reason) => reason.code === selectedReason.value)?.label ?? ''
}

function blockReasonLabel(reason: string) {
  return reason === 'TASK_TERMINAL' ? '任务已结束，不可继续操作' : '当前任务不可操作'
}

function assignmentLabel(task: WarehouseTaskExecutionItem) {
  const assignee = task.assignedOperatorUserId?.trim()
  if (!assignee) return undefined
  return assignee === props.currentPrincipalId?.trim() ? '已派给我' : '已派给他人'
}

function selectTask(task: WarehouseTaskExecutionItem) {
  if ((task.allowedActions ?? []).length === 0) return
  selectedTask.value = task
  executedQuantity.value = task.executedQuantity ?? 0
  selectedReason.value = ''
  validationMessage.value = ''
}

function closeSheet() {
  selectedTask.value = undefined
  validationMessage.value = ''
}

watch(
  [() => props.scopeKey, () => props.status, () => props.keyword, () => props.locationCode],
  closeSheet,
)

function emitSimpleAction(action: 'start' | 'exception') {
  const task = selectedTask.value
  if (!task) return
  if (action === 'exception' && !selectedReason.value) {
    validationMessage.value = '请选择异常原因'
    return
  }

  emit('execute', {
    action,
    task,
    ...(action === 'exception'
      ? { exceptionCode: selectedReason.value, reason: selectedReasonLabel() }
      : {}),
  })
  closeSheet()
}

function emitQuantityAction(action: 'progress' | 'complete') {
  const task = selectedTask.value
  if (!task) return
  const quantity = Number(executedQuantity.value)
  const current = task.executedQuantity ?? 0
  const planned = task.plannedQuantity ?? 0
  if (!Number.isFinite(quantity) || quantity < current || quantity > planned) {
    validationMessage.value = `数量须在 ${current} 至 ${planned} 之间`
    return
  }
  if (action === 'complete' && props.taskType === 'putaway' && quantity < planned) {
    validationMessage.value = '上架任务须完成全部计划数量；无法完成时请上报异常'
    return
  }
  if (action === 'complete' && quantity < planned && !selectedReason.value) {
    validationMessage.value = '请选择差异原因'
    return
  }

  validationMessage.value = ''
  emit('execute', {
    action,
    task,
    executedQuantity: quantity,
    ...(quantity < planned ? { reason: selectedReasonLabel() } : {}),
  })
  closeSheet()
}
</script>

<template>
  <div class="flex min-h-0 flex-1 flex-col">
    <div class="space-y-3 border-b border-border bg-card px-4 py-3">
      <NvSearchBar v-model="keywordModel" :placeholder="`搜索任务号、源单号或物料`" />
      <NvScanBar
        :placeholder="locationCode ? `当前库位 ${locationCode}` : '扫描库位或容器码'"
        @scan="emit('scan-location', $event)"
      />
      <NvMobileDropdownMenu>
        <NvMobileDropdownMenuItem
          v-if="scopeDropdownOptions.length > 0"
          v-model="scopeModel"
          title="作业范围"
          :options="scopeDropdownOptions"
        />
        <NvMobileDropdownMenuItem v-model="statusModel" title="任务状态" :options="statusOptions" />
      </NvMobileDropdownMenu>
    </div>

    <RetryableListError
      v-if="error"
      class="mx-4 mt-3"
      :error="error"
      :pending="pending"
      fallback="任务操作或加载失败，请重试；若状态已变化，刷新后继续。"
      test-id="error-banner"
      @retry="emit('retry')"
    />

    <NvPullRefresh
      data-testid="pull-refresh"
      class="min-h-0 flex-1"
      :model-value="refreshing"
      @refresh="emit('refresh')"
    >
      <NvMobileEmpty
        v-if="!pending && !error && tasks.length === 0"
        description="当前范围暂无任务。任务来自 WMS 派工，可切换作业范围或状态后重试。"
      />

      <div v-else class="divide-y divide-border">
        <NvListRow
          v-for="task in tasks"
          :key="task.warehouseTaskId"
          :data-task-no="task.taskNo"
          :interactive="(task.allowedActions ?? []).length > 0"
          :title="taskTitle(task)"
          :subtitle="taskSubtitle(task)"
          @select="selectTask(task)"
        >
          <template #meta>
            <div class="mt-2 flex flex-wrap gap-1.5">
              <NvMobileTag size="sm">
                {{ warehouseTaskStatusLabel(task.status) }}
              </NvMobileTag>
              <NvMobileTag v-if="assignmentLabel(task)" size="sm" variant="brand">
                {{ assignmentLabel(task) }}
              </NvMobileTag>
              <NvMobileTag
                v-for="reason in task.blockReasons ?? []"
                :key="reason"
                size="sm"
                variant="warning"
              >
                {{ blockReasonLabel(reason) }}
              </NvMobileTag>
            </div>
          </template>
        </NvListRow>
      </div>

      <div
        v-if="tasks.length > 0"
        ref="loadMoreSentinel"
        data-testid="load-more-sentinel"
        class="flex min-h-12 items-center justify-center py-3 text-sm text-muted-foreground"
      >
        {{ loadingMore ? '加载中…' : hasMore ? '继续上滑加载' : '没有更多了' }}
      </div>
    </NvPullRefresh>

    <NvBottomSheet
      :open="actionSheetOpen"
      :title="selectedTask ? taskTitle(selectedTask) : `${actionLabel}任务`"
      data-testid="task-action-sheet"
      @update:open="(open) => !open && closeSheet()"
    >
      <div v-if="selectedTask" class="space-y-4 px-4 pb-5">
        <div class="rounded-xl border border-border bg-muted/30 p-3 text-sm text-foreground">
          <p>{{ taskSubtitle(selectedTask) }}</p>
        </div>

        <div v-if="canProgress || canComplete" class="space-y-2">
          <span class="text-sm font-medium text-foreground">本次累计完成数量</span>
          <NvMobileInput
            v-model="executedQuantity"
            data-testid="executed-quantity"
            inputmode="decimal"
            :min="selectedTask.executedQuantity ?? 0"
            :max="selectedTask.plannedQuantity ?? 0"
          />
        </div>

        <div v-if="canReportException || canComplete" class="space-y-2">
          <span class="text-sm font-medium text-foreground">快捷原因</span>
          <div class="grid grid-cols-2 gap-2">
            <NvMobileButton
              v-for="reason in quickReasons"
              :key="reason.code"
              :data-testid="`difference-${reason.code}`"
              :variant="selectedReason === reason.code ? 'primary' : 'outline'"
              @click="selectedReason = reason.code"
            >
              {{ reason.label }}
            </NvMobileButton>
          </div>
        </div>

        <p v-if="validationMessage" class="text-sm text-destructive">
          {{ validationMessage }}
        </p>

        <NvMobileButton
          v-if="canStart"
          block
          variant="primary"
          :disabled="actionPending"
          @click="emitSimpleAction('start')"
        >
          开始{{ actionLabel }}
        </NvMobileButton>
        <NvMobileButton
          v-if="canProgress"
          data-testid="confirm-progress"
          block
          variant="outline"
          :disabled="actionPending"
          @click="emitQuantityAction('progress')"
        >
          保存进度
        </NvMobileButton>
        <NvMobileButton
          v-if="canComplete"
          data-testid="confirm-complete"
          block
          variant="primary"
          :disabled="actionPending"
          @click="emitQuantityAction('complete')"
        >
          完成{{ actionLabel }}
        </NvMobileButton>
        <NvMobileButton
          v-if="canReportException"
          data-testid="report-exception"
          block
          variant="outline"
          :disabled="actionPending"
          @click="emitSimpleAction('exception')"
        >
          上报异常
        </NvMobileButton>
      </div>
    </NvBottomSheet>
  </div>
</template>
