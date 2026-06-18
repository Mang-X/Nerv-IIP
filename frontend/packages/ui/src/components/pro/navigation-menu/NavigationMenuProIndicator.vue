<script setup lang="ts">
import type { NavigationMenuIndicatorProps } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import { reactiveOmit } from '@vueuse/core'
import { NavigationMenuIndicator, useForwardProps } from 'reka-ui'
import { cn } from '../../../lib/utils'

/**
 * Pro — the small arrow/underline that glides to the active trigger, bridging
 * the bar and the viewport. Slides on the horizontal axis via reka's measured
 * position/size variables; a brand-tinted diamond points at the open panel.
 */
const props = defineProps<NavigationMenuIndicatorProps & { class?: HTMLAttributes['class'] }>()
const forwarded = useForwardProps(reactiveOmit(props, 'class'))
</script>

<template>
  <NavigationMenuIndicator
    data-slot="navigation-menu-pro-indicator"
    v-bind="forwarded"
    :class="
      cn(
        'ds-nav-indicator data-[state=visible]:animate-in data-[state=hidden]:animate-out data-[state=hidden]:fade-out data-[state=visible]:fade-in absolute top-full left-0 z-[1] flex h-2 items-end justify-center overflow-hidden',
        props.class,
      )
    "
  >
    <span class="ds-nav-indicator-diamond" />
  </NavigationMenuIndicator>
</template>

<style scoped>
.ds-nav-indicator {
  width: var(--reka-navigation-menu-indicator-size);
  transform: translateX(var(--reka-navigation-menu-indicator-position));
  transition:
    transform 0.25s var(--ease-out-quart, ease-out),
    width 0.25s var(--ease-out-quart, ease-out);
}
.ds-nav-indicator-diamond {
  position: relative;
  top: 70%;
  height: 0.5rem;
  width: 0.5rem;
  rotate: 45deg;
  border-radius: 2px;
  background: var(--popover);
  box-shadow:
    -1px -1px 0 0 color-mix(in oklch, var(--border) 80%, transparent),
    inset 0 0 0 1px color-mix(in oklch, var(--primary) 30%, transparent);
}
@media (prefers-reduced-motion: reduce) {
  .ds-nav-indicator {
    transition: none;
  }
}
</style>
