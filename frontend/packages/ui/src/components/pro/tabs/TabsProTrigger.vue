<script setup lang="ts">
import type { TabsTriggerProps } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import { reactiveOmit } from '@vueuse/core'
import { TabsTrigger, useForwardProps } from 'reka-ui'
import { cn } from '../../../lib/utils'

const props = defineProps<TabsTriggerProps & { class?: HTMLAttributes['class'] }>()
const delegated = reactiveOmit(props, 'class')
const forwarded = useForwardProps(delegated)
</script>

<template>
  <TabsTrigger
    data-slot="tabs-pro-trigger"
    v-bind="forwarded"
    :class="
      cn(
        'ds-stab relative z-[1] inline-flex h-full flex-1 items-center justify-center gap-1.5 rounded-md px-3 text-sm font-medium whitespace-nowrap text-muted-foreground transition-colors outline-none select-none hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring/50 disabled:pointer-events-none disabled:opacity-50 data-[state=active]:text-foreground [&_svg:not([class*=size-])]:size-4 [&_svg]:shrink-0',
        props.class,
      )
    "
  >
    <slot />
  </TabsTrigger>
</template>

<style scoped>
@layer nv-components {
  /* The active pill is the sliding TabsIndicator (in TabsProList); the trigger
   only animates its text colour. */
  .ds-stab {
    transition: color 0.18s var(--nv-ease-out-quart, ease-out);
  }
}
</style>
