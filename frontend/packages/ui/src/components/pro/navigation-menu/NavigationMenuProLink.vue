<script setup lang="ts">
import type { NavigationMenuLinkEmits, NavigationMenuLinkProps } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import { reactiveOmit } from '@vueuse/core'
import { NavigationMenuLink, useForwardPropsEmits } from 'reka-ui'
import { cn } from '../../../lib/utils'

/**
 * Pro — navigation link. Two roles: as a top-level bar item (pass `bar`) it
 * mirrors a trigger's height/shape; inside a content panel it renders a richer
 * block with an optional title + description slot. Brand-tinted active state.
 */
const props = withDefaults(
  defineProps<NavigationMenuLinkProps & { class?: HTMLAttributes['class']; bar?: boolean }>(),
  { bar: false },
)
const emits = defineEmits<NavigationMenuLinkEmits>()
const forwarded = useForwardPropsEmits(reactiveOmit(props, 'class', 'bar'), emits)
</script>

<template>
  <NavigationMenuLink
    data-slot="navigation-menu-pro-link"
    v-bind="forwarded"
    :class="
      cn(
        'ds-nav-link block outline-none select-none focus-visible:ring-2 focus-visible:ring-ring/50',
        bar
          ? 'inline-flex h-9 w-max items-center rounded-lg px-3 text-sm font-medium text-muted-foreground hover:bg-accent hover:text-foreground data-[active]:text-foreground'
          : 'rounded-md p-2.5 hover:bg-accent data-[active]:bg-accent/70',
        '[&_svg:not([class*=size-])]:size-4 [&_svg]:shrink-0',
        props.class,
      )
    "
  >
    <slot />
  </NavigationMenuLink>
</template>

<style scoped>
@layer nv-components {
  .ds-nav-link {
    transition:
      color 0.16s var(--nv-ease-out-quart, ease-out),
      background-color 0.16s var(--nv-ease-out-quart, ease-out);
  }
  @media (prefers-reduced-motion: reduce) {
    .ds-nav-link {
      transition: none;
    }
  }
}
</style>
