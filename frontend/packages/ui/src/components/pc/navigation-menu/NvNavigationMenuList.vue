<script setup lang="ts">
import type { NavigationMenuListProps } from 'reka-ui'
import type { HTMLAttributes } from 'vue'
import { reactiveOmit } from '@vueuse/core'
import { NavigationMenuList, useForwardProps } from 'reka-ui'
import { cn } from '../../../lib/utils'

const props = defineProps<NavigationMenuListProps & { class?: HTMLAttributes['class'] }>()
const forwarded = useForwardProps(reactiveOmit(props, 'class'))
</script>

<template>
  <NavigationMenuList
    data-slot="nv-navigation-menu-list"
    v-bind="forwarded"
    :class="cn('group flex flex-1 list-none items-center justify-center gap-1', props.class)"
  >
    <slot />
  </NavigationMenuList>
</template>
