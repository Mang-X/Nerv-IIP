<script setup lang="ts">
import RetryableListError from '@/components/RetryableListError.vue'
import QualityCharacteristicPicker from '@/components/quality/QualityCharacteristicPicker.vue'
import type { AuthoritativeInspectionResult } from '@/composables/useInspectionExecution'
import { useInspectionExecution } from '@/composables/useInspectionExecution'
import type {
  BusinessConsoleInspectionPlanCharacteristicItem,
  BusinessConsoleQualityInspectionTaskItem,
  BusinessConsoleQualityReasonItem,
} from '@nerv-iip/api-client'
import { characteristicRowOutOfTolerance, inspectionTaskSourceTypeLabel } from '@nerv-iip/business-core'
import {
  NvMobileButton,
  NvMobileInput,
  NvMobileTag,
  NvNumberKeyboard,
  NvPicker,
  type PickerOption,
} from '@nerv-iip/ui-mobile'
import { computed, reactive, toRef } from 'vue'

type Task = BusinessConsoleQualityInspectionTaskItem
type PlanCharacteristic = BusinessConsoleInspectionPlanCharacteristicItem

const props = defineProps<{
  task: Task
  planCharacteristics: PlanCharacteristic[]
  planCharacteristicsPending: boolean
  planCharacteristicsError: unknown
  planLabel: string
  reasonCodes: BusinessConsoleQualityReasonItem[]
  submitInspection: Parameters<typeof useInspectionExecution>[0]['submitInspection']
  submitPending: boolean
}>()
const emit = defineEmits<{
  back: []
  submitted: [result: AuthoritativeInspectionResult]
  failed: [message: string]
  refreshCharacteristics: []
}>()

const execution = useInspectionExecution({
  planCharacteristics: toRef(props, 'planCharacteristics'),
  submitInspection: props.submitInspection,
})
const {
  rows,
  dispositionReason,
  missingRequiredCodes,
  overallVerdict,
  dispositionRequired,
  canSubmit,
  addCharacteristic,
  addAllCharacteristics,
  removeRow,
} = execution

const addedCodes = computed(() => new Set(rows.map((r) => r.characteristicCode)))
const availableCharacteristics = computed<PlanCharacteristic[]>(() =>
  props.planCharacteristics.filter(
    (c) => c.characteristicCode && !addedCodes.value.has(c.characteristicCode),
  ),
)

// --- 特性选择器（可选可搜）---
const charPicker = reactive({ open: false })
function openCharPicker() {
  charPicker.open = true
}

// --- 数字键盘（计量测量值 / 计数不良数共用）---
const keyboard = reactive<{
  show: boolean
  rowId: number | null
  field: 'measuredValue' | 'defectQuantity'
}>({ show: false, rowId: null, field: 'measuredValue' })
const keyboardValue = computed<string>({
  get: () => {
    const row = rows.find((r) => r.id === keyboard.rowId)
    return row ? String(row[keyboard.field] ?? '') : ''
  },
  set: (value) => {
    const row = rows.find((r) => r.id === keyboard.rowId)
    if (row) row[keyboard.field] = value
  },
})
const keyboardExtraKey = computed(() => (keyboard.field === 'measuredValue' ? '.' : ''))
function openKeyboard(rowId: number, field: 'measuredValue' | 'defectQuantity') {
  keyboard.rowId = rowId
  keyboard.field = field
  keyboard.show = true
}

// --- 原因码 Picker（计数不合格）---
const reasonOptions = computed<PickerOption[]>(() =>
  props.reasonCodes
    .map((r) => ({ label: r.reasonName ?? r.reasonCode ?? '', value: r.reasonCode ?? '' }))
    .filter((o) => o.value !== ''),
)
const reasonPicker = reactive<{ open: boolean; rowId: number | null }>({ open: false, rowId: null })
const reasonPickerValue = computed<string>({
  get: () => rows.find((r) => r.id === reasonPicker.rowId)?.defectReason ?? '',
  set: (value) => {
    const row = rows.find((r) => r.id === reasonPicker.rowId)
    if (row) row.defectReason = value
  },
})
function openReasonPicker(rowId: number) {
  reasonPicker.rowId = rowId
  reasonPicker.open = true
}
function reasonLabel(code: string) {
  return reasonOptions.value.find((o) => o.value === code)?.label ?? code
}

