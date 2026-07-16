<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed } from 'vue'
import { MinusIcon, PlusIcon } from '@lucide/vue'
import { cn } from '../../lib/utils'

/**
 * Touch — large quantity stepper for shop-floor reporting (报工数量). Big ± tap
 * targets and a tabular readout; clamps to [min, max], step-adjustable.
 */
const props = withDefaults(
  defineProps<{
    modelValue?: number
    min?: number
    max?: number
    step?: number
    class?: HTMLAttributes['class']
  }>(),
  { modelValue: 0, min: 0, max: Number.POSITIVE_INFINITY, step: 1 },
)
const emit = defineEmits<{ (e: 'update:modelValue', value: number): void }>()

const canDec = computed(() => props.modelValue > props.min)
const canInc = computed(() => props.modelValue < props.max)

function clamp(n: number) {
  return Math.min(props.max, Math.max(props.min, n))
}
function dec() {
  if (canDec.value) emit('update:modelValue', clamp(props.modelValue - props.step))
}
function inc() {
  if (canInc.value) emit('update:modelValue', clamp(props.modelValue + props.step))
}
</script>

<template>
  <div
    data-slot="qty-stepper"
    :class="
      cn(
        'inline-flex h-14 items-stretch overflow-hidden rounded-xl border border-border bg-card',
        props.class,
      )
    "
  >
    <button
      type="button"
      class="nv-step flex w-14 items-center justify-center text-foreground transition-colors hover:bg-muted active:bg-accent disabled:opacity-40"
      :disabled="!canDec"
      aria-label="减少"
      @click="dec"
    >
      <MinusIcon class="size-5" aria-hidden="true" />
    </button>
    <div
      class="flex min-w-20 items-center justify-center border-x border-border px-4 text-2xl font-semibold tabular-nums"
    >
      {{ modelValue }}
    </div>
    <button
      type="button"
      class="nv-step flex w-14 items-center justify-center text-foreground transition-colors hover:bg-muted active:bg-accent disabled:opacity-40"
      :disabled="!canInc"
      aria-label="增加"
      @click="inc"
    >
      <PlusIcon class="size-5" aria-hidden="true" />
    </button>
  </div>
</template>

<style scoped>
@layer nv-components {
  .nv-step {
    -webkit-tap-highlight-color: transparent;
    touch-action: manipulation;
  }
}
</style>
