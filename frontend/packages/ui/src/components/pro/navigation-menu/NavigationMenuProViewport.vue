<script setup lang="ts">
import type { NavigationMenuViewportProps } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import { reactiveOmit } from '@vueuse/core'
import { NavigationMenuViewport, useForwardProps } from 'reka-ui'
import { cn } from '../../../lib/utils'

/**
 * Pro — the shared mega-menu panel. Reka hoists the active Content in and, with
 * `align="center"`, natively computes a centred + collision-adjusted position
 * (`--reka-navigation-menu-viewport-left/top`) and the measured size
 * (`--reka-navigation-menu-viewport-width/height`). We just apply those vars via
 * scoped CSS (the Tailwind arbitrary `*-(--var)` classes aren't reliably generated
 * for this package, so plain CSS is used), positioning it absolutely under the bar
 * so it follows the active trigger, animates between panels, and escapes the bar.
 * Rendered automatically by NavigationMenuPro.
 */
const props = withDefaults(
  defineProps<NavigationMenuViewportProps & { class?: HTMLAttributes['class'] }>(),
  { align: 'center' },
)
const forwarded = useForwardProps(reactiveOmit(props, 'class'))
</script>

<template>
  <div class="ds-nav-viewport-wrap absolute top-full left-0 isolate z-50">
    <NavigationMenuViewport
      data-slot="navigation-menu-pro-viewport"
      v-bind="forwarded"
      :class="
        cn(
          'ds-nav-viewport data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95 origin-top overflow-hidden rounded-lg border border-border bg-popover text-popover-foreground shadow-lg duration-200',
          props.class,
        )
      "
    />
  </div>
</template>

<style scoped>
/* Apply reka's natively-computed position/size. `left` is relative to the root
   (reka measures against rootRect; the wrap is at the root's left), so a centred,
   collision-clamped panel follows the active trigger. Animating left/width/height
   glides it between panels. */
.ds-nav-viewport {
  position: absolute;
  top: 0.5rem;
  left: var(--reka-navigation-menu-viewport-left, 0);
  width: var(--reka-navigation-menu-viewport-width);
  height: var(--reka-navigation-menu-viewport-height);
  transition:
    left 0.26s var(--ease-out-expo, ease-out),
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
