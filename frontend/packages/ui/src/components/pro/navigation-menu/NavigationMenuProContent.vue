<script setup lang="ts">
import type { NavigationMenuContentEmits, NavigationMenuContentProps } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import { reactiveOmit } from '@vueuse/core'
import { NavigationMenuContent, useForwardPropsEmits } from 'reka-ui'
import { cn } from '../../../lib/utils'

/**
 * Pro — navigation content: the panel shown for an open trigger. Reka hoists
 * this into the shared viewport, so it lays out its own grid of links; the
 * viewport handles the glass surface, sizing and clip-escape. Slides in from
 * the side matching the direction of travel between sibling triggers.
 */
const props = defineProps<NavigationMenuContentProps & { class?: HTMLAttributes['class'] }>()
const emits = defineEmits<NavigationMenuContentEmits>()
const forwarded = useForwardPropsEmits(reactiveOmit(props, 'class'), emits)
</script>

<template>
  <NavigationMenuContent
    data-slot="navigation-menu-pro-content"
    v-bind="forwarded"
    :class="
      cn(
        'ds-nav-content absolute top-0 left-0 w-full p-3 sm:w-auto',
        'data-[motion=from-start]:animate-in data-[motion=from-end]:animate-in data-[motion=to-start]:animate-out data-[motion=to-end]:animate-out',
        'data-[motion=from-start]:fade-in data-[motion=from-end]:fade-in data-[motion=to-start]:fade-out data-[motion=to-end]:fade-out',
        'data-[motion=from-start]:slide-in-from-left-8 data-[motion=from-end]:slide-in-from-right-8 data-[motion=to-start]:slide-out-to-left-8 data-[motion=to-end]:slide-out-to-right-8',
        'duration-200',
        props.class,
      )
    "
  >
    <slot />
  </NavigationMenuContent>
</template>

<style scoped>
.ds-nav-content {
  animation-timing-function: var(--ease-out-quart, ease-out);
}
@media (prefers-reduced-motion: reduce) {
  .ds-nav-content {
    animation: none !important;
  }
}
</style>
