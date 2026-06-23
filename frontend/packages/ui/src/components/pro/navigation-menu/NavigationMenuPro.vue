<script setup lang="ts">
import type { NavigationMenuRootEmits, NavigationMenuRootProps } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import { reactiveOmit } from '@vueuse/core'
import { NavigationMenuRoot, useForwardPropsEmits } from 'reka-ui'
import { cn } from '../../../lib/utils'
import NavigationMenuProViewport from './NavigationMenuProViewport.vue'

/**
 * Pro — navigation menu root: a horizontal control-plane menu bar whose
 * triggers open an animated mega-menu panel below. Rebuilt on reka primitives,
 * styled with our tokens; never edits原版 shadcn. The viewport (rendered here by
 * default) escapes clipping and animates its size/position to follow the open
 * trigger.
 */
const props = withDefaults(
  defineProps<
    NavigationMenuRootProps & {
      class?: HTMLAttributes['class']
      /** Render the built-in animated viewport under the bar. Default true. */
      viewport?: boolean
    }
  >(),
  { viewport: true },
)
const emits = defineEmits<NavigationMenuRootEmits>()
const forwarded = useForwardPropsEmits(reactiveOmit(props, 'class', 'viewport'), emits)
</script>

<template>
  <NavigationMenuRoot
    data-slot="navigation-menu-pro"
    v-bind="forwarded"
    :class="cn('relative flex max-w-max flex-1 items-center justify-center', props.class)"
  >
    <slot />
    <NavigationMenuProViewport v-if="props.viewport" />
  </NavigationMenuRoot>
</template>
