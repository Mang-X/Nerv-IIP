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
  <div class="ds-nav-viewport-wrap absolute top-full left-0 isolate z-50 flex w-full justify-center">
    <NavigationMenuViewport
      data-slot="navigation-menu-pro-viewport"
      v-bind="forwarded"
      :class="
        cn(
          'ds-nav-viewport data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-90 relative mt-2 h-(--reka-navigation-menu-viewport-height) w-full origin-top overflow-hidden rounded-lg border border-border bg-popover text-popover-foreground shadow-lg duration-200 sm:w-(--reka-navigation-menu-viewport-width)',
          props.class,
        )
      "
    />
  </div>
</template>

<style scoped>
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
