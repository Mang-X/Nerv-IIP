<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed } from 'vue'
import { Minus, Plus } from '@lucide/vue'
import { cn } from '../../lib/utils'

/**
 * Mobile Stepper — compact, native (iOS / tdesign-mobile) number stepper with an
 * editable centre input. ~32px tall. Clamps to [min, max] on input/blur.
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
const emit = defineEmits<{ 'update:modelValue': [value: number] }>()

const canDec = computed(() => props.modelValue > props.min)
const canInc = computed(() => props.modelValue < props.max)

function clamp(n: number) {
  if (Number.isNaN(n)) return props.min
  return Math.min(props.max, Math.max(props.min, n))
}
function set(n: number) {
  emit('update:modelValue', clamp(n))
}
function onInput(e: Event) {
  const raw = Number((e.target as HTMLInputElement).value)
  if (!Number.isNaN(raw)) emit('update:modelValue', raw)
}
function onBlur(e: Event) {
  ;(e.target as HTMLInputElement).value = String(clamp(props.modelValue))
  set(props.modelValue)
}
</script>

<template>
  <div
    data-slot="stepper"
    :class="
      cn(
        'nv-m-stepper inline-flex h-8 items-stretch overflow-hidden rounded-lg border border-border bg-card',
        props.class,
      )
    "
  >
    <button
      type="button"
      class="nv-m-stepper-btn grid w-8 place-items-center text-foreground disabled:opacity-30"
      :disabled="!canDec"
      aria-label="减少"
      @click="set(modelValue - step)"
    >
      <Minus class="size-4" aria-hidden="true" />
    </button>
    <input
      :value="modelValue"
      type="number"
      inputmode="numeric"
      class="w-11 border-x border-border bg-transparent text-center text-[15px] font-medium tabular-nums outline-none [appearance:textfield] [&::-webkit-inner-spin-button]:appearance-none [&::-webkit-outer-spin-button]:appearance-none"
      @input="onInput"
      @blur="onBlur"
    />
    <button
      type="button"
      class="nv-m-stepper-btn grid w-8 place-items-center text-foreground disabled:opacity-30"
      :disabled="!canInc"
      aria-label="增加"
      @click="set(modelValue + step)"
    >
      <Plus class="size-4" aria-hidden="true" />
    </button>
  </div>
</template>

<style scoped>
@layer nv-components {
  .nv-m-stepper {
    -webkit-tap-highlight-color: transparent;
    touch-action: manipulation;
  }
  .nv-m-stepper-btn {
    transition: background-color 0.16s ease;
  }
  .nv-m-stepper-btn :deep(svg) {
    transition: transform 0.18s var(--nv-ease-out-quart);
  }
  .nv-m-stepper-btn:active:not(:disabled) {
    background: var(--muted);
  }
  /* WinUI3-style press: the glyph shrinks while held, then springs back. */
  .nv-m-stepper-btn:active:not(:disabled) :deep(svg) {
    transform: scale(0.8);
  }
  @media (prefers-reduced-motion: reduce) {
    .nv-m-stepper-btn :deep(svg) {
      transition: none;
    }
  }
}
</style>
