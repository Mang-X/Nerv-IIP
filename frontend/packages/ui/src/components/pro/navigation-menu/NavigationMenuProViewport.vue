<script setup lang="ts">
import type { NavigationMenuViewportProps } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import { reactiveOmit } from '@vueuse/core'
import { NavigationMenuViewport, useForwardProps } from 'reka-ui'
import { cn } from '../../../lib/utils'

/**
 * Pro — the shared mega-menu panel, following reka's official viewport pattern:
 * a `flex w-full justify-center` wrapper centres it under the bar (purely via CSS
 * flexbox — synchronous, so it never slides in), and reka's measured
 * `--reka-navigation-menu-viewport-width/height` animate the size between panels.
 * The active trigger is tracked by the Indicator, not by moving the whole panel.
 * Rendered automatically by NavigationMenuPro.
 */
const props = defineProps<NavigationMenuViewportProps & { class?: HTMLAttributes['class'] }>()
const forwarded = useForwardProps(reactiveOmit(props, 'class'))
</script>

<template>
  <div class="ds-nav-viewport-wrap absolute top-full left-0 isolate z-50 flex w-full justify-center">
    <NavigationMenuViewport
      data-slot="navigation-menu-pro-viewport"
      v-bind="forwarded"
      :class="
        cn(
          'ds-nav-viewport data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95 relative mt-2 shrink-0 origin-top overflow-hidden rounded-lg border border-border bg-popover text-popover-foreground shadow-lg duration-200',
          props.class,
        )
      "
    />
  </div>
</template>

<style scoped>
/* Size to reka's measured content box (falls back to the content's natural width
   if the var isn't set yet). `shrink-0` keeps the flex-centre wrapper from
   squashing a panel wider than the bar. Only width/height transition — position
   is the centred flex slot, so there is no horizontal slide. */
.ds-nav-viewport {
  width: var(--reka-navigation-menu-viewport-width);
  height: var(--reka-navigation-menu-viewport-height);
  transition:
    width 0.3s var(--ease-out-expo, ease-out),
    height 0.3s var(--ease-out-expo, ease-out);
  /* Glass: faint top highlight over the translucent popover fill. */
  box-shadow:
    0 18px 48px -16px color-mix(in oklch, black 50%, transparent),
    inset 0 1px 0 0 color-mix(in oklch, white 8%, transparent);
}
@media (prefers-reduced-motion: reduce) {
  .ds-nav-viewport {
    transition: none;
  }
}
</style>
