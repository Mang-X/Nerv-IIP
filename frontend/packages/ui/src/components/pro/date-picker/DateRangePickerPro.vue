<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed, ref, watch } from 'vue'
import { CalendarIcon, ChevronLeftIcon, ChevronRightIcon } from 'lucide-vue-next'
import { Popover, PopoverContent, PopoverTrigger } from '../../ui/popover'
import { cn } from '../../../lib/utils'
import ButtonPro from '../button/ButtonPro.vue'

/**
 * Pro — date range picker (YYYY-MM-DD … YYYY-MM-DD). Same self-contained
 * month-grid calendar as DatePickerPro, but selects a start→end range: first
 * click sets the start, second sets the end (auto-ordered), with a live hover
 * preview of the span. String range model.
 */
export interface DateRange {
  start: string | null
  end: string | null
}

const props = withDefaults(
  defineProps<{
    modelValue?: DateRange | null
    placeholder?: string
    disabled?: boolean
    class?: HTMLAttributes['class']
  }>(),
  { modelValue: null, placeholder: '选择日期范围', disabled: false },
)
const emit = defineEmits<{ 'update:modelValue': [value: DateRange] }>()

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

const cursor = ref({ y: today.getFullYear(), m: today.getMonth() })

// In-progress selection: once `anchor` is set we await the second click.
const anchor = ref<string | null>(null)
const hover = ref<string | null>(null)

const start = computed(() => props.modelValue?.start ?? null)
const end = computed(() => props.modelValue?.end ?? null)

function syncCursor() {
  const m = (start.value ?? '').match(/^(\d{4})-(\d{2})-(\d{2})$/)
  cursor.value = m
    ? { y: Number(m[1]), m: Number(m[2]) - 1 }
    : { y: today.getFullYear(), m: today.getMonth() }
}
watch(open, (isOpen) => {
  if (isOpen) {
    anchor.value = null
    hover.value = null
    syncCursor()
  }
})

const monthLabel = computed(() => `${cursor.value.y} 年 ${cursor.value.m + 1} 月`)

/** The live [lo, hi] span: the committed range, or the anchor→hover preview. */
const span = computed<{ lo: string | null; hi: string | null }>(() => {
  if (anchor.value) {
    const other = hover.value ?? anchor.value
    return anchor.value <= other ? { lo: anchor.value, hi: other } : { lo: other, hi: anchor.value }
  }
  return { lo: start.value, hi: end.value }
})

const grid = computed(() => {
  const { y, m } = cursor.value
  const first = new Date(y, m, 1)
  const offset = (first.getDay() + 6) % 7 // Mon=0
  const gridStart = new Date(y, m, 1 - offset)
  const { lo, hi } = span.value
  return Array.from({ length: 42 }, (_, i) => {
    const d = new Date(gridStart.getFullYear(), gridStart.getMonth(), gridStart.getDate() + i)
    const key = fmt(d.getFullYear(), d.getMonth(), d.getDate())
    const inSpan = !!lo && !!hi && key >= lo && key <= hi
    return {
      key,
      day: d.getDate(),
      outside: d.getMonth() !== m,
      today: key === todayKey,
      isStart: !!lo && key === lo && lo !== hi,
      isEnd: !!hi && key === hi && lo !== hi,
      single: !!lo && lo === hi && key === lo,
      inRange: inSpan && key !== lo && key !== hi,
    }
  })
})

function shift(delta: number) {
  const d = new Date(cursor.value.y, cursor.value.m + delta, 1)
  cursor.value = { y: d.getFullYear(), m: d.getMonth() }
}
function pick(key: string) {
  if (!anchor.value) {
    anchor.value = key
    return
  }
  const lo = anchor.value <= key ? anchor.value : key
  const hi = anchor.value <= key ? key : anchor.value
  anchor.value = null
  hover.value = null
  emit('update:modelValue', { start: lo, end: hi })
  open.value = false
}

const label = computed(() =>
  start.value && end.value ? `${start.value} ~ ${end.value}` : props.placeholder,
)
</script>

<template>
  <Popover v-model:open="open">
    <PopoverTrigger as-child>
      <ButtonPro
        variant="outline"
        :disabled="disabled"
        :class="
          cn(
            'w-64 justify-between font-normal',
            !(start && end) && 'text-muted-foreground',
            props.class,
          )
        "
      >
        <template #leading
          ><CalendarIcon class="size-4 text-muted-foreground" aria-hidden="true"
        /></template>
        {{ label }}
      </ButtonPro>
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
      <div class="grid grid-cols-7 gap-y-0.5">
        <span
          v-for="w in WEEKDAYS"
          :key="w"
          class="grid size-8 place-items-center text-xs text-muted-foreground"
          >{{ w }}</span
        >
        <button
          v-for="cell in grid"
          :key="cell.key"
          type="button"
          :class="
            cn(
              'ds-dp-cell grid size-8 place-items-center text-sm tabular-nums transition-colors',
              cell.inRange ? 'bg-accent' : 'rounded-md',
              cell.isStart && 'rounded-l-md bg-accent',
              cell.isEnd && 'rounded-r-md bg-accent',
              cell.outside && !cell.inRange ? 'text-muted-foreground/40' : 'text-foreground',
              !cell.inRange && !cell.isStart && !cell.isEnd && !cell.single && 'hover:bg-accent',
              (cell.isStart || cell.isEnd || cell.single) &&
                'rounded-md bg-brand text-brand-foreground hover:bg-brand',
              cell.today && !cell.isStart && !cell.isEnd && !cell.single && 'ring-1 ring-brand/40',
            )
          "
          @click="pick(cell.key)"
          @mouseenter="anchor && (hover = cell.key)"
        >
          {{ cell.day }}
        </button>
      </div>
    </PopoverContent>
  </Popover>
</template>

<style scoped>
@layer nv-components {
  .ds-dp-cell {
    -webkit-tap-highlight-color: transparent;
  }
}
</style>
