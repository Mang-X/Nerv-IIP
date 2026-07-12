<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed, ref, watch } from 'vue'
import { CalendarIcon, ChevronLeftIcon, ChevronRightIcon } from 'lucide-vue-next'
import { Popover, PopoverContent, PopoverTrigger } from '../../ui/popover'
import { cn } from '../../../lib/utils'
import NvButton from '../button/NvButton.vue'

/**
 * Pro — date picker (YYYY-MM-DD). Self-contained month-grid calendar in a
 * popover (no @internationalized/date dep), NvButton trigger, brand selection,
 * today ring. String model, consistent with NvTimePicker.
 */
const props = withDefaults(
  defineProps<{
    modelValue?: string | null
    placeholder?: string
    disabled?: boolean
    class?: HTMLAttributes['class']
  }>(),
  { modelValue: null, placeholder: '选择日期', disabled: false },
)
const emit = defineEmits<{ 'update:modelValue': [value: string] }>()

const WEEKDAYS = ['一', '二', '三', '四', '五', '六', '日']
const open = ref(false)

function pad(n: number) {
  return String(n).padStart(2, '0')
}
function fmt(y: number, m: number, d: number) {
  return `${y}-${pad(m + 1)}-${pad(d)}`
}
const today = new Date()
const todayKey = fmt(today.getFullYear(), today.getMonth(), today.getDate())

// visible month cursor
const cursor = ref({ y: today.getFullYear(), m: today.getMonth() })

function syncCursor() {
  const v = (props.modelValue ?? '').match(/^(\d{4})-(\d{2})-(\d{2})$/)
  cursor.value = v
    ? { y: Number(v[1]), m: Number(v[2]) - 1 }
    : { y: today.getFullYear(), m: today.getMonth() }
}
watch(open, (isOpen) => isOpen && syncCursor())

const monthLabel = computed(() => `${cursor.value.y} 年 ${cursor.value.m + 1} 月`)

// 6×7 grid, Monday-first
const grid = computed(() => {
  const { y, m } = cursor.value
  const first = new Date(y, m, 1)
  const offset = (first.getDay() + 6) % 7 // Mon=0
  const start = new Date(y, m, 1 - offset)
  return Array.from({ length: 42 }, (_, i) => {
    const d = new Date(start.getFullYear(), start.getMonth(), start.getDate() + i)
    const key = fmt(d.getFullYear(), d.getMonth(), d.getDate())
    return {
      key,
      day: d.getDate(),
      outside: d.getMonth() !== m,
      today: key === todayKey,
      selected: key === props.modelValue,
    }
  })
})

function shift(delta: number) {
  const d = new Date(cursor.value.y, cursor.value.m + delta, 1)
  cursor.value = { y: d.getFullYear(), m: d.getMonth() }
}
function pick(key: string) {
  emit('update:modelValue', key)
  open.value = false
}
function pickToday() {
  pick(todayKey)
}
</script>

<template>
  <Popover v-model:open="open">
    <PopoverTrigger as-child>
      <NvButton
        variant="outline"
        :disabled="disabled"
        :class="
          cn(
            'w-48 justify-between font-normal',
            !modelValue && 'text-muted-foreground',
            props.class,
          )
        "
      >
        <template #leading
          ><CalendarIcon class="size-4 text-muted-foreground" aria-hidden="true"
        /></template>
        {{ modelValue || placeholder }}
      </NvButton>
    </PopoverTrigger>
    <PopoverContent class="w-auto p-3" align="start">
      <div class="mb-2 flex items-center justify-between">
        <button
          type="button"
          class="grid size-7 place-items-center rounded-md text-muted-foreground transition-colors hover:bg-accent hover:text-foreground"
          aria-label="上个月"
          @click="shift(-1)"
        >
          <ChevronLeftIcon class="size-4" aria-hidden="true" />
        </button>
        <span class="text-sm font-medium tabular-nums">{{ monthLabel }}</span>
        <button
          type="button"
          class="grid size-7 place-items-center rounded-md text-muted-foreground transition-colors hover:bg-accent hover:text-foreground"
          aria-label="下个月"
          @click="shift(1)"
        >
          <ChevronRightIcon class="size-4" aria-hidden="true" />
        </button>
      </div>
      <div class="grid grid-cols-7 gap-0.5">
        <span
          v-for="w in WEEKDAYS"
          :key="w"
          class="grid h-8 place-items-center text-xs text-muted-foreground"
          >{{ w }}</span
        >
        <button
          v-for="cell in grid"
          :key="cell.key"
          type="button"
          :class="
            cn(
              'nv-dp-cell grid size-8 place-items-center rounded-md text-sm tabular-nums transition-colors',
              cell.outside ? 'text-muted-foreground/40' : 'text-foreground hover:bg-accent',
              cell.today && !cell.selected && 'ring-1 ring-brand/40',
              cell.selected && 'bg-brand text-brand-foreground hover:bg-brand',
            )
          "
          @click="pick(cell.key)"
        >
          {{ cell.day }}
        </button>
      </div>
      <div class="mt-2 border-t border-border pt-2">
        <NvButton variant="ghost" size="sm" class="w-full" @click="pickToday">今天</NvButton>
      </div>
    </PopoverContent>
  </Popover>
</template>

<style scoped>
@layer nv-components {
  .nv-dp-cell {
    -webkit-tap-highlight-color: transparent;
  }
}
</style>
