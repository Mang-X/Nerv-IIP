<script setup lang="ts">
/**
 * 交班明细录入（在制清点 / 未完工单 / 遗留问题）。
 *
 * 三段都是「填草稿 → 添加成行 → 可删行」，这是 PDA 上唯一能在窄屏里稳住的形态：
 * 不在卡片内用 `sm:` 之类的**视口**断点排多列（容器只有一屏宽，视口断点会让文字竖排成
 * 单字、控件顶出容器）。
 *
 * 校验判据直接对齐 MES 域构造器（`ShiftHandoverWipItem` / `ShiftHandoverUnfinishedWorkOrder` /
 * `ShiftHandoverOpenIssue`），不是拍脑袋：
 * - 在制清点：工单号必填 ≤100，数量 ≥ 0；
 * - 未完工单：工单号必填 ≤100，计划数量 > 0，完成数量 ≥ 0 且 **严格小于** 计划数量
 *   （域方法原话「完成数量已达到计划数量的工单不是未完工单」），状态必填 ≤30；
 * - 遗留问题：类别/严重度取闭合词表，描述必填 ≤1000，关联单据可空 ≤100。
 * 本地先挡一道是为了让操作工当场看见原因，服务端那道守卫仍然是权威。
 */
import {
  SHIFT_HANDOVER_ISSUE_CATEGORY_CODES,
  SHIFT_HANDOVER_ISSUE_SEVERITY_CODES,
  SHIFT_HANDOVER_UNFINISHED_WORK_ORDER_STATUS_OPTIONS,
  shiftHandoverIssueCategoryLabel,
  shiftHandoverIssueSeverityLabel,
  workOrderStatusLabel,
} from '@nerv-iip/business-core'
import {
  NvMobileButton,
  NvMobileCollapse,
  NvMobileInput,
  NvMobileRadioGroup,
  NvMobileRadioItem,
  NvMobileTag,
  NvNumberKeyboard,
  NvPicker,
  type PickerOption,
} from '@nerv-iip/ui-mobile'
import { computed, reactive, ref } from 'vue'
import type {
  ShiftHandoverOpenIssue,
  ShiftHandoverUnfinishedWorkOrder,
  ShiftHandoverWipItem,
} from '@/composables/useBusinessShiftHandover'

const wipItems = defineModel<ShiftHandoverWipItem[]>('wipItems', { required: true })
const unfinishedWorkOrders = defineModel<ShiftHandoverUnfinishedWorkOrder[]>(
  'unfinishedWorkOrders',
  { required: true },
)
const openIssues = defineModel<ShiftHandoverOpenIssue[]>('openIssues', { required: true })

const MAX_BUSINESS_ID = 100
const MAX_STATUS = 30
const MAX_DESCRIPTION = 1000

const wipDraft = reactive({ workOrderId: '', operationTaskId: '', quantity: '' })
const unfinishedDraft = reactive({
  workOrderId: '',
  plannedQuantity: '',
  completedQuantity: '',
  workOrderStatus: '',
})
const issueDraft = reactive({ category: '', severity: '', description: '', referenceId: '' })

const wipOpen = ref(true)
const unfinishedOpen = ref(false)
const issuesOpen = ref(false)

// --- 数量录入：单例数字键盘（AGENTS 硬性规则 5：数值录入不弹系统键盘）---
type QuantityField = 'wip.quantity' | 'unfinished.planned' | 'unfinished.completed'
const keyboard = reactive<{ show: boolean; field: QuantityField }>({
  show: false,
  field: 'wip.quantity',
})
const keyboardTitle = computed(
  () =>
    ({
      'wip.quantity': '在制数量',
      'unfinished.planned': '计划数量',
      'unfinished.completed': '完成数量',
    })[keyboard.field],
)
const keyboardValue = computed<string>({
  get: () => {
    if (keyboard.field === 'wip.quantity') return wipDraft.quantity
    if (keyboard.field === 'unfinished.planned') return unfinishedDraft.plannedQuantity
    return unfinishedDraft.completedQuantity
  },
  set: (value) => {
    if (keyboard.field === 'wip.quantity') wipDraft.quantity = value
    else if (keyboard.field === 'unfinished.planned') unfinishedDraft.plannedQuantity = value
    else unfinishedDraft.completedQuantity = value
  },
})
function openKeyboard(field: QuantityField) {
  keyboard.field = field
  keyboard.show = true
}

/**
 * 工单状态 Picker 用**写面值域**，不是读面展示表。
 *
 * 读面 `WORK_ORDER_STATUS_LABELS` 是历史拼写的并集：`InProgress` 和 `Started` 都显示
 * 「生产中」，屏上会并排出现两条无法区分的选项；它还含 `Completed` / `Closed`，
 * 而「未完工单」的定义里就排除了终态。两件事都不是显示问题，是值域问题。
 */
