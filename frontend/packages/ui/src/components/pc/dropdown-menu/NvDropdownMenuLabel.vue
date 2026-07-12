<script setup lang="ts">
import type { DropdownMenuLabelProps } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import { reactiveOmit } from '@vueuse/core'
import { DropdownMenuLabel, useForwardProps } from 'reka-ui'
import { cn } from '../../../lib/utils'

/**
 * Pro — dropdown menu label. Muted section heading; base inset semantics kept.
 */
const props = defineProps<
  DropdownMenuLabelProps & { class?: HTMLAttributes['class']; inset?: boolean }
>()

const delegatedProps = reactiveOmit(props, 'class', 'inset')
const forwardedProps = useForwardProps(delegatedProps)
</script>

<template>
  <DropdownMenuLabel
    data-slot="nv-dropdown-menu-label"
    :data-inset="inset ? '' : undefined"
    v-bind="forwardedProps"
    :class="
      cn('text-muted-foreground px-2.5 py-1 text-xs font-medium data-inset:pl-7', props.class)
    "
  >
    <slot />
  </DropdownMenuLabel>
</template>
