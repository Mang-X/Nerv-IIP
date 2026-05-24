<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { CalendarIcon, XIcon } from 'lucide-vue-next'
import { computed, shallowRef, watch } from 'vue'
import { cn } from '../../../lib/utils'
import { Button } from '../button'
import { Input } from '../input'
import { Popover, PopoverContent, PopoverTrigger } from '../popover'

const props = withDefaults(defineProps<{
  modelValue?: string | null
  placeholder?: string
  disabled?: boolean
  class?: HTMLAttributes['class']
}>(), {
  modelValue: null,
  placeholder: 'Pick a date',
})

const emits = defineEmits<{
  'update:modelValue': [value: string | null]
  apply: [value: string | null]
  clear: []
}>()

const draftValue = shallowRef(props.modelValue ?? '')
const open = shallowRef(false)
const label = computed(() => props.modelValue || props.placeholder)

watch(() => props.modelValue, (value) => {
  draftValue.value = value ?? ''
})

function apply() {
  const value = draftValue.value || null
  emits('update:modelValue', value)
  emits('apply', value)
  open.value = false
}

function clear() {
  draftValue.value = ''
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
    <PopoverContent class="w-72">
      <div class="flex flex-col gap-3">
        <Input
          v-model="draftValue"
          type="date"
          :disabled="disabled"
          aria-label="Date"
        />
        <div class="flex justify-end gap-2">
          <Button type="button" variant="ghost" size="sm" @click="clear">
            <XIcon data-icon="inline-start" />
            Clear
          </Button>
          <Button type="button" size="sm" @click="apply">
            Apply
          </Button>
        </div>
      </div>
    </PopoverContent>
  </Popover>
</template>
