<script setup lang="ts">
import type { ExecutionRow } from '@/composables/useInspectionExecution'
import { characteristicRowOutOfTolerance } from '@nerv-iip/business-core'
import { NvCell, NvMobileButton, NvMobileTag } from '@nerv-iip/ui-mobile'
import { computed } from 'vue'

const props = defineProps<{
  row: ExecutionRow
  /** 原因码 → 人读名称（父级用原因码目录解析）。 */
  reasonLabel: (code: string) => string
}>()
const emit = defineEmits<{
  openKeyboard: [field: 'measuredValue' | 'defectQuantity']
  openReasonPicker: []
  setCountResult: [value: 'pass' | 'fail']
  remove: []
}>()

const outOfTolerance = computed(() => characteristicRowOutOfTolerance(props.row))

function specRangeText() {
  const lo = props.row.lowerSpecLimit === '' ? null : props.row.lowerSpecLimit
  const hi = props.row.upperSpecLimit === '' ? null : props.row.upperSpecLimit
  const unit = props.row.uomCode ? ` ${props.row.uomCode}` : ''
  if (lo === null && hi === null) return `不限${unit}`
  return `${lo ?? '-∞'} ~ ${hi ?? '+∞'}${unit}`
}
</script>

<template>
  <div
    data-testid="char-row"
    class="space-y-3 rounded-lg border p-3"
    :class="outOfTolerance ? 'border-destructive/60 bg-destructive/10' : 'border-border bg-card'"
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
        @click="emit('remove')"
      >
        移除
      </NvMobileButton>
    </div>

    <!-- 计量特性：测量值（数字键盘）；单位/公差来自计划（只读）→ 超差红警示 -->
    <template v-if="row.kind === 'measured'">
      <NvCell
        data-testid="measured-value"
        class="overflow-hidden rounded-lg border"
        :class="outOfTolerance ? 'border-destructive' : 'border-border'"
        arrow
        :title="`测量值${row.uomCode ? `（${row.uomCode}）` : ''}`"
        @click="emit('openKeyboard', 'measuredValue')"
      >
        <template #value>
          <span :class="row.measuredValue === '' ? 'text-muted-foreground' : 'text-foreground'">
            {{ row.measuredValue === '' ? '点击录入' : row.measuredValue }}
          </span>
        </template>
      </NvCell>
      <div class="flex items-center justify-between text-xs text-muted-foreground">
        <span>规格公差</span>
        <span data-testid="spec-range" class="text-foreground">{{ specRangeText() }}</span>
      </div>
      <p v-if="outOfTolerance" data-testid="out-of-tolerance" class="text-sm font-medium text-destructive">
        超差：测量值越出规格公差
      </p>
    </template>

    <!-- 计数特性：合格 / 不合格 + 原因码 + 不良数 -->
    <template v-else>
      <div class="grid grid-cols-2 gap-2">
        <NvMobileButton
          :variant="row.countResult === 'pass' ? 'primary' : 'outline'"
          data-testid="count-pass"
          @click="emit('setCountResult', 'pass')"
        >
          合格
        </NvMobileButton>
        <NvMobileButton
          :variant="row.countResult === 'fail' ? 'danger' : 'outline'"
          data-testid="count-fail"
          @click="emit('setCountResult', 'fail')"
        >
          不合格
        </NvMobileButton>
      </div>
      <template v-if="row.countResult === 'fail'">
        <NvCell
          data-testid="pick-reason"
          class="overflow-hidden rounded-lg border border-border"
          arrow
          title="原因码"
          @click="emit('openReasonPicker')"
        >
          <template #value>
            <span :class="row.defectReason ? 'text-foreground' : 'text-muted-foreground'">
              {{ row.defectReason ? reasonLabel(row.defectReason) : '选择原因码' }}
            </span>
          </template>
        </NvCell>
        <NvCell
          data-testid="defect-qty"
          class="overflow-hidden rounded-lg border border-border"
          arrow
          title="不良数（可选）"
          @click="emit('openKeyboard', 'defectQuantity')"
        >
          <template #value>
            <span :class="row.defectQuantity === '' ? 'text-muted-foreground' : 'text-foreground'">
              {{ row.defectQuantity === '' ? '点击录入' : row.defectQuantity }}
            </span>
          </template>
        </NvCell>
      </template>
    </template>
  </div>
</template>