const statusOptions = computed<PickerOption[]>(() =>
  SHIFT_HANDOVER_UNFINISHED_WORK_ORDER_STATUS_OPTIONS.map((option) => ({
    label: option.label,
    value: option.code,
  })),
)
const statusPickerOpen = ref(false)

function parseQuantity(raw: string): number | undefined {
  const trimmed = raw.trim()
  if (!/^\d+(\.\d+)?$/.test(trimmed)) return undefined
  const value = Number(trimmed)
  return Number.isFinite(value) ? value : undefined
}

const wipError = computed(() => {
  const workOrderId = wipDraft.workOrderId.trim()
  if (!workOrderId) return '请填写工单号。'
  if (workOrderId.length > MAX_BUSINESS_ID) return `工单号不能超过 ${MAX_BUSINESS_ID} 个字符。`
  if (wipDraft.operationTaskId.trim().length > MAX_BUSINESS_ID)
    return `工序任务号不能超过 ${MAX_BUSINESS_ID} 个字符。`
  if (parseQuantity(wipDraft.quantity) === undefined) return '请填写在制数量（不能为负数）。'
  return ''
})

const unfinishedError = computed(() => {
  const workOrderId = unfinishedDraft.workOrderId.trim()
  if (!workOrderId) return '请填写工单号。'
  if (workOrderId.length > MAX_BUSINESS_ID) return `工单号不能超过 ${MAX_BUSINESS_ID} 个字符。`
  const planned = parseQuantity(unfinishedDraft.plannedQuantity)
  if (planned === undefined || planned <= 0) return '计划数量必须为正数。'
  const completed = parseQuantity(unfinishedDraft.completedQuantity)
  if (completed === undefined) return '请填写完成数量（不能为负数）。'
  if (completed >= planned) return '完成数量已达到计划数量的工单不是未完工单。'
  if (!unfinishedDraft.workOrderStatus.trim()) return '请选择工单状态。'
  if (unfinishedDraft.workOrderStatus.trim().length > MAX_STATUS)
    return `工单状态不能超过 ${MAX_STATUS} 个字符。`
  return ''
})

const issueError = computed(() => {
  if (!issueDraft.category) return '请选择问题类别。'
  if (!issueDraft.severity) return '请选择严重度。'
  const description = issueDraft.description.trim()
  if (!description) return '请填写问题描述。'
  if (description.length > MAX_DESCRIPTION) return `问题描述不能超过 ${MAX_DESCRIPTION} 个字符。`
  if (issueDraft.referenceId.trim().length > MAX_BUSINESS_ID)
    return `关联单据不能超过 ${MAX_BUSINESS_ID} 个字符。`
  return ''
})

const showWipError = ref(false)
const showUnfinishedError = ref(false)
const showIssueError = ref(false)

function addWipItem() {
  showWipError.value = true
  if (wipError.value) return
  const operationTaskId = wipDraft.operationTaskId.trim()
  wipItems.value = [
    ...wipItems.value,
    {
      workOrderId: wipDraft.workOrderId.trim(),
      // 服务端把 null / 空串都当「按工单登记」，这里统一不发空串。
      ...(operationTaskId ? { operationTaskId } : {}),
      quantity: parseQuantity(wipDraft.quantity)!,
    },
  ]
  wipDraft.workOrderId = ''
  wipDraft.operationTaskId = ''
  wipDraft.quantity = ''
  showWipError.value = false
  keyboard.show = false
}

function addUnfinishedWorkOrder() {
  showUnfinishedError.value = true
  if (unfinishedError.value) return
  unfinishedWorkOrders.value = [
    ...unfinishedWorkOrders.value,
    {
      workOrderId: unfinishedDraft.workOrderId.trim(),
      plannedQuantity: parseQuantity(unfinishedDraft.plannedQuantity)!,
      completedQuantity: parseQuantity(unfinishedDraft.completedQuantity)!,
      workOrderStatus: unfinishedDraft.workOrderStatus.trim(),
    },
  ]
  unfinishedDraft.workOrderId = ''
  unfinishedDraft.plannedQuantity = ''
  unfinishedDraft.completedQuantity = ''
  unfinishedDraft.workOrderStatus = ''
  showUnfinishedError.value = false
  keyboard.show = false
}

