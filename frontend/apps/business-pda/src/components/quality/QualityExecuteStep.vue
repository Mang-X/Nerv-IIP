<script setup lang="ts">
import RetryableListError from '@/components/RetryableListError.vue'
import QualityCharacteristicPicker from '@/components/quality/QualityCharacteristicPicker.vue'
import QualityCharacteristicRow from '@/components/quality/QualityCharacteristicRow.vue'
import QualityDispositionSubmit from '@/components/quality/QualityDispositionSubmit.vue'
import QualityTaskContextCard from '@/components/quality/QualityTaskContextCard.vue'
import type { AuthoritativeInspectionResult } from '@/composables/useInspectionExecution'
import { useInspectionExecution } from '@/composables/useInspectionExecution'
import type {
  BusinessConsoleInspectionPlanCharacteristicItem,
  BusinessConsoleQualityInspectionTaskItem,
  BusinessConsoleQualityReasonItem,
} from '@nerv-iip/api-client'
import { NvMobileButton, NvNumberKeyboard, NvPicker, type PickerOption } from '@nerv-iip/ui-mobile'
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

// --- 数字键盘（计量测量值 / 计数不良数共用单例）---
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

// --- 原因码 Picker（计数不合格）共用单例 ---
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
    <QualityTaskContextCard :task="task" :plan-label="planLabel" />

    <!-- 特性结果行 -->
    <section class="space-y-3">
      <h2 class="text-sm font-medium text-muted-foreground">逐特性录结果</h2>

      <QualityCharacteristicRow
        v-for="row in rows"
        :key="row.id"
        :row="row"
        :reason-label="reasonLabel"
        @open-keyboard="(field) => openKeyboard(row.id, field)"
        @open-reason-picker="openReasonPicker(row.id)"
        @set-count-result="(value) => setCountResult(row.id, value)"
        @remove="removeRow(row.id)"
      />

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

    <QualityDispositionSubmit
      v-model:disposition-reason="dispositionReason"
      :disposition-required="dispositionRequired"
      :has-rows="rows.length > 0"
      :overall-verdict="overallVerdict"
      :can-submit="canSubmit"
      :submit-pending="submitPending"
      @submit="submit"
    />

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
