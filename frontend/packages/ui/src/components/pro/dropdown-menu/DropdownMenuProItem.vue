<script setup lang="ts">
import type { DropdownMenuItemProps } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import { reactiveOmit } from '@vueuse/core'
import { DropdownMenuItem, useForwardProps } from 'reka-ui'
import { cn } from '../../../lib/utils'

/**
 * Pro — dropdown menu item. Keeps原版 inset / variant / disabled semantics;
 * focus highlight uses the Pro accent surface with calibrated color motion.
 */
const props = withDefaults(
  defineProps<
    DropdownMenuItemProps & {
      class?: HTMLAttributes['class']
      inset?: boolean
      variant?: 'default' | 'destructive'
    }
  >(),
  {
    variant: 'default',
  },
)

const delegatedProps = reactiveOmit(props, 'inset', 'variant', 'class')

const forwardedProps = useForwardProps(delegatedProps)
</script>

<template>
  <DropdownMenuItem
    data-slot="dropdown-menu-pro-item"
    :data-inset="inset ? '' : undefined"
    :data-variant="variant"
    v-bind="forwardedProps"
    :class="
      cn(
        'group/dropdown-menu-pro-item relative flex h-8 cursor-pointer items-center gap-2 rounded-md px-2.5 py-1 text-sm outline-hidden transition-colors select-none focus:bg-accent focus:text-accent-foreground not-data-[variant=destructive]:focus:**:text-accent-foreground data-inset:pl-7 data-[variant=destructive]:text-destructive data-[variant=destructive]:focus:bg-destructive/10 dark:data-[variant=destructive]:focus:bg-destructive/20 data-[variant=destructive]:focus:text-destructive data-[variant=destructive]:*:[svg]:text-destructive data-disabled:pointer-events-none data-disabled:opacity-50 [&_svg:not([class*=size-])]:size-4 [&_svg]:pointer-events-none [&_svg]:shrink-0',
        props.class,
      )
    "
  >
    <slot />
  </DropdownMenuItem>
</template>
