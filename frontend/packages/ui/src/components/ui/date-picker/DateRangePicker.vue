<script setup lang="ts">
import type { DateRange } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import type { DateRangeValue } from './types'
import { DateFormatter, parseDate } from '@internationalized/date'
import { CalendarRangeIcon, XIcon } from 'lucide-vue-next'
import { computed, shallowRef } from 'vue'
import { cn } from '../../../lib/utils'
import { Button } from '../button'
import { Popover, PopoverContent, PopoverTrigger } from '../popover'
import { RangeCalendar } from '../range-calendar'

const props = withDefaults(defineProps<{
  modelValue?: DateRangeValue | null
  placeholder?: string
  disabled?: boolean
  class?: HTMLAttributes['class']
}>(), {
  modelValue: null,
  placeholder: '选择日期范围',
})

const emits = defineEmits<{
  'update:modelValue': [value: DateRangeValue | null]
  apply: [value: DateRangeValue | null]
  clear: []
}>()

const open = shallowRef(false)

const formatter = new DateFormatter('zh-CN', { dateStyle: 'medium' })

function toDateValue(value: string | null | undefined) {
  if (!value)
    return undefined
  try {
    return parseDate(value)
  }
  catch {
    return undefined
  }
}

const calendarValue = computed<DateRange>({
  get: () => ({
    start: toDateValue(props.modelValue?.from),
    end: toDateValue(props.modelValue?.to),
  }),
  set: (value) => {
    const from = value.start ? value.start.toString() : null
    const to = value.end ? value.end.toString() : null
    const next = from || to ? { from, to } : null
    emits('update:modelValue', next)
    if (from && to) {
      emits('apply', next)
      open.value = false
    }
  },
})

const label = computed(() => {
  const start = toDateValue(props.modelValue?.from)
  const end = toDateValue(props.modelValue?.to)
  if (start && end)
    return `${formatter.format(start.toDate('UTC'))} - ${formatter.format(end.toDate('UTC'))}`
  if (start)
    return `${formatter.format(start.toDate('UTC'))} - ...`
  return props.placeholder
})

function clear() {
  emits('update:modelValue', null)
  emits('clear')
  open.value = false
}
</script>

<template>
  <Popover v-model:open="open">
    <PopoverTrigger as-child>
      <Button
        type="button"
        variant="outline"
        :disabled="disabled"
        :class="cn('w-64 justify-start text-left font-normal', !modelValue && 'text-muted-foreground', props.class)"
      >
        <CalendarRangeIcon data-icon="inline-start" />
        <span class="truncate">{{ label }}</span>
      </Button>
    </PopoverTrigger>
    <PopoverContent class="w-auto p-0">
      <RangeCalendar v-model="calendarValue" :number-of-months="2" initial-focus />
      <div v-if="modelValue" class="flex justify-end border-t p-2">
        <Button type="button" variant="ghost" size="sm" @click="clear">
          <XIcon data-icon="inline-start" />
          清除
        </Button>
      </div>
    </PopoverContent>
  </Popover>
</template>
