<script setup lang="ts">
import type { CapturedPhoto } from '@/composables/useInspectionPhotoCapture'
import { measurementOutOfTolerance, type MeasurementDraftRow } from '@nerv-iip/business-core'
import { NvCell, NvMobileButton, NvMobileTag } from '@nerv-iip/ui-mobile'
import { Camera } from 'lucide-vue-next'
import { computed } from 'vue'

/** 点检测量值单行（数值 / 上下限走数字键盘，只读触发防系统键盘弹出）。 */
export interface MeasurementFormRow extends MeasurementDraftRow {
  id: number
  photos: CapturedPhoto[]
}

const props = defineProps<{
  row: MeasurementFormRow
  /** 相机能力可用时才显示拍照入口（#812：不可用则隐藏）。 */
  photoSupported: boolean
}>()

const emit = defineEmits<{
  openKeyboard: [field: 'measuredValue' | 'lowerSpecLimit' | 'upperSpecLimit']
  capturePhoto: []
  removePhoto: [photoId: number]
  remove: []
}>()

// 超差即时判定（复用 business-core 共享口径，与 Console 一致）。
const outOfTolerance = computed(() => measurementOutOfTolerance(props.row))

function cellText(value: string | number): string {
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
          @input="row.characteristicCode = ($event.target as HTMLInputElement).value"
        />
      </label>
      <label class="space-y-1 text-xs text-muted-foreground">
        <span>单位</span>
        <input
          :value="row.uomCode"
          data-testid="measurement-uom"
          class="min-h-touch w-full rounded-lg border border-border bg-background px-3 text-base text-foreground"
          autocomplete="off"
          @input="row.uomCode = ($event.target as HTMLInputElement).value"
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

    <!-- 拍照取证（相机可用时；后端图片契约就绪前仅本地留存，见 useInspectionPhotoCapture 预留说明） -->
    <div v-if="photoSupported" class="space-y-2">
      <div class="flex flex-wrap gap-2">
        <div
          v-for="photo in row.photos"
          :key="photo.id"
          data-testid="measurement-photo"
          class="relative size-16 overflow-hidden rounded-lg border border-border bg-muted"
        >
          <img :src="photo.url" :alt="photo.name" class="size-full object-cover" />
          <button
            type="button"
            data-testid="remove-photo"
            class="absolute right-0 top-0 grid size-6 place-items-center rounded-bl-lg bg-black/55 text-white"
            aria-label="移除照片"
            @click="emit('removePhoto', photo.id)"
          >
            ×
          </button>
        </div>
        <button
          type="button"
          data-testid="capture-photo"
          class="grid size-16 place-items-center rounded-lg border border-dashed border-border bg-card text-muted-foreground"
          aria-label="拍照取证"
          @click="emit('capturePhoto')"
        >
          <Camera class="size-6" aria-hidden="true" />
        </button>
      </div>
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
