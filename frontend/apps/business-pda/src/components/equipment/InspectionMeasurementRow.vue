<script setup lang="ts">
import { measurementOutOfTolerance, type MeasurementDraftRow } from '@nerv-iip/business-core'
import { NvCell, NvMobileButton, NvMobileTag } from '@nerv-iip/ui-mobile'
import { computed } from 'vue'

/** 点检测量值单行（数值 / 上下限走数字键盘，只读触发防系统键盘弹出）。 */
export interface MeasurementFormRow extends MeasurementDraftRow {
  id: number
}

const props = defineProps<{
  row: MeasurementFormRow
}>()

// Props Down / Events Up：文本字段（特性 / 单位）经类型化事件回吐由父级修改，
// 与数值/移除的 emit 数据流一致（不直接改 row prop 的嵌套字段）。
const emit = defineEmits<{
  openKeyboard: [field: 'measuredValue' | 'lowerSpecLimit' | 'upperSpecLimit']
  updateText: [field: 'characteristicCode' | 'uomCode', value: string]
  remove: []
}>()

// 超差即时判定（复用 business-core 共享口径，与 Console 一致）。
const outOfTolerance = computed(() => measurementOutOfTolerance(props.row))

// 上下限可为空（null）→ 参数类型须容纳可空，避免隐式 any / 断言。
function cellText(value: string | number | null | undefined): string {
  return String(value ?? '').trim() === '' ? '点击录入' : String(value)
}

function specRangeText(): string {
  const lo = String(props.row.lowerSpecLimit ?? '').trim()
  const hi = String(props.row.upperSpecLimit ?? '').trim()
  const unit = String(props.row.uomCode ?? '').trim()
  const suffix = unit ? ` ${unit}` : ''
  if (!lo && !hi) return `不限${suffix}`
  return `${lo || '-∞'} ~ ${hi || '+∞'}${suffix}`
}
</script>

<template>
  <div
    data-testid="measurement-row"
    class="space-y-3 rounded-lg border p-3"
    :class="outOfTolerance ? 'border-destructive/60 bg-destructive/10' : 'border-border bg-card'"
  >
    <!-- 特性 + 单位（文本录入；数值类字段全部走数字键盘） -->
    <div class="grid grid-cols-1 gap-2 sm:grid-cols-2">
      <label class="space-y-1 text-xs text-muted-foreground">
        <span>特性</span>
        <input
          :value="row.characteristicCode"
          data-testid="measurement-characteristic"
          class="min-h-touch w-full rounded-lg border border-border bg-background px-3 text-base text-foreground"
          autocomplete="off"
          @input="
            emit('updateText', 'characteristicCode', ($event.target as HTMLInputElement).value)
          "
        />
      </label>
      <label class="space-y-1 text-xs text-muted-foreground">
        <span>单位</span>
        <input
          :value="row.uomCode"
          data-testid="measurement-uom"
          class="min-h-touch w-full rounded-lg border border-border bg-background px-3 text-base text-foreground"
          autocomplete="off"
          @input="emit('updateText', 'uomCode', ($event.target as HTMLInputElement).value)"
        />
      </label>
    </div>

    <!-- 测量值：大键数字键盘（只读触发） → 超差红警示 -->
    <NvCell
      data-testid="measurement-value"
      class="overflow-hidden rounded-lg border"
      :class="outOfTolerance ? 'border-destructive' : 'border-border'"
      arrow
      title="测量值"
      @click="emit('openKeyboard', 'measuredValue')"
    >
      <template #value>
        <span
          data-testid="measurement-value-text"
          :class="[
            String(row.measuredValue ?? '').trim() === ''
              ? 'text-muted-foreground'
              : outOfTolerance
                ? 'font-semibold text-destructive'
                : 'text-foreground',
          ]"
        >
          {{ cellText(row.measuredValue) }}
        </span>
      </template>
    </NvCell>

    <!-- 规格公差（上下限走键盘） -->
    <div class="grid grid-cols-2 gap-2">
      <NvCell
        data-testid="measurement-lower"
        class="overflow-hidden rounded-lg border border-border"
        arrow
        title="下限"
        @click="emit('openKeyboard', 'lowerSpecLimit')"
      >
        <template #value>
          <span
            :class="
              String(row.lowerSpecLimit ?? '').trim() === ''
                ? 'text-muted-foreground'
                : 'text-foreground'
            "
          >
            {{ cellText(row.lowerSpecLimit) }}
          </span>
        </template>
      </NvCell>
      <NvCell
        data-testid="measurement-upper"
        class="overflow-hidden rounded-lg border border-border"
        arrow
        title="上限"
        @click="emit('openKeyboard', 'upperSpecLimit')"
      >
        <template #value>
          <span
            :class="
              String(row.upperSpecLimit ?? '').trim() === ''
                ? 'text-muted-foreground'
                : 'text-foreground'
            "
          >
            {{ cellText(row.upperSpecLimit) }}
          </span>
        </template>
      </NvCell>
    </div>

    <div class="flex items-center justify-between text-xs text-muted-foreground">
      <span>规格公差</span>
      <span data-testid="spec-range" class="text-foreground">{{ specRangeText() }}</span>
    </div>

    <!-- 超差即时警示 -->
    <div v-if="outOfTolerance" class="flex items-center gap-2">
      <NvMobileTag variant="danger" data-testid="out-of-tolerance">超差 ⚠</NvMobileTag>
      <span class="text-sm font-medium text-destructive">测量值越出规格公差</span>
    </div>

    <NvMobileButton
      variant="outline"
      class="w-full"
      data-testid="remove-measurement"
      @click="emit('remove')"
    >
      移除
    </NvMobileButton>
  </div>
</template>
