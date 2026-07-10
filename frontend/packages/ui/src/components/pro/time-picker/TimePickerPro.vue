<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed, nextTick, ref, watch } from 'vue'
import { ClockIcon } from 'lucide-vue-next'
import { Popover, PopoverContent, PopoverTrigger } from '../../ui/popover'
import { cn } from '../../../lib/utils'
import ButtonPro from '../button/ButtonPro.vue'

/**
 * Pro — time picker (HH:mm). A ButtonPro trigger opens a popover with scrollable
 * hour / minute columns; the current value auto-scrolls into view. Brand-tinted
 * selection. Composes Popover + ButtonPro; never edits原版 primitives.
 */
const props = withDefaults(
  defineProps<{
    modelValue?: string | null
    placeholder?: string
    minuteStep?: number
    disabled?: boolean
    class?: HTMLAttributes['class']
  }>(),
  { modelValue: null, placeholder: '选择时间', minuteStep: 1, disabled: false },
)
const emit = defineEmits<{ 'update:modelValue': [value: string] }>()

const open = ref(false)
const hourCol = ref<HTMLElement>()
const minuteCol = ref<HTMLElement>()

function pad(n: number) {
  return String(n).padStart(2, '0')
}
const hours = Array.from({ length: 24 }, (_, i) => i)
const minutes = computed(() =>
  Array.from({ length: Math.ceil(60 / props.minuteStep) }, (_, i) => i * props.minuteStep),
)

const parts = computed(() => {
  const [h, m] = (props.modelValue ?? '').split(':')
  return { h: h === undefined ? null : Number(h), m: m === undefined ? null : Number(m) }
})

function commit(h: number, m: number) {
  emit('update:modelValue', `${pad(h)}:${pad(m)}`)
}
function selectHour(h: number) {
  commit(h, parts.value.m ?? 0)
}
function selectMinute(m: number) {
  commit(parts.value.h ?? 0, m)
}
function now() {
  const d = new Date()
  const m = Math.round(d.getMinutes() / props.minuteStep) * props.minuteStep
  commit(d.getHours(), Math.min(59, m))
}

watch(open, (isOpen) => {
  if (!isOpen) return
  nextTick(() => {
    for (const col of [hourCol.value, minuteCol.value]) {
      col?.querySelector<HTMLElement>('[data-active=true]')?.scrollIntoView({ block: 'center' })
    }
  })
})
</script>

<template>
  <Popover v-model:open="open">
    <PopoverTrigger as-child>
      <ButtonPro
        variant="outline"
        :disabled="disabled"
        :class="
          cn(
            'w-40 justify-between font-normal',
            !modelValue && 'text-muted-foreground',
            props.class,
          )
        "
      >
        <template #leading
          ><ClockIcon class="size-4 text-muted-foreground" aria-hidden="true"
        /></template>
        {{ modelValue || placeholder }}
      </ButtonPro>
    </PopoverTrigger>
    <PopoverContent class="w-auto p-0" align="start">
      <div class="flex divide-x divide-border">
        <div ref="hourCol" class="ds-tp-col">
          <button
            v-for="h in hours"
            :key="h"
            type="button"
            :data-active="parts.h === h"
            :class="cn('ds-tp-cell', parts.h === h && 'ds-tp-cell-active')"
            @click="selectHour(h)"
          >
            {{ pad(h) }}
          </button>
        </div>
        <div ref="minuteCol" class="ds-tp-col">
          <button
            v-for="m in minutes"
            :key="m"
            type="button"
            :data-active="parts.m === m"
            :class="cn('ds-tp-cell', parts.m === m && 'ds-tp-cell-active')"
            @click="selectMinute(m)"
          >
            {{ pad(m) }}
          </button>
        </div>
      </div>
      <div class="border-t border-border p-1.5">
        <ButtonPro variant="ghost" size="sm" class="w-full" @click="now">此刻</ButtonPro>
      </div>
    </PopoverContent>
  </Popover>
</template>

<style scoped>
@layer nv-components {
  .ds-tp-col {
    display: flex;
    flex-direction: column;
    height: 14rem;
    width: 4rem;
    overflow-y: auto;
    padding: 0.25rem;
    scrollbar-width: thin;
  }
  .ds-tp-cell {
    flex-shrink: 0;
    border-radius: var(--radius-md);
    padding: 0.375rem 0;
    text-align: center;
    font-variant-numeric: tabular-nums;
    font-size: 0.875rem;
    color: var(--foreground);
    transition: background-color 0.12s var(--nv-ease-out-quart, ease-out);
  }
  .ds-tp-cell:hover {
    background: var(--accent);
  }
  .ds-tp-cell-active {
    background: var(--nv-brand);
    color: var(--nv-brand-foreground);
    font-weight: 500;
  }
  .ds-tp-cell-active:hover {
    background: var(--nv-brand);
  }
}
</style>
