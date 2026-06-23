<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { useSlots } from 'vue'
import { useVModel } from '@vueuse/core'
import { cn } from '../../lib/utils'

/**
 * Mobile input — 44px touch height, 15px text (no iOS zoom-on-focus), leading /
 * trailing slots, subtle brand focus ring. Native form-field feel.
 */
const props = defineProps<{
  defaultValue?: string | number
  modelValue?: string | number
  class?: HTMLAttributes['class']
}>()
const emits = defineEmits<{ 'update:modelValue': [value: string | number] }>()
const slots = useSlots()
defineOptions({ inheritAttrs: false })

const model = useVModel(props, 'modelValue', emits, {
  passive: true,
  defaultValue: props.defaultValue,
})
</script>

<template>
  <div
    data-slot="mobile-input"
    :class="
      cn(
        'ds-minput flex h-11 items-center gap-2.5 rounded-xl border border-border bg-card px-3.5',
        props.class,
      )
    "
  >
    <span
      v-if="slots.leading"
      class="flex shrink-0 items-center text-muted-foreground [&_svg]:size-5"
    >
      <slot name="leading" />
    </span>
    <input
      v-model="model"
      data-slot="mobile-input-field"
      class="h-full w-full min-w-0 bg-transparent text-[15px] outline-none placeholder:text-muted-foreground disabled:opacity-50"
      v-bind="$attrs"
    />
    <span
      v-if="slots.trailing"
      class="flex shrink-0 items-center text-muted-foreground [&_svg]:size-5"
    >
      <slot name="trailing" />
    </span>
  </div>
</template>

<style scoped>
.ds-minput {
  transition:
    border-color 0.15s var(--ease-out-quart, ease-out),
    box-shadow 0.15s var(--ease-out-quart, ease-out);
}
.ds-minput:focus-within {
  border-color: var(--brand);
  box-shadow: 0 0 0 3px color-mix(in oklch, var(--brand) 20%, transparent);
}
</style>
