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
@layer nv-components {
  .ds-switch {
    transition: background-color 0.18s var(--nv-ease-out-quart, ease-out);
  }
  .ds-switch-thumb {
    /* Tailwind v4 glides the thumb via the `translate` property; `transform` carries
     the press depress (a separate property, composes with `translate`). A slight
     back-ease spring on the glide unifies it with the mobile Switch. */
    transition:
      translate 0.22s var(--nv-ease-out-quart, ease-out),
      transform 0.18s var(--nv-ease-out-quart),
      box-shadow 0.2s ease;
  }
  /* Press: the thumb depresses while held, then decelerates back — a press
   micro-interaction unified with Radio/Checkbox. No bounce (per our motion
   philosophy); the desktop curve is calmer than the mobile spring. */
  .ds-switch:active:not([data-disabled]) .ds-switch-thumb {
    transform: scale(0.86);
  }
  @media (prefers-reduced-motion: reduce) {
    .ds-switch-thumb {
      transition: none;
    }
  }
}
</style>