function setCountResult(rowId: number, value: 'pass' | 'fail') {
  const row = rows.find((r) => r.id === rowId)
  if (!row) return
  row.countResult = value
  if (value === 'pass') {
    row.defectReason = ''
    row.defectQuantity = ''
  }
}

function specRangeText(row: (typeof rows)[number]) {
  const lo = row.lowerSpecLimit === '' ? null : row.lowerSpecLimit
  const hi = row.upperSpecLimit === '' ? null : row.upperSpecLimit
  const unit = row.uomCode ? ` ${row.uomCode}` : ''
  if (lo === null && hi === null) return `不限${unit}`
  return `${lo ?? '-∞'} ~ ${hi ?? '+∞'}${unit}`
}

function isOverdue() {
  if (!props.task.dueAtUtc) return false
  const due = new Date(props.task.dueAtUtc).getTime()
  return Number.isFinite(due) && due < Date.now()
}
function dueText(iso?: string) {
  if (!iso) return ''
  const d = new Date(iso)
  return Number.isNaN(d.getTime()) ? '' : d.toLocaleString('zh-CN')
}

async function submit() {
  if (!props.task.inspectionTaskId || !canSubmit.value || props.submitPending) return
  keyboard.show = false
  try {
    const result = await execution.submit(props.task.inspectionTaskId)
    emit('submitted', result)
  } catch (e) {
    emit('failed', e instanceof Error ? e.message : '提交失败，请检查网络后重试。')
  }
}

// 暴露 submit：结果页「重试」在执行态保留的情况下直接重提（提交端天然幂等）。
defineExpose({ submit })
</script>

