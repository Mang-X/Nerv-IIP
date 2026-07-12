<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { useVModel } from '@vueuse/core'
import { useSlots } from 'vue'
import { cn } from '../../../lib/utils'

/**
 * Pro — copy-rebuilt input (does NOT touch原版 Input). Adds leading/trailing
 * affordance slots and a focus state that pairs the ring with a soft brand
 * glow, so focus reads instantly without shouting.
 */
const props = defineProps<{
  defaultValue?: string | number
  modelValue?: string | number
  invalid?: boolean
  class?: HTMLAttributes['class']
}>()

const emits = defineEmits<{ (e: 'update:modelValue', payload: string | number): void }>()
const slots = useSlots()

defineOptions({ inheritAttrs: false })

const model = useVModel(props, 'modelValue', emits, {
  passive: true,
  defaultValue: props.defaultValue,
})
</script>

<template>
  <div
    data-slot="nv-input"
    :data-invalid="invalid || undefined"
    :class="
      cn(
        'nv-input group/input flex h-9 items-center gap-2 rounded-md border border-input bg-card px-3 dark:bg-input/30',
        props.class,
      )
    "
  >
    <span
      v-if="slots.leading"
      class="flex shrink-0 items-center text-muted-foreground [&_svg]:size-4"
    >
      <slot name="leading" />
    </span>
    <input
      v-model="model"
      data-slot="nv-input-field"
      class="h-full w-full min-w-0 bg-transparent text-sm outline-none placeholder:text-muted-foreground disabled:cursor-not-allowed disabled:opacity-50"
      v-bind="$attrs"
    />
    <span
      v-if="slots.trailing"
      class="flex shrink-0 items-center text-muted-foreground [&_svg]:size-4"
    >
      <slot name="trailing" />
    </span>
  </div>
</template>

<style scoped>
@layer nv-components {
  .nv-input {
    transition:
      border-color 0.15s var(--nv-ease-out-quart, ease-out),
      box-shadow 0.15s var(--nv-ease-out-quart, ease-out);
  }
  .nv-input:hover {
    border-color: color-mix(in oklch, var(--foreground) 18%, transparent);
  }
  .nv-input:focus-within {
    border-color: var(--nv-brand);
    box-shadow:
      0 0 0 3px color-mix(in oklch, var(--nv-brand) 22%, transparent),
      0 1px 2px 0 color-mix(in oklch, black 6%, transparent);
  }
  .nv-input[data-invalid] {
    border-color: var(--destructive);
  }
  .nv-input[data-invalid]:focus-within {
    box-shadow: 0 0 0 3px color-mix(in oklch, var(--destructive) 22%, transparent);
  }
}
</style>
