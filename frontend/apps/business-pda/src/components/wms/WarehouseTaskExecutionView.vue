<script setup lang="ts">
import TaskListShell from '@/components/task-list/TaskListShell.vue'
import WmsOperationalCandidatePicker from '@/components/wms/WmsOperationalCandidatePicker.vue'
import { PDA_WAREHOUSE_TASK_STATUS_OPTIONS } from '@/data/wmsReference'
import { warehouseTaskStatusLabel } from '@nerv-iip/business-core'
import {
  NvBottomSheet,
  NvCell,
  NvListRow,
  NvMobileButton,
  NvMobileDropdownMenu,
  NvMobileDropdownMenuItem,
  NvMobileTag,
  NvNumberKeyboard,
  NvSearchBar,
  type DropdownOption,
} from '@nerv-iip/ui-mobile'
import { computed, shallowRef, watch } from 'vue'

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

export interface WarehouseTaskCandidateOption {
  label: string
  value: string
  hint?: string
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
    updatedAt?: string | null
    currentPrincipalId?: string
    status?: string
    scopeKey?: string
    keyword?: string
    locationCode?: string
    lotNo?: string
    locationOptions?: WarehouseTaskCandidateOption[]
    lotOptions?: WarehouseTaskCandidateOption[]
    candidateReady?: boolean
    candidateSourceLabel?: string
    candidateAsOfUtc?: string
    candidateFreshnessUtc?: string
    candidateTruncated?: boolean
    candidatePending?: boolean
    candidateError?: unknown
    candidateSearchKeyword?: string
    candidateScanOverrides?: Readonly<Partial<Record<'location' | 'lot', string>>>
    scopeOptions?: WarehouseTaskScopeOption[]
    error?: unknown
    loadMoreError?: unknown
    actionPending?: boolean
    actionUnconfirmed?: boolean
    actionConfirmedSequence?: number
  }>(),
  {
    currentPrincipalId: undefined,
    updatedAt: null,
    status: undefined,
    scopeKey: undefined,
    keyword: undefined,
    locationCode: undefined,
    lotNo: undefined,
    locationOptions: () => [],
    lotOptions: () => [],
    candidateReady: false,
    candidateSourceLabel: '当前范围仓储作业记录候选',
    candidateAsOfUtc: undefined,
    candidateFreshnessUtc: undefined,
    candidateTruncated: false,
    candidatePending: false,
    candidateError: undefined,
    candidateSearchKeyword: '',
    candidateScanOverrides: () => ({}),
    scopeOptions: () => [],
    error: undefined,
    loadMoreError: undefined,
    actionPending: false,
    actionUnconfirmed: false,
    actionConfirmedSequence: 0,
  },
)

const emit = defineEmits<{
  'update:status': [value: string | undefined]
  'update:scopeKey': [value: string | undefined]
  'update:keyword': [value: string | undefined]
  'update:locationCode': [value: string | undefined]
  'update:lotNo': [value: string | undefined]
  'update:candidateSearchKeyword': [value: string]
  candidateScanOverrideChange: [target: 'location' | 'lot', value: string | undefined]
  candidateRetry: []
  refresh: []
  loadMore: []
  retry: []
  verify: []
  execute: [intent: WarehouseTaskExecutionIntent]
}>()

const selectedTask = shallowRef<WarehouseTaskExecutionItem>()
const frozenIntent = shallowRef<WarehouseTaskExecutionIntent>()
const executedQuantity = shallowRef('0')
const numberKeyboardOpen = shallowRef(false)
const selectedReason = shallowRef('')
const validationMessage = shallowRef('')

const statusOptions = PDA_WAREHOUSE_TASK_STATUS_OPTIONS
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
const filterState = computed(() => ({
  scopeKey: props.scopeKey ?? '',
  status: props.status ?? '',
  keyword: props.keyword ?? '',
  locationCode: props.locationCode ?? '',
  lotNo: props.lotNo ?? '',
  candidateSearchKeyword: props.candidateSearchKeyword,
}))
const selectedActions = computed(() => selectedTask.value?.allowedActions ?? [])
const canStart = computed(() => selectedActions.value.includes('start'))
const canProgress = computed(() => selectedActions.value.includes('progress'))
const canReportException = computed(() => selectedActions.value.includes('exception'))
const canComplete = computed(() => selectedActions.value.includes('complete'))
const actionSheetOpen = computed(() => selectedTask.value !== undefined)
const actionLocked = computed(() => props.actionPending || props.actionUnconfirmed)
const actionLabel = computed(() => (props.taskType === 'picking' ? '拣货' : '上架'))
const scanActive = computed(() => !actionSheetOpen.value && !numberKeyboardOpen.value)