<template>
  <div class="space-y-4 p-4">
    <!-- 任务上下文（常显，防错检）-->
    <section class="space-y-1 rounded-lg border border-border bg-card p-4" data-testid="task-context">
      <div class="flex items-center gap-2">
        <NvMobileTag variant="default">
          {{ inspectionTaskSourceTypeLabel(task.sourceType) }}
        </NvMobileTag>
        <NvMobileTag v-if="isOverdue()" variant="danger">超期</NvMobileTag>
      </div>
      <p class="text-base font-semibold text-foreground">{{ task.skuCode ?? '未知物料' }}</p>
      <dl class="space-y-1 text-sm">
        <div v-if="task.sourceDocumentId" class="flex items-baseline justify-between gap-4">
          <dt class="shrink-0 whitespace-nowrap text-muted-foreground">来源单据</dt>
          <dd class="min-w-0 truncate text-right text-foreground">{{ task.sourceDocumentId }}</dd>
        </div>
        <div v-if="task.quantity != null" class="flex items-baseline justify-between gap-4">
          <dt class="shrink-0 whitespace-nowrap text-muted-foreground">数量</dt>
          <dd class="min-w-0 truncate text-right text-foreground">
            {{ task.quantity }}{{ task.uomCode ?? '' }}
          </dd>
        </div>
        <div v-if="planLabel" class="flex items-baseline justify-between gap-4">
          <dt class="shrink-0 whitespace-nowrap text-muted-foreground">检验计划</dt>
          <dd class="min-w-0 truncate text-right text-foreground">{{ planLabel }}</dd>
        </div>
        <div v-if="task.batchNo" class="flex items-baseline justify-between gap-4">
          <dt class="shrink-0 whitespace-nowrap text-muted-foreground">批次</dt>
          <dd class="min-w-0 truncate text-right text-foreground">{{ task.batchNo }}</dd>
        </div>
        <div v-if="task.serialNo" class="flex items-baseline justify-between gap-4">
          <dt class="shrink-0 whitespace-nowrap text-muted-foreground">序列号</dt>
          <dd class="min-w-0 truncate text-right text-foreground">{{ task.serialNo }}</dd>
        </div>
        <div v-if="task.dueAtUtc" class="flex items-baseline justify-between gap-4">
          <dt class="shrink-0 whitespace-nowrap text-muted-foreground">应检至</dt>
          <dd
            class="min-w-0 truncate text-right text-foreground"
            :class="isOverdue() ? 'text-destructive' : undefined"
          >
            {{ dueText(task.dueAtUtc) }}
          </dd>
        </div>
      </dl>
    </section>

    <!-- 特性结果行 -->
    <section class="space-y-3">
      <h2 class="text-sm font-medium text-muted-foreground">逐特性录结果</h2>

      <div
        v-for="row in rows"
        :key="row.id"
        data-testid="char-row"
        class="space-y-3 rounded-lg border p-3"
        :class="
          characteristicRowOutOfTolerance(row)
            ? 'border-destructive/60 bg-destructive/10'
            : 'border-border bg-card'
        "
      >
        <div class="flex items-center justify-between gap-2">
          <div class="flex min-w-0 items-center gap-2">
            <NvMobileTag :variant="row.kind === 'measured' ? 'default' : 'warning'">
              {{ row.kind === 'measured' ? '计量' : '计数' }}
            </NvMobileTag>
            <span data-testid="char-name" class="truncate text-base font-medium text-foreground">
              {{ row.name || row.characteristicCode }}
            </span>
            <NvMobileTag v-if="row.required" variant="brand">必检</NvMobileTag>
          </div>
          <NvMobileButton
            v-if="!row.required"
            variant="text"
            size="sm"
            data-testid="remove-char"
            @click="removeRow(row.id)"
          >
            移除
          </NvMobileButton>
        </div>

        <!-- 计量特性：测量值（数字键盘）；单位/公差来自计划（只读）→ 超差红警示 -->
        <template v-if="row.kind === 'measured'">
          <label class="block space-y-1">
            <span class="text-xs text-muted-foreground">测量值{{ row.uomCode ? `（${row.uomCode}）` : '' }}</span>
            <button
              type="button"
              data-testid="measured-value"
              class="min-h-touch flex w-full items-center rounded-lg border bg-background px-3 text-base text-foreground"
              :class="characteristicRowOutOfTolerance(row) ? 'border-destructive' : 'border-border'"
              @click="openKeyboard(row.id, 'measuredValue')"
            >
              {{ row.measuredValue === '' ? '点击录入' : row.measuredValue }}
            </button>
          </label>
          <div class="flex items-center justify-between text-xs text-muted-foreground">
            <span>规格公差</span>
            <span data-testid="spec-range" class="text-foreground">{{ specRangeText(row) }}</span>
          </div>
          <p
            v-if="characteristicRowOutOfTolerance(row)"
            data-testid="out-of-tolerance"
            class="text-sm font-medium text-destructive"
          >
            超差：测量值越出规格公差
          </p>
        </template>

        <!-- 计数特性：合格 / 不合格 + 原因码 + 不良数 -->
        <template v-else>
          <div class="grid grid-cols-2 gap-2">
            <NvMobileButton
              :variant="row.countResult === 'pass' ? 'primary' : 'outline'"
              data-testid="count-pass"
              @click="setCountResult(row.id, 'pass')"
            >
              合格
            </NvMobileButton>
            <NvMobileButton
              :variant="row.countResult === 'fail' ? 'danger' : 'outline'"
              data-testid="count-fail"
              @click="setCountResult(row.id, 'fail')"
            >
              不合格
            </NvMobileButton>
          </div>
          <template v-if="row.countResult === 'fail'">
            <label class="block space-y-1">
              <span class="text-xs text-muted-foreground">原因码</span>
              <button
                type="button"
                data-testid="pick-reason"
                class="min-h-touch flex w-full items-center rounded-lg border border-border bg-background px-3 text-base text-foreground"
                @click="openReasonPicker(row.id)"
              >
                {{ row.defectReason ? reasonLabel(row.defectReason) : '选择原因码' }}
              </button>
            </label>
            <label class="block space-y-1">
              <span class="text-xs text-muted-foreground">不良数（可选）</span>
              <button
                type="button"
                data-testid="defect-qty"
                class="min-h-touch flex w-full items-center rounded-lg border border-border bg-background px-3 text-base text-foreground"
                @click="openKeyboard(row.id, 'defectQuantity')"
              >
                {{ row.defectQuantity === '' ? '点击录入' : row.defectQuantity }}
              </button>
            </label>
          </template>
        </template>
      </div>

      <!-- 计划特性：错误态可重试（区别于真实空计划）-->
      <RetryableListError
        v-if="planCharacteristicsError"
        :error="planCharacteristicsError"
        :pending="planCharacteristicsPending"
        fallback="检验计划特性加载失败，请稍后重试。"
        test-id="plan-characteristics-error"
        @retry="() => emit('refreshCharacteristics')"
      />
      <div
        v-else-if="planCharacteristicsPending"
        class="px-4 py-4 text-center text-sm text-muted-foreground"
      >
        加载检验计划特性中…
      </div>
      <div
        v-else-if="planCharacteristics.length === 0"
        data-testid="no-plan-characteristics"
        class="rounded-lg border border-dashed border-border bg-card px-4 py-6 text-center text-sm text-muted-foreground"
      >
        该检验计划未配置检验特性。
      </div>
      <template v-else>
        <div class="grid grid-cols-2 gap-2">
          <NvMobileButton
            variant="primary"
            data-testid="add-characteristic"
            :disabled="availableCharacteristics.length === 0"
            @click="openCharPicker"
          >
            + 添加检验特性
          </NvMobileButton>
          <NvMobileButton
            variant="outline"
            data-testid="add-all-characteristics"
            :disabled="availableCharacteristics.length === 0"
            @click="addAllCharacteristics"
          >
            全部添加
          </NvMobileButton>
        </div>
        <p
          v-if="missingRequiredCodes.length > 0"
          data-testid="missing-required"
          class="text-sm text-destructive"
        >
          还有必检特性未录入：{{ missingRequiredCodes.join('、') }}
        </p>
        <p v-else-if="rows.length === 0" class="text-sm text-muted-foreground">
          从检验计划中选择检验特性并录入结果。
        </p>
      </template>
    </section>

    <!-- 处置原因（判不合格时必填）-->
    <label v-if="dispositionRequired && rows.length > 0" class="block space-y-1">
      <span class="text-sm font-medium text-destructive">处置原因（不合格必填）</span>
      <NvMobileInput
        v-model="dispositionReason"
        data-testid="disposition-reason"
        placeholder="如 外径超差且外观不良，判退"
      />
    </label>

    <NvMobileButton
      :variant="overallVerdict === 'fail' ? 'danger' : 'primary'"
      size="lg"
      block
      data-testid="submit"
      :disabled="!canSubmit || submitPending"
      @click="submit"
    >
      {{
        submitPending
          ? '提交中…'
          : overallVerdict === 'fail'
            ? '提交（判不合格）'
            : '提交检验结果'
      }}
    </NvMobileButton>

    <NvNumberKeyboard
      v-model="keyboardValue"
      v-model:show="keyboard.show"
      title="录入数值"
      :extra-key="keyboardExtraKey"
    />
    <NvPicker
      v-model="reasonPickerValue"
      v-model:open="reasonPicker.open"
      :options="reasonOptions"
      title="选择原因码"
    />
    <QualityCharacteristicPicker
      v-model:open="charPicker.open"
      :characteristics="availableCharacteristics"
      @pick="addCharacteristic"
    />
  </div>
</template>
