<script setup lang="ts">
import type { SliderRootEmits, SliderRootProps } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import { reactiveOmit } from '@vueuse/core'
import { SliderRange, SliderRoot, SliderThumb, SliderTrack, useForwardPropsEmits } from 'reka-ui'
import { computed } from 'vue'
import { cn } from '../../../lib/utils'

/**
 * Pro — slider (does NOT touch原版). Brand-filled range over a muted track, a
 * draggable thumb with a focus ring and a calm grab-grow on press (no bounce,
 * per our motion philosophy). Rebuilt on reka primitives; `modelValue` as an
 * array renders one thumb per value (ranges).
 */
const props = defineProps<SliderRootProps & { class?: HTMLAttributes['class'] }>()
const emits = defineEmits<SliderRootEmits>()
const forwarded = useForwardPropsEmits(reactiveOmit(props, 'class'), emits)

// reka renders one SliderThumb per value — mirror the model's length.
const thumbCount = computed(() => {
  const v = props.modelValue ?? props.defaultValue
  return Array.isArray(v) ? Math.max(1, v.length) : 1
})
</script>

<template>
  <SliderRoot
    data-slot="slider-pro"
    v-bind="forwarded"
    :class="
      cn(
        'ds-slider relative flex w-full touch-none items-center select-none data-[orientation=vertical]:h-44 data-[orientation=vertical]:w-auto data-[orientation=vertical]:flex-col data-[disabled]:pointer-events-none data-[disabled]:opacity-50',
        props.class,
      )
    "
  >
    <SliderTrack
      class="ds-slider-track relative h-1.5 w-full grow overflow-hidden rounded-full bg-muted data-[orientation=vertical]:h-full data-[orientation=vertical]:w-1.5"
    >
      <SliderRange
        class="ds-slider-range absolute h-full rounded-full bg-brand data-[orientation=vertical]:w-full"
      />
    </SliderTrack>
    <SliderThumb
      v-for="i in thumbCount"
      :key="i"
      class="ds-slider-thumb block size-4 shrink-0 rounded-full border-2 border-brand bg-background shadow-sm outline-none focus-visible:ring-[3px] focus-visible:ring-brand/30"
    />
  </SliderRoot>
</template>

<style scoped>
@layer nv-components {
  .ds-slider-thumb {
    /* Grab-grow on hover/press — a calm decelerate, no bounce (motion philosophy).
     The range itself tracks the pointer 1:1 (no transition) so dragging is exact. */
    transition:
      transform 0.16s var(--nv-ease-out-quart, ease-out),
      box-shadow 0.18s var(--nv-ease-out-quart, ease-out);
  }
  .ds-slider-thumb:hover {
    transform: scale(1.08);
  }
  .ds-slider-thumb:active {
    transform: scale(1.18);
  }
  @media (prefers-reduced-motion: reduce) {
    .ds-slider-thumb {
      transition: none;
    }
  }
}
</style>
