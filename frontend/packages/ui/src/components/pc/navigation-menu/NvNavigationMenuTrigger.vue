<script setup lang="ts">
import type { NavigationMenuTriggerProps } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import { reactiveOmit } from '@vueuse/core'
import { ChevronDownIcon } from '@lucide/vue'
import { NavigationMenuTrigger, useForwardProps } from 'reka-ui'
import { cn } from '../../../lib/utils'

/**
 * Pro — navigation trigger: a bar button that toggles a mega-menu panel.
 * Brand-tinted active state, hairline hover surface, and a chevron that
 * rotates 180° while the panel is open. Set `chevron={false}` to hide it.
 */
const props = withDefaults(
  defineProps<
    NavigationMenuTriggerProps & { class?: HTMLAttributes['class']; chevron?: boolean }
  >(),
  { chevron: true },
)
const forwarded = useForwardProps(reactiveOmit(props, 'class', 'chevron'))
</script>

<template>
  <NavigationMenuTrigger
    data-slot="nv-navigation-menu-trigger"
    v-bind="forwarded"
    :class="
      cn(
        'nv-nav-trigger group/trigger inline-flex h-9 w-max items-center justify-center gap-1 rounded-lg px-3 text-sm font-medium whitespace-nowrap text-muted-foreground outline-none select-none hover:bg-accent hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring/50 disabled:pointer-events-none disabled:opacity-50 data-[state=open]:bg-accent data-[state=open]:text-foreground [&_svg:not([class*=size-])]:size-4 [&_svg]:shrink-0',
        props.class,
      )
    "
  >
    <slot />
    <ChevronDownIcon
      v-if="props.chevron"
      class="nv-nav-chevron relative top-px text-muted-foreground/80 group-data-[state=open]/trigger:-rotate-180 group-data-[state=open]/trigger:text-foreground"
      aria-hidden="true"
    />
  </NavigationMenuTrigger>
</template>

<style scoped>
@layer nv-components {
  .nv-nav-trigger {
    transition:
      color 0.18s var(--nv-ease-out-quart, ease-out),
      background-color 0.18s var(--nv-ease-out-quart, ease-out);
  }
  .nv-nav-chevron {
    /* Tailwind v4 `rotate-180` animates the `rotate` property (not `transform`),
     so transition `rotate`. An explicit `0deg` base is required: the default
     `rotate: none` is a keyword that won't interpolate to `180deg` (it would
     jump), whereas `0deg → 180deg` animates. */
    rotate: 0deg;
    transition:
      rotate 0.25s var(--nv-ease-out-expo, ease-out),
      color 0.18s var(--nv-ease-out-quart, ease-out);
  }
  @media (prefers-reduced-motion: reduce) {
    .nv-nav-trigger,
    .nv-nav-chevron {
      transition: none;
    }
  }
}
</style>