function addOpenIssue() {
  showIssueError.value = true
  if (issueError.value) return
  const referenceId = issueDraft.referenceId.trim()
  openIssues.value = [
    ...openIssues.value,
    {
      category: issueDraft.category,
      severity: issueDraft.severity,
      description: issueDraft.description.trim(),
      ...(referenceId ? { referenceId } : {}),
    },
  ]
  issueDraft.category = ''
  issueDraft.severity = ''
  issueDraft.description = ''
  issueDraft.referenceId = ''
  showIssueError.value = false
}

function removeWipItem(index: number) {
  wipItems.value = wipItems.value.filter((_, i) => i !== index)
}
function removeUnfinishedWorkOrder(index: number) {
  unfinishedWorkOrders.value = unfinishedWorkOrders.value.filter((_, i) => i !== index)
}
function removeOpenIssue(index: number) {
  openIssues.value = openIssues.value.filter((_, i) => i !== index)
}
</script>

<template>
  <div class="space-y-3">
    <!-- 在制清点 -->
    <NvMobileCollapse
      v-model:open="wipOpen"
      class="rounded-xl border border-border"
      data-testid="wip-section"
    >
      <template #title>
        <span class="text-[15px] font-medium text-foreground">在制清点</span>
        <NvMobileTag class="ml-2" size="sm" variant="brand">{{ wipItems.length }}</NvMobileTag>
      </template>

      <ul v-if="wipItems.length" class="mb-3 space-y-2" data-testid="wip-rows">
        <li
          v-for="(item, index) in wipItems"
          :key="`wip-${index}`"
          class="rounded-lg border border-border bg-background px-3 py-2"
        >
          <p class="text-sm font-medium text-foreground">{{ item.workOrderId }}</p>
          <p class="text-xs text-muted-foreground">
            {{ item.operationTaskId || '按工单登记' }} · 在制 {{ item.quantity }}
          </p>
          <NvMobileButton
            variant="text"
            size="sm"
            class="mt-1 px-0"
            :data-testid="`remove-wip-${index}`"
            @click="removeWipItem(index)"
            >移除</NvMobileButton
          >
        </li>
      </ul>

      <div class="space-y-2">
        <NvMobileInput v-model="wipDraft.workOrderId" placeholder="工单号（必填）" />
        <NvMobileInput v-model="wipDraft.operationTaskId" placeholder="工序任务号（可空）" />
        <button
          type="button"
          data-testid="wip-quantity-cell"
          class="min-h-touch flex w-full items-center justify-between rounded-xl border border-border bg-card px-3.5 text-[15px]"
          @click="openKeyboard('wip.quantity')"
        >
          <span class="text-muted-foreground">在制数量</span>
          <span class="font-medium text-foreground">{{ wipDraft.quantity || '点击录入' }}</span>
        </button>
        <p v-if="showWipError && wipError" role="alert" class="text-sm text-destructive">
          {{ wipError }}
        </p>
        <NvMobileButton variant="primary" block data-testid="add-wip" @click="addWipItem"
          >添加在制清点</NvMobileButton
        >
      </div>
    </NvMobileCollapse>

    <!-- 未完工单 -->
    <NvMobileCollapse
      v-model:open="unfinishedOpen"
      class="rounded-xl border border-border"
      data-testid="unfinished-section"
    >
      <template #title>
        <span class="text-[15px] font-medium text-foreground">未完工单</span>
        <NvMobileTag class="ml-2" size="sm" variant="brand">{{
          unfinishedWorkOrders.length
        }}</NvMobileTag>
      </template>

      <ul v-if="unfinishedWorkOrders.length" class="mb-3 space-y-2" data-testid="unfinished-rows">
        <li
          v-for="(item, index) in unfinishedWorkOrders"
          :key="`unfinished-${index}`"
          class="rounded-lg border border-border bg-background px-3 py-2"
        >
          <p class="text-sm font-medium text-foreground">{{ item.workOrderId }}</p>
          <p class="text-xs text-muted-foreground">
            {{ workOrderStatusLabel(item.workOrderStatus) }} · 完成 {{ item.completedQuantity }} /
            计划 {{ item.plannedQuantity }}
          </p>
          <NvMobileButton
            variant="text"
            size="sm"
            class="mt-1 px-0"
            :data-testid="`remove-unfinished-${index}`"
            @click="removeUnfinishedWorkOrder(index)"
            >移除</NvMobileButton
          >
        </li>
      </ul>

      <div class="space-y-2">
        <NvMobileInput v-model="unfinishedDraft.workOrderId" placeholder="工单号（必填）" />
        <button
          type="button"
          data-testid="unfinished-planned-cell"
          class="min-h-touch flex w-full items-center justify-between rounded-xl border border-border bg-card px-3.5 text-[15px]"
          @click="openKeyboard('unfinished.planned')"
        >
          <span class="text-muted-foreground">计划数量</span>
          <span class="font-medium text-foreground">{{
            unfinishedDraft.plannedQuantity || '点击录入'
          }}</span>
        </button>
        <button
          type="button"
          data-testid="unfinished-completed-cell"
          class="min-h-touch flex w-full items-center justify-between rounded-xl border border-border bg-card px-3.5 text-[15px]"
          @click="openKeyboard('unfinished.completed')"
        >
          <span class="text-muted-foreground">完成数量</span>
          <span class="font-medium text-foreground">{{
            unfinishedDraft.completedQuantity || '点击录入'
          }}</span>
        </button>
        <button
          type="button"
          data-testid="unfinished-status-cell"
          class="min-h-touch flex w-full items-center justify-between rounded-xl border border-border bg-card px-3.5 text-[15px]"
          @click="statusPickerOpen = true"
        >
          <span class="text-muted-foreground">工单状态</span>
          <span class="font-medium text-foreground">{{
            unfinishedDraft.workOrderStatus
              ? workOrderStatusLabel(unfinishedDraft.workOrderStatus)
              : '点击选择'
          }}</span>
        </button>
        <p
          v-if="showUnfinishedError && unfinishedError"
          role="alert"
          class="text-sm text-destructive"
        >
          {{ unfinishedError }}
        </p>
        <NvMobileButton
          variant="primary"
          block
          data-testid="add-unfinished"
          @click="addUnfinishedWorkOrder"
          >添加未完工单</NvMobileButton
        >
      </div>
    </NvMobileCollapse>

    <!-- 遗留问题 -->
    <NvMobileCollapse
      v-model:open="issuesOpen"
      class="rounded-xl border border-border"
      data-testid="issues-section"
    >
      <template #title>
        <span class="text-[15px] font-medium text-foreground">设备与质量遗留问题</span>
        <NvMobileTag class="ml-2" size="sm" variant="brand">{{ openIssues.length }}</NvMobileTag>
      </template>

      <ul v-if="openIssues.length" class="mb-3 space-y-2" data-testid="issue-rows">
        <li
          v-for="(item, index) in openIssues"
          :key="`issue-${index}`"
          class="rounded-lg border border-border bg-background px-3 py-2"
        >
          <p class="text-sm font-medium text-foreground">
            {{ shiftHandoverIssueCategoryLabel(item.category) }} ·
            {{ shiftHandoverIssueSeverityLabel(item.severity) }}
          </p>
          <p class="text-xs text-muted-foreground">{{ item.description }}</p>
          <p v-if="item.referenceId" class="text-xs text-muted-foreground">
            关联单据 {{ item.referenceId }}
          </p>
          <NvMobileButton
            variant="text"
            size="sm"
            class="mt-1 px-0"
            :data-testid="`remove-issue-${index}`"
            @click="removeOpenIssue(index)"
            >移除</NvMobileButton
          >
        </li>
      </ul>

      <div class="space-y-2">
        <p class="text-xs text-muted-foreground">问题类别</p>
        <NvMobileRadioGroup v-model="issueDraft.category" data-testid="issue-category">
          <NvMobileRadioItem
            v-for="code in SHIFT_HANDOVER_ISSUE_CATEGORY_CODES"
            :key="code"
            :value="code"
            >{{ shiftHandoverIssueCategoryLabel(code) }}</NvMobileRadioItem
          >
        </NvMobileRadioGroup>
        <p class="text-xs text-muted-foreground">严重度</p>
        <NvMobileRadioGroup v-model="issueDraft.severity" data-testid="issue-severity">
          <NvMobileRadioItem
            v-for="code in SHIFT_HANDOVER_ISSUE_SEVERITY_CODES"
            :key="code"
            :value="code"
            >{{ shiftHandoverIssueSeverityLabel(code) }}</NvMobileRadioItem
          >
        </NvMobileRadioGroup>
        <NvMobileInput v-model="issueDraft.description" placeholder="问题描述（必填）" />
        <NvMobileInput v-model="issueDraft.referenceId" placeholder="关联单据号（可空）" />
        <p v-if="showIssueError && issueError" role="alert" class="text-sm text-destructive">
          {{ issueError }}
        </p>
        <NvMobileButton variant="primary" block data-testid="add-issue" @click="addOpenIssue"
          >添加遗留问题</NvMobileButton
        >
      </div>
    </NvMobileCollapse>

    <NvPicker
      v-model="unfinishedDraft.workOrderStatus"
      v-model:open="statusPickerOpen"
      :options="statusOptions"
      title="选择工单状态"
    />
    <NvNumberKeyboard
      v-model="keyboardValue"
      v-model:show="keyboard.show"
      :title="keyboardTitle"
      extra-key="."
    />
  </div>
</template>
