<script setup lang="ts">
import type { NavigationMenuViewportProps } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import { reactiveOmit } from '@vueuse/core'
import { NavigationMenuViewport, useForwardProps } from 'reka-ui'
import { cn } from '../../../lib/utils'

/**
 * Pro — the shared panel container. Reka hoists the active Content into it and
 * exposes measured width/height so the surface animates its size to fit. This
 * is the glass overlay (matching our other overlays) and lives outside the bar
 * so it escapes clipping. Rendered automatically by NavigationMenuPro.
 */
const props = defineProps<NavigationMenuViewportProps & { class?: HTMLAttributes['class'] }>()
const forwarded = useForwardProps(reactiveOmit(props, 'class'))
</script>

<template>
  <div class="ds-nav-viewport-wrap absolute top-full left-0 isolate z-50 flex w-full justify-start">
    <NavigationMenuViewport
      data-slot="navigation-menu-pro-viewport"
      v-bind="forwarded"
      :class="
        cn(
          'ds-nav-viewport data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-90 relative mt-2 h-(--reka-navigation-menu-viewport-height) w-(--reka-navigation-menu-viewport-width) shrink-0 origin-top overflow-hidden rounded-lg border border-border bg-popover text-popover-foreground shadow-lg duration-200',
          props.class,
        )
      "
    />
  </div>
</template>

<style scoped>
/* Panel sizing: the template sizes the viewport to reka's measured content box
   via `w-(--reka-navigation-menu-viewport-width)` / `h-(...-height)`, plus
   `shrink-0`. The shrink-0 is load-bearing: the wrap is `flex justify-center` and
   the bar is narrower than the panel, so without it the flex child shrinks the
   444px panel down to the bar width and the mega-menu content gets clipped. */
.ds-nav-viewport {
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