function forwardCandidateScanOverride(target: 'location' | 'lot', value: string | undefined) {
  emit('candidateScanOverrideChange', target, value)
}

function restoreTaskListState(state: { filters: Record<string, unknown> }) {
  const optionalValue = (key: string) => String(state.filters[key] ?? '') || undefined
  emit('update:scopeKey', optionalValue('scopeKey'))
  emit('update:status', optionalValue('status'))
  emit('update:keyword', optionalValue('keyword'))
  emit('update:locationCode', optionalValue('locationCode'))
  emit('update:lotNo', optionalValue('lotNo'))
  emit('update:candidateSearchKeyword', String(state.filters.candidateSearchKeyword ?? ''))
}

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
  const labels: Record<string, string> = {
    TASK_TERMINAL: '任务已结束，不可继续操作',
    TASK_NOT_ASSIGNED_TO_WORK_POOL: '任务尚未分配作业池',
    TASK_ASSIGNED_TO_ANOTHER_OPERATOR: '任务已派给其他人员',
    TASK_EXECUTION_CLAIMED_BY_WCS: '任务已由 WCS 接管',
    TASK_EXECUTION_CLAIMED_BY_ANOTHER_OPERATOR: '任务正由其他人员执行',
    TASK_EXECUTION_NOT_CLAIMED: '任务尚未开始执行',
  }
  return labels[reason] ?? '当前任务不可操作'
}

function assignmentLabel(task: WarehouseTaskExecutionItem) {
  const assignee = task.assignedOperatorUserId?.trim()
  if (!assignee) return undefined
  return assignee === props.currentPrincipalId?.trim() ? '已派给我' : '已派给他人'
}

function selectTask(task: WarehouseTaskExecutionItem) {
  if ((task.allowedActions ?? []).length === 0) return
  selectedTask.value = task
  executedQuantity.value = String(task.executedQuantity ?? 0)
  numberKeyboardOpen.value = false
  selectedReason.value = ''
  validationMessage.value = ''
}

function closeSheet(force = false) {
  if (!force && actionLocked.value) return
  selectedTask.value = undefined
  frozenIntent.value = undefined
  numberKeyboardOpen.value = false
  validationMessage.value = ''
}

function openQuantityKeyboard() {
  if (!actionLocked.value) numberKeyboardOpen.value = true
}

watch(
  [
    () => props.scopeKey,
    () => props.status,
    () => props.keyword,
    () => props.locationCode,
    () => props.lotNo,
  ],
  () => closeSheet(),
)

watch(
  () => props.actionConfirmedSequence,
  (sequence, previous) => {
    if (sequence > previous) closeSheet(true)
  },
)

function dispatchIntent(intent: WarehouseTaskExecutionIntent) {
  if (props.actionPending) return
  const nextIntent = props.actionUnconfirmed && frozenIntent.value ? frozenIntent.value : intent
  frozenIntent.value = nextIntent
  emit('execute', nextIntent)
}

function retryFrozenIntent() {
  if (!frozenIntent.value || props.actionPending) return
  emit('execute', frozenIntent.value)
}

function emitSimpleAction(action: 'start' | 'exception') {
  const task = selectedTask.value
  if (!task) return
  if (action === 'exception' && !selectedReason.value) {
    validationMessage.value = '请选择异常原因'
    return
  }

  dispatchIntent({
    action,
    task,
    ...(action === 'exception'
      ? { exceptionCode: selectedReason.value, reason: selectedReasonLabel() }
      : {}),
  })
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
  dispatchIntent({
    action,
    task,
    executedQuantity: quantity,
    ...(quantity < planned ? { reason: selectedReasonLabel() } : {}),
  })
}
</script>

