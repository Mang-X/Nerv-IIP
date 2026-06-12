<script setup lang="ts">
import type { DateValue } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import { DateFormatter, parseDate } from '@internationalized/date'
import { CalendarIcon, XIcon } from 'lucide-vue-next'
import { computed, shallowRef } from 'vue'
import { cn } from '../../../lib/utils'
import { Button } from '../button'
import { Calendar } from '../calendar'
import { Popover, PopoverContent, PopoverTrigger } from '../popover'

const props = withDefaults(defineProps<{
  modelValue?: string | null
  placeholder?: string
  disabled?: boolean
  class?: HTMLAttributes['class']
}>(), {
  modelValue: null,
  placeholder: '选择日期',
})

const emits = defineEmits<{
  'update:modelValue': [value: string | null]
  apply: [value: string | null]
  clear: []
}>()

const open = shallowRef(false)

const formatter = new DateFormatter('zh-CN', { dateStyle: 'long' })

function toDateValue(value: string | null | undefined): DateValue | undefined {
  if (!value)
    return undefined
  try {
    return parseDate(value)
  }
  catch {
    return undefined
  }
}

const calendarValue = computed<DateValue | undefined>({
  get: () => toDateValue(props.modelValue),
  set: (value) => {
    const next = value ? value.toString() : null
    emits('update:modelValue', next)
    emits('apply', next)
    open.value = false
  },
})

const label = computed(() => {
  const value = toDateValue(props.modelValue)
  if (!value)
    return props.placeholder
  return formatter.format(value.toDate('UTC'))
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
        :class="cn('w-48 justify-start text-left font-normal', !modelValue && 'text-muted-foreground', props.class)"
      >
        <CalendarIcon data-icon="inline-start" />
        <span class="truncate">{{ label }}</span>
      </Button>
    </PopoverTrigger>
    <PopoverContent class="w-auto p-0">
      <Calendar v-model="calendarValue" initial-focus />
      <div v-if="modelValue" class="flex justify-end border-t p-2">
        <Button type="button" variant="ghost" size="sm" @click="clear">
          <XIcon data-icon="inline-start" />
          清除
        </Button>
      </div>
    </PopoverContent>
  </Popover>
</template>
