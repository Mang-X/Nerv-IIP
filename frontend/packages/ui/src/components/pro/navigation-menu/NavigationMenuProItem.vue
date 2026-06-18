<script setup lang="ts">
import type { NavigationMenuItemProps } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import { reactiveOmit } from '@vueuse/core'
import { NavigationMenuItem, useForwardProps } from 'reka-ui'
import { cn } from '../../../lib/utils'

const props = defineProps<NavigationMenuItemProps & { class?: HTMLAttributes['class'] }>()
const forwarded = useForwardProps(reactiveOmit(props, 'class'))
</script>

<template>
  <NavigationMenuItem
    data-slot="navigation-menu-pro-item"
    v-bind="forwarded"
    :class="cn(props.class)"
  >
    <slot />
  </NavigationMenuItem>
</template>
