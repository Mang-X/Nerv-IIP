<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed, ref, watch } from 'vue'
import { CalendarIcon, ChevronLeftIcon, ChevronRightIcon, XIcon } from '@lucide/vue'
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
    id?: string
    placeholder?: string
    disabled?: boolean
    /** 允许清除已选日期（触发按钮左侧出现清除叉，回传空串）。 */
    clearable?: boolean
    ariaInvalid?: boolean
    ariaDescribedby?: string
    class?: HTMLAttributes['class']
  }>(),
  { modelValue: null, placeholder: '选择日期', disabled: false, clearable: false },
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
function clear() {
  emit('update:modelValue', '')
}
</script>

<template>
  <Popover v-model:open="open">
    <!-- 宽度只由调用方给（`class` 走同一条 twMerge 通道），组件不再自带任何宽度。
         此前硬写的 `w-full sm:w-48` 会和「字段壳的 `*:w-full`」以及调用方自己写的
         `sm:w-*` 抢同一个属性，胜负交给 Tailwind 生成顺序——外层 NvField 明明声明了
         `sm:w-36`，里面这枚按钮却按 `sm:w-48` 排版，六个筛选器一个都配不平（#3735）。
         跟 NvFilterBar 里搜索框 / 下拉框（`w-full sm:w-64` / `w-full sm:w-44`）是同一套写法。 -->
    <div class="relative flex w-full" :class="props.class" data-slot="nv-date-picker">
      <PopoverTrigger as-child>
        <NvButton
          :id="id"
          variant="outline"
          :disabled="disabled"
          :aria-invalid="ariaInvalid || undefined"
          :aria-describedby="ariaDescribedby"
          :class="
            cn(
              'w-full justify-between font-normal',
              !modelValue && 'text-muted-foreground',
              // 有清除叉时给右侧让位，否则 `YYYY-MM-DD` 会钻到叉底下。叉 20px 宽
              // 停在 right-8（32px）上，叉左沿落在 32+8=40px 处，收边到 48px 才
              // 不压字——`pr-12` 实测还差 3px。
              clearable && modelValue && !disabled && 'pr-14',
            )
          "
        >
          <template #leading
            ><CalendarIcon class="size-4 shrink-0 text-muted-foreground" aria-hidden="true"
          /></template>
          <span class="truncate tabular-nums">{{ modelValue || placeholder }}</span>
        </NvButton>
      </PopoverTrigger>
      <!-- 清除叉与触发器是**兄弟**而不是子节点：塞在按钮里会产出非法嵌套的
           `<button>`，还会把叉的 aria-label 并进触发器的可及名称。压在触发器之上时
           必须把指针事件彻底截断——pointerdown / mousedown 只要漏一个到下面的触发器，
           点「清除」就会顺带把浮层打开（同 NvEntityPicker）。 -->
      <button
        v-if="clearable && modelValue && !disabled"
        type="button"
        class="absolute right-8 flex size-5 items-center justify-center rounded-sm text-muted-foreground hover:bg-muted hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring/50 focus-visible:outline-none"
        aria-label="清除所选日期"
        @pointerdown.stop.prevent
        @mousedown.stop.prevent
        @click.stop.prevent="clear"
      >
        <XIcon class="size-3.5" aria-hidden="true" />
      </button>
    </div>
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
      <div class="mt-2 flex items-center gap-1 border-t border-border pt-2">
        <NvButton variant="ghost" size="sm" class="flex-1" @click="pickToday">今天</NvButton>
        <!-- 清除留在浮层里与「今天」同排，触发器上的叉是第二道（也是原生 date
             替换掉之后唯一那条无键盘路径的替代：浮层能用 Tab 走到这里）。 -->
        <NvButton
          v-if="clearable && modelValue && !disabled"
          variant="ghost"
          size="sm"
          class="flex-1"
          @click="clear"
        >
          清除
        </NvButton>
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
