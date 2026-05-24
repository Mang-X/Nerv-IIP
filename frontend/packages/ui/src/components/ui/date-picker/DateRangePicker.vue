<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import type { DateRangeValue } from './types'
import { CalendarRangeIcon, XIcon } from 'lucide-vue-next'
import { computed, reactive, shallowRef, watch } from 'vue'
import { cn } from '../../../lib/utils'
import { Button } from '../button'
import { Input } from '../input'
import { Popover, PopoverContent, PopoverTrigger } from '../popover'

const props = withDefaults(defineProps<{
  modelValue?: DateRangeValue | null
  placeholder?: string
  disabled?: boolean
  class?: HTMLAttributes['class']
}>(), {
  modelValue: null,
  placeholder: 'Pick a range',
})

const emits = defineEmits<{
  'update:modelValue': [value: DateRangeValue | null]
  apply: [value: DateRangeValue | null]
  cancel: []
  clear: []
}>()

const draft = reactive<DateRangeValue>({
  from: props.modelValue?.from ?? null,
  to: props.modelValue?.to ?? null,
})
const open = shallowRef(false)

watch(() => props.modelValue, (value) => {
  draft.from = value?.from ?? null
  draft.to = value?.to ?? null
})

const label = computed(() => {
  if (props.modelValue?.from && props.modelValue?.to) {
    return `${props.modelValue.from} - ${props.modelValue.to}`
  }

  if (props.modelValue?.from) {
    return `${props.modelValue.from} - ...`
  }

  return props.placeholder
})

function apply() {
  const value = draft.from || draft.to ? { from: draft.from, to: draft.to } : null
  emits('update:modelValue', value)
  emits('apply', value)
  open.value = false
}

function cancel() {
  draft.from = props.modelValue?.from ?? null
  draft.to = props.modelValue?.to ?? null
  emits('update:modelValue', props.modelValue ?? null)
  emits('cancel')
  open.value = false
}

function clear() {
  draft.from = null
  draft.to = null
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
    <PopoverContent class="w-80">
      <div class="flex flex-col gap-3">
        <div class="grid grid-cols-2 gap-2">
          <Input
            :model-value="draft.from ?? ''"
            type="date"
            :disabled="disabled"
            aria-label="Start date"
            @update:model-value="draft.from = String($event || '') || null"
          />
          <Input
            :model-value="draft.to ?? ''"
            type="date"
            :disabled="disabled"
            aria-label="End date"
            @update:model-value="draft.to = String($event || '') || null"
          />
        </div>
        <div class="flex justify-between gap-2">
          <Button type="button" variant="ghost" size="sm" @click="clear">
            <XIcon data-icon="inline-start" />
            Clear
          </Button>
          <div class="flex gap-2">
            <Button type="button" variant="outline" size="sm" @click="cancel">
              Cancel
            </Button>
            <Button type="button" size="sm" @click="apply">
              Apply
            </Button>
          </div>
        </div>
      </div>
    </PopoverContent>
  </Popover>
</template>
