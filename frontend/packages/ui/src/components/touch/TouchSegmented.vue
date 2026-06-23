<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { cn } from '../../lib/utils'

/**
 * Touch — large segmented control. One-tap switch between a small set of modes,
 * sized for fingers. Reduces operation paths vs. a dropdown on the shop floor.
 */
export interface SegmentOption {
  value: string
  label: string
}

const props = defineProps<{
  modelValue: string
  options: SegmentOption[]
  class?: HTMLAttributes['class']
}>()
const emit = defineEmits<{ (e: 'update:modelValue', value: string): void }>()
</script>

<template>
  <div
    data-slot="touch-segmented"
    role="tablist"
    :class="cn('inline-flex h-12 items-center gap-1 rounded-xl bg-muted p-1', props.class)"
  >
    <button
      v-for="opt in options"
      :key="opt.value"
      type="button"
      role="tab"
      :aria-selected="modelValue === opt.value"
      :class="
        cn(
          'ds-seg h-full rounded-lg px-5 text-base font-medium whitespace-nowrap transition-colors',
          modelValue === opt.value
            ? 'bg-card text-foreground shadow-sm'
            : 'text-muted-foreground hover:text-foreground',
        )
      "
      @click="emit('update:modelValue', opt.value)"
    >
      {{ opt.label }}
    </button>
  </div>
</template>

<style scoped>
.ds-seg {
  -webkit-tap-highlight-color: transparent;
  touch-action: manipulation;
}
</style>