<template>
  <div class="flex min-h-0 flex-1 flex-col">
    <TaskListShell
      :state-key="`wms-${taskType}-tasks`"
      scope="当前授权 WMS 作业范围"
      source="WMS 仓储任务服务"
      :loaded="tasks.length"
      :total="total"
      :updated-at="updatedAt"
      :pending="pending"
      :refreshing="refreshing"
      :loading-more="loadingMore"
      :error="error"
      :load-more-error="loadMoreError"
      error-test-id="error-banner"
      failure-explanation="任务服务或任务操作未成功返回；已加载数据不会被清空。"
      :filter-state="filterState"
      empty-description="当前范围暂无任务。任务来自 WMS 派工，可切换作业范围或状态后重试。"
      @refresh="emit('refresh')"
      @load-more="emit('loadMore')"
      @retry="emit('retry')"
      @retry-load-more="emit('loadMore')"
      @restore="restoreTaskListState"
    >
      <template #filters>
        <div data-testid="wms-task-filters" class="space-y-3 px-4 py-3">
          <NvSearchBar v-model="keywordModel" :placeholder="`搜索任务号、源单号或物料`" />
          <WmsOperationalCandidatePicker
            :location-code="locationCode"
            :lot-no="lotNo"
            :location-options="locationOptions"
            :lot-options="lotOptions"
            :ready="candidateReady"
            :source-label="candidateSourceLabel"
            :as-of-utc="candidateAsOfUtc"
            :freshness-utc="candidateFreshnessUtc"
            :truncated="candidateTruncated"
            :pending="candidatePending"
            :error="candidateError"
            :search-keyword="candidateSearchKeyword"
            :scan-overrides="candidateScanOverrides"
            :active="scanActive"
            @update:location-code="emit('update:locationCode', $event)"
            @update:lot-no="emit('update:lotNo', $event)"
            @update:search-keyword="emit('update:candidateSearchKeyword', $event)"
            @scan-override-change="forwardCandidateScanOverride"
            @retry="emit('candidateRetry')"
          />
          <NvMobileDropdownMenu>
            <NvMobileDropdownMenuItem
              v-if="scopeDropdownOptions.length > 0"
              v-model="scopeModel"
              title="作业范围"
              :options="scopeDropdownOptions"
            />
            <NvMobileDropdownMenuItem
              v-model="statusModel"
              title="任务状态"
              :options="statusOptions"
            />
          </NvMobileDropdownMenu>
        </div>
      </template>

      <div class="divide-y divide-border">
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
    </TaskListShell>

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
          <div class="overflow-hidden rounded-xl border border-border">
            <NvCell
              data-testid="executed-quantity"
              title="累计完成数量"
              :value="executedQuantity || '点击录入'"
              :arrow="!actionLocked"
              :aria-disabled="actionLocked"
              @click="openQuantityKeyboard"
            />
          </div>
        </div>

        <div v-if="canReportException || canComplete" class="space-y-2">
          <span class="text-sm font-medium text-foreground">快捷原因</span>
          <div class="grid grid-cols-2 gap-2">
            <NvMobileButton
              v-for="reason in quickReasons"
              :key="reason.code"
              :data-testid="`difference-${reason.code}`"
              :variant="selectedReason === reason.code ? 'primary' : 'outline'"
              :disabled="actionLocked"
              @click="selectedReason = reason.code"
            >
              {{ reason.label }}
            </NvMobileButton>
          </div>
        </div>

        <p v-if="validationMessage" class="text-sm text-destructive">
          {{ validationMessage }}
        </p>

        <div
          v-if="actionUnconfirmed"
          class="space-y-2 rounded-xl border border-warning/40 bg-warning/10 p-3"
        >
          <p class="text-sm font-medium text-foreground">提交结果待核实</p>
          <p class="text-xs text-muted-foreground">
            当前任务、动作、数量与原因已锁定。请先刷新核实，或按原内容重试。
          </p>
          <NvMobileButton
            data-testid="verify-frozen-action"
            block
            variant="outline"
            :disabled="actionPending || refreshing"
            @click="emit('verify')"
          >
            刷新核实
          </NvMobileButton>
          <NvMobileButton
            data-testid="retry-frozen-action"
            block
            variant="primary"
            :disabled="actionPending"
            @click="retryFrozenIntent"
          >
            {{ actionPending ? '提交中…' : '按原内容重试' }}
          </NvMobileButton>
        </div>

        <NvMobileButton
          v-else-if="canStart"
          block
          variant="primary"
          :disabled="actionPending"
          @click="emitSimpleAction('start')"
        >
          开始{{ actionLabel }}
        </NvMobileButton>
        <NvMobileButton
          v-if="!actionUnconfirmed && canProgress"
          data-testid="confirm-progress"
          block
          variant="outline"
          :disabled="actionPending"
          @click="emitQuantityAction('progress')"
        >
          保存进度
        </NvMobileButton>
        <NvMobileButton
          v-if="!actionUnconfirmed && canComplete"
          data-testid="confirm-complete"
          block
          variant="primary"
          :disabled="actionPending"
          @click="emitQuantityAction('complete')"
        >
          完成{{ actionLabel }}
        </NvMobileButton>
        <NvMobileButton
          v-if="!actionUnconfirmed && canReportException"
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

    <NvNumberKeyboard
      v-model="executedQuantity"
      v-model:show="numberKeyboardOpen"
      title="录入累计完成数量"
      extra-key="."
    />
  </div>
</template>
