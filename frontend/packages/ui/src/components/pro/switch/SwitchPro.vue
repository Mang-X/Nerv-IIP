<script setup lang="ts">
import type { SwitchRootEmits, SwitchRootProps } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import { reactiveOmit } from '@vueuse/core'
import { SwitchRoot, SwitchThumb, useForwardPropsEmits } from 'reka-ui'
import { cn } from '../../../lib/utils'

/**
 * Pro — switch (does NOT touch原版). Brand-filled track when on, thumb glides
 * with the shared ease-out curve. Rebuilt on reka primitives.
 */
const props = defineProps<SwitchRootProps & { class?: HTMLAttributes['class'] }>()
const emits = defineEmits<SwitchRootEmits>()
const forwarded = useForwardPropsEmits(reactiveOmit(props, 'class'), emits)
</script>

<template>
  <SwitchRoot
    data-slot="switch-pro"
    v-bind="forwarded"
    :class="
      cn(
        'ds-switch peer inline-flex h-5 w-9 shrink-0 cursor-pointer items-center rounded-full border border-transparent px-0.5 outline-none transition-colors focus-visible:ring-[3px] focus-visible:ring-brand/30 disabled:cursor-not-allowed disabled:opacity-50 data-[state=checked]:bg-brand data-[state=unchecked]:bg-input dark:data-[state=unchecked]:bg-input/60',
        props.class,
      )
    "
  >
    <SwitchThumb
      class="ds-switch-thumb pointer-events-none block size-4 rounded-full bg-background shadow-sm ring-0 data-[state=checked]:translate-x-4 data-[state=unchecked]:translate-x-0"
    />
  </SwitchRoot>
</template>

<style scoped>
.ds-switch {
  transition: background-color 0.18s var(--ease-out-quart, ease-out);
}
.ds-switch-thumb {
  /* Tailwind v4 moves the thumb via the `translate` property (not `transform`),
     so the transition must name `translate` for the glide to animate. */
  transition:
    translate 0.2s var(--ease-out-quart, ease-out),
    box-shadow 0.2s ease;
}
@media (prefers-reduced-motion: reduce) {
  .ds-switch-thumb {
    transition: none;
  }
}
</style>
